import { PropsWithChildren, useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { useValue } from "cs2/api";
import { Scrollable } from "cs2/ui";
import {
  clearCommunityAction,
  clipboard$,
  CommunityAction,
  communityAction$,
  communityCloseLibrary,
  CommunityDetail,
  communityDetail$,
  communityImport,
  CommunityItem,
  communityList$,
  communityOpen,
  communityOpenLibrary,
  communityReport,
  communitySearch,
  CommunitySort,
  importShareCode,
  openPackagePage,
  packageLoader$,
  pdxStatus$,
  readClipboard,
  ReportReason,
  select,
  subscribeMissing,
} from "bindings";
import { Translate } from "l10n";
import { TextInput } from "vanilla";
import { Glyph, Select, SquareButton, StateBlock, TextButton } from "./controls";
import { formatCount } from "./FilterBar";
import styles from "./panel.module.scss";

export const COMMUNITY_SORTS: CommunitySort[] = ["popular", "downloads", "newest"];
const SORT_KEYS: Record<CommunitySort, string> = {
  popular: "SortPopular",
  downloads: "SortDownloads",
  newest: "SortNewest",
};
const REASONS: ReportReason[] = ["spam", "offensive", "broken", "other"];
const SEARCH_DELAY = 400;

const ERROR_KEYS: Record<string, string> = {
  network: "ErrNetwork",
  timeout: "ErrTimeout",
  rate: "ErrRate",
  rejected: "ErrRejected",
  toolarge: "ErrTooLarge",
  notfound: "ErrNotFound",
  forbidden: "ErrForbidden",
  server: "ErrServer",
  name: "ErrName",
  empty: "ErrEmpty",
  notloggedin: "ErrNotLoggedIn",
  unavailable: "ErrPdxUnavailable",
  subscribe: "ErrSubscribe",
  clipboard: "ErrClipboard",
  "code-format": "ErrCodeFormat",
  "code-schema": "ErrCodeSchema",
  "code-empty": "ErrCodeEmpty",
};

/** An ISO 8601 UTC time as a local date, e.g. 2026-10-14. */
export function formatDate(iso: string): string {
  const date = new Date(iso);

  if (!iso || isNaN(date.getTime())) {
    return "";
  }

  const pad = (n: number) => (n < 10 ? "0" + n : String(n));

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** What went wrong, in the player's language. A filter or validation rejection shows the server's own words. */
export function errorText(t: Translate, error: string, message: string): string {
  if ((error === "rejected" || error === "forbidden") && message) {
    return message;
  }

  return t(ERROR_KEYS[error] ?? "ErrServer");
}

export const isOffline = (error: string) => error === "network" || error === "timeout";

// Sidebar body of the Community tab: the sort order as rows, and where the player stands.

export const CommunitySidebar = ({ t }: { t: Translate }) => {
  const list = useValue(communityList$);
  const pdx = useValue(pdxStatus$);

  return (
    <div className={styles.tabBody}>
      <Scrollable vertical smooth trackVisibility="scrollable" className={styles.sidebarScroll}>
        {COMMUNITY_SORTS.map((sort) => (
          <div
            key={sort}
            className={classNames(styles.row, list.sort === sort && styles.selected)}
            onClick={() => communitySearch(list.query, sort, 0)}
          >
            <div className={styles.rowName}>{t(SORT_KEYS[sort])}</div>
          </div>
        ))}
        <div className={styles.sideNote}>
          <div className={styles.sideNoteLine}>
            {t(list.server === "offline" ? "ServerOffline" : list.server === "online" ? "ServerOnline" : "ServerUnknown")}
          </div>
          <div className={styles.sideNoteLine}>
            {!pdx.available
              ? t("PdxUnavailable")
              : pdx.loggedIn
              ? t("PdxSignedInAs", { NAME: pdx.name || "…" })
              : t("PdxSignedOut")}
          </div>
          <div className={styles.sideNoteLine}>{t("CommunityPrivacy")}</div>
        </div>
      </Scrollable>
    </div>
  );
};

// Main column of the Community tab.

export const CommunityView = ({ t, onUseCode }: { t: Translate; onUseCode: () => void }) => {
  const detail = useValue(communityDetail$);

  // Opening the tab is what contacts the server, once per visit.
  useEffect(() => {
    communityOpen();
  }, []);

  return detail.state === "none" ? (
    <CommunityBrowser t={t} onUseCode={onUseCode} />
  ) : (
    <DetailView t={t} detail={detail} />
  );
};

const CommunityBrowser = ({ t, onUseCode }: { t: Translate; onUseCode: () => void }) => {
  const list = useValue(communityList$);
  const pageCount = Math.max(1, Math.ceil(list.total / Math.max(1, list.pageSize)));

  let body: JSX.Element;

  if (list.state === "error") {
    body = isOffline(list.error) ? (
      <StateBlock
        title={t("ServerUnreachable")}
        text={t("ServerUnreachableHint")}
        actionLabel={t("UseShareCode")}
        onAction={onUseCode}
      />
    ) : (
      <StateBlock
        title={t(list.error === "rate" ? "RateLimitedTitle" : "CommunityErrorTitle")}
        text={errorText(t, list.error, list.message)}
        actionLabel={t("TryAgain")}
        onAction={() => communitySearch(list.query, list.sort, list.page)}
      />
    );
  } else if (list.state === "ready" && list.items.length === 0) {
    body =
      list.query.length > 0 ? (
        <StateBlock
          title={t("NoResultsTitle")}
          text={t("NoResults", { QUERY: list.query })}
          actionLabel={t("ClearSearch")}
          onAction={() => communitySearch("", list.sort, 0)}
        />
      ) : (
        <StateBlock title={t("NoCommunityTitle")} text={t("NoCommunity")} />
      );
  } else if (list.state !== "ready" && list.items.length === 0) {
    body = <StateBlock title={t("Loading")} />;
  } else {
    body = (
      <Scrollable vertical smooth trackVisibility="scrollable" className={styles.listScroll}>
        {list.items.map((item) => (
          <CommunityRow key={item.id} t={t} item={item} />
        ))}
      </Scrollable>
    );
  }

  return (
    <div className={styles.main}>
      <div className={styles.header}>
        <div className={styles.headerName}>{t("Community")}</div>
        <div className={styles.headerNote}>{t(SORT_KEYS[list.sort] ?? "SortPopular")}</div>
      </div>
      <div className={styles.filterBar}>
        <div className={styles.filterRow}>
          <CommunitySearchField t={t} query={list.query} sort={list.sort} />
        </div>
      </div>
      <div className={styles.resultBar}>
        <div className={styles.resultCount}>
          {list.state === "loading"
            ? t("Loading")
            : list.state === "ready"
            ? t("LibraryCount", { COUNT: formatCount(t, list.total) })
            : ""}
        </div>
        <div className={styles.spacer} />
        <div className={styles.columnHead}>{t("ColAssets")}</div>
        <div className={styles.columnHead}>{t("ColPackages")}</div>
        <div className={styles.columnHead}>{t("ColDownloads")}</div>
      </div>
      <div className={styles.gridArea}>
        {body}
        {list.total > list.pageSize && (
          <div className={styles.pager}>
            <SquareButton
              icon="left"
              label={t("PreviousPage")}
              disabled={list.page <= 0 || list.state === "loading"}
              onSelect={() => communitySearch(list.query, list.sort, list.page - 1)}
            />
            <div className={styles.pagerLabel}>{t("Page", { PAGE: list.page + 1, TOTAL: pageCount })}</div>
            <SquareButton
              icon="right"
              label={t("NextPage")}
              disabled={list.page >= pageCount - 1 || list.state === "loading"}
              onSelect={() => communitySearch(list.query, list.sort, list.page + 1)}
            />
          </div>
        )}
      </div>
    </div>
  );
};

const CommunitySearchField = ({ t, query, sort }: { t: Translate; query: string; sort: CommunitySort }) => {
  const [text, setText] = useState(query);
  const timer = useRef<number | undefined>(undefined);
  const sortRef = useRef(sort);

  sortRef.current = sort;

  useEffect(() => () => window.clearTimeout(timer.current), []);

  // Searching is a request to the server, so it waits for a pause in typing.
  const change = (value: string, now?: boolean) => {
    setText(value);
    window.clearTimeout(timer.current);

    const send = () => {
      if (value.trim() !== query) {
        communitySearch(value.trim(), sortRef.current, 0);
      }
    };

    if (now) {
      send();
    } else {
      timer.current = window.setTimeout(send, SEARCH_DELAY);
    }
  };

  return (
    <div className={styles.searchField}>
      <Glyph name="search" className={styles.searchGlyph} />
      <TextInput
        className={classNames(styles.textInput, styles.searchInput)}
        value={text}
        placeholder={t("CommunitySearchPlaceholder")}
        onChange={(e) => change(e.target.value)}
        onKeyDown={(e) => {
          if (e.keyCode === 13) {
            change(text, true);
          }
        }}
      />
      {text.length > 0 && (
        <div className={styles.searchClear} onClick={() => change("", true)}>
          <Glyph name="close" className={styles.searchClearGlyph} />
        </div>
      )}
    </div>
  );
};

const CommunityRow = ({ t, item }: { t: Translate; item: CommunityItem }) => (
  <div className={styles.communityRow} onClick={() => communityOpenLibrary(item.id)}>
    <div className={styles.communityText}>
      <div className={styles.communityTitleLine}>
        <div className={styles.communityName}>{item.name}</div>
        <div className={styles.communityAuthor}>{t("ByAuthor", { NAME: item.author })}</div>
      </div>
      {(item.description.length > 0 || item.tags.length > 0) && (
        <div className={styles.communityMetaLine}>
          {item.tags.map((tag) => (
            <div key={tag} className={styles.tag}>
              {tag}
            </div>
          ))}
          <div className={styles.communityDescription}>{item.description}</div>
        </div>
      )}
    </div>
    <div className={styles.columnValue}>{formatCount(t, item.assetCount)}</div>
    <div className={styles.columnValue}>{formatCount(t, item.packageCount)}</div>
    <div className={styles.columnValue}>{formatCount(t, item.downloads)}</div>
  </div>
);

// Detail of one library, from the server or from a share code.

const DetailView = ({ t, detail }: { t: Translate; detail: CommunityDetail }) => {
  const action = useValue(communityAction$);
  const pdx = useValue(pdxStatus$);
  const [reporting, setReporting] = useState(false);
  const fromServer = detail.source === "server";
  const busy = action.state === "busy";

  if (detail.state !== "ready") {
    return (
      <div className={styles.main}>
        <DetailHeader t={t} name={detail.name} author={detail.author} />
        {detail.state === "loading" ? (
          <StateBlock title={t("Loading")} />
        ) : (
          <StateBlock
            title={t(isOffline(detail.error) ? "ServerUnreachable" : "CommunityErrorTitle")}
            text={isOffline(detail.error) ? t("ServerUnreachableHint") : errorText(t, detail.error, detail.message)}
            actionLabel={t("TryAgain")}
            onAction={() => communityOpenLibrary(detail.id)}
          />
        )}
      </div>
    );
  }

  const imported = detail.importedId.length > 0;

  return (
    <div className={styles.main}>
      <DetailHeader t={t} name={detail.name} author={fromServer ? detail.author : ""} note={fromServer ? undefined : t("FromShareCode")}>
        {imported ? (
          <TextButton icon="check" tooltip={t("OpenLibraryTip")} onSelect={() => select(detail.importedId)}>
            {t("OpenLibrary")}
          </TextButton>
        ) : (
          <TextButton
            icon="plus"
            tooltip={t("ImportLibraryTip")}
            disabled={busy}
            onSelect={communityImport}
          >
            {action.kind === "import" && busy ? t("Importing") : t("ImportLibrary")}
          </TextButton>
        )}
        {fromServer && (
          <TextButton
            className={styles.rowGap}
            disabled={detail.reported}
            selected={reporting}
            tooltip={detail.reported ? t("ReportedTip") : t("ReportTip")}
            onSelect={() => setReporting(!reporting)}
          >
            {detail.reported ? t("Reported") : t("Report")}
          </TextButton>
        )}
      </DetailHeader>

      {reporting && !detail.reported && <ReportBar t={t} busy={busy} onClose={() => setReporting(false)} />}
      <ActionLine t={t} action={action} kinds={["import", "report", "code"]} />

      <Scrollable vertical smooth trackVisibility="scrollable" className={styles.listScroll}>
        <div className={styles.detailBody}>
          {detail.description.length > 0 && <div className={styles.detailDescription}>{detail.description}</div>}
          {detail.tags.length > 0 && (
            <div className={styles.tagRow}>
              {detail.tags.map((tag) => (
                <div key={tag} className={styles.tag}>
                  {tag}
                </div>
              ))}
            </div>
          )}

          <div className={styles.factRow}>
            <Fact label={t("ColAssets")} value={formatCount(t, detail.assetCount)} />
            <Fact label={t("Installed")} value={formatCount(t, detail.installed)} />
            <Fact label={t("Missing")} value={formatCount(t, detail.missing)} warn={detail.missing > 0} />
            <Fact label={t("ColPackages")} value={formatCount(t, detail.packageCount)} />
            {fromServer && <Fact label={t("ColDownloads")} value={formatCount(t, detail.downloads)} />}
            {fromServer && formatDate(detail.updatedAt) && <Fact label={t("Updated")} value={formatDate(detail.updatedAt)} />}
          </div>

          {detail.thumbs.length > 0 && (
            <div className={styles.previewRow}>
              {detail.thumbs.map((thumb) => (
                <img key={thumb} className={styles.previewThumb} src={thumb} />
              ))}
            </div>
          )}

          {!detail.scanned && <div className={styles.detailNote}>{t("NotScannedYet")}</div>}

          <MissingPackages t={t} detail={detail} action={action} canUsePdx={pdx.available} />
        </div>
      </Scrollable>
    </div>
  );
};

interface DetailHeaderProps {
  t: Translate;
  name: string;
  author: string;
  note?: string;
}

const DetailHeader = ({ t, name, author, note, children }: PropsWithChildren<DetailHeaderProps>) => (
  <div className={styles.header}>
    <SquareButton icon="left" label={t("Back")} tooltip={t("BackToList")} onSelect={communityCloseLibrary} />
    <div className={classNames(styles.headerName, styles.headerNameAfterButton)}>{name || t("Community")}</div>
    {author.length > 0 && <div className={styles.headerAuthor}>{t("ByAuthor", { NAME: author })}</div>}
    {note && <div className={styles.headerNote}>{note}</div>}
    <div className={styles.spacer} />
    {children}
  </div>
);

const Fact = ({ label, value, warn }: { label: string; value: string; warn?: boolean }) => (
  <div className={styles.fact}>
    <div className={classNames(styles.factValue, warn && styles.factWarn)}>{value}</div>
    <div className={styles.factLabel}>{label}</div>
  </div>
);

const ReportBar = ({ t, busy, onClose }: { t: Translate; busy: boolean; onClose: () => void }) => {
  const [reason, setReason] = useState<ReportReason>("spam");
  const [note, setNote] = useState("");

  return (
    <div className={styles.resultBar}>
      <div className={styles.resultLabel}>{t("ReportReason")}</div>
      <Select
        label={t("Reason." + reason)}
        items={REASONS.map((id) => ({ id, label: t("Reason." + id) }))}
        selectedId={reason}
        onPick={(id) => setReason(id as ReportReason)}
      />
      <TextInput
        className={classNames(styles.textInput, styles.nameInput, styles.rowGap)}
        value={note}
        placeholder={t("ReportNote")}
        onChange={(e) => setNote(e.target.value)}
      />
      <TextButton danger disabled={busy} className={styles.rowGap} onSelect={() => communityReport(reason, note)}>
        {t("SendReport")}
      </TextButton>
      <TextButton className={styles.rowGap} onSelect={onClose}>
        {t("Cancel")}
      </TextButton>
    </div>
  );
};

interface MissingPackagesProps {
  t: Translate;
  detail: CommunityDetail;
  action: CommunityAction;
  canUsePdx: boolean;
}

const MissingPackages = ({ t, detail, action, canUsePdx }: MissingPackagesProps) => {
  const [confirm, setConfirm] = useState(false);
  const count = detail.packages.length;
  const subscribed = action.kind === "subscribe" && action.state === "done";
  const subscribing = action.kind === "subscribe" && action.state === "busy";
  const loader = useValue(packageLoader$);
  const loadState = (id: number) => loader.items.find((item) => item.id === id)?.state;

  if (count === 0) {
    return detail.missing > 0 ? (
      <div className={styles.detailNote}>{t("MissingNoPackages", { COUNT: formatCount(t, detail.missing) })}</div>
    ) : null;
  }

  return (
    <div className={styles.section}>
      <div className={styles.sectionHead}>
        <div className={styles.sectionTitle}>{t("MissingPackages", { COUNT: count })}</div>
        <div className={styles.spacer} />
        {canUsePdx && !confirm && !subscribed && (
          <TextButton disabled={subscribing} onSelect={() => setConfirm(true)}>
            {subscribing ? t("Subscribing") : t("SubscribeAll")}
          </TextButton>
        )}
      </div>

      {confirm && (
        <div className={styles.confirmBlock}>
          <div className={styles.confirmBlockText}>{t("ConfirmSubscribe", { COUNT: count })}</div>
          <div className={styles.confirmBlockActions}>
            <TextButton
              onSelect={() => {
                setConfirm(false);
                subscribeMissing();
              }}
            >
              {t("SubscribeConfirm", { COUNT: count })}
            </TextButton>
            <TextButton className={styles.rowGap} onSelect={() => setConfirm(false)}>
              {t("Cancel")}
            </TextButton>
          </div>
        </div>
      )}

      <ActionLine t={t} action={action} kinds={["subscribe", "page"]} />
      {!canUsePdx && <div className={styles.detailNote}>{t("PdxUnavailableHint")}</div>}

      {detail.packages.map((pkg) => (
        <div key={pkg.id} className={styles.packageRow}>
          <div className={styles.rowName}>{pkg.title || t("PackageId", { ID: pkg.id })}</div>
          <div className={styles.packageId}>{pkg.id}</div>
          {loadState(pkg.id) && (
            <div className={classNames(styles.packageState, loadState(pkg.id) === "failed" && styles.packageStateWarn)}>
              {t(PACKAGE_STATE_KEYS[loadState(pkg.id)!])}
            </div>
          )}
          {canUsePdx && (
            <TextButton className={styles.rowGap} onSelect={() => openPackagePage(pkg.id)}>
              {t("OpenPage")}
            </TextButton>
          )}
        </div>
      ))}
    </div>
  );
};

const PACKAGE_STATE_KEYS: Record<string, string> = {
  downloading: "PackageDownloading",
  loading: "PackageLoading",
  loaded: "PackageLoaded",
  failed: "PackageFailed",
};

const DONE_KEYS: Record<string, string> = {
  import: "DoneImport",
  code: "DoneCode",
  report: "DoneReport",
  subscribe: "DoneSubscribe",
  publish: "DonePublish",
  unpublish: "DoneUnpublish",
  copy: "DoneCopy",
  forget: "DoneForget",
};

/** One line about the last community operation of the given kinds. */
export const ActionLine = ({ t, action, kinds }: { t: Translate; action: CommunityAction; kinds: string[] }) => {
  if (!kinds.includes(action.kind) || action.state === "" || action.state === "busy") {
    return null;
  }

  const error = action.state === "error";
  let text = error ? errorText(t, action.error, action.message) : t(DONE_KEYS[action.kind] ?? "Done", { COUNT: action.count });

  if (error && isOffline(action.error) && (action.kind === "publish" || action.kind === "import")) {
    text += " " + t("UseShareCodeHint");
  }

  return (
    <div className={classNames(styles.actionLine, error && styles.actionLineError)}>
      <div className={styles.actionText}>{text}</div>
      <div className={styles.actionClose} onClick={clearCommunityAction}>
        <Glyph name="close" className={styles.searchClearGlyph} />
      </div>
    </div>
  );
};

// Footer row for share codes: type or paste a code, then import it.

export const CodeImportRow = ({ t, onClose, onImported }: { t: Translate; onClose: () => void; onImported: () => void }) => {
  const [code, setCode] = useState("");
  const clipboard = useValue(clipboard$);
  const action = useValue(communityAction$);
  const seenClipboard = useRef(clipboard.seq);
  const sent = useRef(false);

  useEffect(() => {
    if (clipboard.seq !== seenClipboard.current) {
      seenClipboard.current = clipboard.seq;
      setCode(clipboard.text.trim());
    }
  }, [clipboard.seq]);

  useEffect(() => {
    if (sent.current && action.kind === "code" && action.state === "done") {
      sent.current = false;
      onImported();
    }
  }, [action.kind, action.state]);

  const trimmed = code.trim();
  const failed = action.kind === "code" && action.state === "error";

  const submit = () => {
    if (trimmed.length > 0) {
      sent.current = true;
      importShareCode(trimmed);
    }
  };

  return (
    <div className={styles.codeArea}>
      {failed && <div className={styles.codeError}>{errorText(t, action.error, action.message)}</div>}
      <div className={styles.footer}>
        <div className={styles.codeLabel}>{t("ShareCode")}</div>
        <TextInput
          className={classNames(styles.textInput, styles.codeInput)}
          value={code}
          placeholder={t("ShareCodePlaceholder")}
          onChange={(e) => setCode(e.target.value)}
          onKeyDown={(e) => {
            if (e.keyCode === 13) {
              submit();
            }
          }}
        />
        <TextButton className={styles.rowGap} tooltip={t("PasteTip")} onSelect={readClipboard}>
          {t("Paste")}
        </TextButton>
        <TextButton className={styles.rowGap} disabled={trimmed.length === 0} onSelect={submit}>
          {t("ImportLibrary")}
        </TextButton>
        <TextButton className={styles.rowGap} onSelect={onClose}>
          {t("Cancel")}
        </TextButton>
      </div>
    </div>
  );
};
