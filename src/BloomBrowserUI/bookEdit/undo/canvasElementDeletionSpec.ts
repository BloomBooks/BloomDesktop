// Tests for the page-frame half of undoing a canvas element deletion (BL-6681 Stage 2b).
//
// jsdom has no Comical and no real canvas element manager, so both are stood in for; what these
// tests pin is the record's content and the DOM surgery restore performs — the index, the family's
// bubble specs, the drag-activity target — and that redo goes through the normal deletion path
// without recording again.

import { describe, it, expect, beforeEach, vi } from "vitest";

// vi.mock factories are hoisted above everything else in the file, so the state they close over
// has to be hoisted with them.
const { comical, FakeBubble, manager, workspace, frames } = vi.hoisted(() => {
    class FakeBubble {
        constructor(public content: HTMLElement) {}
    }
    return {
        comical: { update: vi.fn() },
        FakeBubble,
        manager: {
            refreshCanvasElementEditing: vi.fn(),
            deleteCanvasElement: vi.fn(),
        },
        workspace: { recordCanvasElementDeletion: vi.fn() },
        frames: { workspaceAvailable: true },
    };
});
vi.mock("comicaljs", () => ({ Comical: comical, Bubble: FakeBubble }));
vi.mock("../js/workspaceFrames", () => ({
    getEditablePageBundleExports: () => ({
        getTheOneCanvasElementManager: () => manager,
    }),
    tryGetWorkspaceBundleExports: () =>
        frames.workspaceAvailable ? workspace : null,
}));

import {
    canvasElementsOf,
    captureCanvasElementDeletion,
    ICanvasElementDeletionRecord,
    pathWithin,
    recordCanvasElementDeletionForUndo,
    redeleteCanvasElement,
    resolvePathWithin,
    restoreDeletedCanvasElement,
} from "./canvasElementDeletion";

const kPageHtml = `
<div class="bloom-page" id="page-1">
  <div class="marginBox">
    <div class="bloom-canvas" id="canvas-a">
      <img class="bloom-imageContainer" src="a.png">
      <div class="bloom-canvas-element" data-bubble='{"order":1,"style":"speech"}' data-draggable-id="drag-7"><div class="bloom-translationGroup"><div class="bloom-editable">first</div></div></div>
      <div class="bloom-canvas-element" data-bubble='{"order":2,"style":"speech"}'><div class="bloom-translationGroup"><div class="bloom-editable">second</div></div></div>
      <div class="bloom-canvas-element" data-bubble='{"order":3,"style":"speech"}'><div class="bloom-translationGroup"><div class="bloom-editable">third</div></div></div>
      <svg class="comical-generated"></svg>
    </div>
    <div class="bloom-canvas" id="canvas-b">
      <div class="bloom-canvas-element"><div class="bloom-editable">other canvas</div></div>
    </div>
    <div class="targets">
      <div class="bloom-canvas-element" data-target-of="drag-7">target of first</div>
    </div>
  </div>
</div>`;

function setUpPage(): HTMLElement {
    document.body.innerHTML = kPageHtml;
    return document.getElementById("page-1")!;
}

function elementsOf(canvasId: string): HTMLElement[] {
    return canvasElementsOf(document.getElementById(canvasId)!);
}

describe("canvasElementDeletion (page frame)", () => {
    beforeEach(() => {
        vi.clearAllMocks();
        frames.workspaceAvailable = true;
        setUpPage();
    });

    describe("captureCanvasElementDeletion", () => {
        it("records where the element was, its markup, and the whole family's bubble specs", () => {
            const second = elementsOf("canvas-a")[1];
            const record = captureCanvasElementDeletion(second)!;
            expect(record.pageId).toBe("page-1");
            expect(record.canvasIndex).toBe(0);
            expect(record.elementIndex).toBe(1);
            expect(record.elementHtml).toContain("second");
            expect(record.bubbleSpecs).toEqual([
                '{"order":1,"style":"speech"}',
                '{"order":2,"style":"speech"}',
                '{"order":3,"style":"speech"}',
            ]);
            expect(record.target).toBeUndefined();
        });

        it("records the drag-activity target of a draggable element", () => {
            const first = elementsOf("canvas-a")[0];
            const record = captureCanvasElementDeletion(first)!;
            expect(record.target).toBeDefined();
            expect(record.target!.html).toContain("target of first");
            const page = document.getElementById("page-1")!;
            const parent = resolvePathWithin(page, record.target!.parentPath)!;
            expect(parent.className).toBe("targets");
            expect(record.target!.index).toBe(0);
        });

        it("indexes the canvas among the page's canvases and tolerates a spec-less element", () => {
            const other = elementsOf("canvas-b")[0];
            const record = captureCanvasElementDeletion(other)!;
            expect(record.canvasIndex).toBe(1);
            expect(record.elementIndex).toBe(0);
            expect(record.bubbleSpecs).toEqual([null]);
        });

        it("returns undefined for an element that is not a canvas element on a page", () => {
            const loose = document.createElement("div");
            document.body.appendChild(loose);
            expect(captureCanvasElementDeletion(loose)).toBeUndefined();
        });
    });

    describe("recordCanvasElementDeletionForUndo", () => {
        it("hands the record to the workspace frame", () => {
            recordCanvasElementDeletionForUndo(elementsOf("canvas-a")[2]);
            expect(workspace.recordCanvasElementDeletion).toHaveBeenCalledTimes(
                1,
            );
            const record = workspace.recordCanvasElementDeletion.mock
                .calls[0][0] as ICanvasElementDeletionRecord;
            expect(record.elementIndex).toBe(2);
        });

        it("does nothing when there is no workspace frame (off-screen processing)", () => {
            frames.workspaceAvailable = false;
            expect(() =>
                recordCanvasElementDeletionForUndo(elementsOf("canvas-a")[2]),
            ).not.toThrow();
        });
    });

    describe("restoreDeletedCanvasElement", () => {
        // Play the deletion the way Comical and Bloom really perform it: remove the element,
        // renumber the later relatives, and remove the detached target.
        function deleteLikeBloom(index: number): ICanvasElementDeletionRecord {
            const canvas = document.getElementById("canvas-a")!;
            const element = elementsOf("canvas-a")[index];
            const record = captureCanvasElementDeletion(element)!;
            const goneOrder = JSON.parse(
                element.getAttribute("data-bubble")!,
            ).order;
            element.remove();
            for (const sibling of canvasElementsOf(canvas)) {
                const spec = JSON.parse(sibling.getAttribute("data-bubble")!);
                if (spec.order > goneOrder) {
                    spec.order--;
                    sibling.setAttribute("data-bubble", JSON.stringify(spec));
                }
            }
            if (record.target) {
                document.querySelector('[data-target-of="drag-7"]')!.remove();
            }
            return record;
        }

        it("puts the element back at its index and restores the family's specs", () => {
            const record = deleteLikeBloom(1);
            expect(elementsOf("canvas-a").map((e) => e.textContent)).toEqual([
                "first",
                "third",
            ]); // sanity
            expect(elementsOf("canvas-a")[1].getAttribute("data-bubble")).toBe(
                '{"order":2,"style":"speech"}',
            ); // sanity: Comical renumbered "third" to 2

            restoreDeletedCanvasElement(record);

            const after = elementsOf("canvas-a");
            expect(after.map((e) => e.textContent)).toEqual([
                "first",
                "second",
                "third",
            ]);
            expect(after.map((e) => e.getAttribute("data-bubble"))).toEqual([
                '{"order":1,"style":"speech"}',
                '{"order":2,"style":"speech"}',
                '{"order":3,"style":"speech"}',
            ]);
        });

        it("re-appends when it was the last element", () => {
            const record = deleteLikeBloom(2);
            restoreDeletedCanvasElement(record);
            expect(elementsOf("canvas-a").map((e) => e.textContent)).toEqual([
                "first",
                "second",
                "third",
            ]);
        });

        it("brings back the drag-activity target with the draggable", () => {
            const record = deleteLikeBloom(0);
            expect(
                document.querySelector('[data-target-of="drag-7"]'),
            ).toBeNull(); // sanity

            restoreDeletedCanvasElement(record);

            const target = document.querySelector('[data-target-of="drag-7"]')!;
            expect(target.parentElement!.className).toBe("targets");
            expect(target.textContent).toBe("target of first");
        });

        it("runs Comical and the manager's refresh over the restored element", () => {
            const record = deleteLikeBloom(1);
            restoreDeletedCanvasElement(record);
            const canvas = document.getElementById("canvas-a")!;
            expect(comical.update).toHaveBeenCalledWith(canvas);
            expect(manager.refreshCanvasElementEditing).toHaveBeenCalledTimes(
                1,
            );
            const [canvasArg, bubbleArg, attach, activate] =
                manager.refreshCanvasElementEditing.mock.calls[0];
            expect(canvasArg).toBe(canvas);
            expect((bubbleArg as FakeBubble).content.textContent).toBe(
                "second",
            );
            expect(attach).toBe(true);
            expect(activate).toBe(true);
        });

        it("leaves the other specs alone if the canvas has changed shape since", () => {
            const record = deleteLikeBloom(1);
            // Something else added a canvas element in the meantime.
            const extra = document.createElement("div");
            extra.className = "bloom-canvas-element";
            extra.setAttribute("data-bubble", '{"order":9}');
            document.getElementById("canvas-a")!.appendChild(extra);

            restoreDeletedCanvasElement(record);

            expect(extra.getAttribute("data-bubble")).toBe('{"order":9}');
            expect(elementsOf("canvas-a")[1].textContent).toBe("second");
        });

        it("throws, rather than restoring onto the wrong page", () => {
            const record = deleteLikeBloom(1);
            document.getElementById("page-1")!.id = "page-2";
            expect(() => restoreDeletedCanvasElement(record)).toThrow(
                /not showing/,
            );
        });
    });

    describe("redeleteCanvasElement", () => {
        it("deletes through the manager, with recording suppressed", () => {
            const second = elementsOf("canvas-a")[1];
            const record = captureCanvasElementDeletion(second)!;
            manager.deleteCanvasElement.mockImplementation(
                (el: HTMLElement) => {
                    // The real deleteCanvasElement calls this before removing; it must be a no-op now.
                    recordCanvasElementDeletionForUndo(el);
                    el.remove();
                },
            );

            redeleteCanvasElement(record);

            expect(manager.deleteCanvasElement).toHaveBeenCalledWith(second);
            expect(
                workspace.recordCanvasElementDeletion,
            ).not.toHaveBeenCalled();
            expect(elementsOf("canvas-a").map((e) => e.textContent)).toEqual([
                "first",
                "third",
            ]);
        });

        it("records again once a redo is over", () => {
            const record = captureCanvasElementDeletion(
                elementsOf("canvas-a")[1],
            )!;
            manager.deleteCanvasElement.mockImplementation((el: HTMLElement) =>
                el.remove(),
            );
            redeleteCanvasElement(record);
            recordCanvasElementDeletionForUndo(elementsOf("canvas-a")[0]);
            expect(workspace.recordCanvasElementDeletion).toHaveBeenCalledTimes(
                1,
            );
        });
    });

    describe("path helpers", () => {
        it("round-trip a position within the page", () => {
            const page = document.getElementById("page-1")!;
            const target = document.querySelector('[data-target-of="drag-7"]')!;
            const path = pathWithin(page, target)!;
            expect(resolvePathWithin(page, path)).toBe(target);
            expect(pathWithin(page, document.body)).toBeUndefined();
        });
    });
});
