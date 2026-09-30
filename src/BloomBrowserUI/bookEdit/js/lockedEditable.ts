// A locked text box: a bloom-editable whose text Bloom writes and the person editing may not
// change. It is an ordinary text box in every other way: it takes the focus, so it shows its format
// gear and its style can be changed; it takes part in flow text; it is saved and published like
// any other box. Only the keyboard, the clipboard and dragging are kept from changing its text,
// and it shows no caret. The Folio template's table of contents is one.

export const kLockedEditableClass = "bloom-locked";

// Keys that move around or leave the box without changing its text.
const kKeysThatChangeNothing = new Set([
    "ArrowLeft",
    "ArrowRight",
    "ArrowUp",
    "ArrowDown",
    "Home",
    "End",
    "PageUp",
    "PageDown",
    "Tab",
    "Escape",
    "Shift",
    "Control",
    "Alt",
    "Meta",
    "CapsLock",
]);

/** The locked box an event happened in, if any. */
function lockedBoxOf(event: Event): HTMLElement | null {
    const target = event.target;
    if (!(target instanceof Element)) return null;
    return target.closest<HTMLElement>(
        `.bloom-editable.${kLockedEditableClass}`,
    );
}

/** True for a key press that could change the text of the box it is in. */
export function keyChangesText(event: KeyboardEvent): boolean {
    if (kKeysThatChangeNothing.has(event.key) || /^F\d+$/.test(event.key))
        return false;
    // Copying and selecting everything change nothing.
    if (
        (event.ctrlKey || event.metaKey) &&
        ["c", "a"].includes(event.key.toLowerCase())
    )
        return false;
    return true;
}

function refuse(event: Event) {
    if (!lockedBoxOf(event)) return;
    event.preventDefault();
    event.stopImmediatePropagation();
}

function refuseKey(event: KeyboardEvent) {
    if (lockedBoxOf(event) && keyChangesText(event)) {
        event.preventDefault();
        event.stopImmediatePropagation();
    }
}

const installed = new WeakSet<Document>();

/**
 * Keep the person editing from changing the text of every locked box in this document, now and
 * later. The listeners are on the window, in the capture phase, so they run before CKEditor, which
 * handles some keys (Enter, for one) itself rather than through the browser's own editing.
 */
export function setUpLockedEditables(doc: Document = document) {
    if (installed.has(doc)) return;
    installed.add(doc);
    const win = doc.defaultView;
    if (!win) return;
    win.addEventListener("keydown", refuseKey, true);
    win.addEventListener("keypress", refuseKey, true);
    for (const type of ["beforeinput", "paste", "cut", "drop"])
        win.addEventListener(type, refuse, true);
}

/** Lock this box (see kLockedEditableClass). */
export function lockEditable(box: HTMLElement) {
    box.classList.add(kLockedEditableClass);
}
