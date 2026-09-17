import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import {
    CanvasElementKeyboardProvider,
    getZOrderMoveForShortcut,
    ICanvasElementKeyboardActions,
} from "./CanvasElementKeyboardProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

// Tests for the keyboard shortcuts of the Layer commands (BL-15992): Ctrl+] / Ctrl+[ move the
// active canvas element one step forward / backward, and Ctrl+Alt+] / Ctrl+Alt+[ move it all
// the way to the front / back.

const makeKeyEvent = (init: KeyboardEventInit): KeyboardEvent =>
    new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init });

describe("getZOrderMoveForShortcut", () => {
    test("Ctrl+] is forward and Ctrl+Alt+] is front", () => {
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ code: "BracketRight", ctrlKey: true }),
            ),
        ).toBe("forward");
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    code: "BracketRight",
                    ctrlKey: true,
                    altKey: true,
                }),
            ),
        ).toBe("front");
    });

    test("Ctrl+[ is backward and Ctrl+Alt+[ is back", () => {
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ code: "BracketLeft", ctrlKey: true }),
            ),
        ).toBe("backward");
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    code: "BracketLeft",
                    ctrlKey: true,
                    altKey: true,
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

    test("the Command key counts as Ctrl", () => {
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({ code: "BracketRight", metaKey: true }),
            ),
        ).toBe("forward");
    });

    test("without Ctrl, with Shift, or on another key it is not a layer shortcut", () => {
        expect(
            getZOrderMoveForShortcut(makeKeyEvent({ code: "BracketRight" })),
        ).toBeUndefined();
        expect(
            getZOrderMoveForShortcut(
                makeKeyEvent({
                    code: "BracketRight",
                    ctrlKey: true,
                    shiftKey: true,
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

    test("Ctrl+Alt+[ sends it to the back", () => {
        activeElement.dispatchEvent(
            makeKeyEvent({ code: "BracketLeft", ctrlKey: true, altKey: true }),
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
