// Tests of the internals of the page history that are easy to get wrong: working out a child
// list from records, putting back child lists across parents, rolling back a nested transaction,
// and telling a batch with a net change from one without.

import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createEditTimeMarkupFilter } from "./editTimeMarkup";
import { createPageHistory, kKeepEverythingFilter } from "./pageHistory";
import { childrenBeforeRecords, putBackChildLists } from "./pageHistoryDom";
import { PageHistory, PageHistoryOptions } from "./pageHistoryTypes";

let root: HTMLElement;
let warnings: string[];
let history: PageHistory | undefined;

beforeEach(() => {
    document.body.innerHTML = "";
    root = document.createElement("div");
    root.className = "bloom-page";
    document.body.appendChild(root);
    warnings = [];
});

afterEach(() => {
    history?.dispose();
    history = undefined;
});

/** A history on root that collects unmarked-change notices in warnings. */
function makeHistory(options?: Partial<PageHistoryOptions>): PageHistory {
    history = createPageHistory({
        root,
        onUnmarkedChange: (whatChanged) => warnings.push(whatChanged),
        ...options,
    });
    return history;
}

/** An element with an id, appended to parent. */
function addChild(parent: Node, id: string, tag = "div"): HTMLElement {
    const element = document.createElement(tag);
    element.id = id;
    parent.appendChild(element);
    return element;
}

/** The ids, or "#text:<data>" for text nodes, of a node's children. */
function childIds(parent: Node): string[] {
    return Array.from(parent.childNodes).map((child) =>
        child.nodeType === Node.ELEMENT_NODE
            ? (child as Element).id
            : "#text:" + child.textContent,
    );
}

describe("childrenBeforeRecords", () => {
    it("works back through appends, inserts, removals, replacements and innerHTML", () => {
        const parent = addChild(root, "p");
        const a = addChild(parent, "a");
        const b = addChild(parent, "b");
        parent.appendChild(document.createTextNode("t"));
        const original = Array.from(parent.childNodes);
        expect(childIds(parent)).toEqual(["a", "b", "#text:t"]);

        const observer = new MutationObserver(() => {});
        observer.observe(parent, { childList: true });
        addChild(parent, "c");
        parent.insertBefore(document.createElement("span"), b);
        parent.removeChild(a);
        parent.replaceChild(document.createElement("em"), b);
        parent.appendChild(a); // the same node, back at the end
        const records = observer.takeRecords();
        observer.disconnect();
        expect(childIds(parent)).not.toEqual(["a", "b", "#text:t"]);

        const before = childrenBeforeRecords(parent, records);

        expect(before).toHaveLength(original.length);
        before.forEach((node, i) => expect(node).toBe(original[i]));
    });

    it("works back through innerHTML that replaced everything", () => {
        const parent = addChild(root, "p");
        const a = addChild(parent, "a");
        const b = addChild(parent, "b");
        const observer = new MutationObserver(() => {});
        observer.observe(parent, { childList: true });
        parent.innerHTML = "<i>x</i><i>y</i>";
        const records = observer.takeRecords();
        observer.disconnect();

        const before = childrenBeforeRecords(parent, records);

        expect(before).toEqual([a, b]);
    });
});

describe("putBackChildLists", () => {
    it("moves a node between two parents, whichever parent comes first", () => {
        for (const order of ["source first", "destination first"]) {
            root.innerHTML = "";
            const source = addChild(root, "source");
            const destination = addChild(root, "destination");
            const x = addChild(source, "x");
            const y = addChild(source, "y");
            const z = addChild(destination, "z");
            const lists = new Map<Node, Node[]>();
            if (order === "source first") {
                lists.set(source, [y]);
                lists.set(destination, [x, z]);
            } else {
                lists.set(destination, [x, z]);
                lists.set(source, [y]);
            }

            putBackChildLists(lists, () => false);

            expect(childIds(source)).toEqual(["y"]);
            expect(childIds(destination)).toEqual(["x", "z"]);
        }
    });

    it("swaps nesting without inserting a node into its own descendant", () => {
        const a = addChild(root, "a");
        const b = addChild(a, "b");
        // Want b to contain a, with b directly in root.
        const lists = new Map<Node, Node[]>([
            [a, []],
            [b, [a]],
            [root, [b]],
        ]);

        putBackChildLists(lists, () => false);

        expect(childIds(root)).toEqual(["b"]);
        expect(childIds(b)).toEqual(["a"]);
        expect(a.childNodes).toHaveLength(0);
    });

    it("leaves ignored children where they are", () => {
        const parent = addChild(root, "p");
        const a = addChild(parent, "a");
        addChild(parent, "ignored");
        const b = addChild(parent, "b");
        const c = document.createElement("div");
        c.id = "c";

        putBackChildLists(
            new Map([[parent, [c, a, b]]]),
            (child) => (child as Element).id === "ignored",
        );

        expect(childIds(parent)).toEqual(["c", "a", "ignored", "b"]);
    });
});

describe("page history", () => {
    it("undoes and redoes moving a node between two parents", async () => {
        const source = addChild(root, "source");
        const destination = addChild(root, "destination");
        const x = addChild(source, "x");
        addChild(source, "y");
        addChild(destination, "z");
        const h = makeHistory();

        await h.doTransaction("Move", () => {
            destination.insertBefore(x, destination.firstChild);
        });
        expect(childIds(source)).toEqual(["y"]);
        expect(childIds(destination)).toEqual(["x", "z"]);

        await h.undo();
        expect(childIds(source)).toEqual(["x", "y"]);
        expect(childIds(destination)).toEqual(["z"]);
        expect(source.firstChild).toBe(x);

        await h.redo();
        expect(childIds(source)).toEqual(["y"]);
        expect(childIds(destination)).toEqual(["x", "z"]);
        expect(destination.firstChild).toBe(x);
    });

    it("rolls back only an inner transaction that throws, and makes one step", async () => {
        const box = addChild(root, "box");
        const text = document.createTextNode("old");
        box.appendChild(text);
        const h = makeHistory();

        await h.doTransaction("Outer", async () => {
            box.setAttribute("data-outer", "1");
            await expect(
                h.doTransaction("Inner", () => {
                    text.data = "new";
                    addChild(box, "added");
                    box.setAttribute("data-outer", "2");
                    throw new Error("inner failed");
                }),
            ).rejects.toThrow("inner failed");
            // The inner changes are gone; the outer's stays.
            expect(text.data).toBe("old");
            expect(childIds(box)).toEqual(["#text:old"]);
            expect(box.getAttribute("data-outer")).toBe("1");
            box.setAttribute("data-later", "yes");
        });

        expect(h.undoDescriptionL10nKey).toBe("Outer");
        await h.undo();
        expect(box.hasAttribute("data-outer")).toBe(false);
        expect(box.hasAttribute("data-later")).toBe(false);
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        expect(warnings).toEqual([]);
    });

    it("cancel() of a begun transaction puts back everything since it began", () => {
        const box = addChild(root, "box");
        box.setAttribute("style", "left: 1px");
        const h = makeHistory();

        const transaction = h.beginTransaction("Drag");
        box.setAttribute("style", "left: 5px");
        addChild(box, "handle");
        box.firstChild!.appendChild(document.createTextNode("t"));
        transaction.cancel();

        expect(box.getAttribute("style")).toBe("left: 1px");
        expect(box.childNodes).toHaveLength(0);
        expect(h.hasOpenTransaction).toBe(false);
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        transaction.end(); // late: does nothing
        expect(h.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes no step for a transaction with no net change", async () => {
        const box = addChild(root, "box");
        box.className = "a";
        const h = makeHistory();

        await h.doTransaction("Nothing", () => {
            box.classList.add("b");
            box.classList.remove("b");
            const extra = addChild(box, "extra");
            box.removeChild(extra);
        });

        expect(h.undoDescriptionL10nKey).toBeUndefined();
    });

    it("does not let a change with no net effect clear redo or warn", async () => {
        const box = addChild(root, "box");
        box.className = "a";
        const h = makeHistory();
        await h.doTransaction("Set", () => box.setAttribute("data-x", "1"));
        await h.undo();
        expect(h.redoDescriptionL10nKey).toBe("Set");

        // Queues a record even though the class is already there.
        box.classList.add("a");

        expect(h.redoDescriptionL10nKey).toBe("Set");
        expect(warnings).toEqual([]);
    });

    it("joins an unmarked change to the latest step, warns, and clears redo", async () => {
        const box = addChild(root, "box");
        const h = makeHistory();
        await h.doTransaction("First", () => box.setAttribute("data-a", "1"));
        await h.doTransaction("Second", () => box.setAttribute("data-b", "1"));
        await h.undo();
        expect(h.redoDescriptionL10nKey).toBe("Second");

        box.setAttribute("data-c", "1");

        expect(h.redoDescriptionL10nKey).toBeUndefined();
        expect(warnings).toHaveLength(1);
        expect(warnings[0]).toContain("data-c");
        await h.undo();
        expect(box.hasAttribute("data-a")).toBe(false);
        expect(box.hasAttribute("data-c")).toBe(false);
    });

    it("joins a change inside addToLatestTransaction without a warning", async () => {
        const box = addChild(root, "box");
        const h = makeHistory();
        await h.doTransaction("First", () => box.setAttribute("data-a", "1"));

        h.addToLatestTransaction((transaction) => {
            expect(transaction.descriptionL10nKey).toBe("First");
            box.setAttribute("data-refit", "1");
        });

        expect(warnings).toEqual([]);
        await h.undo();
        expect(box.hasAttribute("data-refit")).toBe(false);
    });

    it("refuses an undo when a value the step holds changed outside the history", async () => {
        const box = addChild(root, "box");
        const h = makeHistory();
        await h.doTransaction("First", () => box.setAttribute("data-a", "1"));
        await h.doTransaction("Second", () => box.setAttribute("data-b", "1"));
        await h.undo();

        h.doOutsideHistory(() => box.setAttribute("data-a", "recomputed"));
        await h.undo();

        expect(box.getAttribute("data-a")).toBe("recomputed");
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        expect(h.redoDescriptionL10nKey).toBe("Second");
    });

    it("refuses an undo when a removed subtree changed while detached", async () => {
        const box = addChild(root, "box");
        const removed = addChild(box, "removed");
        const h = makeHistory();
        await h.doTransaction("Delete", () => box.removeChild(removed));
        // Let the observer drop its interest in the detached node, as it does after delivery.
        await Promise.resolve();
        removed.setAttribute("data-changed", "1");
        expect(removed.getAttribute("data-changed")).toBe("1");

        await h.undo();

        expect(box.childNodes).toHaveLength(0);
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        expect(warnings).toEqual([]);
    });

    it("puts back a removed node that its transaction changed after an await", async () => {
        const box = addChild(root, "box");
        const removed = addChild(box, "removed");
        removed.setAttribute("data-a", "old");
        const h = makeHistory();

        await h.doTransaction("Delete", async () => {
            box.removeChild(removed);
            await new Promise((resolve) => setTimeout(resolve, 0));
            removed.setAttribute("data-a", "new");
        });
        expect(removed.getAttribute("data-a")).toBe("new");

        await h.undo();

        expect(box.firstChild).toBe(removed);
        expect(removed.getAttribute("data-a")).toBe("old");
        await h.redo();
        expect(box.childNodes).toHaveLength(0);
        expect(removed.getAttribute("data-a")).toBe("new");
    });

    it("leaves ignored nodes out of what it records and puts back", async () => {
        const box = addChild(root, "box");
        const h = makeHistory({
            filter: {
                ...kKeepEverythingFilter,
                ignoreNode: (node) =>
                    node.nodeType === Node.ELEMENT_NODE &&
                    (node as Element).classList.contains("ui"),
                keptAttributeValue: (_element, name, value) =>
                    name === "data-active" ? undefined : value,
                valueToPutBack: (_element, name, kept, current) =>
                    name === "data-active" ? current : kept,
            },
        });
        await h.doTransaction("Add", () => addChild(box, "content"));
        const ui = document.createElement("div");
        ui.className = "ui";
        box.appendChild(ui);
        box.setAttribute("data-active", "true");
        expect(warnings).toEqual([]);

        await h.undo();

        expect(childIds(box)).toEqual([""]);
        expect(box.firstChild).toBe(ui);
        expect(box.getAttribute("data-active")).toBe("true");
    });

    it("lets an onAfterUndoOrRedo listener's write clear redo", async () => {
        const box = addChild(root, "box");
        const h = makeHistory();
        await h.doTransaction("First", () => box.setAttribute("data-a", "1"));
        await h.doTransaction("Second", () => box.setAttribute("data-b", "1"));
        const seen: string[] = [];
        h.onAfterUndoOrRedo((restored) => {
            seen.push(restored.direction + ":" + restored.roots[0].id);
            box.setAttribute("data-refreshed", "yes");
        });

        await h.undo();

        expect(seen).toEqual(["undo:box"]);
        expect(h.redoDescriptionL10nKey).toBeUndefined();
        expect(h.undoDescriptionL10nKey).toBe("First");
    });

    it("throws in development when an outer transaction ends before an inner one", () => {
        const h = makeHistory();
        const outer = h.beginTransaction("Outer");
        const inner = h.beginTransaction("Inner");
        addChild(root, "x");

        expect(() => outer.end()).toThrow();

        expect(h.hasOpenTransaction).toBe(false);
        expect(h.undoDescriptionL10nKey).toBe("Outer");
        inner.end(); // late: does nothing
    });

    it("keeps at most maxUndoSteps steps", async () => {
        const box = addChild(root, "box");
        const h = makeHistory({ maxUndoSteps: 2 });
        for (const n of [1, 2, 3])
            await h.doTransaction("Step" + n, () =>
                box.setAttribute("data-n", String(n)),
            );

        await h.undo();
        await h.undo();
        await h.undo();

        expect(box.getAttribute("data-n")).toBe("1");
    });
});

describe("page history: the check of a detached subtree, with the edit-time markup filter", () => {
    /**
     * A canvas element holding a translation group with one editable, inside root, and a history
     * with the real filter. The editable starts with the markup CKEditor gives one that has focus.
     */
    function setUpCanvasElement(): {
        canvas: HTMLElement;
        element: HTMLElement;
        editable: HTMLElement;
        h: PageHistory;
    } {
        const canvas = addChild(root, "canvas");
        canvas.className = "bloom-canvas";
        canvas.innerHTML =
            '<div id="element" class="bloom-canvas-element" style="left: 10px; top: 20px">' +
            '<div class="bloom-translationGroup">' +
            '<div id="editable" class="bloom-editable cke_focus" lang="en" contenteditable="true">' +
            "<p>Hello</p></div></div></div>";
        const element = canvas.querySelector<HTMLElement>("#element")!;
        const editable = canvas.querySelector<HTMLElement>("#editable")!;
        const h = makeHistory({ filter: createEditTimeMarkupFilter() });
        return { canvas, element, editable, h };
    }

    it("undoes and redoes a delete after the removed element lost cke_focus", async () => {
        const { canvas, element, editable, h } = setUpCanvasElement();
        await h.doTransaction("Delete", () => canvas.removeChild(element));
        // After the step, as CKEditor's blur handler runs: the observer no longer sees the
        // detached element.
        await Promise.resolve();
        editable.classList.remove("cke_focus");
        expect(editable.className).toBe("bloom-editable");

        await h.undo();

        expect(canvas.firstChild).toBe(element);
        expect(editable.querySelector("p")!.textContent).toBe("Hello");
        expect(h.redoDescriptionL10nKey).toBe("Delete");
        await h.redo();
        expect(canvas.childNodes).toHaveLength(0);
        expect(h.undoDescriptionL10nKey).toBe("Delete");
    });

    it("undoes and redoes a step whose removed group gained CKEditor's markup later", async () => {
        const { element, editable, h } = setUpCanvasElement();
        editable.classList.remove("cke_focus");
        const group = element.firstElementChild as HTMLElement;
        const replacement = document.createElement("div");
        replacement.className = "bloom-translationGroup";
        await h.doTransaction("Duplicate", () =>
            element.replaceChild(replacement, group),
        );
        // CKEditor finishes attaching to the editable after it has left the page.
        await Promise.resolve();
        editable.classList.add(
            "cke_editable",
            "cke_editable_inline",
            "cke_contents_ltr",
        );
        editable.setAttribute("data-cke-expando", "1");
        const handle = document.createElement("div");
        handle.className = "bloom-ui";
        group.appendChild(handle);

        await h.undo();

        expect(element.firstChild).toBe(group);
        expect(replacement.isConnected).toBe(false);
        await h.redo();
        expect(element.firstChild).toBe(replacement);
        expect(group.isConnected).toBe(false);
        expect(h.undoDescriptionL10nKey).toBe("Duplicate");
    });

    it("still refuses the undo when the removed element's text changed", async () => {
        const { canvas, element, editable, h } = setUpCanvasElement();
        await h.doTransaction("Delete", () => canvas.removeChild(element));
        await Promise.resolve();
        editable.querySelector("p")!.firstChild!.textContent = "Changed";

        await h.undo();

        expect(canvas.childNodes).toHaveLength(0);
        expect(h.undoDescriptionL10nKey).toBeUndefined();
    });

    it("still refuses the undo when an attribute that a save keeps changed", async () => {
        const { canvas, element, editable, h } = setUpCanvasElement();
        await h.doTransaction("Delete", () => canvas.removeChild(element));
        await Promise.resolve();
        editable.setAttribute("lang", "fr");

        await h.undo();

        expect(canvas.childNodes).toHaveLength(0);
        expect(h.undoDescriptionL10nKey).toBeUndefined();
    });
});
