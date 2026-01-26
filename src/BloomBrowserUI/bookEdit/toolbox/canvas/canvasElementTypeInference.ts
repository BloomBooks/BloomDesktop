import { CanvasElementType } from "./canvasElementTypes";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
} from "./canvasElementConstants";

// Best-effort inference of canvas element type based on the DOM structure.
// Keep this dependency-light; it is used from both the toolbox and the page iframe.
export const inferCanvasElementType = (
    canvasElement: HTMLElement,
): CanvasElementType | undefined => {
    if (canvasElement.classList.contains(kBloomButtonClass)) {
        const hasImage =
            canvasElement.getElementsByClassName("bloom-imageContainer")
                .length > 0;
        const hasLabel =
            canvasElement.getElementsByClassName("bloom-translationGroup")
                .length > 0;
        if (hasImage && hasLabel) {
            return "navigation-image-with-label-button";
        }
        if (hasImage) {
            return "navigation-image-button";
        }
        return "navigation-label-button";
    }

    if (canvasElement.getElementsByClassName("bloom-link-grid").length > 0) {
        return "book-link-grid";
    }

    if (
        canvasElement.getElementsByClassName("bloom-videoContainer").length > 0
    ) {
        return "video";
    }

    if (canvasElement.getElementsByClassName("bloom-rectangle").length > 0) {
        return "rectangle";
    }

    if (canvasElement.querySelector('[data-icon-type="audio"]')) {
        return "sound";
    }

    if (
        canvasElement.getElementsByClassName("bloom-imageContainer").length >
            0 ||
        canvasElement.classList.contains(kBackgroundImageClass)
    ) {
        return "image";
    }

    if (canvasElement.getElementsByClassName("bloom-editable").length > 0) {
        return "speech";
    }

    return undefined;
};
