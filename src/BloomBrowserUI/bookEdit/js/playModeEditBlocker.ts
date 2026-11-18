// While a game page is in its Play tab, the page is being tried out to see how it will behave when
// published, not edited. The toolbox and the top bar's editing controls are disabled then (see
// workspaceRoot's setEditingDisabledForPlay); this does the same for the keyboard and the
// clipboard. The page has many separate editing handlers (CKEditor and the browser's own editing in
// text boxes, canvas-element keys, picture and page undo, paste), so rather than teach each of them
// about Play, this stops the events first: a capture listener on the window runs before any
// listener on the document or below it.

// The game tab control puts this class on the page's parent while the page is in Play.
const kPlayingClass = "drag-activity-play";

const kEditingKeysAnywhere = [
    "Delete",
    "Backspace",
    "ArrowLeft",
    "ArrowRight",
    "ArrowUp",
    "ArrowDown",
    "F6",
    "F7",
    "F8",
];

const kModifierKeys = ["Control", "Shift", "Alt", "Meta"];

// With Ctrl (or Cmd), only these change nothing: copying, and zooming.
const kHarmlessControlKeys = ["c", "C", "=", "+", "-", "0"];

// True if this key would (or could) edit the page.
function isEditingKey(e: KeyboardEvent): boolean {
    if (kModifierKeys.includes(e.key)) {
        return false; // pressed on its own, before the key it modifies
    }
    if (e.ctrlKey || e.metaKey) {
        return !kHarmlessControlKeys.includes(e.key);
    }
    if (kEditingKeysAnywhere.includes(e.key)) {
        return true;
    }
    // Typing, Enter and Tab in a text box. Elsewhere plain keys are harmless, and Enter or Space
    // on one of the game's buttons is how a keyboard user plays it.
    const target = e.target as HTMLElement | null;
    return (
        !!target?.isContentEditable ||
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA"
    );
}

export function installPlayModeEditBlocker(win: Window): void {
    const playing = () =>
        !!win.document.getElementsByClassName(kPlayingClass)[0];
    const stop = (e: Event) => {
        e.preventDefault();
        e.stopImmediatePropagation();
    };
    win.addEventListener(
        "keydown",
        (e) => {
            if (playing() && isEditingKey(e)) {
                stop(e);
            }
        },
        { capture: true },
    );
    // Clipboard and drag-and-drop changes, and any input the key filter didn't catch.
    for (const type of ["paste", "cut", "drop", "beforeinput"]) {
        win.addEventListener(
            type,
            (e) => {
                if (playing()) {
                    stop(e);
                }
            },
            { capture: true },
        );
    }
}
