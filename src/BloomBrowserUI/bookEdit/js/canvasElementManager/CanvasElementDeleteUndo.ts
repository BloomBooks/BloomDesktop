// Undo and redo for deleting a canvas element (BL-17003; docs/retire-ckeditor/PLAN.md Stage 2b).
//
// The entry reverses what the delete did, applied to the canvas as it is when the user undoes, not
// as it was when they deleted. Other operations rewrite the other elements' bubble data (adding an
// element at the bottom renumbers every level, for instance), so restoring a saved copy of it would
// put back values those operations set.
//
// This module does the parts that are plain DOM and data-bubble work: where the element goes back
// in the stacking order, its level, and its place in its comic family. It reads and writes the
// data-bubble attribute itself rather than through comicaljs's Bubble, so it can be unit-tested
// without paper.js. Everything that needs Comical or the editing machinery (refreshing the canvas,
// source bubbles, the tidy-up of the ordering rules) is the host's job: CanvasElementManager.

import { BubbleSpec, TailSpec } from "comicaljs";
import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";
import { kDraggableIdAttribute } from "../../toolbox/canvas/canvasElementDraggables";
import { IUndoEntry } from "../../undo/undoTypes";

/** What the entry needs from CanvasElementManager. */
export interface ICanvasElementDeletionHost {
    /**
     * Take the element off the page, doing everything a delete does: the comic family, the
     * editing UI, any drag-activity target, the cover image designation.
     */
    removeCanvasElement(element: HTMLElement): void;

    /**
     * Finish putting back an element this module has re-inserted and given its bubble data:
     * enforce the ordering rules, rebuild its source bubbles, refresh Comical and the editing UI.
     */
    finishRestoringCanvasElement(
        element: HTMLElement,
        canvas: HTMLElement,
    ): void;
}

/**
 * What a deletion changed, captured just before it happens.
 *
 * It holds elements rather than data that would survive a reload, which PLAN.md 4.1 prefers:
 * canvas elements carry no ids to find them by, and holding the very objects is what lets a chain
 * of undos work. Undoing a later delete re-inserts the same element that an earlier delete's
 * record names as a neighbour.
 */
export interface ICanvasElementDeletion {
    /** The .bloom-canvas the element was in. */
    canvas: HTMLElement;
    /** The canvas element directly below it in the stacking order, if any. */
    below?: HTMLElement;
    /** The canvas element directly above it in the stacking order, if any. */
    above?: HTMLElement;
    /** The member of its comic family just before it in family order, if any. */
    familyBefore?: HTMLElement;
    /** The member of its comic family just after it in family order, if any. */
    familyAfter?: HTMLElement;
    /**
     * When the element heads its family, the tails of the member that will take over as head.
     * deleteBubbleFromFamily overwrites them with the old head's, so an undo needs them to give
     * that member back its own.
     */
    successorTails?: TailSpec[];
    /** The drag-activity target that goes with a draggable element, if any. */
    target?: HTMLElement;
}

/** The label of the undo entry. Not localized: it is for logging and tests. */
export const kDeleteCanvasElementLabel = "Delete canvas element";

/**
 * Make the undo entry for deleting `element`, then delete it through the host. The caller pushes
 * the entry (inside a runUndoable scope).
 */
export function deleteCanvasElementUndoably(
    element: HTMLElement,
    host: ICanvasElementDeletionHost,
): IUndoEntry {
    let deletion = captureCanvasElementDeletion(element);
    host.removeCanvasElement(element);
    return {
        label: kDeleteCanvasElementLabel,
        kind: "custom",
        undo: () => {
            restoreDeletedCanvasElement(element, deletion);
            host.finishRestoringCanvasElement(element, deletion.canvas);
        },
        redo: () => {
            // The neighbours and the family may differ from the first time; whatever the next
            // undo needs is what they are now.
            deletion = captureCanvasElementDeletion(element);
            host.removeCanvasElement(element);
        },
    };
}

/** Record what deleting `element` will change. Call it before the element is removed. */
export function captureCanvasElementDeletion(
    element: HTMLElement,
): ICanvasElementDeletion {
    const canvas = element.parentElement as HTMLElement;
    const siblings = canvasElementsIn(canvas);
    const index = siblings.indexOf(element);
    const deletion: ICanvasElementDeletion = {
        canvas,
        below: siblings[index - 1],
        above: siblings[index + 1],
    };

    const spec = readBubbleSpec(element);
    if (spec.order) {
        const family = familyMembers(canvas, spec.level).filter(
            (member) => member !== element,
        );
        deletion.familyBefore = family
            .filter((member) => readBubbleSpec(member).order! < spec.order!)
            .pop();
        deletion.familyAfter = family.find(
            (member) => readBubbleSpec(member).order! > spec.order!,
        );
        if (spec.order === 1 && deletion.familyAfter) {
            deletion.successorTails = readBubbleSpec(
                deletion.familyAfter,
            ).tails;
        }
    }

    const draggableId = element.getAttribute(kDraggableIdAttribute);
    if (draggableId) {
        deletion.target =
            (canvas.querySelector(
                `[data-target-of="${draggableId}"]`,
            ) as HTMLElement) ?? undefined;
    }
    return deletion;
}

/**
 * Put a deleted element back on its canvas, with its bubble data made consistent with the canvas
 * as it is now. The host still has to finish the job (see ICanvasElementDeletionHost).
 *
 * Throws, so that the undo stack discards itself, only when the restore cannot work at all.
 * Anything else that has changed since the delete, it works around.
 */
export function restoreDeletedCanvasElement(
    element: HTMLElement,
    deletion: ICanvasElementDeletion,
): void {
    const canvas = deletion.canvas;
    if (!canvas.isConnected) {
        throw new Error(
            "Cannot undo deleting a canvas element: its canvas is no longer on the page.",
        );
    }
    if (element.isConnected) {
        throw new Error(
            "Cannot undo deleting a canvas element: it is already on the page.",
        );
    }

    insertAtOldPlace(element, deletion);

    const spec = readBubbleSpec(element);
    const rejoin = findPlaceInFamily(deletion);
    if (rejoin) {
        rejoinFamily(element, spec, rejoin, deletion);
    } else {
        delete spec.order;
        spec.level = makeRoomForLevelAt(element);
        writeBubbleSpec(element, spec);
    }

    // removeDetachedTargets takes a draggable's target away with it; it goes back at the end of
    // the canvas, where makeTargetForDraggable puts a new one.
    if (deletion.target && !deletion.target.isConnected) {
        canvas.appendChild(deletion.target);
    }
}

/**
 * Re-insert `element` just below its old upper neighbour, or failing that just above its old lower
 * one, or failing both on top of everything.
 */
function insertAtOldPlace(
    element: HTMLElement,
    deletion: ICanvasElementDeletion,
): void {
    const canvas = deletion.canvas;
    if (deletion.above?.parentElement === canvas) {
        canvas.insertBefore(element, deletion.above);
        return;
    }
    if (deletion.below?.parentElement === canvas) {
        deletion.below.after(element);
        return;
    }
    const top = canvasElementsIn(canvas).pop();
    if (top) {
        top.after(element);
    } else {
        canvas.appendChild(element);
    }
}

/**
 * Give a re-inserted element that is not rejoining a family a level that matches where it now is:
 * one above the canvas element below it, with every element at or above that level moved up one.
 * That is the same bump putBubbleBefore does, so everything else keeps its relative order and a
 * family keeps sharing one level.
 */
function makeRoomForLevelAt(element: HTMLElement): number {
    const siblings = canvasElementsIn(element.parentElement as HTMLElement);
    const below = siblings[siblings.indexOf(element) - 1];
    const level = below ? readBubbleSpec(below).level! + 1 : 1;
    siblings
        .filter((other) => other !== element)
        .forEach((other) => {
            const spec = readBubbleSpec(other);
            if (spec.level! >= level) {
                spec.level = spec.level! + 1;
                writeBubbleSpec(other, spec);
            }
        });
    return level;
}

/**
 * Where in its comic family the element goes back: just after the member that was before it, or
 * failing that just before the one that was after it. Undefined when neither is still in a family
 * on the canvas, and the element comes back on its own.
 */
function findPlaceInFamily(
    deletion: ICanvasElementDeletion,
): { level: number; order: number } | undefined {
    const inAFamily = (member: HTMLElement | undefined) =>
        member?.parentElement === deletion.canvas &&
        !!readBubbleSpec(member).order;
    if (inAFamily(deletion.familyBefore)) {
        const spec = readBubbleSpec(deletion.familyBefore!);
        return { level: spec.level!, order: spec.order! + 1 };
    }
    if (inAFamily(deletion.familyAfter)) {
        const spec = readBubbleSpec(deletion.familyAfter!);
        return { level: spec.level!, order: spec.order! };
    }
    return undefined;
}

/**
 * The inverse of Comical.deleteBubbleFromFamily, applied to the family as it is now: every member
 * from `place.order` on moves up one, and the element takes that order at the family's level. When
 * it becomes the head again, it takes the current head's spec (which holds the family-wide
 * settings, as they are now) with its own tails, and the member it displaces gets its own tails
 * back.
 */
function rejoinFamily(
    element: HTMLElement,
    spec: BubbleSpec,
    place: { level: number; order: number },
    deletion: ICanvasElementDeletion,
): void {
    const family = familyMembers(deletion.canvas, place.level).filter(
        (member) => member !== element,
    );
    const currentHead = family.find(
        (member) => readBubbleSpec(member).order === 1,
    );
    let restoredSpec: BubbleSpec = { ...spec, level: place.level };
    if (place.order === 1 && currentHead) {
        restoredSpec = { ...readBubbleSpec(currentHead), tails: spec.tails };
    }
    restoredSpec.order = place.order;

    family.forEach((member) => {
        const memberSpec = readBubbleSpec(member);
        if (memberSpec.order! < place.order) {
            return;
        }
        memberSpec.order = memberSpec.order! + 1;
        if (
            member === currentHead &&
            place.order === 1 &&
            member === deletion.familyAfter &&
            deletion.successorTails
        ) {
            memberSpec.tails = deletion.successorTails;
        }
        writeBubbleSpec(member, memberSpec);
    });
    writeBubbleSpec(element, restoredSpec);
}

/** The canvas elements that are direct children of `canvas`, bottom of the stack first. */
function canvasElementsIn(canvas: HTMLElement): HTMLElement[] {
    return Array.from(canvas.children).filter((child) =>
        child.classList.contains(kCanvasElementClass),
    ) as HTMLElement[];
}

/** The members of the comic family at `level` on `canvas`, in family order. */
function familyMembers(canvas: HTMLElement, level: number): HTMLElement[] {
    return canvasElementsIn(canvas)
        .filter((member) => {
            const spec = readBubbleSpec(member);
            return spec.level === level && !!spec.order;
        })
        .sort((a, b) => readBubbleSpec(a).order! - readBubbleSpec(b).order!);
}

/**
 * The element's bubble spec, parsed from its data-bubble attribute the way comicaljs's
 * Bubble.getBubbleSpec does (it stores JSON with backquotes for double quotes).
 */
function readBubbleSpec(element: HTMLElement): BubbleSpec {
    const escapedJson = element.getAttribute("data-bubble");
    if (!escapedJson) {
        throw new Error(
            "A canvas element has no data-bubble; Bloom gives every one a bubble spec.",
        );
    }
    return JSON.parse(escapedJson.replace(/`/g, '"')) as BubbleSpec;
}

/** Store `spec` in the element's data-bubble the way Bubble.persistBubbleSpec does. */
function writeBubbleSpec(element: HTMLElement, spec: BubbleSpec): void {
    element.setAttribute(
        "data-bubble",
        JSON.stringify(spec).replace(/"/g, "`"),
    );
}
