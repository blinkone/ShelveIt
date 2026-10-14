using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using ShelveIt.Domain;
using ShelveIt.Logic;

using Colossal.UI.Binding;

using Game.UI;

using UnityEngine;

namespace ShelveIt.Systems
{
	// Community sharing: server list and detail, publishing, reports and share codes. The server is
	// contacted only from the triggers below, never at startup or in the background.
	public class CommunityUISystem : UISystemBase
	{
		private const string kGroup = ShelveItUISystem.kGroup;
		private const int kListDescription = 160;

		private static readonly string[] kSorts = { "popular", "downloads", "newest" };
		private static readonly string[] kReasons = { "spam", "offensive", "broken", "other" };

		private AssetScanSystem m_Scan;
		private CommunityClient m_Client;
		private readonly PublishedStore m_Published = new PublishedStore();

		// Callbacks of Paradox SDK tasks, run on the main thread by OnUpdate.
		private readonly ConcurrentQueue<Action> m_MainThread = new ConcurrentQueue<Action>();

		private RawValueBinding m_ListBinding;
		private RawValueBinding m_DetailBinding;
		private RawValueBinding m_ActionBinding;
		private RawValueBinding m_PdxBinding;
		private RawValueBinding m_PublishedBinding;
		private RawValueBinding m_ClipboardBinding;

		// List
		private string m_Server = "unknown";
		private string m_ListState = "idle";
		private string m_ListError = "";
		private string m_ListMessage = "";
		private string m_Query = "";
		private string m_Sort = "popular";
		private int m_PageIndex;
		private int m_PageSize = 20;
		private int m_Total;
		private List<CommunitySummary> m_Items = new List<CommunitySummary>();
		private int m_ListSeq;

		// Detail
		private string m_DetailState = "none";
		private string m_DetailError = "";
		private string m_DetailMessage = "";
		private string m_DetailSource = "";
		private CommunitySummary m_DetailSummary;
		private CommunityPayload m_DetailPayload;
		private PayloadBuilder.Analysis m_Analysis;
		private string m_ImportedId = "";
		private bool m_Reported;
		private int m_DetailSeq;

		// Last operation
		private string m_ActionKind = "";
		private string m_ActionState = "";
		private string m_ActionError = "";
		private string m_ActionMessage = "";
		private string m_ActionLibrary = "";
		private int m_ActionCount;

		// Paradox account
		private bool m_PdxAvailable;
		private bool m_PdxLoggedIn;
		private string m_PdxName = "";

		private int m_ClipboardSeq;
		private string m_ClipboardText = "";

		protected override void OnCreate()
		{
			base.OnCreate();

			m_Scan = World.GetOrCreateSystemManaged<AssetScanSystem>();
			m_Client = new CommunityClient(() => Mod.Settings?.ServerUrl);

			AddBinding(m_ListBinding = new RawValueBinding(kGroup, "CommunityList", WriteList));
			AddBinding(m_DetailBinding = new RawValueBinding(kGroup, "CommunityDetail", WriteDetail));
			AddBinding(m_ActionBinding = new RawValueBinding(kGroup, "CommunityAction", WriteAction));
			AddBinding(m_PdxBinding = new RawValueBinding(kGroup, "PdxStatus", WritePdx));
			AddBinding(m_PublishedBinding = new RawValueBinding(kGroup, "Published", WritePublished));
			AddBinding(m_ClipboardBinding = new RawValueBinding(kGroup, "Clipboard", WriteClipboard));

			AddBinding(new TriggerBinding(kGroup, "CommunityOpen", Open));
			AddBinding(new TriggerBinding<string, string, int>(kGroup, "CommunitySearch", Search));
			AddBinding(new TriggerBinding<string>(kGroup, "CommunityOpenLibrary", OpenLibrary));
			AddBinding(new TriggerBinding(kGroup, "CommunityCloseLibrary", CloseLibrary));
			AddBinding(new TriggerBinding(kGroup, "CommunityImport", Import));
			AddBinding(new TriggerBinding<string, string>(kGroup, "CommunityReport", Report));
			AddBinding(new TriggerBinding<string, string, string>(kGroup, "CommunityPublish", Publish));
			AddBinding(new TriggerBinding<string>(kGroup, "CommunityUnpublish", Unpublish));
			AddBinding(new TriggerBinding<string>(kGroup, "CopyShareCode", CopyShareCode));
			AddBinding(new TriggerBinding<string>(kGroup, "ImportShareCode", ImportShareCode));
			AddBinding(new TriggerBinding(kGroup, "ReadClipboard", ReadClipboard));
			AddBinding(new TriggerBinding<int>(kGroup, "OpenPackagePage", OpenPackagePage));
			AddBinding(new TriggerBinding(kGroup, "SubscribeMissing", SubscribeMissing));
			AddBinding(new TriggerBinding(kGroup, "ClearCommunityAction", ClearAction));
			AddBinding(new TriggerBinding<string>(kGroup, "CommunityForgetPublished", ForgetPublished));
			AddBinding(new TriggerBinding(kGroup, "CommunityRefreshAccount", () => RefreshPdx(null)));
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
					Mod.Log.Error("Community: " + ex);
				}
			}
		}

		private void Post(Action action) => m_MainThread.Enqueue(action);

		#region Triggers

		// The Community tab was opened: account state, a health check and the first page.
		private void Open()
		{
			RefreshPdx(null);

			m_Client.Health(response =>
			{
				m_Server = response.Ok ? "online" : response.error == "network" || response.error == "timeout" ? "offline" : "online";
				m_ListBinding.Update();
			});

			if (m_ListState == "idle" || m_ListState == "error")
			{
				Search(m_Query, m_Sort, m_PageIndex);
			}
		}

		private void Search(string query, string sort, int page)
		{
			m_Query = (query ?? "").Trim();
			m_Sort = kSorts.Contains(sort) ? sort : "popular";
			m_PageIndex = Math.Max(0, page);
			m_ListState = "loading";
			m_ListError = "";
			m_ListMessage = "";
			m_ListBinding.Update();

			var seq = ++m_ListSeq;

			m_Client.List(m_Query, m_Sort, m_PageIndex, response =>
			{
				if (seq != m_ListSeq)
				{
					return;
				}

				var result = response.Ok ? response.Parse<CommunityPage>() : null;

				if (result == null)
				{
					SetListError(response.Ok ? "server" : response.error, response.message);
					return;
				}

				m_Server = "online";
				m_ListState = "ready";
				m_Items = (result.items ?? new List<CommunitySummary>()).Where(x => x != null).ToList();
				m_Total = result.total;
				m_PageSize = result.pageSize > 0 ? result.pageSize : 20;
				m_PageIndex = result.page;
				m_ListBinding.Update();
			});
		}

		private void SetListError(string error, string message)
		{
			m_ListState = "error";
			m_ListError = error;
			m_ListMessage = message ?? "";

			if (error == "network" || error == "timeout")
			{
				m_Server = "offline";
			}

			m_ListBinding.Update();
		}

		private void OpenLibrary(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return;
			}

			m_DetailState = "loading";
			m_DetailError = "";
			m_DetailMessage = "";
			m_DetailSource = "server";
			m_DetailSummary = m_Items.FirstOrDefault(x => x.id == id) ?? new CommunitySummary { id = id };
			m_DetailPayload = null;
			m_Analysis = null;
			m_ImportedId = "";
			m_Reported = false;
			ClearAction();
			m_DetailBinding.Update();

			var seq = ++m_DetailSeq;

			m_Client.Get(id, false, response =>
			{
				if (seq != m_DetailSeq)
				{
					return;
				}

				var detail = response.Ok ? response.Parse<CommunityDetail>() : null;

				if (detail?.payload == null)
				{
					m_DetailState = "error";
					m_DetailError = response.Ok ? "server" : response.error;
					m_DetailMessage = response.message ?? "";
					m_DetailBinding.Update();
					return;
				}

				ShareCode.Normalize(detail.payload);
				ShowDetail("server", detail, detail.payload);
			});
		}

		private void ShowDetail(string source, CommunitySummary summary, CommunityPayload payload)
		{
			m_DetailState = "ready";
			m_DetailError = "";
			m_DetailMessage = "";
			m_DetailSource = source;
			m_DetailSummary = summary;
			m_DetailPayload = payload;
			m_Analysis = PayloadBuilder.Analyze(payload, m_Scan.ByKey, m_Scan.Records);
			m_DetailBinding.Update();
		}

		private void CloseLibrary()
		{
			++m_DetailSeq;
			m_DetailState = "none";
			m_DetailPayload = null;
			m_DetailSummary = null;
			m_Analysis = null;
			m_ImportedId = "";
			ClearAction();
			m_DetailBinding.Update();
		}

		// Imports the library shown in the detail. The server counts the download.
		private void Import()
		{
			if (m_DetailState != "ready" || m_DetailSummary == null || m_DetailSource != "server" || m_ImportedId.Length > 0)
			{
				return;
			}

			var summary = m_DetailSummary;
			var seq = m_DetailSeq;

			SetAction("import", "busy");

			m_Client.Get(summary.id, true, response =>
			{
				var detail = response.Ok ? response.Parse<CommunityDetail>() : null;

				if (detail?.payload == null)
				{
					SetAction("import", "error", response.Ok ? "server" : response.error, response.message);
					return;
				}

				ShareCode.Normalize(detail.payload);

				var library = CreateLibrary(detail.payload, "community:" + summary.id);

				if (seq == m_DetailSeq)
				{
					m_ImportedId = library.id;
					ShowDetail("server", detail, detail.payload);
				}

				SetAction("import", "done", library: library.id, count: detail.payload.assets.Count);
			});
		}

		private Library CreateLibrary(CommunityPayload payload, string source)
		{
			var name = string.IsNullOrWhiteSpace(payload.name) ? "Community library" : payload.name.Trim();

			// Keys that are not installed stay in the library and show up once the asset is.
			var library = Mod.Store.Create(name, source, payload.assets.Select(x => x.k), payload.description ?? "", payload.tags);

			Mod.Store.RecordPackages(payload);

			Mod.Log.Info($"Imported library \"{name}\" with {library.assets.Count} assets");
			ShelveItUISystem.Instance?.NotifyLibrariesChanged();

			return library;
		}

		private void Report(string reason, string note)
		{
			if (m_DetailSummary == null || m_DetailSource != "server" || m_Reported)
			{
				return;
			}

			reason = kReasons.Contains(reason) ? reason : "other";
			note = (note ?? "").Trim();

			if (note.Length > 500)
			{
				note = note.Substring(0, 500);
			}

			SetAction("report", "busy");

			m_Client.Report(m_DetailSummary.id, reason, note, m_Published.ClientId, response =>
			{
				if (response.Ok)
				{
					m_Reported = true;
					m_DetailBinding.Update();
					SetAction("report", "done");
				}
				else
				{
					SetAction("report", "error", response.error, response.message);
				}
			});
		}

		// Publishes the library, or updates it when this install published it before.
		private void Publish(string libraryId, string description, string tags)
		{
			var library = Mod.Store.Get(libraryId);

			if (library == null)
			{
				return;
			}

			var payload = PayloadBuilder.Build(library, m_Scan.ByKey, Mod.Store, description, PayloadBuilder.ParseTags(tags));

			// Kept locally so the Share bar starts from them and the next update does not lose them.
			if (Mod.Store.SetShareInfo(libraryId, payload.description, payload.tags))
			{
				ShelveItUISystem.Instance?.NotifyLibrariesChanged();
			}

			if (payload.name.Length < PayloadBuilder.kNameMin)
			{
				SetAction("publish", "error", "name", library: libraryId);
				return;
			}

			if (payload.assets.Count == 0)
			{
				SetAction("publish", "error", "empty", library: libraryId);
				return;
			}

			SetAction("publish", "busy", library: libraryId);

			PdxBridge.GetDisplayName(name => Post(() =>
			{
				RefreshPdx(name);

				if (name == null)
				{
					SetAction("publish", "error", "notloggedin", library: libraryId);
					return;
				}

				if (m_Published.TryGet(libraryId, out var entry))
				{
					m_Client.Update(entry.serverId, entry.ownerToken, payload, name, m_Published.ClientId, response =>
					{
						if (response.Ok)
						{
							SetAction("publish", "done", library: libraryId, count: payload.assets.Count);
							return;
						}

						if (response.error == "notfound")
						{
							m_Published.Remove(libraryId);
							m_PublishedBinding.Update();
						}

						SetAction("publish", "error", response.error, response.message, libraryId);
					});

					return;
				}

				m_Client.Publish(payload, name, m_Published.ClientId, response =>
				{
					var result = response.Ok ? response.Parse<PublishResult>() : null;

					if (result == null || string.IsNullOrEmpty(result.id) || string.IsNullOrEmpty(result.ownerToken))
					{
						SetAction("publish", "error", response.Ok ? "server" : response.error, response.message, libraryId);
						return;
					}

					m_Published.Set(libraryId, new PublishedEntry { serverId = result.id, ownerToken = result.ownerToken });
					m_PublishedBinding.Update();
					SetAction("publish", "done", library: libraryId, count: payload.assets.Count);
				});
			}));
		}

		private void Unpublish(string libraryId)
		{
			if (!m_Published.TryGet(libraryId, out var entry))
			{
				return;
			}

			SetAction("unpublish", "busy", library: libraryId);

			m_Client.Delete(entry.serverId, entry.ownerToken, response =>
			{
				if (response.Ok || response.error == "notfound")
				{
					m_Published.Remove(libraryId);
					m_PublishedBinding.Update();
					SetAction("unpublish", "done", library: libraryId);
				}
				else
				{
					SetAction("unpublish", "error", response.error, response.message, libraryId);
				}
			});
		}

		// After a 403 on update or unpublish: the server no longer accepts this install's token.
		private void ForgetPublished(string libraryId)
		{
			if (!m_Published.TryGet(libraryId, out _))
			{
				return;
			}

			m_Published.Remove(libraryId);
			m_PublishedBinding.Update();
			SetAction("forget", "done", library: libraryId);
		}

		private void CopyShareCode(string libraryId)
		{
			var library = Mod.Store.Get(libraryId);

			if (library == null)
			{
				return;
			}

			try
			{
				var payload = PayloadBuilder.Build(library, m_Scan.ByKey, Mod.Store, library.description, library.tags);

				GUIUtility.systemCopyBuffer = ShareCode.Encode(payload);
				SetAction("copy", "done", library: libraryId, count: payload.assets.Count);
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not copy the share code: " + ex.Message);
				SetAction("copy", "error", "clipboard", ex.Message, libraryId);
			}
		}

		private void ImportShareCode(string code)
		{
			CommunityPayload payload;

			try
			{
				payload = ShareCode.Decode(code);
			}
			catch (ShareCode.DecodeException ex)
			{
				SetAction("code", "error", "code-" + ex.Code, ex.Message);
				return;
			}

			if (payload.assets.Count == 0)
			{
				SetAction("code", "error", "code-empty");
				return;
			}

			var library = CreateLibrary(payload, "code");

			++m_DetailSeq;
			m_ImportedId = library.id;
			m_Reported = false;
			ShowDetail("code", new CommunitySummary
			{
				name = library.name,
				description = payload.description,
				tags = payload.tags,
				assetCount = payload.assets.Count,
				packageCount = payload.packages.Count,
			}, payload);

			SetAction("code", "done", library: library.id, count: payload.assets.Count);
		}

		private void ReadClipboard()
		{
			try
			{
				m_ClipboardText = GUIUtility.systemCopyBuffer ?? "";
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not read the clipboard: " + ex.Message);
				m_ClipboardText = "";
			}

			m_ClipboardSeq++;
			m_ClipboardBinding.Update();
		}

		private void OpenPackagePage(int packageId)
		{
			if (!PdxBridge.ShowModPage(packageId))
			{
				SetAction("page", "error", "unavailable");
			}
		}

		private void SubscribeMissing()
		{
			var ids = m_Analysis?.missingPackages.Select(x => x.id).ToList();

			if (ids == null || ids.Count == 0)
			{
				return;
			}

			SetAction("subscribe", "busy", count: ids.Count);

			PdxBridge.SubscribeAll(ids, (ok, message) => Post(() =>
			{
				if (ok)
				{
					SetAction("subscribe", "done", count: ids.Count);
					World.GetOrCreateSystemManaged<PackageLoaderSystem>().TrackSubscribed(ids);
				}
				else
				{
					SetAction("subscribe", "error", message == "unavailable" ? "unavailable" : "subscribe", message, count: ids.Count);
				}
			}));
		}

		private void ClearAction()
		{
			if (m_ActionKind.Length == 0)
			{
				return;
			}

			m_ActionKind = m_ActionState = m_ActionError = m_ActionMessage = m_ActionLibrary = "";
			m_ActionCount = 0;
			m_ActionBinding.Update();
		}

		#endregion

		private void SetAction(string kind, string state, string error = "", string message = "", string library = "", int count = 0)
		{
			m_ActionKind = kind;
			m_ActionState = state;
			m_ActionError = error ?? "";
			m_ActionMessage = message ?? "";
			m_ActionLibrary = library ?? "";
			m_ActionCount = count;
			m_ActionBinding.Update();
		}

		// name: a display name already read, or null to read it now.
		private void RefreshPdx(string name)
		{
			m_PdxAvailable = PdxBridge.Available;
			m_PdxLoggedIn = PdxBridge.LoggedIn;

			if (name != null)
			{
				m_PdxName = name;
			}
			else if (m_PdxLoggedIn && m_PdxName.Length == 0)
			{
				PdxBridge.GetDisplayName(read => Post(() =>
				{
					m_PdxName = read ?? "";
					m_PdxBinding.Update();
				}));
			}

			m_PdxBinding.Update();
		}

		#region Writers

		private static void WriteStrings(IJsonWriter writer, IList<string> values)
		{
			values ??= new string[0];
			writer.ArrayBegin((uint)values.Count);

			foreach (var value in values)
			{
				writer.Write(value ?? "");
			}

			writer.ArrayEnd();
		}

		private static void Property(IJsonWriter writer, string name, string value)
		{
			writer.PropertyName(name);
			writer.Write(value ?? "");
		}

		private static void Property(IJsonWriter writer, string name, int value)
		{
			writer.PropertyName(name);
			writer.Write(value);
		}

		private static void Property(IJsonWriter writer, string name, bool value)
		{
			writer.PropertyName(name);
			writer.Write(value);
		}

		private void WriteList(IJsonWriter writer)
		{
			writer.TypeBegin("ShelveIt.CommunityList");
			Property(writer, "server", m_Server);
			Property(writer, "state", m_ListState);
			Property(writer, "error", m_ListError);
			Property(writer, "message", m_ListMessage);
			Property(writer, "query", m_Query);
			Property(writer, "sort", m_Sort);
			Property(writer, "page", m_PageIndex);
			Property(writer, "pageSize", m_PageSize);
			Property(writer, "total", m_Total);
			writer.PropertyName("items");
			writer.ArrayBegin((uint)m_Items.Count);

			foreach (var item in m_Items)
			{
				var description = item.description ?? "";

				if (description.Length > kListDescription)
				{
					description = description.Substring(0, kListDescription).TrimEnd() + "…";
				}

				writer.TypeBegin("ShelveIt.CommunityItem");
				Property(writer, "id", item.id);
				Property(writer, "name", item.name);
				Property(writer, "author", item.author);
				Property(writer, "description", description);
				writer.PropertyName("tags");
				WriteStrings(writer, item.tags);
				Property(writer, "assetCount", item.assetCount);
				Property(writer, "packageCount", item.packageCount);
				Property(writer, "downloads", item.downloads);
				writer.TypeEnd();
			}

			writer.ArrayEnd();
			writer.TypeEnd();
		}

		private void WriteDetail(IJsonWriter writer)
		{
			var summary = m_DetailSummary ?? new CommunitySummary();
			var analysis = m_Analysis ?? new PayloadBuilder.Analysis();

			writer.TypeBegin("ShelveIt.CommunityDetail");
			Property(writer, "state", m_DetailState);
			Property(writer, "error", m_DetailError);
			Property(writer, "message", m_DetailMessage);
			Property(writer, "source", m_DetailSource);
			Property(writer, "id", summary.id);
			Property(writer, "name", summary.name);
			Property(writer, "author", summary.author);
			Property(writer, "description", summary.description);
			writer.PropertyName("tags");
			WriteStrings(writer, summary.tags);
			Property(writer, "assetCount", m_DetailPayload?.assets.Count ?? summary.assetCount);
			Property(writer, "packageCount", m_DetailPayload?.packages.Count ?? summary.packageCount);
			Property(writer, "downloads", summary.downloads);
			Property(writer, "updatedAt", summary.updatedAt);
			Property(writer, "installed", analysis.installed);
			Property(writer, "missing", analysis.missing);
			Property(writer, "scanned", m_Scan.HasScanned);
			Property(writer, "importedId", m_ImportedId);
			Property(writer, "reported", m_Reported);
			writer.PropertyName("thumbs");
			WriteStrings(writer, analysis.previewThumbs);
			writer.PropertyName("packages");
			writer.ArrayBegin((uint)analysis.missingPackages.Count);

			foreach (var package in analysis.missingPackages)
			{
				writer.TypeBegin("ShelveIt.CommunityPackage");
				Property(writer, "id", package.id);
				Property(writer, "title", package.title);
				writer.TypeEnd();
			}

			writer.ArrayEnd();
			writer.TypeEnd();
		}

		private void WriteAction(IJsonWriter writer)
		{
			writer.TypeBegin("ShelveIt.CommunityAction");
			Property(writer, "kind", m_ActionKind);
			Property(writer, "state", m_ActionState);
			Property(writer, "error", m_ActionError);
			Property(writer, "message", m_ActionMessage);
			Property(writer, "libraryId", m_ActionLibrary);
			Property(writer, "count", m_ActionCount);
			writer.TypeEnd();
		}

		private void WritePdx(IJsonWriter writer)
		{
			writer.TypeBegin("ShelveIt.PdxStatus");
			Property(writer, "available", m_PdxAvailable);
			Property(writer, "loggedIn", m_PdxLoggedIn);
			Property(writer, "name", m_PdxName);
			writer.TypeEnd();
		}

		private void WritePublished(IJsonWriter writer)
		{
			WriteStrings(writer, m_Published.LibraryIds.ToList());
		}

		private void WriteClipboard(IJsonWriter writer)
		{
			writer.TypeBegin("ShelveIt.Clipboard");
			Property(writer, "seq", m_ClipboardSeq);
			Property(writer, "text", m_ClipboardText);
			writer.TypeEnd();
		}

		#endregion
	}
}
