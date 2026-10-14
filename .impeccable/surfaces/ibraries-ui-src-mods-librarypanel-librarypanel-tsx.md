---
version: 1
slug: "ibraries-ui-src-mods-librarypanel-librarypanel-tsx"
primary_target: "ShelveIt/UI/src/mods/LibraryPanel/LibraryPanel.tsx"
related_targets: ["ShelveIt/UI/src/mods/LibraryPanel/panel.module.scss"]
---

# Library panel (Manager and Palette)

Scope: the in-game panel under `ShelveIt/UI/src/mods/LibraryPanel`, plus the toolbar button. Visitor mode: Operate.

Audience and job: a detailer with tens of thousands of assets curates thematic libraries (Manager) and places assets from one library while building (Palette). Constraints: Coherent Gameface (flexbox only, longhands with variables), no work while closed, pages of 60 tiles, a suggestion tree of about 1,300 nodes, English and Italian.

## Direction contract

THESIS: the panel is a vanilla Cities: Skylines II panel. It refuses the web-dashboard arrangement (cards, custom palette, custom controls) and uses the game's Panel, title bar, dropdown, scrollbars, tooltips, asset-menu tiles and tokens.

OWN-WORLD: the game's own system, version 1.6.2f1. Dark translucent blue panel surfaces (`--panelColorDark`, `--panelColorNormal`, panel gradient), cyan accent (`--accentColorNormal`, `--selectedColor`), Overpass through `--fontFamily`, rem sizes that follow the UI scale, tool-button and asset-menu-item shapes. All three theme variants (default, bright blue, dark grey orange).

STORY: the user sees the sets first (libraries and suggestions), reads counts at a glance, accepts or dismisses a suggestion in one click, narrows tens of thousands of assets with facets, and places an asset with one click.

FIRST VIEWPORT: Manager is a 1060rem draggable panel: title bar with mode toggle, a 300rem sidebar (All assets, then Libraries and Suggestions tabs), and a main column with header, search and filter row, result bar with bulk actions, tile grid, pager. Palette is a 292rem vertical dock near the left edge, clear of the bottom toolbar, the asset menu and Find It: library dropdown in the title bar, search and filter button, four columns of 60rem tiles, compact pager.

FORM: user-pinned brief (two modes inside the game's visual world). No concept roll: the request is precisely specified and the user cannot be asked. Seed key: none (pinned).

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Unresolved

Palette default position and library switcher form (dropdown of all libraries, no pinned or recent list yet) await in-game confirmation by the author.
