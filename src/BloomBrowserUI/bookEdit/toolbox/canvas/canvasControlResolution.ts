import * as React from "react";
import {
    ICanvasElementControlConfiguration,
    ICanvasToolsPanelState,
    IControlAvailability,
    IControlContext,
    IControlDefinition,
    ICommandControlDefinition,
    IControlMenuCommandRow,
    IControlMenuRow,
    IControlRule,
    IControlSurfaceRule,
    IControlRuntime,
    IResolvedControl,
    TopLevelControlId,
} from "./canvasControlTypes";
import { controlRegistry, controlSections } from "./canvasControlRegistry";

type ResolvedToolbarItem = {
    control: ICommandControlDefinition;
    enabled: boolean;
};

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
    configuration: ICanvasElementControlConfiguration,
    controlId: TopLevelControlId,
): IControlRule | "exclude" | undefined => {
    return configuration.availabilityRules[controlId];
};

const getEffectiveRule = (
    configuration: ICanvasElementControlConfiguration,
    controlId: TopLevelControlId,
    surface: "toolbar" | "menu" | "toolPanel",
): IControlSurfaceRule => {
    const rule = getRuleForControl(configuration, controlId);
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
    items: Array<ResolvedToolbarItem | { id: "spacer" }>,
): Array<ResolvedToolbarItem | { id: "spacer" }> => {
    // Normalize means removing leading spacers, trailing spacers, and any run
    // of adjacent spacers so filtered toolbars never render empty gaps.
    const normalized: Array<ResolvedToolbarItem | { id: "spacer" }> = [];

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

const buildDefaultMenuRow = (
    control: ICommandControlDefinition,
): IControlMenuCommandRow => ({
    id: control.id,
    l10nId: control.l10nId,
    englishLabel: control.englishLabel,
    helpRowL10nId: control.helpRowL10nId,
    helpRowEnglish: control.helpRowEnglish,
    helpRowSeparatorAbove: control.helpRowSeparatorAbove,
    subLabelL10nId: control.menu?.subLabelL10nId,
    icon: iconToNode(control, "menu"),
    iconScale: control.menu?.iconScale ?? control.iconScale,
    featureName: control.featureName,
    shortcut: control.menu?.shortcutDisplay
        ? {
              id: `${control.id}.defaultShortcut`,
              display: control.menu.shortcutDisplay,
          }
        : undefined,
    onSelect: async (rowCtx: IControlContext, rowRuntime: IControlRuntime) => {
        await control.action(rowCtx, rowRuntime);
    },
});

const applyDefaultMenuRowFields = (
    defaultRow: IControlMenuCommandRow,
    row: IControlMenuCommandRow,
): IControlMenuCommandRow => ({
    ...defaultRow,
    ...row,
    icon: row.icon ?? defaultRow.icon,
    iconScale: row.iconScale ?? defaultRow.iconScale,
    featureName: row.featureName ?? defaultRow.featureName,
    helpRowL10nId: row.helpRowL10nId ?? defaultRow.helpRowL10nId,
    helpRowEnglish: row.helpRowEnglish ?? defaultRow.helpRowEnglish,
    helpRowSeparatorAbove:
        row.helpRowSeparatorAbove ?? defaultRow.helpRowSeparatorAbove,
});

// Resolves a canvas-element control configuration into toolbar controls, applying
// visibility/enabled rules and normalizing spacer placement.
export const getToolbarItems = (
    configuration: ICanvasElementControlConfiguration,
    ctx: IControlContext,
    _runtime: IControlRuntime = defaultRuntime,
): Array<ResolvedToolbarItem | { id: "spacer" }> => {
    const items: Array<ResolvedToolbarItem | { id: "spacer" }> = [];

    configuration.toolbar.forEach((toolbarItem) => {
        if (toolbarItem === "spacer") {
            items.push({ id: "spacer" });
            return;
        }

        const control = controlRegistry[toolbarItem];
        if (control.kind !== "command") {
            throw new Error(
                `Toolbar control ${toolbarItem} must resolve to a command control.`,
            );
        }

        const effectiveRule = getEffectiveRule(
            configuration,
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
        });
    });

    return normalizeToolbarItems(items);
};

// Resolves section-based menu controls into concrete menu rows for the current
// context, including nested availability and disabled-state propagation.
export const getMenuSections = (
    configuration: ICanvasElementControlConfiguration,
    ctx: IControlContext,
    runtime: IControlRuntime = defaultRuntime,
): IResolvedControl[][] => {
    const sections: IResolvedControl[][] = [];

    configuration.menuSections.forEach((sectionId) => {
        const section = controlSections[sectionId];
        const sectionControls = section.controlsBySurface.menu ?? [];
        const resolvedControls: IResolvedControl[] = [];

        sectionControls.forEach((controlId) => {
            const control = controlRegistry[controlId];
            if (control.kind !== "command") {
                return;
            }

            const effectiveRule = getEffectiveRule(
                configuration,
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
            const defaultMenuRow = buildDefaultMenuRow(control);
            const builtRow =
                control.menu?.buildMenuItem?.(ctx, runtime) ?? defaultMenuRow;

            const rowWithAvailability = applyRowAvailability(
                builtRow,
                ctx,
                enabled,
            );
            if (!rowWithAvailability) {
                return;
            }

            const menuRow = applyDefaultMenuRowFields(
                defaultMenuRow,
                rowWithAvailability,
            );

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
    configuration: ICanvasElementControlConfiguration,
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

    configuration.toolPanel.forEach((sectionId) => {
        const section = controlSections[sectionId];
        const sectionControls = section.controlsBySurface.toolPanel ?? [];
        sectionControls.forEach((controlId) => {
            const control = controlRegistry[controlId];
            if (control.kind !== "panel") {
                return;
            }

            const effectiveRule = getEffectiveRule(
                configuration,
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
