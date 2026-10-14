import { useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { useValue } from "cs2/api";
import { Tooltip } from "cs2/ui";
import {
  acceptSuggestion,
  addActivePrefab,
  ALL_ASSETS,
  deleteLibrary,
  dismissSuggestion,
  exportLibrary,
  findItAvailable$,
  Library,
  renameLibrary,
  rescan,
  select,
  SUGGESTION_PREFIX,
  togglePanel,
  TreeRow,
} from "bindings";
import { useOpenInFindIt } from "findItBridge";
import { Translate } from "l10n";
import { TextInput } from "vanilla";
import { AssetGrid } from "./AssetGrid";
import { ShareBar } from "./ShareBar";
import { SquareButton, StateBlock, TextButton } from "./controls";
import { FilterBar, ResultBar } from "./FilterBar";
import styles from "./panel.module.scss";

interface LibraryViewProps {
  t: Translate;
  libraries: Library[];
  tree: TreeRow[];
  selectedId: string;
  busy: boolean;
}

export const LibraryView = ({ t, libraries, tree, selectedId, busy }: LibraryViewProps) => {
  const isAll = selectedId === ALL_ASSETS;
  const isPreview = selectedId.startsWith(SUGGESTION_PREFIX);
  const library = isAll || isPreview ? undefined : libraries.find((l) => l.id === selectedId);

  if (!isAll && !isPreview && !library) {
    return (
      <div className={styles.main}>
        <StateBlock
          title={t("NoSelectionTitle")}
          text={t("NoSelection")}
          actionLabel={t("BrowseAll")}
          onAction={() => select(ALL_ASSETS)}
        />
      </div>
    );
  }

  let header: JSX.Element;
  let empty: JSX.Element;

  if (library) {
    header = <LibraryHeader key={library.id} t={t} library={library} />;
    empty = (
      <StateBlock
        title={t("Empty")}
        text={t("EmptyHint")}
        actionLabel={t("BrowseAll")}
        onAction={() => select(ALL_ASSETS)}
      />
    );
  } else if (isPreview) {
    const nodeId = selectedId.substring(SUGGESTION_PREFIX.length);

    // The row is missing from the flat list while one of its parents is collapsed.
    header = <PreviewHeader t={t} nodeId={nodeId} name={tree.find((row) => row.id === nodeId)?.name} />;
    empty = <StateBlock title={t("NoAssetsTitle")} text={t("NoAssets")} />;
  } else {
    header = (
      <div className={styles.header}>
        <div className={styles.headerName}>{t("AllAssets")}</div>
      </div>
    );
    empty = busy ? (
      <StateBlock title={t("Scanning")} text={t("NothingScanned")} />
    ) : (
      <StateBlock title={t("NoAssetsTitle")} text={t("NothingScanned")} actionLabel={t("Rescan")} onAction={rescan} />
    );
  }

  return (
    <div className={styles.main}>
      {header}
      <FilterBar t={t} />
      <ResultBar key={selectedId} t={t} libraries={libraries} libraryId={library?.id} />
      <AssetGrid t={t} libraries={libraries} libraryId={library?.id} empty={empty} />
    </div>
  );
};

const PreviewHeader = ({ t, nodeId, name }: { t: Translate; nodeId: string; name?: string }) => (
  <div className={styles.header}>
    <div className={styles.headerName}>{name ?? t("Preview")}</div>
    {name !== undefined && <div className={styles.headerNote}>{t("Preview")}</div>}
    <div className={styles.spacer} />
    <TextButton icon="check" tooltip={t("AcceptTip")} onSelect={() => acceptSuggestion(nodeId)}>
      {t("Accept")}
    </TextButton>
    <TextButton icon="close" className={styles.rowGap} onSelect={() => dismissSuggestion(nodeId)}>
      {t("Dismiss")}
    </TextButton>
  </div>
);

const LibraryHeader = ({ t, library }: { t: Translate; library: Library }) => {
  const [draft, setDraft] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [sharing, setSharing] = useState(false);
  const findItAvailable = useValue(findItAvailable$);
  const inputRef = useRef<HTMLInputElement>(null);
  const renaming = draft !== null;

  useEffect(() => {
    if (renaming) {
      inputRef.current?.focus();
    }
  }, [renaming]);

  return (
    <>
    <div className={styles.header}>
      {renaming ? (
        <TextInput
          ref={inputRef}
          className={classNames(styles.textInput, styles.renameInput)}
          value={draft}
          placeholder={library.name}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            // The field has already lost focus here: Enter saves, Escape and clicking away discard.
            const name = draft.trim();

            if (e.keyCode === 13 && name.length > 0 && name !== library.name) {
              renameLibrary(library.id, name);
            }
          }}
          onBlur={() => setDraft(null)}
        />
      ) : (
        <Tooltip tooltip={t("Rename")}>
          <div className={classNames(styles.headerName, styles.renamable)} onClick={() => setDraft(library.name)}>
            {library.name}
          </div>
        </Tooltip>
      )}
      <div className={styles.spacer} />

      {confirmDelete ? (
        <>
          <div className={styles.confirmText}>{t("ConfirmDelete")}</div>
          <TextButton danger onSelect={() => deleteLibrary(library.id)}>
            {t("Delete")}
          </TextButton>
          <TextButton className={styles.rowGap} onSelect={() => setConfirmDelete(false)}>
            {t("Cancel")}
          </TextButton>
        </>
      ) : (
        <>
          <TextButton icon="plus" tooltip={t("AddActiveTip")} onSelect={() => addActivePrefab(library.id)}>
            {t("AddActive")}
          </TextButton>
          {findItAvailable ? (
            <FindItButton t={t} libraryId={library.id} />
          ) : (
            <TextButton disabled tooltip={t("FindItMissing")} className={styles.rowGap} onSelect={noop}>
              {t("OpenInFindIt")}
            </TextButton>
          )}
          <SquareButton icon="pen" label={t("Rename")} className={styles.rowGap} onSelect={() => setDraft(library.name)} />
          <SquareButton
            icon="save"
            label={t("Export")}
            tooltip={t("ExportTip")}
            className={styles.rowGap}
            onSelect={() => exportLibrary(library.id)}
          />
          <TextButton
            selected={sharing}
            tooltip={t("ShareTip")}
            className={styles.rowGap}
            onSelect={() => setSharing(!sharing)}
          >
            {t("Share")}
          </TextButton>
          <SquareButton
            icon="trash"
            label={t("Delete")}
            className={styles.rowGap}
            onSelect={() => setConfirmDelete(true)}
          />
        </>
      )}
    </div>
    {sharing && <ShareBar t={t} library={library} onClose={() => setSharing(false)} />}
    </>
  );
};

const noop = () => undefined;

// Mounted only when Find It is installed, so the FindIt binding is never read otherwise.
const FindItButton = ({ t, libraryId }: { t: Translate; libraryId: string }) => {
  const openInFindIt = useOpenInFindIt();

  return (
    <TextButton
      className={styles.rowGap}
      tooltip={t("OpenInFindItTip")}
      onSelect={() => {
        openInFindIt(libraryId);
        togglePanel();
      }}
    >
      {t("OpenInFindIt")}
    </TextButton>
  );
};
