// The z-order (stacking order) of the canvas elements in a bloom-canvas.
//
// Which canvas element paints on top of which is decided purely by DOM order: a later sibling
// covers an earlier one. We deliberately give canvas elements no z-index, so that they do not
// become stacking contexts (see CanvasElementManager.adjustCanvasElementOrdering and the notes
// in basePage.less). ComicalJS draws the bubbles that belong to these elements on its own
// canvas, in the order of the `level` in each element's data-bubble spec, and uses the same
// levels to decide which bubble a click lands on when bubbles overlap. So whenever we move a
// canvas element in the DOM we also renumber the levels to match, keeping the two orderings
// consistent. Comical.update() rebuilds its drawing from the levels, so that is all it takes.
//
// The background image is a canvas element too, but it is not part of the movable stack: it
// stays first, at level 1, and nothing may be sent behind it.
//
// Bubbles that share a level and have an `order` are a ComicalJS "family": a parent bubble
// and its child bubbles, drawn joined together. A family must keep a single level, so the layer
// commands move a whole family as one unit.
//
// In a drag game the draggable pieces are always kept above the fixed ones
// (adjustCanvasElementOrdering re-establishes that whenever a game page is opened), so an
// element only ever moves among the elements of its own kind: a fixed piece cannot be brought
// in front of a draggable, nor a draggable sent behind a fixed piece.

import { Bubble, Comical } from "comicaljs";
import {
    kBackgroundImageClass,
    kCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";
import { isDraggable } from "../../toolbox/canvas/canvasElementDraggables";

// The four layer commands. "forward" and "backward" move one step; "front" and "back" move
// all the way.
export type ZOrderMove = "forward" | "backward" | "front" | "back";

const isBackgroundImage = (canvasElement: HTMLElement): boolean =>
    canvasElement.classList.contains(kBackgroundImageClass);

// The canvas elements that are direct children of the bloom-canvas, in DOM (stacking) order,
// bottom-most first. Includes the background image, if any.
export const getCanvasElementsInZOrder = (
    bloomCanvas: HTMLElement,
): HTMLElement[] =>
    Array.from(bloomCanvas.children).filter(
        (child): child is HTMLElement =>
            child instanceof HTMLElement &&
            child.classList.contains(kCanvasElementClass),
    );

// The canvas elements whose stacking order the user may change: every canvas element in the
// bloom-canvas except the background image. Bottom-most first.
export const getMovableCanvasElements = (
    bloomCanvas: HTMLElement,
): HTMLElement[] =>
    getCanvasElementsInZOrder(bloomCanvas).filter(
        (canvasElement) => !isBackgroundImage(canvasElement),
    );

// The canvas elements that canvasElement can change places with: the movable ones of its own
// kind, draggable game pieces or ordinary elements (see the note at the top of the file).
const getMovableCanvasElementsOfSameKind = (
    canvasElement: HTMLElement,
    bloomCanvas: HTMLElement,
): HTMLElement[] =>
    getMovableCanvasElements(bloomCanvas).filter(
        (other) => isDraggable(other) === isDraggable(canvasElement),
    );

// The level and family order from the element's data-bubble spec, or nothing if it has no spec.
// The attribute holds the spec as JSON with backticks in place of double quotes, which is how
// Bubble.persistBubbleSpec writes it. We read it ourselves rather than through
// Bubble.getBubbleSpec so that working out whether an element can move needs nothing from the
// ComicalJS runtime: this also runs in the toolbox bundle, whose copy of Comical has no active
// canvases (and an element with no spec would make Comical go looking for them).
const readBubbleLevelAndOrder = (
    canvasElement: HTMLElement,
): { level?: number; order?: number } => {
    const escapedJson = canvasElement.getAttribute("data-bubble");
    if (!escapedJson) {
        return {};
    }
    const spec = JSON.parse(escapedJson.replace(/`/g, '"'));
    return { level: spec.level, order: spec.order };
};

// Group canvas elements into the units that move together in the stacking order. A ComicalJS
// family (elements whose bubble specs share a level and have an `order`) is one unit; any other
// element is a unit by itself. Units come back in the DOM order of their first member, and the
// members of a unit keep their DOM order.
export const getZOrderUnits = (
    canvasElements: HTMLElement[],
): HTMLElement[][] => {
    const units: HTMLElement[][] = [];
    const familyUnitsByLevel = new Map<number, HTMLElement[]>();
    canvasElements.forEach((canvasElement) => {
        const spec = readBubbleLevelAndOrder(canvasElement);
        if (!spec.order || !spec.level) {
            units.push([canvasElement]);
            return;
        }
        let family = familyUnitsByLevel.get(spec.level);
        if (!family) {
            family = [];
            familyUnitsByLevel.set(spec.level, family);
            units.push(family);
        }
        family.push(canvasElement);
    });
    return units;
};

// The units canvasElement can change places with, and the index of the unit it belongs to
// (-1 if it is not movable, e.g. the background image).
const getUnitsAndIndex = (
    canvasElement: HTMLElement,
): { units: HTMLElement[][]; index: number } => {
    const bloomCanvas = canvasElement.parentElement;
    if (!bloomCanvas || isBackgroundImage(canvasElement)) {
        return { units: [], index: -1 };
    }
    const units = getZOrderUnits(
        getMovableCanvasElementsOfSameKind(canvasElement, bloomCanvas),
    );
    const index = units.findIndex((unit) => unit.includes(canvasElement));
    return { units, index };
};

// True if "Bring Forward" or "Bring to Front" would change anything for this canvas element:
// it is movable and something it can change places with is stacked above it.
export const canBringCanvasElementForward = (
    canvasElement: HTMLElement,
): boolean => {
    const { units, index } = getUnitsAndIndex(canvasElement);
    return index >= 0 && index < units.length - 1;
};

// True if "Send Backward" or "Send to Back" would change anything for this canvas element:
// it is movable and something it can change places with is stacked below it.
export const canSendCanvasElementBackward = (
    canvasElement: HTMLElement,
): boolean => {
    const { index } = getUnitsAndIndex(canvasElement);
    return index > 0;
};

// Set the level in the element's bubble spec and persist it to data-bubble.
const setBubbleLevel = (canvasElement: HTMLElement, level: number): void => {
    const bubble = new Bubble(canvasElement);
    bubble.getBubbleSpec().level = level;
    bubble.persistBubbleSpec();
};

// Renumber the bubble levels of all the canvas elements in the bloom-canvas so that they agree
// with the DOM order: the background image (if any) gets level 1, and each movable unit above
// it gets the next level up, so that a family keeps sharing one level. Then tells Comical to
// redraw, which also re-establishes which bubble is on top for hit-testing.
// Call this after changing the order of canvas elements in the DOM.
export const syncBubbleLevelsToDomOrder = (bloomCanvas: HTMLElement): void => {
    const canvasElements = getCanvasElementsInZOrder(bloomCanvas);
    const backgroundImages = canvasElements.filter(isBackgroundImage);
    const movable = canvasElements.filter(
        (canvasElement) => !isBackgroundImage(canvasElement),
    );
    // Work out the units from the current levels before we start changing them.
    const units = getZOrderUnits(movable);

    let nextLevel = 1;
    backgroundImages.forEach((backgroundImage) =>
        setBubbleLevel(backgroundImage, nextLevel),
    );
    if (backgroundImages.length > 0) {
        nextLevel++;
    }
    units.forEach((unit) => {
        unit.forEach((canvasElement) =>
            setBubbleLevel(canvasElement, nextLevel),
        );
        nextLevel++;
    });
    Comical.update(bloomCanvas);
};

// Put the members of each family next to each other in the DOM, right after the family's first
// (lowest) member, keeping their order. A child bubble is appended to the end of the canvas when
// it is created, so a family whose parent was made before some other element straddles that
// element in the DOM; Comical nevertheless draws the whole family at the parent's level, below
// that element. Gathering the family makes the DOM agree with Comical, and means a layer move
// past the family is exactly one step. Only family members (text bubbles) ever move here.
const gatherFamilies = (bloomCanvas: HTMLElement, units: HTMLElement[][]) => {
    units.forEach((unit) => {
        let anchor = unit[0];
        unit.slice(1).forEach((member) => {
            bloomCanvas.insertBefore(member, anchor.nextSibling);
            anchor = member;
        });
    });
};

// Move the canvas element (together with the rest of its ComicalJS family, if it has one) in
// the stacking order, then bring the bubble levels back into line with the DOM order.
// Returns false, having changed nothing, if the move is not possible: the element is the
// background image, or is already at the end the move would take it to.
export const moveCanvasElementInZOrder = (
    canvasElement: HTMLElement,
    move: ZOrderMove,
): boolean => {
    const { units, index } = getUnitsAndIndex(canvasElement);
    if (index < 0) {
        return false;
    }
    let targetIndex: number;
    switch (move) {
        case "forward":
            targetIndex = index + 1;
            break;
        case "backward":
            targetIndex = index - 1;
            break;
        case "front":
            targetIndex = units.length - 1;
            break;
        case "back":
            targetIndex = 0;
            break;
    }
    if (
        targetIndex === index ||
        targetIndex < 0 ||
        targetIndex >= units.length
    ) {
        return false;
    }

    const bloomCanvas = canvasElement.parentElement as HTMLElement;
    gatherFamilies(bloomCanvas, units);

    // Every unit is now contiguous, so the place to land is just past the unit we are moving
    // to. Only the moving unit's members change place (moving a node detaches and re-attaches
    // it, which e.g. resets a playing video, so we leave everything else alone). Inserting each
    // member before one fixed marker node keeps the members in their order.
    const targetUnit = units[targetIndex];
    const marker: Node | null =
        targetIndex > index
            ? targetUnit[targetUnit.length - 1].nextSibling
            : targetUnit[0];
    units[index].forEach((member) => bloomCanvas.insertBefore(member, marker));

    syncBubbleLevelsToDomOrder(bloomCanvas);
    return true;
};
