import * as React from "react";
import {
    ICanvasElementDefinition,
    ICanvasToolsPanelState,
    IControlContext,
    IControlDefinition,
    IControlMenuCommandRow,
    IControlMenuRow,
    IControlRule,
    IControlRuntime,
    IResolvedControl,
    TopLevelControlId,
} from "./canvasControlTypes";
import { controlRegistry, controlSections } from "./canvasControlRegistry";

const defaultRuntime: IControlRuntime = {
    closeMenu: () => {},
};

const alwaysVisible = (): boolean => true;
const alwaysEnabled = (): boolean => true;

const toRenderedIcon = (icon: React.ReactNode | undefined): React.ReactNode => {
    if (!icon) {
        return undefined;
    }

    if (React.isValidElement(icon)) {
        return icon;
    }

    if (typeof icon === "function") {
        return React.createElement(icon, null);
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
): {
    visible: (ctx: IControlContext) => boolean;
    enabled: (ctx: IControlContext) => boolean;
} => {
    const rule = getRuleForControl(definition, controlId);
    if (rule === "exclude") {
        return {
            visible: () => false,
            enabled: () => false,
        };
    }

    const surfaceRule = rule?.surfacePolicy?.[surface];
    return {
        visible: surfaceRule?.visible ?? rule?.visible ?? alwaysVisible,
        enabled: surfaceRule?.enabled ?? rule?.enabled ?? alwaysEnabled,
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
    if (row.kind === "help") {
        if (row.availability?.visible && !row.availability.visible(ctx)) {
            return undefined;
        }

        return row;
    }

    if (row.availability?.visible && !row.availability.visible(ctx)) {
        return undefined;
    }

    const rowEnabled = row.availability?.enabled
        ? row.availability.enabled(ctx)
        : true;

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
        if (!effectiveRule.visible(ctx)) {
            return;
        }

        const enabled = effectiveRule.enabled(ctx);
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
            if (!effectiveRule.visible(ctx)) {
                return;
            }

            const enabled = effectiveRule.enabled(ctx);
            const builtRow = control.menu?.buildMenuItem
                ? control.menu.buildMenuItem(ctx, runtime)
                : {
                      id: control.id,
                      l10nId: control.l10nId,
                      englishLabel: control.englishLabel,
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
            if (!rowWithAvailability || rowWithAvailability.kind === "help") {
                return;
            }

            const menuRow: IControlMenuCommandRow = {
                ...rowWithAvailability,
                icon: rowWithAvailability.icon ?? iconToNode(control, "menu"),
                featureName:
                    rowWithAvailability.featureName ?? control.featureName,
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
            if (!effectiveRule.visible(ctx)) {
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
