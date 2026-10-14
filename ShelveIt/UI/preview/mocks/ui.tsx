import React, {
  cloneElement,
  createContext,
  forwardRef,
  ReactElement,
  ReactNode,
  useCallback,
  useContext,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";
import classMaps from "../data/classes.json";

// Stand-ins for the components of cs2/ui. They reproduce the DOM and the class names of the
// game's components (read from the installed bundle), so the game's real stylesheet styles them.
// Focus handling, sounds, gamepad hints and transitions are left out.

const maps = classMaps as unknown as Record<string, Record<string, string>>;
const css = (path: string): Record<string, string> => maps[path] ?? {};
const cx = (...names: unknown[]) => names.filter((name) => typeof name === "string" && name.length > 0).join(" ");
const unquote = (value: string | undefined) => (value ?? "").replace(/"/g, "");

/** coui://uil/ is served by the icon library mod in game and from its folder here. */
export const fixUrl = (src: string) => src.replace("coui://uil/", "/uil/");

// Icon

const tintedIcon = css("game-ui/common/image/tinted-icon.module.scss");

export const Icon = ({ tinted, className, src }: { tinted?: boolean | string; className?: string; src: string }) => {
  const url = fixUrl(src);

  if (!tinted) {
    return <img src={url} className={className} />;
  }

  const mask = `url("${url}")`;

  return <div className={cx(tintedIcon.tintedIcon, className)} style={{ maskImage: mask, WebkitMaskImage: mask }} />;
};

// Button

const iconButton = css("game-ui/common/input/button/icon-button.module.scss");

const variants: Record<string, Record<string, string>> = {
  flat: css("game-ui/common/input/button/themes/flat-button.module.scss"),
  primary: css("game-ui/common/input/button/themes/primary-button.module.scss"),
  round: css("game-ui/common/input/button/themes/round-highlight-button.module.scss"),
  default: css("game-ui/common/input/button/themes/default-button.module.scss"),
  icon: iconButton,
  floating: css("game-ui/common/input/button/floating-icon-button.module.scss"),
  text: css("game-ui/common/input/button/themes/text-button.module.scss"),
};

export const Button = forwardRef<HTMLButtonElement, any>(
  ({ variant = "default", theme, src, tinted, selected, disabled, className, onSelect, children, ...rest }, ref) => {
    const chosen = theme ?? (src && variant === "default" ? variants.icon : variants[variant]) ?? {};

    return (
      <button
        {...rest}
        ref={ref}
        className={cx(src && !theme && variant !== "floating" && iconButton.button, chosen.button, selected && "selected", className)}
        disabled={disabled}
        onClick={disabled ? undefined : onSelect}
      >
        {src && <Icon src={src} tinted={tinted} className={chosen.icon ?? iconButton.icon} />}
        {children}
      </button>
    );
  }
);

export const FloatingButton = (props: any) => <Button tinted variant="floating" {...props} />;

// Tooltip

const tooltipCss = css("game-ui/common/tooltip/floating-mouse-tooltip/floating-mouse-tooltip.module.scss");
const tooltipRenderer = css("game-ui/common/tooltip/tooltip-renderer/tooltip-renderer.module.scss");

export const Tooltip = ({ tooltip, children }: { tooltip: ReactNode; children: ReactElement }) => {
  const [rect, setRect] = useState<DOMRect | null>(null);
  const timer = useRef<number>();
  const child = children as ReactElement<any>;

  useEffect(() => () => window.clearTimeout(timer.current), []);

  const element = cloneElement(child, {
    onMouseEnter: (e: React.MouseEvent<HTMLElement>) => {
      const target = e.currentTarget.getBoundingClientRect();

      window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => setRect(target), 250);
      child.props.onMouseEnter?.(e);
    },
    onMouseLeave: (e: React.MouseEvent<HTMLElement>) => {
      window.clearTimeout(timer.current);
      setRect(null);
      child.props.onMouseLeave?.(e);
    },
  });

  return (
    <>
      {element}
      {rect &&
        tooltip &&
        createPortal(
          <div
            className={cx(tooltipCss.tooltipBase, tooltipCss.tooltip, tooltipRenderer.tooltip)}
            style={{
              position: "fixed",
              left: Math.max(4, Math.min(rect.left, window.innerWidth - 340)),
              top: rect.bottom + 6 > window.innerHeight - 120 ? undefined : rect.bottom + 6,
              bottom: rect.bottom + 6 > window.innerHeight - 120 ? window.innerHeight - rect.top + 6 : undefined,
              opacity: 1,
            }}
          >
            {tooltip}
          </div>,
          document.body
        )}
    </>
  );
};

// Scrollable

const scroll = css("game-ui/common/scrolling/scrollable.module.scss");

export const Scrollable = forwardRef<HTMLDivElement, any>(({ vertical, className, style, children }, ref) => {
  const outer = useRef<HTMLDivElement | null>(null);
  const content = useRef<HTMLDivElement>(null);
  const [thumb, setThumb] = useState<{ size: number; offset: number } | null>(null);

  const measure = useCallback(() => {
    const box = content.current;
    const host = outer.current;

    if (!box || !host) {
      return;
    }

    // Chrome does not resolve max-height: 100% against a parent that only has a max-height.
    const limit = getComputedStyle(host).maxHeight;

    box.style.maxHeight = limit !== "none" && !limit.endsWith("%") ? limit : "";

    if (box.scrollHeight > box.clientHeight + 1) {
      const track = box.clientHeight - 12;
      const size = Math.max(16, (box.clientHeight / box.scrollHeight) * track);
      const offset = (box.scrollTop / (box.scrollHeight - box.clientHeight)) * (track - size);

      setThumb((old) => (old && old.size === size && old.offset === offset ? old : { size, offset }));
    } else {
      setThumb(null);
    }
  }, []);

  useLayoutEffect(() => {
    measure();
  });

  useEffect(() => {
    window.addEventListener("resize", measure);

    return () => window.removeEventListener("resize", measure);
  }, [measure]);

  return (
    <div
      ref={(node) => {
        outer.current = node;

        if (typeof ref === "function") {
          ref(node);
        }
      }}
      className={cx(scroll.scrollable, vertical && scroll.y, thumb && scroll.trackVisibleY, className)}
      style={style}
    >
      <div ref={content} className={scroll.content} onScroll={measure}>
        {children}
        <div className={scroll.bottomPadding} />
      </div>
      {vertical && (
        <div className={cx(scroll.track, scroll.y)}>
          <div
            className={cx(scroll.thumb, scroll.y, !thumb && scroll.hidden)}
            style={thumb ? { height: thumb.size, top: thumb.offset } : undefined}
          />
        </div>
      )}
    </div>
  );
});

// Panel

const panelCss = css("game-ui/common/panel/panel.module.scss");
const panelDefault = css("game-ui/common/panel/themes/default.module.scss");
const roundButton = css("game-ui/common/input/button/themes/round-highlight-button.module.scss");

export const panelTheme: Record<string, string> = {
  ...panelCss,
  ...panelDefault,
  closeIcon: unquote(panelCss.closeIcon),
};

export const PanelContext = createContext<{ onClose?: () => void; theme: Record<string, string> }>({ theme: panelTheme });

export const Panel = ({ header, footer, className, contentClassName, children, onClose, draggable, initialPosition }: any) => {
  const ref = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState<{ x: number; y: number }>(initialPosition ?? { x: 0.5, y: 0.5 });
  const [size, setSize] = useState<{ width: number; height: number } | null>(null);

  useLayoutEffect(() => {
    const rect = ref.current?.getBoundingClientRect();

    if (rect && (!size || Math.abs(size.width - rect.width) > 0.5 || Math.abs(size.height - rect.height) > 0.5)) {
      setSize({ width: rect.width, height: rect.height });
    }
  });

  const startDrag = (e: React.MouseEvent) => {
    const rect = ref.current?.getBoundingClientRect();

    if (!draggable || !rect) {
      return;
    }

    const offsetX = e.clientX - rect.left;
    const offsetY = e.clientY - rect.top;

    const move = (event: MouseEvent) =>
      setPosition({
        x: (event.clientX - offsetX) / (window.innerWidth - rect.width),
        y: (event.clientY - offsetY) / (window.innerHeight - rect.height),
      });

    const up = () => {
      window.removeEventListener("mousemove", move);
      window.removeEventListener("mouseup", up);
    };

    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup", up);
  };

  const style = draggable
    ? {
        visibility: size ? ("inherit" as const) : ("hidden" as const),
        left: size ? position.x * (window.innerWidth - size.width) : 0,
        top: size ? position.y * (window.innerHeight - size.height) : 0,
      }
    : undefined;

  // Mirrors game-ui/ui/panel.tsx: the header is wrapped in the title bar, which owns the close button.
  const titleBar = header || draggable ? <PanelTitleBar>{header || " "}</PanelTitleBar> : null;

  return (
    <PanelContext.Provider value={{ onClose, theme: panelTheme }}>
      <div ref={ref} className={cx(panelTheme.panel, draggable && "draggable-panel", className)} style={style}>
        {titleBar && (
          <div className={panelTheme.header} onMouseDown={startDrag}>
            {titleBar}
          </div>
        )}
        <div className={cx(panelTheme.content, contentClassName)}>{children}</div>
        {footer && <div className={panelTheme.footer}>{footer}</div>}
      </div>
    </PanelContext.Provider>
  );
};

/** The game's title bar, as it is found in the module registry. */
export const PanelTitleBar = ({ icon, className, children, ...rest }: any) => {
  const { onClose, theme } = useContext(PanelContext);

  return (
    <div {...rest} className={cx(theme.titleBar, className)}>
      {icon ? <img src={icon} className={theme.icon} /> : onClose && <div className={theme.iconSpace} />}
      <div className={theme.title}>{children}</div>
      {onClose ? (
        <button
          className={cx(iconButton.button, roundButton.button, theme.closeButton)}
          onClick={onClose}
          onMouseDown={(e) => {
            e.preventDefault();
            e.stopPropagation();
          }}
        >
          <Icon tinted src={theme.closeIcon} className={iconButton.icon} />
        </button>
      ) : (
        icon && <div className={theme.iconSpace} />
      )}
    </div>
  );
};

// Dropdown

const dropdownBase = css("game-ui/common/input/dropdown/dropdown.module.scss");
const dropdownDefault = css("game-ui/common/input/dropdown/themes/default.module.scss");

interface DropdownState {
  visible: boolean;
  theme: Record<string, string>;
  toggle: () => void;
  hide: () => void;
  anchor: React.MutableRefObject<HTMLElement | null>;
}

const DropdownContext = createContext<DropdownState | null>(null);

export const Dropdown = ({ theme: partial, content, children }: any) => {
  const [visible, setVisible] = useState(false);
  const anchor = useRef<HTMLElement | null>(null);
  const popup = useRef<HTMLDivElement>(null);
  const theme = { ...dropdownBase, ...dropdownDefault, ...partial };

  useEffect(() => {
    if (!visible) {
      return;
    }

    const down = (e: MouseEvent) => {
      const target = e.target as Node;

      if (!popup.current?.contains(target) && !anchor.current?.contains(target)) {
        setVisible(false);
      }
    };

    const key = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        setVisible(false);
      }
    };

    window.addEventListener("mousedown", down, true);
    window.addEventListener("keydown", key, true);

    return () => {
      window.removeEventListener("mousedown", down, true);
      window.removeEventListener("keydown", key, true);
    };
  }, [visible]);

  const state: DropdownState = {
    visible,
    theme,
    anchor,
    toggle: () => setVisible((value) => !value),
    hide: () => setVisible(false),
  };

  const rect = visible ? anchor.current?.getBoundingClientRect() : undefined;
  const below = rect ? window.innerHeight - rect.bottom : 0;
  const up = rect ? below < 240 && rect.top > below : false;

  return (
    <DropdownContext.Provider value={state}>
      {children}
      {rect &&
        createPortal(
          <div
            ref={popup}
            className={theme.dropdownPopup}
            style={{
              position: "absolute",
              left: rect.left,
              top: up ? undefined : rect.bottom,
              bottom: up ? window.innerHeight - rect.top : undefined,
              minWidth: rect.width,
              maxHeight: Math.max(120, (up ? rect.top : below) - 10),
              display: "flex",
              zIndex: 100,
            }}
          >
            <div className={theme.dropdownMenu} style={{ display: "flex", flex: 1, minHeight: 0 }}>
              <Scrollable vertical className={theme.scrollable} style={{ flex: 1 }}>
                <DropdownContext.Provider value={state}>{content}</DropdownContext.Provider>
              </Scrollable>
            </div>
          </div>,
          document.body
        )}
    </DropdownContext.Provider>
  );
};

export const DropdownToggle = ({ className, disabled, children, ...rest }: any) => {
  const state = useContext(DropdownContext)!;

  return (
    <button
      {...rest}
      ref={(node) => {
        state.anchor.current = node;
      }}
      className={cx(state.theme.dropdownToggle, state.visible && "active", className)}
      disabled={disabled}
      onClick={disabled ? undefined : state.toggle}
    >
      <div className={state.theme.label}>{children}</div>
      <Icon
        tinted
        src={unquote(state.visible ? dropdownBase.visibleIcon : dropdownBase.hiddenIcon)}
        className={state.theme.indicator}
      />
    </button>
  );
};

export const DropdownItem = ({ value, selected, className, onChange, onToggleSelected, children }: any) => {
  const state = useContext(DropdownContext)!;

  return (
    <button
      className={cx(state.theme.dropdownItem, selected && "selected", className)}
      style={{ width: "100%" }}
      onClick={() => {
        if (selected) {
          onToggleSelected?.(value);
        } else {
          onChange?.(value);
        }

        state.hide();
      }}
    >
      {children}
    </button>
  );
};
