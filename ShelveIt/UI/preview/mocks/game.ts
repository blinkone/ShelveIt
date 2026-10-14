import { getValue, onTrigger, setValue } from "./api";

// The fake game side: a small copy of what Systems/ShelveItUISystem.cs does, working on
// the preview fixtures (synthetic by default, see tools/make-sample-fixtures.js). Only as much logic as the panel needs to show its
// states. It is not a specification of the C# behaviour.

const GROUP = "ShelveIt";
const PAGE_SIZE = 60;

interface FixtureAsset {
  key: string;
  entityIndex: number;
  name: string;
  kind: string;
  zone: string;
  density: string;
  region: string;
  theme: string;
  pack: string;
  isVanilla: boolean;
  pdxId: number;
  level: number;
  lotW: number;
  lotD: number;
  thumbKind: string;
  thumbnail: string;
  corner: boolean;
}

interface FixtureNode {
  id: string;
  parent: string;
  depth: number;
  name: string;
  group: boolean;
  keys: number[];
}

interface Fixtures {
  assets: FixtureAsset[];
  nodes: FixtureNode[];
  packages: Record<string, string>;
  regions: Record<string, string>;
}

interface Library {
  id: string;
  name: string;
  source: string;
  sourceId: string;
  items: Set<number>;
}

const cap = (text: string) => (text ? text[0].toUpperCase() + text.slice(1) : text);
const spaced = (text: string) => text.replace(/([a-z])([A-Z])/g, "$1 $2");

export function startGame(data: Fixtures, params: URLSearchParams) {
  const assets = data.assets;
  const nodes = data.nodes;
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const children = new Map<string, FixtureNode[]>();

  for (const node of nodes) {
    const list = children.get(node.parent) ?? [];

    list.push(node);
    children.set(node.parent, list);
  }

  const all = assets.map((_, index) => index);
  const libraries: Library[] = [];
  const dismissed = new Set<string>();
  const expanded = new Set<string>(["regions"]);
  const filters = new Map<string, Set<string>>();
  let search = "";
  let sort = "";
  let selected = "";
  let page = 0;
  let base: number[] = [];
  let filtered: number[] = [];
  let nextId = 1;

  const packageOf = (asset: FixtureAsset) => (asset.isVanilla ? "vanilla" : String(asset.pdxId));

  const FACETS: { id: string; value: (a: FixtureAsset) => string | null; label?: (v: string) => string; numeric?: boolean }[] = [
    { id: "kind", value: (a) => a.kind, label: spaced },
    { id: "zone", value: (a) => a.zone || null },
    { id: "density", value: (a) => a.density || null, label: cap },
    { id: "region", value: (a) => a.region || null, label: (v) => data.regions[v] ?? v.toUpperCase() },
    { id: "theme", value: (a) => a.theme || null, label: (v) => cap(v.replace(/[-_]/g, " ")) },
    { id: "pack", value: (a) => a.pack || null },
    { id: "source", value: (a) => (a.isVanilla ? "vanilla" : "custom"), label: (v) => (v === "vanilla" ? "Base game" : "Custom") },
    { id: "package", value: packageOf, label: (v) => (v === "vanilla" ? "Base game" : data.packages[v] ?? v) },
    { id: "level", value: (a) => (a.level > 0 ? String(a.level) : null), numeric: true },
    { id: "lotW", value: (a) => (a.lotW > 0 ? String(a.lotW) : null), numeric: true },
    { id: "lotD", value: (a) => (a.lotD > 0 ? String(a.lotD) : null), numeric: true },
    {
      id: "thumb",
      value: (a) => a.thumbKind,
      label: (v) => (v === "real" ? "Thumbnail" : v === "group" ? "Zone icon" : "No picture"),
    },
    { id: "corner", value: (a) => (a.corner ? "yes" : a.zone ? "no" : null), label: (v) => (v === "yes" ? "Corner lot" : "Not a corner") },
  ];

  const isLibrary = (id: string) => id !== "" && id !== "all" && !id.startsWith("suggestion:");
  const inAnyLibrary = (index: number) => libraries.some((library) => library.items.has(index));

  function addLibrary(name: string, source: string, sourceId: string, items: Iterable<number>) {
    const library = { id: "lib" + nextId++, name, source, sourceId, items: new Set(items) };

    libraries.push(library);

    return library;
  }

  function refreshLibraries() {
    setValue(
      GROUP,
      "Libraries",
      libraries.map((l) => ({ id: l.id, name: l.name, count: l.items.size, source: l.source, sourceId: l.sourceId }))
    );
  }

  function refreshTree() {
    const rows: unknown[] = [];
    const accepted = new Set(libraries.filter((l) => l.source === "suggested").map((l) => l.sourceId));
    const visible = (node: FixtureNode): boolean =>
      node.group ? (children.get(node.id) ?? []).some(visible) : !dismissed.has(node.id) && !accepted.has(node.id);

    const add = (node: FixtureNode) => {
      const kids = (children.get(node.id) ?? []).filter(visible);
      const open = expanded.has(node.id) && kids.length > 0;
      const thumbs: string[] = [];

      if (!node.group) {
        for (let i = 0; i < node.keys.length && i < 400 && thumbs.length < 4; i++) {
          const asset = assets[node.keys[i]];

          if (asset.thumbKind === "real" && !thumbs.includes(asset.thumbnail)) {
            thumbs.push(asset.thumbnail);
          }
        }
      }

      rows.push({
        id: node.id,
        parentId: node.parent,
        depth: node.depth,
        name: node.name,
        count: node.group ? kids.length : node.keys.length,
        group: node.group,
        hasChildren: kids.length > 0,
        expanded: open,
        thumbs,
      });

      if (open) {
        kids.forEach(add);
      }
    };

    if (params.get("suggestions") !== "none") {
      (children.get("") ?? []).filter(visible).forEach(add);
    }

    setValue(GROUP, "SuggestionTree", rows);
  }

  function buildBase(): number[] {
    if (selected === "all") {
      return params.get("scan") === "scanning" ? [] : all;
    }

    if (selected.startsWith("suggestion:")) {
      return byId.get(selected.substring(11))?.keys ?? [];
    }

    return [...(libraries.find((l) => l.id === selected)?.items ?? [])];
  }

  function matches(asset: FixtureAsset, skip?: string) {
    for (const [facetId, values] of filters) {
      if (facetId === skip) {
        continue;
      }

      const value = FACETS.find((f) => f.id === facetId)!.value(asset);

      if (value === null || !values.has(value)) {
        return false;
      }
    }

    return true;
  }

  function refreshView(rebuild: boolean) {
    if (rebuild) {
      base = buildBase();
    }

    const needle = search.toLowerCase();
    const searched = needle ? base.filter((i) => assets[i].name.toLowerCase().includes(needle)) : base;

    filtered = filters.size > 0 ? searched.filter((i) => matches(assets[i])) : searched.slice();

    if (sort === "name") {
      filtered.sort((a, b) => assets[a].name.localeCompare(assets[b].name));
    } else if (sort === "package") {
      filtered.sort((a, b) => packageOf(assets[a]).localeCompare(packageOf(assets[b])));
    } else if (sort === "lot") {
      filtered.sort((a, b) => assets[a].lotW * assets[a].lotD - assets[b].lotW * assets[b].lotD);
    } else if (sort === "newest") {
      filtered.sort((a, b) => assets[b].entityIndex - assets[a].entityIndex);
    }

    const facets = FACETS.map((facet) => {
      const counts = new Map<string, number>();
      const chosen = filters.get(facet.id);

      for (const i of searched) {
        const asset = assets[i];
        const value = facet.value(asset);

        if (value !== null && matches(asset, facet.id)) {
          counts.set(value, (counts.get(value) ?? 0) + 1);
        }
      }

      chosen?.forEach((value) => {
        if (!counts.has(value)) {
          counts.set(value, 0);
        }
      });

      const options = [...counts].map(([value, count]) => ({
        value,
        label: facet.label ? facet.label(value) : value,
        count,
        selected: chosen?.has(value) ?? false,
      }));

      options.sort((a, b) => (facet.numeric ? Number(a.value) - Number(b.value) : b.count - a.count));

      return { id: facet.id, total: options.length, options };
    });

    const active: unknown[] = [];

    for (const [facetId, values] of filters) {
      const facet = FACETS.find((f) => f.id === facetId)!;

      values.forEach((value) => active.push({ facet: facetId, value, label: facet.label ? facet.label(value) : value }));
    }

    setValue(GROUP, "TotalCount", base.length);
    setValue(GROUP, "AssetCount", filtered.length);
    setValue(GROUP, "Facets", facets);
    setValue(GROUP, "ActiveFilters", active);
    refreshPage();
  }

  function refreshPage() {
    const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
    const library = isLibrary(selected);

    page = Math.max(0, Math.min(page, pageCount - 1));

    setValue(GROUP, "Page", page);
    setValue(
      GROUP,
      "Assets",
      filtered.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE).map((i) => {
        const asset = assets[i];

        return {
          key: asset.key,
          entityIndex: asset.entityIndex,
          name: spaced(asset.name.replace(/_/g, " ")),
          thumbnail: asset.thumbnail,
          thumbKind: asset.thumbKind,
          kind: asset.kind,
          inLibrary: library || inAnyLibrary(i),
          zone: asset.zone,
          density: asset.density,
          level: asset.level,
          lotW: asset.lotW,
          lotD: asset.lotD,
          region: asset.region,
          family: "",
          package: asset.isVanilla ? "" : data.packages[String(asset.pdxId)] ?? "",
          isVanilla: asset.isVanilla,
        };
      })
    );
  }

  function select(id: string) {
    selected = id ?? "";
    filters.clear();
    search = "";
    page = 0;
    setValue(GROUP, "SelectedId", selected);
    setValue(GROUP, "Search", "");
    refreshView(true);
  }

  const indexOfKey = (key: string) => assets.findIndex((asset) => asset.key === key);

  onTrigger(GROUP, "TogglePanel", () => setValue(GROUP, "PanelVisible", !getValue<boolean>(GROUP, "PanelVisible")));
  onTrigger(GROUP, "SetPanelMode", (mode: string) => setValue(GROUP, "PanelMode", mode === "palette" ? "palette" : "manager"));
  onTrigger(GROUP, "Select", select);
  onTrigger(GROUP, "SetPage", (value: number) => {
    page = value;
    refreshPage();
  });
  onTrigger(GROUP, "SetFilter", (facet: string, value: string, on: boolean) => {
    const values = filters.get(facet) ?? new Set<string>();

    if (on) {
      values.add(value);
      filters.set(facet, values);
    } else {
      values.delete(value);

      if (values.size === 0) {
        filters.delete(facet);
      }
    }

    page = 0;
    refreshView(false);
  });
  onTrigger(GROUP, "ClearFilters", () => {
    filters.clear();
    search = "";
    page = 0;
    setValue(GROUP, "Search", "");
    refreshView(false);
  });
  onTrigger(GROUP, "SetSearch", (text: string) => {
    search = text.trim();
    page = 0;
    setValue(GROUP, "Search", search);
    refreshView(false);
  });
  onTrigger(GROUP, "SetSort", (value: string) => {
    sort = value;
    page = 0;
    setValue(GROUP, "Sort", sort);
    refreshView(false);
  });
  onTrigger(GROUP, "ToggleSuggestionNode", (id: string) => {
    if (!expanded.delete(id)) {
      expanded.add(id);
    }

    refreshTree();
  });
  onTrigger(GROUP, "AcceptSuggestion", (id: string) => {
    const node = byId.get(id);

    if (node && !node.group) {
      const parent = byId.get(node.parent);
      const library = addLibrary(parent && !parent.group ? parent.name + " / " + node.name : node.name, "suggested", id, node.keys);

      refreshLibraries();
      refreshTree();
      select(library.id);
    }
  });
  onTrigger(GROUP, "DismissSuggestion", (id: string) => {
    dismissed.add(id);
    refreshTree();

    if (selected === "suggestion:" + id) {
      select("");
    }
  });
  onTrigger(GROUP, "CreateLibrary", (name: string) => {
    const library = addLibrary(name, "user", "", []);

    refreshLibraries();
    select(library.id);
  });
  onTrigger(GROUP, "SaveFilteredAsLibrary", (name: string) => {
    const library = addLibrary(name, "user", "", filtered);

    refreshLibraries();
    select(library.id);
  });
  onTrigger(GROUP, "AddFilteredToLibrary", (id: string) => {
    const library = libraries.find((l) => l.id === id);

    filtered.forEach((i) => library?.items.add(i));
    refreshLibraries();
    refreshPage();
  });
  onTrigger(GROUP, "RemoveFilteredFromLibrary", () => {
    const library = libraries.find((l) => l.id === selected);

    filtered.forEach((i) => library?.items.delete(i));
    refreshLibraries();
    refreshView(true);
  });
  onTrigger(GROUP, "RenameLibrary", (id: string, name: string) => {
    const library = libraries.find((l) => l.id === id);

    if (library) {
      library.name = name;
      refreshLibraries();
    }
  });
  onTrigger(GROUP, "DeleteLibrary", (id: string) => {
    const index = libraries.findIndex((l) => l.id === id);

    if (index >= 0) {
      libraries.splice(index, 1);
      refreshLibraries();
      refreshTree();
      select("");
    }
  });
  onTrigger(GROUP, "AddAsset", (id: string, key: string) => {
    libraries.find((l) => l.id === id)?.items.add(indexOfKey(key));
    refreshLibraries();
    refreshPage();
  });
  onTrigger(GROUP, "RemoveAsset", (id: string, key: string) => {
    libraries.find((l) => l.id === id)?.items.delete(indexOfKey(key));
    refreshLibraries();
    refreshView(true);
  });
  onTrigger(GROUP, "PlaceAsset", (entityIndex: number) => console.info("[preview] place asset", entityIndex));

  // Starting state, chosen with query parameters so every state has a URL.
  const pick = (id: string) => byId.get(id)?.keys ?? [];

  if (params.get("libraries") !== "none") {
    addLibrary("UK / Residential medium", "suggested", "region:uk/Residential medium", pick("region:uk/Residential medium"));
    addLibrary("Medium Old Town", "user", "", all.filter((i) => /OldTown/.test(assets[i].name)).slice(0, 212));
    addLibrary("Historic industry", "suggested", "x", all.filter((i) => /historic/i.test(assets[i].name) && assets[i].zone === "Industrial"));
    addLibrary("Dutch row houses", "suggested", "region:nl/Residential row", pick("region:nl/Residential row"));
    addLibrary("Street furniture and signs", "user", "", all.filter((i) => assets[i].kind === "Prop" && /sign|bench|lamp/i.test(assets[i].name)).slice(0, 340));
    addLibrary("Decals, road wear", "user", "", all.filter((i) => assets[i].kind === "Decal").slice(0, 96));
    addLibrary("Old favourites", "legacy", "", all.filter((i) => assets[i].kind === "UniqueBuilding").slice(0, 41));
    addLibrary("New harbour district", "user", "", []);
  }

  if (params.get("expand")) {
    params.get("expand")!.split(",").forEach((id) => expanded.add(id));
  }

  setValue(GROUP, "PanelVisible", params.get("closed") === null);
  setValue(GROUP, "PanelMode", params.get("mode") === "palette" ? "palette" : "manager");
  setValue(GROUP, "ScanState", params.get("scan") ?? "ready");
  setValue(GROUP, "FindItAvailable", params.get("findit") !== "0");
  setValue(GROUP, "PageSize", PAGE_SIZE);
  setValue(GROUP, "Sort", "");
  setValue("FindIt", "ShowFindItPanel", false);

  refreshLibraries();
  refreshTree();

  const wanted = params.get("select") ?? "all";
  const byName = libraries.find((l) => l.name === wanted);

  select(byName ? byName.id : wanted === "none" ? "" : wanted);

  for (const entry of (params.get("filter") ?? "").split(",")) {
    const [facet, value] = entry.split(":");

    if (facet && value) {
      const values = filters.get(facet) ?? new Set<string>();

      values.add(value);
      filters.set(facet, values);
    }
  }

  if (params.get("search")) {
    search = params.get("search")!;
    setValue(GROUP, "Search", search);
  }

  if (filters.size > 0 || search) {
    refreshView(false);
  }

  if (params.get("page")) {
    page = Number(params.get("page"));
    refreshPage();
  }
}
