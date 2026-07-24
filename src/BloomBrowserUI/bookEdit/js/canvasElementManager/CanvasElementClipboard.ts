// Clipboard/paste helpers extracted from CanvasElementManager.
//
// This module owns the logic for pasting images from the clipboard into a bloom-canvas.

import { Point, PointScaling } from "../point";
import {
    IImageInfo,
    kMakeNewCanvasElement,
    changeImageInfo,
    notifyToolOfChangedImage,
    wrapWithRequestPageContentDelay,
} from "../bloomEditing";
import { isPlaceHolderImage, kImageContainerClass } from "../bloomImages";
import {
    adjustTarget,
    correctTabIndex,
    getActiveGameTab,
    playTabIndex,
    startTabIndex,
    wrongTabIndex,
} from "../../toolbox/games/GameTool";
import { postJson, get } from "../../../utils/bloomApi";
import { FeatureStatus } from "../../../react_components/featureStatus";
import { showRequiresSubscriptionDialogInEditView } from "../../../react_components/requiresSubscription";
import BloomMessageBoxSupport from "../../../utils/bloomMessageBoxSupport";
import {
    kBackgroundImageClass,
    kCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";
import { makeTargetAndMatchSize } from "../../toolbox/canvas/CanvasElementItem";
import { getTarget } from "bloom-player";
import $ from "jquery";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

export interface ICanvasElementClipboardHost {
    getActiveOrFirstBloomCanvasOnPage: () => HTMLElement | null;
    getActiveElement: () => HTMLElement | undefined;

    adjustBackgroundImageSize: (
        bloomCanvas: HTMLElement,
        bgCanvasElement: HTMLElement,
        useSizeOfNewImage: boolean,
    ) => void;

    adjustContainerAspectRatio: (
        canvasElement: HTMLElement,
        useSizeOfNewImage?: boolean,
    ) => void;

    addPictureCanvasElement: (
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
        imageInfo?: {
            imageId: string;
            src: string; // must already appropriately URL-encoded.
            copyright: string;
            creator: string;
            license: string;
        },
        size?: { width: number; height: number },
        doAfterElementCreated?: (newElement: HTMLElement) => void,
    ) => HTMLElement;

    setDoAfterNewImageAdjusted: (callback: (() => void) | undefined) => void;
}

export class CanvasElementClipboard {
    private host: ICanvasElementClipboardHost;
    private snapProvider: CanvasSnapProvider;
    private minWidth: number;
    private minHeight: number;

    public constructor(
        host: ICanvasElementClipboardHost,
        snapProvider: CanvasSnapProvider,
        minWidth: number,
        minHeight: number,
    ) {
        this.host = host;
        this.snapProvider = snapProvider;
        this.minWidth = minWidth;
        this.minHeight = minHeight;
    }

    private static getPasteImageApiErrorMessage(
        responseOrError: unknown,
    ): string | undefined {
        const getMessageFromValue = (value: unknown): string | undefined => {
            if (typeof value === "string" && value.trim().length > 0) {
                return value;
            }

            if (!value || typeof value !== "object") {
                return undefined;
            }

            const valueRecord = value as Record<string, unknown>;
            const candidateKeys = [
                "message",
                "Message",
                "error",
                "Error",
                "text",
            ];
            for (const key of candidateKeys) {
                const keyValue = valueRecord[key];
                if (
                    typeof keyValue === "string" &&
                    keyValue.trim().length > 0
                ) {
                    return keyValue;
                }
            }

            return undefined;
        };

        const errorLike = responseOrError as {
            data?: unknown;
            response?: { data?: unknown };
            request?: { responseText?: unknown };
            responseText?: unknown;
        };

        const messageCandidates = [
            errorLike.response?.data,
            errorLike.data,
            errorLike.request?.responseText,
            errorLike.responseText,
        ];

        for (const candidate of messageCandidates) {
            const message = getMessageFromValue(candidate);
            if (message) {
                return message;
            }
        }

        return undefined;
    }

    private static handlePasteImageApiError(responseOrError: unknown): void {
        const message =
            CanvasElementClipboard.getPasteImageApiErrorMessage(
                responseOrError,
            ) ??
            theOneLocalizationManager.getText(
                "EditTab.NoImageFoundOnClipboard",
                "Before you can paste an image, copy one onto your 'clipboard', from another program.",
            );
        BloomMessageBoxSupport.CreateAndShowSimpleMessageBoxWithLocalizedText(
            message,
        );
    }

    // This is called when the user pastes an image from the clipboard.
    // If there is an active canvas element that is an image, and it is empty (placeholder),
    // set its image to the pasted image.
    // Otherwise, if there is a bloom canvas on the page, it will pick the one that has the active element
    // or the first one if none has an active element.
    // (If there is no canvas, it returns false.)
    // If the canvas is empty (including the background), set the background to the image.
    // Else if canvas is allowed by the subscription tier, add the image as a canvas/game item.
    // Make it up to 1/3 width and 1/3 height of the canvas, roughly centered on the canvas.
    // Is it a draggable item? Yes, if we are in the "Start" mode of a game.
    // In that case, we put it a bit higher and further left, so there is room for the target.
    // Otherwise it's just a normal canvas overlay item (restricted to the appropriate state,
    // if we're in the Correct or Wrong state of a game).
    public pasteImageFromClipboard(): boolean {
        const bloomCanvas = this.host.getActiveOrFirstBloomCanvasOnPage();
        if (!bloomCanvas) {
            return false; // No canvas to paste into.
        }
        const activeGameTab = getActiveGameTab();
        if (activeGameTab === playTabIndex) {
            // Can't paste an image into the play tab.
            return false;
        }

        this.postPasteImageRequest();

        return true;
    }

    private postPasteImageRequest(): void {
        // The rest of the job happens after the C# code calls changeImage(), passing this fake ID along
        // with the rest of the information about the new image. The special ID causes a call back to
        // finishPastingImageFromClipboard() with the real image information.
        postJson(
            "editView/pasteImage",
            {
                imageId: kMakeNewCanvasElement,
                imageSrc: "",
                imageIsGif: false,
            },
            undefined,
            CanvasElementClipboard.handlePasteImageApiError,
        );
    }

    public finishPasteImageFromClipboard(imageInfo: IImageInfo): void {
        const bloomCanvas = this.host.getActiveOrFirstBloomCanvasOnPage()!;
        const canvasElements =
            bloomCanvas.getElementsByClassName(kCanvasElementClass);

        // If it's an empty canvas, make this its background image.
        // A possible special case is the custom game page, where the only canvas element is the
        // header. But that works out to our advantage, since we think a background is unlikely
        // in games, and would prefer to interpret the pasted image as a game item.
        if (
            canvasElements.length === 1 &&
            canvasElements[0].classList.contains(kBackgroundImageClass)
        ) {
            const bgimg = canvasElements[0].getElementsByTagName("img")[0];
            if (isPlaceHolderImage(bgimg.getAttribute("src"))) {
                changeImageInfo(bgimg, imageInfo);
                this.host.adjustBackgroundImageSize(
                    bloomCanvas,
                    canvasElements[0] as HTMLElement,
                    true,
                );
                notifyToolOfChangedImage(bgimg);
                return;
            }
        }

        // If an image canvas element is currently selected, replace its image rather than
        // creating a new overlay on top of it. This applies whether the selected element is the
        // background image (as when a Standard Layout cover's picture is selected) or an overlay,
        // and whether or not it currently holds a placeholder. It matches the behavior of the
        // image context menu's Paste command and the expectation that pasting onto a selected
        // image replaces that image. See BL-16542.
        const activeElement = this.host.getActiveElement();
        if (activeElement) {
            const img = activeElement
                .getElementsByClassName(kImageContainerClass)[0]
                ?.getElementsByTagName("img")[0];
            if (img) {
                changeImageInfo(img, imageInfo);
                if (activeElement.classList.contains(kBackgroundImageClass)) {
                    this.host.adjustBackgroundImageSize(
                        bloomCanvas,
                        activeElement,
                        true,
                    );
                } else {
                    this.host.adjustContainerAspectRatio(activeElement, true);
                    adjustTarget(activeElement, getTarget(activeElement));
                }
                notifyToolOfChangedImage(img);
                return;
            }
        }

        // Otherwise we will add a new canvas element...but only if subscription allows it.
        // Keep this wrapper even though adjustContainerAspectRatio now manages its own image-load delay.
        // This branch still has a separate async feature-status request before we even know whether a
        // new element will be created. Without this wrapper, requestPageContent can run before the
        // callback adds the pasted element or decides to show the subscription dialog instead.
        wrapWithRequestPageContentDelay(
            () =>
                new Promise<void>((resolve) => {
                    get(
                        "features/status?featureName=canvas&forPublishing=false",
                        (c) => {
                            const features = c.data as FeatureStatus;
                            if (features.enabled) {
                                // If the feature is enabled, we can proceed with adding the canvas element.
                                const width = Math.max(
                                    this.snapProvider.getSnappedX(
                                        bloomCanvas.offsetWidth / 3,
                                        undefined,
                                    ),
                                    this.minWidth,
                                );
                                const height = Math.max(
                                    this.snapProvider.getSnappedY(
                                        bloomCanvas.offsetHeight / 3,
                                        undefined,
                                    ),
                                    this.minHeight,
                                );
                                if (
                                    width > bloomCanvas.offsetWidth ||
                                    height > bloomCanvas.offsetHeight
                                ) {
                                    // Can't paste image into such a tiny canvas
                                    resolve();
                                    return;
                                }

                                const activeGameTab = getActiveGameTab();
                                let positionX =
                                    (bloomCanvas.offsetWidth - width) / 2;
                                let positionY =
                                    (bloomCanvas.offsetHeight - height) / 2;
                                if (activeGameTab === startTabIndex) {
                                    // If we're in the start tab, we want to put it further towards the top left,
                                    // so there is room for the target.
                                    positionX = positionX / 2;
                                    positionY = positionY / 2;
                                }
                                const { x: adjustedX, y: adjustedY } =
                                    this.snapProvider.getPosition(
                                        undefined,
                                        positionX,
                                        positionY,
                                    );
                                const positionInBloomCanvas = new Point(
                                    adjustedX,
                                    adjustedY,
                                    PointScaling.Scaled,
                                    "pasteImageFromClipboard",
                                );

                                this.host.addPictureCanvasElement(
                                    positionInBloomCanvas,
                                    $(bloomCanvas),
                                    undefined,
                                    imageInfo,
                                    { width, height },
                                    (newCanvasElement) => {
                                        const applyBehaviorByGameTab: Record<
                                            number,
                                            (element: HTMLElement) => void
                                        > = {
                                            [startTabIndex]: (
                                                element: HTMLElement,
                                            ) => {
                                                // make it a draggable, with a target.
                                                // We want to do this after its shape and position are stable, so we arrange for a callback
                                                // after the aspect ratio is adjusted.
                                                // (It would be nice to do this using async and await, or by passing this action as a param
                                                // all the way down to adjustContainerAspectRatio, but there are eight layers of methods
                                                // and at least one settimeout in between, and if each has to await the others, yet other
                                                // callers of those methods have to become async. It would be a mess.)
                                                // We do this as an action passed to addPictureCanvasElement so that doAfterNewImageAdjusted
                                                // is set before the call to adjustContainerAspectRatio, which would be hard to guarantee
                                                // if we did it after the call to addPictureCanvasElement.
                                                this.host.setDoAfterNewImageAdjusted(
                                                    () => {
                                                        makeTargetAndMatchSize(
                                                            element,
                                                        );
                                                    },
                                                );
                                            },
                                            [correctTabIndex]: (
                                                element: HTMLElement,
                                            ) => {
                                                element.classList.add(
                                                    "drag-item-correct",
                                                );
                                            },
                                            [wrongTabIndex]: (
                                                element: HTMLElement,
                                            ) => {
                                                element.classList.add(
                                                    "drag-item-wrong",
                                                );
                                            },
                                        };
                                        const applyBehavior =
                                            applyBehaviorByGameTab[
                                                activeGameTab
                                            ];
                                        if (applyBehavior) {
                                            applyBehavior(newCanvasElement);
                                        }
                                    },
                                );
                                notifyToolOfChangedImage();
                            } else {
                                // If the feature is not enabled, we need to show the subscription dialog.
                                showRequiresSubscriptionDialogInEditView(
                                    "canvas",
                                );
                            }
                            resolve();
                        },
                        () => {
                            resolve();
                        },
                    );
                }),
            "pasteImageFromClipboardAddCanvasElement",
        );
    }
}
