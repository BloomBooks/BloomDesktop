// Canvas control registry and section map.
//
// This module owns both the declarative registry tables and the concrete
// implementation code behind them.
//
// It defines:
// - `controlRegistry`: each top-level control id mapped to concrete behavior
//   (command actions, menu metadata, toolbar hints, panel component mapping,
//   and helper functions used to carry out those commands).
// - `controlSections`: declarative section groupings used by menu and tool panel
//   surfaces.
//
// How the declarative system composes:
// - `canvasElementControlRegistry.ts` picks section/order for each canvas element type.
// - `canvasControlResolution.ts` resolves those declarations into renderable rows/buttons.
// - `canvasControlAvailabilityRules.ts` + per-element rules decide visibility/enabled state.
// - `canvasPanelControls.tsx` supplies panel UI components referenced here.
//
// Note on sync vs async callbacks:
// - Keep handlers synchronous when they only do immediate DOM/manager work
//   (examples: `toggleDraggable`, `copyText`, `duplicate`, `setDestination`).
// - Use async only when we must await asynchronous APIs
//   (example: `chooseAudio` submenu "Choose..." awaits `showDialogToChooseSoundFileAsync`).
// - Ordinary leaf menu commands close centrally in `CanvasElementContextControls.tsx`.
//   Explicit `runtime.closeMenu(...)` calls here are reserved for cases that
//   need dialog-aware focus handling or submenu-specific menu behavior.
//
// UI invocation sites handle both forms through a shared safe runner so promise
// rejections are not dropped when handlers are called from click events.

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
import { showCopyrightAndLicenseDialog } from "../../workspaceRoot";
import {
    doImageCommand,
    getImageFromCanvasElement,
    getImageFromContainer,
    getImageTransparencyMode,
    getImageUrlFromImageContainer,
    HandleImageError,
    getOwningPageBackgroundColor,
    isPlaceHolderImage,
    kImageContainerClass,
    pageBackgroundNeedsTransparency,
    setImgTransparentParam,
} from "../../js/bloomImages";
import { doVideoCommand } from "../../js/bloomVideo";
import {
    changeImage,
    copySelection,
    GetEditor,
    pasteClipboard,
} from "../../js/bloomEditing";
import { post, postJson, postString } from "../../../utils/bloomApi";
import { CogIcon } from "../../js/CogIcon";
import { DuplicateIcon } from "../../js/DuplicateIcon";
import { FillSpaceIcon } from "../../js/FillSpaceIcon";
import { LinkIcon } from "../../js/LinkIcon";
import { MissingMetadataIcon } from "../../js/MissingMetadataIcon";
import StyleEditor from "../../StyleEditor/StyleEditor";
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
    IControlRuntime,
    IControlSection,
    IControlMenuCommandRow,
    SectionId,
    TopLevelControlId,
} from "./canvasControlTypes";
import {
    kBackgroundImageClass,
    kBloomCanvasSelector,
} from "./canvasElementConstants";
import { getCanvasElementManager } from "./canvasElementPageBridge";
import { isDraggable, kDraggableIdAttribute } from "./canvasElementDraggables";
import { setGeneratedDraggableId } from "./CanvasElementItem";
import {
    makeFieldTypeMenuItem,
    makeLanguageMenuItem,
} from "./canvasControlTextMenuItems";
import {
    BackgroundColorPanelControl,
    BubbleStylePanelControl,
    ImageFillModePanelControl,
    OutlineColorPanelControl,
    RoundedCornersPanelControl,
    ShowTailPanelControl,
    TextColorPanelControl,
} from "./canvasPanelControls";

const getImageContainer = (ctx: IControlContext): HTMLElement | undefined => {
    const imageContainer = ctx.canvasElement.getElementsByClassName(
        kImageContainerClass,
    )[0] as HTMLElement | undefined;
    if (imageContainer) {
        return imageContainer;
    }
    return getImageFromCanvasElement(ctx.canvasElement)
        ? ctx.canvasElement
        : undefined;
};

const getImage = (ctx: IControlContext): HTMLImageElement | undefined => {
    const imageContainer = getImageContainer(ctx);
    if (!imageContainer) {
        return undefined;
    }
    return getImageFromContainer(imageContainer) ?? undefined;
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

const getFormatTargetElement = (
    ctx: IControlContext,
): HTMLElement | undefined => {
    const editable = getEditable(ctx);
    if (editable) {
        return editable;
    }

    const candidates = Array.from(
        ctx.canvasElement.querySelectorAll("[class]"),
    ) as HTMLElement[];
    return candidates.find((candidate) =>
        StyleEditor.shouldAllowNonEditableStyleDialogTarget(candidate),
    );
};

const hasRealImage = (img: HTMLImageElement | undefined): boolean => {
    if (!img) {
        return false;
    }

    if (isPlaceHolderImage(img.getAttribute("src"))) {
        return false;
    }

    // If the image actually rendered, it is a real image, even if a stale
    // bloom-imageLoadError class is hanging around. Cover images get a persisted
    // onerror that sets that class (BookData.cs), and their resource load can fail
    // spuriously before bootstrap (see BL-14241); nothing clears the class on a
    // later successful load. naturalWidth is the ground truth: a genuinely broken
    // image has naturalWidth 0, so this still treats those as not-real. See BL-16416.
    if (img.complete && img.naturalWidth > 0) {
        return true;
    }

    if (img.classList.contains("bloom-imageLoadError")) {
        return false;
    }

    if (img.parentElement?.classList.contains("bloom-imageLoadError")) {
        return false;
    }

    return true;
};

const buildDynamicMenuItemFromControl = (
    controlId: TopLevelControlId,
    runtime: IControlRuntime,
    overrides: Partial<IControlMenuCommandRow>,
): IControlMenuCommandRow => {
    const control = controlRegistry[controlId];
    if (control.kind !== "command") {
        throw new Error(
            `Control '${controlId}' must be a command to build a menu row.`,
        );
    }

    return {
        id: control.id,
        l10nId: control.l10nId,
        englishLabel: control.englishLabel,
        ...overrides,
        onSelect:
            overrides.onSelect ??
            (async (rowCtx) => {
                await control.action(rowCtx, runtime);
            }),
    };
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

// Draggability is represented both by data attributes and by style-family class names.
// Keep both in sync so the element's appearance and game behavior stay consistent.
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
        // Draggables must not also act as hyperlinks in player mode.
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
    const hasTextRecording = ctx.textHasAudio;
    return {
        id: "chooseAudio",
        l10nId: hasTextRecording
            ? "ARecording"
            : "EditTab.Toolbox.DragActivity.None",
        englishLabel: hasTextRecording ? "A Recording" : "None",
        subLabelL10nId: "EditTab.Image.PlayWhenTouched",
        featureName: "canvas",
        icon: React.createElement(VolumeUpIcon, null),
        onSelect: () => {},
        subMenuItems: [
            {
                id: "useTalkingBookTool",
                l10nId: "UseTalkingBookTool",
                englishLabel: "Use Talking Book Tool",
                onSelect: () => {
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
        l10nId: ctx.hasCurrentImageSound
            ? undefined
            : "EditTab.Toolbox.DragActivity.None",
        englishLabel: imageSoundLabel === "none" ? "None" : imageSoundLabel,
        subLabelL10nId: "EditTab.Image.PlayWhenTouched",
        featureName: "canvas",
        icon: React.createElement(VolumeUpIcon, null),
        onSelect: () => {},
        subMenuItems: [
            {
                id: "removeAudio",
                l10nId: "EditTab.Toolbox.DragActivity.None",
                englishLabel: "None",
                featureName: "canvas",
                onSelect: () => {
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
                onSelect: () => {
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
                    const selectedSound =
                        (await showDialogToChooseSoundFileAsync()) as unknown;
                    if (typeof selectedSound !== "string" || !ctx.page) {
                        return;
                    }

                    ctx.canvasElement.setAttribute("data-sound", selectedSound);
                    void copyAndPlaySoundAsync(selectedSound, ctx.page, false);
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
        action: (ctx, runtime) => {
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
        menu: {
            shortcutDisplay: "Ctrl+V",
        },
        action: (ctx) => {
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
        menu: {
            shortcutDisplay: "Ctrl+C",
        },
        action: (ctx) => {
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
        icon: MissingMetadataIcon,
        menu: {
            icon: React.createElement(CopyrightIcon, null),
            subLabelL10nId: "EditTab.Image.EditMetadataOverlayMore",
        },
        action: (ctx, runtime) => {
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
        iconScale: 0.9,
        icon: React.createElement("img", {
            src: "/bloom/images/reset image black.svg",
            alt: "",
            className: "canvas-context-menu-monochrome-icon",
        }),
        action: () => {
            getCanvasElementManager()?.resetCropping();
        },
    },
    // "Edit with AI…" — the entry point for the AI Image Editor integration.
    //
    // This is the front-end half of a feature whose C# half is AiImageEditorApi.cs
    // (read that file's header for the full picture). The editor is a SEPARATE web app
    // (the `bloom-ai-image-tools` package); we do not import it — we load it into an
    // <iframe> overlay. The flow:
    //   1. POST aiImageEditor/launch -> C# mints a session, makes the per-book
    //      .ai-image-editor folder, and returns the editor URL + the whole-book image
    //      list + enumerated history + httpBase/sessionToken.
    //   2. Build a fixed overlay <div id="ai-editor-overlay"> holding an <iframe> at
    //      that URL with ?mode=bloom-iframe.
    //   3. Handshake over window.postMessage on channel "bloom-ai-image-tools": the
    //      editor posts `ready`; we post `init` (the launch reply + the right-clicked
    //      image as selectedBookImageId). Image bytes never ride postMessage — they go
    //      over HTTP via aiImageEditor/file; the editor references results by id.
    //   4. On `commit` we POST aiImageEditor/commit; C# applies replacements to all
    //      non-current pages and returns {oldSrc,newSrc} for any on the live page, which
    //      we apply here via Bloom's changeImage(). `cancel`/close just tear the overlay
    //      down. (There is intentionally no C#->iframe message channel; init flows from
    //      here, the overlay JS, because only the browser can postMessage to the iframe.)
    editWithAi: {
        kind: "command",
        id: "editWithAi",
        l10nId: "EditTab.Image.EditWithAI",
        englishLabel: "Edit with AI...",
        icon: React.createElement("img", {
            src: "/bloom/images/ai-edit.svg",
            alt: "",
        }),
        action: (ctx, runtime) => {
            const img = getImage(ctx);
            if (!img) {
                return;
            }
            const imgContainer = getImageContainer(ctx);
            const canvasElement = ctx.canvasElement;

            post("aiImageEditor/launch", (r) => {
                const launchData = r.data as {
                    editorUrl: string;
                    httpBase: string;
                    sessionToken: string;
                    book: { id: string; title: string };
                    bookImages?: Array<{
                        id: string;
                        src: string;
                        pageLabel?: string;
                        width?: number;
                        height?: number;
                        isPlaceholder?: boolean;
                    }>;
                    references?: Array<{
                        id: string;
                        src: string;
                        name?: string;
                    }>;
                    // Enumerated by C# from the per-book history folder; rides through
                    // the `...launchData` spread into the editor's init payload.
                    history?: Array<{
                        id: string;
                        url: string;
                        metadata?: Record<string, unknown> | null;
                    }>;
                    apiKey?: string | null;
                    openRouterUser?: string | null;
                };
                const hostWindow = (window.top ?? window) as Window & {
                    __bloomAiImageEditorCleanup?: () => void;
                };
                const hostDocument = hostWindow.document;
                const iframeUrl = new URL(
                    launchData.editorUrl,
                    hostWindow.location.href,
                );
                iframeUrl.searchParams.set("mode", "bloom-iframe");
                // Bloom (C#) enumerates every user-changeable image in the whole book
                // and supplies them as `launchData.bookImages`, each with a stable
                // "{pageId}:{ordinal}" id the editor echoes back on commit. The host
                // applies replacements book-wide in C#, so there is no per-image DOM
                // id wrangling here anymore.

                // Identify the image the user actually right-clicked so the editor can
                // open with it already in the "Image to Edit" slot. We match by page +
                // filename rather than DOM ordinal, because the live page has extra
                // injected UI images that would throw positional indices off.
                const fileNameOf = (url?: string | null) => {
                    try {
                        return decodeURIComponent(
                            (url ?? "").split("?")[0].split("/").pop() ?? "",
                        );
                    } catch {
                        return "";
                    }
                };
                const clickedUrl = imgContainer
                    ? getImageUrlFromImageContainer(imgContainer as HTMLElement)
                    : (img as HTMLImageElement)?.getAttribute("src");
                const clickedPageId = canvasElement
                    .closest(".bloom-page")
                    ?.getAttribute("id");
                const clickedFile = fileNameOf(clickedUrl);
                const clickedMatch =
                    clickedPageId && clickedFile
                        ? (launchData.bookImages ?? []).find(
                              (bi) =>
                                  bi.id.startsWith(clickedPageId + ":") &&
                                  fileNameOf(bi.src) === clickedFile,
                          )
                        : undefined;
                // Don't preload an empty placeholder slot into the edit target — there's
                // nothing to edit, and its placeholder graphic isn't a real raster image.
                const selectedBookImageId = clickedMatch?.isPlaceholder
                    ? undefined
                    : clickedMatch?.id;

                const initPayload = {
                    ...launchData,
                    bookImages: launchData.bookImages ?? [],
                    references: launchData.references ?? [],
                    apiKey: launchData.apiKey ?? null,
                    selectedBookImageId,
                };

                hostWindow.__bloomAiImageEditorCleanup?.();

                const cleanup = () => {
                    hostWindow.removeEventListener("message", handleMessage);
                    hostDocument.getElementById("ai-editor-overlay")?.remove();
                    delete hostWindow.__bloomAiImageEditorCleanup;
                };

                const overlay = hostDocument.createElement("div");
                overlay.id = "ai-editor-overlay";
                Object.assign(overlay.style, {
                    position: "fixed",
                    inset: "8px",
                    zIndex: "10000",
                    background: "#1a1a2e",
                    borderRadius: "12px",
                    overflow: "hidden",
                    boxShadow: "0 18px 48px rgba(0, 0, 0, 0.45)",
                });

                const closeBtn = hostDocument.createElement("button");
                closeBtn.textContent = "✕";
                Object.assign(closeBtn.style, {
                    position: "absolute",
                    top: "8px",
                    right: "12px",
                    zIndex: "10001",
                    background: "transparent",
                    border: "none",
                    color: "#fff",
                    fontSize: "20px",
                    cursor: "pointer",
                    opacity: "0.6",
                });
                closeBtn.onclick = cleanup;
                overlay.appendChild(closeBtn);

                const iframe = hostDocument.createElement("iframe");
                iframe.src = iframeUrl.toString();
                iframe.setAttribute("allow", "clipboard-read; clipboard-write");
                Object.assign(iframe.style, {
                    width: "100%",
                    height: "100%",
                    border: "none",
                });
                overlay.appendChild(iframe);

                // Apply replacements the host flagged as being on the currently-edited
                // page. The host can't change that page itself (the live browser owns
                // it), so it returns oldSrc/newSrc and we use Bloom's changeImage() on
                // the live DOM. We match by oldSrc rather than index because the live
                // page has extra UI images that would throw off positional ordinals.
                const applyCurrentPageReplacements = (
                    results?: Array<{
                        ok?: boolean;
                        isCurrentPage?: boolean;
                        oldSrc?: string;
                        newSrc?: string;
                    }>,
                ) => {
                    if (!results) return;
                    const pageDoc = img.ownerDocument;
                    const pageRoot =
                        (pageDoc.querySelector(".bloom-page") as HTMLElement) ||
                        pageDoc;
                    const decode = (s?: string | null) => {
                        if (!s) return "";
                        try {
                            return decodeURIComponent(s);
                        } catch {
                            return s;
                        }
                    };
                    const srcOf = (el: Element) => {
                        if (el.tagName === "IMG")
                            return el.getAttribute("src") || "";
                        const m = (el.getAttribute("style") || "").match(
                            /url\(['"]?([^'")]+)/,
                        );
                        return m ? m[1] : "";
                    };
                    // A page can have several slots sharing the same source (e.g. multiple
                    // empty placeholders). Consume each matched element once so distinct
                    // replacements land on distinct elements instead of collapsing onto the
                    // first match.
                    const usedElements = new Set<Element>();
                    results
                        .filter(
                            (r) =>
                                r &&
                                r.ok &&
                                r.isCurrentPage &&
                                r.newSrc &&
                                r.oldSrc,
                        )
                        .forEach((r) => {
                            const target = Array.from(
                                pageRoot.querySelectorAll(
                                    'img, [style*="background-image"]',
                                ),
                            ).find(
                                (el) =>
                                    !usedElements.has(el) &&
                                    decode(srcOf(el)) === decode(r.oldSrc),
                            ) as HTMLElement | undefined;
                            if (!target) return;
                            usedElements.add(target);
                            if (!target.id) {
                                target.id =
                                    "bloom-ai-" +
                                    Math.random().toString(36).slice(2, 10);
                            }
                            const creator =
                                target.getAttribute("data-creator") || "";
                            const newCreator = /Edited with AI/i.test(creator)
                                ? creator
                                : creator
                                  ? creator + ", Edited with AI"
                                  : "Edited with AI";
                            changeImage({
                                imageId: target.id,
                                src: r.newSrc as string,
                                creator: newCreator,
                                copyright:
                                    target.getAttribute("data-copyright") || "",
                                license:
                                    target.getAttribute("data-license") || "",
                                // The AI commit applies replacements book-wide in C#
                                // (saved directly, not undoable), so don't register a
                                // separate per-image undo for the current-page piece.
                                undoable: "false",
                            });
                        });
                };

                const handleMessage = (event: MessageEvent) => {
                    if (event.source !== iframe.contentWindow) {
                        return;
                    }

                    const data = event.data as
                        | {
                              channel?: string;
                              type?: string;
                              requestId?: string;
                              payload?: {
                                  level?: string;
                                  message?: string;
                                  url?: string;
                                  replacements?: Array<{
                                      incomingId?: string;
                                      resultId?: string;
                                  }>;
                                  apiKey?: string | null;
                                  authMethod?: string | null;
                                  openRouterUser?: string | null;
                              };
                          }
                        | undefined;

                    if (data?.channel !== "bloom-ai-image-tools") {
                        return;
                    }

                    switch (data.type) {
                        case "ready":
                            iframe.contentWindow?.postMessage(
                                {
                                    channel: "bloom-ai-image-tools",
                                    type: "init",
                                    payload: initPayload,
                                },
                                iframeUrl.origin,
                            );
                            break;
                        case "cancel":
                            cleanup();
                            break;
                        case "commit": {
                            // Replacements can target images on any page of the book.
                            // The host (AiImageEditorApi.HandleCommit) applies changes to
                            // NON-current pages directly against the whole-book DOM and
                            // saves. It cannot touch the page currently open for editing
                            // (the live browser owns it), so for those it returns
                            // {isCurrentPage, oldSrc, newSrc} and we apply them here via
                            // Bloom's own changeImage() against the live page DOM.
                            const requestId = data.requestId;
                            const ackEditor = (ok: boolean, error?: string) => {
                                iframe.contentWindow?.postMessage(
                                    {
                                        channel: "bloom-ai-image-tools",
                                        type: "ack",
                                        requestId,
                                        ok,
                                        error,
                                    },
                                    iframeUrl.origin,
                                );
                            };

                            const replacements =
                                data.payload?.replacements ?? [];
                            if (replacements.length === 0) {
                                ackEditor(false, "No replacements to apply.");
                                break;
                            }

                            postJson(
                                "aiImageEditor/commit?session=" +
                                    encodeURIComponent(launchData.sessionToken),
                                { replacements },
                                (response) => {
                                    const result = response?.data as
                                        | {
                                              ok?: boolean;
                                              appliedCount?: number;
                                              results?: Array<{
                                                  ok?: boolean;
                                                  isCurrentPage?: boolean;
                                                  oldSrc?: string;
                                                  newSrc?: string;
                                              }>;
                                          }
                                        | undefined;
                                    applyCurrentPageReplacements(
                                        result?.results,
                                    );
                                    const ok = result?.ok !== false;
                                    ackEditor(
                                        ok,
                                        ok
                                            ? undefined
                                            : "Some images could not be replaced.",
                                    );
                                    if (ok) {
                                        cleanup();
                                    }
                                },
                                () => {
                                    ackEditor(
                                        false,
                                        "Failed to apply replacements.",
                                    );
                                },
                            );
                            break;
                        }
                        case "log":
                            console.log(
                                "[AI Image Editor:" +
                                    (data.payload?.level ?? "info") +
                                    "] " +
                                    (data.payload?.message ?? ""),
                            );
                            break;
                        case "open-external":
                            // The editor wants a URL (OpenRouter OAuth) opened in the
                            // user's real default browser, not navigated inside the
                            // WebView. Bloom shells out to the OS default browser.
                            if (data.payload?.url) {
                                postString(
                                    "aiImageEditor/openExternal?session=" +
                                        encodeURIComponent(
                                            launchData.sessionToken,
                                        ),
                                    data.payload.url,
                                );
                            }
                            break;
                        case "saveCredentials":
                            // Bloom owns the OpenRouter key. When the user signs in or
                            // pastes a key in the editor, the editor hands it up here so
                            // Bloom persists it per-user (and supplies it on the next
                            // launch). A null apiKey clears the stored credentials.
                            postJson(
                                "aiImageEditor/saveCredentials?session=" +
                                    encodeURIComponent(launchData.sessionToken),
                                {
                                    apiKey: data.payload?.apiKey ?? null,
                                    authMethod:
                                        data.payload?.authMethod ?? null,
                                    openRouterUser:
                                        data.payload?.openRouterUser ?? null,
                                },
                            );
                            break;
                    }
                };

                hostWindow.addEventListener("message", handleMessage);
                hostWindow.__bloomAiImageEditorCleanup = cleanup;
                hostDocument.body.appendChild(overlay);
            });
            runtime.closeMenu(true);
        },
    },
    expandToFillSpace: {
        kind: "command",
        id: "expandToFillSpace",
        l10nId: "EditTab.Toolbox.ComicTool.Options.FillSpace",
        englishLabel: "Fit Space",
        icon: FillSpaceIcon,
        menu: {
            iconScale: 0.8,
            icon: React.createElement("img", {
                src: "/bloom/images/fill image black.svg",
                alt: "",
                className: "canvas-context-menu-monochrome-icon",
            }),
        },
        action: () => {
            getCanvasElementManager()?.expandImageToFillSpace();
        },
    },
    imageFieldType: {
        kind: "command",
        id: "imageFieldType",
        l10nId: "EditTab.Toolbox.ComicTool.Options.UseForBookThumbnail",
        englishLabel: "Use for book thumbnail",
        action: () => {},
        menu: {
            buildMenuItem: (ctx) => {
                const img = getImage(ctx);
                const isCoverImage =
                    img?.getAttribute("data-book") === "coverImage";

                return {
                    id: "imageFieldType",
                    l10nId: "EditTab.Toolbox.ComicTool.Options.UseForBookThumbnail",
                    englishLabel: "Use for book thumbnail",
                    icon: isCoverImage
                        ? React.createElement(CheckIcon, null)
                        : undefined,
                    onSelect: (rowCtx) => {
                        const rowImage = getImage(rowCtx);
                        if (!rowImage) {
                            return;
                        }

                        if (isCoverImage) {
                            rowImage.removeAttribute("data-book");
                            return;
                        }

                        const page = rowCtx.canvasElement.closest(
                            ".bloom-page",
                        ) as HTMLElement | null;
                        if (!page) {
                            return;
                        }

                        Array.from(
                            page.querySelectorAll(
                                'img[data-book="coverImage"]',
                            ),
                        ).forEach((existingCoverImage) => {
                            if (existingCoverImage !== rowImage) {
                                existingCoverImage.removeAttribute("data-book");
                            }
                        });
                        rowImage.setAttribute("data-book", "coverImage");
                    },
                };
            },
        },
    },
    becomeBackground: {
        kind: "command",
        id: "becomeBackground",
        l10nId: "EditTab.Toolbox.ComicTool.Options.BecomeBackground",
        englishLabel: "Become Background",
        action: (ctx) => {
            const img = getImage(ctx);
            if (!img) {
                return;
            }

            const bloomCanvas = ctx.canvasElement.closest(
                kBloomCanvasSelector,
            ) as HTMLElement | null;
            if (!bloomCanvas) {
                return;
            }

            const canvasElementManager = getCanvasElementManager();
            if (!canvasElementManager) {
                return;
            }

            // Ensure a background canvas element exists before we try to swap.
            canvasElementManager.turnOnCanvasElementEditing();

            const bgImageCe = bloomCanvas.getElementsByClassName(
                kBackgroundImageClass,
            )[0] as HTMLElement | undefined;
            if (!bgImageCe) {
                return;
            }

            const bgImgContainer =
                bgImageCe.getElementsByClassName(kImageContainerClass)[0];
            const bgImg = bgImgContainer?.getElementsByTagName("img")[0] as
                | HTMLImageElement
                | undefined;
            if (!bgImg) {
                return;
            }

            const haveRealBgImage = hasRealImage(bgImg);
            const currentImageSource = img.getAttribute("src") || "";
            const currentCopyright = img.getAttribute("data-copyright");
            const currentCreator = img.getAttribute("data-creator");
            const currentLicense = img.getAttribute("data-license");
            const currentDataBook = img.getAttribute("data-book");
            const backgroundDataBook = bgImg.getAttribute("data-book");

            if (haveRealBgImage) {
                img.setAttribute("src", bgImg.getAttribute("src") || "");
                img.setAttribute(
                    "data-copyright",
                    bgImg.getAttribute("data-copyright") || "",
                );
                img.setAttribute(
                    "data-creator",
                    bgImg.getAttribute("data-creator") || "",
                );
                img.setAttribute(
                    "data-license",
                    bgImg.getAttribute("data-license") || "",
                );
                if (backgroundDataBook) {
                    img.setAttribute("data-book", backgroundDataBook);
                } else {
                    img.removeAttribute("data-book");
                }
                img.classList.remove("bloom-imageObjectFit-cover");
                canvasElementManager.updateCanvasElementForChangedImage(img);
            }

            // Reset any stale load-error state before changing the src, matching
            // switchBackgroundToCanvasElement. Otherwise a leftover bloom-imageLoadError
            // class (e.g. from the cover image's spurious early-load failure) can make
            // the new background image look unreal and disable Delete. See BL-16416.
            // hasRealImage() checks the class on both the img and its container, so
            // clear both.
            bgImg.classList.remove("bloom-imageLoadError");
            bgImgContainer?.classList.remove("bloom-imageLoadError");
            bgImg.onerror = HandleImageError;
            // Keep the stored value book-relative; assigning .src can expand to an absolute URL.
            bgImg.setAttribute("src", currentImageSource);
            bgImg.setAttribute("data-copyright", currentCopyright || "");
            bgImg.setAttribute("data-creator", currentCreator || "");
            bgImg.setAttribute("data-license", currentLicense || "");
            bgImg.classList.add("bloom-imageObjectFit-cover");
            if (currentDataBook) {
                if (currentDataBook === "coverImage" && ctx.page) {
                    Array.from(
                        ctx.page.querySelectorAll(
                            'img[data-book="coverImage"]',
                        ),
                    ).forEach((existingCoverImage) => {
                        if (existingCoverImage !== bgImg) {
                            existingCoverImage.removeAttribute("data-book");
                        }
                    });
                }
                img.removeAttribute("data-book");
                // Order matters when img === bgImg: remove old value first, then set desired value.
                bgImg.setAttribute("data-book", currentDataBook);
            }

            if (!haveRealBgImage) {
                canvasElementManager.deleteCurrentCanvasElement();
            }

            canvasElementManager.updateCanvasElementForChangedImage(bgImg);
            if (!haveRealBgImage) {
                // Wait until selection settles after conversion, then ensure background fills.
                setTimeout(() => {
                    canvasElementManager.setActiveElement(bgImageCe);
                    canvasElementManager.expandImageToFillSpace();
                });
            }
        },
    },
    imageFillMode: {
        kind: "panel",
        id: "imageFillMode",
        l10nId: "EditTab.Toolbox.CanvasTool.ImageFit",
        englishLabel: "Image Fit",
        canvasToolsControl: ImageFillModePanelControl,
    },
    imageBackground: {
        kind: "command",
        id: "imageBackground",
        l10nId: "EditTab.Image.Background",
        englishLabel: "Background",
        action: () => {},
        menu: {
            buildMenuItem: (ctx, _runtime) => {
                const img = getImage(ctx);
                const isTransparent =
                    img?.classList.contains("bloom-transparent") ?? false;
                const isOpaque =
                    img?.classList.contains("bloom-opaque") ?? false;
                const isAuto = !isTransparent && !isOpaque;

                // After mutating the img's classes, recompute and apply the transparent param.
                function applyTransparencyParam() {
                    if (!img) return;
                    const bgColor = getOwningPageBackgroundColor(img);
                    setImgTransparentParam(
                        img,
                        getImageTransparencyMode(
                            img,
                            pageBackgroundNeedsTransparency(bgColor),
                        ),
                    );
                }

                return {
                    id: "imageBackground",
                    l10nId: "EditTab.Image.Background",
                    englishLabel: "Background",
                    onSelect: () => {},
                    subMenuItems: [
                        {
                            l10nId: "EditTab.Image.Background.Auto",
                            englishLabel: "Auto",
                            icon: isAuto
                                ? React.createElement(CheckIcon, null)
                                : undefined,
                            onSelect: () => {
                                if (!img) return;
                                img.classList.remove(
                                    "bloom-transparent",
                                    "bloom-opaque",
                                );
                                applyTransparencyParam();
                            },
                        },
                        {
                            l10nId: "EditTab.Image.Background.Transparent",
                            englishLabel: "Transparent",
                            icon: isTransparent
                                ? React.createElement(CheckIcon, null)
                                : undefined,
                            onSelect: () => {
                                if (!img) return;
                                img.classList.add("bloom-transparent");
                                img.classList.remove("bloom-opaque");
                                applyTransparencyParam();
                            },
                        },
                        {
                            l10nId: "EditTab.Image.Background.Opaque",
                            englishLabel: "Opaque",
                            icon: isOpaque
                                ? React.createElement(CheckIcon, null)
                                : undefined,
                            onSelect: () => {
                                if (!img) return;
                                img.classList.add("bloom-opaque");
                                img.classList.remove("bloom-transparent");
                                // bloom-opaque always means "none" regardless of page background.
                                setImgTransparentParam(img, "none");
                            },
                        },
                    ],
                };
            },
        },
    },
    chooseVideo: {
        kind: "command",
        id: "chooseVideo",
        l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
        englishLabel: "Choose Video from your Computer...",
        icon: SearchIcon,
        action: (ctx, runtime) => {
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
        toolbar: {
            iconScale: 0.8,
        },
        menu: {
            icon: React.createElement(CircleIcon, {
                fontSize: "small",
            }),
        },
        action: (ctx, runtime) => {
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
        action: (ctx) => {
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
        action: (ctx) => {
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
            iconScale: 0.8,
        },
        action: (ctx) => {
            const target = getFormatTargetElement(ctx);
            if (!target) {
                return;
            }

            GetEditor().runFormatDialog(target);
        },
    },
    copyText: {
        kind: "command",
        id: "copyText",
        l10nId: "EditTab.Toolbox.ComicTool.Options.CopyText",
        englishLabel: "Copy Text",
        icon: CopyIcon,
        action: () => {
            copySelection();
        },
    },
    pasteText: {
        kind: "command",
        id: "pasteText",
        l10nId: "EditTab.Toolbox.ComicTool.Options.PasteText",
        englishLabel: "Paste Text",
        icon: PasteIcon,
        action: () => {
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
            buildMenuItem: (ctx, runtime) =>
                buildDynamicMenuItemFromControl("autoHeight", runtime, {
                    icon: React.createElement(CheckIcon, {
                        style: {
                            visibility: ctx.canvasElement.classList.contains(
                                "bloom-noAutoHeight",
                            )
                                ? "hidden"
                                : "visible",
                        },
                    }),
                }),
        },
        action: (ctx) => {
            ctx.canvasElement.classList.toggle("bloom-noAutoHeight");
            getCanvasElementManager()?.updateAutoHeight();
        },
    },
    language: {
        kind: "command",
        id: "language",
        l10nId: "EditTab.Toolbox.ComicTool.Options.Language",
        englishLabel: "Language:",
        action: () => {},
        menu: {
            buildMenuItem: (ctx) => makeLanguageMenuItem(ctx),
        },
    },
    fieldType: {
        kind: "command",
        id: "fieldType",
        l10nId: "EditTab.Toolbox.ComicTool.Options.FieldType",
        englishLabel: "Field:",
        action: () => {},
        menu: {
            buildMenuItem: (ctx) => makeFieldTypeMenuItem(ctx),
        },
    },
    fillBackground: {
        kind: "command",
        id: "fillBackground",
        l10nId: "EditTab.Toolbox.ComicTool.Options.FillBackground",
        englishLabel: "Fill Background",
        icon: CheckIcon,
        menu: {
            buildMenuItem: (ctx, runtime) =>
                buildDynamicMenuItemFromControl("fillBackground", runtime, {
                    icon: ctx.rectangleHasBackground
                        ? React.createElement(CheckIcon, null)
                        : undefined,
                }),
        },
        action: (ctx) => {
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
        action: () => {
            getCanvasElementManager()?.addChildCanvasElement?.();
        },
    },
    bubbleStyle: {
        kind: "panel",
        id: "bubbleStyle",
        l10nId: "EditTab.Toolbox.ComicTool.Options.Style",
        englishLabel: "Style",
        canvasToolsControl: BubbleStylePanelControl,
    },
    showTail: {
        kind: "panel",
        id: "showTail",
        l10nId: "EditTab.Toolbox.ComicTool.Options.ShowTail",
        englishLabel: "Show Tail",
        canvasToolsControl: ShowTailPanelControl,
    },
    roundedCorners: {
        kind: "panel",
        id: "roundedCorners",
        l10nId: "EditTab.Toolbox.ComicTool.Options.RoundedCorners",
        englishLabel: "Rounded Corners",
        canvasToolsControl: RoundedCornersPanelControl,
    },
    textColor: {
        kind: "panel",
        id: "textColor",
        l10nId: "EditTab.Toolbox.ComicTool.Options.TextColor",
        englishLabel: "Text Color",
        canvasToolsControl: TextColorPanelControl,
    },
    backgroundColor: {
        kind: "panel",
        id: "backgroundColor",
        l10nId: "EditTab.Toolbox.ComicTool.Options.BackgroundColor",
        englishLabel: "Background Color",
        canvasToolsControl: BackgroundColorPanelControl,
    },
    outlineColor: {
        kind: "panel",
        id: "outlineColor",
        l10nId: "EditTab.Toolbox.ComicTool.Options.OutlineColor",
        englishLabel: "Outline Color",
        canvasToolsControl: OutlineColorPanelControl,
    },
    setDestination: {
        kind: "command",
        id: "setDestination",
        featureName: "canvas",
        l10nId: "EditTab.Toolbox.CanvasTool.SetDest",
        englishLabel: "Set Destination",
        icon: LinkIcon,
        toolbar: {
            iconScale: 0.8,
        },
        action: (ctx, runtime) => {
            runtime.closeMenu(true);

            // For navigation canvas elements we keep the destination on the canvas
            // element itself (not on any nested image container).
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

                // This toolbar render callback lives inside the registry object,
                // not inside a normal JSX component body, so it returns the node
                // with React.createElement instead of JSX. Use plain style objects
                // here: passing Emotion's css prop through createElement would
                // serialize to a literal DOM attribute.
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
                                // UX requirement: match the primary-blue affordance
                                // used by other clickable toolbar text.
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
        action: (ctx, runtime) => {
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
        menu: {
            shortcutDisplay: "Ctrl+D",
        },
        action: () => {
            makeDuplicateOfDragBubble();
        },
    },
    delete: {
        kind: "command",
        id: "delete",
        l10nId: "Common.Delete",
        englishLabel: "Delete",
        icon: DeleteIcon,
        menu: {
            iconScale: 1.2,
        },
        action: () => {
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
            buildMenuItem: (ctx, runtime) =>
                buildDynamicMenuItemFromControl("toggleDraggable", runtime, {
                    subLabelL10nId:
                        "EditTab.Toolbox.DragActivity.DraggabilityMore",
                    icon: React.createElement(CheckIcon, {
                        style: {
                            visibility: isDraggable(ctx.canvasElement)
                                ? "visible"
                                : "hidden",
                        },
                    }),
                }),
        },
        action: (ctx) => {
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
            buildMenuItem: (ctx, runtime) =>
                buildDynamicMenuItemFromControl(
                    "togglePartOfRightAnswer",
                    runtime,
                    {
                        subLabelL10nId:
                            "EditTab.Toolbox.DragActivity.PartOfRightAnswerMore.v2",
                        icon: React.createElement(CheckIcon, {
                            style: {
                                visibility: ctx.hasDraggableTarget
                                    ? "visible"
                                    : "hidden",
                            },
                        }),
                    },
                ),
        },
        action: (ctx) => {
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
        action: () => {},
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
    gameDraggable: {
        id: "gameDraggable",
        controlsBySurface: {
            menu: ["toggleDraggable", "togglePartOfRightAnswer"],
        },
    },
    formatTarget: {
        id: "formatTarget",
        controlsBySurface: {
            menu: ["format"],
        },
    },
    image: {
        id: "image",
        controlsBySurface: {
            menu: [
                "missingMetadata",
                "chooseImage",
                "copyImage",
                "pasteImage",
                "resetImage",
                "editWithAi",
                "expandToFillSpace",
                "becomeBackground",
                "imageFieldType",
                "imageBackground",
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
                "language",
                "fieldType",
            ],
            toolPanel: ["textColor", "backgroundColor"],
        },
    },
    wholeElement: {
        id: "wholeElement",
        controlsBySurface: {
            menu: ["duplicate", "delete"],
        },
    },
};
