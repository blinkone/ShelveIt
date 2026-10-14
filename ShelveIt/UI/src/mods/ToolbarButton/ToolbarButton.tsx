import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import icon from "images/shelveit.svg";
import { panelVisible$, togglePanel } from "bindings";
import { useT } from "l10n";

/** The only part of the mod that stays mounted while the panel is closed. */
export const ToolbarButton = () => {
  const visible = useValue(panelVisible$);
  const t = useT();

  return (
    <Tooltip tooltip={t("Title")}>
      <Button variant="floating" src={icon} tinted selected={visible} onSelect={togglePanel} />
    </Tooltip>
  );
};
