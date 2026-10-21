// These tests lay out a cropped inline image with the real inlineImages.less and check two
// things: that the text wraps around the part of the picture that shows, not the whole
// picture, and that the CSS puts the whole picture where the crop says, so the part that
// shows is the part that was asked for. bookEdit/js/inlineImageCrop.ts describes how a crop
// is stored.

import { test, expect } from "playwright/test";
import {
    loadInlineImagePage,
    measureLines,
    leftEdgesAt,
} from "../helpers/inlineImagePage";

// The editable is 400px and the image 40% of it, so it is 160px wide, 60px down. A 1em gap
// on a 16px font is 16px, so text beside the image starts at 176.
const IMAGE_WIDTH = 160;
const OFFSET = 60;
const GAP = 16;

// A square picture with its top half cut away: what shows is 160px wide and 80px tall, so
// the wrapper's aspect ratio is 2. The whole picture is 160px square.
const TOP_HALF_CUT = { top: 50, right: 0, bottom: 0, left: 0 };

// The rectangle the browser draws the whole picture in, relative to the editable, and the
// rectangle of the part that shows.
async function measurePicture(page: import("playwright/test").Page) {
    return page.evaluate(() => {
        const editable = document
            .querySelector(".bloom-editable")!
            .getBoundingClientRect();
        const img = document.querySelector(".bloom-inlineImage img")!;
        // getBoundingClientRect includes the scale and translate, so this is where the whole
        // picture is drawn. offsetWidth and offsetHeight are the box that shows.
        const drawn = img.getBoundingClientRect();
        const shown = img as HTMLElement;
        return {
            drawn: {
                left: Math.round(drawn.left - editable.left),
                top: Math.round(drawn.top - editable.top),
                width: Math.round(drawn.width),
                height: Math.round(drawn.height),
            },
            shown: { width: shown.offsetWidth, height: shown.offsetHeight },
            clipPath: getComputedStyle(
                document.querySelector(".bloom-inlineImage")!,
            ).clipPath,
        };
    });
}

test.describe("cropped inline image", () => {
    test("text wraps around the part that shows, not the whole picture", async ({
        page,
    }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "40%",
            offset: `${OFFSET}px`,
            aspectRatio: "2",
            crop: TOP_HALF_CUT,
        });
        const lines = await measureLines(page);

        // Above the image the text runs full width, as for an uncropped image.
        expect(leftEdgesAt(lines, [0, 20, 40])).toEqual([0, 0, 0]);
        // Beside the 80px that shows, the text clears the image and the gap.
        expect(leftEdgesAt(lines, [60, 80, 100, 120])).toEqual([
            IMAGE_WIDTH + GAP,
            IMAGE_WIDTH + GAP,
            IMAGE_WIDTH + GAP,
            IMAGE_WIDTH + GAP,
        ]);
        // What shows ends at 140, and the wrapper's 0.5em bottom margin takes it to 148, so
        // the line at 140 is still indented. Below that, full width again. The whole picture
        // would reach down to 220, so these lines prove that the cut-away half takes up no
        // room.
        expect(leftEdgesAt(lines, [160, 180, 200])).toEqual([0, 0, 0]);
    });

    test("the whole picture is drawn where the crop puts it, and clipped to what shows", async ({
        page,
    }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "40%",
            offset: `${OFFSET}px`,
            aspectRatio: "2",
            crop: TOP_HALF_CUT,
        });
        const picture = await measurePicture(page);

        expect(picture.shown).toEqual({ width: IMAGE_WIDTH, height: 80 });
        // The whole picture is 160px square, with its top half above the box that shows.
        expect(picture.drawn).toEqual({
            left: 0,
            top: OFFSET - 80,
            width: IMAGE_WIDTH,
            height: IMAGE_WIDTH,
        });
        // The clip leaves out the offset, where the text runs, so the cut-away half is hidden.
        expect(picture.clipPath).toBe(`inset(${OFFSET}px 0px 0px)`);
    });

    test("cuts on all four sides put the picture where those cuts say", async ({
        page,
    }) => {
        // 60% of the width and 50% of the height show, so a 160px wide box shows a picture
        // that is 160 / 0.6 wide and twice the box's height tall.
        await loadInlineImagePage(page, {
            dock: "Right",
            width: "40%",
            aspectRatio: "60 / 50",
            crop: { top: 20, right: 10, bottom: 30, left: 30 },
        });
        const picture = await measurePicture(page);

        const shownHeight = IMAGE_WIDTH / (60 / 50);
        expect(picture.shown).toEqual({
            width: IMAGE_WIDTH,
            height: Math.round(shownHeight),
        });
        const pictureWidth = IMAGE_WIDTH / 0.6;
        const pictureHeight = shownHeight / 0.5;
        // The right-docked box starts at 400 - 160. measurePicture rounds each number to a
        // pixel, so each can be a pixel off the exact value.
        const expected = {
            left: 400 - IMAGE_WIDTH - 0.3 * pictureWidth,
            top: -0.2 * pictureHeight,
            width: pictureWidth,
            height: pictureHeight,
        };
        for (const key of ["left", "top", "width", "height"] as const)
            expect(
                Math.abs(picture.drawn[key] - expected[key]),
                `${key} is ${picture.drawn[key]}, expected ${expected[key]}`,
            ).toBeLessThanOrEqual(1);
    });

    test("an image that is not cropped gets no clip and no scale", async ({
        page,
    }) => {
        await loadInlineImagePage(page, {
            dock: "Left",
            width: "40%",
            offset: `${OFFSET}px`,
        });
        const picture = await measurePicture(page);

        expect(picture.clipPath).toBe("none");
        expect(picture.drawn).toEqual({
            left: 0,
            top: OFFSET,
            width: IMAGE_WIDTH,
            height: IMAGE_WIDTH,
        });
    });
});
