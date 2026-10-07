import { beforeEach, describe, expect, test } from "vitest";
import {
    deleteCanvasElementUndoably,
    ICanvasElementDeletionHost,
} from "./CanvasElementDeleteUndo";
import { UndoStack } from "../../undo/UndoStack";

// What a canvas element's data-bubble holds, as far as these tests care.
interface ITestSpec {
    level: number;
    order?: number;
    tails?: { tipX: number }[];
    style?: string;
}

function readSpec(element: HTMLElement): ITestSpec {
    return JSON.parse(
        element.getAttribute("data-bubble")!.replace(/`/g, '"'),
    ) as ITestSpec;
}

function writeSpec(element: HTMLElement, spec: ITestSpec): void {
    element.setAttribute(
        "data-bubble",
        JSON.stringify(spec).replace(/"/g, "`"),
    );
}

let canvas: HTMLElement;

// Make a canvas holding one canvas element per spec, bottom of the stack first, each with the id
// given, so failures can name them.
function setUpCanvas(specs: [string, ITestSpec][]): void {
    document.body.innerHTML = `<div class="bloom-page"><div class="bloom-canvas"></div></div>`;
    canvas = document.querySelector(".bloom-canvas") as HTMLElement;
    specs.forEach(([id, spec]) => addElement(id, spec));
}

function addElement(id: string, spec: ITestSpec): HTMLElement {
    const element = document.createElement("div");
    element.className = "bloom-canvas-element";
    element.id = id;
    writeSpec(element, { tails: [], style: "speech", ...spec });
    canvas.appendChild(element);
    return element;
}

function el(id: string): HTMLElement {
    const element = document.getElementById(id);
    if (!element) {
        throw new Error(`Test setup: no element ${id} on the page`);
    }
    return element;
}

// The ids of the canvas elements on the canvas, bottom of the stack first.
function stackingOrder(): string[] {
    return Array.from(canvas.children)
        .filter((child) => child.classList.contains("bloom-canvas-element"))
        .map((child) => child.id);
}

// A stand-in for CanvasElementManager. Its removal does what the real one does to the data this
// module looks at: Comical.deleteBubbleFromFamily (shift the family's orders down; when the head
// goes, its spec moves to the next member), then removeDetachedTargets.
const host: ICanvasElementDeletionHost & { restoredCount: number } = {
    restoredCount: 0,
    removeCanvasElement: (element) => {
        const goner = readSpec(element);
        if (goner.order) {
            Array.from(canvas.children)
                .filter((other) => other !== element)
                .map((other) => other as HTMLElement)
                .filter((other) => other.hasAttribute("data-bubble"))
                .filter((other) => {
                    const spec = readSpec(other);
                    return spec.level === goner.level && !!spec.order;
                })
                .sort((a, b) => readSpec(a).order! - readSpec(b).order!)
                .forEach((relative, index) => {
                    let spec = readSpec(relative);
                    if (index === 0 && goner.order === 1) {
                        spec = { ...goner };
                    }
                    if (spec.order! > goner.order!) {
                        spec.order!--;
                    }
                    writeSpec(relative, spec);
                });
        }
        element.remove();
        const draggableId = element.getAttribute("data-draggable-id");
        if (draggableId) {
            canvas.querySelector(`[data-target-of="${draggableId}"]`)?.remove();
        }
    },
    finishRestoringCanvasElement: () => {
        host.restoredCount++;
    },
};

let stack: UndoStack;

function deleteElement(id: string): void {
    stack.push(deleteCanvasElementUndoably(el(id), host));
}

function undo(): void {
    stack.undo();
}

beforeEach(() => {
    stack = new UndoStack();
    host.restoredCount = 0;
});

describe("undoing a canvas element deletion: the stacking order", () => {
    test("puts the element back between its neighbours, as the same object", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["d", { level: 3 }],
        ]);
        const a = el("a");
        deleteElement("a");
        expect(stackingOrder()).toEqual(["c", "d"]);

        undo();

        expect(stackingOrder()).toEqual(["c", "a", "d"]);
        expect(document.getElementById("a")).toBe(a);
        expect(host.restoredCount).toBe(1);
    });

    test("undoes two adjacent deletes in turn", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["b", { level: 3 }],
            ["d", { level: 4 }],
        ]);
        deleteElement("a");
        deleteElement("b");
        expect(stackingOrder()).toEqual(["c", "d"]);

        undo();
        expect(stackingOrder()).toEqual(["c", "b", "d"]);
        undo();

        expect(stackingOrder()).toEqual(["c", "a", "b", "d"]);
        const levels = stackingOrder().map((id) => readSpec(el(id)).level);
        expect(levels).toEqual([...levels].sort((x, y) => x - y));
        expect(new Set(levels).size).toBe(4);
    });

    test("goes above its lower neighbour when its upper one is gone", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["d", { level: 3 }],
        ]);
        deleteElement("a");
        el("d").remove(); // something we don't record took d away
        addElement("e", { level: 4 });

        undo();

        expect(stackingOrder()).toEqual(["c", "a", "e"]);
    });

    test("goes on top when both neighbours are gone", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["d", { level: 3 }],
            ["e", { level: 4 }],
        ]);
        deleteElement("a");
        el("c").remove();
        el("d").remove();

        undo();

        expect(stackingOrder()).toEqual(["e", "a"]);
        expect(readSpec(el("a")).level).toBeGreaterThan(
            readSpec(el("e")).level,
        );
    });

    test("an element created in between does not disturb the restore", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["d", { level: 3 }],
        ]);
        deleteElement("a");
        addElement("new", { level: 4 });

        undo();

        expect(stackingOrder()).toEqual(["c", "a", "d", "new"]);
    });

    test("takes a level from where it lands, moving the ones above it up", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["d", { level: 3 }],
        ]);
        deleteElement("a");
        // Something that renumbers the levels in between, as putBubbleBefore does when an element
        // is added at the bottom.
        writeSpec(el("c"), { ...readSpec(el("c")), level: 5 });
        writeSpec(el("d"), { ...readSpec(el("d")), level: 6 });

        undo();

        expect(readSpec(el("c")).level).toBe(5);
        expect(readSpec(el("a")).level).toBe(6);
        expect(readSpec(el("d")).level).toBe(7);
    });
});

describe("undoing a canvas element deletion: comic families", () => {
    // A family of three at level 2, over a picture at level 1.
    function setUpFamily(): void {
        setUpCanvas([
            ["pic", { level: 1, style: "none" }],
            ["head", { level: 2, order: 1, tails: [{ tipX: 10 }] }],
            ["mid", { level: 2, order: 2, tails: [{ tipX: 20 }] }],
            ["last", { level: 2, order: 3, tails: [{ tipX: 30 }] }],
        ]);
    }

    test("a member rejoins its family at its old place", () => {
        setUpFamily();
        deleteElement("mid");
        expect(readSpec(el("last")).order).toBe(2);

        undo();

        expect(readSpec(el("mid"))).toMatchObject({ level: 2, order: 2 });
        expect(readSpec(el("last")).order).toBe(3);
        expect(readSpec(el("head")).order).toBe(1);
    });

    test("the head takes back its place, and the member that took over gets its own tails back", () => {
        setUpFamily();
        deleteElement("head");
        // Sanity check: the delete made mid the head, with head's tails.
        expect(readSpec(el("mid"))).toMatchObject({
            order: 1,
            tails: [{ tipX: 10 }],
        });

        undo();

        expect(readSpec(el("head"))).toMatchObject({
            level: 2,
            order: 1,
            tails: [{ tipX: 10 }],
        });
        expect(readSpec(el("mid"))).toMatchObject({
            order: 2,
            tails: [{ tipX: 20 }],
        });
        expect(readSpec(el("last")).order).toBe(3);
    });

    test("the head takes the family's tail as it is now, keeping an edit made since", () => {
        setUpFamily();
        deleteElement("head");
        writeSpec(el("mid"), { ...readSpec(el("mid")), tails: [{ tipX: 99 }] });

        undo();

        expect(readSpec(el("head")).tails).toEqual([{ tipX: 99 }]);
        expect(readSpec(el("mid")).tails).toEqual([{ tipX: 20 }]);
    });

    test("the head takes the family-wide settings as they are now", () => {
        setUpFamily();
        deleteElement("head");
        writeSpec(el("mid"), { ...readSpec(el("mid")), style: "shout" });

        undo();

        expect(readSpec(el("head")).style).toBe("shout");
    });

    test("deleting a member and then the head, then undoing both, gives the original family", () => {
        setUpFamily();
        const before = ["head", "mid", "last"].map((id) => readSpec(el(id)));
        deleteElement("mid");
        deleteElement("head");

        undo();
        undo();

        expect(["head", "mid", "last"].map((id) => readSpec(el(id)))).toEqual(
            before,
        );
    });

    test("follows the family to a new level", () => {
        setUpFamily();
        deleteElement("mid");
        ["head", "last"].forEach((id) =>
            writeSpec(el(id), { ...readSpec(el(id)), level: 7 }),
        );

        undo();

        expect(readSpec(el("mid"))).toMatchObject({ level: 7, order: 2 });
    });

    test("comes back on its own when none of its family is left", () => {
        setUpCanvas([
            ["pic", { level: 1 }],
            ["head", { level: 2, order: 1 }],
            ["child", { level: 2, order: 2 }],
            ["other", { level: 3 }],
        ]);
        deleteElement("child");
        el("head").remove();

        undo();

        const spec = readSpec(el("child"));
        expect(spec.order).toBeUndefined();
        expect(stackingOrder()).toEqual(["pic", "child", "other"]);
        expect(spec.level).toBe(2);
        expect(readSpec(el("other")).level).toBe(4);
    });
});

describe("undoing a canvas element deletion: the rest", () => {
    test("ignores a canvas element that has no bubble data", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["d", { level: 3 }],
        ]);
        el("d").removeAttribute("data-bubble");
        deleteElement("a");

        undo();

        expect(stackingOrder()).toEqual(["c", "a", "d"]);
        expect(readSpec(el("a")).level).toBe(2);
        expect(el("d").hasAttribute("data-bubble")).toBe(false);
    });

    test("puts back a draggable's target", () => {
        setUpCanvas([["drag", { level: 1 }]]);
        el("drag").setAttribute("data-draggable-id", "d1");
        const target = document.createElement("div");
        target.setAttribute("data-target-of", "d1");
        canvas.appendChild(target);
        deleteElement("drag");
        expect(target.isConnected).toBe(false);

        undo();

        expect(target.parentElement).toBe(canvas);
    });

    test("redo deletes it again, and the next undo still restores it", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
            ["d", { level: 3 }],
        ]);
        deleteElement("a");
        undo();
        expect(stackingOrder()).toEqual(["c", "a", "d"]);

        stack.redo();
        expect(stackingOrder()).toEqual(["c", "d"]);
        expect(stack.getEntryCount()).toBe(1);

        undo();
        expect(stackingOrder()).toEqual(["c", "a", "d"]);
    });

    test("refuses, discarding the stack, when the canvas is gone", () => {
        setUpCanvas([
            ["c", { level: 1 }],
            ["a", { level: 2 }],
        ]);
        deleteElement("a");
        canvas.remove();

        expect(() => undo()).toThrow(/its canvas is no longer on the page/);
        expect(stack.getEntryCount()).toBe(0);
        expect(host.restoredCount).toBe(0);
    });
});
