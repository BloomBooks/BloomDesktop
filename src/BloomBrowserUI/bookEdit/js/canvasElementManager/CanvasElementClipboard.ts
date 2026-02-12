// Clipboard/paste helpers extracted from CanvasElementManager.
//
// This module owns the logic for pasting images from the clipboard into a bloom-canvas.

import { Point, PointScaling } from "../point";
import {
    IImageInfo,
    kMakeNewCanvasElement,
    changeImageInfo,
    notifyToolOfChangedImage,
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
import {
    kBackgroundImageClass,
    kCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";
import { makeTargetAndMatchSize } from "../../toolbox/canvas/CanvasElementItem";
import { getTarget } from "bloom-player";
import $ from "jquery";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

export interface ICanvasElementClipboardHost {
    snapProvider: CanvasSnapProvider;
    minWidth: number;
    minHeight: number;

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

    public constructor(host: ICanvasElementClipboardHost) {
        this.host = host;
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
        // The rest of the job happens after the C# code calls changeImage(), passing this fake ID along
        // with the rest of the information about the new image. The special ID causes a call back to
        // finishPastingImageFromClipboard() with the real image information.
        postJson("editView/pasteImage", {
            imageId: kMakeNewCanvasElement,
            imageSrc: "",
            imageIsGif: false,
        });
        return true;
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

        // If there is an image canvas element (other than the background one) already selected
        // and it is a placeholder, just set its image.
        const activeElement = this.host.getActiveElement();
        if (
            activeElement &&
            !activeElement.classList.contains(kBackgroundImageClass)
        ) {
            const img = activeElement
                .getElementsByClassName(kImageContainerClass)[0]
                ?.getElementsByTagName("img")[0];
            if (img && isPlaceHolderImage(img.getAttribute("src"))) {
                changeImageInfo(img, imageInfo);
                this.host.adjustContainerAspectRatio(
                    activeElement as HTMLElement,
                    true,
                );
                adjustTarget(activeElement, getTarget(activeElement));
                notifyToolOfChangedImage(img);
                return;
            }
        }

        // otherwise we will add a new canvas element...but only if subscription allows it.
        get("features/status?featureName=canvas&forPublishing=false", (c) => {
            const features = c.data as FeatureStatus;
            if (features.enabled) {
                // If the feature is enabled, we can proceed with adding the canvas element.
                const width = Math.max(
                    this.host.snapProvider.getSnappedX(
                        bloomCanvas.offsetWidth / 3,
                        undefined,
                    ),
                    this.host.minWidth,
                );
                const height = Math.max(
                    this.host.snapProvider.getSnappedY(
                        bloomCanvas.offsetHeight / 3,
                        undefined,
                    ),
                    this.host.minHeight,
                );
                if (
                    width > bloomCanvas.offsetWidth ||
                    height > bloomCanvas.offsetHeight
                ) {
                    // Can't paste image into such a tiny canvas
                    return;
                }

                const activeGameTab = getActiveGameTab();
                let positionX = (bloomCanvas.offsetWidth - width) / 2;
                let positionY = (bloomCanvas.offsetHeight - height) / 2;
                if (activeGameTab === startTabIndex) {
                    // If we're in the start tab, we want to put it further towards the top left,
                    // so there is room for the target.
                    positionX = positionX / 2;
                    positionY = positionY / 2;
                }
                const { x: adjustedX, y: adjustedY } =
                    this.host.snapProvider.getPosition(
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
                        switch (activeGameTab) {
                            case startTabIndex:
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
                                this.host.setDoAfterNewImageAdjusted(() => {
                                    makeTargetAndMatchSize(newCanvasElement);
                                });
                                break;
                            case correctTabIndex:
                                newCanvasElement.classList.add(
                                    "drag-item-correct",
                                );
                                break;
                            case wrongTabIndex:
                                newCanvasElement.classList.add(
                                    "drag-item-wrong",
                                );
                                break;
                        }
                    },
                );
                notifyToolOfChangedImage();
            } else {
                // If the feature is not enabled, we need to show the subscription dialog.
                showRequiresSubscriptionDialogInEditView("canvas");
            }
        });
    }
}
