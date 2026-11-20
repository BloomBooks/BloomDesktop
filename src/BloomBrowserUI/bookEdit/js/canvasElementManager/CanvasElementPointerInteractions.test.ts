import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

// Where the mouse is over a table boundary is decided by bloom-table's dragToResize. These tests
// say where the boundary is, so they can move the mouse on and off it.
const tableEdge = vi.hoisted(() => ({
    current: undefined as "row" | "column" | undefined,
}));
vi.mock("bloom-table", () => ({
    dragToResize: { resizeEdgeAtPoint: () => tableEdge.current },
}));
vi.mock("../tableFeature", () => ({ tablesMayBeRestructured: () => true }));
// The toolbar is a React component whose hooks ask Bloom's server for things; unit tests must
// not reach the network, and the toolbar has nothing to do with cursors.
vi.mock("./CanvasElementContextControls", () => ({
    renderCanvasElementContextControls: vi.fn(),
}));

// This import deliberately comes after the vi.mock calls above.
import {
    CanvasElementPointerInteractions,
    ICanvasElementPointerInteractionsHost,
} from "./CanvasElementPointerInteractions";
import { CanvasSnapProvider } from "./CanvasSnapProvider";
import { CanvasGuideProvider } from "./CanvasGuideProvider";

// A bloom-canvas holding a table cell, the control frame of a selected element with one of its
// handles, and an element that sets a resize cursor of its own.
function setUpCanvas(): {
    cell: HTMLElement;
    handle: HTMLElement;
    other: HTMLElement;
} {
    document.body.innerHTML = `
        <div class="bloom-page">
            <div class="bloom-canvas">
                <div class="bloom-canvas-element"><table><tr><td id="cell"></td></tr></table></div>
                <div id="other" style="cursor: nwse-resize"></div>
                <div id="canvas-element-control-frame">
                    <div id="handle" class="bloom-ui-canvas-element-side-handle bloom-ui-canvas-element-side-handle-n"></div>
                </div>
            </div>
        </div>`;
    const canvas = document.querySelector(".bloom-canvas") as HTMLElement;
    const interactions = new CanvasElementPointerInteractions(
        {
            getActiveElement: () => undefined,
            getCanvasElementWeAreTextEditing: () => undefined,
        } as unknown as ICanvasElementPointerInteractionsHost,
        {} as CanvasSnapProvider,
        {} as CanvasGuideProvider,
    );
    interactions.setMouseDragHandlers(canvas);
    return {
        cell: document.getElementById("cell") as HTMLElement,
        handle: document.getElementById("handle") as HTMLElement,
        other: document.getElementById("other") as HTMLElement,
    };
}
function moveMouseOver(element: HTMLElement): void {
    element.dispatchEvent(
        new MouseEvent("mousemove", { bubbles: true, buttons: 0 }),
    );
}

beforeEach(() => {
    tableEdge.current = undefined;
});

afterEach(() => {
    document.body.innerHTML = "";
});

describe("the table resize cursor (BL-16741)", () => {
    test("a control frame handle lying over a table boundary gets no table cursor, because a press there drags the handle", () => {
        const { handle } = setUpCanvas();

        tableEdge.current = "row";
        moveMouseOver(handle);

        // The handle's own cursor comes from the stylesheet; an inline one would hide it.
        expect(handle.style.cursor).toBe("");
    });

    test("hovering an element leaves its own resize cursor alone", () => {
        const { other } = setUpCanvas();
        expect(other.style.cursor).toBe("nwse-resize");

        moveMouseOver(other);

        expect(other.style.cursor).toBe("nwse-resize");
    });

    test("the cursor shown over a table boundary goes when the mouse leaves the boundary, and only that one", () => {
        const { cell, other } = setUpCanvas();

        tableEdge.current = "row";
        moveMouseOver(cell);
        expect(cell.style.cursor).toBe("ns-resize");

        tableEdge.current = undefined;
        moveMouseOver(other);
        expect(cell.style.cursor).toBe("");
        expect(other.style.cursor).toBe("nwse-resize");
    });

    test("moving along a boundary from one element to another moves the cursor with it", () => {
        const { cell } = setUpCanvas();
        const otherCell = document.createElement("td");
        cell.parentElement!.appendChild(otherCell);

        tableEdge.current = "column";
        moveMouseOver(cell);
        moveMouseOver(otherCell);

        expect(cell.style.cursor).toBe("");
        expect(otherCell.style.cursor).toBe("ew-resize");
    });
});
