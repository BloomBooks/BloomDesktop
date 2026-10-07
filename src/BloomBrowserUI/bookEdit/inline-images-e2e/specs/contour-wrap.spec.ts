// These tests lay out inlineImages.less in a browser and check where the text wraps.
//
// The rectangle test describes how inline images wrap today. The contour tests check the
// four facts that wrapping text along the image's transparent silhouette depends on.
// That feature is not built yet, so the tests set --inline-image-contour by hand, which
// is what the future code will do. Each of the four facts turned out different in the
// browser from what we had first assumed, and these tests keep anyone from having to
// work them out again.

import { test, expect } from "playwright/test";
import {
    loadInlineImagePage,
    measureLines,
    measureCharacterWidth,
    leftEdgesAt,
} from "../helpers/inlineImagePage";

// The editable is 400px and the image 40% of it, so the picture is 160x160 (aspect 1),
// sitting 60px down. A 1em gap on a 16px font is 16px, so wrapped text starts at 176.
const IMAGE_WIDTH = 160;
const OFFSET = 60;
const GAP = 16;

// A staircase silhouette: a quarter of the way down the picture the shape reaches a
// quarter of the way across it, and so on. Percentages, so it describes the same
// silhouette whatever size the picture is drawn at.
const STAIRCASE =
    "polygon(0% 0%, 25% 0%, 25% 25%, 50% 25%, 50% 50%, 75% 50%, 75% 75%, 100% 75%, 100% 100%, 0% 100%) content-box";

// The same silhouette mirrored, for an image docked right: the shape's LEFT edge is
// what the text on that side runs into.
const STAIRCASE_MIRRORED =
    "polygon(100% 0%, 100% 100%, 0% 100%, 0% 75%, 25% 75%, 25% 50%, 50% 50%, 50% 25%, 75% 25%, 75% 0%) content-box";

test.describe("inline image wrap geometry", () => {
    test("rectangle wrap: text crosses the offset at full width, then clears image + gap", async ({
        page,
    }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "40%",
            offset: `${OFFSET}px`,
        });
        const lines = await measureLines(page);

        // Above the picture the offset is transparent padding that the wrap shape
        // excludes, so those lines are not indented at all.
        expect(leftEdgesAt(lines, [0, 20, 40])).toEqual([0, 0, 0]);
        // Beside it, every line clears the picture and the gap.
        expect(leftEdgesAt(lines, [60, 100, 200])).toEqual([
            IMAGE_WIDTH + GAP,
            IMAGE_WIDTH + GAP,
            IMAGE_WIDTH + GAP,
        ]);
        // Below it, full width again.
        expect(leftEdgesAt(lines, [240])).toEqual([0]);
    });

    test("contour: text follows the silhouette, and the offset above it stays full width", async ({
        page,
    }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "40%",
            offset: `${OFFSET}px`,
            contour: STAIRCASE,
        });
        const lines = await measureLines(page);

        // Without a contour, this same block indents every one of these lines to 176 (see
        // the previous test), so a staircase here shows the contour is in effect.
        expect(leftEdgesAt(lines, [0, 20, 40])).toEqual([0, 0, 0]);
        expect(
            leftEdgesAt(lines, [60, 80, 100, 120, 140, 160, 180, 200]),
        ).toEqual([40, 40, 80, 80, 120, 120, 160, 160]);
        expect(leftEdgesAt(lines, [220])).toEqual([0]);
    });

    // If the contour resolved against the default reference box (the margin box) it
    // would be stretched over the offset padding and the gap margin, and the staircase
    // would start at the top of the block instead of at the top of the picture.
    test("contour resolves against the content box, which is exactly the picture", async ({
        page,
    }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "40%",
            offset: `${OFFSET}px`,
            contour: STAIRCASE,
        });
        const lines = await measureLines(page);

        // First step of the staircase is a quarter of the way down the PICTURE
        // (y=60..100), not a quarter of the way down the wrapper (y=0..55).
        expect(leftEdgesAt(lines, [0])).toEqual([0]);
        expect(leftEdgesAt(lines, [60])).toEqual([40]);
    });

    // The contour is a polygon in percentages, and not a picture of the silhouette, so
    // that one value stays correct at every size the picture is drawn at.
    test("contour scales with the picture", async ({ page }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "20%", // half as wide as the other tests: an 80x80 picture
            offset: `${OFFSET}px`,
            contour: STAIRCASE,
        });
        const lines = await measureLines(page);

        // Every step is half what it was at 40%, and the staircase ends half as far down.
        expect(leftEdgesAt(lines, [60, 80, 100, 120])).toEqual([
            20, 40, 60, 80,
        ]);
        expect(leftEdgesAt(lines, [140])).toEqual([0]);
    });

    // Once there is a shape, text wraps around the shape and ignores the margin box, so
    // the side margin alone no longer keeps the text away. But the shape, after
    // shape-margin enlarges it, is clipped back to the margin box, so shape-margin alone
    // has no room to grow into. So the CSS sets the gap as both, from one variable.
    test("contour gap: shape-margin holds text off, out to the same edge as a rectangle", async ({
        page,
    }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "40%",
            offset: `${OFFSET}px`,
            contour: STAIRCASE,
            contourMargin: "var(--inline-image-gap)",
        });
        const lines = await measureLines(page);

        // Each step is pushed out by the gap...
        expect(leftEdgesAt(lines, [60, 100])).toEqual([40 + GAP, 80 + GAP]);
        // ...and at the picture's widest the text sits exactly where the rectangle
        // wrap would have put it, so contoured and plain images agree.
        expect(leftEdgesAt(lines, [180])).toEqual([IMAGE_WIDTH + GAP]);
    });

    test("contour works for an image docked right", async ({ page }) => {
        await loadInlineImagePage(page, {
            dock: "Right",
            width: "40%",
            offset: `${OFFSET}px`,
            contour: STAIRCASE_MIRRORED,
        });
        const lines = await measureLines(page);
        const characterWidth = await measureCharacterWidth(page);

        // A line's right edge lands on the last character that fits, so it can fall
        // short of the contour by up to one character.
        const expectRightEdgeNear = (top: number, expected: number) => {
            const line = lines.find((l) => l.top === top);
            if (!line) throw new Error(`no line of text at y=${top}`);
            expect(line.right).toBeLessThanOrEqual(expected + 1);
            expect(line.right).toBeGreaterThan(expected - characterWidth - 1);
        };

        expect(leftEdgesAt(lines, [0, 20, 40])).toEqual([0, 0, 0]);
        expectRightEdgeNear(60, 400 - 40); // shape reaches a quarter across
        expectRightEdgeNear(100, 400 - 80);
        expectRightEdgeNear(140, 400 - 120);
        expectRightEdgeNear(180, 400 - 160);
    });

    // A middle-docked wrapper spans the full width, so percentages of it would not line
    // up with the picture inside it. The CSS therefore ignores the contour there, and
    // setting one must not let text flow beside the wrapper.
    test("the middle band ignores a contour", async ({ page }) => {
        await loadInlineImagePage(page, {
            dock: "Middle",
            width: "40%",
            offset: `${OFFSET}px`,
            contour: STAIRCASE,
        });
        const lines = await measureLines(page);

        // No line is ever indented...
        expect(lines.every((l) => l.left === 0)).toBe(true);
        // ...and no line is beside the image. Text goes above it and resumes below it.
        const insideBand = lines.filter((l) => l.top > OFFSET && l.top < 220);
        expect(insideBand).toEqual([]);
    });
});
