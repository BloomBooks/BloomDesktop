import { css } from "@emotion/react";

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
import { LinkIcon } from "./LinkIcon";
import { showCopyrightAndLicenseDialog } from "../editViewFrame";
import {
    doImageCommand,
    getImageUrlFromImageContainer,
    kImageContainerClass,
} from "./bloomImages";
import {
    doVideoCommand,
    findNextVideoContainer,
    findPreviousVideoContainer,
} from "./bloomVideo";
import {
    copyAndPlaySoundAsync,
    makeDuplicateOfDragBubble,
    makeTargetForDraggable,
    playSound,
    showDialogToChooseSoundFileAsync,
} from "../toolbox/games/GameTool";
import { ThemeProvider } from "@mui/material/styles";
import {
    divider,
    ILocalizableMenuItemProps,
    LocalizableMenuItem,
    LocalizableNestedMenuItem,
} from "../../react_components/localizableMenuItem";
import Menu from "@mui/material/Menu";
import { Divider } from "@mui/material";
import { DuplicateIcon } from "./DuplicateIcon";
import {
    CanvasElementManager,
    isDraggable,
    kBackgroundImageClass,
    kDraggableIdAttribute,
    theOneCanvasElementManager,
} from "./CanvasElementManager";
import { copySelection, GetEditor, pasteClipboard } from "./bloomEditing";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { useL10n } from "../../react_components/l10nHooks";
import { CogIcon } from "./CogIcon";
import { getHyperlinkFromClipboard } from "../bloomField/hyperlinks";
import { MissingMetadataIcon } from "./MissingMetadataIcon";
import { FillSpaceIcon } from "./FillSpaceIcon";
import { kBloomDisabledOpacity } from "../../utils/colorUtils";
import { Span } from "../../react_components/l10nComponents";
import AudioRecording from "../toolbox/talkingBook/audioRecording";
import { getAudioSentencesOfVisibleEditables } from "bloom-player";
import { GameType, getGameType } from "../toolbox/games/GameInfo";
import { setGeneratedDraggableId } from "../toolbox/canvas/CanvasElementItem";
import { editLinkGrid } from "./linkGrid";
import { showLinkTargetChooserDialog } from "../../react_components/LinkTargetChooser/LinkTargetChooserDialogLauncher";

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
    const imgContainer =
        props.canvasElement.getElementsByClassName(kImageContainerClass)[0];
    const hasImage = !!imgContainer;
    const hasText =
        props.canvasElement.getElementsByClassName("bloom-editable").length > 0;
    const linkGrid = props.canvasElement.getElementsByClassName(
        "bloom-link-grid",
    )[0] as HTMLElement | undefined;
    const isLinkGrid = !!linkGrid;
    // These names are not quite consistent, but the behaviors we want to control are currently
    // specific to navigation buttons, while the class name is meant to cover buttons in general.
    // Eventually we may need a way to distinguish buttons used for navigation from other buttons.
    const isNavButton = props.canvasElement.classList.contains(
        "bloom-canvas-button",
    );
    const rectangles =
        props.canvasElement.getElementsByClassName("bloom-rectangle");
    // This is only used by the menu option that toggles it. If the menu stayed up, we would need a state
    // and useEffect. But since it closes when we choose an option, we can just get the current value to show
    // in the current menu opening.
    const hasRectangle = rectangles.length > 0;
    const rectangleHasBackground = rectangles[0]?.classList.contains(
        "bloom-theme-background",
    );
    const img = imgContainer?.getElementsByTagName("img")[0];
    //const hasLicenseProblem = hasImage && !img.getAttribute("data-copyright");
    const videoContainer = props.canvasElement.getElementsByClassName(
        "bloom-videoContainer",
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

    const noneLabel = useL10n("None", "EditTab.Toolbox.DragActivity.None", "");
    const aRecordingLabel = useL10n("A Recording", "ARecording", "");
    const chooseBooksLabel = useL10n(
        "Choose books...",
        "EditTab.Toolbox.CanvasTool.LinkGrid.ChooseBooks",
    );

    const currentDraggableTargetId = props.canvasElement?.getAttribute(
        kDraggableIdAttribute,
    );
    const [currentDraggableTarget, setCurrentDraggableTarget] = useState<
        HTMLElement | undefined
    >();
    // After deleting a draggable, we may get rendered again, and page will be null.
    const page = props.canvasElement.closest(".bloom-page") as HTMLElement;
    useEffect(() => {
        if (!currentDraggableTargetId) {
            setCurrentDraggableTarget(undefined);
            return;
        }

        setCurrentDraggableTarget(
            page?.querySelector(
                `[data-target-of="${currentDraggableTargetId}"]`,
            ) as HTMLElement,
        );
        // We need to re-evaluate when changing pages, it's possible the initially selected item
        // on a new page has the same currentDraggableTargetId.
    }, [currentDraggableTargetId]);

    // The audio menu item states the audio will play when the item is touched.
    // That isn't true yet outside of games, so don't show it.
    const activityType = page?.getAttribute("data-activity") ?? "";
    const isInDraggableGame = activityType.startsWith("drag-");
    const canChooseAudioForElement = isInDraggableGame && (hasImage || hasText);

    const [imageSound, setImageSound] = useState("none");
    useEffect(() => {
        setImageSound(props.canvasElement.getAttribute("data-sound") ?? "none");
    }, [props.canvasElement]);
    const isBackgroundImage = props.canvasElement.classList.contains(
        kBackgroundImageClass,
    );
    // We might eventually want a more general class for this, but for now, we want to prevent
    // deleting and duplicating the special sentence object in the order words game, and this
    // class is already in use to indicate it.
    const isSpecialGameElementSelected = props.canvasElement.classList.contains(
        "drag-item-order-sentence",
    );
    const children = props.canvasElement.parentElement?.querySelectorAll(
        ".bloom-canvas-element",
    );
    const canvasHasMultipleElements = (children?.length ?? 0) > 1; // kBackgroundImageClass is also a canvas element
    const backgroundImageText = useL10n(
        "Background Image",
        "EditTab.Image.BackgroundImage",
    );
    const canExpandBackgroundImage =
        theOneCanvasElementManager?.canExpandToFillSpace();

    const canToggleDraggability =
        isInDraggableGame &&
        getGameType(activityType, page) !== GameType.DragSortSentence &&
        // wrong and correct view items cannot be made draggable
        !props.canvasElement.classList.contains("drag-item-wrong") &&
        !props.canvasElement.classList.contains("drag-item-correct") &&
        // Gifs and rectangles cannot be made draggable
        !props.canvasElement.classList.contains("bloom-gif") &&
        !props.canvasElement.querySelector(`.bloom-rectangle`) &&
        !isSpecialGameElementSelected &&
        // Don't let them make the background image draggable
        !isBackgroundImage &&
        // Audio currently cannot be made non-draggable
        !props.canvasElement.querySelector(`[data-icon-type="audio"]`);

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

    let menuOptions: IMenuItemWithSubmenu[] = [];
    if (hasRectangle) {
        menuOptions.splice(0, 0, {
            l10nId: "EditTab.Toolbox.ComicTool.Options.FillBackground",
            english: "Fill Background",
            onClick: () => {
                props.canvasElement
                    .getElementsByClassName("bloom-rectangle")[0]
                    ?.classList.toggle("bloom-theme-background");
            },
            icon: rectangleHasBackground && (
                <CheckIcon css={getMenuIconCss()} />
            ),
        });
    }
    if (hasText && !isInDraggableGame) {
        menuOptions.splice(0, 0, {
            l10nId: "EditTab.Toolbox.ComicTool.Options.AddChildBubble",
            english: "Add Child Bubble",
            onClick: theOneCanvasElementManager?.addChildCanvasElement,
        });
    }
    if (canToggleDraggability) {
        addMenuItemForTogglingDraggability(
            menuOptions,
            props.canvasElement,
            currentDraggableTarget,
            setCurrentDraggableTarget,
        );
    }
    if (currentDraggableTargetId) {
        addMenuItemsForDraggable(
            menuOptions,
            props.canvasElement,
            currentDraggableTargetId,
            currentDraggableTarget,
            setCurrentDraggableTarget,
        );
    }
    if (canChooseAudioForElement) {
        const audioMenuItem = hasText
            ? getAudioMenuItemForTextItem(textHasAudio, setMenuOpen)
            : getAudioMenuItemForImage(imageSound, setImageSound, setMenuOpen);

        menuOptions.push(divider);
        menuOptions.push(audioMenuItem);
    }
    if (hasImage) {
        const canModifyImage = !imgContainer.classList.contains(
            "bloom-unmodifiable-image",
        );
        if (canModifyImage)
            addImageMenuOptions(
                menuOptions,
                props.canvasElement,
                img,
                setMenuOpen,
            );
    }
    if (hasVideo) {
        addVideoMenuItems(menuOptions, videoContainer, setMenuOpen);
    }

    if (isLinkGrid) {
        // For link grids, add edit and delete options in the menu
        menuOptions.push({
            l10nId: "EditTab.Toolbox.CanvasTool.LinkGrid.ChooseBooks",
            english: "Choose books...",
            onClick: () => {
                if (!linkGrid) return;
                editLinkGrid(linkGrid);
            },
            icon: <CogIcon css={getMenuIconCss()} />,
        });
        menuOptions.push({
            l10nId: "Common.Delete",
            english: "Delete",
            onClick: theOneCanvasElementManager?.deleteCurrentCanvasElement,
            icon: <DeleteIcon css={getMenuIconCss()} />,
        });
    }

    menuOptions.push(divider);

    if (!isBackgroundImage && !isSpecialGameElementSelected && !isLinkGrid) {
        menuOptions.push({
            l10nId: "EditTab.Toolbox.ComicTool.Options.Duplicate",
            english: "Duplicate",
            onClick: () => {
                if (!props.canvasElement) return;
                makeDuplicateOfDragBubble();
            },
            icon: <DuplicateIcon css={getMenuIconCss()} />,
        });
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
            ),
        };
        let index = menuOptions.findIndex(
            (option) => option.l10nId === "EditTab.Image.Reset",
        );
        if (index < 0) {
            index = menuOptions.indexOf(divider);
        }
        menuOptions.splice(index, 0, fillItem);

        // we can't delete the placeholder (or if there isn't an img, somehow)
        deleteEnabled = !!(
            img && !img.getAttribute("src")?.startsWith("placeHolder.png")
        );
    } else if (isSpecialGameElementSelected || isLinkGrid) {
        deleteEnabled = false; // don't allow deleting the single drag item in a sentence drag game or link grids
    }

    // last one
    if (!isLinkGrid) {
        menuOptions.push({
            l10nId: "Common.Delete",
            english: "Delete",
            disabled: !deleteEnabled,
            onClick: theOneCanvasElementManager?.deleteCurrentCanvasElement,
            icon: <DeleteIcon css={getMenuIconCss()} />,
        });
    }
    if (isNavButton) {
        menuOptions.splice(0, 0, {
            l10nId: "EditTab.Toolbox.CanvasTool.SetDest",
            english: "Set Destination",
            onClick: () => setLinkDestination(),
            icon: <LinkIcon css={getMenuIconCss()} />,
        });
    }
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
        "bloom-editable bloom-visibility-code-on",
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
            getImageUrlFromImageContainer(imgContainer as HTMLElement),
        );
    };

    // I don't particularly like this, but the logic of when to add items is
    // so convoluted with most things being added at the beginning of the list instead
    // the end, that it is almost impossible to reason about. It would be great to
    // give it a more linear flow, but we're not taking that on just before releasing 6.2a.
    // But this is also future-proof.
    menuOptions = cleanUpDividers(menuOptions);

    const maxMenuWidth = 260;

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
                    {isLinkGrid && (
                        <>
                            <ButtonWithTooltip
                                tipL10nKey="EditTab.ClickToEditBookGrid"
                                icon={CogIcon}
                                relativeSize={0.8}
                                onClick={() => {
                                    if (!linkGrid) return;
                                    editLinkGrid(linkGrid);
                                }}
                            />
                            <span
                                css={css`
                                    color: ${kBloomBlue};
                                    font-size: 10px;
                                    margin-left: 4px;
                                    cursor: pointer;
                                `}
                                onClick={() => {
                                    if (!linkGrid) return;
                                    editLinkGrid(linkGrid);
                                }}
                            >
                                {chooseBooksLabel}
                            </span>
                        </>
                    )}
                    {isNavButton && (
                        <ButtonWithTooltip
                            tipL10nKey="EditTab.Toolbox.CanvasTool.ClickToSetLinkDest"
                            icon={LinkIcon}
                            relativeSize={0.8}
                            onClick={setLinkDestination}
                        />
                    )}
                    {hasImage && (
                        <Fragment>
                            {
                                // Want an attention-grabbing version of set metadata if there is none.)
                                missingMetadata && !isNavButton && (
                                    <ButtonWithTooltip
                                        tipL10nKey="EditTab.Image.EditMetadataOverlay"
                                        icon={MissingMetadataIcon}
                                        onClick={() => runMetadataDialog()}
                                    />
                                )
                            }
                            {
                                // Choose image is only a LIKELY choice if we don't yet have one.
                                // (or if it's a background image...not sure why, except otherwise
                                // the toolbar might not have any icons for a background image.)
                                (isPlaceHolder || isBackgroundImage) && (
                                    <ButtonWithTooltip
                                        tipL10nKey="EditTab.Image.ChooseImage"
                                        icon={SearchIcon}
                                        onClick={(_) => {
                                            if (!props.canvasElement) return;
                                            const imgContainer =
                                                props.canvasElement.getElementsByClassName(
                                                    kImageContainerClass,
                                                )[0] as HTMLElement;
                                            if (!imgContainer) return;
                                            doImageCommand(
                                                imgContainer.getElementsByTagName(
                                                    "img",
                                                )[0] as HTMLImageElement,
                                                "change",
                                            );
                                        }}
                                    />
                                )
                            }
                            {(isPlaceHolder || isBackgroundImage) && (
                                <ButtonWithTooltip
                                    tipL10nKey="EditTab.Image.PasteImage"
                                    icon={PasteIcon}
                                    relativeSize={0.9}
                                    onClick={(_) => {
                                        if (!props.canvasElement) return;
                                        const imgContainer =
                                            props.canvasElement.getElementsByClassName(
                                                kImageContainerClass,
                                            )[0] as HTMLElement;
                                        if (!imgContainer) return;
                                        doImageCommand(
                                            imgContainer.getElementsByTagName(
                                                "img",
                                            )[0] as HTMLImageElement,
                                            "paste",
                                        );
                                    }}
                                ></ButtonWithTooltip>
                            )}
                        </Fragment>
                    )}
                    {editable && !isNavButton && (
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
                    {!hasVideo &&
                        !isBackgroundImage &&
                        !isSpecialGameElementSelected &&
                        !isLinkGrid && (
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
                    {
                        // Not sure of the reasoning here, since we do have a way to 'delete' a background image,
                        // not by removing the canvas element but by setting the image back to a placeholder.
                        // But the mockup in BL-14069 definitely doesn't have it.
                        isBackgroundImage ||
                            isSpecialGameElementSelected ||
                            isLinkGrid || (
                                <ButtonWithTooltip
                                    tipL10nKey="Common.Delete"
                                    icon={DeleteIcon}
                                    onClick={() => {
                                        if (!props.canvasElement) return;
                                        theOneCanvasElementManager?.deleteCurrentCanvasElement();
                                    }}
                                />
                            )
                    }
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

    function getAudioMenuItem(
        english: string,
        subMenu: ILocalizableMenuItemProps[],
    ) {
        return {
            l10nId: null,
            english,
            subLabelL10nId: "EditTab.Image.PlayWhenTouched",
            onClick: () => {},
            icon: <VolumeUpIcon css={getMenuIconCss(1, "left:2px;")} />,
            featureName: "canvas",
            subMenu,
        };
    }

    function getAudioMenuItemForTextItem(
        textHasAudio: boolean,
        setMenuOpen: (open: boolean, launchingDialog?: boolean) => void,
    ) {
        return getAudioMenuItem(textHasAudio ? aRecordingLabel : noneLabel, [
            {
                l10nId: "UseTalkingBookTool",
                english: "Use Talking Book Tool",
                onClick: () => {
                    setMenuOpen(false);
                    AudioRecording.showTalkingBookTool();
                },
            },
        ]);
    }

    function getAudioMenuItemForImage(
        imageSound: string,
        setImageSound: (sound: string) => void,
        setMenuOpen: (open: boolean, launchingDialog?: boolean) => void,
    ) {
        // This is uncomfortably similar to the method by the same name in GameTool.
        // And indeed that method has a case for handling an image sound, which is no longer
        // handled on the toolbox side. But both methods make use of component state in
        // ways that make sharing code difficult.
        const updateSoundShowingDialog = async () => {
            const newSoundId = await showDialogToChooseSoundFileAsync();
            if (!newSoundId) {
                return;
            }

            const page = props.canvasElement.closest(
                ".bloom-page",
            ) as HTMLElement;
            const copyBuiltIn = false; // already copied, and not in our sounds folder
            props.canvasElement.setAttribute("data-sound", newSoundId);
            setImageSound(newSoundId);
            copyAndPlaySoundAsync(newSoundId, page, copyBuiltIn);
        };

        const imageSoundLabel = imageSound.replace(/.mp3$/, "");
        const subMenu: ILocalizableMenuItemProps[] = [
            {
                l10nId: "EditTab.Toolbox.DragActivity.None",
                english: "None",
                onClick: () => {
                    props.canvasElement.removeAttribute("data-sound");
                    setImageSound("none");
                    setMenuOpen(false);
                },
            },
            {
                l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
                english: "Choose...",
                onClick: () => {
                    setMenuOpen(false, true);
                    updateSoundShowingDialog();
                },
            },
            divider,
            {
                l10nId: null,
                english: "",
                subLabelL10nId: "EditTab.Toolbox.DragActivity.ChooseSound.Help",
                subLabel:
                    "You can use elevenlabs.io to create sound effects if your book is non-commercial. Make sure to give credit to “elevenlabs.io”.",
                onClick: () => {},
            },
        ];
        if (imageSound !== "none") {
            subMenu.splice(1, 0, {
                l10nId: null,
                english: imageSoundLabel,
                onClick: () => {
                    playSound(
                        imageSound,
                        props.canvasElement.closest(".bloom-page")!,
                    );
                    setMenuOpen(false);
                },
                icon: <CheckIcon css={getMenuIconCss()} />,
            });
        }
        return getAudioMenuItem(
            imageSound === "none" ? noneLabel : imageSoundLabel,
            subMenu,
        );
    }
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
    canvasElement: HTMLElement,
) {
    const autoHeight = !canvasElement.classList.contains("bloom-noAutoHeight");
    const toggleAutoHeight = () => {
        canvasElement.classList.toggle("bloom-noAutoHeight");
        theOneCanvasElementManager.updateAutoHeight();
        // In most contexts, we would need to do something now to make the control render, so we get
        // an updated value for autoHeight. But the menu is going to be hidden, and showing it again
        // will involve a re-render, and we don't care until then.
    };

    const textMenuItem: ILocalizableMenuItemProps[] = [
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.Format",
            english: "Format",
            onClick: () => GetEditor().runFormatDialog(editable),
            icon: <CogIcon css={getMenuIconCss()} />,
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.CopyText",
            english: "Copy Text",
            onClick: () => copySelection(),
            icon: <CopyIcon css={getMenuIconCss()} />,
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PasteText",
            english: "Paste Text",
            onClick: () => {
                // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
                pasteClipboard(false);
            },
            icon: <PasteIcon css={getMenuIconCss()} />,
        },
    ];
    // Normally text boxes have the auto-height option, but we keep buttons manual.
    // One reason is that we haven't figured out a good automatic approach to adjusting the button
    // height vs adjusting the image size, when both are present. Also, our current auto-height
    // code doesn't handle padding where our canvas-buttons have it.
    if (!canvasElement.classList.contains("bloom-canvas-button")) {
        textMenuItem.push(divider, {
            l10nId: "EditTab.Toolbox.ComicTool.Options.AutoHeight",
            english: "Auto Height",
            // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
            onClick: () => toggleAutoHeight(),
            icon: autoHeight && <CheckIcon css={getMenuIconCss()} />,
        });
    }
    menuOptions.push(...textMenuItem);
}

function addVideoMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    videoContainer: Element,
    setMenuOpen: (open: boolean, launchingDialog?: boolean) => void,
) {
    menuOptions.unshift(
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
            english: "Choose Video from your Computer...",
            onClick: () => {
                doVideoCommand(videoContainer, "choose");
                setMenuOpen(false, true);
            },
            icon: <SearchIcon css={getMenuIconCss()} />,
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.RecordYourself",
            english: "Record yourself...",
            onClick: () => doVideoCommand(videoContainer, "record"),
            icon: <CircleIcon css={getMenuIconCss(0.85)} />,
        },
        divider,
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PlayEarlier",
            english: "Play Earlier",
            onClick: () => {
                doVideoCommand(videoContainer, "playEarlier");
            },
            icon: <ArrowUpwardIcon css={getMenuIconCss()} />,
            disabled: !findPreviousVideoContainer(videoContainer),
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PlayLater",
            english: "Play Later",
            onClick: () => {
                doVideoCommand(videoContainer, "playLater");
            },
            icon: <ArrowDownwardIcon css={getMenuIconCss()} />,
            disabled: !findNextVideoContainer(videoContainer),
        },
        divider,
    );
}

function addImageMenuOptions(
    menuOptions: IMenuItemWithSubmenu[],
    canvasElement: HTMLElement,
    img: HTMLElement,
    setMenuOpen: (open: boolean, launchingDialog?: boolean) => void,
) {
    const imgContainer = canvasElement.getElementsByClassName(
        kImageContainerClass,
    )[0] as HTMLElement;

    const isCropped = !!img?.style.width;

    const runMetadataDialog = () => {
        if (!canvasElement) return;
        if (!imgContainer) return;
        showCopyrightAndLicenseDialog(
            getImageUrlFromImageContainer(imgContainer),
        );
    };

    const imageMenuOptions: IMenuItemWithSubmenu[] = [
        {
            l10nId: "EditTab.Image.ChooseImage",
            english: "Choose image from your computer...",
            onClick: () => {
                doImageCommand(img, "change");
                setMenuOpen(false, true);
            },
            icon: <SearchIcon css={getMenuIconCss()} />,
        },
        {
            l10nId: "EditTab.Image.PasteImage",
            english: "Paste image",
            onClick: () => doImageCommand(img, "paste"),
            icon: <PasteIcon css={getMenuIconCss()} />,
        },
        {
            l10nId: "EditTab.Image.CopyImage",
            english: "Copy image",
            onClick: () => doImageCommand(img, "copy"),
            icon: <CopyIcon css={getMenuIconCss()} />,
        },
        {
            l10nId: "EditTab.Image.EditMetadataOverlay",
            english: "Set Image Information...",
            subLabelL10nId: "EditTab.Image.EditMetadataOverlayMore",
            onClick: runMetadataDialog,
            icon: <CopyrightIcon css={getMenuIconCss()} />,
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
            ),
        },
    ];
    // It would be too confusing and difficult for the element to be both draggable and clickable with different
    //  behavior such that we'd have to distinguish between the two.
    if (!isDraggable(canvasElement)) {
        imageMenuOptions.push({
            l10nId: "EditTab.SetupHyperlink",
            english: "Set Up Hyperlink",
            subLabel: imgContainer.getAttribute("data-href") && (
                <Span
                    // This is tricky and I don't fully understand it myself.
                    // The default behavior seems to be to extend the string beyond the width of
                    // a couple of containers, so it extends all the way to the right edge of the menu
                    // before it wraps. However, this span is nested inside an element that is to the left
                    // of the Subscription icon, so most approaches limit the width to that, leaving more
                    // space on the right than we really want. Providing a min-width actually makes
                    // a very long URL smaller than it otherwise would be, overriding what I think is
                    // the default: the min-width of the longest 'word' (usually the whole URL).
                    // Making it 110% rather than zero makes it use up a bit more of the available space;
                    // I adjusted it so that (a) the margins are about even when there is a long URL, and
                    // (b) by happy coincidence, the URL only fits on the same line as "Currently: "
                    // when it is short enough to stop just before it runs into the Subscription icon.
                    // I think the other parameters don't take effect in a span unless display is set
                    // to block.
                    css={css`
                        min-width: 110%;
                        overflow-wrap: anywhere; /* so it wraps */
                        display: block;
                    `}
                    l10nKey="EditTab.PasteHyperlink.Currently"
                    l10nParam0={imgContainer.getAttribute("data-href") || ""}
                >
                    Currently: %0
                </Span>
            ),
            featureName: "canvas",
            onClick: () => setupLink(canvasElement),

            /*
            Since the clipboard is not readable by us directly, but
            only through an async call to the server, I haven't found
            a way to know if there is a hyperlink on the clipboard. I could
            know if there was one when we last rendered.
            disabled: !haveHyperlinkOnClipboard
            */
        });
        // Enhance: some way to remove a link you don't want anymore. For now, you can paste an empty string.
    }

    menuOptions.unshift(...imageMenuOptions);
}
function setupLink(canvasElement: HTMLElement) {
    const imgContainer = canvasElement.getElementsByClassName(
        kImageContainerClass,
    )[0] as HTMLElement;
    showLinkTargetChooserDialog("", (url) => {
        if (url) {
            imgContainer.setAttribute("data-href", url);
        } else if (imgContainer.hasAttribute("data-href")) {
            imgContainer.removeAttribute("data-href");
        }
    });
}

// applies the modification to all classes of element
function modifyClassNames(
    element: HTMLElement,
    modification: (className: string) => string,
): void {
    const classList = Array.from(element.classList);
    const newClassList = classList
        .map(modification)
        .filter((className) => className !== "");
    element.classList.remove(...classList);
    element.classList.add(...newClassList);
}

// applies the modification to all classes of element and all its descendants
function modifyAllDescendantsClassNames(
    element: HTMLElement,
    modification: (className: string) => string,
): void {
    const descendants = element.querySelectorAll("*");
    descendants.forEach((descendant) => {
        modifyClassNames(descendant as HTMLElement, modification);
    });
}

function addMenuItemForTogglingDraggability(
    menuOptions: IMenuItemWithSubmenu[],
    canvasElement: HTMLElement,
    currentDraggableTarget: HTMLElement | undefined,
    setCurrentDraggableTarget: (target: HTMLElement | undefined) => void,
) {
    const toggleDragability = () => {
        if (isDraggable(canvasElement)) {
            if (currentDraggableTarget) {
                currentDraggableTarget.ownerDocument
                    .getElementById("target-arrow")
                    ?.remove();
                currentDraggableTarget.remove();
                setCurrentDraggableTarget(undefined);
            }
            canvasElement.removeAttribute(kDraggableIdAttribute);
            if (
                canvasElement.getElementsByClassName("bloom-editable").length >
                0
            ) {
                modifyAllDescendantsClassNames(canvasElement, (className) =>
                    className.replace(
                        /GameDrag((?:Small|Medium|Large)(?:Start|Center))-style/,
                        "GameText$1-style",
                    ),
                );
                canvasElement.classList.remove("draggable-text");
            }
        } else {
            setGeneratedDraggableId(canvasElement);
            setCurrentDraggableTarget(makeTargetForDraggable(canvasElement));
            // Draggables cannot have hyperlinks, otherwise Bloom Player will launch the hyperlink when you click on it
            // and you won't be able to drag it.
            const imageContainer = canvasElement.getElementsByClassName(
                kImageContainerClass,
            )[0] as HTMLElement;
            if (imageContainer) {
                imageContainer.removeAttribute("data-href");
            }

            theOneCanvasElementManager.setActiveElement(canvasElement);
            if (
                canvasElement.getElementsByClassName("bloom-editable").length >
                0
            ) {
                modifyAllDescendantsClassNames(canvasElement, (className) =>
                    className.replace(
                        /GameText((?:Small|Medium|Large)(?:Start|Center))-style/,
                        "GameDrag$1-style",
                    ),
                );
                canvasElement.classList.add("draggable-text");
            }
        }
    };
    menuOptions.push(divider, {
        l10nId: "EditTab.Toolbox.DragActivity.Draggability",
        english: "Draggable",
        subLabelL10nId: "EditTab.Toolbox.DragActivity.DraggabilityMore",
        onClick: toggleDragability,
        icon: isDraggable(canvasElement) ? (
            <CheckIcon css={getMenuIconCss()} />
        ) : undefined,
    });
}

function addMenuItemsForDraggable(
    menuOptions: IMenuItemWithSubmenu[],
    canvasElement: HTMLElement,
    currentDraggableTargetId: string,
    currentDraggableTarget: HTMLElement | undefined,
    setCurrentDraggableTarget: (target: HTMLElement | undefined) => void,
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
    menuOptions.push({
        l10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswer",
        english: "Part of the right answer",
        subLabelL10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswerMore.v2",
        onClick: toggleIsPartOfRightAnswer,
        icon: currentDraggableTarget ? (
            <CheckIcon css={getMenuIconCss()} />
        ) : undefined,
    });
}

// Make sure we don't start/end with a divider, and there aren't two in a row.
function cleanUpDividers(menuItems: IMenuItemWithSubmenu[]) {
    let lastDividerIndex = -1;
    const cleanMenuItems = menuItems.filter((option, index) => {
        if (option === divider) {
            if (
                lastDividerIndex === index - 1 ||
                index === menuItems.length - 1
            ) {
                return false;
            } else {
                lastDividerIndex = index;
            }
        }
        return true;
    });
    return cleanMenuItems;
}

function setLinkDestination(): void {
    const activeElement = theOneCanvasElementManager?.getActiveElement();
    if (!activeElement) return;

    const imgContainer = activeElement.getElementsByClassName(
        kImageContainerClass,
    )[0] as HTMLElement;
    if (!imgContainer) return;

    const currentUrl = imgContainer.getAttribute("data-href") || "";
    showLinkTargetChooserDialog(currentUrl, (newUrl) => {
        if (newUrl) {
            imgContainer.setAttribute("data-href", newUrl);
        } else {
            imgContainer.removeAttribute("data-href");
        }
    });
}
