// Transactions that the user's own clicks and key presses open, so that an action no code has
// wrapped is still an undo step of its own: docs/undo/APPROACHES.md section 3.0.2, rule 2, section
// 3.2 and case 7.7, and docs/undo/MINIMUM.md items 3 and 4.
//
// - A press of a pointer or a key in the page frame, with no transaction open, opens one with the
//   general description. A transaction in which nothing changed makes no undo step.
// - A drag is one transaction, from the press to the event that ends it.
// - Key presses in one text box are one transaction, which stays open between key presses, so
//   that Bloom's reactions to the typing join it (rule 7). It ends where CKEditor 4's undo plugin
//   ends a typing step (see TypingRun).
// - The undo and redo keys open nothing: undo and redo file their own changes.
// - A press in another frame (the toolbox, the root window with its top bar and dialogs) that
//   changes the page ends the history instead of making a step (MINIMUM.md item 4).
//
// Every listener is on a window in the capture phase, so it runs before any listener in that
// frame's document, Bloom's and CKEditor's included.
//
// When a transaction ends: a transaction opened by an event closes once the task that dispatched
// the event has finished, through afterCurrentTask (a timer by default). A browser dispatches
// pointerup, mouseup and click for one release in one task, so Bloom's handlers for all three,
// jQuery UI's `stop` among them, run inside the transaction, even one that stops propagation. A
// microtask would not do: after a trusted event, microtasks run as each listener returns, so it
// would close the transaction before Bloom's own listeners for the event had run.

import {
    BegunTransaction,
    kGenericUndoDescriptionL10nKey,
    PageHistory,
} from "./pageHistoryTypes";

export interface InputTransactionOptions {
    /**
     * True for an event that the user made. Default: event.isTrusted. Tests supply their own,
     * since an event that a script dispatches is never trusted.
     */
    isFromUser?: (event: Event) => boolean;
    /**
     * Run fn once the task that is running now, and its microtasks, have finished. Default:
     * setTimeout(fn, 0). Tests supply their own, to say exactly when a task is over.
     */
    afterCurrentTask?: (fn: () => void) => void;
    /**
     * True for an event on a control that runs undo or redo, such as the top bar's Undo button,
     * which must open no transaction: undo() waits while one is open, and would wait for the end
     * of the click's task. Default: the event's target is inside an element matching
     * kUndoControlSelector.
     */
    isOnUndoOrRedoControl?: (event: Event) => boolean;
}

/**
 * The controls that run undo or redo: the top bar's Undo button (UndoButton in
 * editTopBarControls.tsx), which has this attribute for the purpose.
 */
export const kUndoControlSelector = "[data-runs-undo]";

// From CKEditor 4.5.1's undo plugin (lib/ckeditor/ckeditor.js, CKEDITOR.plugins.undo): a typing
// step closes once it holds this many inputs (UndoManager.strokesLimit).
const kInputsPerTypingStep = 25;

// The keys that end a typing step without being typing (UndoManager.navigationKeyCodes: the
// arrows, Home, End, Page Up and Page Down).
const kNavigationKeys = new Set([
    "ArrowLeft",
    "ArrowRight",
    "ArrowUp",
    "ArrowDown",
    "Home",
    "End",
    "PageUp",
    "PageDown",
]);

// The keys that delete, which CKEditor groups apart from the keys that type
// (UndoManager.keyGroups: FUNCTIONAL is Backspace and Delete, PRINTABLE is every other key).
const kDeletingKeys = new Set(["Backspace", "Delete"]);

// The values of KeyboardEvent.key for a modifier or lock key. A keydown of one of these alone
// opens no transaction, ends no typing step and counts for nothing. The toolbox's markup handlers
// on each box (pageEditingMarkup.ts) leave these keys alone too.
export const kModifierKeys = new Set([
    "Shift",
    "Control",
    "Alt",
    "AltGraph",
    "Meta",
    "OS",
    "Super",
    "Hyper",
    "Fn",
    "FnLock",
    "Symbol",
    "SymbolLock",
    "CapsLock",
    "NumLock",
    "ScrollLock",
]);

// The key code of every key press during IME composition.
const kImeKeyCode = 229;

type InputKind = "typing" | "deleting";

/** A transaction that a user event opened and this module ends. */
interface HeldTransaction {
    transaction: BegunTransaction;
    // "pointer": from a press until the release's task ends. "event": until the task of the event
    // that opened it ends. "typing": key presses in one box (TypingRun).
    kind: "pointer" | "event" | "typing";
    // True once afterCurrentTask has been asked to end it.
    endScheduled: boolean;
    typing?: TypingRun;
    // True when the event came from one of the other windows: a step made then ends the history.
    fromOtherWindow: boolean;
}

/**
 * The state of a typing step, which follows CKEditor 4.5.1's undo plugin
 * (CKEDITOR.plugins.undo.NativeEditingHandler and UndoManager in lib/ckeditor/ckeditor.js):
 * - it counts `input` events, not key presses, so a key that changes nothing does not count
 *   (onInput); it closes at the 25th (strokesLimit);
 * - it closes when the user goes from typing to deleting or back (keyGroupChanged), at a
 *   navigation key (isNavigationKey), and at a click in the box (the click listener that calls
 *   onNavigationKey);
 * - key presses during IME composition (keyCode 229) neither count nor change the kind (onKeydown
 *   ignores them), but one can start a step, since the composition writes into the box;
 * - only a key whose KeyboardEvent.key is one character, or Enter, types, and only Backspace and
 *   Delete delete; any other key that reaches the box, a modifier on its own, a function key or
 *   Escape, neither starts nor ends a step;
 * - the input that a paste or a drop makes is not typing (ignoreInputEventListener);
 * - a command, which CKEditor runs from a key press with Ctrl or Alt, is a step of its own
 *   (the saveSnapshot before and after it, from beforeCommandExec and afterCommandExec).
 * Beyond the plugin: it closes when the focus leaves the box and at any press of a pointer in any
 * frame; and input during an IME composition does not count either, so that the limit never
 * closes a step inside a composition (APPROACHES.md case 7.7).
 */
interface TypingRun {
    box: Element;
    // undefined until the first key that types or deletes
    inputKind: InputKind | undefined;
    inputs: number;
}

/**
 * Open transactions on the user's input in pageWindow, the frame that holds the page, and end
 * the history on input in otherWindows that changes the page. Uses only the public PageHistory
 * API. Returns a function that removes the listeners and ends a transaction that this opened;
 * call it before disposing the history.
 */
export function attachInputTransactions(
    history: PageHistory,
    pageWindow: Window,
    otherWindows: Window[],
    options?: InputTransactionOptions,
): () => void {
    const input = new InputTransactions(history, options);
    const detachers = [
        input.listenToPageWindow(pageWindow),
        ...otherWindows.map((w) => input.listenToOtherWindow(w)),
    ];
    return () => {
        detachers.forEach((detach) => detach());
        input.endHeld();
    };
}

class InputTransactions {
    private held: HeldTransaction | undefined;
    private readonly isFromUser: (event: Event) => boolean;
    private readonly afterCurrentTask: (fn: () => void) => void;
    private readonly isOnUndoOrRedoControl: (event: Event) => boolean;

    public constructor(
        private readonly history: PageHistory,
        options: InputTransactionOptions | undefined,
    ) {
        this.isFromUser = options?.isFromUser ?? ((event) => event.isTrusted);
        this.afterCurrentTask =
            options?.afterCurrentTask ?? ((fn) => window.setTimeout(fn, 0));
        this.isOnUndoOrRedoControl =
            options?.isOnUndoOrRedoControl ?? isInsideUndoControl;
    }

    /** Listen in the frame that holds the page. Returns a function that stops listening. */
    public listenToPageWindow(pageWindow: Window): () => void {
        return this.listen(pageWindow, {
            pointerdown: (e) => this.onPress(e, false),
            mousedown: (e) => this.onPress(e, false),
            pointerup: () => this.onRelease(),
            mouseup: () => this.onRelease(),
            pointercancel: () => this.onRelease(),
            mousemove: (e) => this.onMouseMove(e as MouseEvent),
            click: (e) => this.onClick(e, false),
            keydown: (e) => this.onKeyDown(e as KeyboardEvent, false),
            input: (e) => this.onInput(e),
            paste: () => this.onPasteOrDrop(),
            cut: () => this.onPasteOrDrop(),
            drop: () => this.onPasteOrDrop(),
            focusout: (e) => this.onFocusOut(e as FocusEvent),
            blur: (e) => {
                // An element's blur reaches here too; the window's own has no node as its target.
                if (!isNode(e.target)) this.onWindowBlur();
            },
        });
    }

    /** Listen in a frame other than the page's. Returns a function that stops listening. */
    public listenToOtherWindow(otherWindow: Window): () => void {
        return this.listen(otherWindow, {
            pointerdown: (e) => this.onPress(e, true),
            mousedown: (e) => this.onPress(e, true),
            pointerup: () => this.onRelease(),
            mouseup: () => this.onRelease(),
            pointercancel: () => this.onRelease(),
            mousemove: (e) => this.onMouseMove(e as MouseEvent),
            click: (e) => this.onClick(e, true),
            keydown: (e) => this.onKeyDown(e as KeyboardEvent, true),
            blur: (e) => {
                if (!isNode(e.target)) this.onWindowBlur();
            },
        });
    }

    /**
     * End the transaction that this opened and still holds, if any. One from another window that
     * made an undo step with the general description ends the history, whenever it closes.
     */
    public endHeld(): void {
        const held = this.held;
        if (!held) return;
        this.held = undefined;
        // A step that kept the general description changed the page with no explicit transaction
        // inside it; a nested doTransaction or beginTransaction gives the step its own description
        // and makes it an undo step like any other (MINIMUM.md items 4 and 5). When the
        // transaction waits for one begun inside it, such as a dialog's, it closes only when that
        // one ends, and the step keeps the general description if that one is cancelled; onClose
        // reports the close in either case.
        if (held.fromOtherWindow)
            held.transaction.onClose((stepDescriptionL10nKey) => {
                if (stepDescriptionL10nKey === kGenericUndoDescriptionL10nKey)
                    this.history.clearUndoAndRedo(
                        "a change from the toolbox or the root window with no description",
                    );
            });
        held.transaction.end();
    }

    /**
     * Add each listener to target in the capture phase, behind the check that the user made the
     * event. Returns a function that removes them.
     */
    private listen(
        target: Window,
        listeners: Record<string, (event: Event) => void>,
    ): () => void {
        const wrapped = Object.entries(listeners).map(
            ([type, listener]) =>
                [
                    type,
                    (event: Event) => {
                        if (!this.isFromUser(event)) return;
                        this.forgetHeldIfEnded();
                        listener(event);
                    },
                ] as const,
        );
        wrapped.forEach(([type, listener]) =>
            target.addEventListener(type, listener, true),
        );
        return () =>
            wrapped.forEach(([type, listener]) =>
                target.removeEventListener(type, listener, true),
            );
    }

    /**
     * Forget a held transaction that something else ended, such as clearUndoAndRedo(), so that
     * the next input opens a new one. While the held one is open, the history has one open.
     */
    private forgetHeldIfEnded(): void {
        if (this.held && !this.history.hasOpenTransaction)
            this.held = undefined;
    }

    /**
     * A pointer press, pointerdown or the mousedown after it. A press on the Undo button opens
     * nothing, but still ends the typing step, which would otherwise hold the undo until something
     * else ended it.
     */
    private onPress(event: Event, fromOtherWindow: boolean): void {
        this.endTypingRun();
        this.endHeldIfScheduled();
        if (this.isOnUndoOrRedoControl(event)) return;
        // The mousedown after a pointerdown, or a second button during a drag.
        if (this.held?.kind === "pointer") return;
        this.openUnlessOneIsOpen("pointer", fromOtherWindow);
    }

    /** pointerup, mouseup, pointercancel: the drag, if any, ends with this event's task. */
    private onRelease(): void {
        if (this.held?.kind === "pointer") this.scheduleEnd();
    }

    /**
     * Bloom's canvas element drags end at the first mousemove with no button down
     * (CanvasElementPointerInteractions.ts), for a release that happened outside the window.
     */
    private onMouseMove(event: MouseEvent): void {
        if (this.held?.kind === "pointer" && event.buttons === 0)
            this.scheduleEnd();
    }

    /**
     * A click. Its press opened a transaction, which the click joins. A click with no press, such
     * as one from the keyboard, opens a transaction until the end of its task.
     */
    private onClick(event: Event, fromOtherWindow: boolean): void {
        if (this.isOnUndoOrRedoControl(event)) return;
        if (this.openUnlessOneIsOpen("event", fromOtherWindow))
            this.scheduleEnd();
    }

    private onKeyDown(event: KeyboardEvent, fromOtherWindow: boolean): void {
        this.endHeldIfScheduled();
        // Such as the Control of a Ctrl+Y: a transaction opened here would take whatever the
        // keydown writes, make a step of it and clear redo before the Y arrives.
        if (kModifierKeys.has(event.key)) return;
        if (isUndoOrRedoKey(event) || this.isOnUndoOrRedoControl(event)) {
            // Undo and redo wait while a transaction is open; the typing step must not hold them.
            this.endTypingRun();
            return;
        }
        const box = fromOtherWindow ? undefined : textBoxContaining(event);
        if (box && !isCommandKey(event) && !kNavigationKeys.has(event.key)) {
            this.onKeyDownInBox(event, box);
            return;
        }
        this.endTypingRun();
        if (this.openUnlessOneIsOpen("event", fromOtherWindow))
            this.scheduleEnd();
    }

    /** A key press in a text box that may type: see TypingRun. */
    private onKeyDownInBox(event: KeyboardEvent, box: Element): void {
        const isIme = event.keyCode === kImeKeyCode || event.isComposing;
        let inputKind: InputKind | undefined;
        if (!isIme) {
            if (kDeletingKeys.has(event.key)) inputKind = "deleting";
            else if (isTypingKey(event.key)) inputKind = "typing";
            else return;
        }
        const run = this.held?.typing;
        if (
            run &&
            (run.box !== box ||
                (inputKind && run.inputKind && inputKind !== run.inputKind))
        )
            this.endTypingRun();
        if (this.held?.typing) {
            this.held.typing.inputKind ??= inputKind;
            return;
        }
        if (this.openUnlessOneIsOpen("typing", false))
            this.held!.typing = { box, inputKind, inputs: 0 };
    }

    /** An input event: counts toward the 25 of a typing step (TypingRun). */
    private onInput(event: Event): void {
        const run = this.held?.typing;
        if (!run || !isNode(event.target)) return;
        if (!run.box.contains(event.target)) return;
        if ((event as InputEvent).isComposing) return;
        run.inputs++;
        if (run.inputs >= kInputsPerTypingStep) this.endTypingRun();
    }

    /**
     * A paste, cut or drop is a step of its own, not typing. One that a key press (Ctrl+V)
     * started joins that key press's transaction.
     */
    private onPasteOrDrop(): void {
        this.endTypingRun();
        if (this.openUnlessOneIsOpen("event", false)) this.scheduleEnd();
    }

    /** The focus leaving a box ends its typing step. */
    private onFocusOut(event: FocusEvent): void {
        const run = this.held?.typing;
        if (!run || !isNode(event.target)) return;
        if (!run.box.contains(event.target)) return;
        const next = event.relatedTarget;
        if (isNode(next) && run.box.contains(next)) return;
        this.endTypingRun();
    }

    /** The window losing the focus ends a typing step, and a drag whose release it may miss. */
    private onWindowBlur(): void {
        this.endTypingRun();
        if (this.held?.kind === "pointer") this.scheduleEnd();
    }

    /**
     * Open a transaction with the general description and hold it, unless one is open already,
     * which then takes this input (rule 7). Returns true if it opened one.
     */
    private openUnlessOneIsOpen(
        kind: HeldTransaction["kind"],
        fromOtherWindow: boolean,
    ): boolean {
        if (this.history.hasOpenTransaction) return false;
        this.held = {
            transaction: this.history.beginTransaction(
                kGenericUndoDescriptionL10nKey,
            ),
            kind,
            endScheduled: false,
            fromOtherWindow,
        };
        return true;
    }

    /** End the held transaction once the task running now has finished. */
    private scheduleEnd(): void {
        const held = this.held;
        if (!held || held.endScheduled) return;
        held.endScheduled = true;
        this.afterCurrentTask(() => {
            if (this.held === held) this.endHeld();
        });
    }

    /**
     * End a held transaction whose end is scheduled but has not come yet, so that the next input
     * does not join it.
     */
    private endHeldIfScheduled(): void {
        if (this.held?.endScheduled) this.endHeld();
    }

    private endTypingRun(): void {
        if (this.held?.kind === "typing") this.endHeld();
    }
}

/**
 * Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z, and the same with the Mac's Command key. Matched by the
 * character and by the key code (CKEditor's keystrokes CTRL+90, CTRL+89 and CTRL+SHIFT+90), so
 * that a layout whose Z key types another letter still counts. Matching too many keys only ends a
 * typing step; missing one would leave a typing transaction open, and the undo would wait until it
 * closed.
 */
function isUndoOrRedoKey(event: KeyboardEvent): boolean {
    if (!(event.ctrlKey || event.metaKey) || event.altKey) return false;
    const key = event.key.toLowerCase();
    return (
        key === "z" ||
        key === "y" ||
        event.keyCode === 90 ||
        event.keyCode === 89
    );
}

/**
 * True for the value of KeyboardEvent.key of a key that types: a single character (one code
 * point, which may be outside the BMP), or Enter. Named keys such as "Escape", "F2", "Tab",
 * "Dead" and "Unidentified" are longer.
 */
function isTypingKey(key: string): boolean {
    return Array.from(key).length === 1 || key === "Enter";
}

/**
 * A key press that CKEditor would treat as a command: one with Ctrl, Alt or Command, but not a
 * modifier key on its own, and not AltGr, which types characters on many keyboards and reports
 * Ctrl and Alt both.
 */
function isCommandKey(event: KeyboardEvent): boolean {
    if (kModifierKeys.has(event.key)) return false;
    if (event.getModifierState?.("AltGraph")) return false;
    if (event.ctrlKey && event.altKey) return false;
    return event.ctrlKey || event.altKey || event.metaKey;
}

/** The editable text box, the editing host, that the key press went to, if any. */
function textBoxContaining(event: KeyboardEvent): Element | undefined {
    const target = event.target;
    if (!isNode(target) || target.nodeType !== Node.ELEMENT_NODE)
        return undefined;
    return (
        (target as Element).closest(
            '[contenteditable]:not([contenteditable="false"])',
        ) ?? undefined
    );
}

/** The default isOnUndoOrRedoControl: the event's target is inside kUndoControlSelector. */
function isInsideUndoControl(event: Event): boolean {
    const target = event.target;
    if (!isNode(target) || target.nodeType !== Node.ELEMENT_NODE) return false;
    return !!(target as Element).closest(kUndoControlSelector);
}

/**
 * True for a node. Checked without instanceof, which fails for a node of another frame.
 */
function isNode(target: EventTarget | null): target is Node {
    return !!target && typeof (target as Node).nodeType === "number";
}
