// Undo for deleting a canvas element (BL-6681, PLAN.md 6 Stage 2b) — the page-frame half.
//
// The shape is an inverse operation on a narrow subtree, not a page snapshot. Deleting a canvas
// element removes one child of a `.bloom-canvas`; putting it back is re-inserting that child at
// the same index and running the same setup that adding or duplicating an element runs
// (`refreshCanvasElementEditing`). Two things make it more than `insertBefore`, and both were
// flagged in the plan before this was written:
//
// 1. **Comical renumbers the family.** `Comical.deleteBubbleFromFamily` decrements the `order` of
//    every later relative, and if the patriarch (order 1) was deleted it copies the patriarch's spec
//    onto the new first member. So re-inserting the element with its own old spec would leave two
//    bubbles claiming one `order`, and the family styling on the wrong member. The record therefore
//    carries the `data-bubble` of EVERY canvas element in that canvas as it was before the deletion,
//    and restore puts them all back before Comical looks again.
// 2. **A drag-activity target dies with its draggable.** `removeDetachedTargets` removes the
//    `[data-target-of=<id>]` element once nothing carries that id. The record carries the target's
//    markup and where it sat, so restore brings it back too.
//
// Everything in the record is data — strings, indices — never a DOM node or a closure, because the
// entry that holds it lives in the workspace frame and outlives this document (see undoTypes.ts).
// Positions are structural (nth canvas on the page, nth element in the canvas) for the same reason
// `ISelectionAnchor` is: ordinary canvas elements have no ids, and after any restore the element
// object is new anyway.
//
// Redo is "delete it again" through the very same `deleteCanvasElement`, with recording suppressed
// so the redo does not push a second entry.

import { Bubble, Comical } from "comicaljs";
import {
    getCanvasElementManager,
    kBloomCanvasClass,
    kCanvasElementClass,
} from "../toolbox/canvas/canvasElementPageBridge";
import { kDraggableIdAttribute } from "../toolbox/canvas/canvasElementDraggables";
import { tryGetWorkspaceBundleExports } from "../js/workspaceFrames";

/** Everything needed to put a deleted canvas element back, or delete it again. Pure data. */
export interface ICanvasElementDeletionRecord {
    /** The page the element was on; the entry is scoped to it. */
    pageId: string;
    /** Which `.bloom-canvas` on the page held it, in document order. */
    canvasIndex: number;
    /** Where it sat among that canvas's canvas elements, in document order. */
    elementIndex: number;
    /** The element itself, as `outerHTML`. */
    elementHtml: string;
    /**
     * The `data-bubble` attribute of every canvas element in that canvas, in document order, as
     * they were BEFORE the deletion (the deleted element's own spec included, at `elementIndex`).
     * `null` where an element had none.
     */
    bubbleSpecs: (string | null)[];
    /** The drag-activity target linked to the element, if it had one. */
    target?: {
        html: string;
        /** Child indexes from the `.bloom-page` element down to the target's parent. */
        parentPath: number[];
        /** The target's index among its parent's children. */
        index: number;
    };
}

// True while redeleteCanvasElement is running deleteCanvasElement, so the deletion it performs is
// not recorded as a new entry on top of the one being redone.
let suppressRecording = false;

/** The canvas elements that are direct children of `canvas`, in document order. */
export function canvasElementsOf(canvas: Element): HTMLElement[] {
    return Array.from(canvas.children).filter((child) =>
        child.classList.contains(kCanvasElementClass),
    ) as HTMLElement[];
}

/** Child indexes from `root` down to `element` (exclusive of root). Undefined if not a descendant. */
export function pathWithin(
    root: Element,
    element: Element,
): number[] | undefined {
    const path: number[] = [];
    let current: Element | null = element;
    while (current && current !== root) {
        const parent: Element | null = current.parentElement;
        if (!parent) {
            return undefined;
        }
        path.unshift(Array.from(parent.children).indexOf(current));
        current = parent;
    }
    return current === root ? path : undefined;
}

/** The element at `path` below `root`, or undefined if the structure no longer matches. */
export function resolvePathWithin(
    root: Element,
    path: number[],
): Element | undefined {
    let current: Element | undefined = root;
    for (const index of path) {
        current = current?.children[index];
    }
    return current;
}

/**
 * Describe `element` well enough to bring it back, before it is deleted. Returns undefined if the
 * element is not a canvas element on a page (nothing sensible to record).
 */
export function captureCanvasElementDeletion(
    element: HTMLElement,
): ICanvasElementDeletionRecord | undefined {
    const canvas = element.parentElement;
    const page = element.closest(".bloom-page") as HTMLElement | null;
    if (
        !canvas ||
        !page ||
        !page.id ||
        !canvas.classList.contains(kBloomCanvasClass)
    ) {
        return undefined;
    }
    const canvases = Array.from(page.getElementsByClassName(kBloomCanvasClass));
    const siblings = canvasElementsOf(canvas);
    const record: ICanvasElementDeletionRecord = {
        pageId: page.id,
        canvasIndex: canvases.indexOf(canvas),
        elementIndex: siblings.indexOf(element),
        elementHtml: element.outerHTML,
        bubbleSpecs: siblings.map((sibling) =>
            sibling.getAttribute("data-bubble"),
        ),
    };
    const draggableId = element.getAttribute(kDraggableIdAttribute);
    if (draggableId) {
        const target = page.querySelector(`[data-target-of="${draggableId}"]`);
        const parent = target?.parentElement;
        if (target && parent) {
            const parentPath = pathWithin(page, parent);
            if (parentPath) {
                record.target = {
                    html: target.outerHTML,
                    parentPath,
                    index: Array.from(parent.children).indexOf(target),
                };
            }
        }
    }
    return record;
}

/**
 * Record the deletion of `element` on the one undo stack. Call this from `deleteCanvasElement`
 * just before the element is removed — the record has to be made while the element and its
 * family are still intact. A no-op during a redo, and when there is no workspace frame (the
 * off-screen page-processing context).
 */
export function recordCanvasElementDeletionForUndo(element: HTMLElement): void {
    if (suppressRecording) {
        return;
    }
    const record = captureCanvasElementDeletion(element);
    if (!record) {
        return;
    }
    tryGetWorkspaceBundleExports()?.recordCanvasElementDeletion(record);
}

function pageFor(record: ICanvasElementDeletionRecord): HTMLElement {
    const page = document.querySelector(".bloom-page") as HTMLElement | null;
    if (!page || page.id !== record.pageId) {
        throw new Error(
            `Cannot restore a canvas element deleted from page ${record.pageId}: that page is not showing`,
        );
    }
    return page;
}

function canvasFor(
    page: HTMLElement,
    record: ICanvasElementDeletionRecord,
): HTMLElement {
    const canvas = page.getElementsByClassName(kBloomCanvasClass)[
        record.canvasIndex
    ] as HTMLElement | undefined;
    if (!canvas) {
        throw new Error(
            `Cannot restore a canvas element: the page no longer has a canvas at index ${record.canvasIndex}`,
        );
    }
    return canvas;
}

function elementFromHtml(html: string): HTMLElement {
    const template = document.createElement("template");
    template.innerHTML = html;
    const element = template.content.firstElementChild as HTMLElement | null;
    if (!element) {
        throw new Error(
            "Cannot restore a canvas element: its saved markup is empty",
        );
    }
    return element;
}

/**
 * Put a deleted canvas element back where it was, with its family's bubble specs and its
 * drag-activity target, and run the same setup a newly added element gets. Called by the undo
 * entry in the workspace frame, across the frame boundary. Throws if the page has changed under
 * it; the stack treats that as a failed undo and leaves the entry in place.
 */
export function restoreDeletedCanvasElement(
    record: ICanvasElementDeletionRecord,
): void {
    const page = pageFor(record);
    const canvas = canvasFor(page, record);
    const element = elementFromHtml(record.elementHtml);
    const siblingsBefore = canvasElementsOf(canvas);
    canvas.insertBefore(element, siblingsBefore[record.elementIndex] ?? null);

    // Undo Comical's renumbering of the family (see the header comment). The elements are back
    // in their original order now, so the saved specs line up with them one to one.
    const siblings = canvasElementsOf(canvas);
    if (siblings.length === record.bubbleSpecs.length) {
        siblings.forEach((sibling, i) => {
            const spec = record.bubbleSpecs[i];
            if (spec === null) {
                sibling.removeAttribute("data-bubble");
            } else {
                sibling.setAttribute("data-bubble", spec);
            }
        });
    }
    // else: the canvas has gained or lost elements since; leave the others' specs alone rather
    // than guess, and let Comical make what it can of the restored one.

    if (record.target) {
        const parent = resolvePathWithin(page, record.target.parentPath);
        if (parent) {
            parent.insertBefore(
                elementFromHtml(record.target.html),
                parent.children[record.target.index] ?? null,
            );
        }
    }

    Comical.update(canvas);
    // The same path adding or duplicating a canvas element takes: Comical, event handlers on the
    // new editables, SetupElements over the canvas, and making the element active.
    getCanvasElementManager()?.refreshCanvasElementEditing(
        canvas,
        new Bubble(element),
        true,
        true,
    );
}

/**
 * Delete the element again (redo), through the normal deletion path but without recording a
 * second undo entry for it. Throws if the page has changed under it.
 */
export function redeleteCanvasElement(
    record: ICanvasElementDeletionRecord,
): void {
    const page = pageFor(record);
    const canvas = canvasFor(page, record);
    const element = canvasElementsOf(canvas)[record.elementIndex];
    if (!element) {
        throw new Error(
            `Cannot redo deleting a canvas element: nothing at index ${record.elementIndex} of canvas ${record.canvasIndex}`,
        );
    }
    const manager = getCanvasElementManager();
    if (!manager) {
        throw new Error(
            "Cannot redo deleting a canvas element: no canvas element manager",
        );
    }
    suppressRecording = true;
    try {
        manager.deleteCanvasElement(element);
    } finally {
        suppressRecording = false;
    }
}
