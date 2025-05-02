import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect, Fragment, useRef } from "react";
import * as ReactDOM from "react-dom";
import { kBloomBlue, lightTheme } from "../../bloomMaterialUITheme";
import { SvgIconProps } from "@mui/material";
import { default as CopyrightIcon } from "@mui/icons-material/Copyright";
import { default as SearchIcon } from "@mui/icons-material/Search";
import { default as MenuIcon } from "@mui/icons-material/MoreHorizSharp";
import { default as CopyIcon } from "@mui/icons-material/ContentCopy";
import { default as CheckIcon } from "@mui/icons-material/Check";
import { default as VolumeUpIcon } from "@mui/icons-material/VolumeUp";
import { default as PasteIcon } from "@mui/icons-material/ContentPaste";
import { default as CircleIcon } from "@mui/icons-material/Circle";
import { default as DeleteIcon } from "@mui/icons-material/DeleteOutline";
import { default as ArrowUpwardIcon } from "@mui/icons-material/ArrowUpward";
import { default as ArrowDownwardIcon } from "@mui/icons-material/ArrowDownward";
// MUI thinks of this icon as "Texture", but we're using it for commands that affect the background image.
import { default as BackgroundIcon } from "@mui/icons-material/Texture";
import { showCopyrightAndLicenseDialog } from "../editViewFrame";
import {
    doImageCommand,
    getImageUrlFromImageContainer,
    kImageContainerClass
} from "./bloomImages";
import {
    doVideoCommand,
    findNextVideoContainer,
    findPreviousVideoContainer
} from "./bloomVideo";
import {
    copyAndPlaySoundAsync,
    makeDuplicateOfDragBubble,
    makeTargetForDraggable,
    playSound,
    showDialogToChooseSoundFileAsync
} from "../toolbox/games/GameTool";
import { ThemeProvider } from "@mui/material/styles";
import {
    divider,
    ILocalizableMenuItemProps,
    LocalizableMenuItem,
    LocalizableNestedMenuItem
} from "../../react_components/localizableMenuItem";
import Menu from "@mui/material/Menu";
import { Divider } from "@mui/material";
import { DuplicateIcon } from "./DuplicateIcon";
import {
    CanvasElementManager,
    kBackgroundImageClass,
    theOneCanvasElementManager
} from "./CanvasElementManager";
import { copySelection, GetEditor, pasteClipboard } from "./bloomEditing";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { useL10n } from "../../react_components/l10nHooks";
import { CogIcon } from "./CogIcon";
import {
    getHyperlinkFromClipboard,
    tryProcessHyperlink
} from "../bloomField/hyperlinks";
import { useApiString } from "../../utils/bloomApi";
import { MissingMetadataIcon } from "./MissingMetadataIcon";
import { FillSpaceIcon } from "./FillSpaceIcon";
import { kBloomDisabledOpacity } from "../../utils/colorUtils";
import { Span } from "../../react_components/l10nComponents";

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
}> = props => {
    const imgContainer = props.canvasElement.getElementsByClassName(
        kImageContainerClass
    )[0];
    const hasImage = !!imgContainer;
    const hasText =
        props.canvasElement.getElementsByClassName("bloom-editable").length > 0;
    const rectangles = props.canvasElement.getElementsByClassName(
        "bloom-rectangle"
    );
    // This is only used by the menu option that toggles it. If the menu stayed up, we would need a state
    // and useEffect. But since it closes when we choose an option, we can just get the current value to show
    // in the current menu opening.
    const hasRectangle = rectangles.length > 0;
    const rectangleHasBackground = rectangles[0]?.classList.contains(
        "bloom-theme-background"
    );
    const img = imgContainer?.getElementsByTagName("img")[0];
    //const hasLicenseProblem = hasImage && !img.getAttribute("data-copyright");
    const videoContainer = props.canvasElement.getElementsByClassName(
        "bloom-videoContainer"
    )[0];
    const hasVideo = !!videoContainer;
    const video = videoContainer?.getElementsByTagName("video")[0];
    const videoSource = video?.getElementsByTagName("source")[0];
    const videoAlreadyChosen = !!videoSource?.getAttribute("src");
    const isPlaceHolder =
        hasImage && img?.getAttribute("src")?.startsWith("placeHolder.png");
    const missingMetadata =
        hasImage &&
        !isPlaceHolder &&
        img &&
        !img.getAttribute("data-copyright");
    const setMenuOpen = (open: boolean, launchingDialog?: boolean) => {
        // Even though we've done our best to tell the MUI menu NOT to steal focus, it seems it still does...
        // or some other code somewhere is doing it when we choose a menu item. So we tell the CanvasElementManager
        // to ignore focus changes while the menu is open.
        if (open) {
            CanvasElementManager.ignoreFocusChanges = true;
        }
        props.setMenuOpen(open);
        // Setting ignoreFocusChanges to false immediately after closing the menu doesn't work,
        // because the the focus change is still happening after the menu closes.  This timeout
        // ensures that the focus change is ignored immediately after the menu closes.
        // The skipNextFocusChange flag is used to prevent the focus change that happens when
        // a dialog opened by the menu command closes.  See BL-14123.
        if (!open) {
            setTimeout(() => {
                if (launchingDialog)
                    CanvasElementManager.skipNextFocusChange = true;
                CanvasElementManager.ignoreFocusChanges = false;
            }, 0);
        }
    };

    const menuEl = useRef<HTMLElement | null>();

    // Menu item names for 'none' and "Choose...", options.
    const noneLabel = useL10n("None", "EditTab.Toolbox.DragActivity.None", "");

    const currentDraggableTargetId = props.canvasElement?.getAttribute(
        "data-draggable-id"
    );
    const [currentDraggableTarget, setCurrentDraggableTarget] = useState<
        HTMLElement | undefined
    >();
    useEffect(() => {
        if (!currentDraggableTargetId) {
            setCurrentDraggableTarget(undefined);
            return;
        }
        const page = props.canvasElement.closest(".bloom-page") as HTMLElement;
        setCurrentDraggableTarget(
            page?.querySelector(
                `[data-target-of="${currentDraggableTargetId}"]`
            ) as HTMLElement
        );
        // We need to re-evaluate when changing pages, it's possible the initially selected item
        // on a new page has the same currentDraggableTargetId.
    }, [currentDraggableTargetId]);

    // Currently we only allow associating an extra audio with images (and gifs), which have
    // no other audio (except possibly image descriptions?). If we get an actual user request
    // it may be clearer how attaching one to a text or video would work, given that they
    // can already have narration or an audio channel.
    const canChooseAudioForElement = hasImage;

    const [imageSound, setImageSound] = useState("none");
    useEffect(() => {
        setImageSound(props.canvasElement.getAttribute("data-sound") ?? "none");
    }, [props.canvasElement]);
    const isBackgroundImage = props.canvasElement.classList.contains(
        kBackgroundImageClass
    );
    const canExpandBackgroundImage = theOneCanvasElementManager?.canExpandToFillSpace();

    // These commands apply to all canvas elements (currently none!).
    const menuOptions: IMenuItemWithSubmenu[] = [];
    // These to everything except background images
    if (!isBackgroundImage) {
        menuOptions.unshift({
            l10nId: "EditTab.Toolbox.ComicTool.Options.Duplicate",
            english: "Duplicate",
            onClick: () => {
                if (!props.canvasElement) return;
                makeDuplicateOfDragBubble();
            },
            icon: <DuplicateIcon css={getMenuIconCss()} />
        });
    }
    if (hasRectangle) {
        menuOptions.splice(0, 0, {
            l10nId: "EditTab.Toolbox.ComicTool.Options.FillBackground",
            english: "Fill Background",
            onClick: () => {
                props.canvasElement
                    .getElementsByClassName("bloom-rectangle")[0]
                    ?.classList.toggle("bloom-theme-background");
            },
            icon: rectangleHasBackground && <CheckIcon css={getMenuIconCss()} />
        });
    }
    if (hasText) {
        menuOptions.splice(0, 0, {
            l10nId: "EditTab.Toolbox.ComicTool.Options.AddChildBubble",
            english: "Add Child Bubble",
            onClick: theOneCanvasElementManager?.addChildCanvasElement
        });
    }
    if (currentDraggableTargetId) {
        addMenuItemsForDraggable(
            menuOptions,
            props.canvasElement,
            currentDraggableTargetId,
            currentDraggableTarget,
            setCurrentDraggableTarget
        );
    }
    if (canChooseAudioForElement) {
        addAudioMenuItems(
            menuOptions,
            props.canvasElement,
            imageSound,
            noneLabel,
            setImageSound,
            setMenuOpen
        );
    }
    if (hasImage) {
        const canModifyImage = !imgContainer.classList.contains(
            "bloom-unmodifiable-image"
        );
        if (canModifyImage)
            addImageMenuOptions(
                menuOptions,
                props.canvasElement,
                img,
                setMenuOpen
            );
    }
    if (hasVideo) {
        addVideoMenuItems(menuOptions, videoContainer, setMenuOpen);
    }
    let deleteEnabled = true;
    if (isBackgroundImage) {
        const fillItem = {
            l10nId: "EditTab.Toolbox.ComicTool.Options.FillSpace",
            english: "Fit Space",
            onClick: () => theOneCanvasElementManager?.expandImageToFillSpace(),
            disabled: !canExpandBackgroundImage,
            icon: (
                <img
                    src="/bloom/images/fill image black.svg"
                    // tweak to align better for an icon that is wider than most
                    css={getMenuIconCss(1, "left: -3px;")}
                />
            )
        };
        let index = menuOptions.findIndex(
            option => option.l10nId === "EditTab.Image.Reset"
        );
        if (index < 0) {
            index = menuOptions.indexOf(divider);
        }
        menuOptions.splice(index, 0, fillItem);

        // we can't delete the placeholder (or if there isn't an img, somehow)
        deleteEnabled = !!(
            img && !img.getAttribute("src")?.startsWith("placeHolder.png")
        );
    }

    // last one
    menuOptions.push(divider, {
        l10nId: "Common.Delete",
        english: "Delete",
        disabled: !deleteEnabled,
        onClick: theOneCanvasElementManager?.deleteCurrentCanvasElement,
        icon: <DeleteIcon css={getMenuIconCss()} />
    });
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
    const editable = props.canvasElement.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on"
    )[0] as HTMLElement;
    const langName = editable?.getAttribute("data-languagetipcontent");
    // and these for text boxes
    if (editable) {
        addTextMenuItems(menuOptions, editable, props.canvasElement);
    }

    const runMetadataDialog = () => {
        if (!props.canvasElement) return;
        if (!imgContainer) return;
        showCopyrightAndLicenseDialog(
            getImageUrlFromImageContainer(imgContainer as HTMLElement)
        );
    };

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
                    {hasImage && (
                        <Fragment>
                            {// Want an attention-grabbing version of set metadata if there is none.)
                            missingMetadata && (
                                <ButtonWithTooltip
                                    tipL10nKey="EditTab.Image.EditMetadataOverlay"
                                    icon={MissingMetadataIcon}
                                    onClick={() => runMetadataDialog()}
                                />
                            )}
                            {// Choose image is only a LIKELY choice if we don't yet have one.
                            // (or if it's a background image...not sure why, except otherwise
                            // the toolbar might not have any icons for a background image.)
                            (isPlaceHolder || isBackgroundImage) && (
                                <ButtonWithTooltip
                                    tipL10nKey="EditTab.Image.ChooseImage"
                                    icon={SearchIcon}
                                    onClick={_ => {
                                        if (!props.canvasElement) return;
                                        const imgContainer = props.canvasElement.getElementsByClassName(
                                            kImageContainerClass
                                        )[0] as HTMLElement;
                                        if (!imgContainer) return;
                                        doImageCommand(
                                            imgContainer.getElementsByTagName(
                                                "img"
                                            )[0] as HTMLImageElement,
                                            "change"
                                        );
                                    }}
                                />
                            )}
                            {(isPlaceHolder || isBackgroundImage) && (
                                <ButtonWithTooltip
                                    tipL10nKey="EditTab.Image.PasteImage"
                                    icon={PasteIcon}
                                    relativeSize={0.9}
                                    onClick={_ => {
                                        if (!props.canvasElement) return;
                                        const imgContainer = props.canvasElement.getElementsByClassName(
                                            kImageContainerClass
                                        )[0] as HTMLElement;
                                        if (!imgContainer) return;
                                        doImageCommand(
                                            imgContainer.getElementsByTagName(
                                                "img"
                                            )[0] as HTMLImageElement,
                                            "paste"
                                        );
                                    }}
                                ></ButtonWithTooltip>
                            )}
                        </Fragment>
                    )}
                    {editable && (
                        <ButtonWithTooltip
                            tipL10nKey="EditTab.Toolbox.ComicTool.Options.Format"
                            icon={CogIcon}
                            relativeSize={0.8}
                            onClick={() => {
                                if (!props.canvasElement) return;
                                GetEditor().runFormatDialog(editable);
                            }}
                        />
                    )}
                    {hasVideo && !videoAlreadyChosen && (
                        <Fragment>
                            <ButtonWithTooltip
                                tipL10nKey="EditTab.Toolbox.ComicTool.Options.ChooseVideo"
                                icon={SearchIcon}
                                onClick={() =>
                                    doVideoCommand(videoContainer, "choose")
                                }
                            />
                            <ButtonWithTooltip
                                tipL10nKey="EditTab.Toolbox.ComicTool.Options.RecordYourself"
                                icon={CircleIcon}
                                relativeSize={0.8}
                                onClick={() =>
                                    doVideoCommand(videoContainer, "record")
                                }
                            />
                        </Fragment>
                    )}
                    {(!(hasImage && isPlaceHolder) &&
                        !editable &&
                        !(hasVideo && !videoAlreadyChosen)) || (
                        // Add a spacer if there is any button before these
                        <div
                            css={css`
                                width: ${buttonWidth};
                            `}
                        />
                    )}
                    {!hasVideo && !isBackgroundImage && (
                        <ButtonWithTooltip
                            tipL10nKey="EditTab.Toolbox.ComicTool.Options.Duplicate"
                            icon={DuplicateIcon}
                            relativeSize={0.9}
                            onClick={() => {
                                if (!props.canvasElement) return;
                                makeDuplicateOfDragBubble();
                            }}
                        />
                    )}
                    {// Not sure of the reasoning here, since we do have a way to 'delete' a background image,
                    // not by removing the canvas element but by setting the image back to a placeholder.
                    // But the mockup in BL-14069 definitely doesn't have it.
                    isBackgroundImage || (
                        <ButtonWithTooltip
                            tipL10nKey="Common.Delete"
                            icon={DeleteIcon}
                            onClick={() => {
                                if (!props.canvasElement) return;
                                theOneCanvasElementManager?.deleteCurrentCanvasElement();
                            }}
                        />
                    )}
                    {isBackgroundImage && (
                        <ButtonWithTooltip
                            tipL10nKey="EditTab.Toolbox.ComicTool.Options.FillSpace"
                            icon={FillSpaceIcon}
                            disabled={!canExpandBackgroundImage}
                            onClick={() => {
                                if (!props.canvasElement) return;
                                theOneCanvasElementManager?.expandImageToFillSpace();
                            }}
                        />
                    )}
                    <button
                        ref={ref => (menuEl.current = ref)}
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
                        css={css`
                            ul {
                                max-width: 260px;
                                li {
                                    display: flex;
                                    align-items: flex-start;
                                    p {
                                        white-space: initial;
                                    }
                                    &.MuiDivider-root {
                                        margin-bottom: 12px;
                                    }
                                }
                            }
                        `}
                        open={props.menuOpen}
                        // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
                        anchorEl={menuEl.current!}
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
                                        key={index}
                                        truncateMainLabel={true}
                                    >
                                        {option.subMenu.map(
                                            (subOption, subIndex) => (
                                                <LocalizableMenuItem
                                                    key={subIndex}
                                                    {...subOption}
                                                />
                                            )
                                        )}
                                    </LocalizableNestedMenuItem>
                                );
                            }
                            return (
                                <LocalizableMenuItem
                                    key={index}
                                    {...option}
                                    onClick={e => {
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
}> = props => {
    return (
        <BloomTooltip
            placement="top"
            tip={{
                l10nKey: props.tipL10nKey
            }}
        >
            <button
                onClick={props.onClick}
                css={getIconCss(
                    props.relativeSize,
                    props.disabled ? `opacity: ${kBloomDisabledOpacity};` : ""
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
    menuAnchorPosition?: { left: number; top: number }
) {
    const root = document.getElementById("canvas-element-context-controls");
    if (!root) {
        // not created yet, try later
        setTimeout(
            () =>
                renderCanvasElementContextControls(
                    canvasElement,
                    menuOpen,
                    menuAnchorPosition
                ),
            200
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
        root
    );
}

function getIconCss(relativeSize?: number, extra = "") {
    const defaultFontSize = 1.3;
    const fontSize = defaultFontSize * (relativeSize ?? 1);
    return css`
        ${extra}
        border-color: transparent;
        background-color: transparent;
        vertical-align: middle;
        width: ${buttonWidth};
        svg {
            font-size: ${fontSize}rem;
        }
    `;
}

function getMenuIconCss(relativeSize?: number, extra = "") {
    const defaultFontSize = 1.3;
    const fontSize = defaultFontSize * (relativeSize ?? 1);
    return css`
        color: black;
        font-size: ${fontSize}rem;
        ${extra}
    `;
}

function addTextMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    editable: HTMLElement,
    canvasElement: HTMLElement
) {
    const autoHeight = !canvasElement.classList.contains("bloom-noAutoHeight");
    const toggleAutoHeight = () => {
        canvasElement.classList.toggle("bloom-noAutoHeight");
        theOneCanvasElementManager.updateAutoHeight();
        // In most contexts, we would need to do something now to make the control render, so we get
        // an updated value for autoHeight. But the menu is going to be hidden, and showing it again
        // will involve a re-render, and we don't care until then.
    };
    menuOptions.unshift(
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.Format",
            english: "Format",
            onClick: () => GetEditor().runFormatDialog(editable),
            icon: <CogIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.CopyText",
            english: "Copy Text",
            onClick: () => copySelection(),
            icon: <CopyIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PasteText",
            english: "Paste Text",
            onClick: () => {
                // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
                pasteClipboard(false);
            },
            icon: <PasteIcon css={getMenuIconCss()} />
        },
        divider,
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.AutoHeight",
            english: "Auto Height",
            // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
            onClick: () => toggleAutoHeight(),
            icon: autoHeight && <CheckIcon css={getMenuIconCss()} />
        }
    );
}

function addVideoMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    videoContainer: Element,
    setMenuOpen: (open: boolean, launchingDialog?: boolean) => void
) {
    menuOptions.unshift(
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
            english: "Choose Video from your Computer...",
            onClick: () => {
                doVideoCommand(videoContainer, "choose");
                setMenuOpen(false, true);
            },
            icon: <SearchIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.RecordYourself",
            english: "Record yourself...",
            onClick: () => doVideoCommand(videoContainer, "record"),
            icon: <CircleIcon css={getMenuIconCss(0.85)} />
        },
        divider,
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PlayEarlier",
            english: "Play Earlier",
            onClick: () => {
                doVideoCommand(videoContainer, "playEarlier");
            },
            icon: <ArrowUpwardIcon css={getMenuIconCss()} />,
            disabled: !findPreviousVideoContainer(videoContainer)
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PlayLater",
            english: "Play Later",
            onClick: () => {
                doVideoCommand(videoContainer, "playLater");
            },
            icon: <ArrowDownwardIcon css={getMenuIconCss()} />,
            disabled: !findNextVideoContainer(videoContainer)
        },
        divider
    );
}

function addImageMenuOptions(
    menuOptions: IMenuItemWithSubmenu[],
    canvasElement: HTMLElement,
    img: HTMLElement,
    setMenuOpen: (open: boolean, launchingDialog?: boolean) => void
) {
    const imgContainer = canvasElement.getElementsByClassName(
        kImageContainerClass
    )[0] as HTMLElement;

    const isCropped = !!img?.style.width;

    const runMetadataDialog = () => {
        if (!canvasElement) return;
        if (!imgContainer) return;
        showCopyrightAndLicenseDialog(
            getImageUrlFromImageContainer(imgContainer)
        );
    };

    menuOptions.unshift(
        {
            l10nId: "EditTab.Image.ChooseImage",
            english: "Choose image from your computer...",
            onClick: () => {
                doImageCommand(img, "change");
                setMenuOpen(false, true);
            },
            icon: <SearchIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Image.PasteImage",
            english: "Paste image",
            onClick: () => doImageCommand(img, "paste"),
            icon: <PasteIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Image.CopyImage",
            english: "Copy image",
            onClick: () => doImageCommand(img, "copy"),
            icon: <CopyIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Image.EditMetadataOverlay",
            english: "Set Image Information...",
            subLabelL10nId: "EditTab.Image.EditMetadataOverlayMore",
            onClick: runMetadataDialog,
            icon: <CopyrightIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Image.Reset",
            english: "Reset Image",
            onClick: () => {
                theOneCanvasElementManager?.resetCropping();
            },
            disabled: !isCropped,
            icon: (
                <img
                    src="/bloom/images/reset image black.svg"
                    // tweak to align better and make it look the same size as the other icons
                    css={getMenuIconCss(1, "left: -1px; width: 22px;")}
                />
            )
        },
        divider,
        {
            l10nId: "EditTab.PasteHyperlink",
            english: "Paste Hyperlink",
            subLabel: imgContainer.getAttribute("data-href") && (
                <Span
                    l10nKey="EditTab.PasteHyperlink.Currently"
                    l10nParam0={imgContainer.getAttribute("data-href") || ""}
                >
                    Currently: %0
                </Span>
            ),
            requiresAnySubscription: true,
            onClick: () => pasteLink(canvasElement)

            /*
            Since the clipboard is not readable by us directly, but
            only through an async call to the server, I haven't found
            a way to know if there is a hyperlink on the clipboard. I could
            know if there was one when we last rendered.
            disabled: !haveHyperlinkOnClipboard
            */
        }
        // Enhance: some way to remove a link you don't want anymore. For now, you can paste an empty string.
    );
}
function pasteLink(canvasElement: HTMLElement) {
    const imgContainer = canvasElement.getElementsByClassName(
        kImageContainerClass
    )[0];

    getHyperlinkFromClipboard(url => {
        if (url) imgContainer.setAttribute("data-href", url);
        else {
            if (imgContainer.hasAttribute("data-href")) {
                imgContainer.removeAttribute("data-href");
                // Note, not localizing this stuff yet. A better
                // UX would be nice, just doing this budge English alert for now.
                alert(
                    "Did not find a hyperlink on the clipboard, so the existing hyperlink was removed."
                );
            } else {
                // Note, not localizing this stuff yet. A better
                // UX would be nice, just doing this budge English alert for now.
                alert("Did not find a hyperlink on the clipboard.");
            }
        }
    });
}

function addMenuItemsForDraggable(
    menuOptions: IMenuItemWithSubmenu[],
    canvasElement: HTMLElement,
    currentDraggableTargetId: string,
    currentDraggableTarget: HTMLElement | undefined,
    setCurrentDraggableTarget: (target: HTMLElement | undefined) => void
) {
    const toggleIsPartOfRightAnswer = () => {
        if (!currentDraggableTargetId) {
            return;
        }
        if (currentDraggableTarget) {
            currentDraggableTarget.ownerDocument
                .getElementById("target-arrow")
                ?.remove();
            currentDraggableTarget.remove();
            setCurrentDraggableTarget(undefined);
        } else {
            setCurrentDraggableTarget(makeTargetForDraggable(canvasElement));
        }
    };
    menuOptions.push(divider, {
        l10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswer",
        english: "Part of the right answer",
        subLabelL10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswerMore",
        onClick: toggleIsPartOfRightAnswer,
        icon: currentDraggableTarget ? (
            <CheckIcon css={getMenuIconCss()} />
        ) : (
            undefined
        )
    });
}

function addAudioMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    canvasElement: HTMLElement,
    imageSound: string,
    noneLabel: string,
    setImageSound: (sound: string) => void,
    setMenuOpen: (open: boolean, launchingDialog?: boolean) => void
) {
    // This is uncomfortably similar to the method by the same name in dragActivityTool.
    // And indeed that method has a case for handling an image sound, which is no longer
    // handled on the toolbox side. But both methods make use of component state in
    // ways that make sharing code difficult.
    const updateSoundShowingDialog = async () => {
        const newSoundId = await showDialogToChooseSoundFileAsync();
        if (!newSoundId) {
            return;
        }

        const page = canvasElement.closest(".bloom-page") as HTMLElement;
        const copyBuiltIn = false; // already copied, and not in our sounds folder
        canvasElement.setAttribute("data-sound", newSoundId);
        setImageSound(newSoundId);
        copyAndPlaySoundAsync(newSoundId, page, copyBuiltIn);
    };
    const imageSoundLabel = imageSound.replace(/.mp3$/, "");
    const mainLabel = imageSound === "none" ? noneLabel : imageSoundLabel;
    const subMenu: ILocalizableMenuItemProps[] = [
        {
            l10nId: "EditTab.Toolbox.DragActivity.None",
            english: "None",
            onClick: () => {
                canvasElement.removeAttribute("data-sound");
                setImageSound("none");
                setMenuOpen(false);
            }
        },
        {
            l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
            english: "Choose...",
            onClick: () => {
                setMenuOpen(false, true);
                updateSoundShowingDialog();
            }
        }
    ];
    menuOptions.push(divider, {
        l10nId: null,
        english: mainLabel,
        subLabelL10nId: "EditTab.Image.PlayWhenTouched",
        // eslint-disable-next-line @typescript-eslint/no-empty-function
        onClick: () => {},
        icon: <VolumeUpIcon css={getMenuIconCss(1, "left:2px;")} />,
        requiresAnySubscription: true,
        subMenu
    });
    if (imageSound !== "none") {
        subMenu.splice(1, 0, {
            l10nId: null,
            english: imageSoundLabel,
            onClick: () => {
                playSound(
                    imageSound,
                    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
                    canvasElement.closest(".bloom-page")!
                );
                setMenuOpen(false);
            },
            icon: <CheckIcon css={getMenuIconCss()} />
        });
    }
}
