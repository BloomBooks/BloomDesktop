// The few page history cases where jsdom may differ from the engine behind WebView2: the timing
// of MutationObserver delivery, and the mutations that real editing commands make. These run in
// headless Edge (the "browser" vitest project). The rest of the contract is tested in
// pageHistorySpec.ts.
import { afterEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "./pageHistory";
import type { PageHistory } from "./pageHistoryTypes";

let page: HTMLElement | undefined;
let history: PageHistory | undefined;

const kPage =
    '<div class="bloom-translationGroup">' +
    '<div class="bloom-editable" id="box1" contenteditable="true" lang="en"><p id="p1">Hello <b>bold</b> world</p></div>' +
    "</div>" +
    '<div class="bloom-canvas" id="canvas"><img id="img1" src="a.png" alt=""></div>';

function setUp(onUnmarkedChange = vi.fn<(whatChanged: string) => void>()): {
    page: HTMLElement;
    history: PageHistory;
} {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = kPage;
    document.body.appendChild(page);
    history = createPageHistory({ root: page, onUnmarkedChange });
    return { page, history };
}

afterEach(() => {
    history?.dispose();
    page?.remove();
    history = undefined;
    page = undefined;
});

// MutationObserver queues its callback as a microtask when a change is made, before this await
// queues its own continuation, so by the time this resolves the observer has delivered every
// change made before the call.
async function letObserverDeliver(): Promise<void> {
    await Promise.resolve();
}

function byId(root: Element, id: string): HTMLElement {
    const element = root.querySelector<HTMLElement>(`#${id}`);
    expect(element, `test setup: #${id} should be on the page`).not.toBeNull();
    return element as HTMLElement;
}

function nodesIn(root: Node): Node[] {
    const result: Node[] = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_ALL);
    for (
        let node: Node | null = walker.currentNode;
        node;
        node = walker.nextNode()
    ) {
        result.push(node);
    }
    return result;
}

function expectSameNodes(actual: Node[], expected: Node[], when: string): void {
    expect(actual.length, `${when}: the number of nodes on the page`).toBe(
        expected.length,
    );
    actual.forEach((node, i) => {
        expect(
            node === expected[i],
            `${when}: node ${i} (${expected[i].nodeName}) should be the very object that was there before`,
        ).toBe(true);
    });
}

// Puts the caret at the end of an element's text.
function placeCaretAtEnd(element: HTMLElement): void {
    element.focus();
    const range = document.createRange();
    range.selectNodeContents(element);
    range.collapse(false);
    const selection = window.getSelection();
    expect(
        selection,
        "test setup: the document should have a selection",
    ).not.toBeNull();
    selection?.removeAllRanges();
    selection?.addRange(range);
}

describe("page history in a real browser", () => {
    it("undoes and redoes the mutations of real editing commands exactly", async () => {
        const { page, history } = setUp();
        const box1 = byId(page, "box1");
        const startHtml = page.outerHTML;
        const startNodes = nodesIn(page);

        const t = history.beginTransaction("Typing");
        placeCaretAtEnd(box1);
        expect(document.execCommand("insertText", false, " more")).toBe(true);
        document.execCommand("insertParagraph");
        document.execCommand("insertText", false, "second line");
        document.execCommand("selectAll");
        document.execCommand("bold");
        placeCaretAtEnd(box1);
        document.execCommand("delete");
        document.execCommand("delete");
        t.end();
        const afterHtml = page.outerHTML;
        const afterNodes = nodesIn(page);
        expect(
            afterHtml,
            "test setup: the commands should have changed the page",
        ).not.toBe(startHtml);

        await history.undo();
        expect(page.outerHTML, "after undo").toBe(startHtml);
        expectSameNodes(nodesIn(page), startNodes, "after undo");
        await history.redo();
        expect(page.outerHTML, "after redo").toBe(afterHtml);
        expectSameNodes(nodesIn(page), afterNodes, "after redo");
    });

    it("joins a change outside any transaction to the latest step once the microtask runs", async () => {
        const unmarked = vi.fn<(whatChanged: string) => void>();
        const { page, history } = setUp(unmarked);
        const startHtml = page.outerHTML;
        await history.doTransaction("A", () => {
            byId(page, "img1").setAttribute("src", "b.png");
        });
        await history.doTransaction("B", () => {
            byId(page, "box1").setAttribute("lang", "de");
        });
        await history.undo();
        expect(history.redoDescriptionL10nKey).toBe("B");

        // As a picture's load listener would, after the action that caused it.
        byId(page, "img1").setAttribute("style", "width: 20px");
        await letObserverDeliver();
        expect(unmarked).toHaveBeenCalledTimes(1);
        expect(history.redoDescriptionL10nKey).toBeUndefined();

        await history.undo();
        expect(page.outerHTML).toBe(startHtml);
    });

    it("calls onAfterUndoOrRedo before a component's own MutationObserver sees the restore", async () => {
        const { page, history } = setUp();
        await history.doTransaction("A", () => {
            byId(page, "img1").setAttribute("src", "b.png");
        });
        let componentObserverRan = false;
        const componentObserver = new MutationObserver(() => {
            componentObserverRan = true;
        });
        componentObserver.observe(page, { attributes: true, subtree: true });
        let sawComponentObserverRun: boolean | undefined;
        history.onAfterUndoOrRedo(() => {
            sawComponentObserverRun = componentObserverRan;
        });
        await history.undo();
        componentObserver.disconnect();
        expect(
            sawComponentObserverRun,
            "the listener should have been called",
        ).toBeDefined();
        expect(sawComponentObserverRun).toBe(false);
    });

    it("refuses an undo that would put back a subtree changed after the observer stopped seeing it", async () => {
        const { page, history } = setUp();
        const canvas = byId(page, "canvas");
        await history.doTransaction("Delete", () => {
            canvas.remove();
        });
        // After this delivery the observer no longer sees changes inside the removed subtree.
        await letObserverDeliver();
        byId(canvas, "img1").setAttribute("src", "changed-while-detached.png");
        await letObserverDeliver();
        const beforeHtml = page.outerHTML;

        await history.undo();
        expect(canvas.isConnected).toBe(false);
        expect(page.outerHTML).toBe(beforeHtml);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });
});

describe("page history in a real browser: a subtree changed after removal", () => {
    // jsdom has no transient observers, so this case is checked here: a change that a transaction
    // makes to a subtree just after removing it must be recorded, and undo must put back the
    // subtree as it was.
    it("puts back a subtree that the same transaction changed after removing it", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const startNodes = nodesIn(f.page);
        await f.history.doTransaction("Rebuild", () => {
            const box1 = byId(f.page, "box1");
            const bold = box1.querySelector("b") as HTMLElement;
            box1.innerHTML = "<p>new <u>content</u></p>";
            expect(
                bold.isConnected,
                "test setup: the <b> should be removed with its paragraph",
            ).toBe(false);
            box1.querySelector("u")?.appendChild(bold);
            bold.firstChild?.replaceWith(document.createTextNode("BOLD"));
        });
        const finalHtml = f.page.outerHTML;
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        const restored = nodesIn(f.page);
        expect(restored.length).toBe(startNodes.length);
        restored.forEach((node, i) =>
            expect(node, `node ${i} after undo`).toBe(startNodes[i]),
        );
        await f.history.redo();
        expect(f.page.outerHTML).toBe(finalHtml);
    });
});
