// These tests check what a click lands on while editing a block that holds an inline image
// that has been dragged down. The wrapper's box includes the transparent offset padding above
// the picture, and the text runs through that padding. A click on that text must reach the
// text, and a click on the picture must reach the image (BL-17077).
//
// They use elementFromPoint, which is the browser's own hit test, so they check the same thing
// that decides where a real click goes.

import { test, expect, Page } from "playwright/test";
import { loadInlineImagePage } from "../helpers/inlineImagePage";

// The editable is 400px wide and the image 40% of it, so 160px square (aspect ratio 1),
// 60px down. Its top is therefore at 60 and its bottom at 220.
const OFFSET = 60;

/**
 * What a click at (x, y), relative to the editable, lands on: "image" if anything in the
 * wrapper, "text" if the paragraph, or the tag and class of whatever else it is.
 */
async function whatIsAt(page: Page, x: number, y: number): Promise<string> {
    return page.evaluate(
        ([x, y]) => {
            const box = document
                .querySelector(".bloom-editable")!
                .getBoundingClientRect();
            const hit = document.elementFromPoint(box.left + x, box.top + y);
            if (!hit) return "nothing";
            if (hit.closest(".bloom-inlineImage")) return "image";
            if (hit.closest("#text")) return "text";
            return `${hit.tagName}.${hit.className}`;
        },
        [x, y],
    );
}

/** Sanity check: there really is text under (x, y), so a click there is a click on text. */
async function expectTextUnder(page: Page, x: number, y: number) {
    const hasText = await page.evaluate(
        ([x, y]) => {
            const box = document
                .querySelector(".bloom-editable")!
                .getBoundingClientRect();
            const node = document.querySelector("#text")!.firstChild as Text;
            const range = document.createRange();
            for (let i = 0; i < node.length; i++) {
                range.setStart(node, i);
                range.setEnd(node, i + 1);
                const r = range.getBoundingClientRect();
                if (
                    r.left <= box.left + x &&
                    box.left + x <= r.right &&
                    r.top <= box.top + y &&
                    box.top + y <= r.bottom
                )
                    return true;
            }
            return false;
        },
        [x, y],
    );
    expect(hasText, `there should be text at (${x}, ${y})`).toBe(true);
}

for (const { dock, imageX } of [
    { dock: "Left", imageX: 80 }, // the image spans 0-160
    { dock: "Right", imageX: 320 }, // the image spans 240-400
    { dock: "Middle", imageX: 200 }, // the image spans 120-280, centered
]) {
    test.describe(`an inline image docked ${dock} and dragged down`, () => {
        test.beforeEach(async ({ page }) => {
            await loadInlineImagePage(page, {
                dock,
                width: "40%",
                offset: `${OFFSET}px`,
                editing: true,
            });
        });

        test("a click on the text above the image reaches the text", async ({
            page,
        }) => {
            const y = OFFSET / 2;
            await expectTextUnder(page, imageX, y);
            expect(await whatIsAt(page, imageX, y)).toBe("text");
        });

        test("a click on the image reaches the image", async ({ page }) => {
            expect(await whatIsAt(page, imageX, OFFSET + 80)).toBe("image");
        });
    });
}
