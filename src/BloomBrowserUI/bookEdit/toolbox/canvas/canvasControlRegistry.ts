import * as React from "react";
import { default as ArrowDownwardIcon } from "@mui/icons-material/ArrowDownward";
import { default as ArrowUpwardIcon } from "@mui/icons-material/ArrowUpward";
import { default as CheckIcon } from "@mui/icons-material/Check";
import { default as CircleIcon } from "@mui/icons-material/Circle";
import { default as CopyIcon } from "@mui/icons-material/ContentCopy";
import { default as PasteIcon } from "@mui/icons-material/ContentPaste";
import { default as CopyrightIcon } from "@mui/icons-material/Copyright";
import { default as DeleteIcon } from "@mui/icons-material/DeleteOutline";
import { default as SearchIcon } from "@mui/icons-material/Search";
import { default as VolumeUpIcon } from "@mui/icons-material/VolumeUp";
import { showCopyrightAndLicenseDialog } from "../../editViewFrame";
import {
    doImageCommand,
    getImageUrlFromImageContainer,
    kImageContainerClass,
} from "../../js/bloomImages";
import { doVideoCommand } from "../../js/bloomVideo";
import {
    copySelection,
    GetEditor,
    pasteClipboard,
} from "../../js/bloomEditing";
import { CogIcon } from "../../js/CogIcon";
import { DuplicateIcon } from "../../js/DuplicateIcon";
import { FillSpaceIcon } from "../../js/FillSpaceIcon";
import { LinkIcon } from "../../js/LinkIcon";
import { MissingMetadataIcon } from "../../js/MissingMetadataIcon";
import { editLinkGrid } from "../../js/linkGrid";
import {
    copyAndPlaySoundAsync,
    makeDuplicateOfDragBubble,
    makeTargetForDraggable,
    playSound,
    showDialogToChooseSoundFileAsync,
} from "../games/GameTool";
import AudioRecording from "../talkingBook/audioRecording";
import { showLinkTargetChooserDialog } from "../../../react_components/LinkTargetChooser/LinkTargetChooserDialogLauncher";
import { kBloomBlue } from "../../../bloomMaterialUITheme";
import {
    IControlContext,
    IControlDefinition,
    ICommandControlDefinition,
    IControlRuntime,
    IControlSection,
    IControlMenuCommandRow,
    ICanvasToolsPanelState,
    SectionId,
    TopLevelControlId,
} from "./canvasControlTypes";
import { getCanvasElementManager } from "./canvasElementUtils";
import { isDraggable, kDraggableIdAttribute } from "./canvasElementDraggables";
import { setGeneratedDraggableId } from "./CanvasElementItem";

const getImageContainer = (ctx: IControlContext): HTMLElement | undefined => {
    return ctx.canvasElement.getElementsByClassName(kImageContainerClass)[0] as
        | HTMLElement
        | undefined;
};

const getImage = (ctx: IControlContext): HTMLImageElement | undefined => {
    return getImageContainer(ctx)?.getElementsByTagName("img")[0];
};

const getVideoContainer = (ctx: IControlContext): HTMLElement | undefined => {
    return ctx.canvasElement.getElementsByClassName(
        "bloom-videoContainer",
    )[0] as HTMLElement | undefined;
};

const getEditable = (ctx: IControlContext): HTMLElement | undefined => {
    return ctx.canvasElement.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on",
    )[0] as HTMLElement | undefined;
};

const placeholderPanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (_props) => {
    return null;
};

const modifyClassNames = (
    element: HTMLElement,
    modification: (className: string) => string,
): void => {
    const classList = Array.from(element.classList);
    const newClassList = classList
        .map(modification)
        .filter((className) => className !== "");
    element.classList.remove(...classList);
    element.classList.add(...newClassList);
};

const modifyAllDescendantsClassNames = (
    element: HTMLElement,
    modification: (className: string) => string,
): void => {
    const descendants = element.querySelectorAll("*");
    descendants.forEach((descendant) => {
        modifyClassNames(descendant as HTMLElement, modification);
    });
};

const getCurrentDraggableTarget = (
    ctx: IControlContext,
): HTMLElement | undefined => {
    const draggableId = ctx.canvasElement.getAttribute(kDraggableIdAttribute);
    if (!draggableId || !ctx.page) {
        return undefined;
    }

    return ctx.page.querySelector(`[data-target-of="${draggableId}"]`) as
        | HTMLElement
        | undefined;
};

const toggleDraggability = (ctx: IControlContext): void => {
    const currentDraggableTarget = getCurrentDraggableTarget(ctx);

    if (isDraggable(ctx.canvasElement)) {
        if (currentDraggableTarget) {
            currentDraggableTarget.ownerDocument
                .getElementById("target-arrow")
                ?.remove();
            currentDraggableTarget.remove();
        }
        ctx.canvasElement.removeAttribute(kDraggableIdAttribute);
        if (
            ctx.canvasElement.getElementsByClassName("bloom-editable").length >
            0
        ) {
            modifyAllDescendantsClassNames(ctx.canvasElement, (className) =>
                className.replace(
                    /GameDrag((?:Small|Medium|Large)(?:Start|Center))-style/,
                    "GameText$1-style",
                ),
            );
            ctx.canvasElement.classList.remove("draggable-text");
        }
        return;
    }

    setGeneratedDraggableId(ctx.canvasElement);
    makeTargetForDraggable(ctx.canvasElement);
    const imageContainer = ctx.canvasElement.getElementsByClassName(
        kImageContainerClass,
    )[0] as HTMLElement | undefined;
    if (imageContainer) {
        imageContainer.removeAttribute("data-href");
    }

    getCanvasElementManager()?.setActiveElement(ctx.canvasElement);
    if (ctx.canvasElement.getElementsByClassName("bloom-editable").length > 0) {
        modifyAllDescendantsClassNames(ctx.canvasElement, (className) =>
            className.replace(
                /GameText((?:Small|Medium|Large)(?:Start|Center))-style/,
                "GameDrag$1-style",
            ),
        );
        ctx.canvasElement.classList.add("draggable-text");
    }
};

const togglePartOfRightAnswer = (ctx: IControlContext): void => {
    const draggableId = ctx.canvasElement.getAttribute(kDraggableIdAttribute);
    if (!draggableId) {
        return;
    }

    const currentDraggableTarget = getCurrentDraggableTarget(ctx);
    if (currentDraggableTarget) {
        currentDraggableTarget.ownerDocument
            .getElementById("target-arrow")
            ?.remove();
        currentDraggableTarget.remove();
        return;
    }

    makeTargetForDraggable(ctx.canvasElement);
};

const makeChooseAudioMenuItemForText = (
    ctx: IControlContext,
    runtime: IControlRuntime,
): IControlMenuCommandRow => {
    return {
        id: "chooseAudio",
        l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
        englishLabel: ctx.textHasAudio ? "A Recording" : "None",
        subLabelL10nId: "EditTab.Image.PlayWhenTouched",
        featureName: "canvas",
        icon: React.createElement(VolumeUpIcon, null),
        onSelect: async () => {},
        subMenuItems: [
            {
                id: "useTalkingBookTool",
                l10nId: "UseTalkingBookTool",
                englishLabel: "Use Talking Book Tool",
                featureName: "canvas",
                onSelect: async () => {
                    runtime.closeMenu(false);
                    AudioRecording.showTalkingBookTool();
                },
            },
        ],
    };
};

const makeChooseAudioMenuItemForImage = (
    ctx: IControlContext,
    runtime: IControlRuntime,
): IControlMenuCommandRow => {
    const currentSoundId =
        ctx.canvasElement.getAttribute("data-sound") ?? "none";
    const imageSoundLabel =
        ctx.currentImageSoundLabel ?? currentSoundId.replace(/\.mp3$/, "");

    return {
        id: "chooseAudio",
        l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
        englishLabel: imageSoundLabel === "none" ? "None" : imageSoundLabel,
        subLabelL10nId: "EditTab.Image.PlayWhenTouched",
        featureName: "canvas",
        icon: React.createElement(VolumeUpIcon, null),
        onSelect: async () => {},
        subMenuItems: [
            {
                id: "removeAudio",
                l10nId: "EditTab.Toolbox.DragActivity.None",
                englishLabel: "None",
                featureName: "canvas",
                onSelect: async () => {
                    ctx.canvasElement.removeAttribute("data-sound");
                    runtime.closeMenu(false);
                },
            },
            {
                id: "playCurrentAudio",
                l10nId: "ARecording",
                englishLabel: imageSoundLabel,
                featureName: "canvas",
                availability: {
                    visible: (itemCtx) => itemCtx.hasCurrentImageSound,
                },
                onSelect: async () => {
                    if (ctx.page && currentSoundId !== "none") {
                        playSound(currentSoundId, ctx.page);
                    }
                    runtime.closeMenu(false);
                },
            },
            {
                id: "chooseAudio",
                l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
                englishLabel: "Choose...",
                featureName: "canvas",
                helpRowL10nId: "EditTab.Toolbox.DragActivity.ChooseSound.Help",
                helpRowEnglish:
                    'You can use elevenlabs.io to create sound effects if your book is non-commercial. Make sure to give credit to "elevenlabs.io".',
                helpRowSeparatorAbove: true,
                onSelect: async () => {
                    runtime.closeMenu(true);
                    const newSoundId = await showDialogToChooseSoundFileAsync();
                    if (!newSoundId || !ctx.page) {
                        return;
                    }

                    ctx.canvasElement.setAttribute("data-sound", newSoundId);
                    copyAndPlaySoundAsync(newSoundId, ctx.page, false);
                },
            },
        ],
    };
};

export const controlRegistry: Record<TopLevelControlId, IControlDefinition> = {
    chooseImage: {
        kind: "command",
        id: "chooseImage",
        l10nId: "EditTab.Image.ChooseImage",
        englishLabel: "Choose image from your computer...",
        icon: SearchIcon,
        action: async (ctx, runtime) => {
            const img = getImage(ctx);
            if (!img) {
                return;
            }

            runtime.closeMenu(true);
            doImageCommand(img, "change");
        },
    },
    pasteImage: {
        kind: "command",
        id: "pasteImage",
        l10nId: "EditTab.Image.PasteImage",
        englishLabel: "Paste image",
        icon: PasteIcon,
        action: async (ctx) => {
            const img = getImage(ctx);
            if (!img) {
                return;
            }

            doImageCommand(img, "paste");
        },
    },
    copyImage: {
        kind: "command",
        id: "copyImage",
        l10nId: "EditTab.Image.CopyImage",
        englishLabel: "Copy image",
        icon: CopyIcon,
        action: async (ctx) => {
            const img = getImage(ctx);
            if (!img) {
                return;
            }

            doImageCommand(img, "copy");
        },
    },
    missingMetadata: {
        kind: "command",
        id: "missingMetadata",
        l10nId: "EditTab.Image.EditMetadataOverlay",
        englishLabel: "Set Image Information...",
        helpRowL10nId: "EditTab.Image.EditMetadataOverlay.MenuHelp",
        icon: MissingMetadataIcon,
        menu: {
            icon: React.createElement(CopyrightIcon, null),
            subLabelL10nId: "EditTab.Image.EditMetadataOverlayMore",
        },
        action: async (ctx, runtime) => {
            const imageContainer = getImageContainer(ctx);
            if (!imageContainer) {
                return;
            }

            runtime.closeMenu(true);
            showCopyrightAndLicenseDialog(
                getImageUrlFromImageContainer(imageContainer),
            );
        },
    },
    resetImage: {
        kind: "command",
        id: "resetImage",
        l10nId: "EditTab.Image.Reset",
        englishLabel: "Reset Image",
        icon: React.createElement("img", {
            src: "/bloom/images/reset image black.svg",
            alt: "",
            className: "canvas-context-menu-monochrome-icon",
        }),
        action: async () => {
            getCanvasElementManager()?.resetCropping();
        },
    },
    expandToFillSpace: {
        kind: "command",
        id: "expandToFillSpace",
        l10nId: "EditTab.Toolbox.ComicTool.Options.FillSpace",
        englishLabel: "Fit Space",
        icon: FillSpaceIcon,
        menu: {
            icon: React.createElement("img", {
                src: "/bloom/images/fill image black.svg",
                alt: "",
                className: "canvas-context-menu-monochrome-icon",
            }),
        },
        action: async () => {
            getCanvasElementManager()?.expandImageToFillSpace();
        },
    },
    imageFillMode: {
        kind: "panel",
        id: "imageFillMode",
        l10nId: "EditTab.Toolbox.CanvasTool.ImageFit",
        englishLabel: "Image Fit",
        canvasToolsControl: placeholderPanelControl,
    },
    chooseVideo: {
        kind: "command",
        id: "chooseVideo",
        l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
        englishLabel: "Choose Video from your Computer...",
        icon: SearchIcon,
        action: async (ctx, runtime) => {
            const videoContainer = getVideoContainer(ctx);
            if (!videoContainer) {
                return;
            }

            runtime.closeMenu(true);
            doVideoCommand(videoContainer, "choose");
        },
    },
    recordVideo: {
        kind: "command",
        id: "recordVideo",
        l10nId: "EditTab.Toolbox.ComicTool.Options.RecordYourself",
        englishLabel: "Record yourself...",
        icon: CircleIcon,
        action: async (ctx, runtime) => {
            const videoContainer = getVideoContainer(ctx);
            if (!videoContainer) {
                return;
            }

            runtime.closeMenu(true);
            doVideoCommand(videoContainer, "record");
        },
    },
    playVideoEarlier: {
        kind: "command",
        id: "playVideoEarlier",
        l10nId: "EditTab.Toolbox.ComicTool.Options.PlayEarlier",
        englishLabel: "Play Earlier",
        icon: ArrowUpwardIcon,
        action: async (ctx) => {
            const videoContainer = getVideoContainer(ctx);
            if (!videoContainer) {
                return;
            }

            doVideoCommand(videoContainer, "playEarlier");
        },
    },
    playVideoLater: {
        kind: "command",
        id: "playVideoLater",
        l10nId: "EditTab.Toolbox.ComicTool.Options.PlayLater",
        englishLabel: "Play Later",
        icon: ArrowDownwardIcon,
        action: async (ctx) => {
            const videoContainer = getVideoContainer(ctx);
            if (!videoContainer) {
                return;
            }

            doVideoCommand(videoContainer, "playLater");
        },
    },
    format: {
        kind: "command",
        id: "format",
        l10nId: "EditTab.Toolbox.ComicTool.Options.Format",
        englishLabel: "Format",
        icon: CogIcon,
        toolbar: {
            relativeSize: 0.8,
        },
        action: async (ctx) => {
            const editable = getEditable(ctx);
            if (!editable) {
                return;
            }

            GetEditor().runFormatDialog(editable);
        },
    },
    copyText: {
        kind: "command",
        id: "copyText",
        l10nId: "EditTab.Toolbox.ComicTool.Options.CopyText",
        englishLabel: "Copy Text",
        icon: CopyIcon,
        action: async () => {
            copySelection();
        },
    },
    pasteText: {
        kind: "command",
        id: "pasteText",
        l10nId: "EditTab.Toolbox.ComicTool.Options.PasteText",
        englishLabel: "Paste Text",
        icon: PasteIcon,
        action: async () => {
            pasteClipboard(false);
        },
    },
    autoHeight: {
        kind: "command",
        id: "autoHeight",
        l10nId: "EditTab.Toolbox.ComicTool.Options.AutoHeight",
        englishLabel: "Auto Height",
        icon: CheckIcon,
        menu: {
            buildMenuItem: (ctx, runtime) => ({
                id: "autoHeight",
                l10nId: "EditTab.Toolbox.ComicTool.Options.AutoHeight",
                englishLabel: "Auto Height",
                icon: React.createElement(CheckIcon, {
                    style: {
                        visibility: ctx.canvasElement.classList.contains(
                            "bloom-noAutoHeight",
                        )
                            ? "hidden"
                            : "visible",
                    },
                }),
                onSelect: async (rowCtx) => {
                    await (
                        controlRegistry.autoHeight as ICommandControlDefinition
                    ).action(rowCtx, runtime);
                },
            }),
        },
        action: async (ctx) => {
            ctx.canvasElement.classList.toggle("bloom-noAutoHeight");
            getCanvasElementManager()?.updateAutoHeight();
        },
    },
    fillBackground: {
        kind: "command",
        id: "fillBackground",
        l10nId: "EditTab.Toolbox.ComicTool.Options.FillBackground",
        englishLabel: "Fill Background",
        icon: CheckIcon,
        menu: {
            buildMenuItem: (ctx, runtime) => ({
                id: "fillBackground",
                l10nId: "EditTab.Toolbox.ComicTool.Options.FillBackground",
                englishLabel: "Fill Background",
                icon: ctx.rectangleHasBackground
                    ? React.createElement(CheckIcon, null)
                    : undefined,
                onSelect: async (rowCtx) => {
                    await (
                        controlRegistry.fillBackground as ICommandControlDefinition
                    ).action(rowCtx, runtime);
                },
            }),
        },
        action: async (ctx) => {
            const rectangle = ctx.canvasElement.getElementsByClassName(
                "bloom-rectangle",
            )[0] as HTMLElement | undefined;
            rectangle?.classList.toggle("bloom-theme-background");
        },
    },
    addChildBubble: {
        kind: "command",
        id: "addChildBubble",
        l10nId: "EditTab.Toolbox.ComicTool.Options.AddChildBubble",
        englishLabel: "Add Child Bubble",
        action: async () => {
            getCanvasElementManager()?.addChildCanvasElement?.();
        },
    },
    bubbleStyle: {
        kind: "panel",
        id: "bubbleStyle",
        l10nId: "EditTab.Toolbox.ComicTool.Options.Style",
        englishLabel: "Style",
        canvasToolsControl: placeholderPanelControl,
    },
    showTail: {
        kind: "panel",
        id: "showTail",
        l10nId: "EditTab.Toolbox.ComicTool.Options.ShowTail",
        englishLabel: "Show Tail",
        canvasToolsControl: placeholderPanelControl,
    },
    roundedCorners: {
        kind: "panel",
        id: "roundedCorners",
        l10nId: "EditTab.Toolbox.ComicTool.Options.RoundedCorners",
        englishLabel: "Rounded Corners",
        canvasToolsControl: placeholderPanelControl,
    },
    textColor: {
        kind: "panel",
        id: "textColor",
        l10nId: "EditTab.Toolbox.ComicTool.Options.TextColor",
        englishLabel: "Text Color",
        canvasToolsControl: placeholderPanelControl,
    },
    backgroundColor: {
        kind: "panel",
        id: "backgroundColor",
        l10nId: "EditTab.Toolbox.ComicTool.Options.BackgroundColor",
        englishLabel: "Background Color",
        canvasToolsControl: placeholderPanelControl,
    },
    outlineColor: {
        kind: "panel",
        id: "outlineColor",
        l10nId: "EditTab.Toolbox.ComicTool.Options.OutlineColor",
        englishLabel: "Outline Color",
        canvasToolsControl: placeholderPanelControl,
    },
    setDestination: {
        kind: "command",
        id: "setDestination",
        featureName: "canvas",
        l10nId: "EditTab.Toolbox.CanvasTool.SetDest",
        englishLabel: "Set Destination",
        icon: LinkIcon,
        toolbar: {
            relativeSize: 0.8,
        },
        action: async (ctx, runtime) => {
            runtime.closeMenu(true);

            const currentUrl =
                ctx.canvasElement.getAttribute("data-href") ?? "";
            showLinkTargetChooserDialog(currentUrl, (newUrl) => {
                if (newUrl) {
                    ctx.canvasElement.setAttribute("data-href", newUrl);
                } else {
                    ctx.canvasElement.removeAttribute("data-href");
                }
            });
        },
    },
    linkGridChooseBooks: {
        kind: "command",
        id: "linkGridChooseBooks",
        l10nId: "EditTab.Toolbox.CanvasTool.LinkGrid.ChooseBooks",
        englishLabel: "Choose books...",
        icon: CogIcon,
        toolbar: {
            render: (ctx, _runtime) => {
                const linkGrid = ctx.canvasElement.getElementsByClassName(
                    "bloom-link-grid",
                )[0] as HTMLElement | undefined;
                if (!linkGrid) {
                    return null;
                }

                return React.createElement(
                    React.Fragment,
                    null,
                    React.createElement(
                        "button",
                        {
                            style: {
                                borderColor: "transparent",
                                backgroundColor: "transparent",
                                verticalAlign: "middle",
                                width: "22px",
                            },
                            onClick: () => {
                                editLinkGrid(linkGrid);
                            },
                        },
                        React.createElement(CogIcon, {
                            color: "primary",
                            style: {
                                fontSize: "1.04rem",
                            },
                        }),
                    ),
                    React.createElement(
                        "span",
                        {
                            style: {
                                color: kBloomBlue,
                                fontSize: "10px",
                                marginLeft: "4px",
                                cursor: "pointer",
                            },
                            onClick: () => {
                                editLinkGrid(linkGrid);
                            },
                        },
                        "Choose books...",
                    ),
                );
            },
        },
        action: async (ctx, runtime) => {
            const linkGrid = ctx.canvasElement.getElementsByClassName(
                "bloom-link-grid",
            )[0] as HTMLElement | undefined;
            if (!linkGrid) {
                return;
            }

            runtime.closeMenu(true);
            editLinkGrid(linkGrid);
        },
    },
    duplicate: {
        kind: "command",
        id: "duplicate",
        l10nId: "EditTab.Toolbox.ComicTool.Options.Duplicate",
        englishLabel: "Duplicate",
        icon: DuplicateIcon,
        action: async () => {
            makeDuplicateOfDragBubble();
        },
    },
    delete: {
        kind: "command",
        id: "delete",
        l10nId: "Common.Delete",
        englishLabel: "Delete",
        icon: DeleteIcon,
        action: async () => {
            getCanvasElementManager()?.deleteCurrentCanvasElement?.();
        },
    },
    toggleDraggable: {
        kind: "command",
        id: "toggleDraggable",
        l10nId: "EditTab.Toolbox.DragActivity.Draggability",
        englishLabel: "Draggable",
        icon: CheckIcon,
        menu: {
            buildMenuItem: (ctx, runtime) => ({
                id: "toggleDraggable",
                l10nId: "EditTab.Toolbox.DragActivity.Draggability",
                englishLabel: "Draggable",
                subLabelL10nId: "EditTab.Toolbox.DragActivity.DraggabilityMore",
                icon: React.createElement(CheckIcon, {
                    style: {
                        visibility: isDraggable(ctx.canvasElement)
                            ? "visible"
                            : "hidden",
                    },
                }),
                onSelect: async (rowCtx) => {
                    await (
                        controlRegistry.toggleDraggable as ICommandControlDefinition
                    ).action(rowCtx, runtime);
                },
            }),
        },
        action: async (ctx) => {
            toggleDraggability(ctx);
        },
    },
    togglePartOfRightAnswer: {
        kind: "command",
        id: "togglePartOfRightAnswer",
        l10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswer",
        englishLabel: "Part of the right answer",
        icon: CheckIcon,
        menu: {
            buildMenuItem: (ctx, runtime) => ({
                id: "togglePartOfRightAnswer",
                l10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswer",
                englishLabel: "Part of the right answer",
                subLabelL10nId:
                    "EditTab.Toolbox.DragActivity.PartOfRightAnswerMore.v2",
                icon: React.createElement(CheckIcon, {
                    style: {
                        visibility: ctx.hasDraggableTarget
                            ? "visible"
                            : "hidden",
                    },
                }),
                onSelect: async (rowCtx) => {
                    await (
                        controlRegistry.togglePartOfRightAnswer as ICommandControlDefinition
                    ).action(rowCtx, runtime);
                },
            }),
        },
        action: async (ctx) => {
            togglePartOfRightAnswer(ctx);
        },
    },
    chooseAudio: {
        kind: "command",
        id: "chooseAudio",
        featureName: "canvas",
        l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
        englishLabel: "Choose...",
        icon: VolumeUpIcon,
        action: async () => {},
        menu: {
            buildMenuItem: (ctx, runtime) => {
                if (ctx.hasText) {
                    return makeChooseAudioMenuItemForText(ctx, runtime);
                }
                return makeChooseAudioMenuItemForImage(ctx, runtime);
            },
        },
    },
};

export const controlSections: Record<SectionId, IControlSection> = {
    image: {
        id: "image",
        controlsBySurface: {
            menu: [
                "missingMetadata",
                "chooseImage",
                "pasteImage",
                "copyImage",
                "resetImage",
                "expandToFillSpace",
            ],
        },
    },
    imagePanel: {
        id: "imagePanel",
        controlsBySurface: {
            toolPanel: ["imageFillMode"],
        },
    },
    video: {
        id: "video",
        controlsBySurface: {
            menu: [
                "chooseVideo",
                "recordVideo",
                "playVideoEarlier",
                "playVideoLater",
            ],
        },
    },
    audio: {
        id: "audio",
        controlsBySurface: {
            menu: ["chooseAudio"],
        },
    },
    linkGrid: {
        id: "linkGrid",
        controlsBySurface: {
            menu: ["linkGridChooseBooks"],
        },
    },
    url: {
        id: "url",
        controlsBySurface: {
            menu: ["setDestination"],
        },
    },
    bubble: {
        id: "bubble",
        controlsBySurface: {
            menu: ["addChildBubble"],
            toolPanel: ["bubbleStyle", "showTail", "roundedCorners"],
        },
    },
    outline: {
        id: "outline",
        controlsBySurface: {
            toolPanel: ["outlineColor"],
        },
    },
    text: {
        id: "text",
        controlsBySurface: {
            menu: [
                "format",
                "copyText",
                "pasteText",
                "autoHeight",
                "fillBackground",
            ],
            toolPanel: ["textColor", "backgroundColor"],
        },
    },
    wholeElement: {
        id: "wholeElement",
        controlsBySurface: {
            menu: [
                "duplicate",
                "delete",
                "toggleDraggable",
                "togglePartOfRightAnswer",
            ],
        },
    },
};
