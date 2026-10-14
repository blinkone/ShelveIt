using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ShelveIt.Logic;

using Colossal.UI.Binding;

using Game.UI;

using Newtonsoft.Json;

using UnityEngine;

namespace ShelveIt.Systems
{
	// Loads newly downloaded asset packages without a restart, through the game's own path:
	// enable the package in the active playset, then ParadoxModsDataSource.Populate(), which makes
	// GameManager add the prefabs. Nothing runs per frame unless a package is being followed.
	public partial class PackageLoaderSystem : UISystemBase
	{
		private const string kGroup = ShelveItUISystem.kGroup;

		public const string kDownloading = "downloading";
		public const string kLoading = "loading";
		public const string kLoaded = "loaded";
		public const string kFailed = "failed";

		// Seconds to wait for the SDK to load an enabled package by itself before Populate().
		private const float kLoadGrace = 4f;
		// A download folder that has not changed for this long counts as downloaded.
		private const float kFolderSettle = 15f;
		private const float kPollInterval = 1f;

		private readonly ConcurrentQueue<Action> m_MainThread = new ConcurrentQueue<Action>();

		private AssetScanSystem m_Scan;
		private RawValueBinding m_Binding;
		private Action m_Unhook;
		private bool m_HookTried;

		// Per package: state and the time it entered it. Only packages this session follows.
		private readonly Dictionary<string, string> m_Status = new Dictionary<string, string>();
		private readonly Dictionary<string, float> m_Since = new Dictionary<string, float>();
		private readonly HashSet<string> m_Populated = new HashSet<string>();
		private bool m_Populating;
		private float m_NextPoll;

		// Packages subscribed through Shelve It and not loaded yet, kept across sessions.
		private HashSet<string> m_Subscribed;

		// Pending packages found by the last check, and the main-menu load action.
		private List<string> m_Pending = new List<string>();
		private readonly HashSet<string> m_NeedRestart = new HashSet<string>();
		private bool m_Checking;
		private bool m_CheckAgain;
		private string m_LoadState = "";
		private List<string> m_Batch = new List<string>();

		protected override void OnCreate()
		{
			base.OnCreate();

			m_Scan = World.GetOrCreateSystemManaged<AssetScanSystem>();

			AddBinding(m_Binding = new RawValueBinding(kGroup, "PackageLoader", Write));
			AddBinding(new TriggerBinding(kGroup, "CheckPendingPackages", CheckPending));
			AddBinding(new TriggerBinding(kGroup, "LoadPendingPackages", LoadPending));
			AddBinding(new TriggerBinding(kGroup, "DismissPackageRestart", DismissRestart));
		}

		protected override void OnDestroy()
		{
			m_Unhook?.Invoke();
			m_Unhook = null;
			base.OnDestroy();
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();

			while (m_MainThread.TryDequeue(out var action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					Mod.Log.Error("Package loader: " + ex);
				}
			}

			if (!IsFollowing() || UnityEngine.Time.realtimeSinceStartup < m_NextPoll)
			{
				return;
			}

			m_NextPoll = UnityEngine.Time.realtimeSinceStartup + kPollInterval;

			try
			{
				Poll();
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Package loader: " + ex);
			}
		}

		private void Post(Action action) => m_MainThread.Enqueue(action);

		private bool IsFollowing()
		{
			foreach (var state in m_Status.Values)
			{
				if (state == kDownloading || state == kLoading)
				{
					return true;
				}
			}

			return false;
		}

		private bool EnsureHooked()
		{
			if (m_Unhook != null)
			{
				return true;
			}

			if (m_HookTried && !PdxBridge.CanLoadPackages)
			{
				return false;
			}

			m_HookTried = true;
			m_Unhook = PdxBridge.Hook(
				id => Post(() => OnDownloaded(id)),
				id => Post(() => OnDownloadFailed(id)),
				() => Post(OnModsChanged));

			return m_Unhook != null;
		}

		#region Subscribe flow

		// Called after PdxSdkPlatform.SubscribeAll succeeded. The SDK enables before it subscribes,
		// so new packages can end up subscribed but not enabled: enable each one again here, and
		// once more when its download completes.
		public void TrackSubscribed(IEnumerable<int> packageIds)
		{
			if (!EnsureHooked())
			{
				Mod.Log.Info("Runtime loading unavailable: the new packages load after a restart");
				return;
			}

			var subscribed = Subscribed;

			foreach (var number in packageIds)
			{
				var id = number.ToString();

				if (PdxBridge.IsLoaded(id))
				{
					SetStatus(id, kLoaded);
					continue;
				}

				subscribed.Add(id);
				SetStatus(id, kDownloading);
				Mod.Log.Info($"Package {id}: subscribed, waiting for the download");

				PdxBridge.EnableInPlayset(id, ok => Mod.Log.Info($"Package {id}: enable in playset after subscribe: {(ok ? "ok" : "not yet")}"));
			}

			SaveSubscribed();
			m_Binding.Update();
		}

		private void OnDownloaded(string id)
		{
			if (!m_Status.TryGetValue(id, out var state) || state != kDownloading)
			{
				// Not followed by Shelve It: the game handles it, the next check counts it.
				Mod.Log.Info($"Package {id}: download completed (not followed)");
				return;
			}

			Mod.Log.Info($"Package {id}: download completed");
			StartLoading(id);
		}

		private void OnDownloadFailed(string id)
		{
			if (m_Status.ContainsKey(id))
			{
				Mod.Log.Warn($"Package {id}: download failed");
				SetStatus(id, kFailed);
				m_Binding.Update();
			}
		}

		private void StartLoading(string id)
		{
			if (PdxBridge.IsLoaded(id))
			{
				MarkLoaded(id);
				return;
			}

			SetStatus(id, kLoading);
			m_Binding.Update();

			PdxBridge.EnableInPlayset(id, ok => Post(() =>
			{
				Mod.Log.Info($"Package {id}: enable in playset: {(ok ? "ok" : "failed")}");

				if (!ok && m_Status.TryGetValue(id, out var current) && current == kLoading)
				{
					// Populate() still runs: the package may already be enabled.
					m_Since[id] = UnityEngine.Time.realtimeSinceStartup - kLoadGrace;
				}
			}));
		}

		private void Poll()
		{
			var now = UnityEngine.Time.realtimeSinceStartup;
			var needPopulate = false;

			foreach (var id in m_Status.Keys.ToList())
			{
				var state = m_Status[id];

				if (state != kDownloading && state != kLoading)
				{
					continue;
				}

				if (PdxBridge.IsLoaded(id))
				{
					MarkLoaded(id);
					continue;
				}

				if (state == kDownloading)
				{
					// No download event (the files were already there, or the event was missed):
					// a download folder that stopped changing counts as downloaded.
					var folder = PdxBridge.FindDownloadFolder(id);

					if (folder != null && (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder)).TotalSeconds > kFolderSettle
						&& now - m_Since[id] > 2f)
					{
						Mod.Log.Info($"Package {id}: download folder found, loading");
						StartLoading(id);
					}

					continue;
				}

				if (now - m_Since[id] >= kLoadGrace && !m_Populated.Contains(id))
				{
					needPopulate = true;
				}
			}

			if (needPopulate && !m_Populating)
			{
				RunPopulate();
			}

			if (!IsFollowing())
			{
				FinishLoadAction();
			}
		}

		private void RunPopulate()
		{
			var now = UnityEngine.Time.realtimeSinceStartup;
			var batch = m_Status.Where(x => x.Value == kLoading && now - m_Since[x.Key] >= kLoadGrace).Select(x => x.Key).ToList();

			m_Populating = true;
			Mod.Log.Info($"Populate() for {batch.Count} packages: {string.Join(", ", batch)}");

			PdxBridge.Populate(ok => Post(() =>
			{
				m_Populating = false;

				foreach (var id in batch)
				{
					m_Populated.Add(id);

					if (PdxBridge.IsLoaded(id))
					{
						MarkLoaded(id);
					}
					else if (m_Status.TryGetValue(id, out var state) && state == kLoading)
					{
						Mod.Log.Warn($"Package {id}: not loaded after Populate() ({(ok ? "completed" : "failed")}), needs a restart");
						SetStatus(id, kFailed);
						m_NeedRestart.Add(id);
					}
				}

				m_Binding.Update();

				if (!IsFollowing())
				{
					FinishLoadAction();
				}
			}));
		}

		private void MarkLoaded(string id)
		{
			Mod.Log.Info($"Package {id}: loaded");
			SetStatus(id, kLoaded);
			m_Pending.Remove(id);

			if (Subscribed.Remove(id))
			{
				SaveSubscribed();
			}

			m_Binding.Update();
		}

		private void SetStatus(string id, string state)
		{
			m_Status[id] = state;
			m_Since[id] = UnityEngine.Time.realtimeSinceStartup;
		}

		// New prefabs were added (or removed): the asset list is rescanned the next time the panel
		// opens. Find It keeps its own index and is not refreshed by Shelve It.
		private void OnModsChanged()
		{
			Mod.Log.Info($"PDX mods changed ({PdxBridge.LoadedModCount} loaded): asset scan marked stale");
			m_Scan.MarkStale();

			if (m_LoadState != kLoading && !IsFollowing())
			{
				m_CheckAgain = true;
				CheckPending();
			}
		}

		#endregion

		#region Pending packages and the main-menu action

		// Packages that are downloaded but not loaded, and not code mods: mods enabled in the active
		// playset, plus packages subscribed through Shelve It that have a download folder. Runs on
		// demand (main menu shown, panel opened), never per frame.
		public void CheckPending()
		{
			if (m_Checking)
			{
				m_CheckAgain = true;
				return;
			}

			if (!EnsureHooked())
			{
				SetPending(new List<string>());
				return;
			}

			m_Checking = true;
			m_CheckAgain = false;

			PdxBridge.GetPlaysetMods(playset => Post(() =>
			{
				m_Checking = false;

				if (playset == null)
				{
					SetPending(new List<string>());
					return;
				}

				var candidates = new Dictionary<string, string>(playset);

				foreach (var id in Subscribed)
				{
					if (!candidates.ContainsKey(id))
					{
						var folder = PdxBridge.FindDownloadFolder(id);

						if (folder != null)
						{
							candidates[id] = folder;
						}
					}
				}

				// While the game is still loading the playset nearly everything looks unloaded.
				if (PdxBridge.LoadedModCount == 0 && playset.Count > 0)
				{
					Mod.Log.Info("Pending check skipped: the playset is not loaded yet");
					SetPending(new List<string>());
					return;
				}

				var pending = new List<string>();

				foreach (var pair in candidates)
				{
					if (PdxBridge.IsLoaded(pair.Key) || m_NeedRestart.Contains(pair.Key)
						|| (m_Status.TryGetValue(pair.Key, out var state) && (state == kDownloading || state == kLoading)))
					{
						continue;
					}

					var folder = string.IsNullOrEmpty(pair.Value) ? PdxBridge.FindDownloadFolder(pair.Key) : pair.Value;

					if (PdxBridge.IsCodeMod(folder))
					{
						continue;
					}

					pending.Add(pair.Key);
				}

				Mod.Log.Info($"Pending packages: {pending.Count}" + (pending.Count > 0 ? " (" + string.Join(", ", pending) + ")" : "")
					+ $"; playset {playset.Count}, loaded {PdxBridge.LoadedModCount}");
				SetPending(pending);

				if (m_CheckAgain)
				{
					m_CheckAgain = false;
					CheckPending();
				}
			}));
		}

		private void SetPending(List<string> pending)
		{
			m_Pending = pending;
			m_Binding.Update();
		}

		private void LoadPending()
		{
			if (m_LoadState == kLoading || m_Pending.Count == 0 || !EnsureHooked())
			{
				return;
			}

			m_Batch = m_Pending.ToList();
			m_LoadState = kLoading;
			m_NeedRestart.Clear();
			Mod.Log.Info($"Load new asset packages: {m_Batch.Count} ({string.Join(", ", m_Batch)})");

			foreach (var id in m_Batch)
			{
				m_Populated.Remove(id);
				StartLoading(id);
			}

			m_Binding.Update();
		}

		private void FinishLoadAction()
		{
			if (m_LoadState != kLoading)
			{
				return;
			}

			var failed = m_Batch.Count(x => m_Status.TryGetValue(x, out var state) && state == kFailed);

			m_LoadState = failed > 0 ? "restart" : "done";
			Mod.Log.Info($"Load new asset packages finished: {m_Batch.Count - failed} loaded, {failed} need a restart");
			m_Binding.Update();

			CheckPending();
		}

		private void DismissRestart()
		{
			m_LoadState = "";
			m_Binding.Update();
		}

		#endregion

		#region Persistence

		private HashSet<string> Subscribed
		{
			get
			{
				if (m_Subscribed != null)
				{
					return m_Subscribed;
				}

				m_Subscribed = new HashSet<string>();

				try
				{
					if (File.Exists(Paths.Subscribed))
					{
						var ids = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Paths.Subscribed));

						if (ids != null)
						{
							m_Subscribed.UnionWith(ids.Where(x => !string.IsNullOrEmpty(x)));
						}
					}
				}
				catch (Exception ex)
				{
					Mod.Log.Warn("Could not read subscribed.json: " + ex.Message);
				}

				return m_Subscribed;
			}
		}

		private void SaveSubscribed()
		{
			try
			{
				Paths.WriteAtomic(Paths.Subscribed, JsonConvert.SerializeObject(Subscribed.OrderBy(x => x).ToList()));
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not write subscribed.json: " + ex.Message);
			}
		}

		#endregion

		private void Write(IJsonWriter writer)
		{
			writer.TypeBegin("ShelveIt.PackageLoader");
			writer.PropertyName("available");
			writer.Write(m_Unhook != null);
			writer.PropertyName("pending");
			writer.Write(m_Pending.Count);
			writer.PropertyName("state");
			writer.Write(m_LoadState);
			writer.PropertyName("restart");
			writer.Write(m_NeedRestart.Count);
			writer.PropertyName("items");
			writer.ArrayBegin((uint)m_Status.Count);

			foreach (var pair in m_Status)
			{
				writer.TypeBegin("ShelveIt.PackageStatus");
				writer.PropertyName("id");
				writer.Write(int.TryParse(pair.Key, out var number) ? number : 0);
				writer.PropertyName("state");
				writer.Write(pair.Value);
				writer.TypeEnd();
			}

			writer.ArrayEnd();
			writer.TypeEnd();
		}
	}
}
