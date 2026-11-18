import { afterEach, describe, expect, it } from "vitest";
import { kScrollTopVariable, trackScrollOffsets } from "./scrollOffset";

// These need real layout (scrolling and scroll extents), so this runs in the "browser" vitest
// project, not jsdom.

const kLineHeightPx = 20;

// Adds a text box 200 by 100 pixels holding `lineCount` lines, with a language label placed the
// way editMode.less places it, and returns the box.
function addBox(lineCount: number): HTMLElement {
    const host = document.createElement("div");
    host.id = "scrollOffsetTestHost";
    host.innerHTML = `
        <style>
            #scrollOffsetTestHost .bloom-editable:after {
                content: "English";
                position: absolute;
                right: 0;
                line-height: 1;
                bottom: calc(2px - var(${kScrollTopVariable}, 0px));
            }
        </style>
        <div class="bloom-editable" style="position: relative; width: 200px; height: 100px;
            overflow: hidden; font: 14px Arial; line-height: ${kLineHeightPx}px; padding: 0;
            margin: 0;">${lines(lineCount)}</div>`;
    document.body.appendChild(host);
    return host.querySelector<HTMLElement>(".bloom-editable")!;
}

// `count` lines of text as paragraphs.
function lines(count: number): string {
    return Array.from(
        { length: count },
        (_, i) => `<p style="margin: 0">Line ${i + 1}</p>`,
    ).join("");
}

// Scrolls `box` to `scrollTop` and lets the scroll event that the browser sends for it arrive.
async function scrollTo(box: HTMLElement, scrollTop: number): Promise<void> {
    const scrolled = new Promise((resolve) =>
        box.addEventListener("scroll", resolve, { once: true }),
    );
    box.scrollTop = scrollTop;
    await scrolled;
}

trackScrollOffsets(document);

afterEach(() => {
    document.getElementById("scrollOffsetTestHost")?.remove();
});

describe("a scrolled text box whose text changes so that it fits", () => {
    it("scrolls back to show its first line when lines are removed", async () => {
        const box = addBox(10);
        await scrollTo(box, 5 * kLineHeightPx);
        // Sanity check: the box is scrolled and its label offset recorded.
        expect(box.scrollTop).toBe(100);
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("100px");

        // As Undo or deleting does: the box now holds fewer lines than it shows.
        box.innerHTML = lines(3);
        // MutationObserver callbacks run as a microtask after the change.
        await Promise.resolve();

        expect(box.scrollTop).toBe(0);
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("");
    });

    it("scrolls only as far back as the remaining text needs when it still does not fit", async () => {
        const box = addBox(10);
        await scrollTo(box, 5 * kLineHeightPx);

        // Seven lines in a box that shows five can scroll two lines at most.
        box.innerHTML = lines(7);
        await Promise.resolve();

        expect(box.scrollTop).toBe(2 * kLineHeightPx);
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("40px");
    });

    it("scrolls back when text is deleted from within a paragraph", async () => {
        const box = addBox(1);
        const text = box.firstElementChild!.firstChild as Text;
        text.data = "words ".repeat(100);
        await scrollTo(box, kLineHeightPx);
        // Sanity check: the paragraph wraps to more lines than the box shows.
        expect(box.scrollTop).toBe(20);

        // Deleting inside a paragraph changes its text, not which elements the box holds.
        text.data = "words";
        await Promise.resolve();

        expect(box.scrollTop).toBe(0);
    });
});
