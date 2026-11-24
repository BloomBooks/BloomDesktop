import { describe, it, expect, beforeAll, beforeEach } from "vitest";
import { installPlayModeEditBlocker } from "./playModeEditBlocker";

// While a game page is in Play, keys and clipboard events that would edit the page must not reach
// any of the page's editing handlers; otherwise everything must pass through untouched.

let pageParent: HTMLElement;
let editable: HTMLElement;
let gameButton: HTMLElement;
let seenByPage: string[];

function press(target: HTMLElement, key: string, ctrlKey = false) {
    const e = new KeyboardEvent("keydown", {
        key,
        ctrlKey,
        bubbles: true,
        cancelable: true,
    });
    target.dispatchEvent(e);
    return e;
}

function setPlaying(playing: boolean) {
    pageParent.classList.toggle("drag-activity-play", playing);
}

describe("installPlayModeEditBlocker", () => {
    beforeAll(() => {
        installPlayModeEditBlocker(window);
        // Stands for all the page's own editing handlers, which listen on the document or below.
        document.addEventListener(
            "keydown",
            (e) => seenByPage.push((e.ctrlKey ? "Ctrl+" : "") + e.key),
            { capture: true },
        );
        document.addEventListener("paste", () => seenByPage.push("paste"), {
            capture: true,
        });
    });

    beforeEach(() => {
        seenByPage = [];
        document.body.innerHTML =
            '<div id="parent"><div class="bloom-page"><div class="bloom-editable" contenteditable="true"></div><button>Check</button></div></div>';
        pageParent = document.getElementById("parent")!;
        editable = document.querySelector(".bloom-editable")!;
        // jsdom doesn't compute isContentEditable from the attribute.
        Object.defineProperty(editable, "isContentEditable", { value: true });
        gameButton = document.querySelector("button")!;
    });

    it("in Play, stops keys that would edit the page", () => {
        setPlaying(true);
        expect(press(editable, "a").defaultPrevented).toBe(true); // typing
        expect(press(gameButton, "z", true).defaultPrevented).toBe(true); // undo
        expect(press(gameButton, "v", true).defaultPrevented).toBe(true); // paste
        expect(press(gameButton, "Delete").defaultPrevented).toBe(true);
        expect(press(gameButton, "ArrowLeft").defaultPrevented).toBe(true);
        expect(seenByPage, "none of them reached the page's handlers").toEqual(
            [],
        );
    });

    it("in Play, lets through keys that edit nothing", () => {
        setPlaying(true);
        expect(press(gameButton, "c", true).defaultPrevented).toBe(false); // copy
        expect(press(gameButton, "Enter").defaultPrevented).toBe(false); // press the button
        expect(press(gameButton, "+", true).defaultPrevented).toBe(false); // zoom
        expect(press(gameButton, "Control", true).defaultPrevented).toBe(false); // Ctrl alone
        expect(seenByPage).toEqual([
            "Ctrl+c",
            "Enter",
            "Ctrl++",
            "Ctrl+Control",
        ]);
    });

    it("in Play, stops pasting", () => {
        setPlaying(true);
        const e = new Event("paste", { bubbles: true, cancelable: true });
        editable.dispatchEvent(e);
        expect(e.defaultPrevented).toBe(true);
        expect(seenByPage).toEqual([]);
    });

    it("when not in Play, changes nothing", () => {
        setPlaying(false);
        expect(press(editable, "a").defaultPrevented).toBe(false);
        expect(press(gameButton, "z", true).defaultPrevented).toBe(false);
        editable.dispatchEvent(
            new Event("paste", { bubbles: true, cancelable: true }),
        );
        expect(seenByPage).toEqual(["a", "Ctrl+z", "paste"]);
    });
});
