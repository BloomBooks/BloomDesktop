import * as React from "react";
import {
    ICanvasElementDefinition,
    ICanvasToolsPanelState,
    IControlAvailability,
    IControlContext,
    IControlDefinition,
    IControlMenuCommandRow,
    IControlMenuRow,
    IControlRule,
    IControlSurfaceRule,
    IControlRuntime,
    IResolvedControl,
    TopLevelControlId,
} from "./canvasControlTypes";
import { controlRegistry, controlSections } from "./canvasControlRegistry";

const defaultRuntime: IControlRuntime = {
    closeMenu: () => {},
};

const evaluateAvailability = (
    availability: IControlAvailability | undefined,
    ctx: IControlContext,
    fallback: boolean,
): boolean => {
    if (availability === undefined) {
        return fallback;
    }

    if (typeof availability === "boolean") {
        return availability;
    }

    return availability(ctx);
};

// Registry icons may be declared as component types, already-created elements,
// or legacy icon objects; normalize all of them to renderable nodes.
const toRenderedIcon = (icon: React.ReactNode | undefined): React.ReactNode => {
    if (!icon) {
        return undefined;
    }

    if (React.isValidElement(icon)) {
        return icon;
    }

    if (typeof icon === "function") {
        return React.createElement(icon as React.ElementType, null);
    }

    if (typeof icon === "object" && "$$typeof" in (icon as object)) {
        return React.createElement(icon as React.ElementType, null);
    }

    return icon;
};

const getRuleForControl = (
    definition: ICanvasElementDefinition,
    controlId: TopLevelControlId,
): IControlRule | "exclude" | undefined => {
    return definition.availabilityRules[controlId];
};

const getEffectiveRule = (
    definition: ICanvasElementDefinition,
    controlId: TopLevelControlId,
    surface: "toolbar" | "menu" | "toolPanel",
): IControlSurfaceRule => {
    const rule = getRuleForControl(definition, controlId);
    if (rule === "exclude") {
        return {
            visible: false,
            enabled: false,
        };
    }

    // Precedence: per-surface rule > general rule > always visible/enabled.
    const surfaceRule = rule?.surfacePolicy?.[surface];
    return {
        visible: surfaceRule?.visible ?? rule?.visible,
        enabled: surfaceRule?.enabled ?? rule?.enabled,
    };
};

const iconToNode = (
    control: IControlDefinition,
    surface: "toolbar" | "menu",
) => {
    if (
        surface === "menu" &&
        control.kind === "command" &&
        control.menu?.icon
    ) {
        return toRenderedIcon(control.menu.icon);
    }

    if (
        surface === "toolbar" &&
        control.kind === "command" &&
        control.toolbar?.icon
    ) {
        return toRenderedIcon(control.toolbar.icon);
    }

    return toRenderedIcon(control.icon);
};

const normalizeToolbarItems = (
    items: Array<IResolvedControl | { id: "spacer" }>,
): Array<IResolvedControl | { id: "spacer" }> => {
    const normalized: Array<IResolvedControl | { id: "spacer" }> = [];

    items.forEach((item) => {
        if ("id" in item && item.id === "spacer") {
            if (normalized.length === 0) {
                return;
            }

            const previousItem = normalized[normalized.length - 1];
            if ("id" in previousItem && previousItem.id === "spacer") {
                return;
            }
        }

        normalized.push(item);
    });

    while (normalized.length > 0) {
        const lastItem = normalized[normalized.length - 1];
        if (!("id" in lastItem && lastItem.id === "spacer")) {
            break;
        }

        normalized.pop();
    }

    return normalized;
};

const applyRowAvailability = (
    row: IControlMenuRow,
    ctx: IControlContext,
    parentEnabled: boolean,
): IControlMenuRow | undefined => {
    if (!evaluateAvailability(row.availability?.visible, ctx, true)) {
        return undefined;
    }

    const rowEnabled = evaluateAvailability(
        row.availability?.enabled,
        ctx,
        true,
    );

    // Child rows inherit disabled state from all ancestors so submenu items
    // don't become clickable when the parent row is unavailable.
    const subMenuItems = row.subMenuItems
        ?.map((subItem) =>
            applyRowAvailability(subItem, ctx, parentEnabled && rowEnabled),
        )
        .filter((subItem): subItem is IControlMenuRow => !!subItem);

    return {
        ...row,
        disabled: row.disabled || !parentEnabled || !rowEnabled,
        subMenuItems,
    };
};

// Resolves a canvas element definition into toolbar controls, applying
// visibility/enabled rules and normalizing spacer placement.
export const getToolbarItems = (
    definition: ICanvasElementDefinition,
    ctx: IControlContext,
    runtime: IControlRuntime = defaultRuntime,
): Array<IResolvedControl | { id: "spacer" }> => {
    const items: Array<IResolvedControl | { id: "spacer" }> = [];

    definition.toolbar.forEach((toolbarItem) => {
        if (toolbarItem === "spacer") {
            items.push({ id: "spacer" });
            return;
        }

        const control = controlRegistry[toolbarItem];
        const effectiveRule = getEffectiveRule(
            definition,
            toolbarItem,
            "toolbar",
        );
        if (!evaluateAvailability(effectiveRule.visible, ctx, true)) {
            return;
        }

        const enabled = evaluateAvailability(effectiveRule.enabled, ctx, true);
        items.push({
            control,
            enabled,
            menuRow:
                control.kind === "command"
                    ? {
                          id: control.id,
                          l10nId: control.l10nId,
                          englishLabel: control.englishLabel,
                          icon: iconToNode(control, "toolbar"),
                          disabled: !enabled,
                          featureName: control.featureName,
                          onSelect: async (rowCtx, rowRuntime) => {
                              await control.action(
                                  rowCtx,
                                  rowRuntime ?? runtime,
                              );
                          },
                      }
                    : undefined,
        });
    });

    return normalizeToolbarItems(items);
};

// Resolves section-based menu controls into concrete menu rows for the current
// context, including nested availability and disabled-state propagation.
export const getMenuSections = (
    definition: ICanvasElementDefinition,
    ctx: IControlContext,
    runtime: IControlRuntime = defaultRuntime,
): IResolvedControl[][] => {
    const sections: IResolvedControl[][] = [];

    definition.menuSections.forEach((sectionId) => {
        const section = controlSections[sectionId];
        const sectionControls = section.controlsBySurface.menu ?? [];
        const resolvedControls: IResolvedControl[] = [];

        sectionControls.forEach((controlId) => {
            const control = controlRegistry[controlId];
            if (control.kind !== "command") {
                return;
            }

            const effectiveRule = getEffectiveRule(
                definition,
                controlId,
                "menu",
            );
            if (!evaluateAvailability(effectiveRule.visible, ctx, true)) {
                return;
            }

            const enabled = evaluateAvailability(
                effectiveRule.enabled,
                ctx,
                true,
            );
            const builtRow = control.menu?.buildMenuItem
                ? control.menu.buildMenuItem(ctx, runtime)
                : {
                      // This is the default mapping from a command control
                      // definition to one menu row. Optional help-row metadata
                      // rides along and is rendered by the menu layer.
                      id: control.id,
                      l10nId: control.l10nId,
                      englishLabel: control.englishLabel,
                      helpRowL10nId: control.helpRowL10nId,
                      helpRowEnglish: control.helpRowEnglish,
                      helpRowSeparatorAbove: control.helpRowSeparatorAbove,
                      subLabelL10nId: control.menu?.subLabelL10nId,
                      icon: iconToNode(control, "menu"),
                      featureName: control.featureName,
                      shortcut: control.menu?.shortcutDisplay
                          ? {
                                id: `${control.id}.defaultShortcut`,
                                display: control.menu.shortcutDisplay,
                            }
                          : undefined,
                      onSelect: async (
                          rowCtx: IControlContext,
                          rowRuntime: IControlRuntime,
                      ) => {
                          await control.action(rowCtx, rowRuntime);
                      },
                  };

            const rowWithAvailability = applyRowAvailability(
                builtRow,
                ctx,
                enabled,
            );
            if (!rowWithAvailability) {
                return;
            }

            const menuRow: IControlMenuCommandRow = {
                ...rowWithAvailability,
                icon: rowWithAvailability.icon ?? iconToNode(control, "menu"),
                featureName:
                    rowWithAvailability.featureName ?? control.featureName,
                helpRowL10nId:
                    rowWithAvailability.helpRowL10nId ?? control.helpRowL10nId,
                helpRowEnglish:
                    rowWithAvailability.helpRowEnglish ??
                    control.helpRowEnglish,
                helpRowSeparatorAbove:
                    rowWithAvailability.helpRowSeparatorAbove ??
                    control.helpRowSeparatorAbove,
            };

            resolvedControls.push({
                control,
                enabled: !(menuRow.disabled ?? false),
                menuRow,
            });
        });

        if (resolvedControls.length > 0) {
            sections.push(resolvedControls);
        }
    });

    return sections;
};

// Resolves the tool-panel controls that should render for the current canvas
// element context.
export const getToolPanelControls = (
    definition: ICanvasElementDefinition,
    ctx: IControlContext,
): Array<{
    controlId: TopLevelControlId;
    Component: React.FunctionComponent<{
        ctx: IControlContext;
        panelState: ICanvasToolsPanelState;
    }>;
    ctx: IControlContext;
}> => {
    const controls: Array<{
        controlId: TopLevelControlId;
        Component: React.FunctionComponent<{
            ctx: IControlContext;
            panelState: ICanvasToolsPanelState;
        }>;
        ctx: IControlContext;
    }> = [];

    definition.toolPanel.forEach((sectionId) => {
        const section = controlSections[sectionId];
        const sectionControls = section.controlsBySurface.toolPanel ?? [];
        sectionControls.forEach((controlId) => {
            const control = controlRegistry[controlId];
            if (control.kind !== "panel") {
                return;
            }

            const effectiveRule = getEffectiveRule(
                definition,
                controlId,
                "toolPanel",
            );
            if (!evaluateAvailability(effectiveRule.visible, ctx, true)) {
                return;
            }

            controls.push({
                controlId,
                Component: control.canvasToolsControl,
                ctx,
            });
        });
    });

    return controls;
};
