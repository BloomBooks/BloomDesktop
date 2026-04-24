import { EditableDivUtils } from "../../js/editableDivUtils";
import {
    getCanvasElementManager,
    kBloomCanvasClass,
    kCanvasElementClass,
} from "./canvasElementUtils";

const kDefaultMinimumRightMarginDistance = 10;

function pxToNumber(dimension: string, defaultValue = 0): number {
    if (!dimension) {
        return defaultValue;
    }
    return Number.parseFloat(dimension.replace("px", ""));
}

// This function tries to make sure that the given canvas element fits on the page.
// We're trying to fix really nasty-looking messes, like changing topic from "Math" to
// "Animal stories" and finding that all but the first few letters are off the page.
// On the other hand, we don't want to mess with anything that MIGHT be just how the
// user wants it. The constraints applied here are my best guess of what might be helpful:
// - find the narrowest width that fits the content at the current height
// - then, at that width, reduce the height to the content's actual height
// - if wrapping is not allowed, width fitting is done with no-wrap content
// Once we have determined a size, move it minimally so that, if possible, the whole
// thing is on the page, or as much as possible.
export function ensureFieldFitsOnCustomPage(
    elementToAdjust: HTMLElement,
): void {
    const canvasElement = elementToAdjust.closest(
        `.${kCanvasElementClass}`,
    ) as HTMLElement;
    if (!canvasElement) {
        return;
    }

    const page = canvasElement.closest(".bloom-page") as HTMLElement;
    if (!page || !page.classList.contains("bloom-customLayout")) {
        return;
    }

    const bloomCanvas = page.getElementsByClassName(
        kBloomCanvasClass,
    )[0] as HTMLElement;
    if (!bloomCanvas || !bloomCanvas.contains(canvasElement)) {
        return;
    }

    const currentWidth = pxToNumber(canvasElement.style.width);
    const currentHeight = pxToNumber(canvasElement.style.height);
    if (currentWidth <= 0 || currentHeight <= 0) {
        return;
    }
    const previousRightMarginDistance = Math.max(
        0,
        bloomCanvas.clientWidth - (canvasElement.offsetLeft + currentWidth),
    );

    const scale = EditableDivUtils.getPageScale() || 1;
    const getRenderedHeight = (): number =>
        Math.ceil(elementToAdjust.getBoundingClientRect().height / scale);
    // Returns the narrowest width that fits all content without wrapping,
    // checking both the element and its direct children (since a constrained
    // container clips getBoundingClientRect but children still have their full scrollWidth).
    const getScrollWidth = (): number =>
        Math.ceil(
            Math.max(
                elementToAdjust.scrollWidth + 1,
                ...Array.from(elementToAdjust.children).map(
                    (child) => (child as HTMLElement).scrollWidth,
                ),
            ),
        );
    const wrapsByWhiteSpace = (): boolean => {
        const whiteSpace = getComputedStyle(elementToAdjust).whiteSpace;
        return whiteSpace !== "nowrap" && whiteSpace !== "pre";
    };

    const overflowsVertically = (): boolean =>
        getRenderedHeight() > currentHeight + 1;
    const overflowsHorizontally = (): boolean => {
        const containerWidth = pxToNumber(
            canvasElement.style.width,
            canvasElement.clientWidth,
        );
        return getScrollWidth() > containerWidth + 1;
    };
    const hasOverflow = (): boolean =>
        overflowsVertically() || overflowsHorizontally();
    const oldWhiteSpace = elementToAdjust.style.whiteSpace;

    let fittedWidth: number;
    if (!wrapsByWhiteSpace()) {
        // No wrapping allowed: width must fit all content on one line.
        elementToAdjust.style.whiteSpace = "nowrap";
        fittedWidth = getScrollWidth();
        elementToAdjust.style.whiteSpace = oldWhiteSpace;
    } else {
        elementToAdjust.style.whiteSpace = "nowrap";
        const noWrapWidth = getScrollWidth();
        elementToAdjust.style.whiteSpace = oldWhiteSpace;

        canvasElement.style.width = `${noWrapWidth}px`;
        if (hasOverflow()) {
            // Even at the widest useful width, we still overflow vertically.
            // Keep the width that minimizes height.
            fittedWidth = noWrapWidth;
        } else {
            // Binary search for the narrowest width that still fits at current height.
            let low = 1;
            let high = noWrapWidth;
            while (high - low > 1) {
                const mid = Math.floor((low + high) / 2);
                canvasElement.style.width = `${mid}px`;
                if (hasOverflow()) {
                    low = mid;
                } else {
                    high = mid;
                }
            }

            canvasElement.style.width = `${low}px`;
            fittedWidth = hasOverflow() ? high : low;
        }
    }

    const finalWidth = Math.max(1, Math.ceil(fittedWidth));
    canvasElement.style.width = `${finalWidth}px`;

    const minimumTextBoxHeightPx =
        getCanvasElementManager()?.minTextBoxHeightPx ?? 30;
    const finalHeight = Math.max(
        minimumTextBoxHeightPx,
        Math.min(currentHeight, getRenderedHeight()),
    );
    canvasElement.style.height = `${finalHeight}px`;

    const minimumRightMarginDistance = Math.min(
        kDefaultMinimumRightMarginDistance,
        previousRightMarginDistance,
    );
    const preferredMaxLeft =
        bloomCanvas.clientWidth - finalWidth - minimumRightMarginDistance;
    // If preserving the margin would push the box off the left edge,
    // prioritize keeping the box on-page.
    const maxLeft = Math.max(0, preferredMaxLeft);
    const maxTop = Math.max(0, bloomCanvas.clientHeight - finalHeight);
    const clampedLeft = Math.max(
        0,
        Math.min(canvasElement.offsetLeft, maxLeft),
    );
    const clampedTop = Math.max(0, Math.min(canvasElement.offsetTop, maxTop));
    if (clampedLeft !== canvasElement.offsetLeft) {
        canvasElement.style.left = `${clampedLeft}px`;
    }
    if (clampedTop !== canvasElement.offsetTop) {
        canvasElement.style.top = `${clampedTop}px`;
    }
}
