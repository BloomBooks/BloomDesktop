// The undo and redo keys of the Edit tab: Ctrl+Z undoes, Ctrl+Y and Ctrl+Shift+Z redo
// (docs/undo/MINIMUM.md item 10, docs/undo/APPROACHES.md section 8.2).
//
// In the page frame the keys are taken in the capture phase of the window's keydown, so that no
// handler in the page's document sees them: not CKEditor's, not the reader tools', and not the
// browser's own undo of a contenteditable. The one exception is Change Layout mode, which keeps its
// own undo: there the keys go on to origami's handler on `html` untouched. The toolbox frame and the
// root window (the top bar and dialogs) pass the same keys on to the same commands, except from a
// text field, a dialog or an open list (isInTextFieldOrDialog), where the browser's own undo of that
// field is the one the user means. A checkbox, a radio, a button or a select is not a text field:
// from one of these the keys undo and redo the page. The page frame leaves the keys to a text
// input, a textarea, a dialog (including the Format dialog) or an open list in the same way, but
// not to the page's text boxes.
//
// Two more things keep the keys from reaching what would undo or redo something else:
// - `beforeinput` with historyUndo or historyRedo (the browser's undo of a contenteditable, as from
//   its context menu) is cancelled in the page frame, except in an input, a textarea or a dialog.
// - The keyup events that follow an undo or redo key, up to the release of the last modifier, stop
//   at the page window. On keyup the toolbox schedules its markup pass (pageEditingMarkup.ts),
//   which rewrites the box half a second later; that change would join the step before and clear
//   redo (APPROACHES.md section 3.0.2, rule 8, and section 5.3).

/** What the keys do. */
export interface UndoAndRedoCommands {
    undo(): void;
    redo(): void;
    /** True while origami's Change Layout mode is on; its own handler then takes the keys. */
    isInChangeLayoutMode(): boolean;
}

/**
 * Listen for the undo and redo keys in pageWindow, the frame that holds the page, and in
 * otherWindows. Returns a function that removes the listeners.
 *
 * Attach this after attachInputTransactions (pageHistoryInput.ts) for the same windows. Listeners
 * on one target run in the order they were added, and the input module's keydown listener ends the
 * typing step that an undo or redo key follows; the history's undo() does nothing while that step's
 * transaction is open.
 */
export function attachUndoAndRedoKeys(
    pageWindow: Window,
    otherWindows: Window[],
    commands: UndoAndRedoCommands,
): () => void {
    const detachers: (() => void)[] = [];
    const listen = (
        target: Window,
        type: string,
        listener: (event: Event) => void,
    ) => {
        target.addEventListener(type, listener, true);
        detachers.push(() => target.removeEventListener(type, listener, true));
    };

    // True from an undo or redo keydown in the page frame until no modifier is down, or until the
    // page window loses the focus, after which the release of the modifiers goes elsewhere.
    let swallowingKeyUps = false;

    listen(pageWindow, "keydown", (event) => {
        const keyboardEvent = event as KeyboardEvent;
        // A keydown's modifier flags say what is down; with none, the keyups of an earlier undo
        // or redo key are over, even if the last of them went to another window.
        if (!hasUndoModifier(keyboardEvent)) swallowingKeyUps = false;
        const command = undoOrRedoCommandOf(keyboardEvent);
        if (!command || commands.isInChangeLayoutMode()) return;
        // A text input or textarea on the page, such as a calendar page's, or a dialog shown in
        // the page frame, has its own undo, as in the other windows. The page's text boxes do
        // not. The Format dialog and an open list have no undo of their own, but the page must
        // not change under them, so the keys are left alone there too.
        if (isInTextFieldOrDialog(event.target, false)) return;
        event.preventDefault();
        event.stopPropagation();
        swallowingKeyUps = true;
        runCommand(command, commands);
    });
    listen(pageWindow, "keyup", (event) => {
        if (!swallowingKeyUps) return;
        event.stopPropagation();
        // A keyup's modifier flags say what is still down after this key went up.
        if (!hasUndoModifier(event as KeyboardEvent)) swallowingKeyUps = false;
    });
    listen(pageWindow, "blur", (event) => {
        // An element's blur reaches here too; the window's own has no node as its target.
        if (typeof (event.target as Node | null)?.nodeType !== "number")
            swallowingKeyUps = false;
    });
    listen(pageWindow, "beforeinput", (event) => {
        const inputType = (event as InputEvent).inputType;
        if (inputType !== "historyUndo" && inputType !== "historyRedo") return;
        // The browser's own undo of a field or a dialog is the one that the keys leave to it.
        if (isInTextFieldOrDialog(event.target, false)) return;
        event.preventDefault();
    });

    for (const otherWindow of otherWindows) {
        listen(otherWindow, "keydown", (event) => {
            const keyboardEvent = event as KeyboardEvent;
            const command = undoOrRedoCommandOf(keyboardEvent);
            if (!command || isInTextFieldOrDialog(event.target, true)) return;
            event.preventDefault();
            event.stopPropagation();
            runCommand(command, commands);
        });
    }

    return () => detachers.forEach((detach) => detach());
}

function runCommand(
    command: "undo" | "redo",
    commands: UndoAndRedoCommands,
): void {
    if (command === "undo") commands.undo();
    else commands.redo();
}

/**
 * "undo" for Ctrl+Z, "redo" for Ctrl+Y and Ctrl+Shift+Z, the same with the Mac's Command key, and
 * undefined for any other key. This matches exactly the keys that isUndoOrRedoKey in
 * pageHistoryInput.ts matches: by the character and by the key code, so that a layout whose Z key
 * types another letter still counts, and never with Alt, so that AltGr, which reports Ctrl and Alt
 * both, types its characters. A key that this took and that module did not would leave a typing
 * step open, and undo does nothing while one is.
 */
export function undoOrRedoCommandOf(
    event: KeyboardEvent,
): "undo" | "redo" | undefined {
    if (!(event.ctrlKey || event.metaKey) || event.altKey) return undefined;
    // The character first, and the key code only for a key that types neither letter.
    const key = event.key.toLowerCase();
    let isZ = key === "z";
    let isY = key === "y";
    if (!isZ && !isY) {
        isZ = event.keyCode === 90;
        isY = event.keyCode === 89;
    }
    if (isZ) return event.shiftKey ? "redo" : "undo";
    if (isY) return "redo";
    return undefined;
}

/** True while Ctrl, Command or Shift, any of the modifiers of the undo and redo keys, is down. */
function hasUndoModifier(event: KeyboardEvent): boolean {
    return event.ctrlKey || event.metaKey || event.shiftKey;
}

// Input types that edit no text, so have no undo of their own. With the focus on one of these, as
// after a click on a toolbox checkbox, the undo key is the page's.
const inputTypesWithoutText = new Set([
    "checkbox",
    "radio",
    "button",
    "submit",
    "reset",
    "range",
    "color",
    "file",
    "image",
]);

/**
 * True for a target where the undo key belongs to something other than the page:
 * - a textarea, or an input that edits text (any input type but a checkbox, a radio, a button,
 *   a range, a color, a file or an image input);
 * - anything in a dialog: an element with role="dialog", or the Format dialog (#format-toolbar,
 *   which has no role);
 * - anything in an open list or menu (role="listbox" or role="menu"), such as the Format dialog's
 *   font list, which MUI renders in a portal outside the dialog. While the Format dialog is open,
 *   CKEditor holds a selection in the box being formatted; undoing the page's typing could cut
 *   that selection short, and closing the dialog would then fail to restore it;
 * - a contenteditable element, only when includeContentEditable is true: in the page frame, the
 *   editable elements are the page's own text boxes, whose undo is the page's.
 * A select, a button and the other inputs do not count, so the key undoes the page from them.
 */
function isInTextFieldOrDialog(
    target: EventTarget | null,
    includeContentEditable: boolean,
): boolean {
    if (
        !target ||
        (target as Node).nodeType !== 1 // Node.ELEMENT_NODE, in whichever frame the target is
    )
        return false;
    const element = target as Element;
    if (
        element.closest(
            '[role="dialog"], #format-toolbar, [role="listbox"], [role="menu"]',
        )
    )
        return true;
    if (element.tagName === "TEXTAREA") return true;
    // An input's type property is lower case, and "text" for a missing or unknown type attribute.
    if (element.tagName === "INPUT")
        return !inputTypesWithoutText.has((element as HTMLInputElement).type);
    return (
        includeContentEditable &&
        !!element.closest('[contenteditable]:not([contenteditable="false"])')
    );
}
