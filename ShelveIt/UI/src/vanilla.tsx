import { ChangeEvent, FocusEvent, forwardRef, HTMLAttributes, KeyboardEvent, PropsWithChildren, Ref } from "react";
import { ModuleRegistry } from "cs2/modding";

// Game components and class names that cs2/ui does not export. They are looked up in the module
// registry when the mod registers. Every lookup has a fallback, so a game update that moves a
// module costs the vanilla look of that part and nothing else.

export interface TextInputProps {
  value?: string;
  placeholder?: string;
  className?: string;
  disabled?: boolean;
  selectAllOnFocus?: boolean;
  onChange?: (e: ChangeEvent<HTMLInputElement>) => void;
  onKeyDown?: (e: KeyboardEvent<HTMLInputElement>) => void;
  onFocus?: (e: FocusEvent<HTMLInputElement>) => void;
  onBlur?: (e: FocusEvent<HTMLInputElement>) => void;
  ref?: Ref<HTMLInputElement>;
}

interface TitleBarProps extends HTMLAttributes<HTMLDivElement> {
  /** Shown at the left of the title, like the vanilla info panels. */
  icon?: string;
}

type TextInputComponent = (props: TextInputProps) => JSX.Element | null;
type TitleBarComponent = (props: PropsWithChildren<TitleBarProps>) => JSX.Element | null;

// Same idea as the game's TextInput: key presses stay inside the field, Enter and Escape leave it.
// The engine itself tells the game that a text field has focus, which suspends hotkeys.
const FallbackTextInput = forwardRef<HTMLInputElement, TextInputProps>(
  ({ onKeyDown, selectAllOnFocus, ...props }, ref) => (
    <input
      {...props}
      ref={ref}
      type="text"
      onKeyDown={(e) => {
        if (e.keyCode === 13 || e.keyCode === 27) {
          e.currentTarget.blur();
        }

        e.stopPropagation();
        onKeyDown?.(e);
      }}
    />
  )
);

const FallbackTitleBar: TitleBarComponent = ({ children, icon, ...props }) => <div {...props}>{children}</div>;

/** Class names of the game's own style modules. Empty strings until resolved, or when a module is missing. */
export interface VanillaClasses {
  /** Theme of the dropdowns in the in-game panels (tool options, info panels). */
  dropdown: Record<string, string>;
  /** Tile of the build menu grid, with its hover, pressed and selected states. */
  gridItem: string;
  gridThumbnail: string;
  /** Square 28rem button of the tool options panel. */
  toolButton: string;
  toolButtonIcon: string;
  /** Tab row of the centre panels (economy, progression). */
  tabBar: string;
  tab: string;
}

export const vanillaClasses: VanillaClasses = {
  dropdown: {},
  gridItem: "",
  gridThumbnail: "",
  toolButton: "",
  toolButtonIcon: "",
  tabBar: "",
  tab: "",
};

const vanilla: { TextInput: TextInputComponent; PanelTitleBar: TitleBarComponent } = {
  TextInput: FallbackTextInput as unknown as TextInputComponent,
  PanelTitleBar: FallbackTitleBar,
};

export function resolveVanillaComponents(registry: ModuleRegistry) {
  const classesOf = (path: string): Record<string, string> => {
    const classes = registry.registry.get(path)?.classes;

    if (!classes) {
      console.warn(`[ShelveIt] game style module not found: ${path}`);
    }

    return classes ?? {};
  };

  const textInput = registry.registry.get("game-ui/common/input/text/text-input.tsx")?.TextInput;
  const titleBar = registry.registry.get("game-ui/common/panel/panel-title-bar.tsx")?.PanelTitleBar;

  if (textInput) {
    vanilla.TextInput = textInput;
  } else {
    console.warn("[ShelveIt] game TextInput not found, using a plain input");
  }

  if (titleBar) {
    vanilla.PanelTitleBar = titleBar;
  } else {
    console.warn("[ShelveIt] game PanelTitleBar not found, using a plain title");
  }

  const itemGrid = classesOf("game-ui/game/components/item-grid/item-grid.module.scss");
  const toolButton = classesOf("game-ui/game/components/tool-options/tool-button/tool-button.module.scss");
  const tabs = classesOf("game-ui/common/tabs/tabs.module.scss");

  vanillaClasses.dropdown = classesOf("game-ui/game/themes/game-dropdown.module.scss");
  vanillaClasses.gridItem = itemGrid.item ?? "";
  vanillaClasses.gridThumbnail = itemGrid.thumbnail ?? "";
  vanillaClasses.toolButton = toolButton.button ?? "";
  vanillaClasses.toolButtonIcon = toolButton.icon ?? "";
  vanillaClasses.tabBar = tabs.tabBar ?? "";
  vanillaClasses.tab = tabs.tab ?? "";
}

export const TextInput = forwardRef<HTMLInputElement, TextInputProps>((props, ref) => {
  const Component = vanilla.TextInput;

  return <Component {...props} ref={ref} />;
});

export const PanelTitleBar: TitleBarComponent = (props) => {
  const Component = vanilla.PanelTitleBar;

  return <Component {...props} />;
};
