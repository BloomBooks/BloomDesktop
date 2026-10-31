// Show Invisibles (holding Ctrl+Shift+Space in a text box) and the page history. Showing the
// invisible characters is a view of the page, not an edit: it must make no undo step, clear no
// redo, and leave the box with the same nodes, text and caret as before. While the keys are held,
// each symbol is in a span of its own, as before the page history. As in pageHistoryInputSpec.ts, the
// user's events are the ones in userEvents, and a test says where the browser's task ends by
// calling finishTask().
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import "../../lib/jquery.qtip.js"; // puts qtip into $.fn
import { createPageHistory } from "../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../pageHistory/editTimeMarkup";
import {
    attachInputTransactions,
    kTypingUndoDescriptionL10nKey,
} from "../pageHistory/pageHistoryInput";
import { attachUndoAndRedoKeys } from "../pageHistory/pageHistoryKeys";
import { PageHistory } from "../pageHistory/pageHistoryTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        access.history ? access.history.doOutsideHistory(fn) : fn(),
    clearUndoAndRedo: (reason: string) =>
        access.history?.clearUndoAndRedo(reason),
}));

import { listenForShowInvisiblesKeys } from "./showInvisibles";

const kZeroWidthSpace = "\u200B";
const kNoBreakSpace = "\u00A0";
// The symbols that stand for them while the invisibles show (SymChar's glyphs).
const kZeroWidthSpaceSymbol = "\uE081";
const kNoBreakSpaceSymbol = "\uE082";
const kFirstText = `Hello${kNoBreakSpace}big`;
const kSecondText = `world${kZeroWidthSpace}again`;

let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn<(whatChanged: string) => void>>;
let container: HTMLElement;
let page: HTMLElement;
let editable: HTMLElement;
let firstText: Text;
let secondText: Text;
let detach: () => void;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];

beforeEach(() => {
    document.body.innerHTML = `<div id="page-scaling-container"><div class="bloom-page"><div class="bloom-editable" contenteditable="true" lang="en"><p>${kFirstText}<b>${kSecondText}</b></p></div></div></div>`;
    container = document.getElementById("page-scaling-container")!;
    page = document.querySelector(".bloom-page")!;
    editable = page.querySelector(".bloom-editable")!;
    firstText = editable.querySelector("p")!.firstChild as Text;
    secondText = editable.querySelector("b")!.firstChild as Text;
    // Sanity check of the test data.
    expect(firstText.data).toBe(kFirstText);
    expect(secondText.data).toBe(kSecondText);

    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    access.history = history;
    userEvents = new WeakSet();
    endOfTask = [];
    detach = attachInputTransactions(history, window, [], {
        isFromUser: (event) => userEvents.has(event),
        afterCurrentTask: (fn) => endOfTask.push(fn),
    });
    listenForShowInvisiblesKeys(page);
});

afterEach(() => {
    detach();
    history.dispose();
    access.history = undefined;
    document.body.innerHTML = "";
});

function finishTask(): void {
    while (endOfTask.length > 0) endOfTask.shift()!();
}

/** A key event from the user on the box, in a task of its own. */
function key(
    type: "keydown" | "keyup",
    keyValue: string,
    modifiers?: { ctrlKey?: boolean; shiftKey?: boolean },
): void {
    const event = new KeyboardEvent(type, {
        key: keyValue,
        bubbles: true,
        cancelable: true,
        ...modifiers,
    });
    userEvents.add(event);
    editable.dispatchEvent(event);
    finishTask();
}

/** Type letters at the end of the second text node, as the browser would. */
function type(letters: string): void {
    for (const letter of letters) {
        key("keydown", letter);
        secondText.appendData(letter);
        const input = new InputEvent("input", { bubbles: true });
        userEvents.add(input);
        editable.dispatchEvent(input);
        finishTask();
        key("keyup", letter);
    }
}

/** Hold Ctrl+Shift+Space (the key repeats while it is held), then release the space. */
function pressShowInvisibles(): void {
    holdShowInvisibles();
    releaseShowInvisibles();
}

function holdShowInvisibles(): void {
    const ctrlShift = { ctrlKey: true, shiftKey: true };
    key("keydown", "Control", { ctrlKey: true });
    key("keydown", "Shift", ctrlShift);
    key("keydown", " ", ctrlShift);
    key("keydown", " ", ctrlShift);
    key("keydown", " ", ctrlShift);
}

function releaseShowInvisibles(): void {
    key("keyup", " ", { ctrlKey: true, shiftKey: true });
    key("keyup", "Shift", { ctrlKey: true });
    key("keyup", "Control");
}

/** Every node inside root, in document order. */
function allNodesIn(root: Node): Node[] {
    const nodes: Node[] = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_ALL);
    while (walker.nextNode()) nodes.push(walker.currentNode);
    return nodes;
}

/** Expect the box to hold exactly the nodes in before, the same objects in the same order. */
function expectSameNodes(before: Node[]): void {
    const now = allNodesIn(editable);
    expect(now.length).toBe(before.length);
    now.forEach((node, i) => expect(node).toBe(before[i]));
    expect(editable.querySelector(".invisibles")).toBe(null);
}

/** The symbols that the box shows now, each in its span. */
function shownSymbols(): string[] {
    return Array.from(
        editable.querySelectorAll("span.invisibles"),
        (span) => span.textContent!,
    );
}

/** The text of each tooltip that is in the page scaling container. */
function tooltipTexts(): string[] {
    return Array.from(
        container.querySelectorAll(".qtip"),
        (tooltip) => tooltip.textContent!,
    ).sort();
}

function putCaret(node: Text, offset: number): void {
    document.getSelection()!.collapse(node, offset);
}

function expectCaret(node: Text, offset: number): void {
    const selection = document.getSelection()!;
    expect(selection.anchorNode).toBe(node);
    expect(selection.anchorOffset).toBe(offset);
    expect(selection.isCollapsed).toBe(true);
}

describe("Show Invisibles and the page history", () => {
    it("shows each invisible character as a symbol in a span while held, and then the nodes from before", () => {
        const nodesBefore = allNodesIn(editable);

        holdShowInvisibles();

        // The text nodes keep the text before their first invisible character.
        expect(editable.querySelector("p")!.firstChild).toBe(firstText);
        expect(firstText.data).toBe("Hello");
        expect(editable.querySelector("b")!.firstChild).toBe(secondText);
        expect(secondText.data).toBe("world");
        expect(shownSymbols()).toEqual([
            kNoBreakSpaceSymbol,
            kZeroWidthSpaceSymbol,
        ]);
        expect(editable.textContent).toBe(
            `Hello${kNoBreakSpaceSymbol}bigworld${kZeroWidthSpaceSymbol}again`,
        );
        // One explanation for each kind of invisible character.
        expect(tooltipTexts()).toEqual(["No-Break Space", "Zero Width Space"]);

        releaseShowInvisibles();

        expectSameNodes(nodesBefore);
        expect(firstText.data).toBe(kFirstText);
        expect(secondText.data).toBe(kSecondText);
        expect(tooltipTexts()).toEqual([]);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("makes no undo step: the next undo undoes the typing before it, and redo puts it back", async () => {
        type("xyz");
        const nodesBefore = allNodesIn(editable);
        pressShowInvisibles();
        expectSameNodes(nodesBefore);

        expect(secondText.data).toBe(`${kSecondText}xyz`);
        expect(history.undoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );
        await history.undo();
        expect(secondText.data).toBe(kSecondText);
        expect(history.undoDescriptionL10nKey).toBe(undefined);

        await history.redo();
        expect(secondText.data).toBe(`${kSecondText}xyz`);
        expect(history.redoDescriptionL10nKey).toBe(undefined);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("keeps redo after an undo", async () => {
        type("xyz");
        key("keydown", "ArrowLeft"); // ends the typing step
        await history.undo();
        expect(secondText.data).toBe(kSecondText);
        expect(history.redoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );

        const nodesBefore = allNodesIn(editable);
        holdShowInvisibles();
        expect(history.redoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );
        releaseShowInvisibles();
        expectSameNodes(nodesBefore);
        expect(history.redoDescriptionL10nKey).toBe(
            kTypingUndoDescriptionL10nKey,
        );

        await history.redo();
        expect(secondText.data).toBe(`${kSecondText}xyz`);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("puts back exactly the text that was there, U+200B included, in the same nodes", () => {
        const nodesBefore = allNodesIn(editable);
        const textBefore = editable.textContent;
        expect(textBefore).toContain(kZeroWidthSpace);

        pressShowInvisibles();

        expectSameNodes(nodesBefore);
        expect(editable.textContent).toBe(textBefore);
        expect(firstText.data).toBe(kFirstText);
        expect(secondText.data).toBe(kSecondText);
        expect(history.undoDescriptionL10nKey).toBe(undefined);
    });

    it("keeps the caret just after an invisible character where it was", () => {
        editable.focus();
        // Just after the U+200B.
        putCaret(secondText, 6);

        holdShowInvisibles();
        // At the start of the text after the symbol.
        const selection = document.getSelection()!;
        expect(selection.anchorNode!.previousSibling).toBe(
            editable.querySelectorAll("span.invisibles")[1],
        );
        expect(selection.anchorOffset).toBe(0);
        releaseShowInvisibles();
        expectCaret(secondText, 6);
    });

    it("keeps the caret just before an invisible character where it was", () => {
        editable.focus();
        putCaret(secondText, 5);

        holdShowInvisibles();
        expectCaret(secondText, 5);
        releaseShowInvisibles();
        expectCaret(secondText, 5);
    });

    it("keeps the caret after an invisible character that ends the text where it was", () => {
        history.doOutsideHistory(
            () => (firstText.data = `Hello${kNoBreakSpace}`),
        );
        const nodesBefore = allNodesIn(editable);
        editable.focus();
        putCaret(firstText, 6);

        holdShowInvisibles();
        // After the symbol's span, before the <b>.
        const selection = document.getSelection()!;
        expect(selection.anchorNode).toBe(editable.querySelector("p"));
        expect(selection.anchorNode!.childNodes[selection.anchorOffset]).toBe(
            editable.querySelector("b"),
        );
        releaseShowInvisibles();
        expectCaret(firstText, 6);
        expectSameNodes(nodesBefore);
    });

    it("keeps a selection across invisible characters where it was", () => {
        editable.focus();
        document.getSelection()!.setBaseAndExtent(firstText, 2, secondText, 8);

        holdShowInvisibles();
        expect(document.getSelection()!.toString()).toBe(
            `llo${kNoBreakSpaceSymbol}bigworld${kZeroWidthSpaceSymbol}ag`,
        );
        releaseShowInvisibles();

        const selection = document.getSelection()!;
        expect(selection.anchorNode).toBe(firstText);
        expect(selection.anchorOffset).toBe(2);
        expect(selection.focusNode).toBe(secondText);
        expect(selection.focusOffset).toBe(8);
    });

    it("puts the characters back before an undo or redo that comes while they show", async () => {
        type("xyz");
        key("keydown", "ArrowLeft");
        await history.undo();

        const nodesBefore = allNodesIn(editable);
        holdShowInvisibles();
        // Ctrl+Shift+Z, taken by the page history's keys ahead of the box's own handlers.
        await history.redo();

        expect(secondText.data).toBe(`${kSecondText}xyz`);
        expect(firstText.data).toBe(kFirstText);
        expectSameNodes(nodesBefore);
        releaseShowInvisibles();
        expect(secondText.data).toBe(`${kSecondText}xyz`);
        await history.undo();
        expect(secondText.data).toBe(kSecondText);
    });

    it("hides the symbols at a Ctrl+Shift+Z with nothing to redo, which the page history's keys take", () => {
        const detachKeys = attachUndoAndRedoKeys(window, [], {
            undo: () => void history.undo(),
            redo: () => void history.redo(),
            isInChangeLayoutMode: () => false,
        });
        try {
            const nodesBefore = allNodesIn(editable);
            holdShowInvisibles();
            // Sanity checks: the symbols show, and there is nothing to redo.
            expect(shownSymbols().length).toBe(2);
            expect(history.redoDescriptionL10nKey).toBe(undefined);

            key("keydown", "Z", { ctrlKey: true, shiftKey: true });

            expectSameNodes(nodesBefore);
            expect(firstText.data).toBe(kFirstText);
            expect(secondText.data).toBe(kSecondText);
        } finally {
            detachKeys();
        }
    });

    it("leaves CKEditor's filling character alone", () => {
        // A box where CKEditor keeps its filling character in an empty <i>, as it does after
        // Ctrl+I with nothing selected.
        const italic = document.createElement("i");
        const fillingCharNode = document.createTextNode(kZeroWidthSpace);
        italic.appendChild(fillingCharNode);
        history.doOutsideHistory(() =>
            editable.querySelector("p")!.appendChild(italic),
        );
        const removeCustomData = vi.fn();
        (
            editable as unknown as {
                bloomCkEditor: { editable(): object };
            }
        ).bloomCkEditor = {
            editable: () => ({
                getCustomData: (customDataKey: string) =>
                    customDataKey === "cke-fillingChar"
                        ? { $: fillingCharNode }
                        : undefined,
                removeCustomData,
            }),
        };
        editable.focus();
        putCaret(fillingCharNode, 1);

        const nodesBefore = allNodesIn(editable);

        holdShowInvisibles();
        expect(fillingCharNode.data).toBe(kZeroWidthSpace);
        expect(shownSymbols()).toEqual([
            kNoBreakSpaceSymbol,
            kZeroWidthSpaceSymbol,
        ]);
        releaseShowInvisibles();

        expectSameNodes(nodesBefore);

        expect(fillingCharNode.data).toBe(kZeroWidthSpace);
        expect(italic.firstChild).toBe(fillingCharNode);
        expect(removeCustomData).not.toHaveBeenCalled();
        expectCaret(fillingCharNode, 1);
        expect(history.undoDescriptionL10nKey).toBe(undefined);
    });
});
