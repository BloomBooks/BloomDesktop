// Tests of the transactions that the user's clicks and key presses open (pageHistoryInput.ts):
// docs/undo/APPROACHES.md section 3.0.2 rule 2 and case 7.7, and docs/undo/MINIMUM.md items 3
// and 4. Each test builds a page with a real history on it and dispatches events.
//
// A script's events are never trusted, so the tests mark the events that stand for the user's in
// a set and pass a check of that set as isFromUser. They also pass their own afterCurrentTask,
// which queues the function until finishTask() runs it: a test says where the browser's task
// ends. pageHistoryInput.browser.spec.ts covers the real defaults in a browser.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "./pageHistory";
import {
    attachInputTransactions,
    kDeleteTextUndoDescriptionL10nKey,
    kDropUndoDescriptionL10nKey,
    kIgnoredByPageHistoryInputSelector,
    kTypingUndoDescriptionL10nKey,
} from "./pageHistoryInput";
import {
    BegunTransaction,
    kCutUndoDescriptionL10nKey,
    kGenericUndoDescriptionL10nKey,
    kPasteUndoDescriptionL10nKey,
    PageHistory,
} from "./pageHistoryTypes";

const kPage =
    '<div class="bloom-editable" id="box1" contenteditable="true" lang="en"><p id="p1">Hello</p></div>' +
    '<div class="bloom-editable" id="box2" contenteditable="true" lang="fr"><p id="p2">Bonjour</p></div>' +
    '<div class="bloom-canvas" id="canvas" data-x="0"><img id="img1" src="a.png" alt=""></div>';

let page: HTMLElement;
let history: PageHistory;
let detach: (() => void) | undefined;
let otherFrame: HTMLIFrameElement;
let otherWindow: Window;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];
// Listeners that tests add for Bloom's handlers, removed after each test.
let removers: (() => void)[];

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = kPage;
    document.body.appendChild(page);
    history = createPageHistory({
        root: page,
        onUnmarkedChange: vi.fn(),
    });
    otherFrame = document.createElement("iframe");
    document.body.appendChild(otherFrame);
    otherWindow = otherFrame.contentWindow!;
    otherWindow.document.body.innerHTML =
        '<button id="toolButton">Tool</button><button id="undoButton" data-runs-undo><img id="undoIcon" alt=""></button>';
    userEvents = new WeakSet();
    endOfTask = [];
    removers = [];
    detach = attachInputTransactions(history, window, [otherWindow], {
        isFromUser: (event) => userEvents.has(event),
        afterCurrentTask: (fn) => endOfTask.push(fn),
    });
});

afterEach(() => {
    removers.forEach((remove) => remove());
    detach?.();
    history.dispose();
    page.remove();
    otherFrame.remove();
});

/** Run what was waiting for the end of the browser's task. */
function finishTask(): void {
    while (endOfTask.length > 0) endOfTask.shift()!();
}

function byId(id: string): HTMLElement {
    const element = page.querySelector<HTMLElement>(`#${id}`);
    expect(element, `test setup: #${id} should be on the page`).not.toBeNull();
    return element!;
}

function otherById(id: string): HTMLElement {
    const element = otherWindow.document.getElementById(id);
    expect(
        element,
        `test setup: #${id} should be in the other frame`,
    ).not.toBeNull();
    return element!;
}

/** Dispatch event on target as one the user made. */
function asUser<T extends Event>(target: EventTarget, event: T): T {
    userEvents.add(event);
    target.dispatchEvent(event);
    return event;
}

/** A listener standing for one of Bloom's handlers, on target in the bubble phase. */
function onBloomEvent(
    target: EventTarget,
    type: string,
    listener: (event: Event) => void,
): void {
    target.addEventListener(type, listener);
    removers.push(() => target.removeEventListener(type, listener));
}

function mouse(target: Element, type: string, buttons: number): MouseEvent {
    const view = target.ownerDocument.defaultView!;
    return asUser(
        target,
        new view.MouseEvent(type, { bubbles: true, cancelable: true, buttons }),
    );
}

/** pointerdown and mousedown, as one task. */
function press(target: Element): void {
    mouse(target, "pointerdown", 1);
    mouse(target, "mousedown", 1);
}

/** pointerup, mouseup and click, as one task, and the end of that task. */
function release(target: Element): void {
    mouse(target, "pointerup", 0);
    mouse(target, "mouseup", 0);
    mouse(target, "click", 0);
    finishTask();
}

function click(target: Element): void {
    press(target);
    release(target);
}

function keyDown(target: Element, init: KeyboardEventInit): KeyboardEvent {
    const view = target.ownerDocument.defaultView!;
    return asUser(
        target,
        new view.KeyboardEvent("keydown", {
            bubbles: true,
            cancelable: true,
            ...init,
        }),
    );
}

/**
 * A beforeinput of inputType on target, as the browser sends it before it changes the box, whose
 * getTargetRanges() gives ranges that are collapsed or not, as `collapsed` says. jsdom has no
 * getTargetRanges.
 */
function beforeInput(
    target: Element,
    inputType: string,
    collapsed: boolean,
): void {
    const event = new InputEvent("beforeinput", {
        bubbles: true,
        cancelable: true,
        inputType,
    });
    Object.defineProperty(event, "getTargetRanges", {
        value: () => [{ collapsed }],
    });
    asUser(target, event);
}

/** The input event that the browser sends after it changed the box. */
function input(target: Element, inputType: string): void {
    asUser(target, new InputEvent("input", { bubbles: true, inputType }));
}

/**
 * A key press that types one character at the end of box, as the browser would: keydown,
 * beforeinput, the change, then input. The task ends, but a typing transaction stays open.
 */
function typeCharacter(box: HTMLElement, character: string): void {
    keyDown(box, { key: character });
    beforeInput(box, "insertText", true);
    box.lastElementChild!.append(character);
    input(box, "insertText");
    finishTask();
}

function typeText(box: HTMLElement, text: string): void {
    for (const character of text) typeCharacter(box, character);
}

/** A Backspace that deletes the last character of box. */
function deleteCharacter(box: HTMLElement): void {
    keyDown(box, { key: "Backspace" });
    beforeInput(box, "deleteContentBackward", false);
    const paragraph = box.lastElementChild!;
    paragraph.textContent = paragraph.textContent!.slice(0, -1);
    input(box, "deleteContentBackward");
    finishTask();
}

/**
 * A Delete with the caret at the end of the text, which changes nothing: the browser sends
 * beforeinput with collapsed target ranges, and no input.
 */
function deleteNothing(box: HTMLElement): void {
    keyDown(box, { key: "Delete" });
    beforeInput(box, "deleteContentForward", true);
    finishTask();
}

/** Put the caret in node at offset. */
function putCaret(node: Node, offset: number): void {
    const range = document.createRange();
    range.setStart(node, offset);
    range.collapse(true);
    document.getSelection()!.removeAllRanges();
    document.getSelection()!.addRange(range);
}

/**
 * An Enter that starts a new, empty paragraph at the end of box, with the caret in it, as
 * CKEditor's "enter" command does in the keydown's task, with no input event.
 */
function pressEnter(box: HTMLElement): void {
    keyDown(box, { key: "Enter" });
    const paragraph = document.createElement("p");
    box.append(paragraph);
    putCaret(paragraph, 0);
    finishTask();
}

/** The text of each paragraph of box. */
function paragraphTexts(box: HTMLElement): string[] {
    return Array.from(box.querySelectorAll("p")).map((p) => p.textContent!);
}

/** Ctrl+Z as the user presses it; the handler that runs undo is not in this module. */
function pressUndoKey(target: Element = byId("box1")): void {
    keyDown(target, { key: "z", ctrlKey: true });
    finishTask();
}

/**
 * Undo every step, and return how many there were, with the page's text after each. The input
 * transaction must be closed first, since undo waits while one is open.
 */
async function undoEverything(): Promise<string[]> {
    expect(
        history.hasOpenTransaction,
        "test setup: no transaction should be open before undoing",
    ).toBe(false);
    const texts: string[] = [];
    for (let i = 0; i < 100 && history.undoDescriptionL10nKey; i++) {
        await history.undo();
        texts.push(page.textContent!);
    }
    return texts;
}

/** Make an undo step in the page frame: a click whose handler sets an attribute on #canvas. */
function makeStepWithClick(value: string): void {
    const canvas = byId("canvas");
    const listener = () => canvas.setAttribute("data-x", value);
    canvas.addEventListener("pointerdown", listener);
    click(canvas);
    canvas.removeEventListener("pointerdown", listener);
}

describe("a press in the page frame", () => {
    it("is one undo step with the general description when it changes the page", async () => {
        const canvas = byId("canvas");
        onBloomEvent(canvas, "pointerdown", () =>
            canvas.setAttribute("data-x", "1"),
        );
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        click(canvas);

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        await history.undo();
        expect(canvas.getAttribute("data-x")).toBe("0");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("opens its transaction before Bloom's handlers in the document run", () => {
        let openInHandler: boolean | undefined;
        onBloomEvent(document, "pointerdown", () => {
            openInHandler = history.hasOpenTransaction;
        });
        press(byId("canvas"));
        expect(openInHandler).toBe(true);
        release(byId("canvas"));
    });

    it("makes no step and keeps redo when it changes nothing", async () => {
        makeStepWithClick("1");
        await history.undo();
        expect(history.redoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );

        click(byId("img1"));
        pressUndoKey(byId("canvas"));

        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
    });

    it("opens nothing for an event that the user did not make", () => {
        const canvas = byId("canvas");
        canvas.dispatchEvent(
            new MouseEvent("pointerdown", { bubbles: true, buttons: 1 }),
        );
        expect(history.hasOpenTransaction).toBe(false);
    });

    it("joins a transaction that code has open, and does not end it (rule 7)", async () => {
        const transaction = history.beginTransaction("Test.Explicit");
        const canvas = byId("canvas");
        onBloomEvent(canvas, "pointerdown", () =>
            canvas.setAttribute("data-x", "1"),
        );

        click(canvas);
        expect(history.hasOpenTransaction).toBe(true);
        transaction.end();

        expect(history.undoDescriptionL10nKey).toBe("Test.Explicit");
        expect(await undoEverything()).toHaveLength(1);
    });

    it("makes no step of its own for Bloom's reaction to an earlier action that lands during it", async () => {
        makeStepWithClick("1");
        const canvas = byId("canvas");

        press(canvas);
        history.addToLatestTransaction(() =>
            byId("img1").setAttribute("src", "b.png"),
        );
        release(canvas);

        expect(
            await undoEverything(),
            "the reaction joined the step before, which one undo takes back",
        ).toHaveLength(1);
        expect(byId("img1").getAttribute("src")).toBe("a.png");
        expect(canvas.getAttribute("data-x")).toBe("0");
    });

    it("is separate from the press before it, even if that one's task has not ended", async () => {
        const canvas = byId("canvas");
        let value = 0;
        onBloomEvent(canvas, "pointerdown", () =>
            canvas.setAttribute("data-x", String(++value)),
        );
        press(canvas);
        mouse(canvas, "pointerup", 0);
        mouse(canvas, "mouseup", 0);
        // The task of this release has not ended when the next press comes.
        press(canvas);
        release(canvas);

        expect(await undoEverything()).toHaveLength(2);
    });
});

describe("a drag", () => {
    it("is one step from the press to the release, with Bloom's handlers for the release and the click", async () => {
        const canvas = byId("canvas");
        onBloomEvent(canvas, "pointerdown", () =>
            canvas.setAttribute("data-x", "1"),
        );
        onBloomEvent(document, "mousemove", () =>
            canvas.setAttribute("data-x", "2"),
        );
        onBloomEvent(document, "mouseup", () =>
            canvas.setAttribute("data-moved", "yes"),
        );
        onBloomEvent(canvas, "click", () =>
            canvas.setAttribute("data-clicked", "yes"),
        );

        press(canvas);
        finishTask();
        mouse(canvas, "mousemove", 1);
        finishTask();
        expect(
            history.hasOpenTransaction,
            "the drag's transaction stays open between events",
        ).toBe(true);
        release(canvas);

        expect(history.hasOpenTransaction).toBe(false);
        expect(canvas.getAttribute("data-clicked")).toBe("yes");
        expect(await undoEverything()).toHaveLength(1);
        expect(canvas.getAttribute("data-x")).toBe("0");
        expect(canvas.hasAttribute("data-moved")).toBe(false);
        expect(canvas.hasAttribute("data-clicked")).toBe(false);
    });

    it("includes a handler for the release that stops the event from propagating", async () => {
        const canvas = byId("canvas");
        onBloomEvent(canvas, "pointerdown", () =>
            canvas.setAttribute("data-x", "1"),
        );
        onBloomEvent(canvas, "mouseup", (e) => {
            e.stopPropagation();
            canvas.setAttribute("data-moved", "yes");
        });

        click(canvas);

        expect(await undoEverything()).toHaveLength(1);
        expect(canvas.hasAttribute("data-moved")).toBe(false);
    });

    it("ends at a mousemove with no button down, after Bloom's handler for it", async () => {
        const canvas = byId("canvas");
        onBloomEvent(canvas, "pointerdown", () =>
            canvas.setAttribute("data-x", "1"),
        );
        onBloomEvent(document, "mousemove", (e) => {
            if ((e as MouseEvent).buttons === 0)
                canvas.setAttribute("data-moved", "yes");
        });

        press(canvas);
        finishTask();
        mouse(canvas, "mousemove", 0);
        expect(history.hasOpenTransaction).toBe(true);
        finishTask();
        expect(history.hasOpenTransaction).toBe(false);

        expect(await undoEverything()).toHaveLength(1);
        expect(canvas.hasAttribute("data-moved")).toBe(false);
    });

    it("ends at pointercancel", () => {
        press(byId("canvas"));
        finishTask();
        mouse(byId("canvas"), "pointercancel", 0);
        finishTask();
        expect(history.hasOpenTransaction).toBe(false);
    });

    it("ends when the window loses the focus", () => {
        press(byId("canvas"));
        finishTask();
        asUser(window, new FocusEvent("blur"));
        finishTask();
        expect(history.hasOpenTransaction).toBe(false);
    });

    it("takes the key presses made during it", async () => {
        const box = byId("box1");
        press(byId("canvas"));
        finishTask();
        typeCharacter(box, "x");
        release(byId("canvas"));

        expect(history.hasOpenTransaction).toBe(false);
        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });
});

describe("typing", () => {
    it("merges consecutive key presses in one box into one step, kept open between them", async () => {
        const box = byId("box1");
        typeText(box, "abc");
        expect(history.hasOpenTransaction).toBe(true);

        pressUndoKey();

        expect(history.hasOpenTransaction).toBe(false);
        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("takes Bloom's reactions between key presses (rule 7)", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        // A change from a timer, after the task of the key press.
        box.setAttribute("data-reacted", "yes");
        typeText(box, "c");
        pressUndoKey();

        expect(await undoEverything()).toHaveLength(1);
        expect(box.hasAttribute("data-reacted")).toBe(false);
    });

    it("has no limit on its length", async () => {
        const box = byId("box1");
        typeText(box, "x".repeat(200));
        expect(history.hasOpenTransaction).toBe(true);
        pressUndoKey();

        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("starts a new step when deleting follows typing, which the typing after the deleting joins", async () => {
        const box = byId("box1");
        typeText(box, "abc");
        deleteCharacter(box);
        deleteCharacter(box);
        typeText(box, "xy");
        deleteCharacter(box);
        pressUndoKey();
        expect(page.textContent, "test setup").toBe("HelloaxBonjour");

        expect(await undoEverything()).toEqual([
            "HelloaxyBonjour",
            "HelloabcBonjour",
            "HelloBonjour",
        ]);
    });

    it("does not start a new step for a delete that changes nothing", async () => {
        const box = byId("box1");
        typeText(box, "abc");
        deleteNothing(box);
        typeText(box, "xy");
        pressUndoKey();

        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("keeps a modifier on its own, such as Shift, in the run", async () => {
        const box = byId("box1");
        typeText(box, "a");
        keyDown(box, { key: "Shift", shiftKey: true });
        finishTask();
        typeText(box, "B");
        pressUndoKey();

        expect(await undoEverything()).toHaveLength(1);
    });

    it.each(["Control", "Shift", "Alt", "AltGraph", "Meta", "CapsLock"])(
        "opens nothing for %s on its own",
        (key) => {
            let openInHandler: boolean | undefined;
            onBloomEvent(document, "keydown", () => {
                openInHandler = history.hasOpenTransaction;
            });
            keyDown(byId("box1"), { key, ctrlKey: key === "Control" });
            expect(openInHandler).toBe(false);
            finishTask();
            expect(history.hasOpenTransaction).toBe(false);
        },
    );

    it.each(["Escape", "F2", "Tab", "Dead", "Unidentified"])(
        "opens nothing, and leaves the step open, for %s, which neither types nor deletes",
        (key) => {
            const box = byId("box1");
            keyDown(box, { key });
            finishTask();
            expect(history.hasOpenTransaction).toBe(false);
            typeText(box, "a");
            keyDown(box, { key });
            finishTask();
            expect(history.hasOpenTransaction).toBe(true);
        },
    );

    it("keeps redo when Ctrl goes down before the Y of Ctrl+Y, after an undo", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        pressUndoKey();
        await history.undo();
        expect(page.textContent).toBe("HelloBonjour");
        expect(history.redoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );

        keyDown(box, { key: "Control", ctrlKey: true });
        expect(history.hasOpenTransaction).toBe(false);
        keyDown(box, { key: "y", ctrlKey: true });
        finishTask();

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.redoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );
        await history.redo();
        expect(page.textContent).toBe("HelloabBonjour");
    });

    it("opens nothing for a modifier on its own outside a text box", () => {
        keyDown(byId("canvas"), { key: "Shift", shiftKey: true });
        expect(history.hasOpenTransaction).toBe(false);
    });

    it("makes a command (a key press with Ctrl) a step of its own", async () => {
        const box = byId("box1");
        onBloomEvent(box, "keydown", (e) => {
            if (
                (e as KeyboardEvent).ctrlKey &&
                (e as KeyboardEvent).key === "b"
            )
                box.firstElementChild!.setAttribute("data-bold", "yes");
        });
        typeText(box, "ab");
        keyDown(box, { key: "b", ctrlKey: true });
        expect(history.hasOpenTransaction).toBe(true);
        finishTask();
        expect(history.hasOpenTransaction).toBe(false);
        typeText(box, "cd");
        pressUndoKey();

        expect(await undoEverything()).toEqual([
            "HelloabBonjour",
            "HelloabBonjour",
            "HelloBonjour",
        ]);
        expect(box.firstElementChild!.hasAttribute("data-bold")).toBe(false);
    });

    it("treats AltGr, which reports Ctrl and Alt, as typing", async () => {
        const box = byId("box1");
        typeText(box, "a");
        keyDown(box, { key: "@", ctrlKey: true, altKey: true });
        box.lastElementChild!.append("@");
        asUser(box, new InputEvent("input", { bubbles: true }));
        finishTask();
        pressUndoKey();

        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("closes a step at a navigation key", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        keyDown(box, { key: "ArrowLeft" });
        finishTask();
        typeText(box, "c");
        pressUndoKey();

        expect(await undoEverything()).toHaveLength(2);
    });

    it("makes each line typed a step, with the Enter that ends it", async () => {
        const box = byId("box1");
        typeText(box, "one");
        pressEnter(box);
        typeText(box, "two");
        pressEnter(box);
        typeText(box, "three");
        pressUndoKey();
        expect(
            paragraphTexts(box),
            "test setup: the typing should have made three paragraphs after Hello's",
        ).toEqual(["Helloone", "two", "three"]);

        const undone: { description: string; paragraphs: string[] }[] = [];
        while (history.undoDescriptionL10nKey) {
            const description = history.undoDescriptionL10nKey;
            await history.undo();
            undone.push({ description, paragraphs: paragraphTexts(box) });
        }

        expect(undone).toEqual([
            {
                description: kTypingUndoDescriptionL10nKey,
                paragraphs: ["Helloone", "two", ""],
            },
            {
                description: kTypingUndoDescriptionL10nKey,
                paragraphs: ["Helloone", ""],
            },
            {
                description: kTypingUndoDescriptionL10nKey,
                paragraphs: ["Hello"],
            },
        ]);
    });

    it("keeps an Enter in the middle of a line, and the typing after it, in the step", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        // CKEditor splits the paragraph at the caret, which goes to the start of the new one.
        keyDown(box, { key: "Enter" });
        const rest = document.createElement("p");
        rest.textContent = "llo";
        byId("p1").textContent = "He";
        box.append(rest);
        putCaret(rest.firstChild!, 0);
        finishTask();
        typeText(box, "xy");
        pressUndoKey();

        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("makes the typing after a line break (Shift+Enter) at the end of a line a new step", async () => {
        const box = byId("box1");
        typeText(box, "one");
        // Bloom puts in the line break itself (BloomField.InsertLineBreak), with no input event.
        keyDown(box, { key: "Enter", shiftKey: true });
        const lineBreak = document.createElement("span");
        lineBreak.className = "bloom-linebreak";
        const paragraph = box.lastElementChild!;
        paragraph.append(lineBreak, "\u200c");
        putCaret(paragraph.lastChild!, 1);
        finishTask();
        expect(history.hasOpenTransaction).toBe(true);
        typeText(box, "two");
        pressUndoKey();

        expect(await undoEverything()).toEqual([
            "Helloone\u200cBonjour",
            "HelloBonjour",
        ]);
        expect(box.querySelector(".bloom-linebreak")).toBeNull();
    });

    it("keeps an Enter that ends an IME composition in the composition's step", async () => {
        const box = byId("box1");
        keyDown(box, { key: "Process", keyCode: 229 });
        box.lastElementChild!.append("か");
        asUser(
            box,
            new InputEvent("input", { bubbles: true, isComposing: true }),
        );
        finishTask();
        keyDown(box, { key: "Enter", keyCode: 229, isComposing: true });
        finishTask();
        expect(history.hasOpenTransaction).toBe(true);
        pressUndoKey();

        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("makes Ctrl+Enter a command, a step of its own", async () => {
        const box = byId("box1");
        onBloomEvent(box, "keydown", (e) => {
            if ((e as KeyboardEvent).key === "Enter")
                box.firstElementChild!.setAttribute("data-command", "yes");
        });
        typeText(box, "ab");
        keyDown(box, { key: "Enter", ctrlKey: true });
        finishTask();
        pressUndoKey();

        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        expect(await undoEverything()).toEqual([
            "HelloabBonjour",
            "HelloBonjour",
        ]);
    });

    it("closes a step when the focus leaves the box", async () => {
        const box1 = byId("box1");
        typeText(box1, "ab");
        asUser(
            box1,
            new FocusEvent("focusout", { bubbles: true, relatedTarget: null }),
        );
        expect(history.hasOpenTransaction).toBe(false);
        typeText(box1, "c");
        pressUndoKey();

        expect(await undoEverything()).toHaveLength(2);
    });

    it("keeps the step open when the focus moves within the box", () => {
        const box1 = byId("box1");
        typeText(box1, "ab");
        asUser(
            byId("p1"),
            new FocusEvent("focusout", {
                bubbles: true,
                relatedTarget: box1,
            }),
        );
        expect(history.hasOpenTransaction).toBe(true);
    });

    it("closes a step when a key press goes to another box", async () => {
        typeText(byId("box1"), "ab");
        typeText(byId("box2"), "cd");
        pressUndoKey(byId("box2"));

        expect(await undoEverything()).toEqual([
            "HelloabBonjour",
            "HelloBonjour",
        ]);
    });

    it("closes a step at a press of the pointer outside the box", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        click(byId("canvas"));
        expect(history.hasOpenTransaction).toBe(false);
        typeText(box, "c");
        pressUndoKey();

        expect(await undoEverything()).toHaveLength(2);
    });

    it("keeps the step open at a click in the box that leaves the caret where it was", async () => {
        const box = byId("box1");
        putCaret(byId("p1").firstChild!, 5);
        typeText(box, "ab");
        click(box);
        expect(history.hasOpenTransaction).toBe(true);
        typeText(box, "c");
        pressUndoKey();

        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("closes a step at a click in the box that moves the caret", async () => {
        const box = byId("box1");
        putCaret(byId("p1").firstChild!, 5);
        typeText(box, "ab");
        // The browser moves the caret as the press's default action.
        onBloomEvent(box, "mousedown", () =>
            putCaret(byId("p1").firstChild!, 0),
        );
        click(box);
        expect(history.hasOpenTransaction).toBe(false);
        typeText(box, "c");
        pressUndoKey();

        expect(await undoEverything()).toHaveLength(2);
    });

    it("puts a composition in the typing step, and ends the step at the composition's end", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        for (let i = 0; i < 3; i++) {
            keyDown(box, { key: "Process", keyCode: 229 });
            box.lastElementChild!.append("か");
            asUser(
                box,
                new InputEvent("input", {
                    bubbles: true,
                    isComposing: true,
                    inputType: "insertCompositionText",
                }),
            );
            finishTask();
        }
        expect(history.hasOpenTransaction).toBe(true);
        asUser(box, new CompositionEvent("compositionend", { bubbles: true }));
        expect(history.hasOpenTransaction).toBe(false);
        typeText(box, "c");
        pressUndoKey();

        expect(await undoEverything()).toEqual([
            "HelloabかかかBonjour",
            "HelloBonjour",
        ]);
    });

    it("starts a typing step for input with no key press, such as a spelling suggestion, and goes on with it", async () => {
        const box = byId("box1");
        expect(history.hasOpenTransaction, "test setup").toBe(false);
        for (const text of ["a", "b"]) {
            beforeInput(box, "insertReplacementText", false);
            box.lastElementChild!.append(text);
            input(box, "insertReplacementText");
            finishTask();
        }
        expect(history.hasOpenTransaction).toBe(true);
        pressUndoKey();

        expect(history.undoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );
        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("starts no typing step for the input of a paste, which is a step of its own", () => {
        const box = byId("box1");
        asUser(box, new Event("paste", { bubbles: true }));
        beforeInput(box, "insertFromPaste", false);
        box.lastElementChild!.append("pasted");
        input(box, "insertFromPaste");
        finishTask();

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            kPasteUndoDescriptionL10nKey,
        );
    });

    it("ends the step at a compositionend that is not trusted, as Chromium sends at a commit", () => {
        const box = byId("box1");
        typeText(box, "ab");
        expect(history.hasOpenTransaction, "test setup").toBe(true);
        // Not through asUser: isFromUser says no.
        box.dispatchEvent(
            new CompositionEvent("compositionend", { bubbles: true }),
        );
        expect(history.hasOpenTransaction).toBe(false);
    });

    it("makes a paste a step of its own", async () => {
        const box = byId("box1");
        onBloomEvent(box, "paste", () =>
            box.lastElementChild!.append("PASTED"),
        );
        typeText(box, "ab");
        asUser(box, new Event("paste", { bubbles: true }));
        // The input that the paste makes is not typing.
        asUser(box, new InputEvent("input", { bubbles: true }));
        finishTask();
        expect(history.hasOpenTransaction).toBe(false);
        typeText(box, "c");
        pressUndoKey();

        expect(await undoEverything()).toEqual([
            "HelloabPASTEDBonjour",
            "HelloabBonjour",
            "HelloBonjour",
        ]);
    });

    it("puts a paste from Ctrl+V in the transaction of its key press", async () => {
        const box = byId("box1");
        onBloomEvent(box, "paste", () =>
            box.lastElementChild!.append("PASTED"),
        );
        typeText(box, "ab");
        keyDown(box, { key: "v", ctrlKey: true });
        asUser(box, new Event("paste", { bubbles: true }));
        finishTask();
        pressUndoKey();

        expect(await undoEverything()).toEqual([
            "HelloabBonjour",
            "HelloBonjour",
        ]);
    });

    it("makes a drop a step of its own", async () => {
        const box = byId("box1");
        onBloomEvent(box, "drop", () =>
            box.lastElementChild!.append("DROPPED"),
        );
        typeText(box, "ab");
        asUser(box, new Event("drop", { bubbles: true }));
        finishTask();
        pressUndoKey();

        expect(await undoEverything()).toHaveLength(2);
    });

    it("starts again after something else ends its transaction", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        history.clearUndoAndRedo("a test");
        expect(history.hasOpenTransaction).toBe(false);
        typeText(box, "c");
        expect(history.hasOpenTransaction).toBe(true);
        pressUndoKey();

        expect(await undoEverything()).toEqual(["HelloabBonjour"]);
    });
});

describe("the description of a step that no code describes", () => {
    it("is typing for a typing step, and deleting text for a step of Backspace presses", async () => {
        const box = byId("box1");
        typeText(box, "abc");
        // Going from typing to deleting ends the typing step.
        deleteCharacter(box);
        expect(history.undoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );
        pressUndoKey();

        expect(history.undoDescriptionL10nKey).toBe(
            kDeleteTextUndoDescriptionL10nKey,
        );
        expect(await undoEverything()).toEqual([
            "HelloabcBonjour",
            "HelloBonjour",
        ]);
    });

    it.each([
        ["paste", kPasteUndoDescriptionL10nKey],
        ["cut", kCutUndoDescriptionL10nKey],
        ["drop", kDropUndoDescriptionL10nKey],
    ])("is what the user did for a %s", (type, description) => {
        const box = byId("box1");
        onBloomEvent(box, type, () => box.lastElementChild!.append("CHANGED"));
        asUser(box, new Event(type, { bubbles: true }));
        finishTask();

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(description);
    });

    it("stays the description that a transaction nested in the step gave it", () => {
        const box = byId("box1");
        typeText(box, "a");
        void history.doTransaction("Test.Reaction", () =>
            box.lastElementChild!.append("b"),
        );
        typeText(box, "c");
        pressUndoKey();

        expect(page.textContent).toBe("HelloabcBonjour");
        expect(history.undoDescriptionL10nKey).toBe("Test.Reaction");
    });

    it("stays the general one for a press with no description", () => {
        makeStepWithClick("1");
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
    });
});

describe("the undo and redo keys", () => {
    it.each([
        ["Ctrl+Z", { key: "z", ctrlKey: true }],
        ["Ctrl+Y", { key: "y", ctrlKey: true }],
        ["Ctrl+Shift+Z", { key: "Z", ctrlKey: true, shiftKey: true }],
        [
            "Ctrl+Z on a layout whose Z key types another letter",
            { key: "я", keyCode: 90, ctrlKey: true },
        ],
    ])("%s ends a typing step and opens nothing", (_name, init) => {
        const box = byId("box1");
        typeText(box, "ab");
        expect(history.hasOpenTransaction).toBe(true);

        let openInHandler: boolean | undefined;
        onBloomEvent(document, "keydown", () => {
            openInHandler = history.hasOpenTransaction;
        });
        keyDown(box, init);

        expect(openInHandler).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );
    });

    it("opens nothing in another frame either", () => {
        let openInHandler: boolean | undefined;
        onBloomEvent(otherWindow.document, "keydown", () => {
            openInHandler = history.hasOpenTransaction;
        });
        keyDown(otherById("toolButton"), { key: "z", ctrlKey: true });
        expect(openInHandler).toBe(false);
    });
});

describe("input in another frame", () => {
    it("ends the history when a click there changes the page", async () => {
        makeStepWithClick("1");
        makeStepWithClick("2");
        await history.undo();
        expect(history.redoDescriptionL10nKey).toBeDefined();
        const button = otherById("toolButton");
        onBloomEvent(button, "click", () =>
            byId("img1").setAttribute("src", "b.png"),
        );

        click(button);

        expect(byId("img1").getAttribute("src")).toBe("b.png");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("names the event that ended the history in the reason it gives", () => {
        const clear = vi.spyOn(history, "clearUndoAndRedo");
        const button = otherById("toolButton");
        onBloomEvent(button, "click", () =>
            byId("img1").setAttribute("src", "b.png"),
        );

        click(button);

        expect(clear).toHaveBeenCalledTimes(1);
        const reason = clear.mock.calls[0][0];
        expect(reason).toMatch(
            /^a change with no description, after this input: /,
        );
        expect(reason, "the reason should name the press").toContain(
            "pointerdown",
        );
        expect(reason, "the reason should name what was clicked").toContain(
            "toolButton",
        );
    });

    it("ends the history when the latest step has the same description and there is no redo", () => {
        makeStepWithClick("1");
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        expect(history.redoDescriptionL10nKey).toBeUndefined();
        const button = otherById("toolButton");
        onBloomEvent(button, "pointerdown", () =>
            byId("img1").setAttribute("src", "b.png"),
        );

        click(button);

        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("ends the history when a key press there changes the page", () => {
        makeStepWithClick("1");
        const button = otherById("toolButton");
        onBloomEvent(button, "keydown", () =>
            byId("img1").setAttribute("src", "b.png"),
        );

        keyDown(button, { key: "Enter" });
        finishTask();

        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("keeps the history when a click there runs an explicit transaction, which describes the step", async () => {
        makeStepWithClick("1");
        const button = otherById("toolButton");
        onBloomEvent(button, "click", () => {
            void history.doTransaction("Test.ToolCommand", () =>
                byId("img1").setAttribute("src", "b.png"),
            );
        });

        click(button);

        expect(history.undoDescriptionL10nKey).toBe("Test.ToolCommand");
        expect(await undoEverything()).toHaveLength(2);
    });

    it("keeps the history when a click there opens a dialog's transaction, which makes the step when it ends", async () => {
        makeStepWithClick("1");
        const button = otherById("toolButton");
        let dialog: BegunTransaction | undefined;
        onBloomEvent(button, "click", () => {
            dialog = history.beginTransaction("Test.Dialog");
            byId("img1").setAttribute("src", "b.png");
        });

        click(button);
        expect(
            dialog,
            "test setup: the click should open the dialog",
        ).toBeDefined();
        expect(
            history.hasOpenTransaction,
            "the click's transaction waits for the dialog's",
        ).toBe(true);
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        expect(dialog!.end()).toBe(true);

        expect(history.undoDescriptionL10nKey).toBe("Test.Dialog");
        expect(await undoEverything()).toHaveLength(2);
        expect(byId("img1").getAttribute("src")).toBe("a.png");
    });

    it("ends the history when a dialog that a click there opened is cancelled, after the click changed the page", () => {
        makeStepWithClick("1");
        const button = otherById("toolButton");
        let dialog: BegunTransaction | undefined;
        onBloomEvent(button, "pointerdown", () =>
            byId("img1").setAttribute("src", "b.png"),
        );
        onBloomEvent(button, "click", () => {
            dialog = history.beginTransaction("Test.Dialog");
            byId("box2").setAttribute("lang", "es");
        });

        click(button);
        expect(
            history.hasOpenTransaction,
            "test setup: the click's transaction waits for the dialog's",
        ).toBe(true);
        expect(
            history.undoDescriptionL10nKey,
            "test setup: the step from before is still there",
        ).toBe(kGenericUndoDescriptionL10nKey);
        dialog!.cancel();

        expect(byId("box2").getAttribute("lang")).toBe("fr");
        expect(
            byId("img1").getAttribute("src"),
            "the click's own change stays on the page",
        ).toBe("b.png");
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("keeps the history when Bloom's reaction to an earlier action lands during a press there that changes nothing", async () => {
        makeStepWithClick("1");
        const button = otherById("toolButton");

        press(button);
        expect(
            history.hasOpenTransaction,
            "test setup: the press holds a transaction open",
        ).toBe(true);
        history.addToLatestTransaction(() =>
            byId("img1").setAttribute("src", "b.png"),
        );
        release(button);

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        expect(
            await undoEverything(),
            "the reaction joined the step before, which one undo takes back",
        ).toHaveLength(1);
        expect(byId("img1").getAttribute("src")).toBe("a.png");
        expect(byId("canvas").getAttribute("data-x")).toBe("0");
    });

    it("keeps the history when a click there changes nothing in the page", () => {
        makeStepWithClick("1");
        click(otherById("toolButton"));
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
    });

    it("ends a typing step, so that the page's typing is a step of its own", async () => {
        typeText(byId("box1"), "ab");
        click(otherById("toolButton"));

        expect(history.hasOpenTransaction).toBe(false);
        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("opens nothing for a press or click on the Undo button, which ends a typing step", async () => {
        typeText(byId("box1"), "ab");
        expect(history.hasOpenTransaction).toBe(true);
        const undoButton = otherById("undoButton");
        const openInHandlers: boolean[] = [];
        for (const type of ["pointerdown", "mousedown", "mouseup", "click"])
            onBloomEvent(undoButton, type, () =>
                openInHandlers.push(history.hasOpenTransaction),
            );
        // The top bar posts to C#, which runs the undo in the page frame; here it runs at once,
        // in the click's task, as the worst case.
        onBloomEvent(undoButton, "click", () => void history.undo());

        click(otherById("undoIcon"));

        expect(openInHandlers).toEqual([false, false, false, false]);
        expect(page.textContent).toBe("HelloBonjour");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );
    });

    it("opens nothing for a key press on the Undo button", () => {
        let openInHandler: boolean | undefined;
        onBloomEvent(otherById("undoButton"), "keydown", () => {
            openInHandler = history.hasOpenTransaction;
        });
        keyDown(otherById("undoButton"), { key: "Enter" });
        expect(openInHandler).toBe(false);
    });
});

describe("detaching", () => {
    it("ends the transaction it holds and stops listening", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        detach!();
        detach = undefined;
        expect(history.hasOpenTransaction).toBe(false);

        keyDown(box, { key: "c" });
        expect(history.hasOpenTransaction).toBe(false);
        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });
});

describe("a tool that watches the history (kIgnoredByPageHistoryInputSelector)", () => {
    // A button inside such a tool, in the other frame, as the Show Page History dialog is in the
    // root window.
    function toolButton(): HTMLElement {
        const tool = otherWindow.document.createElement("div");
        tool.setAttribute(kIgnoredByPageHistoryInputSelector.slice(1, -1), "");
        tool.innerHTML = '<button id="watcherButton">Copy</button>';
        otherWindow.document.body.appendChild(tool);
        expect(
            otherById("watcherButton").closest(
                kIgnoredByPageHistoryInputSelector,
            ),
            "test setup: the button should be inside the tool",
        ).not.toBeNull();
        return otherById("watcherButton");
    }

    it("neither ends the typing step nor opens a transaction when clicked", async () => {
        const box = byId("box1");
        typeText(box, "ab");
        const button = toolButton();
        click(button);
        expect(
            history.hasOpenTransaction,
            "the typing step should still be open",
        ).toBe(true);
        typeText(box, "c");
        click(byId("canvas"));

        expect(await undoEverything()).toEqual(["HelloBonjour"]);
    });

    it("does not end the history when the page changes while it is clicked", () => {
        makeStepWithClick("1");
        const button = toolButton();
        onBloomEvent(button, "pointerdown", () =>
            byId("img1").setAttribute("src", "b.png"),
        );

        click(button);

        expect(
            history.undoDescriptionL10nKey,
            "the change joins the latest step (rule 1) instead of ending the history",
        ).toBe(kGenericUndoDescriptionL10nKey);
    });
});

describe("onOpened", () => {
    it("gets the event and the kind of each transaction that input opens, and nothing for input that opens none", () => {
        detach!();
        const opened =
            vi.fn<
                (event: Event, kind: "pointer" | "event" | "typing") => void
            >();
        detach = attachInputTransactions(history, window, [otherWindow], {
            isFromUser: (event) => userEvents.has(event),
            afterCurrentTask: (fn) => endOfTask.push(fn),
            onOpened: opened,
        });
        const box = byId("box1");
        const firstKey = keyDown(box, { key: "a" });
        keyDown(box, { key: "b" });
        finishTask();
        click(byId("canvas"));

        expect(
            opened.mock.calls.map((call) => [call[0].type, call[1]]),
            "the second key joins the typing step that the first opened",
        ).toEqual([
            ["keydown", "typing"],
            ["pointerdown", "pointer"],
        ]);
        expect(opened.mock.calls[0][0]).toBe(firstKey);
    });
});
