import { memo, useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { Button, Scrollable, Tooltip } from "cs2/ui";
import {
  acceptSuggestion,
  ALL_ASSETS,
  createLibrary,
  dismissSuggestion,
  Library,
  rescan,
  select,
  SUGGESTION_PREFIX,
  toggleSuggestionNode,
  TreeRow,
} from "bindings";
import { Translate } from "l10n";
import { TextInput, vanillaClasses } from "vanilla";
import { CommunitySidebar } from "./Community";
import { Glyph, RoundButton, SquareButton, StateBlock } from "./controls";
import styles from "./panel.module.scss";

interface SidebarProps {
  t: Translate;
  libraries: Library[];
  tree: TreeRow[];
  selectedId: string;
  busy: boolean;
  tab: SidebarTabId;
  onTab: (tab: SidebarTabId) => void;
}

export type SidebarTabId = "libraries" | "suggestions" | "community";

// Indent per level below the root groups, in rem.
const INDENT = 12;

// Kept while the UI lives, so the sidebar reopens on the tab that was in use.
let lastTab: SidebarTabId = "suggestions";

/** The sidebar tab, owned by the manager because the Community tab also changes the main column. */
export function useSidebarTab(hasLibraries: boolean): [SidebarTabId, (tab: SidebarTabId) => void] {
  const [tab, setTab] = useState<SidebarTabId>(hasLibraries || lastTab === "community" ? lastTab : "suggestions");

  const show = (next: SidebarTabId) => {
    lastTab = next;
    setTab(next);
  };

  return [tab, show];
}

export const Sidebar = ({ t, libraries, tree, selectedId, busy, tab, onTab: show }: SidebarProps) => {
  const acceptLabel = t("Accept");
  const dismissLabel = t("Dismiss");

  // Accepting a suggestion selects the new library: follow it to the tab where it now lives.
  // The first run is skipped while the Community tab is open, so the panel reopens on it.
  const mounted = useRef(false);

  useEffect(() => {
    const first = !mounted.current;

    mounted.current = true;

    if (first && tab === "community") {
      return;
    }

    if (selectedId.startsWith(SUGGESTION_PREFIX)) {
      show("suggestions");
    } else if (selectedId !== "" && selectedId !== ALL_ASSETS) {
      show("libraries");
    }
  }, [selectedId]);

  return (
    <div className={styles.sidebar}>
      <div
        className={classNames(styles.allRow, selectedId === ALL_ASSETS && tab !== "community" && styles.selected)}
        onClick={() => {
          select(ALL_ASSETS);

          if (tab === "community") {
            show("libraries");
          }
        }}
      >
        <div className={styles.rowName}>{t("AllAssets")}</div>
      </div>

      <div className={classNames(vanillaClasses.tabBar, styles.tabBar)}>
        <SidebarTab
          label={t("TabLibraries")}
          count={libraries.length}
          selected={tab === "libraries"}
          onSelect={() => show("libraries")}
        />
        <SidebarTab
          label={t("Suggestions")}
          count={0}
          selected={tab === "suggestions"}
          onSelect={() => show("suggestions")}
        />
        <SidebarTab
          label={t("Community")}
          count={0}
          selected={tab === "community"}
          onSelect={() => show("community")}
        />
      </div>

      {tab === "community" ? (
        <CommunitySidebar t={t} />
      ) : tab === "libraries" ? (
        <div className={styles.tabBody}>
          <NewLibrary t={t} />
          <Scrollable vertical smooth trackVisibility="scrollable" className={styles.sidebarScroll}>
            {libraries.length === 0 && (
              <StateBlock
                compact
                title={t("NoLibrariesShort")}
                text={t("NoLibraries")}
                actionLabel={t("SeeSuggestions")}
                onAction={() => show("suggestions")}
              />
            )}
            {libraries.map((library) => (
              <LibraryRow
                key={library.id}
                library={library}
                selected={selectedId === library.id}
                suggestedLabel={t("FromSuggestion")}
                legacyLabel={t("LegacySource")}
              />
            ))}
          </Scrollable>
        </div>
      ) : (
        <div className={styles.tabBody}>
          <Scrollable vertical smooth trackVisibility="scrollable" className={styles.sidebarScroll}>
            {tree.length === 0 &&
              (busy ? (
                <StateBlock compact title={t("SuggestionsPendingTitle")} text={t("SuggestionsPending")} />
              ) : (
                <StateBlock
                  compact
                  title={t("NoSuggestionsTitle")}
                  text={t("NoSuggestions")}
                  actionLabel={t("Rescan")}
                  onAction={rescan}
                />
              ))}
            {tree.map((row) => (
              <TreeRowView
                key={row.id}
                row={row}
                selected={selectedId === SUGGESTION_PREFIX + row.id}
                acceptLabel={acceptLabel}
                dismissLabel={dismissLabel}
              />
            ))}
          </Scrollable>
        </div>
      )}
    </div>
  );
};

interface SidebarTabProps {
  label: string;
  count: number;
  selected: boolean;
  onSelect: () => void;
}

const SidebarTab = ({ label, count, selected, onSelect }: SidebarTabProps) => (
  <Button
    theme={TAB_THEME}
    className={classNames(vanillaClasses.tab || styles.tabFallback, styles.tab)}
    selected={selected}
    onSelect={onSelect}
  >
    {label}
    {count > 0 && <div className={styles.tabCount}>{count}</div>}
  </Button>
);

const TAB_THEME = { button: "" };

interface LibraryRowProps {
  library: Library;
  selected: boolean;
  suggestedLabel: string;
  legacyLabel: string;
}

const sameLibraryRow = (a: LibraryRowProps, b: LibraryRowProps) =>
  a.selected === b.selected &&
  a.suggestedLabel === b.suggestedLabel &&
  a.library.id === b.library.id &&
  a.library.name === b.library.name &&
  a.library.count === b.library.count &&
  a.library.source === b.library.source;

const LibraryRow = memo(({ library, selected, suggestedLabel, legacyLabel }: LibraryRowProps) => (
  <div className={classNames(styles.row, selected && styles.selected)} onClick={() => select(library.id)}>
    <div className={styles.rowName}>{library.name}</div>
    {library.source === "suggested" && (
      <Tooltip tooltip={suggestedLabel}>
        <div className={styles.sourceMarker}>
          <Glyph name="star" className={styles.sourceGlyph} />
        </div>
      </Tooltip>
    )}
    {library.source === "legacy" && (
      <Tooltip tooltip={legacyLabel}>
        <div className={styles.sourceMarker}>
          <Glyph name="info" className={styles.sourceGlyph} />
        </div>
      </Tooltip>
    )}
    <div className={styles.rowCount}>{library.count}</div>
  </div>
), sameLibraryRow);

interface TreeRowViewProps {
  row: TreeRow;
  selected: boolean;
  acceptLabel: string;
  dismissLabel: string;
}

// The binding sends new row objects on every update, so rows are compared field by field and
// only the ones that really changed render again.
const sameRow = (a: TreeRowViewProps, b: TreeRowViewProps) => {
  const x = a.row;
  const y = b.row;

  if (
    a.selected !== b.selected ||
    a.acceptLabel !== b.acceptLabel ||
    x.id !== y.id ||
    x.name !== y.name ||
    x.count !== y.count ||
    x.depth !== y.depth ||
    x.expanded !== y.expanded ||
    x.hasChildren !== y.hasChildren ||
    x.group !== y.group ||
    x.thumbs.length !== y.thumbs.length
  ) {
    return false;
  }

  for (let i = 0; i < x.thumbs.length; i++) {
    if (x.thumbs[i] !== y.thumbs[i]) {
      return false;
    }
  }

  return true;
};

const TreeRowView = memo(({ row, selected, acceptLabel, dismissLabel }: TreeRowViewProps) => {
  const [hovered, setHovered] = useState(false);
  const root = row.depth === 0;
  const showActions = !row.group && (hovered || selected);

  return (
    <div
      className={classNames(
        styles.row,
        styles.treeRow,
        root && styles.treeRoot,
        row.group && !root && styles.treeGroup,
        selected && styles.selected
      )}
      style={root ? undefined : { paddingLeft: `${4 + (row.depth - 1) * INDENT}rem` }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onClick={() => (row.group ? toggleSuggestionNode(row.id) : select(SUGGESTION_PREFIX + row.id))}
    >
      <div
        className={styles.treeToggle}
        onClick={(e) => {
          if (row.hasChildren) {
            e.stopPropagation();
            toggleSuggestionNode(row.id);
          }
        }}
      >
        {row.hasChildren && <Glyph name={row.expanded ? "down" : "right"} className={styles.treeChevron} />}
      </div>
      <div className={styles.rowName}>{row.name}</div>
      {showActions ? (
        <div className={styles.treeActions}>
          <RoundButton icon="check" tooltip={acceptLabel} onSelect={() => acceptSuggestion(row.id)} />
          <RoundButton icon="close" tooltip={dismissLabel} danger onSelect={() => dismissSuggestion(row.id)} />
        </div>
      ) : (
        !row.group &&
        row.thumbs.slice(0, 3).map((thumb, index) => <img key={index} className={styles.treeThumb} src={thumb} />)
      )}
      <div className={styles.rowCount}>{row.count}</div>
    </div>
  );
}, sameRow);

const NewLibrary = ({ t }: { t: Translate }) => {
  const [name, setName] = useState("");
  const trimmed = name.trim();

  const create = () => {
    if (trimmed.length > 0) {
      createLibrary(trimmed);
      setName("");
    }
  };

  return (
    <div className={styles.newLibrary}>
      <TextInput
        className={styles.textInput}
        value={name}
        placeholder={t("NewLibrary")}
        onChange={(e) => setName(e.target.value)}
        onKeyDown={(e) => {
          if (e.keyCode === 13) {
            create();
          }
        }}
      />
      <SquareButton
        icon="plus"
        label={t("Create")}
        disabled={trimmed.length === 0}
        className={styles.rowGap}
        onSelect={create}
      />
    </div>
  );
};
