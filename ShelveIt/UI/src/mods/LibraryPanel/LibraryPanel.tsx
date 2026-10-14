import { MouseEvent, useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { useValue } from "cs2/api";
import { Button, Panel, Tooltip } from "cs2/ui";
import icon from "images/shelveit.svg";
import {
  ALL_ASSETS,
  exportContribution,
  importAll,
  libraries$,
  PanelMode,
  panelMode$,
  panelVisible$,
  rescan,
  ScanState,
  scanState$,
  select,
  selectedId$,
  setPanelMode,
  SUGGESTION_PREFIX,
  suggestionTree$,
  togglePanel,
} from "bindings";
import { Translate, useT } from "l10n";
import { AssetGrid } from "./AssetGrid";
import { Glyph, Select, StateBlock, TextButton, UilContext, useUilAvailable } from "./controls";
import { FilterBar } from "./FilterBar";
import { LibraryView } from "./LibraryView";
import { CodeImportRow, CommunityView } from "./Community";
import { Sidebar, useSidebarTab } from "./Sidebar";
import styles from "./panel.module.scss";

// Where each layout opens, as a fraction of the free space around the panel. Updated when a
// panel is dragged, so it reopens in the same place until the UI is reloaded. The palette starts
// at the left edge, above the build menu and clear of the toolbar and of Find It at the bottom.
const lastPosition: Record<PanelMode, { x: number; y: number }> = {
  manager: { x: 0.5, y: 0.35 },
  palette: { x: 0.006, y: 0.3 },
};

const SCAN_STATE_KEYS: Record<ScanState, string> = {
  idle: "Idle",
  scanning: "Scanning",
  classifying: "Classifying",
  ready: "Ready",
};

/** Subscribes to PanelVisible only. Everything else mounts with the panel. */
export const LibraryPanelHost = () => {
  const visible = useValue(panelVisible$);

  return visible ? <LibraryPanel /> : null;
};

const LibraryPanel = () => {
  const t = useT();
  const mode = useValue(panelMode$) === "palette" ? "palette" : "manager";
  const uil = useUilAvailable();

  // The key remounts the game panel, which reads its position only once.
  return (
    <UilContext.Provider value={uil}>
      {mode === "palette" ? <Palette key="palette" t={t} /> : <Manager key="manager" t={t} />}
    </UilContext.Provider>
  );
};

const clamp01 = (value: number) => (value < 0 ? 0 : value > 1 ? 1 : value);

// The game's Panel does not report its position, so it is read from the DOM after each drag.
function usePanelPosition(mode: PanelMode, panelClass: string) {
  const marker = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const remember = () => {
      let element: HTMLElement | null = marker.current;

      while (element && !element.classList.contains(panelClass)) {
        element = element.parentElement;
      }

      if (!element) {
        return;
      }

      const rect = element.getBoundingClientRect();
      const freeX = window.innerWidth - rect.width;
      const freeY = window.innerHeight - rect.height;

      if (rect.width > 0 && freeX > 0 && freeY > 0) {
        lastPosition[mode] = { x: clamp01(rect.left / freeX), y: clamp01(rect.top / freeY) };
      }
    };

    window.addEventListener("mouseup", remember);

    return () => {
      window.removeEventListener("mouseup", remember);
      remember();
    };
  }, [mode, panelClass]);

  return marker;
}

const stop = (e: MouseEvent) => e.stopPropagation();

const TITLE_BUTTON_THEME = { button: styles.titleButton };

/** The switch between the two layouts, next to the close button. */
const ModeButton = ({ t, mode }: { t: Translate; mode: PanelMode }) => (
  <Tooltip tooltip={t(mode === "manager" ? "ToPalette" : "ToManager")}>
    <Button
      theme={TITLE_BUTTON_THEME}
      onSelect={() => setPanelMode(mode === "manager" ? "palette" : "manager")}
      onMouseDown={stop}
    >
      <Glyph name={mode === "manager" ? "compact" : "expand"} className={styles.titleGlyph} />
    </Button>
  </Tooltip>
);

const ScanProgress = ({ label }: { label: string }) => (
  <div className={styles.progress}>
    <div className={styles.progressLabel}>{label}</div>
    <div className={styles.progressTrack}>
      <div className={styles.progressBar} />
    </div>
  </div>
);

// Manager: the large layout for organizing libraries.

// The game's Panel wraps header in its own PanelTitleBar (with the close button), so header is plain content.

const Manager = ({ t }: { t: Translate }) => {
  const libraries = useValue(libraries$);
  const tree = useValue(suggestionTree$);
  const selectedId = useValue(selectedId$);
  const scanState = useValue(scanState$);
  const busy = scanState === "scanning" || scanState === "classifying";
  const marker = usePanelPosition("manager", styles.manager);

  const [tab, setTab] = useSidebarTab(libraries.length > 0);
  const [codeOpen, setCodeOpen] = useState(false);

  // Captured once per mount: the panel must not see a new initial position while it is open.
  const [initialPosition] = useState(lastPosition.manager);

  return (
    <Panel
      draggable
      initialPosition={initialPosition}
      className={styles.manager}
      header={
        <div className={styles.titleRow}>
          <img src={icon} className={styles.titleIcon} />
          <div className={styles.titleText}>{t("Title")}</div>
          <ModeButton t={t} mode="manager" />
        </div>
      }
      footer={
        codeOpen ? (
          <CodeImportRow
            t={t}
            onClose={() => setCodeOpen(false)}
            onImported={() => {
              setCodeOpen(false);
              setTab("community");
            }}
          />
        ) : (
          <Footer t={t} scanState={scanState} busy={busy} onImportCode={() => setCodeOpen(true)} />
        )
      }
      onClose={togglePanel}
    >
      <div ref={marker} className={styles.marker} />
      {busy && <ScanProgress label={t(SCAN_STATE_KEYS[scanState])} />}
      <div className={styles.body}>
        <Sidebar
          t={t}
          libraries={libraries}
          tree={tree}
          selectedId={selectedId}
          busy={busy}
          tab={tab}
          onTab={setTab}
        />
        {tab === "community" ? (
          <CommunityView t={t} onUseCode={() => setCodeOpen(true)} />
        ) : (
          <LibraryView t={t} libraries={libraries} tree={tree} selectedId={selectedId} busy={busy} />
        )}
      </div>
    </Panel>
  );
};

interface FooterProps {
  t: Translate;
  scanState: ScanState;
  busy: boolean;
  onImportCode: () => void;
}

const Footer = ({ t, scanState, busy, onImportCode }: FooterProps) => (
  <div className={styles.footer}>
    <TextButton tooltip={t("ImportTip")} onSelect={importAll}>
      {t("Import")}
    </TextButton>
    <TextButton tooltip={t("ImportCodeTip")} className={styles.rowGap} onSelect={onImportCode}>
      {t("ImportCode")}
    </TextButton>
    <TextButton tooltip={t("ContributionTip")} className={styles.rowGap} onSelect={exportContribution}>
      {t("ExportContribution")}
    </TextButton>
    <div className={styles.scanState}>{t(SCAN_STATE_KEYS[scanState] ?? "Idle")}</div>
    <TextButton icon="rescan" tooltip={t("RescanTip")} disabled={busy} onSelect={rescan}>
      {t("Rescan")}
    </TextButton>
  </div>
);

// Palette: the compact layout that stays open while building.

const Palette = ({ t }: { t: Translate }) => {
  const libraries = useValue(libraries$);
  const selectedId = useValue(selectedId$);
  const scanState = useValue(scanState$);
  const busy = scanState === "scanning" || scanState === "classifying";
  const marker = usePanelPosition("palette", styles.palette);
  const [initialPosition] = useState(lastPosition.palette);

  const isAll = selectedId === ALL_ASSETS;
  const isPreview = selectedId.startsWith(SUGGESTION_PREFIX);
  const library = isAll || isPreview ? undefined : libraries.find((l) => l.id === selectedId);
  const hasView = isAll || isPreview || library !== undefined;

  const items = [
    { id: ALL_ASSETS, label: t("AllAssets") },
    ...libraries.map((l) => ({ id: l.id, label: l.name, note: l.count })),
  ];

  let empty: JSX.Element;

  if (busy) {
    empty = <StateBlock compact title={t(SCAN_STATE_KEYS[scanState])} text={t("NothingScanned")} />;
  } else if (library) {
    empty = (
      <StateBlock
        compact
        title={t("Empty")}
        text={t("EmptyPaletteHint")}
        actionLabel={t("ToManager")}
        onAction={() => setPanelMode("manager")}
      />
    );
  } else {
    empty = <StateBlock compact title={t("NoAssetsTitle")} text={t(isPreview ? "NoAssets" : "NothingScanned")} />;
  }

  return (
    <Panel
      draggable
      initialPosition={initialPosition}
      className={styles.palette}
      header={
        <div className={styles.titleRow}>
          <Select
            className={styles.paletteSwitch}
            label={library ? library.name : isAll ? t("AllAssets") : isPreview ? t("Preview") : t("ChooseLibrary")}
            items={items}
            selectedId={selectedId}
            onPick={select}
          />
          <ModeButton t={t} mode="palette" />
        </div>
      }
      onClose={togglePanel}
    >
      <div ref={marker} className={styles.marker} />
      {busy && <ScanProgress label={t(SCAN_STATE_KEYS[scanState])} />}
      <div className={classNames(styles.main, styles.paletteBody)}>
        {hasView ? (
          <>
            <FilterBar t={t} compact />
            <AssetGrid t={t} libraries={libraries} libraryId={library?.id} empty={empty} compact />
          </>
        ) : (
          <StateBlock
            compact
            title={t("ChooseLibrary")}
            text={t(libraries.length > 0 ? "PaletteHint" : "PaletteNoLibraries")}
            actionLabel={libraries.length > 0 ? t("AllAssets") : t("ToManager")}
            onAction={() => (libraries.length > 0 ? select(ALL_ASSETS) : setPanelMode("manager"))}
          />
        )}
      </div>
    </Panel>
  );
};
