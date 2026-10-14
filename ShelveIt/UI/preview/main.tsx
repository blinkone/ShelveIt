import React from "react";
import { createRoot } from "react-dom/client";
import { LibraryPanelHost } from "mods/LibraryPanel/LibraryPanel";
import { ToolbarButton } from "mods/ToolbarButton/ToolbarButton";
import { resolveVanillaComponents } from "vanilla";
import { startCommunity } from "./mocks/community";
import { startGame } from "./mocks/game";
import { registry } from "./mocks/modding";
import { fixUrl } from "./mocks/ui";

// Browser preview of the panel, outside the game. It approximates the game's UI engine:
// Chrome lays out and paints differently from Coherent Gameface, and the cs2 modules are mocks.
//
// Query parameters:
//   mode=palette            start in the palette
//   theme=bright-blue | dark-grey-orange     the game's interface styles (default: default)
//   legacy=1                the legacy interface option
//   select=all | none | <library name> | suggestion:<node id>
//   filter=zone:Residential,region:uk        search=...      page=3      filters=open
//   scan=scanning | classifying              libraries=none   suggestions=none
//   findit=0   uil=0   registry=empty   locale=it   expand=<node id>,<node id>
//   scale=1.25              the game's text scale setting
//   community=offline | rate | empty | detail | code | rejected     pdx=out | none   (see mocks/community.ts)
//   shots=1   thumbs=kind   reveal=<tree row text>   published=lib2   (store screenshots, with ghosts=0)
//   click=<button text>     hover=<tile number>     ghosts=0     backdrop=<Glossary image name>

const params = new URLSearchParams(location.search);

// Icons of the Unified Icon Library are served under /uil/ here. uil=0 pretends it is missing.
const srcProperty = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, "src")!;

Object.defineProperty(HTMLImageElement.prototype, "src", {
  ...srcProperty,
  set(value: string) {
    const uil = typeof value === "string" && value.startsWith("coui://uil/");

    srcProperty.set!.call(this, uil ? (params.get("uil") === "0" ? "/uil/missing.svg" : fixUrl(value)) : value);
  },
});

// What the game does to the document for its interface options.
document.body.classList.add("style--" + (params.get("theme") ?? "default"));
document.body.classList.add(params.get("legacy") === "1" ? "legacy-interface" : "overwrite-legacy");

if (params.get("scale")) {
  const scale = Number(params.get("scale"));

  document.documentElement.style.setProperty("--fontScale", String(scale));
  document.documentElement.style.setProperty("--fontScaleChange", String(scale - 1));
}

document.getElementById("backdrop")!.style.backgroundImage = `url("/game/Media/Game/Glossary/${
  params.get("backdrop") ?? "BusStation"
}.png")`;

// Outlines of what the game puts at the bottom of the screen, to judge where the palette sits.
// Sizes come from the game's stylesheet: two toolbar rows of --toolbarHeight, and the build menu
// (tab row, then two rows of nine 72rem tiles).
const Ghosts = () => (
  <>
    <div className="pv-ghost" style={{ left: 0, right: 0, bottom: 0, height: "100rem" }}>
      vanilla toolbar (2 x 50rem)
    </div>
    <div
      className="pv-ghost"
      style={{ left: "50%", marginLeft: "-353rem", width: "706rem", bottom: "106rem", height: "206rem" }}
    >
      vanilla build menu, and Find It when it is open (outline, not to scale for Find It)
    </div>
  </>
);

const App = () => (
  <>
    {params.get("ghosts") !== "0" && <Ghosts />}
    <div className="pv-topleft">
      <ToolbarButton />
    </div>
    <LibraryPanelHost />
  </>
);

// thumbs=kind: the fixtures give custom assets a random game icon as stand-in thumbnail, so a
// house can show a fork and knife. For store screenshots, pick a stand-in that matches the kind
// of asset instead (zone and density icon for buildings, prop and surface icons for props and decals).
type StandIn = { name: string; kind: string; zone: string; density: string; thumbKind: string; thumbnail: string };

function kindThumbnails(assets: StandIn[]) {
  const zoneIcons = new Set([
    "ResidentialLow", "ResidentialMedium", "ResidentialHigh", "ResidentialMixed", "ResidentialMediumRow",
    "CommercialLow", "CommercialHigh", "OfficeLow", "OfficeHigh",
  ]);
  const props = ["Props", "PropsCommercial", "PropsIndustrial", "PropsPark", "PropsResidential", "PropsStone", "PropsDynamic"];
  const decals = ["PropsDecals", "SurfaceConcrete01", "SurfacePavement01", "SurfaceTiles01", "SurfaceGrass01", "SurfaceSand01"];
  const hash = (s: string) => Array.from(s).reduce((h, c) => (h * 31 + c.charCodeAt(0)) | 0, 7) >>> 0;
  const cap = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

  for (const asset of assets) {
    if (asset.thumbKind !== "real" || asset.thumbnail === "Media/Game/Icons/" + asset.name + ".svg") {
      continue;
    }

    let icon = "";

    if (asset.zone) {
      const withDensity = asset.zone + cap(asset.density || "");

      icon = "Zone" + (zoneIcons.has(withDensity) ? withDensity : asset.zone);
    } else if (asset.kind === "Prop") {
      icon = /tree|plant|flower|bush|hedge/i.test(asset.name) ? "PropsPlants" : props[hash(asset.name) % props.length];
    } else if (asset.kind === "Decal") {
      icon = decals[hash(asset.name) % decals.length];
    }

    if (icon) {
      asset.thumbKind = "group";
      asset.thumbnail = "Media/Game/Icons/" + icon + ".svg";
    }
  }
}

async function start() {
  const response = await fetch("/data/fixtures.json");

  resolveVanillaComponents(registry as never);
  const data = await response.json();

  if (params.get("thumbs") === "kind") {
    kindThumbnails(data.assets);
  }

  startGame(data, params);
  startCommunity(
    params,
    params.get("thumbs") === "kind"
      ? ["ZoneEUMixedOldTown", "ZoneEUResidentialLowOldTown", "ZoneResidentialMediumRow", "PropsResidential"].map(
          (icon) => "Media/Game/Icons/" + icon + ".svg"
        )
      : (data.assets as { thumbKind: string; thumbnail: string }[]).filter((a) => a.thumbKind === "real").map((a) => a.thumbnail)
  );
  createRoot(document.getElementById("root")!).render(<App />);

  // click=<button text> opens a menu; hover=<n> shows the tooltip and the action of the nth tile.
  // Headless screenshots have no pointer, so these stand in for one.
  window.setTimeout(() => {
    const wanted = params.get("click");

    if (wanted) {
      Array.from(document.querySelectorAll("button"))
        .find((button) => (button.textContent ?? "").startsWith(wanted))
        ?.click();
    }

    // reveal=<row text> scrolls the tree so that row is at the top.
    const reveal = params.get("reveal");

    if (reveal) {
      Array.from(document.querySelectorAll<HTMLElement>("div"))
        .filter((row) => (row.textContent ?? "").startsWith(reveal) && row.children.length <= 4)
        .pop()
        ?.scrollIntoView({ block: "start" });
    }

    const hover = params.get("hover");

    if (hover) {
      const tiles = Array.from(document.querySelectorAll<HTMLElement>('[class*="tile_"]')).filter((tile) =>
        /(^| )tile_/.test(tile.className)
      );
      const tile = tiles[Number(hover)];

      if (tile) {
        tile.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
        tile.querySelectorAll<HTMLElement>('[class*="tile-button"]').forEach((button) => (button.style.display = "flex"));
      }
    }
  }, 900);

  if (params.get("filters") === "open") {
    // The filter area remembers that it was open; clicking is the only way in from outside.
    window.setTimeout(() => {
      const buttons = Array.from(document.querySelectorAll("button"));
      const byIcon = buttons.find((button) => button.querySelector('[style*="FunnelFilter"]'));

      (byIcon ?? buttons.find((button) => /^(Filters|Filtri)/.test(button.textContent ?? "")))?.click();

      if (params.get("more") === "1") {
        window.setTimeout(() => {
          const rows = Array.from(document.querySelectorAll("div"));

          rows.find((row) => /^(More filters|Altri filtri)/.test(row.textContent ?? "") && row.children.length <= 3)?.click();
        }, 200);
      }
    }, 400);
  }
}

start();
