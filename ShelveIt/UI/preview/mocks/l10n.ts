import en from "../../../Locale/en-US.json";
import it from "../../../Locale/it-IT.json";

// Stand-in for cs2/l10n, reading the mod's own locale files. ?locale=it switches language.

const italian = new URLSearchParams(location.search).get("locale") === "it";
const dictionary: Record<string, string> = italian ? (it as Record<string, string>) : (en as Record<string, string>);
const translate = (key: string) => dictionary[key] ?? null;

export function useLocalization() {
  return { translate };
}
