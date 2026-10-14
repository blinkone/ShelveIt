import { bindValue, trigger } from "cs2/api";

// Mirrors Systems/ShelveItUISystem.cs. Binding objects are created here, but the game
// only subscribes to a value while a mounted component reads it with useValue.

export const GROUP = "ShelveIt";
export const SUGGESTION_PREFIX = "suggestion:";
export const ALL_ASSETS = "all";

export type ScanState = "idle" | "scanning" | "classifying" | "ready";
export type PanelMode = "manager" | "palette";
export type ThumbKind = "real" | "group" | "none";

export interface Library {
  id: string;
  name: string;
  count: number;
  /** "user", "suggested" or "legacy" */
  source: string;
  sourceId: string;
  /** Community description and tags, as last published or imported. */
  description: string;
  tags: string[];
}

/** One visible row of the suggestion tree, in depth-first order. */
export interface TreeRow {
  id: string;
  parentId: string;
  depth: number;
  name: string;
  count: number;
  /** Group rows only organize the tree and cannot be accepted. */
  group: boolean;
  hasChildren: boolean;
  expanded: boolean;
  thumbs: string[];
}

export interface Asset {
  key: string;
  entityIndex: number;
  name: string;
  thumbnail: string;
  thumbKind: ThumbKind;
  kind: string;
  inLibrary: boolean;
  zone: string;
  density: string;
  level: number;
  lotW: number;
  lotD: number;
  region: string;
  family: string;
  package: string;
  isVanilla: boolean;
}

export interface FacetOption {
  value: string;
  label: string;
  count: number;
  selected: boolean;
}

export interface Facet {
  id: string;
  total: number;
  options: FacetOption[];
}

export interface ActiveFilter {
  facet: string;
  value: string;
  label: string;
}

export const panelVisible$ = bindValue<boolean>(GROUP, "PanelVisible", false);
export const panelMode$ = bindValue<PanelMode>(GROUP, "PanelMode", "manager");
export const scanState$ = bindValue<ScanState>(GROUP, "ScanState", "idle");
export const findItAvailable$ = bindValue<boolean>(GROUP, "FindItAvailable", false);
export const libraries$ = bindValue<Library[]>(GROUP, "Libraries", []);
export const suggestionTree$ = bindValue<TreeRow[]>(GROUP, "SuggestionTree", []);
export const selectedId$ = bindValue<string>(GROUP, "SelectedId", "");
export const assets$ = bindValue<Asset[]>(GROUP, "Assets", []);
export const assetCount$ = bindValue<number>(GROUP, "AssetCount", 0);
export const totalCount$ = bindValue<number>(GROUP, "TotalCount", 0);
export const page$ = bindValue<number>(GROUP, "Page", 0);
export const pageSize$ = bindValue<number>(GROUP, "PageSize", 60);
export const search$ = bindValue<string>(GROUP, "Search", "");
export const sort$ = bindValue<string>(GROUP, "Sort", "");
export const facets$ = bindValue<Facet[]>(GROUP, "Facets", []);
export const activeFilters$ = bindValue<ActiveFilter[]>(GROUP, "ActiveFilters", []);

export const togglePanel = () => trigger(GROUP, "TogglePanel");
export const setPanelMode = (mode: PanelMode) => trigger(GROUP, "SetPanelMode", mode);
export const select = (id: string) => trigger(GROUP, "Select", id);
export const setPage = (page: number) => trigger(GROUP, "SetPage", page);
export const createLibrary = (name: string) => trigger(GROUP, "CreateLibrary", name);
export const renameLibrary = (id: string, name: string) => trigger(GROUP, "RenameLibrary", id, name);
export const deleteLibrary = (id: string) => trigger(GROUP, "DeleteLibrary", id);
export const acceptSuggestion = (nodeId: string) => trigger(GROUP, "AcceptSuggestion", nodeId);
export const dismissSuggestion = (nodeId: string) => trigger(GROUP, "DismissSuggestion", nodeId);
export const toggleSuggestionNode = (nodeId: string) => trigger(GROUP, "ToggleSuggestionNode", nodeId);
export const addActivePrefab = (libraryId: string) => trigger(GROUP, "AddActivePrefab", libraryId);
export const addAsset = (libraryId: string, key: string) => trigger(GROUP, "AddAsset", libraryId, key);
export const removeAsset = (libraryId: string, key: string) => trigger(GROUP, "RemoveAsset", libraryId, key);
export const placeAsset = (entityIndex: number) => trigger(GROUP, "PlaceAsset", entityIndex);
export const setFilter = (facet: string, value: string, on: boolean) => trigger(GROUP, "SetFilter", facet, value, on);
export const clearFilters = () => trigger(GROUP, "ClearFilters");
export const setSearch = (text: string) => trigger(GROUP, "SetSearch", text);
export const setSort = (sort: string) => trigger(GROUP, "SetSort", sort);
export const saveFilteredAsLibrary = (name: string) => trigger(GROUP, "SaveFilteredAsLibrary", name);
export const addFilteredToLibrary = (libraryId: string) => trigger(GROUP, "AddFilteredToLibrary", libraryId);
export const removeFilteredFromLibrary = () => trigger(GROUP, "RemoveFilteredFromLibrary");
export const exportLibrary = (id: string) => trigger(GROUP, "Export", id);
export const importAll = () => trigger(GROUP, "ImportAll");
export const exportContribution = () => trigger(GROUP, "ExportContribution");
export const rescan = () => trigger(GROUP, "Rescan");

// Community sharing. Mirrors Systems/CommunityUISystem.cs. The server is contacted only from
// the triggers below, never while these values are merely read.

export type CommunitySort = "popular" | "downloads" | "newest";
export type ReportReason = "spam" | "offensive" | "broken" | "other";

export interface CommunityItem {
  id: string;
  name: string;
  author: string;
  description: string;
  tags: string[];
  assetCount: number;
  packageCount: number;
  downloads: number;
}

export interface CommunityList {
  /** "unknown", "online" or "offline" */
  server: string;
  /** "idle", "loading", "ready" or "error" */
  state: string;
  /** "network", "timeout", "rate", "server" and the other codes of CommunityClient.Response */
  error: string;
  message: string;
  query: string;
  sort: CommunitySort;
  page: number;
  pageSize: number;
  total: number;
  items: CommunityItem[];
}

export interface CommunityPackage {
  id: number;
  title: string;
}

export interface CommunityDetail {
  /** "none", "loading", "ready" or "error" */
  state: string;
  error: string;
  message: string;
  /** "server" or "code" */
  source: string;
  id: string;
  name: string;
  author: string;
  description: string;
  tags: string[];
  assetCount: number;
  packageCount: number;
  downloads: number;
  updatedAt: string;
  installed: number;
  missing: number;
  scanned: boolean;
  /** The local library made from it, "" until imported. */
  importedId: string;
  reported: boolean;
  thumbs: string[];
  /** Missing PDX packages only. */
  packages: CommunityPackage[];
}

export interface CommunityAction {
  /** "publish", "unpublish", "import", "code", "copy", "report", "subscribe", "page" or "" */
  kind: string;
  /** "busy", "done", "error" or "" */
  state: string;
  error: string;
  message: string;
  libraryId: string;
  count: number;
}

export interface PdxStatus {
  available: boolean;
  loggedIn: boolean;
  name: string;
}

export interface ClipboardValue {
  seq: number;
  text: string;
}

const EMPTY_LIST: CommunityList = {
  server: "unknown",
  state: "idle",
  error: "",
  message: "",
  query: "",
  sort: "popular",
  page: 0,
  pageSize: 20,
  total: 0,
  items: [],
};

const EMPTY_DETAIL: CommunityDetail = {
  state: "none",
  error: "",
  message: "",
  source: "",
  id: "",
  name: "",
  author: "",
  description: "",
  tags: [],
  assetCount: 0,
  packageCount: 0,
  downloads: 0,
  updatedAt: "",
  installed: 0,
  missing: 0,
  scanned: false,
  importedId: "",
  reported: false,
  thumbs: [],
  packages: [],
};

const EMPTY_ACTION: CommunityAction = { kind: "", state: "", error: "", message: "", libraryId: "", count: 0 };

export const communityList$ = bindValue<CommunityList>(GROUP, "CommunityList", EMPTY_LIST);
export const communityDetail$ = bindValue<CommunityDetail>(GROUP, "CommunityDetail", EMPTY_DETAIL);
export const communityAction$ = bindValue<CommunityAction>(GROUP, "CommunityAction", EMPTY_ACTION);
export const pdxStatus$ = bindValue<PdxStatus>(GROUP, "PdxStatus", { available: false, loggedIn: false, name: "" });
export const published$ = bindValue<string[]>(GROUP, "Published", []);
export const clipboard$ = bindValue<ClipboardValue>(GROUP, "Clipboard", { seq: 0, text: "" });

export const communityOpen = () => trigger(GROUP, "CommunityOpen");
export const communitySearch = (query: string, sort: CommunitySort, page: number) =>
  trigger(GROUP, "CommunitySearch", query, sort, page);
export const communityOpenLibrary = (id: string) => trigger(GROUP, "CommunityOpenLibrary", id);
export const communityCloseLibrary = () => trigger(GROUP, "CommunityCloseLibrary");
export const communityImport = () => trigger(GROUP, "CommunityImport");
export const communityReport = (reason: ReportReason, note: string) => trigger(GROUP, "CommunityReport", reason, note);
export const communityPublish = (libraryId: string, description: string, tags: string) =>
  trigger(GROUP, "CommunityPublish", libraryId, description, tags);
export const communityUnpublish = (libraryId: string) => trigger(GROUP, "CommunityUnpublish", libraryId);
export const copyShareCode = (libraryId: string) => trigger(GROUP, "CopyShareCode", libraryId);
export const importShareCode = (code: string) => trigger(GROUP, "ImportShareCode", code);
export const readClipboard = () => trigger(GROUP, "ReadClipboard");
export const openPackagePage = (id: number) => trigger(GROUP, "OpenPackagePage", id);
export const subscribeMissing = () => trigger(GROUP, "SubscribeMissing");
export const clearCommunityAction = () => trigger(GROUP, "ClearCommunityAction");
export const communityRefreshAccount = () => trigger(GROUP, "CommunityRefreshAccount");
export const communityForgetPublished = (libraryId: string) => trigger(GROUP, "CommunityForgetPublished", libraryId);

// Mirrors Systems/PackageLoaderSystem.cs.

export type PackageState = "downloading" | "loading" | "loaded" | "failed";

export interface PackageStatus {
  id: number;
  state: PackageState;
}

export interface PackageLoader {
  /** False when the Paradox platform or the game's mod loader is not reachable. */
  available: boolean;
  /** Downloaded packages that are not loaded yet. */
  pending: number;
  /** "", "loading", "done" or "restart" */
  state: string;
  /** Packages that could not be loaded without a restart. */
  restart: number;
  items: PackageStatus[];
}

export const packageLoader$ = bindValue<PackageLoader>(GROUP, "PackageLoader", {
  available: false,
  pending: 0,
  state: "",
  restart: 0,
  items: [],
});

export const checkPendingPackages = () => trigger(GROUP, "CheckPendingPackages");
export const loadPendingPackages = () => trigger(GROUP, "LoadPendingPackages");
export const dismissPackageRestart = () => trigger(GROUP, "DismissPackageRestart");
export const exitApplication = () => trigger("app", "exitApplication");
