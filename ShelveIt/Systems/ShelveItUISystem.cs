using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ShelveIt.Domain;
using ShelveIt.Logic;

using Colossal.Serialization.Entities;
using Colossal.UI.Binding;

using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;

namespace ShelveIt.Systems
{
	// All work happens in trigger callbacks and scan state changes. OnUpdate is left to the base class,
	// so nothing runs per frame whether the panel is open or not.
	public partial class ShelveItUISystem : UISystemBase
	{
		public const string kGroup = "ShelveIt";
		public const string kSuggestionPrefix = "suggestion:";
		public const string kAllAssets = "all";
		public const int kPageSize = 60;
		public const string kModeManager = "manager";
		public const string kModePalette = "palette";

		private struct LibraryView
		{
			public string id, name, source, sourceId, description;
			public string[] tags;
			public int count;
		}

		private struct TreeRow
		{
			public string id, parentId, name;
			public int depth, count;
			public bool hasChildren, expanded, group;
			public string[] thumbs;
		}

		private struct AssetView
		{
			public AssetRecord record;
			public string name;
			public bool inLibrary;
		}

		private struct FilterView
		{
			public string facet, value, label;
		}

		public static ShelveItUISystem Instance { get; private set; }

		private AssetScanSystem m_Scan;
		private PrefabSystem m_PrefabSystem;
		private PrefabUISystem m_PrefabUISystem;
		private ToolSystem m_ToolSystem;

		private ValueBinding<bool> m_PanelVisible;
		private ValueBinding<string> m_PanelMode;
		private ValueBinding<string> m_ScanState;
		private ValueBinding<bool> m_FindItAvailable;
		private ValueBinding<string> m_SelectedId;
		private ValueBinding<int> m_AssetCount;
		private ValueBinding<int> m_TotalCount;
		private ValueBinding<int> m_Page;
		private ValueBinding<string> m_Search;
		private ValueBinding<string> m_Sort;
		private RawValueBinding m_Libraries;
		private RawValueBinding m_SuggestionTree;
		private RawValueBinding m_Assets;
		private RawValueBinding m_Facets;
		private RawValueBinding m_ActiveFilters;

		private readonly List<LibraryView> m_LibraryViews = new List<LibraryView>();
		private readonly List<TreeRow> m_TreeRows = new List<TreeRow>();
		private readonly List<AssetView> m_AssetViews = new List<AssetView>(kPageSize);
		private readonly List<FilterView> m_FilterViews = new List<FilterView>();
		private readonly List<FacetResult> m_FacetResults = new List<FacetResult>();
		private readonly HashSet<string> m_Expanded = new HashSet<string>();
		private readonly Dictionary<string, string[]> m_Thumbs = new Dictionary<string, string[]>();
		private readonly QueryState m_Query = new QueryState();

		private List<AssetRecord> m_Base = new List<AssetRecord>();
		private List<AssetRecord> m_Filtered = new List<AssetRecord>();
		private bool m_QueryLogged;

		protected override void OnCreate()
		{
			base.OnCreate();

			Instance = this;

			m_Scan = World.GetOrCreateSystemManaged<AssetScanSystem>();
			m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
			m_PrefabUISystem = World.GetOrCreateSystemManaged<PrefabUISystem>();
			m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();

			m_Scan.StateChanged += OnScanStateChanged;

			AddBinding(m_PanelVisible = new ValueBinding<bool>(kGroup, "PanelVisible", false));
			AddBinding(m_PanelMode = new ValueBinding<string>(kGroup, "PanelMode", ReadPanelMode()));
			AddBinding(m_ScanState = new ValueBinding<string>(kGroup, "ScanState", m_Scan.State));
			AddBinding(m_FindItAvailable = new ValueBinding<bool>(kGroup, "FindItAvailable", false));
			AddBinding(m_SelectedId = new ValueBinding<string>(kGroup, "SelectedId", ""));
			AddBinding(m_AssetCount = new ValueBinding<int>(kGroup, "AssetCount", 0));
			AddBinding(m_TotalCount = new ValueBinding<int>(kGroup, "TotalCount", 0));
			AddBinding(m_Page = new ValueBinding<int>(kGroup, "Page", 0));
			AddBinding(m_Search = new ValueBinding<string>(kGroup, "Search", ""));
			AddBinding(m_Sort = new ValueBinding<string>(kGroup, "Sort", ""));
			AddBinding(new ValueBinding<int>(kGroup, "PageSize", kPageSize));
			AddBinding(m_Libraries = new RawValueBinding(kGroup, "Libraries", WriteLibraries));
			AddBinding(m_SuggestionTree = new RawValueBinding(kGroup, "SuggestionTree", WriteTree));
			AddBinding(m_Assets = new RawValueBinding(kGroup, "Assets", WriteAssets));
			AddBinding(m_Facets = new RawValueBinding(kGroup, "Facets", WriteFacets));
			AddBinding(m_ActiveFilters = new RawValueBinding(kGroup, "ActiveFilters", WriteActiveFilters));

			// The flat list of the first version. Kept empty so an older UI bundle does not break.
			AddBinding(new RawValueBinding(kGroup, "Suggestions", writer =>
			{
				writer.ArrayBegin(0u);
				writer.ArrayEnd();
			}));

			AddBinding(new TriggerBinding(kGroup, "TogglePanel", TogglePanel));
			AddBinding(new TriggerBinding<string>(kGroup, "SetPanelMode", SetPanelMode));
			AddBinding(new TriggerBinding<string>(kGroup, "Select", Select));
			AddBinding(new TriggerBinding<int>(kGroup, "SetPage", SetPage));
			AddBinding(new TriggerBinding<string>(kGroup, "CreateLibrary", CreateLibrary));
			AddBinding(new TriggerBinding<string, string>(kGroup, "RenameLibrary", RenameLibrary));
			AddBinding(new TriggerBinding<string>(kGroup, "DeleteLibrary", DeleteLibrary));
			AddBinding(new TriggerBinding<string>(kGroup, "AcceptSuggestion", AcceptSuggestion));
			AddBinding(new TriggerBinding<string>(kGroup, "DismissSuggestion", DismissSuggestion));
			AddBinding(new TriggerBinding<string>(kGroup, "ToggleSuggestionNode", ToggleSuggestionNode));
			AddBinding(new TriggerBinding<string, bool>(kGroup, "SetSuggestionExpanded", SetSuggestionExpanded));
			AddBinding(new TriggerBinding<string>(kGroup, "AddActivePrefab", AddActivePrefab));
			AddBinding(new TriggerBinding<string, string>(kGroup, "AddAsset", AddAsset));
			AddBinding(new TriggerBinding<string, string>(kGroup, "RemoveAsset", RemoveAsset));
			AddBinding(new TriggerBinding<int>(kGroup, "PlaceAsset", PlaceAsset));
			AddBinding(new TriggerBinding<string, string, bool>(kGroup, "SetFilter", SetFilter));
			AddBinding(new TriggerBinding(kGroup, "ClearFilters", ClearFilters));
			AddBinding(new TriggerBinding<string>(kGroup, "SetSearch", SetSearch));
			AddBinding(new TriggerBinding<string>(kGroup, "SetSort", SetSort));
			AddBinding(new TriggerBinding<string>(kGroup, "SaveFilteredAsLibrary", SaveFilteredAsLibrary));
			AddBinding(new TriggerBinding<string>(kGroup, "AddFilteredToLibrary", AddFilteredToLibrary));
			AddBinding(new TriggerBinding(kGroup, "RemoveFilteredFromLibrary", RemoveFilteredFromLibrary));
			AddBinding(new TriggerBinding<string>(kGroup, "Export", Export));
			AddBinding(new TriggerBinding(kGroup, "ImportAll", ImportAll));
			AddBinding(new TriggerBinding(kGroup, "ExportContribution", ExportContribution));
			AddBinding(new TriggerBinding(kGroup, "Rescan", Rescan));

			RefreshLibraries();
			RefreshTree();
		}

		protected override void OnDestroy()
		{
			if (m_Scan != null)
			{
				m_Scan.StateChanged -= OnScanStateChanged;
			}

			if (Instance == this)
			{
				Instance = null;
			}

			base.OnDestroy();
		}

		protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
		{
			base.OnGameLoadingComplete(purpose, mode);

			m_PanelVisible.Update(false);

			if (!mode.IsGameOrEditor())
			{
				return;
			}

			m_FindItAvailable.Update(FindItBridge.IsAvailable());

			if (!m_Scan.HasScanned || m_Scan.Stale)
			{
				m_Scan.RequestScan(false);
			}
		}

		private void OnScanStateChanged()
		{
			m_ScanState.Update(m_Scan.State);

			if (m_Scan.State == AssetScanSystem.kReady)
			{
				m_Thumbs.Clear();
				m_QueryLogged = false;
				RefreshAll();
			}
		}

		public void OnSettingsChanged()
		{
			m_PanelMode.Update(ReadPanelMode());
			RefreshTree();
		}

		// A library was added from outside this system (community import, share code).
		public void NotifyLibrariesChanged()
		{
			RefreshLibraries();
			RefreshTree();
			RefreshPage();
		}

		private static string ReadPanelMode()
		{
			return Mod.Settings != null && Mod.Settings.PanelMode == kModePalette ? kModePalette : kModeManager;
		}

		#region Triggers

		private void TogglePanel()
		{
			var visible = !m_PanelVisible.value;

			if (visible)
			{
				m_FindItAvailable.Update(FindItBridge.IsAvailable());

				if (m_Scan.Stale)
				{
					Mod.Log.Info("Panel opened: rescanning for newly loaded assets");
					m_Scan.RequestScan(false);
				}

				World.GetOrCreateSystemManaged<PackageLoaderSystem>().CheckPending();
			}

			m_PanelVisible.Update(visible);
		}

		// Remembered across sessions through the mod settings file.
		private void SetPanelMode(string mode)
		{
			mode = mode == kModePalette ? kModePalette : kModeManager;

			if (mode == m_PanelMode.value)
			{
				return;
			}

			m_PanelMode.Update(mode);

			if (Mod.Settings != null)
			{
				Mod.Settings.PanelMode = mode;

				try
				{
					Colossal.IO.AssetDatabase.AssetDatabase.global.SaveSettings();
				}
				catch (Exception ex)
				{
					Mod.Log.Warn("Could not save the panel mode: " + ex.Message);
				}
			}
		}

		// Ids: a library id, "all", "suggestion:<node id>" or "" for nothing.
		private void Select(string id)
		{
			m_SelectedId.Update(id ?? "");
			m_Query.filters.Clear();
			m_Query.search = "";
			m_Search.Update("");
			m_Page.Update(0);

			RefreshView(true);
		}

		private void SetPage(int page)
		{
			m_Page.Update(page);

			RefreshPage();
		}

		private void SetFilter(string facet, string value, bool on)
		{
			if (facet == null || FacetIndex.FacetOf(facet) < 0)
			{
				return;
			}

			value ??= "";

			if (!m_Query.filters.TryGetValue(facet, out var values))
			{
				if (!on)
				{
					return;
				}

				m_Query.filters[facet] = values = new HashSet<string>(StringComparer.Ordinal);
			}

			if (!(on ? values.Add(value) : values.Remove(value)))
			{
				return;
			}

			if (values.Count == 0)
			{
				m_Query.filters.Remove(facet);
			}

			m_Page.Update(0);
			RefreshView(false);
		}

		private void ClearFilters()
		{
			if (!m_Query.HasFilters)
			{
				return;
			}

			m_Query.filters.Clear();
			m_Query.search = "";
			m_Search.Update("");
			m_Page.Update(0);
			RefreshView(false);
		}

		private void SetSearch(string text)
		{
			text = (text ?? "").Trim();

			if (text == m_Query.search)
			{
				return;
			}

			m_Query.search = text;
			m_Search.Update(text);
			m_Page.Update(0);
			RefreshView(false);
		}

		// "" keeps the natural order of the view, otherwise "name", "package", "lot" or "newest".
		private void SetSort(string sort)
		{
			sort ??= "";

			if (sort != "" && sort != AssetQuery.kSortName && sort != AssetQuery.kSortPackage && sort != AssetQuery.kSortLot && sort != AssetQuery.kSortNewest)
			{
				return;
			}

			if (sort == m_Query.sort)
			{
				return;
			}

			m_Query.sort = sort;
			m_Sort.Update(sort);
			m_Page.Update(0);
			RefreshView(false);
		}

		private void CreateLibrary(string name)
		{
			var library = Mod.Store.Create(name);

			RefreshLibraries();
			Select(library.id);
		}

		private void SaveFilteredAsLibrary(string name)
		{
			if (m_Filtered.Count == 0)
			{
				return;
			}

			var library = Mod.Store.Create(name, "user", m_Filtered.Select(x => x.key));

			RefreshLibraries();
			Select(library.id);
		}

		private void AddFilteredToLibrary(string libraryId)
		{
			if (m_Filtered.Count > 0 && Mod.Store.AddAssets(libraryId, m_Filtered.Select(x => x.key)) > 0)
			{
				OnLibraryContentChanged(libraryId);
			}
		}

		private void RemoveFilteredFromLibrary()
		{
			var libraryId = m_SelectedId.value;

			if (IsLibraryId(libraryId) && m_Filtered.Count > 0 && Mod.Store.RemoveAssets(libraryId, new HashSet<string>(m_Filtered.Select(x => x.key))) > 0)
			{
				OnLibraryContentChanged(libraryId);
			}
		}

		private void RenameLibrary(string id, string name)
		{
			if (Mod.Store.Rename(id, name))
			{
				RefreshLibraries();
			}
		}

		private void DeleteLibrary(string id)
		{
			if (!Mod.Store.Delete(id))
			{
				return;
			}

			RefreshLibraries();

			// A deleted suggested library makes its suggestion visible again.
			RefreshTree();

			if (m_SelectedId.value == id)
			{
				Select("");
			}
			else
			{
				RefreshPage();
			}
		}

		private void AcceptSuggestion(string nodeId)
		{
			var node = FindNode(nodeId);

			if (node == null || node.group)
			{
				return;
			}

			var library = Mod.Store.Create(GetPathName(node), LibraryStore.kSuggestedPrefix + node.id, node.keys);

			RefreshLibraries();
			RefreshTree();
			Select(library.id);
		}

		private void DismissSuggestion(string nodeId)
		{
			if (string.IsNullOrEmpty(nodeId))
			{
				return;
			}

			Mod.Store.Dismiss(nodeId);

			RefreshTree();

			if (m_SelectedId.value == kSuggestionPrefix + nodeId)
			{
				Select("");
			}
		}

		private void ToggleSuggestionNode(string nodeId)
		{
			SetSuggestionExpanded(nodeId, !m_Expanded.Contains(nodeId ?? ""));
		}

		private void SetSuggestionExpanded(string nodeId, bool expanded)
		{
			if (string.IsNullOrEmpty(nodeId) || !(expanded ? m_Expanded.Add(nodeId) : m_Expanded.Remove(nodeId)))
			{
				return;
			}

			RefreshTree();
		}

		private void AddActivePrefab(string libraryId)
		{
			var prefab = m_ToolSystem.activePrefab;

			if (prefab == null)
			{
				Mod.Log.Info("AddActivePrefab: no prefab is active");
				return;
			}

			AddAsset(libraryId, AssetScanSystem.KeyOf(prefab));
		}

		private void AddAsset(string libraryId, string key)
		{
			if (!string.IsNullOrEmpty(key) && Mod.Store.AddAsset(libraryId, key))
			{
				OnLibraryContentChanged(libraryId);
			}
		}

		private void RemoveAsset(string libraryId, string key)
		{
			if (Mod.Store.RemoveAsset(libraryId, key))
			{
				OnLibraryContentChanged(libraryId);
			}
		}

		private void PlaceAsset(int entityIndex)
		{
			if (m_Scan.ByIndex.TryGetValue(entityIndex, out var record)
				&& m_PrefabSystem.TryGetPrefab<PrefabBase>(record.entity, out var prefab)
				&& prefab != null)
			{
				m_ToolSystem.ActivatePrefabTool(prefab);
			}
		}

		private void Export(string id)
		{
			try
			{
				Mod.Log.Info("Exported library to " + (Mod.Store.Export(id) ?? "(library not found)"));
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Export failed: " + ex.Message);
			}
		}

		public void ExportAllLibraries()
		{
			try
			{
				Mod.Log.Info($"Exported {Mod.Store.ExportAll()} libraries to {Paths.ExportDir}");
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Export failed: " + ex.Message);
			}
		}

		public void ImportAll()
		{
			try
			{
				Mod.Log.Info($"Imported {Mod.Store.ImportAll()} library files from {Paths.ImportDir}");
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Import failed: " + ex.Message);
			}

			RefreshAll();
		}

		public void ExportContribution()
		{
			try
			{
				Mod.Log.Info("Exported contribution to " + Mod.Store.ExportContribution());
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Contribution export failed: " + ex.Message);
			}
		}

		public void Rescan()
		{
			m_Scan.RequestScan(true);
		}

		#endregion

		#region Refresh

		private void OnLibraryContentChanged(string libraryId)
		{
			RefreshLibraries();

			// A suggested library that changed no longer hides its suggestion.
			RefreshTree();

			if (m_SelectedId.value == libraryId)
			{
				RefreshView(true);
			}
			else
			{
				RefreshPage();
			}
		}

		private void RefreshAll()
		{
			RefreshLibraries();
			RefreshTree();
			RefreshView(true);
		}

		private void RefreshLibraries()
		{
			m_LibraryViews.Clear();

			foreach (var library in Mod.Store.Snapshot())
			{
				var count = library.assets.Count;

				if (m_Scan.HasScanned)
				{
					count = 0;

					foreach (var key in library.assets)
					{
						if (m_Scan.ByKey.ContainsKey(key))
						{
							count++;
						}
					}
				}

				m_LibraryViews.Add(new LibraryView
				{
					id = library.id,
					name = library.name ?? "",
					source = LibraryStore.SourceKind(library.source),
					sourceId = LibraryStore.SourceId(library.source),
					description = library.description ?? "",
					tags = library.tags?.ToArray() ?? new string[0],
					count = count,
				});
			}

			m_Libraries.Update();
		}

		// Rows are sent depth first: the three root groups, then the children of every expanded node.
		private void RefreshTree()
		{
			m_TreeRows.Clear();

			if (Mod.Settings != null && Mod.Settings.EnableSuggestions && m_Scan.Classification.nodes.Count > 0)
			{
				var minAssets = Math.Max(1, Mod.Settings.MinAssetsPerSuggestion);
				var dismissed = Mod.Store.DismissedSet();
				var suggested = Mod.Store.SuggestedLibraries();
				var italian = GetLocale() == "it-IT";

				bool Visible(SuggestionNode node)
				{
					if (node.group)
					{
						return node.children.Any(Visible);
					}

					if (node.keys.Count < minAssets || dismissed.Contains(node.id))
					{
						return false;
					}

					// Hidden only while the library made from it still has exactly the same content.
					return !suggested.TryGetValue(node.id, out var assets) || !assets.SetEquals(node.keys);
				}

				void Add(SuggestionNode node)
				{
					var visibleChildren = node.children.Where(Visible).ToList();
					var expanded = m_Expanded.Contains(node.id) && visibleChildren.Count > 0;

					m_TreeRows.Add(new TreeRow
					{
						id = node.id,
						parentId = node.parent ?? "",
						depth = node.depth,
						name = italian && !string.IsNullOrEmpty(node.nameIt) ? node.nameIt : node.name,
						count = node.group ? visibleChildren.Count : node.keys.Count,
						hasChildren = visibleChildren.Count > 0,
						expanded = expanded,
						group = node.group,
						thumbs = node.group ? new string[0] : GetThumbs(node),
					});

					if (expanded)
					{
						foreach (var child in visibleChildren)
						{
							Add(child);
						}
					}
				}

				foreach (var root in m_Scan.Classification.nodes)
				{
					if (root.depth == 0 && Visible(root))
					{
						Add(root);
					}
				}
			}

			m_SuggestionTree.Update();
		}

		// Rebuilds the base set when the view or its content changed, then filters, counts facets and sorts.
		private void RefreshView(bool rebuildBase)
		{
			if (rebuildBase)
			{
				m_Base = BuildBase(m_SelectedId.value ?? "");
			}

			var index = m_Scan.Classification.index;

			if (index == null || m_Base.Count == 0)
			{
				m_Filtered = new List<AssetRecord>();
				m_FacetResults.Clear();
			}
			else
			{
				var watch = Stopwatch.StartNew();

				m_Filtered = AssetQuery.Run(m_Base, index, m_Query, m_FacetResults);

				if (!m_QueryLogged)
				{
					m_QueryLogged = true;
					Mod.Log.Info($"First query after scan: {m_Base.Count} assets filtered and counted in {watch.Elapsed.TotalMilliseconds:0.00} ms");
				}
			}

			Localize(m_FacetResults);

			m_FilterViews.Clear();

			foreach (var pair in m_Query.filters)
			{
				var facet = m_FacetResults.FirstOrDefault(x => x.id == pair.Key);

				foreach (var value in pair.Value)
				{
					var label = value;

					if (facet != null)
					{
						foreach (var option in facet.options)
						{
							if (option.value == value)
							{
								label = option.label;
								break;
							}
						}
					}

					m_FilterViews.Add(new FilterView { facet = pair.Key, value = value, label = label });
				}
			}

			m_TotalCount.Update(m_Base.Count);
			m_AssetCount.Update(m_Filtered.Count);
			m_Facets.Update();
			m_ActiveFilters.Update();

			RefreshPage();
		}

		private void RefreshPage()
		{
			m_AssetViews.Clear();

			var selected = m_SelectedId.value ?? "";
			var isLibrary = IsLibraryId(selected);
			var pageCount = Math.Max(1, (m_Filtered.Count + kPageSize - 1) / kPageSize);
			var page = Math.Max(0, Math.Min(m_Page.value, pageCount - 1));
			var end = Math.Min(m_Filtered.Count, (page + 1) * kPageSize);

			for (var i = page * kPageSize; i < end; i++)
			{
				var record = m_Filtered[i];

				m_AssetViews.Add(new AssetView
				{
					record = record,
					name = GetDisplayName(record),
					inLibrary = isLibrary || Mod.Store.IsInAnyLibrary(record.key),
				});
			}

			m_Page.Update(page);
			m_Assets.Update();
		}

		private List<AssetRecord> BuildBase(string selected)
		{
			if (selected == kAllAssets)
			{
				return m_Scan.Records;
			}

			IList<string> keys = null;

			if (selected.StartsWith(kSuggestionPrefix, StringComparison.Ordinal))
			{
				keys = FindNode(selected.Substring(kSuggestionPrefix.Length))?.keys;
			}
			else if (selected.Length > 0)
			{
				keys = Mod.Store.GetAssetKeys(selected);
			}

			var result = new List<AssetRecord>(keys?.Count ?? 0);

			if (keys != null)
			{
				// Keys of assets that are not installed stay in the library but are not listed.
				foreach (var key in keys)
				{
					if (m_Scan.ByKey.TryGetValue(key, out var record))
					{
						result.Add(record);
					}
				}
			}

			return result;
		}

		#endregion

		#region Lookups

		private static bool IsLibraryId(string id)
		{
			return !string.IsNullOrEmpty(id) && id != kAllAssets && !id.StartsWith(kSuggestionPrefix, StringComparison.Ordinal);
		}

		private SuggestionNode FindNode(string nodeId)
		{
			return nodeId != null && m_Scan.Classification.byId.TryGetValue(nodeId, out var node) ? node : null;
		}

		private static string GetLocale()
		{
			return GameManager.instance?.localizationManager?.activeLocaleId ?? "en-US";
		}

		// "British / Residential medium": the node names below the root group.
		private string GetPathName(SuggestionNode node)
		{
			var italian = GetLocale() == "it-IT";
			var parts = new List<string>(3);

			for (var current = node; current != null && !current.group; current = FindNode(current.parent))
			{
				parts.Insert(0, italian && !string.IsNullOrEmpty(current.nameIt) ? current.nameIt : current.name);
			}

			return string.Join(" / ", parts);
		}

		// Up to four thumbnails, real ones first.
		private string[] GetThumbs(SuggestionNode node)
		{
			if (m_Thumbs.TryGetValue(node.id, out var cached))
			{
				return cached;
			}

			var thumbs = new List<string>(4);
			var limit = Math.Min(node.keys.Count, 400);

			for (var i = 0; i < limit && thumbs.Count < 4; i++)
			{
				if (m_Scan.ByKey.TryGetValue(node.keys[i], out var record) && record.hasThumbnail && !thumbs.Contains(record.thumbnail))
				{
					thumbs.Add(record.thumbnail);
				}
			}

			for (var i = 0; i < limit && thumbs.Count < 4; i++)
			{
				if (m_Scan.ByKey.TryGetValue(node.keys[i], out var record) && !record.hasThumbnail && !string.IsNullOrEmpty(record.thumbnail) && !thumbs.Contains(record.thumbnail))
				{
					thumbs.Add(record.thumbnail);
				}
			}

			return m_Thumbs[node.id] = thumbs.ToArray();
		}

		// Region ids and the vanilla package get a readable label. Everything else is already one.
		private void Localize(List<FacetResult> facets)
		{
			var locale = GetLocale();

			foreach (var facet in facets)
			{
				if (facet.id != "region" && facet.id != "package")
				{
					continue;
				}

				for (var i = 0; i < facet.options.Count; i++)
				{
					var option = facet.options[i];

					if (facet.id == "package")
					{
						if (option.value == SuggestionEngine.kVanillaPackage)
						{
							option.label = locale == "it-IT" ? "Gioco base" : "Base game";
						}
					}
					else if (m_Scan.Classification.defs.TryGetValue(option.value, out var def) && def.name != null
						&& (def.name.TryGetValue(locale, out var name) || def.name.TryGetValue("en-US", out name)))
					{
						option.label = name;
					}

					facet.options[i] = option;
				}
			}
		}

		private string GetDisplayName(AssetRecord record)
		{
			try
			{
				m_PrefabUISystem.GetTitleAndDescription(record.entity, out var titleId, out _);

				if (titleId != null && GameManager.instance.localizationManager.activeDictionary.TryGetValue(titleId, out var title) && !string.IsNullOrEmpty(title))
				{
					return title;
				}
			}
			catch
			{
			}

			return record.name;
		}

		#endregion

		#region Writers

		private void WriteLibraries(IJsonWriter writer)
		{
			writer.ArrayBegin((uint)m_LibraryViews.Count);

			foreach (var view in m_LibraryViews)
			{
				writer.TypeBegin("ShelveIt.Library");
				writer.PropertyName("id");
				writer.Write(view.id);
				writer.PropertyName("name");
				writer.Write(view.name);
				writer.PropertyName("count");
				writer.Write(view.count);
				writer.PropertyName("source");
				writer.Write(view.source);
				writer.PropertyName("sourceId");
				writer.Write(view.sourceId);
				writer.PropertyName("description");
				writer.Write(view.description);
				writer.PropertyName("tags");
				writer.ArrayBegin((uint)view.tags.Length);

				foreach (var tag in view.tags)
				{
					writer.Write(tag);
				}

				writer.ArrayEnd();
				writer.TypeEnd();
			}

			writer.ArrayEnd();
		}

		private void WriteTree(IJsonWriter writer)
		{
			writer.ArrayBegin((uint)m_TreeRows.Count);

			foreach (var row in m_TreeRows)
			{
				writer.TypeBegin("ShelveIt.SuggestionNode");
				writer.PropertyName("id");
				writer.Write(row.id);
				writer.PropertyName("parentId");
				writer.Write(row.parentId);
				writer.PropertyName("depth");
				writer.Write(row.depth);
				writer.PropertyName("name");
				writer.Write(row.name ?? "");
				writer.PropertyName("count");
				writer.Write(row.count);
				writer.PropertyName("group");
				writer.Write(row.group);
				writer.PropertyName("hasChildren");
				writer.Write(row.hasChildren);
				writer.PropertyName("expanded");
				writer.Write(row.expanded);
				writer.PropertyName("thumbs");
				writer.ArrayBegin((uint)row.thumbs.Length);

				foreach (var thumb in row.thumbs)
				{
					writer.Write(thumb ?? "");
				}

				writer.ArrayEnd();
				writer.TypeEnd();
			}

			writer.ArrayEnd();
		}

		private void WriteAssets(IJsonWriter writer)
		{
			writer.ArrayBegin((uint)m_AssetViews.Count);

			foreach (var view in m_AssetViews)
			{
				var record = view.record;

				writer.TypeBegin("ShelveIt.Asset");
				writer.PropertyName("key");
				writer.Write(record.key);
				writer.PropertyName("entityIndex");
				writer.Write(record.entityIndex);
				writer.PropertyName("name");
				writer.Write(view.name ?? "");
				writer.PropertyName("thumbnail");
				writer.Write(record.thumbnail ?? "");
				writer.PropertyName("thumbKind");
				writer.Write(record.thumbKind ?? "none");
				writer.PropertyName("kind");
				writer.Write(record.kind);
				writer.PropertyName("inLibrary");
				writer.Write(view.inLibrary);
				writer.PropertyName("zone");
				writer.Write(record.zone == "None" ? "" : record.zone);
				writer.PropertyName("density");
				writer.Write(record.density ?? "");
				writer.PropertyName("level");
				writer.Write(record.level);
				writer.PropertyName("lotW");
				writer.Write(record.lotW);
				writer.PropertyName("lotD");
				writer.Write(record.lotD);
				writer.PropertyName("region");
				writer.Write(record.region ?? "");
				writer.PropertyName("family");
				writer.Write(record.family ?? "");
				writer.PropertyName("package");
				writer.Write(record.packageTitle ?? "");
				writer.PropertyName("isVanilla");
				writer.Write(record.isVanilla);
				writer.TypeEnd();
			}

			writer.ArrayEnd();
		}

		private void WriteFacets(IJsonWriter writer)
		{
			writer.ArrayBegin((uint)m_FacetResults.Count);

			foreach (var facet in m_FacetResults)
			{
				writer.TypeBegin("ShelveIt.Facet");
				writer.PropertyName("id");
				writer.Write(facet.id);
				writer.PropertyName("total");
				writer.Write(facet.total);
				writer.PropertyName("options");
				writer.ArrayBegin((uint)facet.options.Count);

				foreach (var option in facet.options)
				{
					writer.TypeBegin("ShelveIt.FacetOption");
					writer.PropertyName("value");
					writer.Write(option.value ?? "");
					writer.PropertyName("label");
					writer.Write(option.label ?? "");
					writer.PropertyName("count");
					writer.Write(option.count);
					writer.PropertyName("selected");
					writer.Write(option.selected);
					writer.TypeEnd();
				}

				writer.ArrayEnd();
				writer.TypeEnd();
			}

			writer.ArrayEnd();
		}

		private void WriteActiveFilters(IJsonWriter writer)
		{
			writer.ArrayBegin((uint)m_FilterViews.Count);

			foreach (var view in m_FilterViews)
			{
				writer.TypeBegin("ShelveIt.ActiveFilter");
				writer.PropertyName("facet");
				writer.Write(view.facet);
				writer.PropertyName("value");
				writer.Write(view.value);
				writer.PropertyName("label");
				writer.Write(view.label ?? "");
				writer.TypeEnd();
			}

			writer.ArrayEnd();
		}

		#endregion
	}
}
