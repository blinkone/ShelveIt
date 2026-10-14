// Builds the preview fixtures from a local scan (ModsData/ShelveIt/scan-dump.json, written by the mod).
// The output reflects your own installed assets: keep it in preview/data (git-ignored), never commit it.
// For shareable data use make-sample-fixtures.js instead.
// The scan has no thumbnail URLs or package titles, so those are stand-ins: game icons play the
// part of thumbnails and package names are derived from the asset names.
// usage: node make-fixtures.js "<ModsData/ShelveIt>" "<game UI folder>" <out.json>
const fs = require("fs");
const path = require("path");

const [dataDir, gameUi, outFile] = process.argv.slice(2);
const scan = JSON.parse(fs.readFileSync(path.join(dataDir, "scan-dump.json"), "utf8"));
const cache = JSON.parse(fs.readFileSync(path.join(dataDir, "cache.json"), "utf8"));
const iconDir = path.join(gameUi, "Media", "Game", "Icons");
const icons = fs.readdirSync(iconDir).filter((f) => f.endsWith(".svg"));
const iconSet = new Set(icons);

const hash = (text) => {
  let h = 0;
  for (let i = 0; i < text.length; i++) h = (h * 31 + text.charCodeAt(i)) | 0;
  return Math.abs(h);
};
const cap = (s) => (s ? s[0].toUpperCase() + s.slice(1) : s);
const top = (list) => {
  const m = new Map();
  for (const v of list) if (v) m.set(v, (m.get(v) || 0) + 1);
  let best = "", n = 0;
  for (const [k, c] of m) if (c > n) { best = k; n = c; }
  return best;
};

const REGIONS = { uk: "United Kingdom", fr: "France", de: "Germany", nl: "Netherlands", ee: "Eastern Europe", jp: "Japan", chi: "China", cn: "China", sw: "US Southwest", ne: "US Northeast", na: "North America", eu: "Europe" };
const DENSITIES = ["low", "medium", "high", "row", "mixed"];

const themeOf = new Map();
for (const theme of cache.themes) for (const key of theme.keys) if (!themeOf.has(key)) themeOf.set(key, theme.id);

const byPackage = new Map();
scan.forEach((r) => { if (r.pdxId) (byPackage.get(r.pdxId) || byPackage.set(r.pdxId, []).get(r.pdxId)).push(r); });
const packageTitle = new Map();
for (const [id, list] of byPackage) {
  const pack = top(list.map((r) => (r.packs[0] || "").replace(/ Pack Filter$/, "")));
  const creator = cap(top(list.map((r) => r.tokens[0])));
  const second = cap(top(list.map((r) => r.tokens[1])));
  packageTitle.set(id, pack && list.filter((r) => r.packs.length).length > list.length / 2 ? pack : (creator + " " + second).trim());
}

const assets = scan
  .filter((r) => r.kind !== "Other" && r.kind !== "Vehicle")
  .map((r) => {
    const tokens = r.tokens || [];
    let region = "";
    const packRegion = (r.packs[0] || "").match(/^([A-Z]{2}) Pack Filter$/);
    if (packRegion) region = packRegion[1].toLowerCase();
    else region = tokens.find((t) => REGIONS[t]) || (r.theme === "North American" ? "na" : r.theme === "European" ? "eu" : "");
    const zone = r.zone === "None" ? "" : r.zone;
    const density = zone ? tokens.find((t) => DENSITIES.includes(t)) || "" : "";
    const h = hash(r.key);
    let thumbKind = "none", thumb = "";
    if (iconSet.has(r.name + ".svg")) { thumbKind = "real"; thumb = "Media/Game/Icons/" + r.name + ".svg"; }
    else if (r.isVanilla && zone) { thumbKind = "group"; thumb = "Media/Game/Icons/Zone" + zone + ".svg"; }
    else if (h % 100 < 55) { thumbKind = "real"; thumb = "Media/Game/Icons/" + icons[h % icons.length]; }
    else if (h % 100 < 72 && zone) { thumbKind = "group"; thumb = "Media/Game/Icons/Zone" + zone + ".svg"; }
    return {
      key: r.key, entityIndex: r.entityIndex, name: r.name, kind: r.kind, zone, density, region,
      theme: themeOf.get(r.key) || "", pack: (r.packs[0] || "").replace(/ Pack Filter$/, ""),
      isVanilla: r.isVanilla, pdxId: r.pdxId, level: r.level, lotW: r.lotW, lotD: r.lotD,
      thumbKind, thumbnail: thumb, corner: tokens.includes("corner"),
    };
  });

// Suggestion tree with the three root groups of the mod.
const nodes = [];
const add = (id, parent, depth, name, group, keys) => { nodes.push({ id, parent, depth, name, group, keys }); return id; };
const index = new Map(assets.map((a, i) => [a.key, i]));
const groupBy = (list, f) => { const m = new Map(); list.forEach((i) => { const k = f(assets[i]); if (k) (m.get(k) || m.set(k, []).get(k)).push(i); }); return m; };
const all = assets.map((_, i) => i);
const MIN = 5;

add("regions", "", 0, "Regions and styles", true, []);
for (const [region, list] of [...groupBy(all, (a) => a.region)].sort((a, b) => b[1].length - a[1].length)) {
  if (list.length < MIN) continue;
  const id = add("region:" + region, "regions", 1, REGIONS[region] || region.toUpperCase(), false, list);
  for (const [combo, sub] of groupBy(list, (a) => (a.zone ? a.zone + (a.density ? " " + a.density : "") : a.kind === "Prop" ? "Props" : a.kind === "Decal" ? "Decals" : ""))) {
    if (sub.length >= MIN && sub.length < list.length) add(id + "/" + combo, id, 2, combo, false, sub);
  }
}
add("packs", "", 0, "Creator packs", true, []);
const custom = all.filter((i) => assets[i].pdxId);
for (const [pdx, list] of [...groupBy(custom, (a) => String(a.pdxId))].sort((a, b) => b[1].length - a[1].length)) {
  if (list.length < MIN) continue;
  const id = add("package:" + pdx, "packs", 1, packageTitle.get(Number(pdx)), false, list);
  for (const [kind, sub] of groupBy(list, (a) => (a.zone ? a.zone + (a.density ? " " + a.density : "") : a.kind))) {
    if (sub.length >= MIN && sub.length < list.length) add(id + "/" + kind, id, 2, kind.replace(/([a-z])([A-Z])/g, "$1 $2"), false, sub);
  }
}
add("sets", "", 0, "Themed sets", true, []);
for (const theme of cache.themes) {
  const list = theme.keys.map((k) => index.get(k)).filter((i) => i !== undefined);
  if (list.length >= MIN) add("theme:" + theme.id, "sets", 1, cap(theme.id.replace(/[-_]/g, " ")), false, list);
}
for (const [pack, list] of groupBy(all, (a) => a.pack)) if (list.length >= MIN) add("pack:" + pack, "sets", 1, pack, false, list);

const packages = {};
for (const [id, title] of packageTitle) packages[id] = title;

fs.writeFileSync(outFile, JSON.stringify({ assets, nodes, packages, regions: REGIONS }));
console.log(assets.length + " assets, " + nodes.length + " tree nodes, " + Object.keys(packages).length + " packages, " + (fs.statSync(outFile).size / 1e6).toFixed(1) + " MB");
