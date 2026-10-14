import { memo, useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { useValue } from "cs2/api";
import { Scrollable } from "cs2/ui";
import {
  ActiveFilter,
  activeFilters$,
  addFilteredToLibrary,
  assetCount$,
  clearFilters,
  Facet,
  FacetOption,
  facets$,
  Library,
  removeFilteredFromLibrary,
  saveFilteredAsLibrary,
  search$,
  setFilter,
  setSearch,
  setSort,
  sort$,
  totalCount$,
} from "bindings";
import { Translate } from "l10n";
import { TextInput } from "vanilla";
import { Glyph, Select, SquareButton, TextButton } from "./controls";
import styles from "./panel.module.scss";

const SEARCH_DELAY = 250;
const SORTS = ["", "name", "package", "lot", "newest"];
const NUMERIC_FACETS = ["level", "lotW", "lotD"];
const SEARCHABLE_FACETS = ["package", "pack"];

// Facets a detailer reaches for all the time. The rest sit behind "More filters", except
// while one of them has a value selected.
const PRIMARY_FACETS = ["kind", "zone", "density", "region", "theme", "pack"];

/** Options shown before a facet has to be expanded. */
const OPTION_LIMIT = 12;
const EXPANDED_LIMIT = 80;

// Kept while the UI lives, so the filter area reopens the way it was left.
let filtersOpen = false;
let moreOpen = false;

/** 39012 becomes "39,012" or "39.012", with the separator of the active language. */
export function formatCount(t: Translate, value: number): string {
  const text = String(Math.max(0, Math.floor(value)));

  return text.length > 3 ? text.replace(/\B(?=(\d{3})+(?!\d))/g, t("Thousands")) : text;
}

interface FilterBarProps {
  t: Translate;
  /** The palette shows the same controls in less space. */
  compact?: boolean;
}

export const FilterBar = ({ t, compact }: FilterBarProps) => {
  const [open, setOpen] = useState(filtersOpen);
  const sort = useValue(sort$);
  const activeFilters = useValue(activeFilters$);

  const toggle = () => {
    filtersOpen = !open;
    setOpen(!open);
  };

  return (
    <div className={styles.filterBar}>
      <div className={styles.filterRow}>
        <SearchField t={t} />
        {compact ? (
          <SquareButton
            icon="filter"
            label={t("Filters")}
            selected={open}
            badge={activeFilters.length}
            className={styles.rowGap}
            onSelect={toggle}
          />
        ) : (
          <>
            <Select
              className={styles.sortSelect}
              label={`${t("Sort")}: ${t("Sort." + (SORTS.includes(sort) ? sort : ""))}`}
              items={SORTS.map((id) => ({ id, label: t("Sort." + id) }))}
              selectedId={sort}
              onPick={setSort}
            />
            <TextButton icon="filter" selected={open} className={styles.rowGap} onSelect={toggle}>
              {t("Filters")}
              {activeFilters.length > 0 && <div className={styles.badge}>{activeFilters.length}</div>}
            </TextButton>
          </>
        )}
      </div>

      {activeFilters.length > 0 && <ActiveChips t={t} filters={activeFilters} />}
      {open && <FacetArea t={t} compact={compact} />}
    </div>
  );
};

const SearchField = ({ t }: { t: Translate }) => {
  const search = useValue(search$);
  const [text, setText] = useState(search);
  const sent = useRef(search);
  const timer = useRef<number | undefined>(undefined);

  // The game side changes the search too (selecting another view clears it).
  useEffect(() => {
    if (search !== sent.current) {
      sent.current = search;
      setText(search);
    }
  }, [search]);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  const change = (value: string) => {
    setText(value);
    window.clearTimeout(timer.current);

    timer.current = window.setTimeout(() => {
      const trimmed = value.trim();

      if (trimmed !== sent.current) {
        sent.current = trimmed;
        setSearch(trimmed);
      }
    }, SEARCH_DELAY);
  };

  return (
    <div className={styles.searchField}>
      <Glyph name="search" className={styles.searchGlyph} />
      <TextInput
        className={classNames(styles.textInput, styles.searchInput)}
        value={text}
        placeholder={t("SearchPlaceholder")}
        onChange={(e) => change(e.target.value)}
      />
      {text.length > 0 && (
        <div className={styles.searchClear} onClick={() => change("")}>
          <Glyph name="close" className={styles.searchClearGlyph} />
        </div>
      )}
    </div>
  );
};

const ActiveChips = ({ t, filters }: { t: Translate; filters: ActiveFilter[] }) => (
  <div className={styles.chipRow}>
    {filters.map((filter) => (
      <div
        key={filter.facet + "|" + filter.value}
        className={classNames(styles.chip, styles.chipActive)}
        onClick={() => setFilter(filter.facet, filter.value, false)}
      >
        <div className={styles.chipFacet}>{t("Facet." + filter.facet)}</div>
        <div className={styles.chipLabel}>{filter.label || t("None")}</div>
        <Glyph name="close" className={styles.chipCross} />
      </div>
    ))}
    <div className={classNames(styles.chip, styles.chipClear)} onClick={clearFilters}>
      {t("ClearFilters")}
    </div>
  </div>
);

// Subscribes to Facets only while the filter area is open.
const FacetArea = ({ t, compact }: { t: Translate; compact?: boolean }) => {
  const facets = useValue(facets$);
  const [more, setMore] = useState(moreOpen);
  const shown: Facet[] = [];
  const rest: Facet[] = [];

  for (const facet of facets) {
    if (facet.options.length === 0) {
      continue;
    }

    if (PRIMARY_FACETS.includes(facet.id) || facet.options.some((option) => option.selected)) {
      shown.push(facet);
    } else {
      rest.push(facet);
    }
  }

  return (
    <Scrollable
      vertical
      smooth
      trackVisibility="scrollable"
      className={classNames(styles.facetArea, compact && styles.facetAreaCompact)}
    >
      {shown.map((facet) => (
        <FacetSection key={facet.id} t={t} facet={facet} compact={compact} />
      ))}
      {rest.length > 0 && (
        <div
          className={styles.moreFilters}
          onClick={() => {
            moreOpen = !more;
            setMore(!more);
          }}
        >
          <Glyph name={more ? "down" : "right"} className={styles.moreGlyph} />
          <div className={styles.moreLabel}>{t(more ? "FewerFilters" : "MoreFilters")}</div>
          {!more && <div className={styles.moreList}>{rest.map((facet) => t("Facet." + facet.id)).join(", ")}</div>}
        </div>
      )}
      {more && rest.map((facet) => <FacetSection key={facet.id} t={t} facet={facet} compact={compact} />)}
    </Scrollable>
  );
};

const FacetSection = ({ t, facet, compact }: { t: Translate; facet: Facet; compact?: boolean }) => {
  const [needle, setNeedle] = useState("");
  const [expanded, setExpanded] = useState(false);
  const searchable = SEARCHABLE_FACETS.includes(facet.id);
  const numeric = NUMERIC_FACETS.includes(facet.id);
  const lower = needle.trim().toLowerCase();

  // Options that match nothing in the current result only add noise, unless they are selected.
  let options = facet.options.filter(
    (option) =>
      option.selected || (option.count > 0 && (lower.length === 0 || option.label.toLowerCase().includes(lower)))
  );

  const limit = expanded || lower.length > 0 ? EXPANDED_LIMIT : numeric ? EXPANDED_LIMIT : OPTION_LIMIT;
  const hidden = Math.max(0, options.length - limit);

  if (hidden > 0) {
    options = options.slice(0, limit);
  }

  return (
    <div className={classNames(styles.facet, compact && styles.facetCompact)}>
      <div className={styles.facetTitle}>{t("Facet." + facet.id)}</div>
      <div className={styles.facetBody}>
        {searchable && (expanded || lower.length > 0) && (
          <TextInput
            className={classNames(styles.textInput, styles.facetInput)}
            value={needle}
            placeholder={t("FilterOptions")}
            onChange={(e) => setNeedle(e.target.value)}
          />
        )}
        <div className={styles.chipWrap}>
          {options.map((option) => (
            <OptionChip key={option.value} facetId={facet.id} option={option} numeric={numeric} noneLabel={t("None")} />
          ))}
          {hidden > 0 && (
            <div className={classNames(styles.chip, styles.chipMore)} onClick={() => setExpanded(true)}>
              {expanded ? t("MoreOptions", { COUNT: hidden }) : t("ShowMore", { COUNT: hidden })}
            </div>
          )}
          {expanded && hidden === 0 && lower.length === 0 && (
            <div className={classNames(styles.chip, styles.chipMore)} onClick={() => setExpanded(false)}>
              {t("ShowFewer")}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

interface OptionChipProps {
  facetId: string;
  option: FacetOption;
  numeric: boolean;
  noneLabel: string;
}

const sameOption = (a: OptionChipProps, b: OptionChipProps) =>
  a.facetId === b.facetId &&
  a.numeric === b.numeric &&
  a.noneLabel === b.noneLabel &&
  a.option.value === b.option.value &&
  a.option.label === b.option.label &&
  a.option.count === b.option.count &&
  a.option.selected === b.option.selected;

const OptionChip = memo(
  ({ facetId, option, numeric, noneLabel }: OptionChipProps) => (
    <div
      className={classNames(styles.chip, numeric && styles.chipNumeric, option.selected && styles.chipSelected)}
      onClick={() => setFilter(facetId, option.value, !option.selected)}
    >
      <div className={styles.chipLabel}>{option.label || noneLabel}</div>
      <div className={styles.chipCount}>{option.count}</div>
    </div>
  ),
  sameOption
);

interface ResultBarProps {
  t: Translate;
  libraries: Library[];
  /** Set in a library view. */
  libraryId?: string;
}

/**
 * How many assets the search and filters leave, and what can be done with all of them at once.
 * Mount it with the selected id as key: a half finished action does not carry over to another view.
 */
export const ResultBar = ({ t, libraries, libraryId }: ResultBarProps) => {
  const [mode, setMode] = useState<"" | "save" | "remove">("");
  const [name, setName] = useState("");
  const assetCount = useValue(assetCount$);
  const totalCount = useValue(totalCount$);
  const inputRef = useRef<HTMLInputElement>(null);
  const none = assetCount === 0;
  const trimmed = name.trim();

  useEffect(() => {
    if (mode === "save") {
      inputRef.current?.focus();
    }
  }, [mode]);

  const close = () => {
    setMode("");
    setName("");
  };

  const save = () => {
    if (trimmed.length > 0 && !none) {
      saveFilteredAsLibrary(trimmed);
      close();
    }
  };

  if (mode === "save") {
    return (
      <div className={styles.resultBar}>
        <TextInput
          ref={inputRef}
          className={classNames(styles.textInput, styles.nameInput)}
          value={name}
          placeholder={t("LibraryName")}
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => {
            if (e.keyCode === 13) {
              save();
            } else if (e.keyCode === 27) {
              close();
            }
          }}
        />
        <TextButton disabled={trimmed.length === 0 || none} className={styles.rowGap} onSelect={save}>
          {t("SaveCount", { COUNT: formatCount(t, assetCount) })}
        </TextButton>
        <TextButton className={styles.rowGap} onSelect={close}>
          {t("Cancel")}
        </TextButton>
      </div>
    );
  }

  if (mode === "remove") {
    return (
      <div className={styles.resultBar}>
        <div className={styles.confirmText}>{t("ConfirmRemoveFiltered", { COUNT: formatCount(t, assetCount) })}</div>
        <div className={styles.spacer} />
        <TextButton
          danger
          onSelect={() => {
            removeFilteredFromLibrary();
            close();
          }}
        >
          {t("Remove")}
        </TextButton>
        <TextButton className={styles.rowGap} onSelect={close}>
          {t("Cancel")}
        </TextButton>
      </div>
    );
  }

  return (
    <div className={styles.resultBar}>
      <div className={styles.resultCount}>
        {assetCount === totalCount
          ? t("AssetCount", { COUNT: formatCount(t, assetCount) })
          : t("CountOf", { COUNT: formatCount(t, assetCount), TOTAL: formatCount(t, totalCount) })}
      </div>
      <div className={styles.spacer} />
      <div className={styles.resultLabel}>{t("WithResult")}</div>
      <TextButton disabled={none} tooltip={t("SaveAsLibraryTip")} onSelect={() => setMode("save")}>
        {t("SaveAsLibrary")}
      </TextButton>
      <Select
        className={styles.rowGap}
        label={t("AddToLibrary")}
        items={libraries.filter((l) => l.id !== libraryId).map((l) => ({ id: l.id, label: l.name, note: l.count }))}
        emptyText={t("NoLibrariesShort")}
        disabled={none}
        onPick={addFilteredToLibrary}
      />
      {libraryId && (
        <TextButton disabled={none} className={styles.rowGap} onSelect={() => setMode("remove")}>
          {t("RemoveFiltered")}
        </TextButton>
      )}
    </div>
  );
};
