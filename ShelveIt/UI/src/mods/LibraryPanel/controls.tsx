import { createContext, MouseEvent, PropsWithChildren, ReactNode, useContext, useEffect, useState } from "react";
import classNames from "classnames";
import * as ui from "cs2/ui";
import { Button, Dropdown, DropdownToggle, Icon, Tooltip } from "cs2/ui";
import { vanillaClasses } from "vanilla";
import styles from "./panel.module.scss";

interface DropdownItemProps {
  value: string;
  selected?: boolean;
  className?: string;
  onChange?: (value: string) => void;
  onToggleSelected?: (value: string) => void;
}

// cs2/ui exports the component, but its type declaration names an interface the same way.
const DropdownItem = (ui as unknown as { DropdownItem: (props: PropsWithChildren<DropdownItemProps>) => JSX.Element })
  .DropdownItem;

// Icons. The game's own glyphs come first. A few shapes the game does not ship (magnifier,
// funnel, minus, expand, minimize) come from the Unified Icon Library mod when it is installed,
// with a game glyph or a text label standing in when it is not.

export type IconName =
  | "search"
  | "filter"
  | "close"
  | "plus"
  | "minus"
  | "check"
  | "trash"
  | "pen"
  | "left"
  | "right"
  | "down"
  | "compact"
  | "expand"
  | "rescan"
  | "save"
  | "star"
  | "info";

const GLYPHS = "Media/Glyphs/";
const UIL = "coui://uil/Standard/";

const GAME_ICONS: Record<IconName, string | null> = {
  search: null,
  filter: null,
  close: GLYPHS + "Close.svg",
  plus: GLYPHS + "Plus.svg",
  minus: GLYPHS + "Close.svg",
  check: GLYPHS + "Checkmark.svg",
  trash: GLYPHS + "Trash.svg",
  pen: GLYPHS + "Pen.svg",
  left: GLYPHS + "ThickStrokeArrowLeft.svg",
  right: GLYPHS + "ThickStrokeArrowRight.svg",
  down: GLYPHS + "ThickStrokeArrowDown.svg",
  compact: GLYPHS + "ThickStrokeArrowDown.svg",
  expand: GLYPHS + "ThickStrokeArrowUp.svg",
  rescan: GLYPHS + "ArrowCircular.svg",
  save: GLYPHS + "Save.svg",
  star: GLYPHS + "StarFilled.svg",
  info: GLYPHS + "Info.svg",
};

const UIL_ICONS: Partial<Record<IconName, string>> = {
  search: UIL + "Magnifier.svg",
  filter: UIL + "FunnelFilter.svg",
  minus: UIL + "Minus.svg",
  compact: UIL + "ArrowsMinimize.svg",
  expand: UIL + "ArrowsExpand.svg",
};

// Probed once, the first time the panel opens.
let uilState: boolean | null = null;
let uilProbing = false;
const uilListeners = new Set<(available: boolean) => void>();

function probeUil() {
  if (uilState !== null || uilProbing) {
    return;
  }

  uilProbing = true;

  const done = (available: boolean) => {
    uilState = available;
    uilListeners.forEach((listener) => listener(available));
  };

  const image = new Image();

  image.onload = () => done(true);
  image.onerror = () => done(false);
  image.src = UIL + "Magnifier.svg";
}

/** True when the Unified Icon Library answers. Call it once, at the root of the panel. */
export function useUilAvailable(): boolean {
  const [available, setAvailable] = useState(uilState === true);

  useEffect(() => {
    if (uilState !== null) {
      setAvailable(uilState);
      return;
    }

    uilListeners.add(setAvailable);
    probeUil();

    return () => {
      uilListeners.delete(setAvailable);
    };
  }, []);

  return available;
}

export const UilContext = createContext(false);

/** The icon URL, or null when there is no drawing for it without the icon library. */
export function useIconSrc(name: IconName): string | null {
  const uil = useContext(UilContext);

  return (uil && UIL_ICONS[name]) || GAME_ICONS[name];
}

/** A monochrome icon tinted with --iconColor, the way the game tints its glyphs. */
export const Glyph = ({ name, className }: { name: IconName; className?: string }) => {
  const src = useIconSrc(name);

  return src ? <Icon tinted src={src} className={classNames(styles.glyph, className)} /> : null;
};

const stop = (e: MouseEvent) => e.stopPropagation();

interface TextButtonProps {
  className?: string;
  disabled?: boolean;
  selected?: boolean;
  danger?: boolean;
  tooltip?: ReactNode;
  icon?: IconName;
  onSelect: () => void;
}

/** A labelled button. The engine has no :disabled pseudo class, so the state is also a plain class. */
export const TextButton = ({
  className,
  disabled,
  selected,
  danger,
  tooltip,
  icon,
  onSelect,
  children,
}: PropsWithChildren<TextButtonProps>) => {
  const button = (
    <Button
      theme={BUTTON_THEME}
      className={classNames(disabled && styles.disabled, danger && styles.danger, className)}
      disabled={disabled}
      selected={selected}
      onSelect={onSelect}
      onMouseDown={stop}
    >
      {icon && <Glyph name={icon} className={styles.buttonGlyph} />}
      {children}
    </Button>
  );

  return tooltip ? <Tooltip tooltip={tooltip}>{button}</Tooltip> : button;
};

const BUTTON_THEME = { button: styles.button };

interface SquareButtonProps {
  icon: IconName;
  /** Shown instead of the icon when the icon has no drawing. */
  label: string;
  tooltip?: ReactNode;
  disabled?: boolean;
  selected?: boolean;
  badge?: number;
  className?: string;
  onSelect: () => void;
}

/** The square button of the game's tool options, with an optional count badge. */
export const SquareButton = ({
  icon,
  label,
  tooltip,
  disabled,
  selected,
  badge,
  className,
  onSelect,
}: SquareButtonProps) => {
  const src = useIconSrc(icon);

  const button = (
    <Button
      theme={SQUARE_THEME}
      className={classNames(
        vanillaClasses.toolButton || styles.squareFallback,
        styles.square,
        !src && styles.squareText,
        disabled && styles.disabled,
        className
      )}
      disabled={disabled}
      selected={selected}
      onSelect={onSelect}
      onMouseDown={stop}
    >
      {src ? <Icon tinted src={src} className={classNames(styles.glyph, styles.squareGlyph)} /> : label}
      {badge !== undefined && badge > 0 && <div className={styles.badge}>{badge}</div>}
    </Button>
  );

  return <Tooltip tooltip={tooltip ?? label}>{button}</Tooltip>;
};

const SQUARE_THEME = { button: "" };

interface RoundButtonProps {
  icon: IconName;
  tooltip: ReactNode;
  danger?: boolean;
  className?: string;
  onSelect: () => void;
}

/** A small round action on a row or a tile. Clicks do not reach the row. */
export const RoundButton = ({ icon, tooltip, danger, className, onSelect }: RoundButtonProps) => (
  <Tooltip tooltip={tooltip}>
    <div
      className={classNames(styles.round, danger && styles.roundDanger, className)}
      onMouseDown={stop}
      onClick={(e) => {
        e.stopPropagation();
        onSelect();
      }}
    >
      <Glyph name={icon} className={styles.roundGlyph} />
    </div>
  </Tooltip>
);

export interface SelectItem {
  id: string;
  label: string;
  note?: string | number;
}

interface SelectProps {
  label: ReactNode;
  items: SelectItem[];
  selectedId?: string;
  disabled?: boolean;
  emptyText?: string;
  className?: string;
  onPick: (id: string) => void;
}

let dropdownTheme: Record<string, string> | null = null;

// The game's in-game dropdown theme, with our sizes on top. Built on first use, after the
// registry lookups have run.
function getDropdownTheme() {
  if (!dropdownTheme) {
    const vanilla = vanillaClasses.dropdown;

    dropdownTheme = {
      ...vanilla,
      dropdownToggle: classNames(vanilla.dropdownToggle || styles.selectToggleFallback, styles.selectToggle),
      dropdownMenu: classNames(vanilla.dropdownMenu || styles.selectMenuFallback, styles.selectMenu),
      dropdownItem: classNames(
        (vanilla.dropdownItem ?? "").replace("undefined", "").trim() || styles.selectItemFallback,
        styles.selectItem
      ),
      label: styles.selectLabel,
    };
  }

  return dropdownTheme;
}

/** The game's dropdown: it opens in a portal, closes on Escape and on a click elsewhere. */
export const Select = ({ label, items, selectedId, disabled, emptyText, className, onPick }: SelectProps) => (
  <Dropdown
    theme={getDropdownTheme()}
    content={
      <>
        {items.length === 0 && emptyText && <div className={styles.selectEmpty}>{emptyText}</div>}
        {items.map((item) => (
          <DropdownItem
            key={item.id}
            value={item.id}
            selected={item.id === selectedId}
            onChange={onPick}
            onToggleSelected={onPick}
          >
            <div className={styles.selectItemName}>{item.label}</div>
            {item.note !== undefined && <div className={styles.selectItemNote}>{item.note}</div>}
          </DropdownItem>
        ))}
      </>
    }
  >
    <DropdownToggle disabled={disabled} className={classNames(disabled && styles.disabled, className)} onMouseDown={stop}>
      {label}
    </DropdownToggle>
  </Dropdown>
);

interface StateBlockProps {
  title: string;
  text?: string;
  actionLabel?: string;
  onAction?: () => void;
  compact?: boolean;
}

/** What an empty area says: what happened, and the one thing to do about it. */
export const StateBlock = ({ title, text, actionLabel, onAction, compact }: StateBlockProps) => (
  <div className={classNames(styles.state, compact && styles.stateCompact)}>
    <div className={styles.stateTitle}>{title}</div>
    {text && <div className={styles.stateText}>{text}</div>}
    {actionLabel && onAction && (
      <TextButton className={styles.stateAction} onSelect={onAction}>
        {actionLabel}
      </TextButton>
    )}
  </div>
);
