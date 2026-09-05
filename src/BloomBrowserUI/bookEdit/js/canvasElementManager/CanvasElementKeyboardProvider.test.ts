import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { CanvasElementKeyboardProvider } from "./CanvasElementKeyboardProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

// A table is a canvas element, so its element-level keys collide with typing in a
// cell: Delete and Backspace would delete the whole table while the user meant to
// delete a character, and the arrow keys would nudge the table while the user meant
// to move the caret. The provider's guard is that it ignores a key whose target is
// contenteditable, which is exactly what a cell's bloom-editable is. These tests
// pin that down for the table case, alongside the plain text canvas element it was
// first written for.

// jsdom does not implement isContentEditable (it reads back as undefined), so the
// property has to be stood in for. The value a browser computes is what is being
// emulated: true for an element inside a contenteditable subtree.
const makeEditable = (host: HTMLElement): HTMLElement => {
    const editable = document.createElement("div");
    editable.classList.add("bloom-editable");
    editable.setAttribute("contenteditable", "true");
    Object.defineProperty(editable, "isContentEditable", { value: true });
    host.appendChild(editable);
    return editable;
};

describe("CanvasElementKeyboardProvider", () => {
    let provider: CanvasElementKeyboardProvider;
    let deleteCurrentCanvasElement: ReturnType<typeof vi.fn>;
    let moveActiveCanvasElement: ReturnType<typeof vi.fn>;
    let activeElement: HTMLElement;

    beforeEach(() => {
        document.body.innerHTML = "";
        deleteCurrentCanvasElement = vi.fn();
        moveActiveCanvasElement = vi.fn();
        // The active element is the table's canvas element, as it is once the user
        // has clicked a cell.
        activeElement = document.createElement("div");
        activeElement.classList.add("bloom-canvas-element");
        document.body.appendChild(activeElement);
        provider = new CanvasElementKeyboardProvider(
            {
                deleteCurrentCanvasElement,
                moveActiveCanvasElement,
                getActiveCanvasElement: () => activeElement,
            },
            new CanvasSnapProvider(),
        );
    });

    afterEach(() => {
        provider.dispose();
    });

    const pressKey = (target: HTMLElement, key: string) =>
        target.dispatchEvent(
            new KeyboardEvent("keydown", { key, bubbles: true }),
        );

    /** A table with one cell holding a Bloom text box the caret can sit in. */
    const makeTableWithACellEditable = (): HTMLElement => {
        const table = document.createElement("div");
        table.classList.add("bloom-table");
        activeElement.appendChild(table);
        const cell = document.createElement("div");
        cell.classList.add("bloom-cell");
        table.appendChild(cell);
        return makeEditable(cell);
    };

    it("leaves the table element alone when Delete or Backspace comes from a cell", () => {
        const cellEditable = makeTableWithACellEditable();

        pressKey(cellEditable, "Delete");
        pressKey(cellEditable, "Backspace");

        expect(deleteCurrentCanvasElement).not.toHaveBeenCalled();
    });

    it("does not nudge the table when an arrow key comes from a cell", () => {
        const cellEditable = makeTableWithACellEditable();

        pressKey(cellEditable, "ArrowRight");
        pressKey(cellEditable, "ArrowDown");

        expect(moveActiveCanvasElement).not.toHaveBeenCalled();
    });

    it("still deletes and nudges when the key comes from the selected element itself", () => {
        // Sanity check for the two tests above: the provider does act on these keys,
        // so their silence there is the guard working and not a provider that never
        // does anything.
        makeTableWithACellEditable();

        pressKey(activeElement, "Delete");
        expect(deleteCurrentCanvasElement).toHaveBeenCalledTimes(1);

        pressKey(activeElement, "ArrowRight");
        expect(moveActiveCanvasElement).toHaveBeenCalledTimes(1);
    });
});
