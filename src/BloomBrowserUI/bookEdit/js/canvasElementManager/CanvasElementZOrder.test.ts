import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

// Tests for the Layer commands (BL-15992): moving a canvas element forward or backward in the
// stacking order. Two things must stay in step: the order of the elements in the DOM, which is
// what actually paints on top of what, and the `level` in each element's ComicalJS bubble spec,
// which is what Comical draws bubbles by and uses to decide which bubble a click hits.

// Enough of comicaljs for the module under test: specs live in data-bubble as JSON with
// backticks for quotes, exactly as the real Bubble reads and writes them. Comical.update is
// what tells the real library to redraw from the new levels; here we only check it is called.
vi.mock("comicaljs", () => {
    interface ISpec {
        level?: number;
        order?: number;
    }
    class Bubble {
        private spec: ISpec;
        constructor(private element: HTMLElement) {
            this.spec = Bubble.getBubbleSpec(element);
        }
        static getBubbleSpec(element: HTMLElement): ISpec {
            const raw = element.getAttribute("data-bubble");
            return raw ? JSON.parse(raw.replace(/`/g, '"')) : {};
        }
        getBubbleSpec(): ISpec {
            return this.spec;
        }
        persistBubbleSpec(): void {
            this.element.setAttribute(
                "data-bubble",
                JSON.stringify(this.spec).replace(/"/g, "`"),
            );
        }
    }
    return { Bubble, Comical: { update: vi.fn() } };
});

import { Comical } from "comicaljs";
import {
    canBringCanvasElementForward,
    canSendCanvasElementBackward,
    getMovableCanvasElements,
    getZOrderUnits,
    moveCanvasElementInZOrder,
    syncBubbleLevelsToDomOrder,
    ZOrderMove,
} from "./CanvasElementZOrder";

interface IElementSpec {
    id: string;
    level?: number;
    order?: number;
    background?: boolean;
}

// Build a bloom-canvas whose canvas elements are the given ones, in the given (bottom-first)
// order. As in a real page, Comical's own <canvas> comes first and the control frame last.
function makeBloomCanvas(elements: IElementSpec[]): HTMLElement {
    const bloomCanvas = document.createElement("div");
    bloomCanvas.className = "bloom-canvas";
    bloomCanvas.appendChild(document.createElement("canvas"));
    elements.forEach((spec) => {
        const canvasElement = document.createElement("div");
        canvasElement.id = spec.id;
        canvasElement.className = "bloom-canvas-element";
        if (spec.background) {
            canvasElement.classList.add("bloom-backgroundImage");
        }
        if (spec.level !== undefined) {
            const bubbleSpec: Record<string, unknown> = {
                version: "1.0",
                style: "none",
                tails: [],
                level: spec.level,
            };
            if (spec.order !== undefined) {
                bubbleSpec.order = spec.order;
            }
            canvasElement.setAttribute(
                "data-bubble",
                JSON.stringify(bubbleSpec).replace(/"/g, "`"),
            );
        }
        bloomCanvas.appendChild(canvasElement);
    });
    const controlFrame = document.createElement("div");
    controlFrame.id = "canvas-element-control-frame";
    bloomCanvas.appendChild(controlFrame);
    document.body.appendChild(bloomCanvas);
    return bloomCanvas;
}

const byId = (id: string): HTMLElement => {
    const element = document.getElementById(id);
    if (!element) {
        throw new Error(`No element with id ${id}`);
    }
    return element;
};

// The ids of all the bloom-canvas's children, in DOM order, so a test can check that the
// non-canvas-element children stayed where they were too.
const childIds = (bloomCanvas: HTMLElement): string[] =>
    Array.from(bloomCanvas.children).map(
        (child) => child.id || child.tagName.toLowerCase(),
    );

const levelOf = (id: string): number | undefined => {
    const raw = byId(id).getAttribute("data-bubble");
    return raw ? JSON.parse(raw.replace(/`/g, '"')).level : undefined;
};

const orderOf = (id: string): number | undefined => {
    const raw = byId(id).getAttribute("data-bubble");
    return raw ? JSON.parse(raw.replace(/`/g, '"')).order : undefined;
};

// A background image at level 1 and three ordinary elements above it, with levels that
// already match their DOM order.
const kBackgroundAndThree: IElementSpec[] = [
    { id: "bg", level: 1, background: true },
    { id: "a", level: 2 },
    { id: "b", level: 3 },
    { id: "c", level: 4 },
];

describe("CanvasElementZOrder", () => {
    beforeEach(() => {
        vi.mocked(Comical.update).mockClear();
    });

    afterEach(() => {
        document.body.innerHTML = "";
    });

    test("setup: the test canvas has the expected order and levels", () => {
        const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);
        expect(childIds(bloomCanvas)).toEqual([
            "canvas",
            "bg",
            "a",
            "b",
            "c",
            "canvas-element-control-frame",
        ]);
        expect(getMovableCanvasElements(bloomCanvas).map((e) => e.id)).toEqual([
            "a",
            "b",
            "c",
        ]);
        expect(levelOf("bg")).toBe(1);
        expect(levelOf("c")).toBe(4);
    });

    describe("the four moves", () => {
        test("forward swaps the element with the one above it", () => {
            const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);

            expect(moveCanvasElementInZOrder(byId("a"), "forward")).toBe(true);

            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "b",
                "a",
                "c",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("bg")).toBe(1);
            expect(levelOf("b")).toBe(2);
            expect(levelOf("a")).toBe(3);
            expect(levelOf("c")).toBe(4);
            expect(Comical.update).toHaveBeenCalledTimes(1);
            expect(Comical.update).toHaveBeenCalledWith(bloomCanvas);
        });

        test("backward swaps the element with the one below it", () => {
            const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);

            expect(moveCanvasElementInZOrder(byId("c"), "backward")).toBe(true);

            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "a",
                "c",
                "b",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("a")).toBe(2);
            expect(levelOf("c")).toBe(3);
            expect(levelOf("b")).toBe(4);
        });

        test("front puts the element above everything, but still before the control frame", () => {
            const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);

            expect(moveCanvasElementInZOrder(byId("a"), "front")).toBe(true);

            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "b",
                "c",
                "a",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("b")).toBe(2);
            expect(levelOf("c")).toBe(3);
            expect(levelOf("a")).toBe(4);
        });

        test("back puts the element just above the background image, never behind it", () => {
            const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);

            expect(moveCanvasElementInZOrder(byId("c"), "back")).toBe(true);

            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "c",
                "a",
                "b",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("bg")).toBe(1);
            expect(levelOf("c")).toBe(2);
            expect(levelOf("a")).toBe(3);
            expect(levelOf("b")).toBe(4);
        });
    });

    describe("moves that are not possible change nothing", () => {
        const expectUnchanged = (bloomCanvas: HTMLElement) => {
            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "a",
                "b",
                "c",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("a")).toBe(2);
            expect(levelOf("b")).toBe(3);
            expect(levelOf("c")).toBe(4);
            expect(Comical.update).not.toHaveBeenCalled();
        };

        test("the top element cannot go forward or to the front", () => {
            const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);
            expect(moveCanvasElementInZOrder(byId("c"), "forward")).toBe(false);
            expect(moveCanvasElementInZOrder(byId("c"), "front")).toBe(false);
            expectUnchanged(bloomCanvas);
        });

        test("the bottom movable element cannot go backward or to the back", () => {
            const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);
            expect(moveCanvasElementInZOrder(byId("a"), "backward")).toBe(
                false,
            );
            expect(moveCanvasElementInZOrder(byId("a"), "back")).toBe(false);
            expectUnchanged(bloomCanvas);
        });

        test("the background image cannot be moved at all", () => {
            const bloomCanvas = makeBloomCanvas(kBackgroundAndThree);
            const moves: ZOrderMove[] = [
                "forward",
                "backward",
                "front",
                "back",
            ];
            moves.forEach((move) => {
                expect(moveCanvasElementInZOrder(byId("bg"), move)).toBe(false);
            });
            expectUnchanged(bloomCanvas);
        });
    });

    describe("canBringCanvasElementForward / canSendCanvasElementBackward", () => {
        test("report which way each element can go", () => {
            makeBloomCanvas(kBackgroundAndThree);
            expect(canBringCanvasElementForward(byId("a"))).toBe(true);
            expect(canSendCanvasElementBackward(byId("a"))).toBe(false);

            expect(canBringCanvasElementForward(byId("b"))).toBe(true);
            expect(canSendCanvasElementBackward(byId("b"))).toBe(true);

            expect(canBringCanvasElementForward(byId("c"))).toBe(false);
            expect(canSendCanvasElementBackward(byId("c"))).toBe(true);
        });

        test("are both false for the background image", () => {
            makeBloomCanvas(kBackgroundAndThree);
            expect(canBringCanvasElementForward(byId("bg"))).toBe(false);
            expect(canSendCanvasElementBackward(byId("bg"))).toBe(false);
        });

        test("are both false when there is only one movable element", () => {
            makeBloomCanvas([
                { id: "bg", level: 1, background: true },
                { id: "only", level: 2 },
            ]);
            expect(canBringCanvasElementForward(byId("only"))).toBe(false);
            expect(canSendCanvasElementBackward(byId("only"))).toBe(false);
        });

        test("are both false for an element that is not in a bloom-canvas", () => {
            const loose = document.createElement("div");
            loose.className = "bloom-canvas-element";
            expect(canBringCanvasElementForward(loose)).toBe(false);
            expect(canSendCanvasElementBackward(loose)).toBe(false);
        });
    });

    describe("bubble families (a parent bubble and its children share a level)", () => {
        // Parent p and child p2 are a family at level 3; another element x was added after the
        // parent but before the child, so the family is not contiguous in the DOM.
        const kFamilyCanvas: IElementSpec[] = [
            { id: "bg", level: 1, background: true },
            { id: "a", level: 2 },
            { id: "p", level: 3, order: 1 },
            { id: "x", level: 4 },
            { id: "p2", level: 3, order: 2 },
            { id: "top", level: 5 },
        ];

        test("setup: getZOrderUnits groups the family and keeps DOM order otherwise", () => {
            const bloomCanvas = makeBloomCanvas(kFamilyCanvas);
            const units = getZOrderUnits(
                getMovableCanvasElements(bloomCanvas),
            ).map((unit) => unit.map((e) => e.id));
            expect(units).toEqual([["a"], ["p", "p2"], ["x"], ["top"]]);
        });

        test("a family moves as a unit and keeps sharing one level", () => {
            const bloomCanvas = makeBloomCanvas(kFamilyCanvas);

            expect(moveCanvasElementInZOrder(byId("p2"), "forward")).toBe(true);

            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "a",
                "x",
                "p",
                "p2",
                "top",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("a")).toBe(2);
            expect(levelOf("x")).toBe(3);
            expect(levelOf("p")).toBe(4);
            expect(levelOf("p2")).toBe(4);
            expect(levelOf("top")).toBe(5);
            // The family relationship itself is untouched.
            expect(orderOf("p")).toBe(1);
            expect(orderOf("p2")).toBe(2);
        });

        test("an element sent backward past a family lands below all of it", () => {
            const bloomCanvas = makeBloomCanvas(kFamilyCanvas);

            expect(moveCanvasElementInZOrder(byId("x"), "backward")).toBe(true);

            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "a",
                "x",
                "p",
                "p2",
                "top",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("x")).toBe(3);
            expect(levelOf("p")).toBe(4);
            expect(levelOf("p2")).toBe(4);
        });

        test("an element brought forward past a straddling family moves exactly one step: the family is gathered at its parent, and the element lands just above it", () => {
            const bloomCanvas = makeBloomCanvas(kFamilyCanvas);

            expect(moveCanvasElementInZOrder(byId("a"), "forward")).toBe(true);

            // a passed the family but not x, which the family used to straddle.
            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "p",
                "p2",
                "a",
                "x",
                "top",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("p")).toBe(2);
            expect(levelOf("p2")).toBe(2);
            expect(levelOf("a")).toBe(3);
            expect(levelOf("x")).toBe(4);
            expect(levelOf("top")).toBe(5);
        });

        test("the top family cannot go forward, even from its first member", () => {
            makeBloomCanvas([
                { id: "bg", level: 1, background: true },
                { id: "a", level: 2 },
                { id: "p", level: 3, order: 1 },
                { id: "p2", level: 3, order: 2 },
            ]);
            expect(canBringCanvasElementForward(byId("p"))).toBe(false);
            expect(canBringCanvasElementForward(byId("p2"))).toBe(false);
            expect(canSendCanvasElementBackward(byId("p"))).toBe(true);
            expect(moveCanvasElementInZOrder(byId("p"), "forward")).toBe(false);
        });
    });

    describe("draggable game pieces stay above fixed ones", () => {
        // Two fixed elements below two draggables, as adjustCanvasElementOrdering leaves a
        // game page.
        const makeGameCanvas = (): HTMLElement => {
            const bloomCanvas = makeBloomCanvas([
                { id: "bg", level: 1, background: true },
                { id: "fixed1", level: 2 },
                { id: "fixed2", level: 3 },
                { id: "drag1", level: 4 },
                { id: "drag2", level: 5 },
            ]);
            byId("drag1").setAttribute("data-draggable-id", "d1");
            byId("drag2").setAttribute("data-draggable-id", "d2");
            return bloomCanvas;
        };

        test("the top fixed element cannot be brought in front of a draggable", () => {
            const bloomCanvas = makeGameCanvas();
            expect(canBringCanvasElementForward(byId("fixed2"))).toBe(false);
            expect(moveCanvasElementInZOrder(byId("fixed2"), "front")).toBe(
                false,
            );
            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "fixed1",
                "fixed2",
                "drag1",
                "drag2",
                "canvas-element-control-frame",
            ]);
        });

        test("the bottom draggable cannot be sent behind a fixed element", () => {
            makeGameCanvas();
            expect(canSendCanvasElementBackward(byId("drag1"))).toBe(false);
            expect(moveCanvasElementInZOrder(byId("drag1"), "back")).toBe(
                false,
            );
        });

        test("elements still move among their own kind", () => {
            const bloomCanvas = makeGameCanvas();
            expect(canSendCanvasElementBackward(byId("drag2"))).toBe(true);
            expect(moveCanvasElementInZOrder(byId("drag2"), "back")).toBe(true);
            expect(moveCanvasElementInZOrder(byId("fixed1"), "front")).toBe(
                true,
            );
            expect(childIds(bloomCanvas)).toEqual([
                "canvas",
                "bg",
                "fixed2",
                "fixed1",
                "drag2",
                "drag1",
                "canvas-element-control-frame",
            ]);
            expect(levelOf("fixed2")).toBe(2);
            expect(levelOf("fixed1")).toBe(3);
            expect(levelOf("drag2")).toBe(4);
            expect(levelOf("drag1")).toBe(5);
        });
    });

    describe("syncBubbleLevelsToDomOrder", () => {
        test("renumbers levels to match the DOM, background first at level 1", () => {
            const bloomCanvas = makeBloomCanvas([
                { id: "bg", level: 7, background: true },
                { id: "a", level: 9 },
                { id: "b", level: 3 },
                { id: "c", level: 3 },
            ]);
            // Sanity: the levels really do start out disagreeing with the DOM order.
            expect(levelOf("a")).toBeGreaterThan(levelOf("b") as number);

            syncBubbleLevelsToDomOrder(bloomCanvas);

            expect(levelOf("bg")).toBe(1);
            expect(levelOf("a")).toBe(2);
            // b and c shared a level but have no `order`, so they are not a family and get
            // distinct levels.
            expect(levelOf("b")).toBe(3);
            expect(levelOf("c")).toBe(4);
            expect(Comical.update).toHaveBeenCalledWith(bloomCanvas);
        });

        test("gives an element with no bubble spec a level too", () => {
            const bloomCanvas = makeBloomCanvas([
                { id: "bg", level: 1, background: true },
                { id: "a", level: 2 },
                { id: "noSpec" },
            ]);
            expect(byId("noSpec").hasAttribute("data-bubble")).toBe(false);

            syncBubbleLevelsToDomOrder(bloomCanvas);

            expect(levelOf("noSpec")).toBe(3);
        });

        test("starts at level 1 when there is no background image", () => {
            const bloomCanvas = makeBloomCanvas([
                { id: "a", level: 5 },
                { id: "b", level: 6 },
            ]);

            syncBubbleLevelsToDomOrder(bloomCanvas);

            expect(levelOf("a")).toBe(1);
            expect(levelOf("b")).toBe(2);
        });
    });
});
