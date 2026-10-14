import { getValue, onTrigger, setValue } from "./api";

// Fake community server and Paradox account for the preview. Query parameters:
//   click=Community                     open the Community tab
//   community=offline | rate | empty | detail | code | rejected     start state of the tab
//   pdx=out | none                      signed out of Paradox, or Paradox Mods unavailable
//   click=Share                         open the share bar of the selected library

const GROUP = "ShelveIt";

const NAMES = [
  ["Medium Old Town, complete", "Brickworks", ["european", "residential", "old town"]],
  ["UK terraces and corner shops", "Alfie_Builds", ["british", "residential"]],
  ["Dutch canal houses", "Grachtenpand", ["dutch", "row houses"]],
  ["Harbour industry 1900", "Rustbelt", ["industry", "historic"]],
  ["Japanese suburbs", "Kei", ["japanese", "low density"]],
  ["Street furniture essentials", "PropMaster", ["props"]],
  ["Alpine village", "Matterhorn", ["swiss", "rural"]],
  ["American downtown towers", "SkylineSam", ["american", "high density", "office"]],
] as [string, string, string[]][];

const DESCRIPTIONS = [
  "Curated set with matching facades, corner lots and a few landmarks. Works with the base game roads.",
  "Red brick terraces, end-of-terrace corner shops and the props to dress a high street.",
  "",
  "Warehouses, cranes and brick factories for a waterfront from the turn of the century.",
  "Low houses, small shops and narrow-street props for quiet residential blocks.",
  "Benches, bins, bollards and signs that fit most European styles.",
  "",
  "Glass and stone office towers with plazas and street-level retail.",
];

export function startCommunity(params: URLSearchParams, thumbs: string[]) {
  const mode = params.get("community") ?? "";
  const pdx = params.get("pdx");
  // published=lib2,lib5 starts with those libraries already shared on the server.
  const published = new Set<string>((params.get("published") ?? "").split(",").filter((id) => id !== ""));

  const items = NAMES.map(([name, author, tags], index) => ({
    id: "lib" + (index + 1),
    name,
    author,
    description: DESCRIPTIONS[index] ?? "",
    tags,
    assetCount: [212, 188, 154, 302, 175, 96, 143, 260][index] ?? 40 + index * 37,
    packageCount: [7, 4, 3, 6, 5, 2, 4, 6][index] ?? 2 + (index % 5),
    downloads: 1834 - index * 211,
  }));

  const list = (patch: object) => setValue(GROUP, "CommunityList", { ...getValue<object>(GROUP, "CommunityList"), ...patch });
  const action = (kind: string, state: string, extra: object = {}) =>
    setValue(GROUP, "CommunityAction", { kind, state, error: "", message: "", libraryId: "", count: 0, ...extra });

  const detailOf = (item: (typeof items)[number], source: string, importedId = "") => ({
    state: "ready",
    error: "",
    message: "",
    source,
    id: item.id,
    name: item.name,
    author: item.author,
    description: item.description,
    tags: item.tags,
    assetCount: item.assetCount,
    packageCount: item.packageCount,
    downloads: item.downloads,
    updatedAt: "2026-10-02T14:31:00Z",
    installed: item.assetCount - Math.round(item.assetCount * 0.21),
    missing: Math.round(item.assetCount * 0.21),
    scanned: true,
    importedId,
    reported: false,
    thumbs: thumbs.slice(0, 4),
    packages: [
      { id: 81234, title: "Old Town Facades Vol. 2" },
      { id: 79011, title: "Corner Shops Pack" },
      { id: 90455, title: "Victorian Street Lamps" },
      { id: 86120, title: "Cobblestone Decals" },
      { id: 93377, title: "Canal Bridges and Quays" },
    ].slice(0, Math.max(1, item.packageCount - 2)),
  });

  setValue(GROUP, "PdxStatus", {
    available: pdx !== "none",
    loggedIn: pdx !== "none" && pdx !== "out",
    name: pdx === "none" || pdx === "out" ? "" : params.get("shots") === "1" ? "CityPlanner" : "PreviewUser",
  });
  setValue(GROUP, "Published", Array.from(published));

  const search = (query: string, sort: string, page: number) => {
    if (mode === "offline") {
      list({ server: "offline", state: "error", error: "network", message: "Cannot resolve destination host", query, sort, page });
      return;
    }

    if (mode === "rate") {
      list({ server: "online", state: "error", error: "rate", message: "Too many requests", query, sort, page });
      return;
    }

    const found = mode === "empty" ? [] : items.filter((x) => x.name.toLowerCase().includes(query.toLowerCase()));

    list({ server: "online", state: "loading", query, sort, page });
    window.setTimeout(
      () => list({ server: "online", state: "ready", error: "", items: found, total: found.length === 0 ? 0 : 47, page, pageSize: 20 }),
      150
    );
  };

  onTrigger(GROUP, "CommunityOpen", () => {
    const current = getValue<{ state: string; query: string; sort: string; page: number }>(GROUP, "CommunityList");

    if (current.state === "idle") {
      search(current.query, current.sort, current.page);
    }

    if (mode === "detail" || mode === "rejected") {
      setValue(GROUP, "CommunityDetail", detailOf(items[0], "server"));
    } else if (mode === "code") {
      setValue(GROUP, "CommunityDetail", { ...detailOf(items[1], "code", "lib2"), author: "" });
      action("code", "done", { libraryId: "lib2", count: items[1].assetCount });
    }
  });
  onTrigger(GROUP, "CommunitySearch", search);
  onTrigger(GROUP, "CommunityOpenLibrary", (id: string) => {
    const item = items.find((x) => x.id === id);

    if (item) {
      setValue(GROUP, "CommunityDetail", detailOf(item, "server"));
    }
  });
  onTrigger(GROUP, "CommunityCloseLibrary", () =>
    setValue(GROUP, "CommunityDetail", { ...getValue<object>(GROUP, "CommunityDetail"), state: "none" })
  );
  onTrigger(GROUP, "CommunityImport", () => {
    const detail = getValue<{ assetCount: number }>(GROUP, "CommunityDetail");

    setValue(GROUP, "CommunityDetail", { ...detail, importedId: "lib1" });
    action("import", "done", { libraryId: "lib1", count: detail.assetCount });
  });
  onTrigger(GROUP, "CommunityReport", () => {
    setValue(GROUP, "CommunityDetail", { ...getValue<object>(GROUP, "CommunityDetail"), reported: true });
    action("report", "done");
  });
  onTrigger(GROUP, "CommunityPublish", (libraryId: string) => {
    if (mode === "rejected") {
      action("publish", "error", { error: "rejected", message: "The name contains a word that is not allowed.", libraryId });
      return;
    }

    published.add(libraryId);
    setValue(GROUP, "Published", Array.from(published));
    action("publish", "done", { libraryId, count: 212 });
  });
  onTrigger(GROUP, "CommunityUnpublish", (libraryId: string) => {
    published.delete(libraryId);
    setValue(GROUP, "Published", Array.from(published));
    action("unpublish", "done", { libraryId });
  });
  onTrigger(GROUP, "CommunityForgetPublished", (libraryId: string) => {
    published.delete(libraryId);
    setValue(GROUP, "Published", Array.from(published));
    action("forget", "done", { libraryId });
  });
  onTrigger(GROUP, "CopyShareCode", (libraryId: string) => action("copy", "done", { libraryId, count: 212 }));
  onTrigger(GROUP, "ImportShareCode", (code: string) => {
    if (!code.trim().toUpperCase().startsWith("SHELVE1:")) {
      action("code", "error", { error: "code-format" });
      return;
    }

    setValue(GROUP, "CommunityDetail", { ...detailOf(items[1], "code", "lib2"), author: "" });
    action("code", "done", { libraryId: "lib2", count: items[1].assetCount });
  });
  onTrigger(GROUP, "ReadClipboard", () => {
    const current = getValue<{ seq: number }>(GROUP, "Clipboard");

    setValue(GROUP, "Clipboard", { seq: current.seq + 1, text: "SHELVE1:H4sIAAAAAAAAA6tWKkotLs3JUbJSMjQwMjI1N1KqBQBH3n8SEwAAAA" });
  });
  onTrigger(GROUP, "OpenPackagePage", (id: number) => console.info("[preview] open package page", id));
  onTrigger(GROUP, "SubscribeMissing", () => {
    const detail = getValue<{ packages: unknown[] }>(GROUP, "CommunityDetail");

    action("subscribe", "done", { count: detail.packages.length });
  });
  onTrigger(GROUP, "ClearCommunityAction", () => action("", ""));
  onTrigger(GROUP, "CommunityRefreshAccount", () => undefined);
}
