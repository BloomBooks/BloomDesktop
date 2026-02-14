import { CanvasElementType } from "./canvasElementTypes";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
} from "./canvasElementConstants";

const getBubbleStyle = (canvasElement: HTMLElement): string | undefined => {
    const bubbleSpecJson = canvasElement.getAttribute("data-bubble");
    if (bubbleSpecJson) {
        try {
            const bubbleSpec = JSON.parse(bubbleSpecJson) as {
                style?: unknown;
            };
            if (typeof bubbleSpec.style === "string") {
                return bubbleSpec.style.toLowerCase();
            }
        } catch {
            // If the attribute is malformed, fall back to class-based inference below.
        }
    }

    const editable = canvasElement.getElementsByClassName(
        "bloom-editable",
    )[0] as HTMLElement | undefined;
    if (!editable) {
        return undefined;
    }

    const styleClass = Array.from(editable.classList).find((className) =>
        className.endsWith("-style"),
    );
    if (!styleClass) {
        return undefined;
    }

    return styleClass.substring(0, styleClass.length - "-style".length);
};

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
        const bubbleStyle = getBubbleStyle(canvasElement);
        if (bubbleStyle === "caption") {
            return "caption";
        }
        return "speech";
    }

    return undefined;
};
