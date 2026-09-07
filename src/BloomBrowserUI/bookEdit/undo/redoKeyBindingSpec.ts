// Tests for the page-frame Ctrl+Y binding (BL-6681). See redoKeyBinding.ts for why it exists and
// why it must be the last resort rather than the first.

import { describe, it, expect, beforeEach } from "vitest";
import {
    installRedoKeyBinding,
    IRedoTarget,
    isRedoKeystroke,
} from "./redoKeyBinding";

function keydown(init: KeyboardEventInit): KeyboardEvent {
    return new KeyboardEvent("keydown", {
        bubbles: true,
        cancelable: true,
        ...init,
    });
}

describe("redoKeyBinding", () => {
    describe("isRedoKeystroke", () => {
        it("is Ctrl+Y, either case", () => {
            expect(isRedoKeystroke(keydown({ key: "y", ctrlKey: true }))).toBe(
                true,
            );
            expect(isRedoKeystroke(keydown({ key: "Y", ctrlKey: true }))).toBe(
                true,
            );
        });

        it("is not Y alone, Ctrl+Z, or Ctrl+Y with another modifier", () => {
            expect(isRedoKeystroke(keydown({ key: "y" }))).toBe(false);
            expect(isRedoKeystroke(keydown({ key: "z", ctrlKey: true }))).toBe(
                false,
            );
            expect(
                isRedoKeystroke(
                    keydown({ key: "y", ctrlKey: true, shiftKey: true }),
                ),
            ).toBe(false);
            expect(
                isRedoKeystroke(
                    keydown({ key: "y", ctrlKey: true, altKey: true }),
                ),
            ).toBe(false);
        });
    });

    describe("installRedoKeyBinding", () => {
        let doc: Document;
        let editable: HTMLElement;
        let target: IRedoTarget;
        let canRedo: boolean;
        let redoCalls: number;

        beforeEach(() => {
            doc = document.implementation.createHTMLDocument("page");
            editable = doc.createElement("div");
            doc.body.appendChild(editable);
            canRedo = false;
            redoCalls = 0;
            target = {
                canRedo: () => canRedo,
                handleRedo: () => {
                    redoCalls++;
                },
            };
            installRedoKeyBinding(doc, () => target);
        });

        it("redoes, and claims the keystroke, when the stack has something to redo", () => {
            canRedo = true;
            const e = keydown({ key: "y", ctrlKey: true });
            editable.dispatchEvent(e);
            expect(redoCalls).toBe(1);
            expect(e.defaultPrevented).toBe(true);
        });

        it("leaves the keystroke alone when there is nothing to redo", () => {
            // This is what lets CKEditor's redo, and the browser's, keep working until converted.
            canRedo = false;
            const e = keydown({ key: "y", ctrlKey: true });
            editable.dispatchEvent(e);
            expect(redoCalls).toBe(0);
            expect(e.defaultPrevented).toBe(false);
        });

        it("defers to a handler earlier in the bubble that already claimed the keystroke", () => {
            canRedo = true;
            // Stand-in for the reader tools' per-editable handler, which prevents the default.
            editable.addEventListener("keydown", (e) => e.preventDefault());
            const e = keydown({ key: "y", ctrlKey: true });
            editable.dispatchEvent(e);
            expect(redoCalls).toBe(0);
        });

        it("stands down in Change Layout mode, where origami redoes without claiming the key", () => {
            canRedo = true;
            const marginBox = doc.createElement("div");
            marginBox.className = "marginBox origami-layout-mode";
            doc.body.appendChild(marginBox);
            const e = keydown({ key: "y", ctrlKey: true });
            editable.dispatchEvent(e);
            expect(redoCalls).toBe(0);
            expect(e.defaultPrevented).toBe(false);
        });

        it("ignores keystrokes that are not Ctrl+Y", () => {
            canRedo = true;
            editable.dispatchEvent(keydown({ key: "y" }));
            editable.dispatchEvent(keydown({ key: "z", ctrlKey: true }));
            expect(redoCalls).toBe(0);
        });

        it("does nothing when there is no workspace bundle to reach", () => {
            const bare = document.implementation.createHTMLDocument("bare");
            installRedoKeyBinding(bare, () => null);
            const e = keydown({ key: "y", ctrlKey: true });
            bare.body.dispatchEvent(e);
            expect(e.defaultPrevented).toBe(false);
        });
    });
});
