import { describe, expect, it } from "vitest";
import { keyChangesText } from "./lockedEditable";

function key(key: string, modifiers: Partial<KeyboardEventInit> = {}) {
    return new KeyboardEvent("keydown", { key, ...modifiers });
}

describe("keyChangesText", () => {
    it("lets through keys that move around or leave the box", () => {
        for (const k of [
            "ArrowLeft",
            "Home",
            "PageDown",
            "Tab",
            "Escape",
            "Shift",
            "F5",
        ])
            expect(keyChangesText(key(k)), k).toBe(false);
    });

    it("lets through copying and selecting everything", () => {
        expect(keyChangesText(key("c", { ctrlKey: true }))).toBe(false);
        expect(keyChangesText(key("A", { ctrlKey: true }))).toBe(false);
        expect(keyChangesText(key("c", { metaKey: true }))).toBe(false);
    });

    it("refuses keys that would change the text", () => {
        for (const k of ["a", "Enter", "Backspace", "Delete", " "])
            expect(keyChangesText(key(k)), k).toBe(true);
        expect(keyChangesText(key("v", { ctrlKey: true })), "paste").toBe(true);
        expect(keyChangesText(key("x", { ctrlKey: true })), "cut").toBe(true);
        expect(keyChangesText(key("z", { ctrlKey: true })), "undo").toBe(true);
    });
});
