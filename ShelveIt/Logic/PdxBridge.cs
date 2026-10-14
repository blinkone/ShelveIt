using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using Colossal.PSI.PdxSdk;

namespace ShelveIt.Logic
{
	// Paradox account and PDX Mods, reached through the game's own platform service the way
	// Game.UI.Menu.ParadoxBindings does. Every call is guarded: without the service the community
	// features that need it are hidden and the rest keeps working.
	public static class PdxBridge
	{
		private static bool s_Warned;

		private static PdxSdkPlatform Platform
		{
			get
			{
				try
				{
					return PlatformManager.instance?.GetPSI<PdxSdkPlatform>("PdxSdk");
				}
				catch (Exception ex)
				{
					WarnOnce("PDX platform not reachable: " + ex.Message);
					return null;
				}
			}
		}

		public static bool Available => Platform != null;

		public static bool LoggedIn
		{
			get
			{
				try
				{
					return Platform?.cachedLoggedIn ?? false;
				}
				catch (Exception ex)
				{
					WarnOnce("PDX login state not readable: " + ex.Message);
					return false;
				}
			}
		}

		// The Paradox display name, or null when it cannot be read. done runs on a worker thread.
		public static void GetDisplayName(Action<string> done)
		{
			var platform = Platform;

			if (platform == null || !LoggedIn)
			{
				done(null);
				return;
			}

			try
			{
				platform.GetSocialProfile().ContinueWith(task =>
				{
					string name = null;

					try
					{
						if (task.Status == TaskStatus.RanToCompletion)
						{
							name = task.Result.m_Name;
						}
						else if (task.Exception != null)
						{
							WarnOnce("PDX profile not readable: " + task.Exception.GetBaseException().Message);
						}
					}
					catch (Exception ex)
					{
						WarnOnce("PDX profile not readable: " + ex.Message);
					}

					done(string.IsNullOrWhiteSpace(name) ? null : name.Trim());
				});
			}
			catch (Exception ex)
			{
				WarnOnce("PDX profile not readable: " + ex.Message);
				done(null);
			}
		}

		public static bool ShowModPage(int packageId)
		{
			try
			{
				var platform = Platform;

				if (platform == null || packageId <= 0)
				{
					return false;
				}

				platform.ShowModDetail(packageId.ToString());
				return true;
			}
			catch (Exception ex)
			{
				WarnOnce("PDX mod page not available: " + ex.Message);
				return false;
			}
		}

		// done(ok, message) runs on a worker thread.
		public static void SubscribeAll(IEnumerable<int> packageIds, Action<bool, string> done)
		{
			var platform = Platform;
			var ids = new List<string>();

			foreach (var id in packageIds)
			{
				if (id > 0)
				{
					ids.Add(id.ToString());
				}
			}

			if (platform == null || ids.Count == 0)
			{
				done(false, "unavailable");
				return;
			}

			try
			{
				platform.SubscribeAll(ids).ContinueWith(task =>
				{
					if (task.Status == TaskStatus.RanToCompletion)
					{
						var report = task.Result;

						Mod.Log.Info($"Subscribed to {ids.Count} packages" + (report?.message != null ? ": " + report.message : ""));
						done(true, report?.message ?? "");
					}
					else
					{
						var message = task.Exception?.GetBaseException().Message ?? "cancelled";

						Mod.Log.Warn("Subscribing failed: " + message);
						done(false, message);
					}
				});
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Subscribing failed: " + ex.Message);
				done(false, ex.Message);
			}
		}

		#region Runtime loading of asset packages

		// The game's own loader for PDX Mods content. Populate() loads the mods of the active
		// playset that are not loaded yet; GameManager then adds their prefabs.
		private static ParadoxModsDataSource ModsSource
		{
			get
			{
				try
				{
					return AssetDatabase<ParadoxMods>.instance?.dataSource as ParadoxModsDataSource;
				}
				catch (Exception ex)
				{
					WarnOnce("PDX mods data source not reachable: " + ex.Message);
					return null;
				}
			}
		}

		public static bool CanLoadPackages => Platform != null && ModsSource != null;

		// Mods whose content the game has loaded in this session.
		public static int LoadedModCount
		{
			get
			{
				try
				{
					return ModsSource?.GetActiveMods().Count ?? 0;
				}
				catch (Exception ex)
				{
					WarnOnce("PDX loaded mods not readable: " + ex.Message);
					return 0;
				}
			}
		}

		public static bool IsLoaded(string packageId)
		{
			try
			{
				return ModsSource?.ContainsActiveMod(packageId) ?? false;
			}
			catch (Exception ex)
			{
				WarnOnce("PDX loaded mods not readable: " + ex.Message);
				return false;
			}
		}

		// Hooks the download events of the platform and the "mods changed" event of the data source.
		// The handlers may run on any thread. Returns the unhook action, or null when unavailable.
		public static Action Hook(Action<string> downloaded, Action<string> downloadFailed, Action modsChanged)
		{
			var platform = Platform;
			var source = ModsSource;

			if (platform == null || source == null)
			{
				return null;
			}

			try
			{
				ModEventHandler completed = (psi, mod) => downloaded(mod.id);
				ModEventHandler failed = (psi, mod) => downloadFailed(mod.id);
				Action changed = () => modsChanged();

				platform.onModDownloadCompleted += completed;
				platform.onModDownloadFailed += failed;
				source.onAfterActivePlaysetOrModStatusChanged += changed;

				return () =>
				{
					try
					{
						platform.onModDownloadCompleted -= completed;
						platform.onModDownloadFailed -= failed;
						source.onAfterActivePlaysetOrModStatusChanged -= changed;
					}
					catch (Exception ex)
					{
						Mod.Log.Warn("PDX events not released: " + ex.Message);
					}
				};
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("PDX events not available: " + ex.Message);
				return null;
			}
		}

		// done(ok) runs on a worker thread.
		public static void EnableInPlayset(string packageId, Action<bool> done)
		{
			var platform = Platform;

			if (platform == null)
			{
				done(false);
				return;
			}

			try
			{
				platform.EnableModInActivePlayset(packageId, true).ContinueWith(task =>
				{
					if (task.Status != TaskStatus.RanToCompletion)
					{
						Mod.Log.Warn($"Enabling {packageId} in the playset failed: " + (task.Exception?.GetBaseException().Message ?? "cancelled"));
					}

					done(task.Status == TaskStatus.RanToCompletion && task.Result);
				});
			}
			catch (Exception ex)
			{
				Mod.Log.Warn($"Enabling {packageId} in the playset failed: " + ex.Message);
				done(false);
			}
		}

		// Ids and folders of the mods enabled in the active playset that are downloaded. null when
		// the SDK cannot answer. done runs on a worker thread.
		public static void GetPlaysetMods(Action<Dictionary<string, string>> done)
		{
			var platform = Platform;

			if (platform == null)
			{
				done(null);
				return;
			}

			try
			{
				platform.GetModsInActivePlayset().ContinueWith(task =>
				{
					Dictionary<string, string> result = null;

					try
					{
						if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
						{
							result = new Dictionary<string, string>();

							foreach (var mod in task.Result)
							{
								if (!string.IsNullOrEmpty(mod.id))
								{
									result[mod.id] = mod.path ?? "";
								}
							}
						}
						else if (task.Exception != null)
						{
							Mod.Log.Warn("PDX playset not readable: " + task.Exception.GetBaseException().Message);
						}
					}
					catch (Exception ex)
					{
						Mod.Log.Warn("PDX playset not readable: " + ex.Message);
						result = null;
					}

					done(result);
				});
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("PDX playset not readable: " + ex.Message);
				done(null);
			}
		}

		// Runs the game's load path for playset mods that are not loaded yet. done(ok) runs on a
		// worker thread once the game has added their prefabs.
		public static void Populate(Action<bool> done)
		{
			var source = ModsSource;

			if (source == null)
			{
				done(false);
				return;
			}

			try
			{
				Task.Run(() => source.Populate()).ContinueWith(task =>
				{
					if (task.Status != TaskStatus.RanToCompletion)
					{
						Mod.Log.Warn("PDX Populate failed: " + (task.Exception?.GetBaseException().Message ?? "cancelled"));
					}

					done(task.Status == TaskStatus.RanToCompletion);
				});
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("PDX Populate failed: " + ex.Message);
				done(false);
			}
		}

		// The newest download folder of a subscribed package (<id>_<version>), or null.
		public static string FindDownloadFolder(string packageId)
		{
			try
			{
				var root = Path.Combine(Paths.ModsCache, "mods_subscribed");

				if (!Directory.Exists(root))
				{
					return null;
				}

				return new DirectoryInfo(root).GetDirectories(packageId + "_*")
					.OrderByDescending(x => x.LastWriteTimeUtc)
					.Select(x => x.FullName)
					.FirstOrDefault();
			}
			catch (Exception ex)
			{
				WarnOnce("Mods folder not readable: " + ex.Message);
				return null;
			}
		}

		// Code mods need a restart by design, so they are never offered for runtime loading.
		public static bool IsCodeMod(string folder)
		{
			try
			{
				return !string.IsNullOrEmpty(folder) && Directory.Exists(folder)
					&& Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Any();
			}
			catch
			{
				return false;
			}
		}

		#endregion

		private static void WarnOnce(string message)
		{
			if (!s_Warned)
			{
				s_Warned = true;
				Mod.Log.Warn(message);
			}
		}
	}
}
