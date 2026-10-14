import { ModRegistrar } from "cs2/modding";
import { LibraryPanelHost } from "mods/LibraryPanel/LibraryPanel";
import { ToolbarButton } from "mods/ToolbarButton/ToolbarButton";
import { LoadPackagesButton } from "mods/MainMenu/LoadPackagesButton";
import { resolveVanillaComponents } from "vanilla";

const register: ModRegistrar = (moduleRegistry) => {
  resolveVanillaComponents(moduleRegistry);

  // Button row at the top left of the game screen.
  moduleRegistry.append("GameTopLeft", ToolbarButton);

  // Renders nothing until PanelVisible is true.
  moduleRegistry.append("Game", LibraryPanelHost);

  // Main-menu column: a button for downloaded asset packages that are not loaded yet.
  moduleRegistry.extend(
    "game-ui/menu/components/main-menu-screen/main-menu-screen.tsx",
    "MainMenuNavigation",
    (Component) => (props) => {
      const { children, ...rest } = props;

      return (
        <Component {...rest}>
          <LoadPackagesButton />
          {children}
        </Component>
      );
    }
  );
};

export default register;
