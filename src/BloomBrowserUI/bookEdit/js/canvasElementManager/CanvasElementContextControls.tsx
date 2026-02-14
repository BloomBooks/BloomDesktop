import { css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect, useRef } from "react";
import * as ReactDOM from "react-dom";
import { kBloomBlue, lightTheme } from "../../../bloomMaterialUITheme";
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
import { LinkIcon } from "../LinkIcon";
import { showCopyrightAndLicenseDialog } from "../../editViewFrame";
import {
    doImageCommand,
    getImageUrlFromImageContainer,
    kImageContainerClass,
    isPlaceHolderImage,
} from "../bloomImages";
import {
    doVideoCommand,
    findNextVideoContainer,
    findPreviousVideoContainer,
} from "../bloomVideo";
import {
    copyAndPlaySoundAsync,
    makeDuplicateOfDragBubble,
    makeTargetForDraggable,
    playSound,
    showDialogToChooseSoundFileAsync,
} from "../../toolbox/games/GameTool";
import { ThemeProvider } from "@mui/material/styles";
import {
    divider,
    ILocalizableMenuItemProps,
    LocalizableMenuItem,
    LocalizableNestedMenuItem,
} from "../../../react_components/localizableMenuItem";
import Menu from "@mui/material/Menu";
import { Divider } from "@mui/material";
import { DuplicateIcon } from "../DuplicateIcon";
import { getCanvasElementManager } from "../../toolbox/canvas/canvasElementUtils";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
} from "../../toolbox/canvas/canvasElementConstants";
import {
    isDraggable,
    kDraggableIdAttribute,
} from "../../toolbox/canvas/canvasElementDraggables";
import { copySelection, GetEditor, pasteClipboard } from "../bloomEditing";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { useL10n } from "../../../react_components/l10nHooks";
import { CogIcon } from "../CogIcon";
import { MissingMetadataIcon } from "../MissingMetadataIcon";
import { FillSpaceIcon } from "../FillSpaceIcon";
import { kBloomDisabledOpacity } from "../../../utils/colorUtils";
import AudioRecording from "../../toolbox/talkingBook/audioRecording";
import { getAudioSentencesOfVisibleEditables } from "bloom-player";
import { GameType, getGameType } from "../../toolbox/games/GameInfo";
import { setGeneratedDraggableId } from "../../toolbox/canvas/CanvasElementItem";
import { editLinkGrid } from "../linkGrid";
import { showLinkTargetChooserDialog } from "../../../react_components/LinkTargetChooser/LinkTargetChooserDialogLauncher";
import { CanvasElementType } from "../../toolbox/canvas/canvasElementTypes";
import {
    CanvasElementMenuSection,
    CanvasElementToolbarButton,
    canvasElementDefinitions,
} from "../../toolbox/canvas/canvasElementDefinitions";
import { inferCanvasElementType } from "../../toolbox/canvas/canvasElementTypeInference";

interface IMenuItemWithSubmenu extends ILocalizableMenuItemProps {
    subMenu?: ILocalizableMenuItemProps[];
}

// These names are not quite consistent, but the behaviors we want to control are currently
// specific to navigation buttons, while the class name is meant to cover buttons in general.
// Eventually we may need a way to distinguish buttons used for navigation from other buttons.
const isNavigationButtonType = (
    canvasElementType: CanvasElementType,
): boolean => canvasElementType.startsWith("navigation-");

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

    const imgContainer =
        props.canvasElement.getElementsByClassName(kImageContainerClass)[0];
    const hasImage = !!imgContainer;
    const hasText =
        props.canvasElement.getElementsByClassName("bloom-editable").length > 0;
    const editable = props.canvasElement.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on",
    )[0] as HTMLElement | undefined;
    const langName = editable?.getAttribute("data-languagetipcontent");
    const linkGrid = props.canvasElement.getElementsByClassName(
        "bloom-link-grid",
    )[0] as HTMLElement | undefined;
    const isLinkGrid = !!linkGrid;
    const inferredCanvasElementType = inferCanvasElementType(
        props.canvasElement,
    );
    if (!inferredCanvasElementType) {
        const canvasElementId = props.canvasElement.getAttribute("id");
        const canvasElementClasses = props.canvasElement.getAttribute("class");
        console.warn(
            `inferCanvasElementType() returned undefined for a selected canvas element${canvasElementId ? ` id='${canvasElementId}'` : ""}${canvasElementClasses ? ` (class='${canvasElementClasses}')` : ""}. Falling back to 'none'.`,
        );
    }

    if (
        inferredCanvasElementType &&
        !Object.prototype.hasOwnProperty.call(
            canvasElementDefinitions,
            inferredCanvasElementType,
        )
    ) {
        console.warn(
            `Canvas element type '${inferredCanvasElementType}' is not registered in canvasElementDefinitions. Falling back to 'none'.`,
        );
    }

    // Use the inferred type if it's recognized, otherwise fall back to "none"
    // so that the controls degrade gracefully (e.g. for elements from a newer
    // version of Bloom).
    // Check that the inferred type has a matching entry in canvasElementDefinitions.
    // We use hasOwnProperty to guard against a type string that happens to match
    // an inherited Object property (e.g. "constructor").
    const isKnownType =
        !!inferredCanvasElementType &&
        Object.prototype.hasOwnProperty.call(
            canvasElementDefinitions,
            inferredCanvasElementType,
        );
    const canvasElementType: CanvasElementType = isKnownType
        ? inferredCanvasElementType
        : "none";
    const isNavButton = isNavigationButtonType(canvasElementType);

    const allowedMenuSections = new Set<CanvasElementMenuSection>(
        canvasElementDefinitions[canvasElementType].menuSections,
    );
    const isMenuSectionAllowed = (
        section: CanvasElementMenuSection,
    ): boolean => {
        return allowedMenuSections.has(section);
    };
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
    const isPlaceHolder =
        hasImage && isPlaceHolderImage(img?.getAttribute("src"));
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
    const page = props.canvasElement.closest(
        ".bloom-page",
    ) as HTMLElement | null;
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
    }, [currentDraggableTargetId, page]);

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
        canvasElementManager?.canExpandToFillSpace();

    const showMissingMetadataButton = hasRealImage(img) && missingMetadata;
    const showChooseImageButton = hasImage;
    const showPasteImageButton = hasImage;
    const showFormatButton = !!editable;
    const showChooseVideoButtons = hasVideo;
    const showExpandToFillSpaceButton = isBackgroundImage;

    const canModifyImage =
        !!imgContainer &&
        !imgContainer.classList.contains("bloom-unmodifiable-image") &&
        !!img;

    const allowWholeElementCommandsSection = isMenuSectionAllowed(
        "wholeElementCommands",
    );
    const allowDuplicateMenu =
        allowWholeElementCommandsSection &&
        !isLinkGrid &&
        !isBackgroundImage &&
        !isSpecialGameElementSelected;
    const allowDuplicateToolbar =
        !isLinkGrid && !isBackgroundImage && !isSpecialGameElementSelected;
    const showDeleteMenuItem = allowWholeElementCommandsSection && !isLinkGrid;
    const showDeleteToolbarButton =
        !isLinkGrid && !isSpecialGameElementSelected;

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

    const canToggleDraggability =
        page !== null &&
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

    const runMetadataDialog = () => {
        if (!props.canvasElement) return;
        if (!imgContainer) return;
        showCopyrightAndLicenseDialog(
            getImageUrlFromImageContainer(imgContainer as HTMLElement),
        );
    };

    const urlMenuItems: IMenuItemWithSubmenu[] = [];
    const videoMenuItems: IMenuItemWithSubmenu[] = [];
    const imageMenuItems: IMenuItemWithSubmenu[] = [];
    const audioMenuItems: IMenuItemWithSubmenu[] = [];
    const bubbleMenuItems: IMenuItemWithSubmenu[] = [];
    const textMenuItems: IMenuItemWithSubmenu[] = [];
    const wholeElementCommandsMenuItems: IMenuItemWithSubmenu[] = [];

    let deleteEnabled = true;
    if (isBackgroundImage) {
        // We can't delete the placeholder (or if there isn't an img, somehow)
        deleteEnabled = hasRealImage(img);
    } else if (isSpecialGameElementSelected) {
        // Don't allow deleting the single drag item in a sentence drag game.
        deleteEnabled = false;
    }

    type CanvasElementCommandId = Exclude<CanvasElementToolbarButton, "spacer">;

    const makeMenuItem = (props: {
        l10nId: string;
        english: string;
        onClick: () => void;
        icon: React.ReactNode;
        disabled?: boolean;
        featureName?: string;
    }): IMenuItemWithSubmenu => {
        return {
            l10nId: props.l10nId,
            english: props.english,
            onClick: props.onClick,
            icon: props.icon,
            disabled: props.disabled,
            featureName: props.featureName,
        };
    };

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

    const canvasElementCommands: Record<
        CanvasElementCommandId,
        {
            getToolbarItem: () => IToolbarItem | undefined;
            getMenuItem?: () => IMenuItemWithSubmenu | undefined;
        }
    > = {
        setDestination: {
            getToolbarItem: () => {
                if (!isNavButton) return undefined;
                return makeToolbarButton({
                    key: "setDestination",
                    tipL10nKey: "EditTab.Toolbox.CanvasTool.ClickToSetLinkDest",
                    icon: LinkIcon,
                    relativeSize: 0.8,
                    onClick: () => setLinkDestination(),
                });
            },
            getMenuItem: () => {
                if (!isNavButton) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Toolbox.CanvasTool.SetDest",
                    english: "Set Destination",
                    onClick: () => setLinkDestination(),
                    icon: <LinkIcon css={getMenuIconCss()} />,
                    featureName: "canvas",
                });
            },
        },
        chooseVideo: {
            getToolbarItem: () => {
                if (!showChooseVideoButtons || !videoContainer)
                    return undefined;
                return makeToolbarButton({
                    key: "chooseVideo",
                    tipL10nKey: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
                    icon: SearchIcon,
                    onClick: () => doVideoCommand(videoContainer, "choose"),
                });
            },
            getMenuItem: () => {
                if (!hasVideo) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
                    english: "Choose Video from your Computer...",
                    onClick: () => {
                        setMenuOpen(false, true);
                        doVideoCommand(videoContainer, "choose");
                    },
                    icon: <SearchIcon css={getMenuIconCss()} />,
                });
            },
        },
        recordVideo: {
            getToolbarItem: () => {
                if (!showChooseVideoButtons || !videoContainer)
                    return undefined;
                return makeToolbarButton({
                    key: "recordVideo",
                    tipL10nKey:
                        "EditTab.Toolbox.ComicTool.Options.RecordYourself",
                    icon: CircleIcon,
                    relativeSize: 0.8,
                    onClick: () => doVideoCommand(videoContainer, "record"),
                });
            },
            getMenuItem: () => {
                if (!hasVideo) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Toolbox.ComicTool.Options.RecordYourself",
                    english: "Record yourself...",
                    onClick: () => {
                        setMenuOpen(false, true);
                        doVideoCommand(videoContainer, "record");
                    },
                    icon: <CircleIcon css={getMenuIconCss(0.85)} />,
                });
            },
        },
        chooseImage: {
            getToolbarItem: () => {
                if (!showChooseImageButton || !canModifyImage) return undefined;
                return makeToolbarButton({
                    key: "chooseImage",
                    tipL10nKey: "EditTab.Image.ChooseImage",
                    icon: SearchIcon,
                    onClick: () =>
                        doImageCommand(img as HTMLImageElement, "change"),
                });
            },
            getMenuItem: () => {
                if (!canModifyImage) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Image.ChooseImage",
                    english: "Choose image from your computer...",
                    onClick: () => {
                        doImageCommand(img as HTMLImageElement, "change");
                        setMenuOpen(false, true);
                    },
                    icon: <SearchIcon css={getMenuIconCss()} />,
                });
            },
        },
        pasteImage: {
            getToolbarItem: () => {
                if (!showPasteImageButton || !canModifyImage) return undefined;
                return makeToolbarButton({
                    key: "pasteImage",
                    tipL10nKey: "EditTab.Image.PasteImage",
                    icon: PasteIcon,
                    relativeSize: 0.9,
                    onClick: () =>
                        doImageCommand(img as HTMLImageElement, "paste"),
                });
            },
            getMenuItem: () => {
                if (!canModifyImage) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Image.PasteImage",
                    english: "Paste image",
                    onClick: () =>
                        doImageCommand(img as HTMLImageElement, "paste"),
                    icon: <PasteIcon css={getMenuIconCss()} />,
                });
            },
        },
        missingMetadata: {
            getToolbarItem: () => {
                if (!showMissingMetadataButton) return undefined;
                return makeToolbarButton({
                    key: "missingMetadata",
                    tipL10nKey: "EditTab.Image.EditMetadataOverlay",
                    icon: MissingMetadataIcon,
                    onClick: () => runMetadataDialog(),
                });
            },
            getMenuItem: () => {
                if (!canModifyImage) return undefined;
                const realImagePresent = hasRealImage(img);
                return makeMenuItem({
                    l10nId: "EditTab.Image.EditMetadataOverlay",
                    english: "Set Image Information...",
                    onClick: () => {
                        setMenuOpen(false, true);
                        runMetadataDialog();
                    },
                    disabled: !realImagePresent,
                    icon: <CopyrightIcon css={getMenuIconCss()} />,
                });
            },
        },
        expandToFillSpace: {
            getToolbarItem: () => {
                if (!showExpandToFillSpaceButton) return undefined;
                return makeToolbarButton({
                    key: "expandToFillSpace",
                    tipL10nKey: "EditTab.Toolbox.ComicTool.Options.FillSpace",
                    icon: FillSpaceIcon,
                    disabled: !canExpandBackgroundImage,
                    onClick: () =>
                        canvasElementManager?.expandImageToFillSpace(),
                });
            },
            getMenuItem: () => {
                if (!isBackgroundImage) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Toolbox.ComicTool.Options.FillSpace",
                    english: "Fit Space",
                    onClick: () =>
                        canvasElementManager?.expandImageToFillSpace(),
                    disabled: !canExpandBackgroundImage,
                    icon: (
                        <img
                            src="/bloom/images/fill image black.svg"
                            css={getMenuIconCss(1, "left: -3px;")}
                        />
                    ),
                });
            },
        },
        format: {
            getToolbarItem: () => {
                if (!showFormatButton) return undefined;
                return makeToolbarButton({
                    key: "format",
                    tipL10nKey: "EditTab.Toolbox.ComicTool.Options.Format",
                    icon: CogIcon,
                    relativeSize: 0.8,
                    onClick: () => {
                        if (!editable) return;
                        GetEditor().runFormatDialog(editable);
                    },
                });
            },
        },
        duplicate: {
            getToolbarItem: () => {
                if (!allowDuplicateToolbar) return undefined;
                return makeToolbarButton({
                    key: "duplicate",
                    tipL10nKey: "EditTab.Toolbox.ComicTool.Options.Duplicate",
                    icon: DuplicateIcon,
                    relativeSize: 0.9,
                    onClick: () => {
                        if (!props.canvasElement) return;
                        makeDuplicateOfDragBubble();
                    },
                });
            },
            getMenuItem: () => {
                if (!allowDuplicateMenu) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Toolbox.ComicTool.Options.Duplicate",
                    english: "Duplicate",
                    onClick: () => {
                        if (!props.canvasElement) return;
                        makeDuplicateOfDragBubble();
                    },
                    icon: <DuplicateIcon css={getMenuIconCss()} />,
                });
            },
        },
        delete: {
            getToolbarItem: () => {
                if (!showDeleteToolbarButton) return undefined;
                return makeToolbarButton({
                    key: "delete",
                    tipL10nKey: "Common.Delete",
                    icon: DeleteIcon,
                    disabled: !deleteEnabled,
                    onClick: () =>
                        canvasElementManager?.deleteCurrentCanvasElement(),
                });
            },
            getMenuItem: () => {
                if (!showDeleteMenuItem) return undefined;
                return makeMenuItem({
                    l10nId: "Common.Delete",
                    english: "Delete",
                    disabled: !deleteEnabled,
                    onClick: () =>
                        canvasElementManager?.deleteCurrentCanvasElement?.(),
                    icon: <DeleteIcon css={getMenuIconCss()} />,
                });
            },
        },
        linkGridChooseBooks: {
            getToolbarItem: () => {
                if (!isLinkGrid || !linkGrid) return undefined;
                return {
                    key: "linkGridChooseBooks",
                    node: (
                        <>
                            <ButtonWithTooltip
                                tipL10nKey="EditTab.Toolbox.CanvasTool.LinkGrid.ChooseBooks"
                                icon={CogIcon}
                                relativeSize={0.8}
                                onClick={() => {
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
                                    editLinkGrid(linkGrid);
                                }}
                            >
                                {chooseBooksLabel}
                            </span>
                        </>
                    ),
                };
            },
            getMenuItem: () => {
                if (!isLinkGrid || !linkGrid) return undefined;
                return makeMenuItem({
                    l10nId: "EditTab.Toolbox.CanvasTool.LinkGrid.ChooseBooks",
                    english: "Choose books...",
                    onClick: () => {
                        setMenuOpen(false, true);
                        editLinkGrid(linkGrid);
                    },
                    icon: <CogIcon css={getMenuIconCss()} />,
                });
            },
        },
    };

    if (isMenuSectionAllowed("url")) {
        const setDestMenuItem =
            canvasElementCommands.setDestination.getMenuItem?.();
        if (setDestMenuItem) {
            urlMenuItems.push(setDestMenuItem);
        }
    }

    if (hasVideo) {
        const chooseVideoMenuItem =
            canvasElementCommands.chooseVideo.getMenuItem?.();
        if (chooseVideoMenuItem) {
            videoMenuItems.push(chooseVideoMenuItem);
        }
        const recordVideoMenuItem =
            canvasElementCommands.recordVideo.getMenuItem?.();
        if (recordVideoMenuItem) {
            videoMenuItems.push(recordVideoMenuItem);
        }
        videoMenuItems.push(
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
        );
    }

    if (hasImage && canModifyImage) {
        const chooseImageMenuItem =
            canvasElementCommands.chooseImage.getMenuItem?.();
        if (chooseImageMenuItem) {
            imageMenuItems.push(chooseImageMenuItem);
        }
        const pasteImageMenuItem =
            canvasElementCommands.pasteImage.getMenuItem?.();
        if (pasteImageMenuItem) {
            imageMenuItems.push(pasteImageMenuItem);
        }
        const realImagePresent = hasRealImage(img);
        imageMenuItems.push({
            l10nId: "EditTab.Image.CopyImage",
            english: "Copy image",
            onClick: () => doImageCommand(img as HTMLImageElement, "copy"),
            icon: <CopyIcon css={getMenuIconCss()} />,
            disabled: !realImagePresent,
        });
        const metadataMenuItem =
            canvasElementCommands.missingMetadata.getMenuItem?.();
        if (metadataMenuItem) {
            imageMenuItems.push(metadataMenuItem);
        }

        const isCropped = !!(img as HTMLElement | undefined)?.style?.width;
        imageMenuItems.push({
            l10nId: "EditTab.Image.Reset",
            english: "Reset Image",
            onClick: () => {
                getCanvasElementManager()?.resetCropping();
            },
            disabled: !isCropped,
            icon: (
                <img
                    src="/bloom/images/reset image black.svg"
                    // tweak to align better and make it look the same size as the other icons
                    css={getMenuIconCss(1, "left: -1px; width: 22px;")}
                />
            ),
        });
    }

    const expandToFillSpaceMenuItem =
        canvasElementCommands.expandToFillSpace.getMenuItem?.();
    if (expandToFillSpaceMenuItem) {
        imageMenuItems.push(expandToFillSpaceMenuItem);
    }

    if (canChooseAudioForElement) {
        audioMenuItems.push(
            hasText
                ? getAudioMenuItemForTextItem(textHasAudio, setMenuOpen)
                : getAudioMenuItemForImage(
                      imageSound,
                      setImageSound,
                      setMenuOpen,
                  ),
        );
    }

    if (hasRectangle) {
        textMenuItems.push({
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
    if (isMenuSectionAllowed("bubble") && hasText && !isInDraggableGame) {
        bubbleMenuItems.push({
            l10nId: "EditTab.Toolbox.ComicTool.Options.AddChildBubble",
            english: "Add Child Bubble",
            onClick: () => canvasElementManager?.addChildCanvasElement?.(),
        });
    }
    if (canToggleDraggability) {
        addMenuItemForTogglingDraggability(
            wholeElementCommandsMenuItems,
            props.canvasElement,
            currentDraggableTarget,
            setCurrentDraggableTarget,
        );
    }
    if (currentDraggableTargetId) {
        addMenuItemsForDraggable(
            wholeElementCommandsMenuItems,
            props.canvasElement,
            currentDraggableTargetId,
            currentDraggableTarget,
            setCurrentDraggableTarget,
        );
    }

    const linkGridChooseBooksMenuItem =
        canvasElementCommands.linkGridChooseBooks.getMenuItem?.();
    if (linkGridChooseBooksMenuItem) {
        textMenuItems.push(linkGridChooseBooksMenuItem);
    }

    const duplicateMenuItem = canvasElementCommands.duplicate.getMenuItem?.();
    if (duplicateMenuItem) {
        wholeElementCommandsMenuItems.push(duplicateMenuItem);
    }

    const deleteMenuItem = canvasElementCommands.delete.getMenuItem?.();
    if (deleteMenuItem) {
        wholeElementCommandsMenuItems.push(deleteMenuItem);
    }

    if (editable) {
        addTextMenuItems(textMenuItems, editable, props.canvasElement);
    }

    const orderedMenuSections: Array<
        [CanvasElementMenuSection, IMenuItemWithSubmenu[]]
    > = [
        ["url", urlMenuItems],
        ["video", videoMenuItems],
        ["image", imageMenuItems],
        ["audio", audioMenuItems],
        ["bubble", bubbleMenuItems],
        ["text", textMenuItems],
        ["wholeElementCommands", wholeElementCommandsMenuItems],
    ];
    const menuOptions = joinMenuSectionsWithSingleDividers(
        orderedMenuSections
            .filter(([section, items]) => {
                if (items.length === 0) {
                    return false;
                }
                return isMenuSectionAllowed(section);
            })
            .map((entry) => entry[1]),
    );
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

    const getToolbarItemForButton = (
        button: CanvasElementToolbarButton,
        index: number,
    ): IToolbarItem | undefined => {
        if (button === "spacer") {
            return getSpacerToolbarItem(index);
        }
        const command = canvasElementCommands[button as CanvasElementCommandId];
        return command.getToolbarItem();
    };

    const toolbarItems = normalizeToolbarItems(
        canvasElementDefinitions[canvasElementType].toolbarButtons
            .map((button, index) => getToolbarItemForButton(button, index))
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
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            canvasElementManager.updateAutoHeight();
        }
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
    if (!canvasElement.classList.contains(kBloomButtonClass)) {
        textMenuItem.push({
            l10nId: "EditTab.Toolbox.ComicTool.Options.AutoHeight",
            english: "Auto Height",
            // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
            onClick: () => toggleAutoHeight(),
            icon: autoHeight && <CheckIcon css={getMenuIconCss()} />,
        });
    }
    menuOptions.push(...textMenuItem);
}

function hasRealImage(img) {
    return (
        img &&
        !isPlaceHolderImage(img.getAttribute("src")) &&
        !img.classList.contains("bloom-imageLoadError") &&
        img.parentElement &&
        !img.parentElement.classList.contains("bloom-imageLoadError")
    );
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

            const canvasElementManager = getCanvasElementManager();
            if (canvasElementManager) {
                canvasElementManager.setActiveElement(canvasElement);
            }
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
    const visibilityCss = isDraggable(canvasElement)
        ? ""
        : "visibility: hidden;";
    menuOptions.push({
        l10nId: "EditTab.Toolbox.DragActivity.Draggability",
        english: "Draggable",
        subLabelL10nId: "EditTab.Toolbox.DragActivity.DraggabilityMore",
        onClick: toggleDragability,
        icon: <CheckIcon css={getMenuIconCss(1, visibilityCss)} />,
    });
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
    const visibilityCss = currentDraggableTarget ? "" : "visibility: hidden;";
    menuOptions.push({
        l10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswer",
        english: "Part of the right answer",
        subLabelL10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswerMore.v2",
        onClick: toggleIsPartOfRightAnswer,
        icon: <CheckIcon css={getMenuIconCss(1, visibilityCss)} />,
    });
}

// Make sure we don't start/end with a divider, and there aren't two in a row.
function setLinkDestination(): void {
    const activeElement = getCanvasElementManager()?.getActiveElement();
    if (!activeElement) return;

    // Note that here we place data-href on the canvas element itself.
    // This is different from how we do it for simple images (not in nav buttons),
    // where we put data-href on the image container.
    // We didn't want to change the existing behavior for simple images,
    // so as not to break existing books in 6.2.
    const currentUrl = activeElement.getAttribute("data-href") || "";
    showLinkTargetChooserDialog(currentUrl, (newUrl) => {
        if (newUrl) {
            activeElement.setAttribute("data-href", newUrl);
        } else {
            activeElement.removeAttribute("data-href");
        }
    });
}
