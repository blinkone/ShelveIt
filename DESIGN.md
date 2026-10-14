# Design

The panel has no visual identity of its own. The authority is the interface of Cities: Skylines II, read from the installed game (version 1.6.2f1, `Cities2_Data/Content/Game/UI/index.css` and `index.js`). When this file and the game disagree, the game is right and this file is stale.

## Where the look comes from

| Part | Source in the game |
| --- | --- |
| Panel, header, footer, title bar, close button | `cs2/ui` `Panel` and the registry's `panel-title-bar.tsx` |
| Buttons | `cs2/ui` `Button` with our own theme class, so focus, sounds and the disabled attribute stay vanilla |
| Dropdowns (sort, add to library, palette switcher) | `cs2/ui` `Dropdown`, `DropdownToggle`, `DropdownItem` with `game-ui/game/themes/game-dropdown.module.scss` |
| Scroll areas | `cs2/ui` `Scrollable`, padded through its `--contentPadding*` variables |
| Tooltips | `cs2/ui` `Tooltip` |
| Text fields | the registry's `text-input.tsx` |
| Asset tiles | class `item` of `game-ui/game/components/item-grid/item-grid.module.scss`, sized with `--assetMenuItemSize`, `--assetMenuImageSize`, `--assetMenuItemGap` |
| Square icon buttons | class `button` of `tool-options/tool-button/tool-button.module.scss` |
| Sidebar tabs | classes `tabBar` and `tab` of `game-ui/common/tabs/tabs.module.scss` |
| Icons | `cs2/ui` `Icon` with `tinted`, on `Media/Glyphs/*.svg` |

Every registry lookup has a fallback class in `panel.module.scss` (`*-fallback`). A game update that moves a module costs the vanilla look of that part only.

## Tokens

All colours are the game's CSS variables, so the three interface styles (default, bright blue, dark grey orange) and the transparency setting apply without extra work.

- Surfaces: `--panelColorDark` (header, footer, sidebar), `--panelColorNormal` and the panel gradient (content, from the Panel), `--sectionHeaderColor` (tree root rows, picker strip, preview label), `--sectionBackgroundColor` (filter area), `--toolbarFieldColor` (tiles without a picture).
- State: `--hoverColorNormal`, `--activeColorNormal`, `--selectedColor`, `--selectedColor-hover`.
- Accent: `--accentColorNormal` (badge, input focus), `--accentColorLight` (links, tooltip title), `--accentColorDark` (round button hover), `--progressColor`.
- Meaning: `--positiveColor` (already in a library), `--negativeColor` and `--buttonStrokeRed` (destructive).
- Lines: `--dividerColor` at `--stroke1`.
- Text: `--textColor`. Type sizes `--fontSizeXXS` to `--fontSizeL`. The font is the game's `--fontFamily`.

Values the game writes as literals, copied into SCSS variables at the top of `panel.module.scss`:

- Secondary text `rgba(255, 255, 255, 0.7)`. The game's `--textColorDim` variables are plain white in the default style.
- Control surface `#0f1013`, hover `#1d1f25`, pressed `#23262d`, radius `4rem`, size `28rem` (tool button and in-game dropdown).
- Field `rgba(15, 16, 19, 0.5)` (tool options number field).
- Tile fallback `rgba(98, 128, 138, 0.4)`, radius `8rem`.

## Rules

- Sizes in `rem`. The game scales `rem` with the screen, so the panel follows the UI scale.
- Flexbox and margins only. No grid, `gap`, `position: sticky`, `:has` or `:disabled`. A declaration that contains `var()` is written as longhands (mixins `radius`, `border`, `border-color`, `edge`).
- No shadows, filters or blur of our own. Motion is limited to the scan progress bar.
- Text buttons name their action. Icon-only buttons are the square tool button with a tooltip.
- Counts sit in a right aligned column and use the thousands separator of the language.
- A tile shows a real thumbnail on the vanilla tile. A tile without one is darker and shows a small zone or category icon, the name, and lot and level.

## Layout

- Manager: 1060rem wide, 640rem body. Sidebar 300rem (All assets, then Libraries and Suggestions tabs). Main column: header, search and filter row, result bar with bulk actions, 86rem tiles in rows of eight, pager.
- Palette: 292rem wide, 474rem body, opens at the left edge above the build menu. Library dropdown in the title bar, search with a filter button, 60rem tiles in rows of four, compact pager.
- The toggle beside the close button switches layouts. The choice is stored in the mod settings (`PanelMode`).

## Icons

Game glyphs first. Magnifier, funnel, minus, expand and minimize come from the Unified Icon Library (`coui://uil/Standard/`) when it answers a one-time probe. Without it the search field has no icon, the filter button shows its label, and game arrows stand in for the layout toggle.

The toolbar icon (`UI/src/images/shelveit.svg`) is a 32 unit white glyph in the weight of `Media/Glyphs`, tinted by the game.

## Preview

`UI/preview` renders the panel in a browser with the game's real stylesheet and mocked `cs2` modules. It approximates Coherent Gameface. See the header of `UI/preview/webpack.preview.js` for the commands.
