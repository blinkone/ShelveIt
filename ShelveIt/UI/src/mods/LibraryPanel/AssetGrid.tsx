import { memo, ReactNode, useCallback, useEffect, useState } from "react";
import classNames from "classnames";
import { useValue } from "cs2/api";
import { Scrollable, Tooltip } from "cs2/ui";
import {
  addAsset,
  Asset,
  assetCount$,
  assets$,
  clearFilters,
  Library,
  page$,
  pageSize$,
  placeAsset,
  removeAsset,
  setPage,
  totalCount$,
} from "bindings";
import { Translate } from "l10n";
import { vanillaClasses } from "vanilla";
import { Glyph, Select, SquareButton, StateBlock, TextButton } from "./controls";
import { formatCount } from "./FilterBar";
import styles from "./panel.module.scss";

const ICONS = "Media/Game/Icons/";

// The game's own category icons, shown on tiles of assets that have no picture.
const KIND_ICONS: Record<string, string> = {
  Building: ICONS + "Zones.svg",
  UniqueBuilding: ICONS + "Unique.svg",
  ServiceBuilding: ICONS + "Services.svg",
  Prop: ICONS + "Props.svg",
  Vegetation: ICONS + "Vegetation.svg",
  Decal: ICONS + "PropsDecals.svg",
  Network: ICONS + "Roads.svg",
  Surface: ICONS + "Landscaping.svg",
  Intersection: ICONS + "Intersections.svg",
  Vehicle: ICONS + "GenericVehicle.svg",
};

interface AssetGridProps {
  t: Translate;
  libraries: Library[];
  /** Set in a library view, where tiles can be removed. Elsewhere tiles can be added to a library. */
  libraryId?: string;
  /** Shown when the view itself has no assets, before any search or filter. */
  empty: ReactNode;
  /** Palette: smaller tiles, a shorter pager and no per tile library actions. */
  compact?: boolean;
}

export const AssetGrid = ({ t, libraries, libraryId, empty, compact }: AssetGridProps) => {
  const assets = useValue(assets$);
  const assetCount = useValue(assetCount$);
  const totalCount = useValue(totalCount$);
  const page = useValue(page$);
  const pageSize = Math.max(1, useValue(pageSize$));
  const [picking, setPicking] = useState<Asset | null>(null);
  const pageCount = Math.max(1, Math.ceil(assetCount / pageSize));

  // The picker belongs to one page of one view.
  useEffect(() => setPicking(null), [assets]);

  // Escape closes the picker before it reaches the panel.
  useEffect(() => {
    if (!picking) {
      return;
    }

    const onKey = (e: KeyboardEvent) => {
      if (e.keyCode === 27) {
        e.stopPropagation();
        setPicking(null);
      }
    };

    window.addEventListener("keydown", onKey, true);

    return () => window.removeEventListener("keydown", onKey, true);
  }, [picking]);

  const pick = useCallback((asset: Asset) => setPicking(asset), []);

  if (totalCount === 0) {
    return <>{empty}</>;
  }

  if (assetCount === 0) {
    return (
      <StateBlock
        compact={compact}
        title={t("NoMatches")}
        text={t("NoMatchesHint")}
        actionLabel={t("ClearFilters")}
        onAction={clearFilters}
      />
    );
  }

  return (
    <div className={styles.gridArea}>
      {picking && (
        <div className={styles.picker}>
          <div className={styles.pickerTitle}>{t("AddAssetTo", { NAME: picking.name })}</div>
          <Select
            className={styles.pickerSelect}
            label={t("ChooseLibrary")}
            items={libraries.map((library) => ({ id: library.id, label: library.name, note: library.count }))}
            emptyText={t("NoLibrariesShort")}
            onPick={(id) => {
              addAsset(id, picking.key);
              setPicking(null);
            }}
          />
          <TextButton className={styles.rowGap} onSelect={() => setPicking(null)}>
            {t("Cancel")}
          </TextButton>
        </div>
      )}

      <Scrollable vertical smooth trackVisibility="scrollable" className={styles.gridScroll}>
        <div className={styles.grid}>
          {assets.map((asset) => (
            <AssetTile key={asset.key} t={t} asset={asset} libraryId={libraryId} compact={compact} onPick={pick} />
          ))}
        </div>
      </Scrollable>

      {(assetCount > pageSize || compact) && (
        <div className={styles.pager}>
          {compact && <div className={styles.pagerCount}>{formatCount(t, assetCount)}</div>}
          <SquareButton
            icon="left"
            label={t("PreviousPage")}
            disabled={page <= 0}
            onSelect={() => setPage(page - 1)}
          />
          <div className={styles.pagerLabel}>
            {compact ? `${page + 1} / ${pageCount}` : t("Page", { PAGE: page + 1, TOTAL: pageCount })}
          </div>
          <SquareButton
            icon="right"
            label={t("NextPage")}
            disabled={page >= pageCount - 1}
            onSelect={() => setPage(page + 1)}
          />
          {compact && <div className={styles.pagerCount} />}
        </div>
      )}
    </div>
  );
};

interface AssetTileProps {
  t: Translate;
  asset: Asset;
  libraryId?: string;
  compact?: boolean;
  onPick: (asset: Asset) => void;
}

// The binding sends new objects with every page, so tiles are compared by what they show.
const sameTile = (a: AssetTileProps, b: AssetTileProps) =>
  a.t === b.t &&
  a.libraryId === b.libraryId &&
  a.compact === b.compact &&
  a.asset.key === b.asset.key &&
  a.asset.entityIndex === b.asset.entityIndex &&
  a.asset.name === b.asset.name &&
  a.asset.thumbnail === b.asset.thumbnail &&
  a.asset.thumbKind === b.asset.thumbKind &&
  a.asset.level === b.asset.level &&
  a.asset.lotW === b.asset.lotW &&
  a.asset.lotD === b.asset.lotD &&
  a.asset.inLibrary === b.asset.inLibrary;

const AssetTile = memo(({ t, asset, libraryId, compact, onPick }: AssetTileProps) => {
  const [failed, setFailed] = useState(false);
  const hasImage = !failed && asset.thumbKind !== "none" && asset.thumbnail.length > 0;
  const group = hasImage && asset.thumbKind === "group";
  const kindLabel = t("Kind." + asset.kind);
  const kindIcon = KIND_ICONS[asset.kind];
  const owned = asset.inLibrary && !libraryId;

  const facts = [kindLabel];

  if (asset.lotW > 0 && asset.lotD > 0) {
    facts.push(t("Lot", { W: asset.lotW, D: asset.lotD }));
  }

  if (asset.level > 0) {
    facts.push(t("Level", { LEVEL: asset.level }));
  }

  // Variants of one building share a name, so tiles without a picture also say which one this is.
  const meta = [
    asset.lotW > 0 && asset.lotD > 0 ? `${asset.lotW}x${asset.lotD}` : "",
    asset.level > 0 ? t("LevelShort", { LEVEL: asset.level }) : "",
  ]
    .filter((part) => part.length > 0)
    .join(" · ");

  const tooltip = (
    <div className={styles.tip}>
      <div className={styles.tipName}>{asset.name}</div>
      {asset.package.length > 0 && <div className={styles.tipLine}>{asset.package}</div>}
      <div className={styles.tipLine}>{facts.join(" · ")}</div>
      {owned && <div className={styles.tipNote}>{t("AlreadyInLibrary")}</div>}
      <div className={styles.tipHint}>{t("ClickToPlace")}</div>
    </div>
  );

  return (
    <Tooltip tooltip={tooltip}>
      <div
        className={classNames(vanillaClasses.gridItem || styles.tileFallback, styles.tile)}
        onClick={() => placeAsset(asset.entityIndex)}
      >
        {hasImage && !group && (
          <img
            className={classNames(vanillaClasses.gridThumbnail, styles.tileImage)}
            src={asset.thumbnail}
            onError={() => setFailed(true)}
          />
        )}
        {group && (
          <div className={styles.tileStandIn}>
            <img className={styles.tileGroupIcon} src={asset.thumbnail} onError={() => setFailed(true)} />
            <div className={styles.tileName}>{asset.name}</div>
            {meta.length > 0 && <div className={styles.tileMeta}>{meta}</div>}
          </div>
        )}
        {!hasImage && (
          <div className={styles.tileStandIn}>
            {kindIcon && <img className={styles.tileKindIcon} src={kindIcon} />}
            <div className={styles.tileName}>{asset.name}</div>
            {meta.length > 0 && <div className={styles.tileMeta}>{meta}</div>}
          </div>
        )}
        {owned && (
          <div className={styles.ownedMarker}>
            <Glyph name="check" className={styles.ownedGlyph} />
          </div>
        )}
        {!compact && (
          <div
            className={classNames(styles.tileButton, libraryId && styles.tileRemove)}
            onClick={(e) => {
              e.stopPropagation();

              if (libraryId) {
                removeAsset(libraryId, asset.key);
              } else {
                onPick(asset);
              }
            }}
          >
            <Glyph name={libraryId ? "minus" : "plus"} className={styles.tileButtonGlyph} />
          </div>
        )}
      </div>
    </Tooltip>
  );
}, sameTile);
