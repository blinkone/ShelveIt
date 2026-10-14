import { useEffect, useState } from "react";
import classNames from "classnames";
import { useValue } from "cs2/api";
import {
  communityAction$,
  communityForgetPublished,
  communityPublish,
  communityRefreshAccount,
  communityUnpublish,
  copyShareCode,
  Library,
  pdxStatus$,
  published$,
} from "bindings";
import { Translate } from "l10n";
import { TextInput } from "vanilla";
import { ActionLine } from "./Community";
import { TextButton } from "./controls";
import styles from "./panel.module.scss";

const StateLine = ({ text }: { text: string }) => <div className={styles.shareNote}>{text}</div>;

interface ShareBarProps {
  t: Translate;
  library: Library;
  onClose: () => void;
}

// Sharing of one library: publish to the community server (or update and unpublish what this
// install published), and the offline share code. Opening it reads the Paradox account only.
export const ShareBar = ({ t, library, onClose }: ShareBarProps) => {
  const published = useValue(published$).includes(library.id);
  const pdx = useValue(pdxStatus$);
  const action = useValue(communityAction$);
  const [description, setDescription] = useState(library.description ?? "");
  const [tags, setTags] = useState((library.tags ?? []).join(", "));
  const [confirmUnpublish, setConfirmUnpublish] = useState(false);

  // Starts from what was last published or imported for this library.
  useEffect(() => {
    setDescription(library.description ?? "");
    setTags((library.tags ?? []).join(", "));
    setConfirmUnpublish(false);
  }, [library.id]);

  useEffect(() => {
    communityRefreshAccount();
  }, []);

  const mine = action.libraryId === library.id;
  const busy = mine && action.state === "busy";
  const canPublish = pdx.available && pdx.loggedIn && !busy;
  const tooShort = library.name.trim().length < 3;

  // A 403 on update or unpublish: the server no longer accepts this install's token.
  const tokenRejected =
    published && mine && action.state === "error" && action.error === "forbidden" && (action.kind === "publish" || action.kind === "unpublish");

  let status: JSX.Element | null = null;

  if (mine && action.state === "busy") {
    status = <StateLine text={t(action.kind === "unpublish" ? "Unpublishing" : "Publishing")} />;
  } else if (mine && action.state !== "") {
    status = <ActionLine t={t} action={action} kinds={["publish", "unpublish", "copy", "forget"]} />;
  } else if (!pdx.available) {
    status = <StateLine text={t("PublishNeedsPdx")} />;
  } else if (!pdx.loggedIn) {
    status = <StateLine text={t("PublishNeedsLogin")} />;
  } else if (tooShort) {
    status = <StateLine text={t("ErrName")} />;
  } else {
    status = (
      <StateLine text={t(published ? "PublishedAs" : "PublishAs", { NAME: pdx.name || "…" })} />
    );
  }

  return (
    <div className={styles.shareBar}>
      <div className={styles.shareRow}>
        <TextInput
          className={classNames(styles.textInput, styles.shareDescription)}
          value={description}
          placeholder={t("DescriptionPlaceholder")}
          onChange={(e) => setDescription(e.target.value.substring(0, 500))}
        />
        <TextInput
          className={classNames(styles.textInput, styles.shareTags, styles.rowGap)}
          value={tags}
          placeholder={t("TagsPlaceholder")}
          onChange={(e) => setTags(e.target.value)}
        />
      </div>
      <div className={styles.shareRow}>
        <div className={styles.shareStatus}>{status}</div>
        {confirmUnpublish ? (
          <>
            <div className={styles.confirmText}>{t("ConfirmUnpublish")}</div>
            <TextButton
              danger
              onSelect={() => {
                setConfirmUnpublish(false);
                communityUnpublish(library.id);
              }}
            >
              {t("Unpublish")}
            </TextButton>
            <TextButton className={styles.rowGap} onSelect={() => setConfirmUnpublish(false)}>
              {t("Cancel")}
            </TextButton>
          </>
        ) : (
          <>
            {tokenRejected && (
              <TextButton tooltip={t("ForgetPublishedTip")} onSelect={() => communityForgetPublished(library.id)}>
                {t("ForgetPublished")}
              </TextButton>
            )}
            <TextButton
              disabled={!canPublish || tooShort}
              className={tokenRejected ? styles.rowGap : undefined}
              tooltip={pdx.loggedIn ? t(published ? "UpdateTip" : "PublishTip") : t("PublishNeedsLogin")}
              onSelect={() => communityPublish(library.id, description, tags)}
            >
              {t(published ? "Update" : "Publish")}
            </TextButton>
            {published && (
              <TextButton className={styles.rowGap} disabled={busy} onSelect={() => setConfirmUnpublish(true)}>
                {t("Unpublish")}
              </TextButton>
            )}
            <TextButton className={styles.rowGap} tooltip={t("CopyCodeTip")} onSelect={() => copyShareCode(library.id)}>
              {t("CopyCode")}
            </TextButton>
            <TextButton className={styles.rowGap} onSelect={onClose}>
              {t("Close")}
            </TextButton>
          </>
        )}
      </div>
    </div>
  );
};
