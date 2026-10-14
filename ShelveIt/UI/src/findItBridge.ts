import { useCallback } from "react";
import { bindValue, trigger, useValue } from "cs2/api";

// Everything that talks to the Find It mod lives in this file. None of it has been run in game.
//
// Names come from Find It's FindItUISystem (binding group = Mod.Id = "FindIt"):
//   value   ShowFindItPanel : bool
//   trigger FindItIconToggled()          toggles the panel (closing also clears the active tool)
//   trigger SearchChanged(string text)   sets the search text and starts a search
//
// Our C# side registers a search hook, so the text "lib:<library id>" lists that library.

const FIND_IT = "FindIt";
const SEARCH_PREFIX = "lib:";

const showFindItPanel$ = bindValue<boolean>(FIND_IT, "ShowFindItPanel", false);

/**
 * Returns a callback that shows the given library in Find It.
 * Mount the calling component only when Find It is installed (FindItAvailable).
 */
export function useOpenInFindIt(): (libraryId: string) => void {
  const findItShown = useValue(showFindItPanel$);

  return useCallback(
    (libraryId: string) => {
      // FindItIconToggled is a toggle: send it only when the panel is closed.
      if (!findItShown) {
        trigger(FIND_IT, "FindItIconToggled");
      }

      trigger(FIND_IT, "SearchChanged", SEARCH_PREFIX + libraryId);
    },
    [findItShown]
  );
}
