// Keeps the user out of the page being edited until it is ready to use.
//
// A page is visible, and its text boxes are editable, well before the code that sets it up for
// editing has finished. Input that arrives in that window goes wrong in ways that are hard to see:
// most seriously, when each CKEditor finishes starting it writes the text it took over from its
// box back into it, wiping out anything typed there since (see the KNOWN DEFECT in bootstrap(),
// bloomEditing.ts). On a slow machine the window lasts seconds. So until the page says it is ready
// (markPageReadyForInput), every mouse and keyboard event that could change it is swallowed here, and
// the cursor shows the page is busy. The workspace covers the page frame for the same span (see
// coverPageUntilReadyForInput in workspaceRoot.ts), which also covers the time before this code runs.

// What would change the page, or move the focus or the caret within it. Scrolling is left alone.
const kBlockedEvents = [
    "keydown",
    "keypress",
    "keyup",
    "beforeinput",
    "paste",
    "cut",
    "drop",
    "dragstart",
    "pointerdown",
    "pointerup",
    "mousedown",
    "mouseup",
    "click",
    "dblclick",
    "auxclick",
    "contextmenu",
    "touchstart",
];

// On the document's root element while input is blocked; the style below shows the busy cursor.
const kNotReadyClass = "bloom-page-not-ready-for-input";
// On the document's root element once the page is ready for input. Tests wait for it.
export const kReadyForInputClass = "bloom-page-ready-for-input";

// On the page frame: the number the workspace gave the load now under way in it (see
// coverPageUntilReadyForInput in workspaceRoot.ts).
export const kPageLoadNumberAttribute = "data-bloom-page-load";

let blocking = false;
let busyCursorStyle: HTMLStyleElement | undefined;
// The element that had the focus when composition started while input was blocked (see
// stopComposition).
let elementLeftForComposition: HTMLElement | undefined;

function swallow(event: Event): void {
    // Only what the user does. Events the page's own code dispatches while it sets itself up (a
    // programmatic click, say) must still work.
    if (!event.isTrusted) return;
    event.preventDefault();
    event.stopImmediatePropagation();
}

// Text from an input method (IME), voice typing or a touch keyboard arrives through composition,
// which cannot be cancelled: preventing its keydown (keyCode 229) or its beforeinput does not stop
// the text going in. Setting up the page puts the caret in a text box, so the only way to keep that
// text out is to take the focus away from the box until the page is ready.
function stopComposition(event: Event): void {
    if (!event.isTrusted) return;
    const active = document.activeElement;
    if (active instanceof HTMLElement && active !== document.body) {
        elementLeftForComposition = active;
        active.blur();
    }
}

/** Start swallowing input to this page. Call it as early as possible in the page's script. */
export function blockInputUntilPageIsReady(): void {
    if (blocking) return;
    blocking = true;
    // On the window and in the capture phase, so it runs before any of the page's own handlers.
    kBlockedEvents.forEach((type) =>
        window.addEventListener(type, swallow, { capture: true }),
    );
    window.addEventListener("compositionstart", stopComposition, {
        capture: true,
    });
    document.documentElement.classList.add(kNotReadyClass);
    busyCursorStyle = document.createElement("style");
    busyCursorStyle.textContent = `.${kNotReadyClass}, .${kNotReadyClass} * { cursor: wait !important; }`;
    document.head.appendChild(busyCursorStyle);
}

/** True once the page has been marked ready for input, or if input was never blocked. */
export function isPageReadyForInput(): boolean {
    return !blocking;
}

/** Let input reach the page again. */
export function markPageReadyForInput(): void {
    if (!blocking) return;
    blocking = false;
    kBlockedEvents.forEach((type) =>
        window.removeEventListener(type, swallow, { capture: true }),
    );
    window.removeEventListener("compositionstart", stopComposition, {
        capture: true,
    });
    // Put the caret back where setting up the page had put it, unless something else has the focus.
    if (
        elementLeftForComposition?.isConnected &&
        (!document.activeElement || document.activeElement === document.body)
    ) {
        elementLeftForComposition.focus({ preventScroll: true });
    }
    elementLeftForComposition = undefined;
    busyCursorStyle?.remove();
    busyCursorStyle = undefined;
    document.documentElement.classList.remove(kNotReadyClass);
    document.documentElement.classList.add(kReadyForInputClass);
}
