// Transactions that the user's own clicks and key presses open, so that an action no code has
// wrapped is still an undo step of its own: docs/undo/APPROACHES.md section 3.0.2, rule 2, section
// 3.2 and case 7.7, and docs/undo/MINIMUM.md items 3 and 4.
//
// - A press of a pointer or a key in the page frame, with no transaction open, opens one with the
//   general description. A transaction in which nothing changed makes no undo step.
// - A drag is one transaction, from the press to the event that ends it.
// - Key presses in one text box are one transaction, which stays open between key presses, so
//   that Bloom's reactions to the typing join it (rule 7). It ends where the browser's own undo
//   of typing ends a step (see TypingRun).
// - A typing step, and the step of a paste, a cut or a drop in the page frame, is described by
//   that input (EditTab.Undo.Typing and the rest below) unless a transaction nested in it has
//   described it. Every other step keeps the general description unless the action's own code
//   describes it.
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

import { describeInputOpening } from "./pageHistoryDetails";
import {
    BegunTransaction,
    kCutUndoDescriptionL10nKey,
    kGenericUndoDescriptionL10nKey,
    kPasteUndoDescriptionL10nKey,
    PageHistory,
} from "./pageHistoryTypes";

/** The l10n key of the description of a typing step: key presses in one text box that type. */
export const kTypingUndoDescriptionL10nKey = "EditTab.Undo.Typing";

/** The l10n key of the description of a typing step of Backspace and Delete presses. */
export const kDeleteTextUndoDescriptionL10nKey = "EditTab.Undo.DeleteText";

/** The l10n key of the description of a drop in the page, such as text dragged between boxes. */
export const kDropUndoDescriptionL10nKey = "EditTab.Undo.Drop";

// The description of a step that a paste, a cut or a drop in the page window made, by event type.
const kDescriptionOfEvent: Record<string, string> = {
    paste: kPasteUndoDescriptionL10nKey,
    cut: kCutUndoDescriptionL10nKey,
    drop: kDropUndoDescriptionL10nKey,
};

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
     * The time now in milliseconds, for the pause that ends a typing step
     * (kPauseThatEndsTypingStepMs). Default: performance.now(). Tests supply their own clock.
     */
    now?: () => number;
    /**
     * True for an event on a control that runs undo or redo, such as the top bar's Undo button,
     * which must open no transaction: undo() waits while one is open, and would wait for the end
     * of the click's task. Default: the event's target is inside an element matching
     * kUndoControlSelector.
     */
    isOnUndoOrRedoControl?: (event: Event) => boolean;
    /**
     * Called just after a user event opens a transaction, with the event and what kind of
     * transaction it opened; for the Show Page History dialog. Default: nothing.
     */
    onOpened?: (event: Event, kind: "pointer" | "event" | "typing") => void;
}

/**
 * The controls that run undo or redo: the top bar's Undo button (UndoButton in
 * editTopBarControls.tsx), which has this attribute for the purpose.
 */
export const kUndoControlSelector = "[data-runs-undo]";

/**
 * Elements whose events this module leaves alone, as if they never happened: a developer's tool
 * that watches the history, such as the Show Page History dialog, which must neither open a
 * transaction nor end the typing step.
 */
export const kIgnoredByPageHistoryInputSelector =
    "[data-ignored-by-page-history-input]";

// The keys that end a typing step without being typing: the browser's undo ends the step at each,
// even one that leaves the caret where it was.
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

// The inputType of a beforeinput that types or deletes, as the browser's undo groups into typing
// steps: not a paste, a drop, a format command or an undo.
const kTypingInputTypes =
    /^(insertText|insertCompositionText|insertReplacementText|insertParagraph|insertLineBreak|delete(Content|Word|SoftLine|HardLine)(Backward|Forward)?)$/;

// The keys that delete, which the browser's undo groups apart from the keys that type.
const kDeletingKeys = new Set(["Backspace", "Delete"]);

// The KeyboardEvent.key of a function key, F1 to F24.
const kFunctionKey = /^F([1-9]|1[0-9]|2[0-4])$/;

// The KeyboardEvent.key of a digit key.
const kDigitKey = /^[0-9]$/;

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
    // For a transaction from another window: the event that opened it, as the reason that the
    // history gives Bloom's log and the Show Page History window when the step ends the history.
    openedBy?: string;
    // What the user did, for input in the page window that no code of Bloom's describes: a paste,
    // a cut or a drop. The step gets this description when the transaction ends, unless a
    // transaction nested in it gave it one. A typing step gets its description from its TypingRun.
    descriptionOfInput?: string;
}

/**
 * A pause of this long between two key presses in a text box ends the typing step: the key after
 * the pause starts a new one. The browser's own undo has no such rule; Bloom has it so that a
 * person who stops typing for a moment and starts again can undo the second burst alone.
 */
export const kPauseThatEndsTypingStepMs = 1000;

/**
 * The state of a typing step. Its rules are those of the undo of the browser that Bloom runs in,
 * as measured in Edge (Chromium 155) by typing into a contenteditable box with real key presses
 * (docs/undo/TYPING-STEPS.md), with one rule added:
 * - it has no limit on its length, but a pause (kPauseThatEndsTypingStepMs) ends it, except inside
 *   an IME composition, whose end ends it anyway;
 * - deleting (Backspace or Delete) right after typing starts a new step, which typing after the
 *   deleting joins; a delete that changes nothing, such as Backspace at the start of the box, does
 *   not count (onBeforeInput);
 * - Enter and Shift+Enter join it. After one that leaves no text after the caret on its line, the
 *   next key that types starts a new step, so that each line typed is a step, with the Enter that
 *   ends it. (Chromium ends that step one character later, a by-product of its replacing the
 *   placeholder of the empty line; the step ends at the Enter here instead.)
 * - it ends at a navigation key, at a press of a pointer outside the box, and when a press in the
 *   box moves the caret; a click that leaves the caret where it was does not end it;
 * - it ends when the focus leaves the box or the window;
 * - an IME composition joins it, and the end of the composition ends it;
 * - a key press with Ctrl, Alt or Cmd is a step of its own, a command; so are a paste, a cut and
 *   a drop;
 * - only a key whose KeyboardEvent.key is one character, or Enter, types, and only Backspace and
 *   Delete delete; any other key that reaches the box, a modifier on its own, a function key or
 *   Escape, neither starts nor ends a step.
 */
interface TypingRun {
    box: Element;
    // What the step began with, for its description; undefined until the first key that types or
    // deletes.
    firstKind: InputKind | undefined;
    // What the latest change to the box did: typing or deleting.
    latestKind: InputKind | undefined;
    // True from an Enter or Shift+Enter until the next key that types or deletes.
    afterLineBreak: boolean;
    // When the latest key press or input of the step came, by InputTransactionOptions.now.
    latestInputTime: number;
    // The selection when a press of the pointer in the box began, until the press has ended.
    selectionAtPress?: SelectionPoints;
}

/** Where a selection starts and ends, to tell whether a click moved the caret. */
interface SelectionPoints {
    anchorNode: Node | null;
    anchorOffset: number;
    focusNode: Node | null;
    focusOffset: number;
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
    private readonly now: () => number;
    private readonly isOnUndoOrRedoControl: (event: Event) => boolean;
    private readonly onOpened:
        | ((event: Event, kind: HeldTransaction["kind"]) => void)
        | undefined;

    public constructor(
        private readonly history: PageHistory,
        options: InputTransactionOptions | undefined,
    ) {
        this.isFromUser = options?.isFromUser ?? ((event) => event.isTrusted);
        this.afterCurrentTask =
            options?.afterCurrentTask ?? ((fn) => window.setTimeout(fn, 0));
        this.now = options?.now ?? (() => performance.now());
        this.isOnUndoOrRedoControl =
            options?.isOnUndoOrRedoControl ?? isInsideUndoControl;
        this.onOpened = options?.onOpened;
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
            beforeinput: (e) => this.onBeforeInput(e as InputEvent),
            input: (e) => this.onInput(e as InputEvent),
            compositionend: (e) => this.onCompositionEnd(e),
            paste: (e) => this.onPasteOrDrop(e),
            cut: (e) => this.onPasteOrDrop(e),
            drop: (e) => this.onPasteOrDrop(e),
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
        this.describeHeldByInput(held);
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
                        `a change with no description, after this input: ${held.openedBy}`,
                    );
            });
        held.transaction.end();
    }

    /**
     * Give the step of held, which is about to end, the description of what the user did, for
     * input that no code of Bloom's describes: typing, deleting text, a paste, a cut or a drop. An
     * empty transaction nested in held does it, so the step keeps a description that a transaction
     * nested in it gave it earlier (PageHistory.doTransaction). Does nothing if held has closed
     * already, as when clearUndoAndRedo() ended it.
     */
    private describeHeldByInput(held: HeldTransaction): void {
        let description = held.descriptionOfInput;
        if (held.typing) {
            description =
                held.typing.firstKind === "deleting"
                    ? kDeleteTextUndoDescriptionL10nKey
                    : kTypingUndoDescriptionL10nKey;
        }
        if (!description) return;
        let closed = false;
        held.transaction.onClose(() => (closed = true));
        if (closed) return;
        void this.history.doTransaction(description, () => {});
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
                        // Chromium dispatches the compositionend of a committed composition
                        // as untrusted (seen in Edge 155), and no code of Bloom's dispatches
                        // one, so it counts as the user's.
                        if (
                            !this.isFromUser(event) &&
                            event.type !== "compositionend"
                        )
                            return;
                        if (isInsideIgnoredElement(event)) return;
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
     * else ended it. A press in the text of the box of the typing step leaves the step open until
     * the press has ended, and ends it then only if the caret moved (onRelease). A press on
     * something in the box that is not text, such as an inline image (contenteditable=false),
     * ends it like any other press, so that a drag of the picture is a step of its own.
     */
    private onPress(event: Event, fromOtherWindow: boolean): void {
        const run = this.held?.typing;
        if (
            run &&
            !fromOtherWindow &&
            isNode(event.target) &&
            run.box.contains(event.target) &&
            !isInsideNonEditablePartOf(run.box, event.target)
        ) {
            run.selectionAtPress ??= readSelection(run.box);
            return;
        }
        this.endTypingRun();
        this.endHeldIfScheduled();
        if (this.isOnUndoOrRedoControl(event)) return;
        // The mousedown after a pointerdown, or a second button during a drag.
        if (this.held?.kind === "pointer") return;
        this.openUnlessOneIsOpen(event, "pointer", fromOtherWindow);
    }

    /**
     * pointerup, mouseup, pointercancel: the drag, if any, ends with this event's task. A press in
     * the box of the typing step ends the step with this event's task if it moved the caret.
     */
    private onRelease(): void {
        const run = this.held?.typing;
        if (run?.selectionAtPress) {
            const atPress = run.selectionAtPress;
            run.selectionAtPress = undefined;
            this.afterCurrentTask(() => {
                if (
                    this.held?.typing === run &&
                    !sameSelection(atPress, readSelection(run.box))
                )
                    this.endTypingRun();
            });
            return;
        }
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
        if (this.openUnlessOneIsOpen(event, "event", fromOtherWindow))
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
        if (this.openUnlessOneIsOpen(event, "event", fromOtherWindow))
            this.scheduleEnd();
    }

    /**
     * A key press in a text box that may type: see TypingRun. Whether a delete starts a new step
     * waits for its beforeinput, which says whether it will change anything (onBeforeInput).
     */
    private onKeyDownInBox(event: KeyboardEvent, box: Element): void {
        const isIme = event.keyCode === kImeKeyCode || event.isComposing;
        let inputKind: InputKind | undefined;
        if (!isIme) {
            if (kDeletingKeys.has(event.key)) inputKind = "deleting";
            else if (isTypingKey(event.key)) inputKind = "typing";
            else return;
        }
        const isLineBreak = !isIme && event.key === "Enter";
        const now = this.now();
        const run = this.held?.typing;
        if (run && run.box !== box) this.endTypingRun();
        else if (
            run &&
            !event.isComposing &&
            now - run.latestInputTime >= kPauseThatEndsTypingStepMs
        )
            this.endTypingRun();
        else if (
            run?.afterLineBreak &&
            inputKind !== "deleting" &&
            !isLineBreak &&
            !hasTextAfterCaret(box)
        )
            this.endTypingRun();
        let current = this.held?.typing;
        if (!current) {
            if (!this.openUnlessOneIsOpen(event, "typing", false)) return;
            current = {
                box,
                firstKind: undefined,
                latestKind: undefined,
                afterLineBreak: false,
                latestInputTime: now,
            };
            this.held!.typing = current;
        }
        current.latestInputTime = now;
        current.firstKind ??= inputKind;
        current.afterLineBreak = isLineBreak;
        // Bloom and CKEditor make the line break themselves, with no input event.
        if (isLineBreak) current.latestKind = "typing";
    }

    /**
     * Before the browser changes the box: a delete right after typing starts a new step, unless
     * it will change nothing, as Backspace at the start of the box does, whose target ranges are
     * all collapsed (TypingRun). Input that types or deletes with no key press before it, such
     * as a spelling suggestion, dictation, or an input method that sends no keydown, starts a
     * typing step if nothing is open.
     */
    private onBeforeInput(event: InputEvent): void {
        const run = this.held?.typing;
        if (!run) {
            this.onInputWithNoKeyPress(event);
            return;
        }
        if (!isNode(event.target) || !run.box.contains(event.target)) return;
        if (!event.inputType.startsWith("delete")) return;
        if (run.latestKind !== "typing") return;
        const ranges = event.getTargetRanges();
        if (ranges.length > 0 && ranges.every((range) => range.collapsed))
            return;
        this.endTypingRun();
        if (this.openUnlessOneIsOpen(event, "typing", false))
            this.held!.typing = {
                box: run.box,
                firstKind: "deleting",
                latestKind: undefined,
                afterLineBreak: false,
                latestInputTime: this.now(),
            };
    }

    /** The beforeinput of typing or deleting in a box with nothing open: see onBeforeInput. */
    private onInputWithNoKeyPress(event: InputEvent): void {
        if (this.held) return;
        if (!kTypingInputTypes.test(event.inputType)) return;
        const box = textBoxContaining(event);
        if (!box) return;
        if (this.openUnlessOneIsOpen(event, "typing", false))
            this.held!.typing = {
                box,
                firstKind: event.inputType.startsWith("delete")
                    ? "deleting"
                    : "typing",
                latestKind: undefined,
                afterLineBreak: false,
                latestInputTime: this.now(),
            };
    }

    /** An input event in the box of the typing step: notes whether it typed or deleted. */
    private onInput(event: InputEvent): void {
        const run = this.held?.typing;
        if (!run || !isNode(event.target)) return;
        if (!run.box.contains(event.target)) return;
        if (event.inputType.startsWith("delete")) run.latestKind = "deleting";
        else if (event.inputType.startsWith("insert"))
            run.latestKind = "typing";
    }

    /** The end of an IME composition in the box of the typing step ends the step. */
    private onCompositionEnd(event: Event): void {
        const run = this.held?.typing;
        if (!run || !isNode(event.target)) return;
        if (run.box.contains(event.target)) this.endTypingRun();
    }

    /**
     * A paste, cut or drop is a step of its own, not typing. One that a key press (Ctrl+V)
     * started joins that key press's transaction.
     */
    private onPasteOrDrop(event: Event): void {
        this.endTypingRun();
        if (this.openUnlessOneIsOpen(event, "event", false)) this.scheduleEnd();
        if (this.held && !this.held.fromOtherWindow)
            this.held.descriptionOfInput ??= kDescriptionOfEvent[event.type];
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
     * Open a transaction with the general description for event and hold it, unless one is open
     * already, which then takes this input (rule 7). Returns true if it opened one.
     */
    private openUnlessOneIsOpen(
        event: Event,
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
            openedBy: fromOtherWindow
                ? describeInputOpening(event, kind).text
                : undefined,
        };
        this.onOpened?.(event, kind);
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
 * point, which may be outside the BMP), or Enter. Named keys such as "Escape", "Tab", "Dead"
 * and "Unidentified" are longer.
 */
function isTypingKey(key: string): boolean {
    return Array.from(key).length === 1 || key === "Enter";
}

/**
 * A key press that CKEditor or Bloom would treat as a command: a function key, such as Bloom's
 * F6, F7 and F8 (bloomEditing.ts), or one with Ctrl, Alt or Command, but not a modifier key on its
 * own, and not AltGr, which types characters on many keyboards and reports Ctrl and Alt both. A
 * Ctrl+Alt press counts as AltGr unless the browser says otherwise and its key is a digit, such
 * as Bloom's Ctrl+Alt+0, 1 and 2: AltGr with a digit key types something other than the digit.
 */
function isCommandKey(event: KeyboardEvent): boolean {
    if (kModifierKeys.has(event.key)) return false;
    if (kFunctionKey.test(event.key)) return true;
    if (event.getModifierState?.("AltGraph")) return false;
    if (event.ctrlKey && event.altKey) return kDigitKey.test(event.key);
    return event.ctrlKey || event.altKey || event.metaKey;
}

/** The editable text box, the editing host, that the key press or input went to, if any. */
function textBoxContaining(event: Event): Element | undefined {
    const target = event.target;
    if (!isNode(target) || target.nodeType !== Node.ELEMENT_NODE)
        return undefined;
    return (
        (target as Element).closest(
            '[contenteditable]:not([contenteditable="false"])',
        ) ?? undefined
    );
}

/** True when node is inside a contenteditable=false element of box, such as an inline image. */
function isInsideNonEditablePartOf(box: Element, node: Node): boolean {
    const element =
        node.nodeType === Node.ELEMENT_NODE
            ? (node as Element)
            : node.parentElement;
    const nonEditable = element?.closest('[contenteditable="false"]');
    return !!nonEditable && box.contains(nonEditable);
}

/** True when the event's target is inside kIgnoredByPageHistoryInputSelector. */
function isInsideIgnoredElement(event: Event): boolean {
    const target = event.target;
    if (!isNode(target) || target.nodeType !== Node.ELEMENT_NODE) return false;
    return !!(target as Element).closest(kIgnoredByPageHistoryInputSelector);
}

// The characters that take up no space, which a line holds for the caret's sake: the zero width
// space of CKEditor's filling character, the zero width non-joiner that Bloom puts after a line
// break (BloomField.InsertLineBreak), and the byte order mark.
const kZeroWidthCharacters = /[\u200b\u200c\ufeff]/g;

/**
 * True when the line that the caret is on has text after the caret, as after an Enter in the
 * middle of a paragraph, or when there is no caret in the document. The line is the block that
 * holds the caret: the nearest paragraph, heading, list item or div inside box, or box itself.
 */
function hasTextAfterCaret(box: Element): boolean {
    const selection = box.ownerDocument.getSelection();
    if (!selection || selection.rangeCount === 0) return true;
    const caret = selection.getRangeAt(0);
    const container = caret.endContainer;
    const element =
        container.nodeType === Node.ELEMENT_NODE
            ? (container as Element)
            : container.parentElement;
    const block = element?.closest("p, h1, h2, h3, h4, h5, h6, li, div");
    const line = block && box.contains(block) ? block : box;
    const after = box.ownerDocument.createRange();
    after.selectNodeContents(line);
    after.setStart(container, caret.endOffset);
    return after.toString().replace(kZeroWidthCharacters, "") !== "";
}

/** The selection of box's document, to compare with sameSelection. */
function readSelection(box: Element): SelectionPoints {
    const selection = box.ownerDocument.getSelection();
    return {
        anchorNode: selection?.anchorNode ?? null,
        anchorOffset: selection?.anchorOffset ?? 0,
        focusNode: selection?.focusNode ?? null,
        focusOffset: selection?.focusOffset ?? 0,
    };
}

/** True when a and b are the same selection. */
function sameSelection(a: SelectionPoints, b: SelectionPoints): boolean {
    return (
        a.anchorNode === b.anchorNode &&
        a.anchorOffset === b.anchorOffset &&
        a.focusNode === b.focusNode &&
        a.focusOffset === b.focusOffset
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
