import { css } from "@emotion/react";
import * as React from "react";
import { Divider } from "@mui/material";
import {
    divider,
    ILocalizableMenuItemProps,
    LocalizableMenuItem,
    LocalizableNestedMenuItem,
} from "../../../react_components/localizableMenuItem";
import {
    IControlContext,
    IControlMenuRow,
    IControlRuntime,
} from "../../toolbox/canvas/canvasControlTypes";

// This file renders the menu rows that controlRegistry provides. Two menus use it:
// CanvasElementContextControls (a canvas element's "..." menu and its right-click menu) and
// TextContextMenu, whose inline-image commands come from the same registry (BL-16649).
// The registry describes each row as an IControlMenuRow, and the LocalizableMenuItem
// components take ILocalizableMenuItemProps, so most of this code converts one to the other.
// The rest draws the dividers, nested submenus, shortcut column and icon sizes, so that the
// rows look the same in both menus.

export interface IMenuItemWithSubmenu extends ILocalizableMenuItemProps {
    subMenu?: ILocalizableMenuItemProps[];
}

// Both menus use this maximum width, so a command's label wraps at the same place in each.
export const kContextMenuMaxWidth = 338;

// Styles for the ul of any menu that shows these rows. Among other things, the filter here
// makes the raster icons (reset image, fill space, AI edit) the same grey as the MUI svg
// icons.
export const contextMenuCss = css`
    ul {
        max-width: ${kContextMenuMaxWidth}px;
        color: #4d4d4d;
        li {
            display: flex;
            align-items: flex-start;
            color: #4d4d4d;
            .MuiListItemIcon-root {
                color: inherit !important;
            }
            svg {
                color: inherit !important;
            }
            p,
            span {
                color: #4d4d4d;
            }
            img.canvas-context-menu-monochrome-icon {
                display: block;
                width: 24px;
                height: 24px;
                object-fit: contain;
                filter: brightness(0) saturate(100%) invert(31%) sepia(0%)
                    saturate(0%) hue-rotate(180deg) brightness(95%)
                    contrast(94%);
            }
            p {
                white-space: initial;
            }
            &.MuiDivider-root {
                margin-bottom: 12px;
            }
        }
    }
`;

// Control callbacks can be either sync or async by contract.
// We always call through this helper so sync exceptions and async
// rejections are handled consistently from UI event handlers.
export const runControlCallback = (
    callbackLabel: string,
    callback: () => void | Promise<void>,
): void => {
    try {
        const result = callback();
        if (result) {
            void result.catch((error) => {
                console.error(
                    `Canvas control callback failed (${callbackLabel})`,
                    error,
                );
            });
        }
    } catch (error) {
        console.error(
            `Canvas control callback failed (${callbackLabel})`,
            error,
        );
    }
};

export const scaleIconNode = (
    iconNode: React.ReactNode,
    iconScale?: number,
): React.ReactNode => {
    if (!iconNode || iconScale === undefined || iconScale === 1) {
        return iconNode;
    }

    return (
        <span
            css={css`
                display: inline-flex;
                align-items: center;
                justify-content: center;
                transform: scale(${iconScale});
                transform-origin: center;
            `}
        >
            {iconNode}
        </span>
    );
};

export const convertControlMenuRows = (
    rows: IControlMenuRow[],
    controlContext: IControlContext,
    controlRuntime: IControlRuntime,
): IMenuItemWithSubmenu[] => {
    const convertedRows: IMenuItemWithSubmenu[] = [];

    rows.forEach((row) => {
        if (row.separatorAbove && convertedRows.length > 0) {
            convertedRows.push(divider as IMenuItemWithSubmenu);
        }

        const convertedSubMenu = row.subMenuItems
            ? convertControlMenuRows(
                  row.subMenuItems,
                  controlContext,
                  controlRuntime,
              )
            : undefined;

        const convertedRow: IMenuItemWithSubmenu = {
            l10nId: row.l10nId ?? null,
            english: row.englishLabel ?? "",
            subLabelL10nId: row.subLabelL10nId,
            generatedSubLabel: row.subLabel,
            shortcutDisplay: row.shortcut?.display,
            icon: scaleIconNode(row.icon, row.iconScale),
            disabled: row.disabled,
            featureName: row.featureName,
            subscriptionTooltipOverride: row.subscriptionTooltipOverride,
            onClick: () => {
                // Ordinary leaf commands close centrally here. Registry
                // handlers only call runtime.closeMenu(...) for special
                // cases such as dialog launches or submenu-specific focus
                // behavior.
                if (!convertedSubMenu) {
                    controlRuntime.closeMenu();
                }
                runControlCallback(
                    `menu:${row.id ?? row.englishLabel ?? "unknown"}`,
                    () => row.onSelect(controlContext, controlRuntime),
                );
            },
        };

        if (convertedSubMenu) {
            convertedRow.subMenu = convertedSubMenu;
        }

        convertedRows.push(convertedRow);

        if (row.helpRowL10nId || row.helpRowEnglish) {
            if (row.helpRowSeparatorAbove && convertedRows.length > 0) {
                convertedRows.push(divider as IMenuItemWithSubmenu);
            }

            convertedRows.push({
                l10nId: null,
                english: "",
                subLabelL10nId: row.helpRowL10nId,
                generatedSubLabel: row.helpRowEnglish,
                onClick: () => {},
                disabled: true,
            });
        }
    });

    return convertedRows;
};

export function joinMenuSectionsWithSingleDividers(
    menuSections: IMenuItemWithSubmenu[][],
): IMenuItemWithSubmenu[] {
    const nonEmptySections = menuSections.filter(
        (section) => section.length > 0,
    );
    const menuItems: IMenuItemWithSubmenu[] = [];
    nonEmptySections.forEach((section, index) => {
        if (index > 0) {
            menuItems.push(divider as IMenuItemWithSubmenu);
        }
        menuItems.push(...section);
    });
    return menuItems;
}

// Renders the converted rows, including dividers and nested submenus, as the children of an
// MUI Menu, and lines up the keyboard shortcuts in one column. menuOpen tells whether the
// Menu that holds these rows is open.
export function renderContextMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    menuOpen: boolean,
): React.ReactNode[] {
    const menuHasShortcuts = menuOptions.some((o) => !!o.shortcutDisplay);
    return menuOptions.map((option, index) => {
        if (option.l10nId === "-") {
            return <Divider key={index} variant="middle" component="li" />;
        }
        if (option.subMenu) {
            const subMenuHasShortcuts = option.subMenu.some(
                (o) => !!o.shortcutDisplay,
            );
            return (
                <LocalizableNestedMenuItem
                    {...option}
                    key={option.l10nId}
                    truncateMainLabel={true}
                    // The menu is keepMounted, so the row must hear
                    // that the menu shut, or its submenu stays drawn.
                    parentMenuOpen={menuOpen}
                >
                    {option.subMenu.map((subOption, subIndex) => {
                        if (subOption.l10nId === "-") {
                            return (
                                <Divider
                                    key={subIndex}
                                    variant="middle"
                                    component="li"
                                />
                            );
                        }
                        return (
                            <LocalizableMenuItem
                                key={subOption.l10nId}
                                {...subOption}
                                onClick={subOption.onClick}
                                leaveSpaceForShortcut={subMenuHasShortcuts}
                                css={css`
                                    max-width: ${kContextMenuMaxWidth}px;
                                    white-space: wrap;
                                    // Styles for subLabels
                                    p {
                                        // Determined empirically...
                                        // Styling in NestedMenuItem is impossibly difficult.
                                        left: -8px;
                                    }
                                `}
                            />
                        );
                    })}
                </LocalizableNestedMenuItem>
            );
        }
        return (
            <LocalizableMenuItem
                key={option.l10nId}
                {...option}
                onClick={option.onClick}
                variant="body1"
                leaveSpaceForShortcut={menuHasShortcuts}
            />
        );
    });
}
