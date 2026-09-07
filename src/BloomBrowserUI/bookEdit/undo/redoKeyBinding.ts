// Ctrl+Y in the page frame reaches the one undo stack's Redo (BL-6681).
//
// Why the binding is in the PAGE frame although the stack lives in the workspace frame: keyboard
// events inside the page iframe are delivered to that iframe's document and never reach the parent,
// so a workspace-frame handler would fire only when focus is outside the page -- which is the
// opposite of when Redo is wanted. This is also why both pre-existing Ctrl+Y handlers are in the
// page frame: origami's on `html` (origami.ts) and the reader tools' on each editable
// (decodableReaderTool.tsx).
//
// Why it is the LAST resort and not the first: those two handlers claim the keystroke where they
// apply (the reader tools' one returns false, which stops propagation; origami's fires but does not
// prevent the default), and CKEditor's own redo command handles Ctrl+Y inside a box with anything
// on its per-box stack. Until those mechanisms are converted (Stages 3-4) they must keep winning.
// So this handler sits at the document, in the bubble phase, and acts only when nothing earlier
// claimed the event AND the shared stack actually has something to redo. When it does not, the
// keystroke falls through untouched to whatever would have handled it before this existed.
//
// One case defaultPrevented cannot catch: origami's handler runs origamiRedo() without claiming
// the event, so in Change Layout mode a Ctrl+Y that reaches us with something on the shared stack
// would redo twice. We therefore also stand down whenever the page is in Change Layout mode, which
// origami itself signals with the `origami-layout-mode` class on `.marginBox`. This goes away when
// Stage 4 converts origami's undo onto the shared stack and retires its handler.
//
// There is no Redo button and no C# involvement: Redo is JS-only by decision (PLAN.md 10).

/** The part of the workspace bundle this binding needs. Kept small so a test can fake it. */
export interface IRedoTarget {
    canRedo(): boolean;
    handleRedo(): void;
}

/** Whether this keydown is the Redo gesture: Ctrl+Y with no other modifier. */
export function isRedoKeystroke(e: KeyboardEvent): boolean {
    return (
        e.ctrlKey &&
        !e.altKey &&
        !e.metaKey &&
        !e.shiftKey &&
        (e.key === "y" || e.key === "Y")
    );
}

/** Whether the page is in Change Layout mode, where origami owns Ctrl+Z and Ctrl+Y. */
export function isInChangeLayoutMode(doc: Document): boolean {
    return !!doc.querySelector(".marginBox.origami-layout-mode");
}

/**
 * Listen for Ctrl+Y on `doc` and redo through the workspace bundle when it has something to redo.
 *
 * `getTarget` is called per keystroke rather than once, because the workspace bundle is reached
 * across frames and may legitimately be absent (the off-screen page-processing context loads a
 * page with no workspace root). A null target means "do nothing", not an error.
 */
export function installRedoKeyBinding(
    doc: Document,
    getTarget: () => IRedoTarget | null,
): void {
    doc.addEventListener("keydown", (e: KeyboardEvent) => {
        if (!isRedoKeystroke(e)) {
            return;
        }
        // Someone earlier in the bubble already claimed this keystroke (e.g. the reader tools'
        // handler, when a markup tool is active). Not ours.
        if (e.defaultPrevented) {
            return;
        }
        // Origami claims Ctrl+Y in Change Layout mode without preventing the default; see above.
        if (isInChangeLayoutMode(doc)) {
            return;
        }
        const target = getTarget();
        if (!target?.canRedo()) {
            // Nothing of ours to redo: leave the keystroke to CKEditor's redo or the browser's.
            return;
        }
        e.preventDefault();
        target.handleRedo();
    });
}
