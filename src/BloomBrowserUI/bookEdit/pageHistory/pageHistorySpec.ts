// Black-box tests of the page history, written from the contract in pageHistoryTypes.ts and the
// rules of docs/undo/APPROACHES.md section 3.0.2 (sections 6.1 and 11.8.1 for the API), not from
// the implementation. Each test builds a small `.bloom-page`, makes changes, and checks the page's
// HTML, the identity of its nodes, and the history's state after undo and redo.
//
// A MutationObserver delivers its records in a microtask, so a change made outside any API call
// reaches the history only at the next microtask or at the next call to the history. Tests of
// changes made outside any transaction use letObserverDeliver() for that.
import { afterEach, describe, expect, it, vi, type Mock } from "vitest";
import { createPageHistory } from "./pageHistory";
import type {
    BegunTransaction,
    PageHistory,
    PageHistoryFilter,
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
        f.history.clearUndoAndRedo();
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
        f.history.clearUndoAndRedo();
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
        f.history.clearUndoAndRedo();
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
        f.history.clearUndoAndRedo();
        expect(f.history.hasOpenTransaction).toBe(false);
        gate.open();
        expect(await done).toBe(5);
        expect(f.history.undoDescriptionL10nKey).toBeUndefined();
        expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
    });
});

describe("page history: undo and redo while a transaction is open", () => {
    it("does nothing on undo() or redo() while a beginTransaction is open", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await stepSettingAttribute(f, "B", byId(f, "box2"), "lang", "es");
        await f.history.undo();
        expect(f.history.redoDescriptionL10nKey).toBe("B");

        const t = f.history.beginTransaction("Drag");
        byId(f, "box1").setAttribute("lang", "de");
        const openHtml = f.page.outerHTML;
        await f.history.undo();
        expect(f.page.outerHTML, "undo while open").toBe(openHtml);
        await f.history.redo();
        expect(f.page.outerHTML, "redo while open").toBe(openHtml);
        expect(f.history.hasOpenTransaction).toBe(true);
        t.end();

        expect(f.history.undoDescriptionL10nKey).toBe("Drag");
        await f.history.undo();
        expect(byId(f, "box1").getAttribute("lang")).toBe("en");
        expect(
            byId(f, "img1").getAttribute("src"),
            "step A should be untouched",
        ).toBe("b.png");
        expect(f.history.undoDescriptionL10nKey).toBe("A");
    });

    it("does nothing on undo() called from inside an async doTransaction", async () => {
        const f = setUp();
        await stepSettingAttribute(f, "A", byId(f, "img1"), "src", "b.png");
        await f.history.doTransaction("Async", async () => {
            byId(f, "box1").setAttribute("lang", "de");
            await f.history.undo();
            expect(byId(f, "box1").getAttribute("lang")).toBe("de");
            expect(byId(f, "img1").getAttribute("src")).toBe("b.png");
        });
        expect(f.history.undoDescriptionL10nKey).toBe("Async");
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
        ignoreNode: (node) =>
            node instanceof Element && node.classList.contains("bloom-ui"),
        ignoreAttribute: (_element, attributeName) =>
            attributeName === "data-hover",
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

describe("page history: dispose", () => {
    it("makes every later call throw", async () => {
        const f = setUp();
        disposeHistory(f);
        expect(() => f.history.beginTransaction("X")).toThrow();
        expect(() => f.history.addToLatestTransaction(() => 1)).toThrow();
        expect(() => f.history.doOutsideHistory(() => 1)).toThrow();
        expect(() => f.history.clearUndoAndRedo()).toThrow();
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
