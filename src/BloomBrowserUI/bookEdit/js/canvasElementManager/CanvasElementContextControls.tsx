import { css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect, useRef } from "react";
import { renderRoot } from "../../../utils/reactRender";
import { kBloomBlue, lightTheme } from "../../../bloomMaterialUITheme";
import { SvgIconProps } from "@mui/material";
import { default as MenuIcon } from "@mui/icons-material/MoreHorizSharp";
import { ThemeProvider } from "@mui/material/styles";
import Menu from "@mui/material/Menu";
import { getCanvasElementManager } from "../../toolbox/canvas/canvasElementPageBridge";
import { kBackgroundImageClass } from "../../toolbox/canvas/canvasElementConstants";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { useL10n } from "../../../react_components/l10nHooks";
import { useGetFeatureStatus } from "../../../react_components/featureStatus";
import { kBloomDisabledOpacity } from "../../../utils/colorUtils";
import { getAsync, useApiObject } from "../../../utils/bloomApi";
import { audioExistsForIdsAsync } from "../../toolbox/talkingBook/audioUtils";
import { getAudioSentencesOfVisibleEditables } from "bloom-player";
import { canvasElementControlRegistry } from "../../toolbox/canvas/canvasElementControlRegistry";
import { buildCanvasElementControlRegistryContext } from "../../toolbox/canvas/buildCanvasElementControlRegistryContext";
import {
    IControlContext,
    ILanguageNameValues,
    IControlMenuRow,
    IControlRuntime,
} from "../../toolbox/canvas/canvasControlTypes";
import {
    getMenuSections,
    getToolbarItems,
} from "../../toolbox/canvas/canvasControlResolution";
import {
    contextMenuCss,
    convertControlMenuRows,
    IMenuItemWithSubmenu,
    joinMenuSectionsWithSingleDividers,
    renderContextMenuItems,
    runControlCallback,
    scaleIconNode,
} from "./canvasControlMenuRendering";

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
    // The "Edit with AI" command is an experimental, subscription-gated feature.
    // Its FeatureStatus.visible reflects whether the experimental feature is on;
    // we feed that into the control context so the menu item is hidden when off.
    const aiImageEditingStatus = useGetFeatureStatus("AiImageEditing");
    const languageNameValues = useApiObject<ILanguageNameValues>(
        "settings/languageNames",
        {
            language1Name: "",
            language1Tag: "",
            language2Name: "",
            language2Tag: "",
        },
    );

    // After deleting a draggable, we may get rendered again, and page will be null.
    const page = props.canvasElement.closest(".bloom-page");

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

    // Collapse duplicate spacers and trim any spacer left at either edge after
    // controls are filtered or remapped for the current canvas element state.
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
    const [hasClipboardText, setHasClipboardText] = useState(false);

    // Refresh the text-audio state when the menu opens so submenu labels and
    // commands reflect the current recording state.
    useEffect(() => {
        if (!props.menuOpen || !props.canvasElement || !hasText) return;

        const audioSentences = getAudioSentencesOfVisibleEditables(
            props.canvasElement,
        );
        const ids = audioSentences.map((sentence) => sentence.id);
        audioExistsForIdsAsync(ids)
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

    // Query the host clipboard when the menu opens so Paste Text availability
    // reflects whether there is currently text to paste.
    useEffect(() => {
        if (!props.menuOpen || !props.canvasElement || !hasText) {
            return;
        }

        let isCurrent = true;
        setHasClipboardText(false);

        getAsync("common/clipboardText")
            .then((response) => {
                if (!isCurrent) {
                    return;
                }

                const clipboardText =
                    typeof response.data === "string"
                        ? response.data
                        : (response.data?.data ?? "");
                setHasClipboardText(clipboardText.length > 0);
            })
            .catch((error) => {
                if (!isCurrent) {
                    return;
                }

                console.error(
                    "Error checking clipboard text availability:",
                    error,
                );
            });

        return () => {
            isCurrent = false;
        };
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
        iconScale?: number;
        disabled?: boolean;
    }): IToolbarItem => {
        return {
            key: props.key,
            node: (
                <ButtonWithTooltip
                    tipL10nKey={props.tipL10nKey}
                    icon={props.icon}
                    iconScale={props.iconScale}
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

    const getToolbarItemForResolvedControl = (
        item: ReturnType<typeof getToolbarItems>[number],
        index: number,
        controlContext: IControlContext,
    ): IToolbarItem | undefined => {
        if (!("control" in item)) {
            return getSpacerToolbarItem(index);
        }

        if (item.control.kind !== "command") {
            return undefined;
        }

        const control = item.control;

        if (control.toolbar?.render) {
            return {
                key: `${control.id}-${index}`,
                node: control.toolbar.render(controlContext, {
                    closeMenu: () => {},
                }),
            };
        }

        const icon = control.toolbar?.icon ?? control.icon;
        const iconScale = control.toolbar?.iconScale ?? control.iconScale;
        const onClick = () => {
            runControlCallback(`toolbar:${control.id}`, () =>
                control.action(controlContext, {
                    closeMenu: () => {},
                }),
            );
        };

        if (typeof icon === "function") {
            return makeToolbarButton({
                key: `${control.id}-${index}`,
                tipL10nKey: control.tooltipL10nId ?? control.l10nId,
                icon: icon as React.FunctionComponent<SvgIconProps>,
                onClick,
                iconScale: iconScale ?? 1,
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
            key: `${control.id}-${index}`,
            node: (
                <BloomTooltip
                    placement="top"
                    tip={{
                        l10nKey: control.tooltipL10nId ?? control.l10nId,
                    }}
                >
                    <button
                        onClick={onClick}
                        css={getIconCss(
                            iconScale,
                            !item.enabled
                                ? `opacity: ${kBloomDisabledOpacity};`
                                : "",
                        )}
                        disabled={!item.enabled}
                    >
                        {scaleIconNode(renderedIcon, iconScale)}
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
        ...buildCanvasElementControlRegistryContext(props.canvasElement),
        textHasAudio,
        hasClipboardText,
        languageNameValues,
        aiImageEditingAvailable: aiImageEditingStatus?.visible ?? false,
    };

    const definition =
        canvasElementControlRegistry[controlContext.elementType] ??
        canvasElementControlRegistry.none;

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
                        data-testid="canvas-context-menu-button"
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
                        css={contextMenuCss}
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
                        {renderContextMenuItems(menuOptions)}
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
    iconScale?: number;
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
                    props.iconScale,
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
    renderRoot(
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

function getIconCss(iconScale?: number, extra = "") {
    const defaultFontSize = 1.3;
    const fontSize = defaultFontSize * (iconScale ?? 1);
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
