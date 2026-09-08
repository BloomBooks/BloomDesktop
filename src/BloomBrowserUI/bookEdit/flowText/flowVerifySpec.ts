import { beforeEach, describe, expect, it } from "vitest";
import { verifyAndNudge } from "./flowVerify";

function makeEditable(innerHtml: string): HTMLElement {
    const editable = document.createElement("div");
    editable.className = "bloom-editable bloom-visibility-code-on";
    editable.setAttribute("contenteditable", "true");
    editable.innerHTML = innerHtml;
    document.body.appendChild(editable);
    return editable;
}

/** Overflow amounts the real layout would report, one per call, the last one repeating. */
function scriptOverflow(amounts: number[]) {
    const calls: HTMLElement[] = [];
    const measure = (editable: HTMLElement): number => {
        const amount = amounts[Math.min(calls.length, amounts.length - 1)];
        calls.push(editable);
        return amount;
    };

    return { measure, calls };
}

/** The words of the box, counting a paragraph break as a word break. */
function wordsIn(editable: HTMLElement): string[] {
    return Array.from(editable.querySelectorAll("p"))
        .map((paragraph) => paragraph.textContent ?? "")
        .join(" ")
        .split(/\s+/)
        .filter(Boolean);
}

describe("verifyAndNudge", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("moves one word on when the real layout says the box is over full", () => {
        const first = makeEditable("<p>one two three</p>");
        const second = makeEditable("<p><br></p>");
        const overflow = scriptOverflow([5, 0]);

        const moved = verifyAndNudge([first, second], [0], overflow.measure);

        expect(moved).toBe(true);
        expect(wordsIn(first)).toEqual(["one", "two"]);
        expect(wordsIn(second)).toEqual(["three"]);
        expect(overflow.calls).toHaveLength(2);
    });

    it("moves nothing when the box already fits", () => {
        const first = makeEditable("<p>one two three</p>");
        const second = makeEditable("<p><br></p>");
        const overflow = scriptOverflow([0]);

        const moved = verifyAndNudge([first, second], [0], overflow.measure);

        expect(moved).toBe(false);
        expect(wordsIn(first)).toEqual(["one", "two", "three"]);
        expect(overflow.calls).toHaveLength(1);
    });

    it("gives up after six nudges of one box", () => {
        const first = makeEditable("<p>a b c d e f g h i j</p>");
        const second = makeEditable("<p><br></p>");
        const overflow = scriptOverflow([5]);

        verifyAndNudge([first, second], [0], overflow.measure);

        expect(overflow.calls).toHaveLength(6);
        expect(wordsIn(first)).toEqual(["a", "b", "c", "d"]);
        expect(wordsIn(second)).toEqual(["e", "f", "g", "h", "i", "j"]);
    });

    it("moves one grapheme when the box holds a single word, and never empties it", () => {
        const first = makeEditable("<p>abcdef</p>");
        const second = makeEditable("<p><br></p>");
        const overflow = scriptOverflow([5]);

        verifyAndNudge([first, second], [0], overflow.measure);

        expect(first.textContent).toBe("a");
        expect(second.textContent).toBe("bcdef");
    });

    it("does not check the last box on the page, which has nowhere to send text", () => {
        const first = makeEditable("<p>one two three</p>");
        const second = makeEditable("<p>four five</p>");
        const overflow = scriptOverflow([5]);

        const moved = verifyAndNudge([first, second], [1], overflow.measure);

        expect(moved).toBe(false);
        expect(overflow.calls).toHaveLength(0);
    });

    it("checks the boxes in chain order, so a word can travel two boxes on", () => {
        const first = makeEditable("<p>one two</p>");
        const second = makeEditable("<p>three four</p>");
        const third = makeEditable("<p>five</p>");
        // First box over full, then fits; second box over full, then fits.
        const overflow = scriptOverflow([5, 0, 5, 0]);

        verifyAndNudge([first, second, third], [1, 0], overflow.measure);

        expect(wordsIn(first)).toEqual(["one"]);
        expect(wordsIn(third)).toEqual(["four", "five"]);
    });
});
