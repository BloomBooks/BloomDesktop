// Black-box tests of the page history, written from the contract in pageHistoryTypes.ts and the
// rules of docs/undo/APPROACHES.md section 3.0.2 (sections 6.1 and 11.8.1 for the API), not from
// the implementation. Each test builds a small `.bloom-page`, makes changes, and checks the page's
// HTML, the identity of its nodes, and the history's state after undo and redo.
//
// A MutationObserver delivers its records in a microtask, so a change made outside any API call
// reaches the history only at the next microtask or at the next call to the history. Tests of
// changes made outside any transaction use letObserverDeliver() for that.
import { afterEach, describe, expect, it, vi, type Mock } from "vitest";
import { createPageHistory, kKeepEverythingFilter } from "./pageHistory";
import {
    formatUndoDescription,
    kGenericUndoDescriptionL10nKey,
    type UndoDescription,
} from "./pageHistoryTypes";
import type {
    BegunTransaction,
    PageHistory,
    PageHistoryChangeContext,
    PageHistoryDetail,
    PageHistoryFilter,
    PageHistoryLogKind,
    PageHistoryOptions,
    RestoredContent,
    Transaction,
} from "./pageHistoryTypes";

interface IFixture {
    page: HTMLElement;
    history: PageHistory;
    unmarked: Mock<(whatChanged: string) => void>;
    disposed: boolean;
}

let fixture: IFixture | undefined;

const kStandardPage =
    '<div class="bloom-translationGroup" id="group">' +
    '<div class="bloom-editable" id="box1" lang="en"><p id="p1">Hello <b id="b1">bold</b> world</p></div>' +
    '<div class="bloom-editable" id="box2" lang="fr"><p id="p2">Bonjour</p></div>' +
    "</div>" +
    '<div class="bloom-canvas" id="canvas"><img id="img1" src="a.png" alt=""></div>';

// Builds a page in the document and a history on it. The history reports unmarked changes to a
// mock, so that tests can check when rule 1's development warning would have fired.
function setUp(
    innerHtml: string = kStandardPage,
    options?: Partial<Omit<PageHistoryOptions, "root">>,
): IFixture {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.id = "page";
    page.innerHTML = innerHtml;
    document.body.appendChild(page);
    const unmarked = vi.fn<(whatChanged: string) => void>();
    const history = createPageHistory({
        root: page,
        onUnmarkedChange: unmarked,
        ...options,
    });
    fixture = { page, history, unmarked, disposed: false };
    return fixture;
}

function disposeHistory(f: IFixture): void {
    f.disposed = true;
    f.history.dispose();
}

afterEach(() => {
    if (fixture) {
        if (!fixture.disposed) {
            fixture.history.dispose();
        }
        fixture.page.remove();
        fixture = undefined;
    }
});

// MutationObserver queues its callback as a microtask when a change is made, before this await
// queues its own continuation, so by the time this resolves the observer has delivered every
// change made before the call.
async function letObserverDeliver(): Promise<void> {
    await Promise.resolve();
}

function byId(f: IFixture, id: string): HTMLElement {
    const element = f.page.querySelector<HTMLElement>(`#${id}`);
    expect(element, `test setup: #${id} should be on the page`).not.toBeNull();
    return element as HTMLElement;
}

// Every node of the page, root included, in document order.
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

function describeNode(node: Node): string {
    if (node instanceof Element) {
        return `<${node.tagName.toLowerCase()}${node.id ? "#" + node.id : ""}>`;
    }
    return `${node.nodeName} "${node.textContent}"`;
}

// Undo and redo must put back the same node objects, not copies.
function expectSameNodes(actual: Node[], expected: Node[], when: string): void {
    expect(
        actual.map(describeNode),
        `${when}: the page should have the same nodes in the same order`,
    ).toEqual(expected.map(describeNode));
    actual.forEach((node, i) => {
        expect(
            node,
            `${when}: node ${i}, ${describeNode(expected[i])}, should be the very object that was there before, not a copy`,
        ).toBe(expected[i]);
    });
}

// An element's attributes as names and values, without their order.
function attributesOf(element: Element): Record<string, string> {
    const result: Record<string, string> = {};
    for (const attribute of Array.from(element.attributes)) {
        result[attribute.name] = attribute.value;
    }
    return result;
}

// A promise that the test resolves when it chooses, to hold an async transaction open.
function makeGate(): { promise: Promise<void>; open: () => void } {
    let open: () => void = () => {
        throw new Error("test setup: gate not initialised");
    };
    const promise = new Promise<void>((resolve) => {
        open = resolve;
    });
    return { promise, open };
}

// Calls a method that returns a promise, turning a synchronous throw into a rejection, so that a
// test accepts either way of reporting an error from a promise-returning method.
function settle<T>(call: () => Promise<T>): Promise<T> {
    return (async () => call())();
}

// Makes one undo step that sets an attribute, and returns the HTML from before it.
async function stepSettingAttribute(
    f: IFixture,
    key: string,
    element: Element,
    name: string,
    value: string,
): Promise<string> {
    const before = f.page.outerHTML;
    await f.history.doTransaction(key, () => {
        element.setAttribute(name, value);
    });
    expect(
        f.history.undoDescriptionL10nKey,
        `test setup: the step "${key}" should be the latest`,
    ).toBe(key);
    return before;
}

describe("page history: undo and redo put back the same nodes and the same HTML", () => {
    async function expectUndoAndRedoExact(
        f: IFixture,
        key: string,
        change: () => void,
    ): Promise<void> {
        const beforeHtml = f.page.outerHTML;
        const beforeNodes = nodesIn(f.page);
        await f.history.doTransaction(key, change);
        const afterHtml = f.page.outerHTML;
        const afterNodes = nodesIn(f.page);
        expect(
            afterHtml,
            "test setup: the change should change the page",
        ).not.toBe(beforeHtml);
        expect(f.history.undoDescriptionL10nKey).toBe(key);

        await f.history.undo();
        expect(f.page.outerHTML, "after undo").toBe(beforeHtml);
        expectSameNodes(nodesIn(f.page), beforeNodes, "after undo");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBe(key);

        await f.history.redo();
        expect(f.page.outerHTML, "after redo").toBe(afterHtml);
        expectSameNodes(nodesIn(f.page), afterNodes, "after redo");
        expect(f.history.undoDescriptionL10nKey).toBe(key);
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();

        await f.history.undo();
        expect(f.page.outerHTML, "after a second undo").toBe(beforeHtml);
        expectSameNodes(nodesIn(f.page), beforeNodes, "after a second undo");
    }

    it("puts back attributes that were added, changed and removed", async () => {
        const f = setUp();
        const box1 = byId(f, "box1");
        const img = byId(f, "img1");
        expect(box1.getAttribute("lang")).toBe("en");
        expect(img.hasAttribute("data-copyright")).toBe(false);
        await expectUndoAndRedoExact(f, "Attributes", () => {
            img.setAttribute("data-copyright", "2026");
            img.setAttribute("src", "b.png");
            box1.removeAttribute("lang");
            box1.classList.add("bloom-visibility-code-on");
        });
    });

    it("puts back several changes to one attribute as its first value", async () => {
        const f = setUp();
        const img = byId(f, "img1");
        expect(img.getAttribute("src")).toBe("a.png");
        const beforeAttributes = attributesOf(img);
        await f.history.doTransaction("Src", () => {
            img.setAttribute("src", "b.png");
            img.setAttribute("src", "c.png");
            img.removeAttribute("src");
            img.setAttribute("src", "d.png");
        });
        const afterAttributes = attributesOf(img);
        expect(afterAttributes.src).toBe("d.png");

        // Removing an attribute and adding it back moves it to the end of the list. Attribute
        // order is not part of an exact restore, so these compare names and values only.
        await f.history.undo();
        expect(attributesOf(img), "after undo").toEqual(beforeAttributes);
        expect(byId(f, "img1"), "after undo, the same img").toBe(img);
        await f.history.redo();
        expect(attributesOf(img), "after redo").toEqual(afterAttributes);
        await f.history.undo();
        expect(attributesOf(img), "after a second undo").toEqual(
            beforeAttributes,
        );
    });

    it("puts back the root element's own attributes", async () => {
        const f = setUp();
        expect(f.page.hasAttribute("data-page-number")).toBe(false);
        await expectUndoAndRedoExact(f, "Root", () => {
            f.page.setAttribute("data-page-number", "3");
            f.page.classList.add("A5Portrait");
        });
    });

    it("puts back text changed in place", async () => {
        const f = setUp();
        const text = byId(f, "p2").firstChild as Text;
        expect(text.data).toBe("Bonjour");
        await expectUndoAndRedoExact(f, "Text", () => {
            text.data = "Bonsoir";
            text.appendData(" tout le monde");
        });
    });

    it("puts back text whose node was replaced, with the original text node", async () => {
        const f = setUp();
        const p1 = byId(f, "p1");
        expect(p1.childNodes.length).toBe(3);
        await expectUndoAndRedoExact(f, "Replace text", () => {
            p1.textContent = "Goodbye";
        });
    });

    it("takes out added nodes and puts the same objects back on redo", async () => {
        const f = setUp();
        const group = byId(f, "group");
        expect(group.children.length).toBe(2);
        await expectUndoAndRedoExact(f, "Add", () => {
            const box3 = document.createElement("div");
            box3.className = "bloom-editable";
            box3.setAttribute("lang", "es");
            box3.innerHTML = "<p>Hola <i>mundo</i></p>";
            group.insertBefore(box3, byId(f, "box2"));
            group.appendChild(document.createTextNode("tail"));
        });
    });

    it("puts removed nodes back in their places", async () => {
        const f = setUp();
        expect(byId(f, "b1").textContent).toBe("bold");
        await expectUndoAndRedoExact(f, "Remove", () => {
            byId(f, "b1").remove();
            byId(f, "box2").remove();
            byId(f, "canvas").replaceChildren();
        });
    });

    it("puts back a node moved within its parent and one moved to another parent", async () => {
        const f = setUp();
        const group = byId(f, "group");
        expect(group.firstElementChild?.id).toBe("box1");
        await expectUndoAndRedoExact(f, "Move", () => {
            group.insertBefore(byId(f, "box2"), byId(f, "box1"));
            byId(f, "canvas").appendChild(byId(f, "b1"));
        });
    });

    it("puts back a node moved several times in one transaction", async () => {
        const f = setUp();
        await expectUndoAndRedoExact(f, "Moves", () => {
            const b1 = byId(f, "b1");
            byId(f, "p2").appendChild(b1);
            byId(f, "canvas").prepend(b1);
            f.page.appendChild(b1);
            b1.setAttribute("data-moved", "yes");
        });
    });

    it("puts back a subtree removed in one step and re-inserted elsewhere in a later one", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const startNodes = nodesIn(f.page);
        const box2 = byId(f, "box2");
        const canvas = byId(f, "canvas");

        await f.history.doTransaction("Cut", () => {
            box2.remove();
        });
        const afterCutHtml = f.page.outerHTML;
        expect(box2.isConnected, "test setup: box2 should be removed").toBe(
            false,
        );

        await f.history.doTransaction("Paste", () => {
            canvas.appendChild(box2);
        });
        const afterPasteHtml = f.page.outerHTML;
        const afterPasteNodes = nodesIn(f.page);
        expect(box2.parentElement).toBe(canvas);

        await f.history.undo();
        expect(f.page.outerHTML, "after undoing the paste").toBe(afterCutHtml);
        expect(box2.isConnected).toBe(false);
        await f.history.undo();
        expect(f.page.outerHTML, "after undoing the cut").toBe(startHtml);
        expectSameNodes(nodesIn(f.page), startNodes, "after undoing the cut");

        await f.history.redo();
        expect(f.page.outerHTML, "after redoing the cut").toBe(afterCutHtml);
        await f.history.redo();
        expect(f.page.outerHTML, "after redoing the paste").toBe(
            afterPasteHtml,
        );
        expectSameNodes(
            nodesIn(f.page),
            afterPasteNodes,
            "after redoing the paste",
        );
    });

    it("puts back a page whose content was rebuilt with innerHTML along with many other changes", async () => {
        const f = setUp();
        await expectUndoAndRedoExact(f, "Everything", () => {
            const box1 = byId(f, "box1");
            box1.innerHTML = "<p>new <u>content</u></p>";
            box1.querySelector("u")?.setAttribute("class", "underlined");
            byId(f, "group").appendChild(byId(f, "canvas"));
            byId(f, "img1").setAttribute("style", "width: 10px");
            byId(f, "p2").firstChild?.remove();
            byId(f, "p2").append("x", "y");
        });
    });

    // A subtree that the same transaction changed just after removing it is tested in
    // pageHistory.browser.spec.ts: jsdom has no transient observers, so it never reports that change.

    it("redoes a later step over a node that an earlier step created", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const box3 = document.createElement("div");
        await f.history.doTransaction("Add", () => {
            byId(f, "group").appendChild(box3);
        });
        await f.history.doTransaction("Fill", () => {
            box3.innerHTML = "<p>filled</p>";
            box3.setAttribute("lang", "de");
        });
        const finalHtml = f.page.outerHTML;
        const finalNodes = nodesIn(f.page);

        await f.history.undo();
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        await f.history.redo();
        await f.history.redo();
        expect(f.page.outerHTML).toBe(finalHtml);
        expectSameNodes(nodesIn(f.page), finalNodes, "after redoing both");
    });
});

describe("page history: doTransaction", () => {
    it("records a synchronous fn as one step, resolves to its result and passes the transaction", async () => {
        const f = setUp();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        let received: Transaction | undefined;
        const resultPromise = f.history.doTransaction("Sync", (t) => {
            received = t;
            byId(f, "img1").setAttribute("src", "b.png");
            byId(f, "box1").setAttribute("lang", "de");
            return 42;
        });
        // A synchronous fn closes its transaction when it returns (rule 7 keeps only an async
        // fn's transaction open).
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(await resultPromise).toBe(42);
        expect(received?.descriptionL10nKey).toBe("Sync");
        expect(f.history.undoDescriptionL10nKey).toBe("Sync");

        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(
            f.history.undoDescriptionL10nKey,
            "both changes should have been one step",
        ).toBeUndefined();
    });

    it("keeps an async fn's transaction open across awaits and takes in changes made meanwhile (rule 7)", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const gate = makeGate();
        const done = f.history.doTransaction("Async", async () => {
            byId(f, "img1").setAttribute("src", "b.png");
            await gate.promise;
            byId(f, "box1").setAttribute("lang", "de");
            return "result";
        });
        expect(f.history.hasOpenTransaction).toBe(true);

        // The user types while the transaction waits.
        (byId(f, "p2").firstChild as Text).data = "Bonjour typed";
        await letObserverDeliver();
        expect(
            f.unmarked,
            "a change made while a transaction is open is inside it, not unmarked",
        ).not.toHaveBeenCalled();
        expect(f.history.hasOpenTransaction).toBe(true);
        expect(
            f.history.undoDescriptionL10nKey,
            "no step should exist until the fn settles",
        ).toBeUndefined();

        gate.open();
        expect(await done).toBe("result");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("Async");

        await f.history.undo();
        expect(
            f.page.outerHTML,
            "the typing should be undone with the action",
        ).toBe(startHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("nests a doTransaction or beginTransaction that begins while an async one is open (rule 7)", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const gate = makeGate();
        const outer = f.history.doTransaction("Outer", async () => {
            byId(f, "img1").setAttribute("src", "b.png");
            await gate.promise;
        });
        await f.history.doTransaction("Other", () => {
            byId(f, "box1").setAttribute("lang", "de");
        });
        const begun = f.history.beginTransaction("Dragged");
        byId(f, "box2").setAttribute("lang", "es");
        begun.end();
        expect(f.history.hasOpenTransaction).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();

        gate.open();
        await outer;
        expect(f.history.undoDescriptionL10nKey).toBe("Outer");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        expect(
            f.history.undoDescriptionL10nKey,
            "the nested transactions should have made no steps of their own",
        ).toBeUndefined();
    });

    it("puts back the changes of a synchronous fn that throws, and rethrows (rule 9)", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "Earlier", byId(f, "box2"), "lang", "es");
        const beforeHtml = f.page.outerHTML;
        const beforeNodes = nodesIn(f.page);

        await expect(
            settle(() =>
                f.history.doTransaction("Fails", () => {
                    byId(f, "img1").setAttribute("src", "b.png");
                    byId(f, "b1").remove();
                    throw new Error("action failed");
                }),
            ),
        ).rejects.toThrow("action failed");

        expect(f.page.outerHTML).toBe(beforeHtml);
        expectSameNodes(nodesIn(f.page), beforeNodes, "after the throw");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(
            f.history.undoDescriptionL10nKey,
            "the failed transaction should make no step, and the earlier one should remain",
        ).toBe("Earlier");
    });

    it("puts back every change since the call when an async fn rejects, the user's meanwhile included", async () => {
        const f = setUp();
        const beforeHtml = f.page.outerHTML;
        const gate = makeGate();
        const done = f.history.doTransaction("Fails", async () => {
            byId(f, "img1").setAttribute("src", "b.png");
            await gate.promise;
            byId(f, "box1").setAttribute("lang", "de");
            throw new Error("async action failed");
        });
        (byId(f, "p2").firstChild as Text).data = "typed meanwhile";
        gate.open();

        await expect(done).rejects.toThrow("async action failed");
        expect(f.page.outerHTML).toBe(beforeHtml);
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes no step, and keeps redo, when fn changes nothing", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("A");

        expect(
            await f.history.doTransaction("Nothing", () => "unchanged"),
        ).toBe("unchanged");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBe("A");
    });

    it("clears redo when it makes a step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("A");
        await stepSettingAttribute(f, "B", byId(f, "box1"), "lang", "de");
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
    });
});

describe("page history: beginTransaction", () => {
    it("makes one step on end(), with its description", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const t = f.history.beginTransaction("Drag");
        expect(t.descriptionL10nKey).toBe("Drag");
        expect(f.history.hasOpenTransaction).toBe(true);
        byId(f, "img1").setAttribute("style", "left: 1px");
        byId(f, "img1").setAttribute("style", "left: 2px");
        byId(f, "img1").setAttribute("style", "left: 3px");
        t.end();
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("Drag");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("takes in changes that other code makes while it is open", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const t = f.history.beginTransaction("Dialog");
        byId(f, "img1").setAttribute("src", "b.png");
        await letObserverDeliver();
        byId(f, "box1").setAttribute("lang", "de");
        await letObserverDeliver();
        expect(f.unmarked).not.toHaveBeenCalled();
        t.end();
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("puts back everything on cancel() and makes no step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "Earlier", byId(f, "box2"), "lang", "es");
        const beforeHtml = f.page.outerHTML;
        const beforeNodes = nodesIn(f.page);
        const t = f.history.beginTransaction("Preview");
        byId(f, "img1").setAttribute("src", "b.png");
        byId(f, "canvas").appendChild(byId(f, "b1"));
        await letObserverDeliver();
        byId(f, "p2").textContent = "changed";
        t.cancel();
        expect(f.page.outerHTML).toBe(beforeHtml);
        expectSameNodes(nodesIn(f.page), beforeNodes, "after cancel");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("Earlier");
    });

    it("ignores cancel() after end()", async () => {
        const f = setUp();
        const t = f.history.beginTransaction("Drag");
        byId(f, "img1").setAttribute("src", "b.png");
        t.end();
        const afterHtml = f.page.outerHTML;
        t.cancel();
        expect(
            f.page.outerHTML,
            "a late cancel() should put nothing back",
        ).toBe(afterHtml);
        expect(f.history.undoDescriptionL10nKey).toBe("Drag");
    });

    it("ignores end() after cancel(), and a second end()", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "Earlier", byId(f, "box2"), "lang", "es");
        const cancelled = f.history.beginTransaction("Cancelled");
        byId(f, "img1").setAttribute("src", "b.png");
        cancelled.cancel();
        cancelled.end();
        expect(f.history.undoDescriptionL10nKey).toBe("Earlier");

        const ended = f.history.beginTransaction("Ended");
        byId(f, "img1").setAttribute("src", "c.png");
        ended.end();
        ended.end();
        expect(f.history.undoDescriptionL10nKey).toBe("Ended");
        await f.history.undo();
        expect(
            f.history.undoDescriptionL10nKey,
            "a second end() should not have made a second step",
        ).toBe("Earlier");
    });

    it("makes no step, and keeps redo, when nothing changed before end()", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.undo();
        const t = f.history.beginTransaction("Click");
        t.end();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBe("A");
    });
});

describe("page history: transactions inside transactions", () => {
    it("makes one step with the outer description for a doTransaction inside a doTransaction", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await f.history.doTransaction("Paste", async () => {
            byId(f, "box1").setAttribute("lang", "de");
            const inner = await f.history.doTransaction(
                "ChangeImage",
                async () => {
                    expect(f.history.hasOpenTransaction).toBe(true);
                    byId(f, "img1").setAttribute("src", "b.png");
                    await Promise.resolve();
                    byId(f, "img1").setAttribute("data-copyright", "2026");
                    return "inner";
                },
            );
            expect(inner).toBe("inner");
            expect(
                f.history.hasOpenTransaction,
                "the outer transaction should still be open after the inner one ends",
            ).toBe(true);
            byId(f, "box2").setAttribute("lang", "es");
        });
        expect(f.history.undoDescriptionL10nKey).toBe("Paste");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes one step with the outer description for a beginTransaction inside a doTransaction", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await f.history.doTransaction("Outer", () => {
            const inner = f.history.beginTransaction("Inner");
            byId(f, "img1").setAttribute("src", "b.png");
            inner.end();
            byId(f, "box1").setAttribute("lang", "de");
        });
        expect(f.history.undoDescriptionL10nKey).toBe("Outer");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("puts back only the inner changes when an inner doTransaction throws", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await f.history.doTransaction("Outer", async () => {
            byId(f, "box1").setAttribute("lang", "de");
            await expect(
                settle(() =>
                    f.history.doTransaction("Inner", () => {
                        byId(f, "img1").setAttribute("src", "b.png");
                        byId(f, "b1").remove();
                        throw new Error("inner failed");
                    }),
                ),
            ).rejects.toThrow("inner failed");
            expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
            expect(byId(f, "p1").contains(byId(f, "b1"))).toBe(true);
            expect(
                byId(f, "box1").getAttribute("lang"),
                "the outer change from before the inner one began should stay",
            ).toBe("de");
            expect(f.history.hasOpenTransaction).toBe(true);
            byId(f, "box2").setAttribute("lang", "es");
        });
        expect(byId(f, "box2").getAttribute("lang")).toBe("es");
        expect(f.history.undoDescriptionL10nKey).toBe("Outer");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("puts back only the inner changes when an inner beginTransaction is cancelled", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const outer = f.history.beginTransaction("Outer");
        byId(f, "box1").setAttribute("lang", "de");
        const inner = f.history.beginTransaction("Inner");
        byId(f, "img1").setAttribute("src", "b.png");
        inner.cancel();
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(byId(f, "box1").getAttribute("lang")).toBe("de");
        expect(f.history.hasOpenTransaction).toBe(true);
        outer.end();
        expect(f.history.undoDescriptionL10nKey).toBe("Outer");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("throws in development when the outer beginTransaction ends while an inner one is open", () => {
        expect(
            import.meta.env.DEV,
            "this test assumes a development build",
        ).toBe(true);
        const f = setUp();
        const outer: BegunTransaction = f.history.beginTransaction("Outer");
        f.history.beginTransaction("Inner");
        byId(f, "img1").setAttribute("src", "b.png");
        expect(() => outer.end()).toThrow();
    });

    it("rejects in development when an outer doTransaction's fn returns with an inner one open", async () => {
        expect(
            import.meta.env.DEV,
            "this test assumes a development build",
        ).toBe(true);
        const f = setUp();
        await expect(
            settle(() =>
                f.history.doTransaction("Outer", () => {
                    f.history.beginTransaction("Inner");
                    byId(f, "img1").setAttribute("src", "b.png");
                }),
            ),
        ).rejects.toThrow();
    });
});

describe("page history: changes outside any transaction (rule 1)", () => {
    it("joins such a change to the latest step, clears redo and reports it", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box1"), "lang", "de");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("B");
        expect(f.unmarked).not.toHaveBeenCalled();

        byId(f, "box2").setAttribute("data-zebra", "striped");
        await letObserverDeliver();

        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
        expect(f.unmarked).toHaveBeenCalledTimes(1);
        expect(
            f.unmarked.mock.calls[0][0],
            "the report should name the attribute that changed",
        ).toContain("data-zebra");
        expect(f.history.undoDescriptionL10nKey).toBe("A");

        await f.history.undo();
        expect(
            f.page.outerHTML,
            "undoing A should also take out the change that joined it",
        ).toBe(startHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("joins an added node to the latest step, and redo puts back the same node", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const added = document.createElement("span");
        added.textContent = "added by a load listener";
        byId(f, "canvas").appendChild(added);
        await letObserverDeliver();
        expect(f.unmarked).toHaveBeenCalledTimes(1);

        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        expect(added.isConnected).toBe(false);
        await f.history.redo();
        expect(added.parentElement).toBe(byId(f, "canvas"));
    });

    it("takes in a pending change at the next call, before that call's own transaction begins", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        // No await between this change and the next call: the observer has not delivered it.
        byId(f, "box2").setAttribute("data-late", "1");
        await f.history.doTransaction("B", () => {
            byId(f, "box1").setAttribute("lang", "de");
        });

        await f.history.undo();
        expect(
            byId(f, "box2").getAttribute("data-late"),
            "the change made before B began belongs to A, so undoing B leaves it",
        ).toBe("1");
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        await f.history.undo();
        expect(byId(f, "box2").hasAttribute("data-late")).toBe(false);
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
    });

    it("takes in a pending change before a beginTransaction, so cancelling it leaves the change", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        byId(f, "box2").setAttribute("data-late", "1");
        const t = f.history.beginTransaction("Preview");
        byId(f, "box1").setAttribute("lang", "de");
        t.cancel();
        expect(byId(f, "box2").getAttribute("data-late")).toBe("1");
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        await f.history.undo();
        expect(byId(f, "box2").hasAttribute("data-late")).toBe(false);
    });

    it("takes in a pending change at undo(), and undoes it with the latest step", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        byId(f, "img1").setAttribute("src", "c.png");
        byId(f, "box2").setAttribute("data-late", "1");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("joins a change made in addToLatestTransaction without reporting it", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box1"), "lang", "de");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("B");

        const result = f.history.addToLatestTransaction((t) => {
            expect(
                t.descriptionL10nKey,
                "fn should get the latest transaction",
            ).toBe("A");
            byId(f, "img1").setAttribute("style", "width: 5px");
            return "done";
        });
        expect(result).toBe("done");
        await letObserverDeliver();

        expect(f.unmarked).not.toHaveBeenCalled();
        expect(
            f.history.redoDescriptionL10nKey,
            "a recorded change on top of the latest step makes the redo steps stale",
        ).toBeUndefined();
        expect(f.history.undoDescriptionL10nKey).toBe("A");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("makes a change in addToLatestTransaction with no latest step part of the starting state", async () => {
        const f = setUp();
        let called = false;
        f.history.addToLatestTransaction(() => {
            called = true;
            byId(f, "img1").setAttribute("src", "start.png");
        });
        expect(called).toBe(true);
        await letObserverDeliver();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.unmarked).not.toHaveBeenCalled();

        await stepSettingAttribute(f, "A", byId(f, "box1"), "lang", "de");
        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("start.png");
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
    });

    it("leaves a change made in doOutsideHistory in place on undo and does not report it", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "box1"), "lang", "de");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("B");

        const result = f.history.doOutsideHistory(() => {
            byId(f, "img1").setAttribute("src", "thumbnail.png");
            return 7;
        });
        expect(result).toBe(7);
        await letObserverDeliver();
        expect(f.unmarked).not.toHaveBeenCalled();
        expect(
            f.history.redoDescriptionL10nKey,
            "an unrecorded change should not clear redo",
        ).toBe("B");

        await f.history.redo();
        expect(byId(f, "box2").getAttribute("lang")).toBe("es");
        await f.history.undo();
        await f.history.undo();
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(byId(f, "img1").getAttribute("src")).toBe("thumbnail.png");
    });

    it("makes a change with no latest step part of the starting state", async () => {
        const f = setUp();
        byId(f, "img1").setAttribute("src", "start.png");
        await letObserverDeliver();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();

        await stepSettingAttribute(f, "A", byId(f, "box1"), "lang", "de");
        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("start.png");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes a change straight after clearUndoAndRedo part of the starting state", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "box1"), "lang", "de");
        f.history.clearUndoAndRedo("a test");
        byId(f, "img1").setAttribute("src", "start.png");
        await letObserverDeliver();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("start.png");
        expect(byId(f, "box1").getAttribute("lang")).toBe("de");
    });
});

describe("page history: only a net change counts (rule 1)", () => {
    const noNetChanges: { name: string; make: (f: IFixture) => void }[] = [
        {
            name: "classList.add of a class already there",
            make: (f) => byId(f, "box1").classList.add("bloom-editable"),
        },
        {
            name: "setting an attribute to its current value",
            make: (f) => byId(f, "img1").setAttribute("src", "a.png"),
        },
        {
            name: "setting text to its current value",
            make: (f) => {
                const text = byId(f, "p2").firstChild as Text;
                text.data = "Bonjour";
            },
        },
        {
            name: "changing an attribute and changing it back",
            make: (f) => {
                byId(f, "img1").setAttribute("src", "other.png");
                byId(f, "img1").setAttribute("src", "a.png");
            },
        },
        {
            name: "adding a node and removing it again",
            make: (f) => {
                const span = document.createElement("span");
                byId(f, "canvas").appendChild(span);
                span.remove();
            },
        },
    ];

    it.each(noNetChanges)(
        "keeps redo and reports nothing after $name outside any transaction",
        async (testCase) => {
            const f = setUp();
            await stepSettingAttribute(f, "A", byId(f, "box2"), "lang", "es");
            await f.history.undo();
            expect(f.history.redoDescriptionL10nKey).toBe("A");
            const beforeHtml = f.page.outerHTML;

            testCase.make(f);
            await letObserverDeliver();

            expect(
                f.page.outerHTML,
                "test setup: the page should look unchanged",
            ).toBe(beforeHtml);
            expect(f.unmarked).not.toHaveBeenCalled();
            expect(f.history.redoDescriptionL10nKey).toBe("A");
            await f.history.redo();
            expect(byId(f, "box2").getAttribute("lang")).toBe("es");
        },
    );
});

describe("page history: undo is refused when the page lacks the values a step left (rule 4)", () => {
    it("refuses an undo over an unrecorded change to a value that the step holds", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "Older", byId(f, "box1"), "lang", "de");
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("B");

        f.history.doOutsideHistory(() => {
            byId(f, "img1").setAttribute("src", "recomputed.png");
        });
        const beforeHtml = f.page.outerHTML;
        await f.history.undo();

        expect(
            f.page.outerHTML,
            "a refused undo should leave the page alone",
        ).toBe(beforeHtml);
        expect(
            f.history.undoDescriptionL10nKey,
            "the refused step and every older one should be discarded",
        ).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey, "redo should be kept").toBe(
            "B",
        );
        await f.history.undo();
        expect(f.page.outerHTML).toBe(beforeHtml);
    });

    it("refuses an undo over an unrecorded change to text that the step holds", async () => {
        const f = setUp();
        const text = byId(f, "p2").firstChild as Text;
        await f.history.doTransaction("Type", () => {
            text.data = "Bonsoir";
        });
        f.history.doOutsideHistory(() => {
            text.data = "Salut";
        });
        await f.history.undo();
        expect(text.data).toBe("Salut");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("refuses an undo whose text node an unrecorded change took out of the page", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "Older", byId(f, "box1"), "lang", "de");
        const text = byId(f, "p2").firstChild as Text;
        await f.history.doTransaction("Type", () => {
            text.data = "Bonsoir";
        });
        f.history.doOutsideHistory(() => {
            byId(f, "p2").replaceChildren(document.createTextNode("Salut"));
        });
        expect(
            text.isConnected,
            "test setup: the step's text node should be out of the page",
        ).toBe(false);
        expect(
            text.data,
            "test setup: the detached node still has the step's text",
        ).toBe("Bonsoir");
        const beforeHtml = f.page.outerHTML;

        await f.history.undo();

        expect(f.page.outerHTML).toBe(beforeHtml);
        expect(
            text.data,
            "a refused undo writes nothing, not even into the detached node",
        ).toBe("Bonsoir");
        expect(
            f.history.undoDescriptionL10nKey,
            "the refused step and every older one should be discarded",
        ).toBeUndefined();
        expect(
            f.history.redoDescriptionL10nKey,
            "nothing should move to redo",
        ).toBeUndefined();
    });

    it("refuses an undo whose element an unrecorded change took out of the page", async () => {
        const f = setUp();
        const image = byId(f, "img1");
        await stepSettingAttribute(f, "Picture", image, "src", "b.png");
        f.history.doOutsideHistory(() => image.remove());

        await f.history.undo();

        expect(image.getAttribute("src")).toBe("b.png");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("does not refuse a step that changed a node and then took out what holds it", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const paragraph = byId(f, "p2");
        const text = paragraph.firstChild as Text;
        await f.history.doTransaction("Type then delete", () => {
            text.data = "Bonsoir";
            paragraph.remove();
        });
        const afterHtml = f.page.outerHTML;

        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        await f.history.redo();
        expect(f.page.outerHTML).toBe(afterHtml);
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        expect(byId(f, "p2").firstChild).toBe(text);
    });

    it("refuses an undo over an unrecorded removal of a node that the step added", async () => {
        const f = setUp();
        const span = document.createElement("span");
        await f.history.doTransaction("Add", () => {
            byId(f, "canvas").appendChild(span);
        });
        f.history.doOutsideHistory(() => {
            span.remove();
        });
        const beforeHtml = f.page.outerHTML;
        await f.history.undo();
        expect(f.page.outerHTML).toBe(beforeHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("refuses an undo that would re-insert a subtree changed while it was detached", async () => {
        const f = setUp();
        const box2 = byId(f, "box2");
        const p2 = byId(f, "p2");
        await stepSettingAttribute(f, "Older", byId(f, "img1"), "src", "b.png");
        await f.history.doTransaction("Delete", () => {
            box2.remove();
        });
        // Once the observer has delivered the removal, it no longer sees changes to box2.
        await letObserverDeliver();
        p2.setAttribute("data-changed-while-detached", "1");
        await letObserverDeliver();
        const beforeHtml = f.page.outerHTML;

        await f.history.undo();
        expect(
            box2.isConnected,
            "the changed subtree should not be put back",
        ).toBe(false);
        expect(f.page.outerHTML).toBe(beforeHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("refuses a redo over an unrecorded change, and discards every redo step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("A");

        f.history.doOutsideHistory(() => {
            byId(f, "img1").setAttribute("src", "recomputed.png");
        });
        const beforeHtml = f.page.outerHTML;
        await f.history.redo();
        expect(f.page.outerHTML).toBe(beforeHtml);
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("does not refuse when the change was one that rule 1 saw", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        byId(f, "img1").setAttribute("src", "refit.png");
        await letObserverDeliver();
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
    });
});

describe("page history: clearUndoAndRedo", () => {
    it("drops every undo and redo step and leaves the page alone", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        const beforeHtml = f.page.outerHTML;
        f.history.clearUndoAndRedo("a test");
        expect(f.page.outerHTML).toBe(beforeHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
        await f.history.undo();
        await f.history.redo();
        expect(f.page.outerHTML).toBe(beforeHtml);
    });

    it("ends an open beginTransaction, leaving its changes, and makes its end() and cancel() do nothing", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "box2"), "lang", "es");
        const t = f.history.beginTransaction("Drag");
        byId(f, "img1").setAttribute("src", "b.png");
        f.history.clearUndoAndRedo("a test");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        t.cancel();
        expect(
            byId(f, "img1").getAttribute("src"),
            "cancel() after the boundary should put nothing back",
        ).toBe("b.png");
        t.end();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(byId(f, "box2").getAttribute("lang")).toBe("es");
    });

    it("ends an open async doTransaction, which still resolves but makes no step", async () => {
        const f = setUp();
        const gate = makeGate();
        const done = f.history.doTransaction("Async", async () => {
            byId(f, "img1").setAttribute("src", "b.png");
            await gate.promise;
            return 5;
        });
        f.history.clearUndoAndRedo("a test");
        expect(f.history.hasOpenTransaction).toBe(false);
        gate.open();
        expect(await done).toBe(5);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
    });
});

describe("page history: undo and redo asked for while a transaction is open", () => {
    it("waits, and runs the undo as soon as the transaction ends, undoing its step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");

        const t = f.history.beginTransaction("Drag");
        byId(f, "box1").setAttribute("lang", "de");
        const openHtml = f.page.outerHTML;
        let undoHasSettled = false;
        const undone = f.history.undo().then(() => (undoHasSettled = true));
        await Promise.resolve();
        expect(f.page.outerHTML, "undo while open").toBe(openHtml);
        expect(undoHasSettled, "undo's promise while open").toBe(false);
        expect(f.history.hasOpenTransaction).toBe(true);

        t.end();

        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(
            byId(f, "img1").getAttribute("src"),
            "step A should be untouched",
        ).toBe("b.png");
        expect(f.history.undoDescriptionL10nKey).toBe("A");
        expect(f.history.redoDescriptionL10nKey).toBe("Drag");
        await undone;
        expect(undoHasSettled).toBe(true);
    });

    it("runs every request made while the transaction was open, in the order made", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");

        const t = f.history.beginTransaction("Drag");
        byId(f, "box1").setAttribute("lang", "de");
        const requests = [
            f.history.undo(),
            f.history.undo(),
            f.history.undo(),
            f.history.redo(),
        ];
        expect(byId(f, "box2").getAttribute("lang")).toBe("es");
        t.end();
        await Promise.all(requests);

        // Drag, B and A undone, then A redone.
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(f.history.undoDescriptionL10nKey).toBe("A");
        expect(f.history.redoDescriptionL10nKey).toBe("B");
    });

    it("runs an undo asked for inside an async doTransaction once that closes", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        let undone: Promise<void> | undefined;
        await f.history.doTransaction("Async", async () => {
            byId(f, "box1").setAttribute("lang", "de");
            // Not awaited: it waits for this transaction.
            undone = f.history.undo();
            await Promise.resolve();
            expect(byId(f, "box1").getAttribute("lang")).toBe("de");
        });
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(f.history.redoDescriptionL10nKey).toBe("Async");
        await undone;
    });

    it("undoes the step before when the transaction it waited for made no step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const t = f.history.beginTransaction("Nothing");
        const undone = f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(t.end(), "test setup: the transaction makes no step").toBe(
            false,
        );
        await undone;
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(f.history.redoDescriptionL10nKey).toBe("A");
    });

    it("runs the waiting undo after a cancel, against the step before", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const t = f.history.beginTransaction("Cancelled");
        byId(f, "box1").setAttribute("lang", "de");
        const undone = f.history.undo();
        t.cancel();
        await undone;
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("does nothing on a redo that waited for a transaction that made a step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("A");

        const t = f.history.beginTransaction("New");
        byId(f, "box1").setAttribute("lang", "de");
        const redone = f.history.redo();
        t.end();
        await redone;

        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(byId(f, "box1").getAttribute("lang")).toBe("de");
        expect(f.history.undoDescriptionL10nKey).toBe("New");
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("drops the waiting requests on dispose, settling their promises", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        f.history.beginTransaction("Drag");
        byId(f, "box1").setAttribute("lang", "de");
        const undone = f.history.undo();

        disposeHistory(f);

        await undone;
        expect(byId(f, "box1").getAttribute("lang")).toBe("de");
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
    });

    it("drops the waiting requests on clearUndoAndRedo, so a later step is not undone", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const t = f.history.beginTransaction("Drag");
        byId(f, "box1").setAttribute("lang", "de");
        const undone = f.history.undo();

        f.history.clearUndoAndRedo("a test");
        await undone;
        t.end();
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");

        expect(byId(f, "box2").getAttribute("lang")).toBe("es");
        expect(byId(f, "box1").getAttribute("lang")).toBe("de");
        expect(f.history.undoDescriptionL10nKey).toBe("B");
    });

    it("finishes one waiting request before starting the next, even if a listener ends a transaction", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        const afterCalls: string[] = [];
        f.history.onAfterUndoOrRedo(() => {
            // A listener's own transaction, which changes nothing.
            f.history.beginTransaction("Listener").end();
            afterCalls.push(
                `${byId(f, "img1").getAttribute("src")} ${byId(f, "box2").getAttribute("lang")}`,
            );
        });
        const t = f.history.beginTransaction("Drag");
        const requests = [f.history.undo(), f.history.undo()];
        t.end();
        await Promise.all(requests);

        // Had the listener's end started the second undo inside the first, the first call would
        // have seen A undone too.
        expect(afterCalls).toEqual(["b.png fr", "a.png fr"]);
    });
});

describe("page history: onBeforeUndoOrRedo", () => {
    it("runs before the check, and a change it makes joins the latest step", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const calls: string[] = [];
        f.history.onBeforeUndoOrRedo(() => {
            calls.push(byId(f, "img1").getAttribute("src") ?? "none");
            // A pending refit that the component finishes now. It changes a value that A holds,
            // so if it did not join A before the check, the undo would be refused.
            byId(f, "img1").setAttribute("src", "refit.png");
            byId(f, "img1").setAttribute("data-refit", "1");
        });
        await f.history.undo();
        expect(
            calls,
            "the listener should see the page before the restore",
        ).toEqual(["b.png"]);
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("runs before redo too, and stops after unsubscribing", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const listener = vi.fn();
        const unsubscribe = f.history.onBeforeUndoOrRedo(listener);
        await f.history.undo();
        expect(listener).toHaveBeenCalledTimes(1);
        await f.history.redo();
        expect(listener).toHaveBeenCalledTimes(2);
        unsubscribe();
        await f.history.undo();
        expect(listener).toHaveBeenCalledTimes(2);
    });
});

describe("page history: onAfterUndoOrRedo", () => {
    // "The elements whose attributes, text or children were put back": read as allowing the
    // history to name an ancestor instead, since a listener re-reads the subtree of each root.
    function expectCovered(
        roots: Element[],
        element: Element,
        what: string,
    ): void {
        expect(
            roots.some((root) => root.contains(element)),
            `${what}, ${describeNode(element)}, should be one of the roots or inside one; the roots were ${roots.map(describeNode).join(", ")}`,
        ).toBe(true);
    }

    it("passes the restored roots and the direction, after the page is back", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const img = byId(f, "img1");
        const p2 = byId(f, "p2");
        const canvas = byId(f, "canvas");
        await f.history.doTransaction("A", () => {
            img.setAttribute("src", "b.png");
            (p2.firstChild as Text).data = "Bonsoir";
            canvas.appendChild(document.createElement("span"));
        });
        const afterHtml = f.page.outerHTML;
        const received: { restored: RestoredContent; html: string }[] = [];
        f.history.onAfterUndoOrRedo((restored) => {
            received.push({ restored, html: f.page.outerHTML });
        });

        await f.history.undo();
        expect(received.length).toBe(1);
        expect(received[0].restored.direction).toBe("undo");
        expect(
            received[0].html,
            "the listener should see the restored page",
        ).toBe(startHtml);
        const undoRoots = received[0].restored.roots;
        expectCovered(
            undoRoots,
            img,
            "the element whose attribute was put back",
        );
        expectCovered(undoRoots, p2, "the element whose text was put back");
        expectCovered(
            undoRoots,
            canvas,
            "the element whose children were put back",
        );

        await f.history.redo();
        expect(received.length).toBe(2);
        expect(received[1].restored.direction).toBe("redo");
        expect(received[1].html).toBe(afterHtml);
        expectCovered(
            received[1].restored.roots,
            img,
            "after redo, the element whose attribute was put back",
        );
    });

    it("calls listeners in the same task as the restore, before any other MutationObserver sees it", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        let componentObserverRan = false;
        const componentObserver = new MutationObserver(() => {
            componentObserverRan = true;
        });
        componentObserver.observe(f.page, { attributes: true, subtree: true });
        let sawComponentObserverRun: boolean | undefined;
        f.history.onAfterUndoOrRedo(() => {
            sawComponentObserverRun = componentObserverRan;
        });
        await f.history.undo();
        componentObserver.disconnect();
        expect(
            sawComponentObserverRun,
            "the listener should have been called",
        ).toBeDefined();
        expect(
            sawComponentObserverRun,
            "the listener should run before a component's own MutationObserver acts on the restore",
        ).toBe(false);
    });

    it("joins a listener's write to the latest step and clears redo (rule 8)", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "Older", byId(f, "box1"), "lang", "de");
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const unsubscribe = f.history.onAfterUndoOrRedo(() => {
            byId(f, "box2").classList.add("markup-pass-ran");
        });
        await f.history.undo();
        await letObserverDeliver();
        unsubscribe();
        expect(byId(f, "box2").classList.contains("markup-pass-ran")).toBe(
            true,
        );
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
        expect(f.history.undoDescriptionL10nKey).toBe("Older");

        await f.history.undo();
        expect(
            f.page.outerHTML,
            "the listener's write should be undone with Older",
        ).toBe(startHtml);
    });

    it("stops calling a listener after unsubscribing", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const listener = vi.fn();
        const unsubscribe = f.history.onAfterUndoOrRedo(listener);
        unsubscribe();
        await f.history.undo();
        expect(listener).not.toHaveBeenCalled();
    });
});

describe("page history: restoring the selection on undo and redo", () => {
    // Restoring the selection can focus a box, and the browser runs the focus handlers at once. A
    // handler that calls into the history then must not make it take what undo or redo itself
    // wrote as a change made outside any transaction.
    it("takes its own changes as its own when a handler that the restored selection sets off calls into the history", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(f, "Older", byId(f, "box1"), "lang", "de");
        const selection = document.getSelection()!;
        selection.collapse(byId(f, "p2").firstChild, 0);
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        selection.removeAllRanges();
        const setBaseAndExtent = selection.setBaseAndExtent.bind(selection);
        let handlerCalls = 0;
        const restoring = vi
            .spyOn(selection, "setBaseAndExtent")
            .mockImplementation((...args) => {
                setBaseAndExtent(...args);
                // What a focus handler might do.
                f.history.doOutsideHistory(() => {});
                handlerCalls++;
            });
        try {
            await f.history.undo();
            expect(
                handlerCalls,
                "test setup: undo should have restored the selection once",
            ).toBe(1);
            expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
            expect(f.unmarked).not.toHaveBeenCalled();
            expect(f.history.undoDescriptionL10nKey).toBe("Older");
            expect(f.history.redoDescriptionL10nKey).toBe("A");

            await f.history.redo();
            expect(handlerCalls).toBe(2);
            expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
            expect(f.unmarked).not.toHaveBeenCalled();
            expect(f.history.undoDescriptionL10nKey).toBe("A");

            await f.history.undo();
            await f.history.undo();
            expect(f.page.outerHTML).toBe(startHtml);
        } finally {
            restoring.mockRestore();
        }
    });
});

describe("page history: descriptions", () => {
    it("names the steps that undo and redo would take", async () => {
        const f = setUp();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBeUndefined();
        expect(f.history.hasOpenTransaction).toBe(false);
        await stepSettingAttribute(
            f,
            "EditTab.Undo.First",
            byId(f, "img1"),
            "src",
            "b.png",
        );
        await stepSettingAttribute(
            f,
            "EditTab.Undo.Second",
            byId(f, "box2"),
            "lang",
            "es",
        );
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.Second");
        await f.history.undo();
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.First");
        expect(f.history.redoDescriptionL10nKey).toBe("EditTab.Undo.Second");
        await f.history.undo();
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBe("EditTab.Undo.First");
        await f.history.redo();
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.First");
        expect(f.history.redoDescriptionL10nKey).toBe("EditTab.Undo.Second");
    });

    it("does nothing on undo() or redo() with no step to take", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await f.history.undo();
        await f.history.redo();
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("lists every undo step oldest first, and every redo step in the order redo takes them", async () => {
        const f = setUp();
        expect(listedSteps(f)).toEqual({
            undo: [],
            redo: [],
        });
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        await stepSettingAttribute(f, "C", byId(f, "box1"), "lang", "de");
        expect(listedSteps(f)).toEqual({
            undo: ["A", "B", "C"],
            redo: [],
        });

        await f.history.undo();
        await f.history.undo();

        expect(listedSteps(f)).toEqual({
            undo: ["A"],
            redo: ["B", "C"],
        });
        expect(
            f.history.undoDescriptionL10nKey,
            "the last undo step should be the one undo would undo",
        ).toBe("A");
        expect(
            f.history.redoDescriptionL10nKey,
            "the first redo step should be the one redo would redo",
        ).toBe("B");
        f.history.clearUndoAndRedo("a test");
        expect(listedSteps(f)).toEqual({
            undo: [],
            redo: [],
        });
    });

    it("lists the steps without taking in a change that the observer has not delivered", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        expect(listedSteps(f), "test setup: B can be redone").toEqual({
            undo: ["A"],
            redo: ["B"],
        });

        // Outside any transaction: once the history takes it in, it joins A and clears redo.
        byId(f, "box1").setAttribute("lang", "de");
        expect(listedSteps(f)).toEqual({
            undo: ["A"],
            redo: ["B"],
        });
        expect(
            f.unmarked,
            "listing the steps should not have taken in the change",
        ).not.toHaveBeenCalled();

        await letObserverDeliver();
        expect(f.unmarked).toHaveBeenCalled();
        expect(listedSteps(f)).toEqual({
            undo: ["A"],
            redo: [],
        });
    });
});

// The steps that getStepDescriptions lists, each as formatUndoDescription writes it.
function listedSteps(f: IFixture): { undo: string[]; redo: string[] } {
    const steps = f.history.getStepDescriptions();
    const format = (description: UndoDescription) =>
        formatUndoDescription(description.l10nId, description.args);
    return { undo: steps.undo.map(format), redo: steps.redo.map(format) };
}

describe("page history: descriptions made of a template and its arguments", () => {
    const kDeleteTable: UndoDescription = {
        l10nId: "EditTab.Undo.Delete",
        args: ["EditTab.Undo.Object.Table"],
    };

    it("keeps the arguments with the step through undo and redo, and names the step with them in the log", async () => {
        const log = vi.fn<(line: string, kind: PageHistoryLogKind) => void>();
        const f = setUp(kStandardPage, { log });
        await f.history.doTransaction(kDeleteTable, () =>
            byId(f, "img1").setAttribute("src", "b.png"),
        );
        expect(
            f.history.undoDescriptionL10nKey,
            "the step's l10n id is the template's",
        ).toBe("EditTab.Undo.Delete");
        expect(f.history.getStepDescriptions().undo).toEqual([kDeleteTable]);

        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("EditTab.Undo.Delete");
        expect(f.history.getStepDescriptions().redo).toEqual([kDeleteTable]);
        await f.history.redo();
        expect(f.history.getStepDescriptions().undo).toEqual([kDeleteTable]);

        expect(log.mock.calls).toEqual([
            [
                "Page history: step EditTab.Undo.Delete(EditTab.Undo.Object.Table)",
                "step",
            ],
            [
                "Page history: undo EditTab.Undo.Delete(EditTab.Undo.Object.Table)",
                "undo",
            ],
            [
                "Page history: redo EditTab.Undo.Delete(EditTab.Undo.Object.Table)",
                "redo",
            ],
        ]);
    });

    it("gives beginTransaction's transaction and onClose the arguments", () => {
        const f = setUp();
        const t = f.history.beginTransaction(kDeleteTable);
        expect(t.descriptionL10nKey).toBe("EditTab.Undo.Delete");
        expect(t.descriptionArgs).toEqual(["EditTab.Undo.Object.Table"]);
        const closed =
            vi.fn<(key?: string, args?: readonly string[]) => void>();
        t.onClose(closed);
        byId(f, "img1").setAttribute("src", "b.png");
        t.end();
        expect(closed).toHaveBeenCalledWith("EditTab.Undo.Delete", [
            "EditTab.Undo.Object.Table",
        ]);
    });

    it("gives a plain l10n id no arguments, and shows it as before", async () => {
        const log = vi.fn<(line: string, kind: PageHistoryLogKind) => void>();
        const f = setUp(kStandardPage, { log });
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        expect(f.history.getStepDescriptions().undo[0].args).toBeUndefined();
        expect(log).toHaveBeenCalledWith("Page history: step A", "step");
        expect(formatUndoDescription("A", undefined)).toBe("A");
        expect(formatUndoDescription("A", [])).toBe("A");
    });

    it("lets a nested description with arguments describe the stand-in's step, comparing the template's id with the general one", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        await f.history.doTransaction(kDeleteTable, () =>
            byId(f, "img1").setAttribute("src", "b.png"),
        );
        await f.history.doTransaction(
            { l10nId: "EditTab.Undo.Later", args: ["X"] },
            () => byId(f, "box2").setAttribute("lang", "es"),
        );
        click.end();
        expect(listedSteps(f).undo).toEqual([
            "EditTab.Undo.Delete(EditTab.Undo.Object.Table)",
        ]);

        // A general description with arguments is still the general one.
        const press = f.history.beginTransaction({
            l10nId: kGenericUndoDescriptionL10nKey,
            args: ["X"],
        });
        await f.history.doTransaction("EditTab.Undo.Plain", () =>
            byId(f, "box1").setAttribute("lang", "de"),
        );
        press.end();
        expect(listedSteps(f).undo).toEqual([
            "EditTab.Undo.Delete(EditTab.Undo.Object.Table)",
            "EditTab.Undo.Plain",
        ]);
    });

    it("drops the arguments with the description when the nested transaction that gave them is cancelled", () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const inner = f.history.beginTransaction(kDeleteTable);
        byId(f, "img1").setAttribute("src", "b.png");
        inner.cancel();
        byId(f, "box2").setAttribute("lang", "es");
        click.end();
        expect(f.history.getStepDescriptions().undo).toEqual([
            { l10nId: kGenericUndoDescriptionL10nKey },
        ]);
    });

    it("reports the arguments in the details", async () => {
        const f = setUp();
        const seen: PageHistoryDetail[] = [];
        f.history.setDetailListener((detail) => {
            if (detail.kind !== "changes") seen.push(detail);
        });
        await f.history.doTransaction(kDeleteTable, () =>
            byId(f, "img1").setAttribute("src", "b.png"),
        );
        expect(seen).toEqual([
            {
                kind: "opened",
                descriptionL10nKey: "EditTab.Undo.Delete",
                descriptionArgs: ["EditTab.Undo.Object.Table"],
                depth: 1,
            },
            {
                kind: "ended",
                descriptionL10nKey: "EditTab.Undo.Delete",
                descriptionArgs: ["EditTab.Undo.Object.Table"],
                depth: 1,
            },
        ]);
    });
});

describe("page history: a transaction nested in a general one describes the step", () => {
    // Bloom opens a transaction with the general description on a real click or key press; the
    // first transaction nested inside it with a description of its own names the step.
    it("takes the description of the first nested doTransaction", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        await f.history.doTransaction("EditTab.Undo.Delete", () =>
            byId(f, "img1").setAttribute("src", "b.png"),
        );
        await f.history.doTransaction("EditTab.Undo.Later", () =>
            byId(f, "box2").setAttribute("lang", "es"),
        );
        click.end();
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.Delete");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("EditTab.Undo.Delete");
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
    });

    it("takes the description of a nested beginTransaction, and of one nested deeper", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const inner = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const drag = f.history.beginTransaction("EditTab.Undo.Drag");
        byId(f, "img1").setAttribute("src", "b.png");
        drag.end();
        inner.end();
        click.end();
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.Drag");
    });

    it("keeps the description of an outermost transaction that is not general", async () => {
        const f = setUp();
        await f.history.doTransaction("EditTab.Undo.Paste", async () => {
            await f.history.doTransaction("EditTab.Undo.ChangeImage", () =>
                byId(f, "img1").setAttribute("src", "b.png"),
            );
        });
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.Paste");
    });

    it("goes back to the general description when the describing transaction fails, and lets a later one describe it", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        await expect(
            f.history.doTransaction("EditTab.Undo.Failed", () => {
                byId(f, "img1").setAttribute("src", "b.png");
                throw new Error("expected by the test");
            }),
        ).rejects.toThrow("expected by the test");
        byId(f, "box2").setAttribute("lang", "es");
        click.end();
        expect(f.history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );

        const secondClick = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const cancelled = f.history.beginTransaction("EditTab.Undo.Cancelled");
        byId(f, "img1").setAttribute("src", "c.png");
        cancelled.cancel();
        await f.history.doTransaction("EditTab.Undo.Kept", () =>
            byId(f, "img1").setAttribute("src", "d.png"),
        );
        secondClick.end();
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.Kept");
    });

    it("is general when the nested transaction is cancelled along with one nested in it", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const outerNested = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        f.history.beginTransaction("EditTab.Undo.Inner");
        byId(f, "img1").setAttribute("src", "b.png");
        outerNested.cancel();
        byId(f, "box2").setAttribute("lang", "es");
        click.end();
        expect(f.history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
    });
});

describe("page history: what end() returns", () => {
    it("is true only from the call that makes an undo step", async () => {
        const f = setUp();
        const outer = f.history.beginTransaction("Outer");
        const inner = f.history.beginTransaction("Inner");
        byId(f, "img1").setAttribute("src", "b.png");
        expect(inner.end(), "a nested transaction makes no step").toBe(false);
        expect(outer.end()).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBe("Outer");
        expect(outer.end(), "it has already ended").toBe(false);
        expect(inner.end(), "it has already ended").toBe(false);

        const nothing = f.history.beginTransaction("Nothing");
        expect(nothing.end(), "nothing changed").toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("Outer");

        const cleared = f.history.beginTransaction("Cleared");
        byId(f, "box2").setAttribute("lang", "es");
        f.history.clearUndoAndRedo("a test");
        expect(cleared.end(), "clearUndoAndRedo ended it").toBe(false);
    });
});

describe("page history: the general transaction waits for transactions begun inside it", () => {
    // Bloom opens a transaction with the general description on a real click or key press and
    // ends it just after the release, but the click may have begun a transaction that lasts
    // longer, such as a dialog's.
    it("closes when a dialog-style transaction begun inside it ends, taking its description", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        byId(f, "box2").setAttribute("lang", "es");
        const dialog = f.history.beginTransaction("EditTab.Undo.Dialog");

        expect(
            click.end(),
            "the click's end does not throw or make a step",
        ).toBe(false);
        expect(f.history.hasOpenTransaction).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(click.end(), "a second end does nothing").toBe(false);
        click.cancel();
        expect(
            byId(f, "box2").getAttribute("lang"),
            "cancel() after end() does nothing",
        ).toBe("es");

        byId(f, "img1").setAttribute("src", "b.png");
        expect(dialog.end(), "the dialog's end closes the click's step").toBe(
            true,
        );
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.Dialog");

        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("closes when an async doTransaction begun inside it settles, taking its description", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        let answerFromCSharp!: () => void;
        const answered = new Promise<void>((resolve) => {
            answerFromCSharp = resolve;
        });
        const paste = f.history.doTransaction(
            "EditTab.Undo.Paste",
            async () => {
                byId(f, "box2").setAttribute("lang", "es");
                await answered;
                byId(f, "img1").setAttribute("src", "b.png");
            },
        );
        expect(click.end()).toBe(false);
        expect(f.history.hasOpenTransaction).toBe(true);

        answerFromCSharp();
        await paste;

        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.Paste");
        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
    });

    it("waits for the last of several transactions begun inside it", () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const first = f.history.beginTransaction("EditTab.Undo.First");
        byId(f, "img1").setAttribute("src", "b.png");
        click.end();
        const second = f.history.beginTransaction("EditTab.Undo.Second");
        byId(f, "box2").setAttribute("lang", "es");
        expect(second.end()).toBe(false);
        expect(f.history.hasOpenTransaction).toBe(true);
        expect(first.end()).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBe("EditTab.Undo.First");
    });

    it("closes with whatever else changed when the transaction begun inside it is cancelled", async () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        byId(f, "box2").setAttribute("lang", "es");
        const dialog = f.history.beginTransaction("EditTab.Undo.Dialog");
        byId(f, "img1").setAttribute("src", "b.png");
        click.end();

        dialog.cancel();

        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        await f.history.undo();
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
    });

    it("makes no step when nothing is left after the transaction begun inside it is cancelled", () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const dialog = f.history.beginTransaction("EditTab.Undo.Dialog");
        byId(f, "img1").setAttribute("src", "b.png");
        click.end();
        dialog.cancel();
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("still cancels everything when the general transaction itself is cancelled before its end", () => {
        const f = setUp();
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        byId(f, "box2").setAttribute("lang", "es");
        const dialog = f.history.beginTransaction("EditTab.Undo.Dialog");
        byId(f, "img1").setAttribute("src", "b.png");
        click.cancel();
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(dialog.end()).toBe(false);
    });

    it("still throws in development when any other outer transaction ends before an inner one", () => {
        const f = setUp();
        const generic = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const nestedGeneric = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        f.history.beginTransaction("EditTab.Undo.Inner");
        expect(
            () => nestedGeneric.end(),
            "only the outermost general transaction waits",
        ).toThrow();
        generic.end();
        expect(f.history.hasOpenTransaction).toBe(false);
    });
});

describe("page history: isDisposed", () => {
    it("is false until dispose() runs", () => {
        const f = setUp();
        expect(f.history.isDisposed).toBe(false);
        disposeHistory(f);
        expect(f.history.isDisposed).toBe(true);
    });
});

describe("page history: maxUndoSteps", () => {
    it("keeps only the newest steps when given a limit", async () => {
        const f = setUp(kStandardPage, { maxUndoSteps: 3 });
        const img = byId(f, "img1");
        for (let i = 1; i <= 5; i++) {
            await stepSettingAttribute(f, `Step${i}`, img, "data-step", `${i}`);
        }
        await f.history.undo();
        await f.history.undo();
        await f.history.undo();
        expect(img.getAttribute("data-step")).toBe("2");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        await f.history.undo();
        expect(
            img.getAttribute("data-step"),
            "the oldest steps should be gone",
        ).toBe("2");
    });

    it("keeps 50 steps by default", async () => {
        const f = setUp();
        const img = byId(f, "img1");
        for (let i = 1; i <= 51; i++) {
            await stepSettingAttribute(f, `Step${i}`, img, "data-step", `${i}`);
        }
        for (let i = 0; i < 50; i++) {
            expect(f.history.undoDescriptionL10nKey).toBe(`Step${51 - i}`);
            await f.history.undo();
        }
        expect(img.getAttribute("data-step")).toBe("1");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });
});

describe("page history: the filter (rule 6)", () => {
    // The filter answers only for the node itself; the history must apply it to everything inside
    // an ignored node.
    const filter: PageHistoryFilter = {
        ...kKeepEverythingFilter,
        ignoreNode: (node) =>
            node instanceof Element && node.classList.contains("bloom-ui"),
        keptAttributeValue: (_element, attributeName, value) =>
            attributeName === "data-hover" ? undefined : value,
        valueToPutBack: (_element, attributeName, kept, current) =>
            attributeName === "data-hover" ? current : kept,
    };
    const kPageWithUi =
        kStandardPage +
        '<div class="bloom-ui" id="ui"><span id="uiChild" data-state="a">handle</span></div>';

    it("neither records nor puts back an ignored node", async () => {
        const f = setUp(kPageWithUi, { filter });
        const canvas = byId(f, "canvas");
        const handle = document.createElement("div");
        handle.className = "bloom-ui";
        await f.history.doTransaction("A", () => {
            byId(f, "img1").setAttribute("src", "b.png");
            canvas.appendChild(handle);
        });
        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(
            handle.parentElement,
            "undo should leave the ignored node in place",
        ).toBe(canvas);
        handle.remove();
        await letObserverDeliver();
        expect(
            f.unmarked,
            "removing an ignored node is not an unmarked change",
        ).not.toHaveBeenCalled();
        await f.history.redo();
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(
            handle.isConnected,
            "redo should not put back the ignored node",
        ).toBe(false);
    });

    it("ignores a node that is given its ignored class straight after it is inserted", async () => {
        // As the Motion tool does with its rectangles (makeElement, then bloom-ui), and as
        // PageHistoryFilter.ignoreNode requires: no call to the history comes between.
        const f = setUp(kPageWithUi, { filter });
        const canvas = byId(f, "canvas");
        const childrenBefore = Array.from(canvas.childNodes);
        const handle = document.createElement("div");
        await f.history.doTransaction("A", () => {
            byId(f, "img1").setAttribute("src", "b.png");
            canvas.appendChild(handle);
            handle.className = "bloom-ui";
        });

        await f.history.undo();

        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(
            handle.parentElement,
            "undo should leave the ignored node in place",
        ).toBe(canvas);
        expect(Array.from(canvas.childNodes)).toEqual([
            ...childrenBefore,
            handle,
        ]);
        await f.history.redo();
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(f.history.undoDescriptionL10nKey).toBe("A");
    });

    it("neither records nor puts back an ignored attribute", async () => {
        const f = setUp(kPageWithUi, { filter });
        const img = byId(f, "img1");
        await f.history.doTransaction("A", () => {
            img.setAttribute("src", "b.png");
            img.setAttribute("data-hover", "yes");
        });
        await f.history.undo();
        expect(img.getAttribute("src")).toBe("a.png");
        expect(img.getAttribute("data-hover")).toBe("yes");
    });

    it("ignores changes inside an ignored node and to ignored attributes outside any transaction", async () => {
        const f = setUp(kPageWithUi, { filter });
        await stepSettingAttribute(f, "A", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("A");

        byId(f, "uiChild").setAttribute("data-state", "b");
        (byId(f, "uiChild").firstChild as Text).data = "moved";
        byId(f, "uiChild").appendChild(document.createElement("i"));
        byId(f, "img1").setAttribute("data-hover", "yes");
        await letObserverDeliver();

        expect(f.unmarked).not.toHaveBeenCalled();
        expect(f.history.redoDescriptionL10nKey).toBe("A");
        await f.history.redo();
        expect(byId(f, "box2").getAttribute("lang")).toBe("es");
    });

    it("makes no step, and keeps redo, for a transaction that changes only ignored things", async () => {
        const f = setUp(kPageWithUi, { filter });
        await stepSettingAttribute(f, "A", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        await f.history.doTransaction("Hover", () => {
            byId(f, "img1").setAttribute("data-hover", "yes");
            byId(f, "uiChild").setAttribute("data-state", "b");
        });
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBe("A");
    });

    it("does not refuse an undo because an ignored attribute changed on an element that the step holds", async () => {
        const f = setUp(kPageWithUi, { filter });
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        byId(f, "img1").setAttribute("data-hover", "yes");
        await letObserverDeliver();
        await f.history.undo();
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(byId(f, "img1").getAttribute("data-hover")).toBe("yes");
    });
});

describe("page history: a node made and thrown away within one batch", () => {
    // As the overflow checker does: it appends a hidden clone of a box to measure it, changes
    // styles inside it, and removes it again, all in one task.
    function measureWithClone(f: IFixture, moveIn?: Node): void {
        const clone = byId(f, "box1").cloneNode(true) as HTMLElement;
        clone.removeAttribute("id");
        clone.querySelectorAll("[id]").forEach((e) => e.removeAttribute("id"));
        clone.setAttribute(
            "style",
            "min-height: 24px; visibility: hidden; position: absolute; width: 450px;",
        );
        byId(f, "group").appendChild(clone);
        const paragraph = clone.querySelector("p") as HTMLElement;
        paragraph.style.flexGrow = "1";
        clone.style.flexGrow = "1";
        (paragraph.firstChild as Text).data = "measured";
        if (moveIn) {
            paragraph.appendChild(moveIn);
            (moveIn as Element).setAttribute("data-x", "1");
            (moveIn.firstChild as Text).data = "changed";
        }
        paragraph.style.flexGrow = "";
        clone.remove();
    }

    it("is no change outside any transaction, and keeps redo", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey, "test setup").toBe("A");

        measureWithClone(f);
        await letObserverDeliver();

        expect(f.unmarked).not.toHaveBeenCalled();
        expect(f.history.redoDescriptionL10nKey).toBe("A");
        await f.history.redo();
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
    });

    it("makes no step inside a transaction", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.undo();

        await f.history.doTransaction("Measure", () => measureWithClone(f));

        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(f.history.redoDescriptionL10nKey).toBe("A");
    });

    it("still counts a change to a node of the page that was moved into it", async () => {
        const f = setUp();
        const bold = byId(f, "b1");
        await f.history.doTransaction("Move", () => {
            measureWithClone(f, bold);
        });
        expect(bold.isConnected, "test setup").toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("Move");

        await f.history.undo();

        expect(bold.parentNode).toBe(byId(f, "p1"));
        expect(bold.hasAttribute("data-x")).toBe(false);
        expect(bold.textContent).toBe("bold");
    });
});

describe("page history: dispose", () => {
    it("makes every later call throw", async () => {
        const f = setUp();
        disposeHistory(f);
        expect(() => f.history.beginTransaction("X")).toThrow();
        expect(() => f.history.addToLatestTransaction(() => 1)).toThrow();
        expect(() => f.history.doOutsideHistory(() => 1)).toThrow();
        expect(() => f.history.clearUndoAndRedo("a test")).toThrow();
        expect(() => f.history.onBeforeUndoOrRedo(() => undefined)).toThrow();
        expect(() => f.history.onAfterUndoOrRedo(() => undefined)).toThrow();
        expect(() => f.history.dispose()).toThrow();
        await expect(
            settle(() => f.history.doTransaction("X", () => 1)),
        ).rejects.toThrow();
        await expect(settle(() => f.history.undo())).rejects.toThrow();
        await expect(settle(() => f.history.redo())).rejects.toThrow();
    });

    it("ends an open transaction, leaving its changes, and stops observing", async () => {
        const f = setUp();
        f.history.beginTransaction("Drag");
        byId(f, "img1").setAttribute("src", "b.png");
        disposeHistory(f);
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        byId(f, "box1").setAttribute("lang", "de");
        await letObserverDeliver();
        expect(f.unmarked).not.toHaveBeenCalled();
    });

    it("does not report changes made after it on a page that had steps", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        disposeHistory(f);
        byId(f, "box1").setAttribute("lang", "de");
        await letObserverDeliver();
        expect(f.unmarked).not.toHaveBeenCalled();
    });
});

describe("page history: before the page is ready (startsReady false, markPageReady)", () => {
    it("does not record a change made outside any transaction before ready, nor join it to a step", async () => {
        const f = setUp(kStandardPage, { startsReady: false });
        const user = f.history.beginTransaction(kGenericUndoDescriptionL10nKey);
        byId(f, "box1").setAttribute("data-x", "user");
        expect(user.end(), "test setup: the user's change makes a step").toBe(
            true,
        );

        // Bloom's setup, after the user's step but before ready.
        byId(f, "box2").setAttribute("data-setup", "1");
        await letObserverDeliver();
        expect(f.unmarked).not.toHaveBeenCalled();

        await f.history.undo();
        expect(
            byId(f, "box1").hasAttribute("data-x"),
            "undo should take back the user's change",
        ).toBe(false);
        expect(
            byId(f, "box2").getAttribute("data-setup"),
            "the setup change joined no step, so undo leaves it",
        ).toBe("1");
    });

    it("keeps redo when a change is made outside any transaction before ready", async () => {
        const f = setUp(kStandardPage, { startsReady: false });
        await f.history.doTransaction("Test.Edit", () =>
            byId(f, "box1").setAttribute("data-x", "1"),
        );
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("Test.Edit");

        byId(f, "box2").setAttribute("data-setup", "1");
        await letObserverDeliver();

        expect(f.history.redoDescriptionL10nKey).toBe("Test.Edit");
        await f.history.redo();
        expect(byId(f, "box1").getAttribute("data-x")).toBe("1");
    });

    it("records a transaction before ready, and undoes it", async () => {
        const f = setUp(kStandardPage, { startsReady: false });
        const before = f.page.innerHTML;
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        byId(f, "p2").textContent = "Salut";
        click.end();
        expect(f.history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );

        await f.history.undo();
        expect(f.page.innerHTML).toBe(before);
    });

    it("applies rule 1 again once the page is marked ready", async () => {
        const f = setUp(kStandardPage, { startsReady: false });
        await f.history.doTransaction("Test.Edit", () =>
            byId(f, "box1").setAttribute("data-x", "1"),
        );
        // Made before ready but not yet delivered when markPageReady is called: still setup.
        byId(f, "box2").setAttribute("data-setup", "1");
        f.history.markPageReady();
        await letObserverDeliver();
        expect(
            f.unmarked,
            "a change made before markPageReady is setup, even if delivered after",
        ).not.toHaveBeenCalled();

        byId(f, "box2").setAttribute("data-late", "1");
        await letObserverDeliver();
        expect(f.unmarked).toHaveBeenCalled();

        await f.history.undo();
        expect(byId(f, "box1").hasAttribute("data-x")).toBe(false);
        expect(
            byId(f, "box2").hasAttribute("data-late"),
            "after ready, an unmarked change joins the latest step",
        ).toBe(false);
        expect(byId(f, "box2").getAttribute("data-setup")).toBe("1");
    });

    it("refuses an undo when a setup change before ready wrote a value that the step holds (rule 4)", async () => {
        const f = setUp(kStandardPage, { startsReady: false });
        await f.history.doTransaction("Test.Edit", () =>
            byId(f, "box1").setAttribute("data-x", "user"),
        );
        byId(f, "box1").setAttribute("data-x", "setup");
        await letObserverDeliver();
        expect(
            f.history.undoDescriptionL10nKey,
            "test setup: the step should still be there",
        ).toBe("Test.Edit");

        await f.history.undo();

        expect(
            byId(f, "box1").getAttribute("data-x"),
            "a refused undo must leave the page as it is",
        ).toBe("setup");
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("starts ready by default", async () => {
        const f = setUp();
        await f.history.doTransaction("Test.Edit", () =>
            byId(f, "box1").setAttribute("data-x", "1"),
        );
        byId(f, "box2").setAttribute("data-late", "1");
        await letObserverDeliver();
        expect(f.unmarked).toHaveBeenCalled();
    });
});

describe("page history: addToLatestTransaction while a stand-in is open", () => {
    // Bloom's delayed reactions to an earlier action, such as the toolbox's markup pass half a
    // second after typing, may come while the user's next press holds the stand-in open.
    it("joins the latest step when the stand-in has changed nothing, and the stand-in makes no step", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        await stepSettingAttribute(
            f,
            "Earlier",
            byId(f, "img1"),
            "src",
            "b.png",
        );
        const press = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        let received: Transaction | undefined;

        f.history.addToLatestTransaction((transaction) => {
            received = transaction;
            byId(f, "box1").setAttribute("lang", "de");
        });

        expect(received?.descriptionL10nKey).toBe("Earlier");
        expect(press.end(), "the stand-in holds no change").toBe(false);
        expect(f.unmarked).not.toHaveBeenCalled();
        expect(f.history.undoDescriptionL10nKey).toBe("Earlier");
        await f.history.undo();
        expect(
            f.page.outerHTML,
            "one undo should take back the step and the reaction",
        ).toBe(startHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("joins the stand-in when it has changed something already", async () => {
        const f = setUp();
        await stepSettingAttribute(
            f,
            "Earlier",
            byId(f, "img1"),
            "src",
            "b.png",
        );
        const press = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        byId(f, "box2").setAttribute("lang", "es");

        f.history.addToLatestTransaction(() =>
            byId(f, "box1").setAttribute("lang", "de"),
        );

        expect(press.end()).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        await f.history.undo();
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(f.history.undoDescriptionL10nKey).toBe("Earlier");
    });

    it("joins a named transaction, and one open inside the stand-in, even when they have changed nothing", async () => {
        const f = setUp();
        await stepSettingAttribute(
            f,
            "Earlier",
            byId(f, "img1"),
            "src",
            "b.png",
        );
        const named = f.history.beginTransaction("Test.Named");
        f.history.addToLatestTransaction(() =>
            byId(f, "box1").setAttribute("lang", "de"),
        );
        expect(named.end()).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBe("Test.Named");

        const press = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const dialog = f.history.beginTransaction("Test.Dialog");
        f.history.addToLatestTransaction(() =>
            byId(f, "box2").setAttribute("lang", "es"),
        );
        press.end();
        expect(dialog.end()).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBe("Test.Dialog");
        await f.history.undo();
        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
        expect(byId(f, "box1").getAttribute("lang")).toBe("de");
    });

    it("makes no step of the stand-in when it changed a value and changed it back before the reaction changed it", async () => {
        const f = setUp();
        await stepSettingAttribute(
            f,
            "Earlier",
            byId(f, "img1"),
            "src",
            "b.png",
        );
        const press = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        byId(f, "box1").setAttribute("lang", "de");
        await letObserverDeliver();
        byId(f, "box1").setAttribute("lang", "en");
        await letObserverDeliver();

        f.history.addToLatestTransaction(() =>
            byId(f, "box1").setAttribute("lang", "fr"),
        );

        expect(press.end()).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("Earlier");
        await f.history.undo();
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
    });
});

describe("page history: async transactions that overlap in time", () => {
    // B begins while A awaits, and A settles first: neither awaits the other.
    it("lets the first settle without error, and makes the step when the last ends", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const gateA = makeGate();
        const gateB = makeGate();
        const a = f.history.doTransaction("Test.A", async () => {
            byId(f, "img1").setAttribute("src", "b.png");
            await gateA.promise;
            byId(f, "box1").setAttribute("lang", "de");
            return "a";
        });
        const b = f.history.doTransaction("Test.B", async () => {
            byId(f, "box2").setAttribute("lang", "es");
            await gateB.promise;
            byId(f, "img1").setAttribute("alt", "picture");
            return "b";
        });

        gateA.open();
        expect(await a).toBe("a");
        expect(f.history.hasOpenTransaction, "the step waits for B").toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();

        gateB.open();
        expect(await b).toBe("b");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(
            f.unmarked,
            "the write that B made after A settled is inside the transaction",
        ).not.toHaveBeenCalled();
        expect(f.history.undoDescriptionL10nKey).toBe("Test.A");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("puts back what changed since the second began when it fails after the first settled", async () => {
        const f = setUp();
        const startHtml = f.page.outerHTML;
        const gateA = makeGate();
        const gateB = makeGate();
        const a = f.history.doTransaction("Test.A", async () => {
            byId(f, "img1").setAttribute("src", "b.png");
            await gateA.promise;
        });
        const b = f.history.doTransaction("Test.B", async () => {
            byId(f, "box2").setAttribute("lang", "es");
            await gateB.promise;
            byId(f, "box1").setAttribute("lang", "de");
            throw new Error("B failed");
        });
        gateA.open();
        await a;

        gateB.open();
        await expect(b).rejects.toThrow("B failed");

        expect(byId(f, "box2").getAttribute("lang")).toBe("fr");
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(
            byId(f, "img1").getAttribute("src"),
            "the change that A made before B began stays",
        ).toBe("b.png");
        expect(f.history.hasOpenTransaction).toBe(false);
        expect(f.history.undoDescriptionL10nKey).toBe("Test.A");
        await f.history.undo();
        expect(f.page.outerHTML).toBe(startHtml);
    });

    it("lets an async transaction settle while a beginTransaction begun after it is open", async () => {
        const f = setUp();
        const gate = makeGate();
        const a = f.history.doTransaction("Test.A", async () => {
            byId(f, "img1").setAttribute("src", "b.png");
            await gate.promise;
        });
        const dialog = f.history.beginTransaction("Test.Dialog");
        byId(f, "box1").setAttribute("lang", "de");
        gate.open();
        await a;
        expect(f.history.hasOpenTransaction).toBe(true);
        expect(dialog.end()).toBe(true);
        expect(f.history.undoDescriptionL10nKey).toBe("Test.A");
    });
});

describe("page history: onClose", () => {
    it("reports the step that closing the outermost transaction made, when it closes", () => {
        const f = setUp();
        const reported: (string | undefined)[] = [];
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        click.onClose((description) => reported.push(description));
        byId(f, "box2").setAttribute("lang", "es");
        const dialog = f.history.beginTransaction("Test.Dialog");
        byId(f, "img1").setAttribute("src", "b.png");

        expect(click.end()).toBe(false);
        expect(
            reported,
            "the click's transaction waits for the dialog's",
        ).toEqual([]);
        dialog.cancel();

        expect(
            reported,
            "cancelling the dialog closes the click's step, which is general again",
        ).toEqual([kGenericUndoDescriptionL10nKey]);
        const late: (string | undefined)[] = [];
        click.onClose((description) => late.push(description));
        expect(
            late,
            "a listener added after the close is called at once",
        ).toEqual([kGenericUndoDescriptionL10nKey]);
    });

    it("reports no step for a transaction that changed nothing, was cancelled, or was ended by clearUndoAndRedo", () => {
        const f = setUp();
        const reported: (string | undefined)[] = [];
        const unchanged = f.history.beginTransaction("Test.Unchanged");
        unchanged.onClose((description) => reported.push(description));
        unchanged.end();
        const cancelled = f.history.beginTransaction("Test.Cancelled");
        cancelled.onClose((description) => reported.push(description));
        byId(f, "img1").setAttribute("src", "b.png");
        cancelled.cancel();
        const cleared = f.history.beginTransaction("Test.Cleared");
        cleared.onClose((description) => reported.push(description));
        byId(f, "img1").setAttribute("src", "c.png");
        f.history.clearUndoAndRedo("a test");
        expect(reported).toEqual([undefined, undefined, undefined]);
    });

    it("reports the description that a nested transaction gave the step", async () => {
        const f = setUp();
        const reported: (string | undefined)[] = [];
        const click = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        click.onClose((description) => reported.push(description));
        await f.history.doTransaction("Test.Command", () =>
            byId(f, "img1").setAttribute("src", "b.png"),
        );
        click.end();
        expect(reported).toEqual(["Test.Command"]);
    });
});

describe("page history: log", () => {
    it("logs each step made, undone and redone, by its description", async () => {
        const log = vi.fn<(line: string) => void>();
        const f = setUp(kStandardPage, { log });
        expect(
            log,
            "nothing should be logged before any change",
        ).not.toHaveBeenCalled();

        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.doTransaction("Nothing", () => {});
        await f.history.undo();
        await f.history.redo();

        expect(log.mock.calls.map((call) => call[0])).toEqual([
            "Page history: step A",
            "Page history: undo A",
            "Page history: redo A",
        ]);
    });

    it("says what kind of line each is", async () => {
        const log = vi.fn<(line: string, kind: PageHistoryLogKind) => void>();
        const f = setUp(kStandardPage, { log });
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.undo();
        await f.history.redo();
        f.history.clearUndoAndRedo("a test");

        expect(log.mock.calls.map((call) => call[1])).toEqual([
            "step",
            "undo",
            "redo",
            "cleared",
        ]);
    });

    it("logs clearUndoAndRedo with its reason", () => {
        const log = vi.fn<(line: string, kind: PageHistoryLogKind) => void>();
        const f = setUp(kStandardPage, { log });
        f.history.clearUndoAndRedo("request some/endpoint");
        expect(log).toHaveBeenCalledWith(
            "Page history: cleared by request some/endpoint",
            "cleared",
        );
    });

    it("logs a cancel that reversed changes as a rollback, and one that reversed none not at all", () => {
        const log = vi.fn<(line: string, kind: PageHistoryLogKind) => void>();
        const f = setUp(kStandardPage, { log });
        f.history.beginTransaction("Nothing").cancel();
        expect(
            log,
            "a cancel with nothing to reverse should log nothing",
        ).not.toHaveBeenCalled();

        const t = f.history.beginTransaction("Typing");
        byId(f, "img1").setAttribute("src", "b.png");
        byId(f, "img1").setAttribute("alt", "a picture");
        t.cancel();

        expect(byId(f, "img1").getAttribute("src")).toBe("a.png");
        expect(log.mock.calls).toEqual([
            ["Page history: rollback of Typing reversed 2 records", "rollback"],
        ]);
    });

    it("logs an undo that found the page changed since its step", async () => {
        const log = vi.fn<(line: string) => void>();
        const f = setUp(kStandardPage, { log });
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        f.history.doOutsideHistory(() => {
            byId(f, "img1").setAttribute("src", "recomputed.png");
        });
        log.mockClear();

        await f.history.undo();

        expect(byId(f, "img1").getAttribute("src")).toBe("recomputed.png");
        const lines = log.mock.calls.map((call) => call[0]);
        expect(lines.length).toBe(1);
        expect(lines[0]).toMatch(
            /^Page history: undo of A found the page changed since, and dropped the undo steps: attribute src of .*img1.* is "recomputed\.png", not "b\.png"$/,
        );
    });

    // What Talking Book's sentence markup does to typing: the typing makes a text node and types
    // into it, then the markup, in the same step, replaces that node with a span holding new text.
    it("undoes a step that added a text node, typed into it and took it out again", async () => {
        const f = setUp();
        const p2 = byId(f, "p2");
        const doc = p2.ownerDocument;
        expect(p2.textContent, "test setup").toBe("Bonjour");
        const t = f.history.beginTransaction("Typing");
        const typed = doc.createTextNode(" d");
        p2.appendChild(typed);
        await letObserverDeliver();
        typed.data = " dd";
        await letObserverDeliver();
        const span = doc.createElement("span");
        span.className = "audio-sentence";
        span.textContent = " dd";
        p2.replaceChild(span, typed);
        await letObserverDeliver();
        t.end();
        expect(f.history.undoDescriptionL10nKey).toBe("Typing");

        await f.history.undo();

        expect(p2.textContent, "the undo should not be refused").toBe(
            "Bonjour",
        );
        expect(f.history.redoDescriptionL10nKey).toBe("Typing");
        await f.history.redo();
        expect(p2.lastChild).toBe(span);
        expect(p2.textContent).toBe("Bonjour dd");
    });

    it("logs which child list differs when an undo finds the page changed", async () => {
        const log = vi.fn<(line: string) => void>();
        const f = setUp(kStandardPage, { log });
        const p2 = byId(f, "p2");
        await f.history.doTransaction("A", () => {
            p2.appendChild(p2.ownerDocument.createElement("br"));
        });
        f.history.doOutsideHistory(() => {
            p2.appendChild(p2.ownerDocument.createElement("hr"));
        });
        log.mockClear();

        await f.history.undo();

        const lines = log.mock.calls.map((call) => call[0]);
        expect(lines.length).toBe(1);
        expect(lines[0]).toMatch(
            /: the children of .*p2.* are not the ones that the step left$/,
        );
    });
});

describe("page history: setDetailListener", () => {
    // What a detail said, read while the listener ran, as the contract asks: a record's node may
    // change later.
    type Seen =
        | Exclude<PageHistoryDetail, { kind: "changes" }>
        | {
              kind: "changes";
              context: PageHistoryChangeContext;
              records: {
                  type: string;
                  oldValue: string | null;
                  target: Node;
              }[];
              ignored: boolean[];
          };

    // Listens to f's history and returns what it hears.
    function listen(f: IFixture): Seen[] {
        const seen: Seen[] = [];
        f.history.setDetailListener((detail) => {
            if (detail.kind !== "changes") {
                seen.push(detail);
                return;
            }
            seen.push({
                kind: "changes",
                context: detail.context,
                records: detail.records.map((record) => ({
                    type: record.type,
                    oldValue: record.oldValue,
                    target: record.target,
                })),
                ignored: detail.ignored,
            });
        });
        return seen;
    }

    // The contexts of the batches of changes seen, in order.
    function contexts(seen: Seen[]): PageHistoryChangeContext[] {
        return seen.flatMap((detail) =>
            detail.kind === "changes" ? [detail.context] : [],
        );
    }

    function firstText(f: IFixture, id: string): Text {
        const text = byId(f, id).firstChild;
        expect(
            text?.nodeType,
            `test setup: #${id} should start with a text node`,
        ).toBe(Node.TEXT_NODE);
        return text as Text;
    }

    it("reports nothing once the listener is removed, and changes nothing the history does", async () => {
        const f = setUp();
        const seen = listen(f);
        f.history.setDetailListener(undefined);

        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const t = f.history.beginTransaction("B");
        firstText(f, "p1").data = "typed";
        t.cancel();
        f.history.doOutsideHistory(() =>
            byId(f, "img1").setAttribute("alt", "x"),
        );
        await f.history.undo();
        await f.history.redo();

        expect(seen).toEqual([]);
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        expect(firstText(f, "p1").data).toBe("Hello ");
        expect(f.history.undoDescriptionL10nKey).toBe("A");
    });

    it("reports a transaction opening, its changes, and its end", async () => {
        const f = setUp();
        const seen = listen(f);
        const text = firstText(f, "p1");

        await f.history.doTransaction("A", () => {
            text.data = "Hello there ";
        });

        expect(seen).toEqual([
            { kind: "opened", descriptionL10nKey: "A", depth: 1 },
            {
                kind: "changes",
                context: {
                    kind: "transaction",
                    descriptionL10nKey: "A",
                    depth: 1,
                },
                records: [
                    { type: "characterData", oldValue: "Hello ", target: text },
                ],
                ignored: [false],
            },
            { kind: "ended", descriptionL10nKey: "A", depth: 1 },
        ]);
    });

    it("names the innermost open transaction as the context of a change", () => {
        const f = setUp();
        const seen = listen(f);
        const outer = f.history.beginTransaction("Outer");
        const inner = f.history.beginTransaction("Inner");
        byId(f, "img1").setAttribute("src", "b.png");
        inner.end();
        outer.end();

        expect(contexts(seen)).toEqual([
            { kind: "transaction", descriptionL10nKey: "Inner", depth: 2 },
        ]);
        expect(seen.map((detail) => detail.kind)).toEqual([
            "opened",
            "opened",
            "changes",
            "ended",
            "ended",
        ]);
    });

    it("reports a transaction that closes with no net change", async () => {
        const f = setUp();
        const seen = listen(f);
        await f.history.doTransaction("A", () => {
            byId(f, "img1").setAttribute("src", "b.png");
            byId(f, "img1").setAttribute("src", "a.png");
        });
        expect(seen[seen.length - 1]).toEqual({
            kind: "closedWithNoStep",
            descriptionL10nKey: "A",
        });
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("reports the general transaction waiting for one begun inside it, and both ending", () => {
        const f = setUp();
        const seen = listen(f);
        const general = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const dialog = f.history.beginTransaction("Dialog");
        expect(general.end()).toBe(false);
        byId(f, "img1").setAttribute("src", "b.png");
        expect(dialog.end()).toBe(true);

        expect(seen.filter((detail) => detail.kind !== "changes")).toEqual([
            {
                kind: "opened",
                descriptionL10nKey: kGenericUndoDescriptionL10nKey,
                depth: 1,
            },
            { kind: "opened", descriptionL10nKey: "Dialog", depth: 2 },
            {
                kind: "endWaits",
                descriptionL10nKey: kGenericUndoDescriptionL10nKey,
                depth: 1,
                innerOpen: 1,
            },
            { kind: "ended", descriptionL10nKey: "Dialog", depth: 2 },
            {
                kind: "ended",
                descriptionL10nKey: kGenericUndoDescriptionL10nKey,
                depth: 1,
            },
        ]);
    });

    it("reports a cancelled transaction and the rollback that puts back typing made in it", () => {
        const f = setUp();
        const seen = listen(f);
        const text = firstText(f, "p1");
        const t = f.history.beginTransaction("Background");
        text.data = "Hello t";
        text.data = "Hello ty";
        t.cancel();

        expect(text.data, "the rollback should put back the text").toBe(
            "Hello ",
        );
        const afterOpen = seen.slice(1);
        expect(afterOpen[0]).toEqual({
            kind: "changes",
            context: {
                kind: "transaction",
                descriptionL10nKey: "Background",
                depth: 1,
            },
            records: [
                { type: "characterData", oldValue: "Hello ", target: text },
                { type: "characterData", oldValue: "Hello t", target: text },
            ],
            ignored: [false, false],
        });
        expect(afterOpen[1]).toEqual({
            kind: "cancelled",
            descriptionL10nKey: "Background",
            depth: 1,
            recordsReversed: 2,
        });
        const rollback = afterOpen[2];
        expect(rollback.kind).toBe("changes");
        if (rollback.kind !== "changes") return;
        expect(rollback.context).toEqual({
            kind: "rollback",
            descriptionL10nKey: "Background",
        });
        expect(
            rollback.records[0],
            "the rollback's first record should hold the typed text it took away",
        ).toEqual({
            type: "characterData",
            oldValue: "Hello ty",
            target: text,
        });
        expect(afterOpen.length).toBe(3);
    });

    it("reports a change inside doOutsideHistory", () => {
        const f = setUp();
        const seen = listen(f);
        f.history.doOutsideHistory(() =>
            byId(f, "img1").setAttribute("src", "b.png"),
        );
        expect(contexts(seen)).toEqual([{ kind: "outsideHistory" }]);
    });

    it("reports a change before the page is ready, and an unmarked one after it", async () => {
        const f = setUp(kStandardPage, { startsReady: false });
        const seen = listen(f);
        byId(f, "img1").setAttribute("src", "b.png");
        await letObserverDeliver();
        f.history.markPageReady();
        byId(f, "img1").setAttribute("src", "c.png");
        await letObserverDeliver();

        expect(contexts(seen)).toEqual([
            { kind: "beforeReady" },
            {
                kind: "unmarked",
                joins: "latestStep",
                descriptionL10nKey: undefined,
            },
        ]);
    });

    it("reports an unmarked change joining the latest step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const seen = listen(f);
        byId(f, "img1").setAttribute("alt", "x");
        await letObserverDeliver();

        expect(contexts(seen)).toEqual([
            { kind: "unmarked", joins: "latestStep", descriptionL10nKey: "A" },
        ]);
        expect(
            f.unmarked,
            "the warning should still be given",
        ).toHaveBeenCalled();
    });

    it("reports addToLatestTransaction joining the latest step, or the open transaction", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const seen = listen(f);
        f.history.addToLatestTransaction(() =>
            byId(f, "img1").setAttribute("alt", "x"),
        );
        const t = f.history.beginTransaction("B");
        byId(f, "box2").setAttribute("lang", "de");
        f.history.addToLatestTransaction(() =>
            byId(f, "img1").setAttribute("alt", "y"),
        );
        t.end();

        expect(contexts(seen)).toEqual([
            {
                kind: "addToLatestTransaction",
                joins: "latestStep",
                descriptionL10nKey: "A",
            },
            { kind: "transaction", descriptionL10nKey: "B", depth: 1 },
            {
                kind: "addToLatestTransaction",
                joins: "openTransaction",
                descriptionL10nKey: "B",
            },
        ]);
    });

    it("reports addToLatestTransaction inside a general transaction that has changed nothing as joining the latest step", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        const seen = listen(f);
        const general = f.history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        f.history.addToLatestTransaction(() =>
            byId(f, "img1").setAttribute("alt", "x"),
        );
        general.end();

        expect(contexts(seen)).toEqual([
            {
                kind: "addToLatestTransaction",
                joins: "latestStep",
                descriptionL10nKey: "A",
            },
        ]);
    });

    it("reports what undo and redo put back", async () => {
        const f = setUp();
        const img = byId(f, "img1");
        await stepSettingAttribute(f, "A", img, "src", "b.png");
        const seen = listen(f);
        await f.history.undo();
        await f.history.redo();

        const putBack = seen.filter(
            (detail) =>
                detail.kind === "changes" &&
                (detail.context.kind === "undo" ||
                    detail.context.kind === "redo"),
        );
        expect(
            putBack.map((detail) =>
                detail.kind === "changes"
                    ? [detail.context, detail.records.map((r) => r.oldValue)]
                    : [],
            ),
        ).toEqual([
            [{ kind: "undo", descriptionL10nKey: "A" }, ["b.png"]],
            [{ kind: "redo", descriptionL10nKey: "A" }, ["a.png"]],
        ]);
    });

    it("marks the records of a node that the filter ignores", async () => {
        const filter: PageHistoryFilter = {
            ...kKeepEverythingFilter,
            ignoreNode: (node) =>
                node instanceof Element && node.classList.contains("bloom-ui"),
        };
        const f = setUp(
            kStandardPage +
                '<div class="bloom-ui" id="ui" data-state="a"></div>',
            { filter },
        );
        const seen = listen(f);
        await f.history.doTransaction("A", () => {
            byId(f, "ui").setAttribute("data-state", "b");
            byId(f, "img1").setAttribute("src", "b.png");
        });

        const changes = seen.find((detail) => detail.kind === "changes");
        expect(changes?.kind === "changes" && changes.ignored).toEqual([
            true,
            false,
        ]);
    });
});
