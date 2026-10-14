// Builds synthetic preview fixtures: invented asset names, packages and creators, no scan needed.
// Thumbnails are icons from the installed game, so the preview looks like the real panel.
// usage (from the UI folder): node preview/tools/make-sample-fixtures.js ["<game UI folder>"] [out.json]
//   default game UI folder: $GAME_UI, else $CS2_INSTALL/Cities2_Data/Content/Game/UI,
//   else the standard Steam install path. Default output: preview/data/fixtures.json
const fs = require("fs");
const path = require("path");

const install = process.env.CS2_INSTALL || "C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II";
const gameUi = process.argv[2] || process.env.GAME_UI || path.join(install, "Cities2_Data", "Content", "Game", "UI");
const outFile = process.argv[3] || path.join(__dirname, "..", "data", "fixtures.json");

let icons = [];
try {
  icons = fs.readdirSync(path.join(gameUi, "Media", "Game", "Icons")).filter((f) => f.endsWith(".svg"));
} catch {
  console.warn("Game icons not found in " + gameUi + ": tiles will use generated thumbnails.");
}

// Small deterministic random generator, so every run gives the same data.
let seed = 20261014;
const rnd = () => ((seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff);
const pick = (list) => list[Math.floor(rnd() * list.length)];

const REGIONS = { uk: "United Kingdom", fr: "France", de: "Germany", nl: "Netherlands", jp: "Japan", na: "North America", eu: "Europe" };
const ZONES = ["Residential", "Commercial", "Industrial", "Office"];
const DENSITIES = ["low", "medium", "high", "row"];
const STYLES = ["OldTown", "Modern", "Historic", "Brick", "Glass", "Suburban", "Canal", "Terrace"];
const PROPS = ["Bench", "Lamp", "Sign", "Bin", "Planter", "Fence", "Bollard", "Kiosk"];
const DECALS = ["RoadWear", "Puddle", "Graffiti", "Crack", "Leaves"];
const CREATORS = ["Brickworks", "Kei", "Rustbelt", "PropMaster", "Grachtenpand", "SkylineSam", "Matterhorn", "Alfie"];

const packages = {};
CREATORS.forEach((creator, i) => (packages[String(90000 + i * 17)] = creator + " " + pick(["Collection", "Pack", "Set", "Vol. 2"])));
const packageIds = Object.keys(packages).map(Number);

const assets = [];
const add = (fields) => {
  const index = assets.length;
  const thumbKind = icons.length && rnd() < 0.6 ? "real" : fields.zone && rnd() < 0.5 ? "group" : "none";
  const thumbnail =
    thumbKind === "real" ? "Media/Game/Icons/" + pick(icons) : thumbKind === "group" ? "Media/Game/Icons/Zone" + fields.zone + ".svg" : "";
  assets.push({
    key: fields.kind + "Prefab:" + fields.name,
    entityIndex: 1000 + index,
    zone: "",
    density: "",
    region: "",
    theme: "",
    pack: "",
    level: 1,
    lotW: 0,
    lotD: 0,
    corner: false,
    thumbKind,
    thumbnail,
    ...fields,
  });
};

for (let i = 0; i < 900; i++) {
  const region = pick(Object.keys(REGIONS));
  const zone = pick(ZONES);
  const density = pick(DENSITIES);
  const style = pick(STYLES);
  const vanilla = rnd() < 0.25;
  const pdxId = vanilla ? 0 : pick(packageIds);
  add({
    name: `${region.toUpperCase()}_${style}_${zone}_${density}_${String(i).padStart(3, "0")}`,
    kind: rnd() < 0.05 ? "UniqueBuilding" : "Building",
    zone,
    density,
    region,
    theme: style === "Historic" ? "historic" : style === "Glass" ? "contemporary-glass" : "",
    pack: rnd() < 0.2 ? region.toUpperCase() : "",
    isVanilla: vanilla,
    pdxId,
    level: 1 + Math.floor(rnd() * 5),
    lotW: 1 + Math.floor(rnd() * 4),
    lotD: 2 + Math.floor(rnd() * 5),
    corner: rnd() < 0.1,
  });
}
for (let i = 0; i < 400; i++) {
  add({ name: `${pick(PROPS)}_${pick(STYLES)}_${String(i).padStart(3, "0")}`, kind: "Prop", isVanilla: rnd() < 0.3, pdxId: pick(packageIds), region: rnd() < 0.5 ? pick(Object.keys(REGIONS)) : "" });
}
for (let i = 0; i < 150; i++) {
  add({ name: `Decal_${pick(DECALS)}_${String(i).padStart(3, "0")}`, kind: "Decal", isVanilla: false, pdxId: pick(packageIds) });
}
assets.forEach((a) => a.isVanilla && (a.pdxId = 0));

// Suggestion tree with the three root groups of the mod.
const nodes = [];
const node = (id, parent, depth, name, group, keys) => (nodes.push({ id, parent, depth, name, group, keys }), id);
const groupBy = (list, f) => {
  const m = new Map();
  list.forEach((i) => {
    const k = f(assets[i]);
    if (k) (m.get(k) || m.set(k, []).get(k)).push(i);
  });
  return m;
};
const all = assets.map((_, i) => i);
const MIN = 5;

node("regions", "", 0, "Regions and styles", true, []);
for (const [region, list] of groupBy(all, (a) => a.region)) {
  const id = node("region:" + region, "regions", 1, REGIONS[region], false, list);
  for (const [combo, sub] of groupBy(list, (a) => (a.zone ? a.zone + " " + a.density : a.kind === "Prop" ? "Props" : ""))) {
    if (sub.length >= MIN) node(id + "/" + combo, id, 2, combo, false, sub);
  }
}
node("packs", "", 0, "Creator packs", true, []);
for (const [pdx, list] of groupBy(all, (a) => (a.pdxId ? String(a.pdxId) : ""))) {
  const id = node("package:" + pdx, "packs", 1, packages[pdx], false, list);
  for (const [kind, sub] of groupBy(list, (a) => a.zone || a.kind)) {
    if (sub.length >= MIN && sub.length < list.length) node(id + "/" + kind, id, 2, kind, false, sub);
  }
}
node("sets", "", 0, "Themed sets", true, []);
for (const [theme, list] of groupBy(all, (a) => a.theme)) node("theme:" + theme, "sets", 1, theme.replace(/-/g, " "), false, list);

fs.mkdirSync(path.dirname(outFile), { recursive: true });
fs.writeFileSync(outFile, JSON.stringify({ assets, nodes, packages, regions: REGIONS }));
console.log(assets.length + " synthetic assets, " + nodes.length + " tree nodes -> " + outFile);
