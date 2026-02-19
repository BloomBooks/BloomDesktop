import { css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect, useRef } from "react";
import * as ReactDOM from "react-dom";
import { kBloomBlue, lightTheme } from "../../../bloomMaterialUITheme";
import { SvgIconProps } from "@mui/material";
import { default as MenuIcon } from "@mui/icons-material/MoreHorizSharp";
import { kImageContainerClass } from "../bloomImages";
import { ThemeProvider } from "@mui/material/styles";
import {
    divider,
    ILocalizableMenuItemProps,
    LocalizableMenuItem,
    LocalizableNestedMenuItem,
} from "../../../react_components/localizableMenuItem";
import Menu from "@mui/material/Menu";
import { Divider } from "@mui/material";
import { getCanvasElementManager } from "../../toolbox/canvas/canvasElementUtils";
import { kBackgroundImageClass } from "../../toolbox/canvas/canvasElementConstants";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { useL10n } from "../../../react_components/l10nHooks";
import { kBloomDisabledOpacity } from "../../../utils/colorUtils";
import AudioRecording from "../../toolbox/talkingBook/audioRecording";
import { getAudioSentencesOfVisibleEditables } from "bloom-player";
import { canvasElementDefinitions as controlCanvasElementDefinitions } from "../../toolbox/canvas/canvasElementDefinitions";
import { buildControlContext } from "../../toolbox/canvas/buildControlContext";
import {
    IControlContext,
    IControlMenuRow,
    IControlRuntime,
} from "../../toolbox/canvas/canvasControlTypes";
import {
    getMenuSections,
    getToolbarItems,
} from "../../toolbox/canvas/canvasControlHelpers";

interface IMenuItemWithSubmenu extends ILocalizableMenuItemProps {
    subMenu?: ILocalizableMenuItemProps[];
}

// This is the controls bar that appears beneath a canvas element when it is selected. It contains buttons
// for the most common operations that apply to the canvas element in its current state, and a menu for less common
// operations.

const CanvasElementContextControls: React.FunctionComponent<{
    canvasElement: HTMLElement;
    // These props support reusing the context controls menu for a right-click on the canvas element.
    // The first two make the open state of the menu a controlled property. Basically the
    // parent stores the state and passes it in, but to get the normal behavior of
    // clicking on the "..." menu and closing the menu, this component can request that
    // it be changed. The third is the position of the menu, which is used when the menu
    // is opened by a right-click, to place it near the click.
    menuOpen: boolean;
    setMenuOpen: (open: boolean) => void;
    menuAnchorPosition?: { left: number; top: number };
}> = (props) => {
    const canvasElementManager = getCanvasElementManager();

    const hasText =
        props.canvasElement.getElementsByClassName("bloom-editable").length > 0;
    const editable = props.canvasElement.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on",
    )[0] as HTMLElement | undefined;
    const langName = editable?.getAttribute("data-languagetipcontent");
    const setMenuOpen = (open: boolean, launchingDialog?: boolean) => {
        // Even though we've done our best to tell the MUI menu NOT to steal focus, it seems it still does...
        // or some other code somewhere is doing it when we choose a menu item. So we tell the CanvasElementManager
        // to ignore focus changes while the menu is open.
        if (open) {
            canvasElementManager?.setIgnoreFocusChanges?.(true);
        }
        props.setMenuOpen(open);
        // Setting ignoreFocusChanges to false immediately after closing the menu doesn't work,
        // because the the focus change is still happening after the menu closes.  This timeout
        // ensures that the focus change is ignored immediately after the menu closes.
        // The skipNextFocusChange flag is used to prevent the focus change that happens when
        // a dialog opened by the menu command closes.  See BL-14123.
        if (!open) {
            setTimeout(() => {
                canvasElementManager?.setIgnoreFocusChanges?.(
                    false,
                    launchingDialog,
                );
            }, 0);
        }
    };

    const menuEl = useRef<HTMLElement | null>(null);

    // After deleting a draggable, we may get rendered again, and page will be null.
    const page = props.canvasElement.closest(
        ".bloom-page",
    ) as HTMLElement | null;

    const isBackgroundImage = props.canvasElement.classList.contains(
        kBackgroundImageClass,
    );

    const children = props.canvasElement.parentElement?.querySelectorAll(
        ".bloom-canvas-element",
    );
    const canvasHasMultipleElements = (children?.length ?? 0) > 1; // kBackgroundImageClass is also a canvas element
    const backgroundImageText = useL10n(
        "Background Image",
        "EditTab.Image.BackgroundImage",
    );

    interface IToolbarItem {
        key: string;
        node: React.ReactNode;
        isSpacer?: boolean;
    }

    const normalizeToolbarItems = (items: IToolbarItem[]): IToolbarItem[] => {
        const normalized: IToolbarItem[] = [];
        items.forEach((item) => {
            if (item.isSpacer) {
                if (normalized.length === 0) {
                    return;
                }
                if (normalized[normalized.length - 1].isSpacer) {
                    return;
                }
            }
            normalized.push(item);
        });
        while (
            normalized.length > 0 &&
            normalized[normalized.length - 1].isSpacer
        ) {
            normalized.pop();
        }
        return normalized;
    };

    const [textHasAudio, setTextHasAudio] = useState(true);
    useEffect(() => {
        if (!props.menuOpen || !props.canvasElement || !hasText) return;

        const audioSentences = getAudioSentencesOfVisibleEditables(
            props.canvasElement,
        );
        const ids = audioSentences.map((sentence) => sentence.id);
        AudioRecording.audioExistsForIdsAsync(ids)
            .then((audioExists) => {
                setTextHasAudio(audioExists);
            })
            .catch((err) => {
                console.error(
                    "Error checking for existing of audio for IDs: ",
                    err,
                );
            });
        // Need to include menuOpen so we can re-evaluate if the user has added or removed audio.
    }, [props.canvasElement, props.menuOpen, hasText]);

    if (!page) {
        // Probably right after deleting the canvas element. Wish we could return early sooner,
        // but has to be after all the hooks.
        return null;
    }

    const makeToolbarButton = (props: {
        key: string;
        tipL10nKey: string;
        icon: React.FunctionComponent<SvgIconProps>;
        onClick: () => void;
        relativeSize?: number;
        disabled?: boolean;
    }): IToolbarItem => {
        return {
            key: props.key,
            node: (
                <ButtonWithTooltip
                    tipL10nKey={props.tipL10nKey}
                    icon={props.icon}
                    relativeSize={props.relativeSize}
                    disabled={props.disabled}
                    onClick={props.onClick}
                />
            ),
        };
    };

    let menuOptions: IMenuItemWithSubmenu[] = [];
    const handleMenuButtonMouseDown = (e: React.MouseEvent) => {
        // This prevents focus leaving the text box.
        e.preventDefault();
        e.stopPropagation();
    };
    const handleMenuButtonMouseUp = (e: React.MouseEvent) => {
        // This prevents focus leaving the text box.
        e.preventDefault();
        e.stopPropagation();
        setMenuOpen(true); // Review: better on mouse down? But then the mouse up may be missed, if the menu is on top...
    };
    // editable and langName are computed earlier, but keep them here for the UI below.

    const maxMenuWidth = 260;

    const getSpacerToolbarItem = (index: number): IToolbarItem => {
        return {
            key: `spacer-${index}`,
            isSpacer: true,
            node: (
                <div
                    css={css`
                        width: ${buttonWidth};
                    `}
                />
            ),
        };
    };

    let toolbarItems: IToolbarItem[] = [];

    const convertControlMenuRows = (
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
                icon: row.icon,
                disabled: row.disabled,
                featureName: row.featureName,
                subscriptionTooltipOverride: row.subscriptionTooltipOverride,
                onClick: () => {
                    if (!convertedSubMenu) {
                        controlRuntime.closeMenu();
                    }
                    void row.onSelect(controlContext, controlRuntime);
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
                    subLabel: row.helpRowEnglish,
                    onClick: () => {},
                    disabled: true,
                    dontGiveAffordanceForCheckbox: true,
                });
            }
        });

        return convertedRows;
    };

    const getToolbarItemForResolvedControl = (
        item: ReturnType<typeof getToolbarItems>[number],
        index: number,
        controlContext: IControlContext,
    ): IToolbarItem | undefined => {
        if ("id" in item && item.id === "spacer") {
            return getSpacerToolbarItem(index);
        }

        if (item.control.kind !== "command") {
            return undefined;
        }

        if (item.control.toolbar?.render) {
            return {
                key: `${item.control.id}-${index}`,
                node: item.control.toolbar.render(controlContext, {
                    closeMenu: () => {},
                }),
            };
        }

        const icon = item.control.toolbar?.icon ?? item.control.icon;
        const onClick = () => {
            void item.control.action(controlContext, {
                closeMenu: () => {},
            });
        };

        if (typeof icon === "function") {
            return makeToolbarButton({
                key: `${item.control.id}-${index}`,
                tipL10nKey: item.control.tooltipL10nId ?? item.control.l10nId,
                icon,
                onClick,
                relativeSize: item.control.toolbar?.relativeSize,
                disabled: !item.enabled,
            });
        }

        if (!icon) {
            return undefined;
        }

        const renderedIcon = React.isValidElement(icon)
            ? icon
            : typeof icon === "object" && "$$typeof" in (icon as object)
              ? React.createElement(icon as React.ElementType, null)
              : icon;

        return {
            key: `${item.control.id}-${index}`,
            node: (
                <BloomTooltip
                    placement="top"
                    tip={{
                        l10nKey:
                            item.control.tooltipL10nId ?? item.control.l10nId,
                    }}
                >
                    <button
                        onClick={onClick}
                        css={getIconCss(
                            item.control.toolbar?.relativeSize,
                            !item.enabled
                                ? `opacity: ${kBloomDisabledOpacity};`
                                : "",
                        )}
                        disabled={!item.enabled}
                    >
                        {renderedIcon}
                    </button>
                </BloomTooltip>
            ),
        };
    };

    const controlRuntime: IControlRuntime = {
        closeMenu: (launchingDialog?: boolean) => {
            setMenuOpen(false, launchingDialog);
        },
    };

    const controlContext: IControlContext = {
        ...buildControlContext(props.canvasElement),
        textHasAudio,
    };

    const definition =
        controlCanvasElementDefinitions[controlContext.elementType] ??
        controlCanvasElementDefinitions.none;

    menuOptions = joinMenuSectionsWithSingleDividers(
        getMenuSections(definition, controlContext, controlRuntime).map(
            (section) =>
                convertControlMenuRows(
                    section
                        .map((item) => item.menuRow)
                        .filter((row): row is IControlMenuRow => !!row),
                    controlContext,
                    controlRuntime,
                ),
        ),
    );

    toolbarItems = normalizeToolbarItems(
        getToolbarItems(definition, controlContext, controlRuntime)
            .map((item, index) =>
                getToolbarItemForResolvedControl(item, index, controlContext),
            )
            .filter((item): item is IToolbarItem => !!item),
    );

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                css={css`
                    background-color: white;
                    border-radius: 3.785px;
                    border: 0.757px solid rgba(255, 255, 255, 0.2);
                    //opacity: 0.2;
                    box-shadow: 0px 0px 4px 0px rgba(0, 0, 0, 0.25);
                    border-radius: 4px;
                    display: flex;
                    flex-direction: column;
                    padding: 0px 10px 0px;
                    margin: 0 auto 0 auto;
                    width: fit-content;
                    // needed because it's a child of #canvas-element-context-controls which has pointer-events:none
                    pointer-events: all;
                `}
            >
                {isBackgroundImage && canvasHasMultipleElements && (
                    <div
                        css={css`
                            color: ${kBloomBlue};
                            text-align: center;
                            font-size: 8pt;
                        `}
                    >
                        <strong>{backgroundImageText}</strong>
                    </div>
                )}
                <div
                    css={css`
                        display: flex;
                        align-items: center;
                        // Not really sure what's going on here, since none of the buttons contain text
                        // But somehow they have a tendency to be several pixels higher than the contained
                        // icons, and this seems to be related to line-height. I don't want to set it
                        // to zero, in case (in some language) the tooltips wrap. But this seems to be small enough
                        // to prevent the problem.
                        line-height: 0.8em;
                        button {
                            line-height: 0.7em;
                        }
                    `}
                >
                    {toolbarItems.map((item) => (
                        <React.Fragment key={item.key}>
                            {item.node}
                        </React.Fragment>
                    ))}
                    <button
                        ref={(ref) => (menuEl.current = ref)}
                        css={getIconCss()}
                        // It would be more natural to handle a click. But clicks are a combination of
                        // mouse down and mouse up, and those have side effects, especially change of focus,
                        // that we need to prevent. So we handle them ourselves.
                        onMouseDown={handleMenuButtonMouseDown}
                        onMouseUp={handleMenuButtonMouseUp}
                    >
                        <MenuIcon color="primary" />
                    </button>
                    <Menu
                        // if we don't keep the menu mounted, then whenever the menu opens it calculates its size and
                        // the localizations aren't done yet at that point so it positions itself incorrectly (BL-14549).
                        // The other option would be to put a resize observer on the menu, and use an action prop and
                        // call updatePosition() whenever it resizes
                        keepMounted
                        css={css`
                            ul {
                                max-width: ${maxMenuWidth}px;
                                color: #4d4d4d;
                                li {
                                    display: flex;
                                    align-items: flex-start;
                                    color: #4d4d4d;
                                    svg {
                                        color: #4d4d4d;
                                    }
                                    p,
                                    span {
                                        color: #4d4d4d;
                                    }
                                    img.canvas-context-menu-monochrome-icon {
                                        filter: brightness(0) saturate(100%)
                                            invert(31%) sepia(0%) saturate(0%)
                                            hue-rotate(180deg) brightness(95%)
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
                        `}
                        open={
                            props.menuOpen &&
                            (!!props.menuAnchorPosition || !!menuEl.current)
                        }
                        anchorEl={
                            props.menuAnchorPosition ? null : menuEl.current
                        }
                        anchorReference={
                            props.menuAnchorPosition
                                ? "anchorPosition"
                                : "anchorEl"
                        }
                        anchorPosition={props.menuAnchorPosition}
                        onClose={() => setMenuOpen(false)}
                        disableAutoFocus={true}
                        disableEnforceFocus={true}
                    >
                        {menuOptions.map((option, index) => {
                            if (option.l10nId === "-") {
                                return (
                                    <Divider
                                        key={index}
                                        variant="middle"
                                        component="li"
                                    />
                                );
                            }
                            if (option.subMenu) {
                                return (
                                    <LocalizableNestedMenuItem
                                        {...option}
                                        key={option.l10nId}
                                        truncateMainLabel={true}
                                    >
                                        {option.subMenu.map(
                                            (subOption, subIndex) => {
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
                                                        onClick={(e) => {
                                                            setMenuOpen(false);
                                                            subOption.onClick(
                                                                e,
                                                            );
                                                        }}
                                                        css={css`
                                                            max-width: ${maxMenuWidth}px;
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
                                            },
                                        )}
                                    </LocalizableNestedMenuItem>
                                );
                            }
                            return (
                                <LocalizableMenuItem
                                    key={option.l10nId}
                                    {...option}
                                    onClick={(e) => {
                                        setMenuOpen(false);
                                        option.onClick(e);
                                    }}
                                    variant="body1"
                                />
                            );
                        })}
                    </Menu>
                </div>
                {langName && (
                    <div
                        css={css`
                            color: ${kBloomBlue};
                            font-size: 10px;
                            margin-top: -4px; // pull it up tighter to the buttons
                            margin-left: 2px; // align with the first icon; the button has a 2px border
                        `}
                    >
                        {langName}
                    </div>
                )}
            </div>
        </ThemeProvider>
    );
};

const buttonWidth = "22px";

const ButtonWithTooltip: React.FunctionComponent<{
    icon: React.FunctionComponent<SvgIconProps>;
    tipL10nKey: string;
    onClick: React.MouseEventHandler;
    relativeSize?: number;
    disabled?: boolean;
}> = (props) => {
    return (
        <BloomTooltip
            placement="top"
            tip={{
                l10nKey: props.tipL10nKey,
            }}
        >
            <button
                onClick={props.onClick}
                css={getIconCss(
                    props.relativeSize,
                    props.disabled ? `opacity: ${kBloomDisabledOpacity};` : "",
                )}
                disabled={props.disabled}
            >
                <props.icon color="primary" />
            </button>
        </BloomTooltip>
    );
};

// This is used to render the CanvasElementContextControls as the root component of a div.
export function renderCanvasElementContextControls(
    canvasElement: HTMLElement,
    menuOpen: boolean,
    menuAnchorPosition?: { left: number; top: number },
) {
    const root = document.getElementById("canvas-element-context-controls");
    if (!root) {
        // not created yet, try later
        setTimeout(
            () =>
                renderCanvasElementContextControls(
                    canvasElement,
                    menuOpen,
                    menuAnchorPosition,
                ),
            200,
        );
        return;
    }
    ReactDOM.render(
        <CanvasElementContextControls
            canvasElement={canvasElement}
            menuOpen={menuOpen}
            setMenuOpen={(open: boolean) => {
                // turns out we don't need to store it anywhere. When it requests a change, we just
                // re-render it that way.
                renderCanvasElementContextControls(canvasElement, open);
            }}
            menuAnchorPosition={menuAnchorPosition}
        />,
        root,
    );
}

function getIconCss(relativeSize?: number, extra = "") {
    const defaultFontSize = 1.3;
    const fontSize = defaultFontSize * (relativeSize ?? 1);
    return css`
        ${extra}
        border-color: transparent;
        background-color: transparent;
        color: ${kBloomBlue};
        vertical-align: middle;
        width: ${buttonWidth};
        svg {
            font-size: ${fontSize}rem;
        }
    `;
}

function joinMenuSectionsWithSingleDividers(
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
