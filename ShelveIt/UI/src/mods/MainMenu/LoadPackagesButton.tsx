import { useEffect } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import {
  checkPendingPackages,
  dismissPackageRestart,
  exitApplication,
  loadPendingPackages,
  packageLoader$,
} from "bindings";
import { useT } from "l10n";
import styles from "./mainMenu.module.scss";

const ICON_LOAD = "Media/Glyphs/Plus.svg";
const ICON_QUIT = "Media/Glyphs/Close.svg";

/**
 * "Load new asset packages (N)" in the main-menu column, shown only when downloaded packages are
 * waiting. Styled as the vanilla menu items (cs2/ui Button with the menu variant).
 */
export const LoadPackagesButton = () => {
  const t = useT();
  const loader = useValue(packageLoader$);

  // The main menu was shown: count the pending packages once.
  useEffect(() => {
    checkPendingPackages();
  }, []);

  if (!loader.available) {
    return null;
  }

  if (loader.state === "restart") {
    return (
      <>
        <div className={styles.note}>{t("PackagesNeedRestart", { COUNT: loader.restart })}</div>
        <Button variant="menu" src={ICON_QUIT} tinted onSelect={exitApplication}>
          {t("QuitGame")}
        </Button>
        <Button variant="menu" src={ICON_LOAD} tinted onSelect={dismissPackageRestart}>
          {t("Later")}
        </Button>
      </>
    );
  }

  const loading = loader.state === "loading";

  if (!loading && loader.pending <= 0) {
    return null;
  }

  return (
    <Button variant="menu" src={ICON_LOAD} tinted disabled={loading} onSelect={loadPendingPackages}>
      {loading ? t("LoadingPackages") : t("LoadNewPackages", { COUNT: loader.pending })}
    </Button>
  );
};
