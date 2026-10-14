import React, { forwardRef, useState } from "react";
import classMaps from "../data/classes.json";
import { PanelTitleBar } from "./ui";

// Stand-in for the game's module registry. Style modules carry the real class names of the
// installed game; the two components the mod looks up are simplified copies.

const cx = (...names: unknown[]) => names.filter((name) => typeof name === "string" && name.length > 0).join(" ");

// The game's text input shows the placeholder as its value, with a "placeholder" class, and
// leaves the field on Enter and Escape.
const TextInput = forwardRef<HTMLInputElement, any>(
  ({ value, placeholder, className, onChange, onKeyDown, onFocus, onBlur, disabled }, ref) => {
    const [focused, setFocused] = useState(false);
    const showPlaceholder = !focused && !value && !!placeholder;

    return (
      <input
        ref={ref}
        type="text"
        disabled={disabled}
        className={cx(className, showPlaceholder && "placeholder")}
        value={showPlaceholder ? placeholder : value ?? ""}
        onChange={onChange}
        onFocus={(e) => {
          setFocused(true);
          onFocus?.(e);
        }}
        onBlur={(e) => {
          setFocused(false);
          onBlur?.(e);
        }}
        onKeyDown={(e) => {
          if (e.keyCode === 13 || e.keyCode === 27) {
            e.currentTarget.blur();
          }

          e.stopPropagation();
          onKeyDown?.(e);
        }}
      />
    );
  }
);

const modules = new Map<string, Record<string, any>>();

for (const [path, classes] of Object.entries(classMaps as unknown as Record<string, Record<string, string>>)) {
  modules.set(path, { classes });
}

modules.set("game-ui/common/input/text/text-input.tsx", { TextInput });
modules.set("game-ui/common/panel/panel-title-bar.tsx", { PanelTitleBar });

// ?registry=empty shows the panel as it looks when a game update moved every module.
if (new URLSearchParams(location.search).get("registry") === "empty") {
  modules.clear();
}

export const registry = {
  registry: modules,
  append: () => undefined,
};
