// The descriptions of the undo steps that act on a canvas element as a whole: adding, deleting,
// duplicating, moving, nudging, resizing or rotating it, and changing its layer. Each is a
// template with one argument, the kind of element, so that a step can read "Delete Table" in any
// language (UndoDescription in pageHistoryTypes.ts).
//
// Used from the toolbox and from the page frame; keep it dependency-light.

import type { UndoDescription } from "../../pageHistory/pageHistoryTypes";
import { kGameTargetSelector } from "../../js/imageTargetResolution";
import { kBackgroundImageClass } from "./canvasElementConstants";
import {
    getBubbleStyle,
    inferCanvasElementType,
} from "./canvasElementTypeInference";
import type { CanvasElementType } from "./canvasElementTypes";

/** The templates, each with {0} for the kind of element (canvasElementUndoNoun). */
export const kCanvasElementUndoTemplates = {
    add: "EditTab.Undo.Add",
    delete: "EditTab.Undo.Delete",
    // From the menu and by dragging with Alt held.
    duplicate: "EditTab.Undo.Duplicate",
    move: "EditTab.Undo.Move",
    // With the arrow keys.
    nudge: "EditTab.Undo.Nudge",
    resize: "EditTab.Undo.Resize",
    rotate: "EditTab.Undo.Rotate",
    bringForward: "EditTab.Undo.BringForward",
    bringToFront: "EditTab.Undo.BringToFront",
    sendBackward: "EditTab.Undo.SendBackward",
    sendToBack: "EditTab.Undo.SendToBack",
} as const;

export type CanvasElementUndoTemplate =
    keyof typeof kCanvasElementUndoTemplates;

// The nouns that fill the templates, one for each kind of canvas element that the code tells
// apart. They are ids of their own, used only by undo, and not the ids that name these things
// elsewhere in Bloom (a palette item, a menu), on purpose: the translator has to give the form
// of the noun that fits in "Delete {0}" and the other templates, which in many languages is not
// the form that stands alone as a label (an object case, a different article, a lower-case
// initial).
const kNounsByType: Record<CanvasElementType, string> = {
    image: "EditTab.Undo.Object.Image",
    video: "EditTab.Undo.Object.Video",
    sound: "EditTab.Undo.Object.Sound",
    rectangle: "EditTab.Undo.Object.Rectangle",
    speech: "EditTab.Undo.Object.SpeechBubble",
    caption: "EditTab.Undo.Object.Caption",
    table: "EditTab.Undo.Object.Table",
    "book-link-grid": "EditTab.Undo.Object.LinkGrid",
    "navigation-image-button": "EditTab.Undo.Object.ImageButton",
    "navigation-image-with-label-button":
        "EditTab.Undo.Object.ImageWithLabelButton",
    "navigation-label-button": "EditTab.Undo.Object.LabelButton",
    // The Canvas tool's plain text box, whose bubble style is "none".
    none: "EditTab.Undo.Object.TextBox",
};
const kGameTargetNoun = "EditTab.Undo.Object.GameTarget";
const kBackgroundImageNoun = "EditTab.Undo.Object.BackgroundImage";
// For an element whose kind the code cannot tell.
const kCanvasElementNoun = "EditTab.Undo.Object.CanvasElement";

/** Every noun id, for tests. */
export const kCanvasElementUndoNouns: readonly string[] = [
    ...new Set([
        ...Object.values(kNounsByType),
        kGameTargetNoun,
        kBackgroundImageNoun,
        kCanvasElementNoun,
    ]),
];

/** The noun id of the kind of canvas element that element is. */
export function canvasElementUndoNoun(element: HTMLElement): string {
    if (element.matches(kGameTargetSelector)) return kGameTargetNoun;
    if (element.classList.contains(kBackgroundImageClass))
        return kBackgroundImageNoun;
    const type = inferCanvasElementType(element);
    if (!type) return kCanvasElementNoun;
    // inferCanvasElementType calls every text that is not a caption "speech".
    if (type === "speech" && getBubbleStyle(element) === "none")
        return kNounsByType.none;
    return kNounsByType[type];
}

/**
 * The noun id of a kind of canvas element, for a step that begins before the element exists, an
 * add. No type is a speech bubble, as CanvasElementManager.addCanvasElement makes it; the type
 * "none" is a plain text box.
 */
export function canvasElementTypeUndoNoun(
    type: CanvasElementType | undefined,
): string {
    return kNounsByType[type ?? "speech"];
}

/**
 * The description of a step that does template's action to element, or to an element of the
 * given type (for an add, before the element exists).
 */
export function describeCanvasElementStep(
    template: CanvasElementUndoTemplate,
    elementOrType: HTMLElement | CanvasElementType | undefined,
): UndoDescription {
    const noun =
        typeof elementOrType === "object"
            ? canvasElementUndoNoun(elementOrType)
            : canvasElementTypeUndoNoun(elementOrType);
    return { l10nId: kCanvasElementUndoTemplates[template], args: [noun] };
}
