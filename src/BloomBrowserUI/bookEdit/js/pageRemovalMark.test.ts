import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

// Tests for the red X that shows which page the user is being asked to remove.

import { markPageForRemoval } from "./pageRemovalMark";

const kViewportWidth = 1000;
const kViewportHeight = 800;

function placeAt(
    element: HTMLElement,
    left: number,
    top: number,
    width: number,
    height: number,
): void {
    element.getBoundingClientRect = () =>
        new DOMRect(left - window.scrollX, top - window.scrollY, width, height);
}

function makeEditedPage(): HTMLElement {
    const page = document.createElement("div");
    page.classList.add("bloom-page");
    page.id = "edited-page";
    document.body.appendChild(page);
    return page;
}

function getMarks(): SVGSVGElement[] {
    return Array.from(document.querySelectorAll("svg.bloom-page-removal-mark"));
}

function getLineEnds(mark: SVGSVGElement): number[][] {
    return Array.from(mark.querySelectorAll("line")).map((line) =>
        ["x1", "y1", "x2", "y2"].map((name) => Number(line.getAttribute(name))),
    );
}

let scrollBy: ReturnType<typeof vi.fn>;

beforeEach(() => {
    Object.defineProperty(document.documentElement, "clientWidth", {
        configurable: true,
        value: kViewportWidth,
    });
    Object.defineProperty(document.documentElement, "clientHeight", {
        configurable: true,
        value: kViewportHeight,
    });
    scrollBy = vi.fn();
    window.scrollBy = scrollBy as unknown as typeof window.scrollBy;
});

afterEach(() => {
    document.body.innerHTML = "";
});

describe("markPageForRemoval", () => {
    test("draws an X across the page being edited, 30px short of each corner", () => {
        const page = makeEditedPage();
        placeAt(page, 100, 50, 400, 600);

        markPageForRemoval();

        const marks = getMarks();
        expect(marks.length).toBe(1);
        const mark = marks[0];
        expect(mark.style.position).toBe("fixed");
        expect(mark.style.left).toBe("100px");
        expect(mark.style.top).toBe("50px");
        expect(mark.getAttribute("width")).toBe("400");
        expect(mark.getAttribute("height")).toBe("600");
        expect(mark.classList.contains("bloom-ui")).toBe(true);
        expect(getLineEnds(mark)).toEqual([
            [30, 30, 370, 570],
            [370, 30, 30, 570],
        ]);
        mark.querySelectorAll("line").forEach((line) => {
            expect(line.getAttribute("stroke")).toBe("#ff0000");
            expect(line.getAttribute("stroke-width")).toBe("4");
        });
    });

    test("the function it returns takes the X away", () => {
        placeAt(makeEditedPage(), 100, 50, 400, 600);

        const removeMark = markPageForRemoval();
        expect(getMarks().length).toBe(1);

        removeMark();
        expect(getMarks().length).toBe(0);
    });

    test("a page that is already fully in view is not scrolled", () => {
        placeAt(makeEditedPage(), 100, 50, 400, 600);

        markPageForRemoval();

        expect(scrollBy).not.toHaveBeenCalled();
    });

    test("a page below the view is scrolled to the middle of it", () => {
        placeAt(makeEditedPage(), 300, 2000, 400, 600);

        markPageForRemoval();

        // The page is 600 high in an 800 high view, so its top should end up at 100.
        expect(scrollBy).toHaveBeenCalledWith(0, 1900);
    });

    test("the bar of controls across the top of the frame does not count as part of the view", () => {
        const bar = document.createElement("div");
        bar.classList.add("bloom-controls-bar");
        document.body.appendChild(bar);
        placeAt(bar, 0, 0, kViewportWidth, 40);
        const page = makeEditedPage();
        // Fully inside the window, but its top is under the bar.
        placeAt(page, 100, 20, 400, 600);

        markPageForRemoval();

        // The view below the bar is 760 high, so the page's top should end up at 40 + 80 = 120.
        expect(scrollBy).toHaveBeenCalledWith(0, -100);
    });
});
