// Adapters that let Bloom's four pre-existing undo mechanisms take part in the one stack
// (BL-6681, PLAN.md 6 Stage 1).
//
// This is deliberately a wrapping exercise and not a conversion. Registering these in the order
// below reproduces exactly what workspaceRoot.handleUndo() / canUndo() did before the stack
// existed, so the stack can become the single entry point with no behaviour change at all.
//
// Know what that order governs. handleUndo() has one caller: the toolbar Undo button. Ctrl+Z is
// claimed in the *page* frame — by origami in Change Layout mode, by the reader tools in any
// editable while a markup tool is active (they preventDefault), and otherwise by CKEditor — and
// never enters handleUndo() at all. So this file unifies the button, and the keyboard stays as it
// was until those page-frame handlers are converted. See PLAN.md 3's correction.
//
// Why no conversions yet: the four mechanisms are contextually exclusive in practice (origami only
// in Change Layout mode, the reader undo only with an active markup tool, image undo only on an
// image container), so their relative ordering only starts to matter once text edits share the
// stack, which is Stage 3. Converting them now would mean maturing the riskiest new machinery —
// in-place snapshot restore — on a page full of live CKEditor instances.
//
// Each function here disappears when its mechanism is converted (Stages 3 and 4), and the last one
// out takes this file with it.

import {
    getEditablePageBundleExports,
    getToolboxBundleExports,
} from "../js/workspaceFrames";
import { theOneUndoStack, UndoStack } from "./UndoStack";
import { ILegacyUndoProvider } from "./undoTypes";

/**
 * Origami's own stack of jQuery `clone(true)` copies of `.marginBox`.
 *
 * Only ever has anything while Change Layout mode is active. It also has its own Ctrl+Z/Ctrl+Y
 * handler bound to `html`, and its own Redo, both of which keep working independently until it is
 * converted — which must happen in a single commit with retiring that handler, or its Redo breaks
 * in between.
 */
export const origamiUndoProvider: ILegacyUndoProvider = {
    name: "origami",
    canUndo: () => !!getEditablePageBundleExports()?.origamiCanUndo(),
    undo: () => getEditablePageBundleExports()?.origamiUndo(),
};

/**
 * The toolbox's per-editable text-typing undo (`readerToolsModel`).
 *
 * Despite living in the toolbox, this is a text undo, not a "reader setup" undo: it snapshots
 * `{html, text, caretOffset}` per editable and pushes on markup-changing keystrokes. It is
 * consulted *before* CKEditor on purpose — while a reader tool is active it must shadow CKEditor's
 * undo, which would otherwise restore stale decodable/leveled markup.
 *
 * It is also the one legacy mechanism with a Redo (`readerToolsModel.redo`), reached by
 * Ctrl+Y/Ctrl+Shift+Z from the reader tools' own per-editable handler. The shared stack does not
 * offer it: that handler keeps working, and this adapter covers only the button's undo.
 *
 * `canUndo` is called through an existence check because the old `canUndo()` did the same, and it
 * is polled on a timer by C#: a throw here would fire repeatedly. `undo` is not, also matching the
 * old code, since it only runs just after `canUndo()` returned true.
 */
export const toolboxUndoProvider: ILegacyUndoProvider = {
    name: "toolbox",
    canUndo: () => {
        const toolbox = getToolboxBundleExports();
        return !!toolbox?.canUndo?.();
    },
    undo: () => getToolboxBundleExports()?.undo(),
};

/**
 * `ImageUndoManager` — restores an image's src, copyright or crop.
 *
 * The cleanest of the four: a two-phase prepare/commit, already scoped by page id, and gated on
 * the active element being an image container. A good candidate to convert early, since it depends
 * on nothing in Stage 3.
 */
export const imageUndoProvider: ILegacyUndoProvider = {
    name: "image",
    canUndo: () => !!getEditablePageBundleExports()?.imageOperationCanUndo(),
    undo: () => {
        getEditablePageBundleExports()?.imageOperationUndo();
    },
};

/**
 * CKEditor's own per-editable undo manager — the mechanism this whole project exists to replace.
 *
 * Its ordering across boxes is already wrong (each editable has its own stack, so undo follows
 * focus rather than time). We reproduce that rather than fix it: fixing it here would be a
 * behaviour change in the commit whose whole value is being behaviour-neutral, and Stage 3 removes
 * the mechanism.
 */
export const ckeditorUndoProvider: ILegacyUndoProvider = {
    name: "ckeditor",
    canUndo: () => !!getEditablePageBundleExports()?.ckeditorCanUndo(),
    undo: () => getEditablePageBundleExports()?.ckeditorUndo(),
};

/**
 * Register all four, in the order `workspaceRoot.handleUndo()` used.
 *
 * Call once, as the workspace frame sets up. Registering twice would double-consult each
 * mechanism, which is harmless for `canUndo` but would be confusing, so callers should not.
 *
 * @param stack defaults to the one real stack; a parameter only so tests need not use a singleton.
 */
export function registerLegacyUndoProviders(
    stack: UndoStack = theOneUndoStack,
): void {
    stack.registerLegacyProvider(origamiUndoProvider);
    stack.registerLegacyProvider(toolboxUndoProvider);
    stack.registerLegacyProvider(imageUndoProvider);
    stack.registerLegacyProvider(ckeditorUndoProvider);
}
