import { getCanvasElementManager } from "../canvas/canvasElementPageBridge";
import { kBloomCanvasClass } from "../canvas/canvasElementConstants";
import {
    currentPageHistory,
    doOutsideHistory,
} from "../../pageHistory/pageHistoryAccess";

// This file is intended to expose some image description functions that other parts of the
// code (in both iframes) need to use, while pulling in a minimum of dependencies.

const kDescribedImageClass = "bloom-describedImage";

// The child nodes that each bloom-canvas had before wrapDescribedImages moved them into a
// bloom-describedImage, in their order, so that unwrapDescribedImages can put them back in it.
const childNodesBeforeWrapping = new WeakMap<Element, Node[]>();

// Stops the page history listeners that take the bloom-describedImage divs away before each undo
// and redo and put them back after it; undefined while the descriptions are not shown.
let stopUnwrappingForUndoAndRedo: (() => void) | undefined;

export function showImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const canvasElementManager = getCanvasElementManager();
    // Turning canvas editing off saves each canvas element's place as its current-language
    // alternate and takes away editing markup. That is how the tool shows the page, not something
    // the user did, so none of it is recorded: inside the click that opens the tool, a change that
    // is recorded would end the page history.
    doOutsideHistory(() =>
        canvasElementManager?.suspendComicEditing("forTool"),
    );
    // turn on special layout to make image descriptions visible (might already be on, so check first)
    if (!bodyOfPageIframe.classList.contains("bloom-showImageDescriptions")) {
        // The wrapping is how a tool shows the page, not something the user did, so it is not
        // recorded in the page history: undo leaves it in place.
        doOutsideHistory(() => {
            bodyOfPageIframe.classList.add("bloom-showImageDescriptions");
            wrapDescribedImages(bodyOfPageIframe);
        });
        unwrapForEachUndoAndRedo(bodyOfPageIframe);
    }
}
export function hideImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const canvasElementManager = getCanvasElementManager();
    stopUnwrappingForUndoAndRedo?.();
    stopUnwrappingForUndoAndRedo = undefined;
    // removing the class and wrapper should be done first; resume may not work
    // right while the extra wrapper is present.
    // Like the wrapping in showImageDescriptions, the unwrapping is not recorded in the page
    // history.
    doOutsideHistory(() => {
        bodyOfPageIframe.classList.remove("bloom-showImageDescriptions");
        unwrapDescribedImages(bodyOfPageIframe);
    });
    canvasElementManager?.resumeComicEditing();
}

/**
 * For each bloom-canvas, wrap the contents (except the description) with another division with
 * class bloom-describedImage. See comment in editMode.less under bloom-describedImage for why we
 * do this.
 */
function wrapDescribedImages(bodyOfPageIframe: HTMLElement) {
    for (const bloomCanvas of Array.from(
        bodyOfPageIframe.getElementsByClassName(kBloomCanvasClass),
    )) {
        // unwrapForEachUndoAndRedo may wrap twice after an undo.
        if (childOfClass(bloomCanvas, kDescribedImageClass)) continue;
        childNodesBeforeWrapping.set(
            bloomCanvas,
            Array.from(bloomCanvas.childNodes),
        );
        const describedImage = document.createElement("div");
        describedImage.classList.add(kDescribedImageClass);
        for (const child of Array.from(bloomCanvas.children)) {
            if (!child.classList.contains("bloom-imageDescription")) {
                describedImage.appendChild(child);
            }
        }
        bloomCanvas.appendChild(describedImage);
    }
}

/**
 * Take each bloom-describedImage away, putting the nodes in it back among the bloom-canvas's
 * other children in the order that wrapDescribedImages found them. The page history compares
 * bloom-canvas child lists, so any other order would make it refuse to undo a step that holds
 * one. Nodes that the bloom-canvas did not have then stay where they are, or go at the end if
 * they are in the bloom-describedImage.
 */
function unwrapDescribedImages(bodyOfPageIframe: HTMLElement) {
    for (const describedImage of Array.from(
        bodyOfPageIframe.getElementsByClassName(kDescribedImageClass),
    )) {
        const bloomCanvas = describedImage.parentElement!;
        const wrapped = Array.from(describedImage.childNodes);
        const before = childNodesBeforeWrapping.get(bloomCanvas) ?? [];
        const order = [
            ...before.filter(
                (node) =>
                    node.parentNode === bloomCanvas ||
                    node.parentNode === describedImage,
            ),
            ...wrapped.filter((node) => !before.includes(node)),
        ];
        // From the end, put each node from the bloom-describedImage just before the node that
        // follows it in order. The bloom-canvas's own children do not move, so the focus and
        // selection in a description are left alone.
        for (let i = order.length - 1; i >= 0; i--) {
            if (order[i].parentNode === describedImage)
                bloomCanvas.insertBefore(order[i], order[i + 1] ?? null);
        }
        describedImage.remove();
        childNodesBeforeWrapping.delete(bloomCanvas);
    }
}

/**
 * The page history does not leave bloom-describedImage out of the bloom-canvas child lists that
 * it records and compares (editTimeMarkup.ts), so while the descriptions are shown it would refuse
 * to undo or redo a step that holds such a list, such as adding, duplicating or deleting a canvas
 * element, and empty that stack of steps. So take the bloom-describedImage divs away before each
 * undo and redo, and put them back after it, outside the history, until hideImageDescriptions.
 * The listeners go with the page's history when the page goes.
 */
function unwrapForEachUndoAndRedo(bodyOfPageIframe: HTMLElement) {
    stopUnwrappingForUndoAndRedo?.();
    stopUnwrappingForUndoAndRedo = undefined;
    const history = currentPageHistory();
    if (!history) return;
    const wrapAgain = () => {
        if (
            history.isDisposed ||
            !bodyOfPageIframe.classList.contains("bloom-showImageDescriptions")
        )
            return;
        history.doOutsideHistory(() => wrapDescribedImages(bodyOfPageIframe));
    };
    const stopUnwrapping = history.onBeforeUndoOrRedo(() => {
        history.doOutsideHistory(() => unwrapDescribedImages(bodyOfPageIframe));
        // The history calls no onAfterUndoOrRedo listener for an undo or redo that it refuses,
        // so wrap again once this one has finished, too.
        queueMicrotask(wrapAgain);
    });
    const stopWrapping = history.onAfterUndoOrRedo(wrapAgain);
    stopUnwrappingForUndoAndRedo = () => {
        stopUnwrapping();
        stopWrapping();
    };
}

/** True when element has a child with this class. */
function childOfClass(element: Element, className: string): boolean {
    return Array.from(element.children).some((child) =>
        child.classList.contains(className),
    );
}
