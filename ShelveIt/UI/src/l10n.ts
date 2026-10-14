import { useCallback } from "react";
import { useLocalization } from "cs2/l10n";

export type Translate = (key: string, vars?: Record<string, string | number>) => string;

/** Translates ShelveIt.UI[key] and fills {NAME} placeholders. */
export function useT(): Translate {
  const { translate } = useLocalization();

  return useCallback(
    (key, vars) => {
      let text = translate(`ShelveIt.UI[${key}]`) ?? key;

      if (vars) {
        for (const name of Object.keys(vars)) {
          text = text.split(`{${name}}`).join(String(vars[name]));
        }
      }

      return text;
    },
    [translate]
  );
}
