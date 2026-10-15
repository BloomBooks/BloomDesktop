// The few page history cases where jsdom may differ from the engine behind WebView2: the timing
// of MutationObserver delivery, and the mutations that real editing commands make. These run in
// headless Edge (the "browser" vitest project). The rest of the contract is tested in
// pageHistorySpec.ts.
import { afterEach, describe, expect, it, vi } from "vitest";
import { removeTableEditingArtifacts } from "bloom-table";
import {
    createEditTimeMarkupFilter,
    isInsideEditTimeElement,
    keptAttributeValue,
} from "./editTimeMarkup";
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

describe("the edit-time markup filter in a real browser", () => {
    function setUpWithFilter(
        innerHtml: string,
        onUnmarkedChange = vi.fn<(whatChanged: string) => void>(),
    ): { page: HTMLElement; history: PageHistory } {
        page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML = innerHtml;
        document.body.appendChild(page);
        history = createPageHistory({
            root: page,
            filter: createEditTimeMarkupFilter(),
            onUnmarkedChange,
        });
        return { page, history };
    }

    it("ignores the style and class writes that niceScroll makes, after Chromium rewrites the attribute", async () => {
        const unmarked = vi.fn<(whatChanged: string) => void>();
        const { page, history } = setUpWithFilter(
            '<div class="bloom-translationGroup bloom-vertical-align-center" id="group">' +
                '<div class="bloom-editable bloom-visibility-code-on" id="box" style="color:#FF0000;margin:0">' +
                "<p>text</p></div></div>" +
                '<img id="img" src="a.png">',
            unmarked,
        );
        await history.doTransaction("A", () =>
            byId(page, "img").setAttribute("src", "b.png"),
        );
        await history.undo();

        const box = byId(page, "box");
        box.style.overflowY = "hidden";
        box.style.outline = "none";
        box.style.width = "40px";
        const group = byId(page, "group");
        group.classList.remove("bloom-vertical-align-center");
        group.classList.add("bloom-vertical-align-center-removed");
        await letObserverDeliver();
        expect(
            box.getAttribute("style"),
            "test setup: Chromium should have rewritten the authored values",
        ).not.toContain("#FF0000");
        // As cleanupNiceScroll does.
        box.style.overflow = "";
        box.style.outline = "";
        box.style.width = "";
        group.classList.remove("bloom-vertical-align-center-removed");
        group.classList.add("bloom-vertical-align-center");
        await letObserverDeliver();

        expect(unmarked).not.toHaveBeenCalled();
        expect(history.redoDescriptionL10nKey).toBe("A");
    });

    it("drops exactly what bloom-table's save cleanup removes from a table", () => {
        const { page } = setUpWithFilter(
            '<div class="bloom-table table--selected bloom-current-table bloom-pointer-near" id="table" data-column-widths="fill,fill">' +
                '<div class="bloom-cell cell--selected bloom-pulse-fill bloom-pulse-border" id="cell1" data-content-type="text" style="--hint-top-color: red; --hint-left-color: blue; grid-column: 1;"></div>' +
                '<div class="bloom-cell" id="cell2" data-btable-anchor-name="--a" style="anchor-name: --a; color: blue;"></div>' +
                '<div data-table-overlay="1" id="overlay"><span id="inOverlay"></span></div>' +
                "</div>",
        );
        // Turn off the history, so that it does not see the cleanup.
        history?.dispose();
        history = undefined;
        const before = new Map<Element, Map<string, string>>();
        page.querySelectorAll("*").forEach((element) =>
            before.set(
                element,
                new Map(
                    element
                        .getAttributeNames()
                        .map((name) => [name, element.getAttribute(name)!]),
                ),
            ),
        );
        expect(before.size, "test setup").toBe(5);

        removeTableEditingArtifacts(document);

        before.forEach((attributes, element) => {
            if (!page.contains(element)) {
                expect(
                    isInsideEditTimeElement(element),
                    `the save removed <${element.tagName} id=${element.id}>`,
                ).toBe(true);
                return;
            }
            const names = new Set([
                ...attributes.keys(),
                ...element.getAttributeNames(),
            ]);
            names.forEach((name) => {
                const after = element.getAttribute(name) ?? undefined;
                expect(
                    keptAttributeValue(element, name, attributes.get(name)),
                    `the save should change only the edit-time part of ${name} on #${element.id}`,
                ).toBe(keptAttributeValue(element, name, after));
                if (after === undefined) return;
                expect(
                    keptAttributeValue(element, name, after),
                    `the rules should drop nothing that the save left in ${name} on #${element.id}`,
                ).toBeDefined();
            });
            const keptClassCount = (
                keptAttributeValue(
                    element,
                    "class",
                    element.getAttribute("class") ?? undefined,
                ) ?? ""
            )
                .split(" ")
                .filter((t) => t).length;
            expect(keptClassCount, `the classes of #${element.id}`).toBe(
                element.classList.length,
            );
            const scratch = document.createElement("div");
            scratch.setAttribute(
                "style",
                keptAttributeValue(
                    element,
                    "style",
                    element.getAttribute("style") ?? undefined,
                ) ?? "",
            );
            expect(scratch.style.length, `the style of #${element.id}`).toBe(
                (element as HTMLElement).style.length,
            );
        });
        expect(byId(page, "cell1").getAttribute("style")).toBe(
            "grid-column: 1;",
        );
    });
});

describe("page history in a real browser: a node made and thrown away within one batch", () => {
    // As the overflow checker does after an undo: a hidden clone of a box is appended, styled
    // inside, and removed in one task. Unlike jsdom, the browser reports changes made to the
    // clone after its removal too.
    it("keeps redo when a measuring clone comes and goes, and still records a moved page node", async () => {
        const unmarked = vi.fn<(whatChanged: string) => void>();
        const { page, history } = setUp(unmarked);
        await history.doTransaction("A", () =>
            byId(page, "img1").setAttribute("src", "b.png"),
        );
        await history.undo();

        const box1 = byId(page, "box1");
        const clone = box1.cloneNode(true) as HTMLElement;
        clone.removeAttribute("id");
        clone.querySelectorAll("[id]").forEach((e) => e.removeAttribute("id"));
        box1.parentElement!.appendChild(clone);
        const paragraph = clone.querySelector("p") as HTMLElement;
        paragraph.style.flexGrow = "1";
        clone.style.flexGrow = "1";
        clone.remove();
        paragraph.style.flexGrow = "2";
        await letObserverDeliver();

        expect(unmarked).not.toHaveBeenCalled();
        expect(history.redoDescriptionL10nKey).toBe("A");

        const bold = byId(page, "p1").querySelector("b") as HTMLElement;
        await history.doTransaction("Move", () => {
            const holder = document.createElement("div");
            box1.parentElement!.appendChild(holder);
            holder.appendChild(bold);
            holder.remove();
            bold.setAttribute("data-x", "1");
        });
        expect(history.undoDescriptionL10nKey).toBe("Move");
        await history.undo();
        expect(bold.parentElement).toBe(byId(page, "p1"));
        expect(bold.hasAttribute("data-x")).toBe(false);
    });
});
