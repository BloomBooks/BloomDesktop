import { afterEach, beforeEach, describe, expect, it, test, vi } from "vitest";

import {
    CanvasElementKeyboardProvider,
    getZOrderMoveForShortcut,
    ICanvasElementKeyboardActions,
} from "./CanvasElementKeyboardProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

// Tests for the keyboard shortcuts of the Layer commands (BL-15992): Ctrl+] / Ctrl+[ move the
// active canvas element one step forward / backward, and Ctrl+Shift+] / Ctrl+Shift+[ move it all
// the way to the front / back.

const makeKeyEvent = (init: KeyboardEventInit): KeyboardEvent =>
    new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init });

describe("getZOrderMoveForShortcut", () => {
    test("Ctrl+] is forward and Ctrl+Shift+] is front", () => {
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ code: "BracketRight", ctrlKey: true }),
            ),
        ).toBe("forward");
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    key: "}",
                    code: "BracketRight",
                    ctrlKey: true,
                    shiftKey: true,
                }),
            ),
        ).toBe("front");
    });

    test("Ctrl+[ is backward and Ctrl+Shift+[ is back", () => {
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ code: "BracketLeft", ctrlKey: true }),
            ),
        ).toBe("backward");
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    key: "{",
                    code: "BracketLeft",
                    ctrlKey: true,
                    shiftKey: true,
                }),
            ),
        ).toBe("back");
    });

    test("the bracket characters count too, for layouts where they live on other keys", () => {
        // e.g. a German layout, where ] is AltGr+9: the code is the digit key.
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ key: "]", code: "Digit9", ctrlKey: true }),
            ),
        ).toBe("forward");
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ key: "[", code: "Digit8", ctrlKey: true }),
            ),
        ).toBe("backward");
    });

    test("a bracket typed with AltGr counts as the bracket", () => {
        // Windows reports AltGr as Ctrl+Alt with the AltGraph modifier state set.
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    key: "]",
                    code: "Digit9",
                    ctrlKey: true,
                    altKey: true,
                    modifierAltGraph: true,
                }),
            ),
        ).toBe("forward");
        // And Shift on the physical bracket key still goes all the way on that layout.
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    key: "*",
                    code: "BracketRight",
                    ctrlKey: true,
                    shiftKey: true,
                }),
            ),
        ).toBe("front");
    });

    test("the Command key counts as Ctrl", () => {
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ code: "BracketRight", metaKey: true }),
            ),
        ).toBe("forward");
    });

    test("without Ctrl, with a real Alt, or on another key it is not a layer shortcut", () => {
        expect(
            getZOrderMoveForShortcut(makeKeyEvent({ code: "BracketRight" })),
        ).toBeUndefined();
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    code: "BracketRight",
                    ctrlKey: true,
                    altKey: true,
                }),
            ),
        ).toBeUndefined();
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ code: "KeyD", ctrlKey: true }),
            ),
        ).toBeUndefined();
    });
});

describe("CanvasElementKeyboardProvider layer shortcuts", () => {
    let actions: ICanvasElementKeyboardActions;
    let provider: CanvasElementKeyboardProvider;
    let activeElement: HTMLElement;

    beforeEach(() => {
        activeElement = document.createElement("div");
        activeElement.className = "bloom-canvas-element";
        document.body.appendChild(activeElement);
        actions = {
            deleteCurrentCanvasElement: vi.fn(),
            moveActiveCanvasElement: vi.fn(),
            moveActiveCanvasElementInZOrder: vi.fn(),
            getActiveCanvasElement: () => activeElement,
        };
        // Only getMinimumStepSize is used, and only for arrow keys.
        const snapProvider = {
            getMinimumStepSize: () => 1,
        } as unknown as CanvasSnapProvider;
        provider = new CanvasElementKeyboardProvider(actions, snapProvider);
    });

    afterEach(() => {
        provider.dispose();
        document.body.innerHTML = "";
    });

    test("Ctrl+] on a selected canvas element brings it forward and is consumed", () => {
        const event = makeKeyEvent({ code: "BracketRight", ctrlKey: true });
        activeElement.dispatchEvent(event);

        expect(actions.moveActiveCanvasElementInZOrder).toHaveBeenCalledWith(
            "forward",
        );
        expect(event.defaultPrevented).toBe(true);
        // Sanity: the other actions were left alone.
        expect(actions.moveActiveCanvasElement).not.toHaveBeenCalled();
        expect(actions.deleteCurrentCanvasElement).not.toHaveBeenCalled();
    });

    test("Ctrl+Shift+[ sends it to the back", () => {
        activeElement.dispatchEvent(
            makeKeyEvent({
                key: "{",
                code: "BracketLeft",
                ctrlKey: true,
                shiftKey: true,
            }),
        );
        expect(actions.moveActiveCanvasElementInZOrder).toHaveBeenCalledWith(
            "back",
        );
    });

    test("the shortcut is ignored while typing in editable text", () => {
        const editable = document.createElement("div");
        editable.contentEditable = "true";
        // jsdom does not compute isContentEditable from the attribute.
        Object.defineProperty(editable, "isContentEditable", { value: true });
        activeElement.appendChild(editable);

        const event = makeKeyEvent({ code: "BracketRight", ctrlKey: true });
        editable.dispatchEvent(event);

        expect(actions.moveActiveCanvasElementInZOrder).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
    });

    test("the shortcut is ignored when the background image is selected", () => {
        activeElement.classList.add("bloom-backgroundImage");

        const event = makeKeyEvent({ code: "BracketRight", ctrlKey: true });
        activeElement.dispatchEvent(event);

        expect(actions.moveActiveCanvasElementInZOrder).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
    });

    test("the shortcut is left alone when no canvas element is selected", () => {
        actions.getActiveCanvasElement = () => null;

        const event = makeKeyEvent({ code: "BracketRight", ctrlKey: true });
        document.body.dispatchEvent(event);

        expect(actions.moveActiveCanvasElementInZOrder).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
    });

    test("a plain ] does nothing", () => {
        const event = makeKeyEvent({ code: "BracketRight" });
        activeElement.dispatchEvent(event);

        expect(actions.moveActiveCanvasElementInZOrder).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
    });
});

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
                moveActiveCanvasElementInZOrder: vi.fn(),
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
