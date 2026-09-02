// Where the Format dialog appears when the format gear is clicked: close to the gear, and wholly
// on the screen even when the gear is at the very edge of it; that a click outside the dialog
// closes it; and that it can be dragged. Automates the manual test "Format Gear Positioning"
// (Test Case ID 356).
//
// The page frame's viewport is the screen here. Bloom keeps the dialog inside that viewport, and
// the gear can only ever be scrolled off the edge of it, so every position is measured against it
// (see helpers/formatDialog.ts).
//
// The tests are serial: each starts from the state the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    clickOutsideFormatDialog,
    dragFormatDialog,
    getFormatDialogPlacement,
    isFormatDialogOpen,
    openFormatDialog,
    scrollFormatGearIntoView,
    scrollFormatGearPartlyIntoView,
    zoomUntilFormatGearIsOutOfView,
    type IFormatDialogPlacement,
    type IRect,
} from "../helpers/formatDialog";
import { getZoom, setZoom } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "format-gear-positioning", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

/** The one text box on the page this file builds: the box under the picture. */
const TEXT_BOX = ".bloom-translationGroup";
const LANGUAGE = "en";

/**
 * How far, in pixels, the dialog may be from the gear and still count as close to it. Bloom places
 * the dialog 30px to the right of the gear and level with it, so the gap is normally about 30;
 * this leaves room for the nudge that keeps the dialog on the screen.
 */
const NEAR_PX = 50;

/** How far the drag test moves the dialog, and how far off that the dialog may land. */
const DRAG = { dx: -120, dy: -80 };
const DRAG_TOLERANCE_PX = 15;

/** The zoom Bloom started with, put back at the end so the setting does not leak out of the run. */
let startingZoom: number;

/** The gap between two rectangles along each axis: zero where they overlap on that axis. */
const gapBetween = (a: IRect, b: IRect) => ({
    x: Math.max(0, a.left - b.right, b.left - a.right),
    y: Math.max(0, a.top - b.bottom, b.top - a.bottom),
});

/** Assert that the Format dialog is open and close to the format gear. */
const expectDialogCloseToGear = (placement: IFormatDialogPlacement) => {
    expect(placement.dialog, "The Format dialog is not open.").toBeDefined();
    expect(
        placement.gear,
        "No text box is showing its format gear.",
    ).toBeDefined();
    const gap = gapBetween(placement.dialog!, placement.gear!);
    expect(
        gap.x,
        `The dialog is ${Math.round(gap.x)}px to the side of the gear.`,
    ).toBeLessThanOrEqual(NEAR_PX);
    expect(
        gap.y,
        `The dialog is ${Math.round(gap.y)}px above or below the gear.`,
    ).toBeLessThanOrEqual(NEAR_PX);
};

/** Assert that the Format dialog is open and wholly on the screen. */
const expectDialogEntirelyOnScreen = (placement: IFormatDialogPlacement) => {
    expect(placement.dialog, "The Format dialog is not open.").toBeDefined();
    const { dialog, viewport } = placement;
    expect(
        dialog!.left,
        "The dialog runs off the left edge.",
    ).toBeGreaterThanOrEqual(0);
    expect(
        dialog!.top,
        "The dialog runs off the top edge.",
    ).toBeGreaterThanOrEqual(0);
    expect(
        dialog!.right,
        "The dialog runs off the right edge.",
    ).toBeLessThanOrEqual(viewport.width);
    expect(
        dialog!.bottom,
        "The dialog runs off the bottom edge.",
    ).toBeLessThanOrEqual(viewport.height);
};

test.describe("Format gear positioning", () => {
    test.afterAll(async ({ page }) => {
        if (startingZoom !== undefined) await setZoom(page, startingZoom);
    });

    test("builds a book with a text box at the bottom of a page", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        startingZoom = (await getZoom(page)).zoom;

        await clickInGroup(page, TEXT_BOX, LANGUAGE);
        await scrollFormatGearIntoView(page);
        // Sanity check the page the rest of the file rests on: the gear, and so the box it belongs
        // to, is in the lower half of the view.
        const { gear, viewport } = await getFormatDialogPlacement(page);
        expect(gear!.top).toBeGreaterThan(viewport.height / 2);
    });

    test("the Format dialog opens close to the gear [Test Case ID 356]", async ({
        page,
    }) => {
        await openFormatDialog(page);
        const placement = await getFormatDialogPlacement(page);
        expectDialogCloseToGear(placement);
        expectDialogEntirelyOnScreen(placement);
    });

    test("a click outside the Format dialog closes it [Test Case ID 356]", async ({
        page,
    }) => {
        expect(await isFormatDialogOpen(page)).toBe(true);
        await clickOutsideFormatDialog(page);
        expect(await isFormatDialogOpen(page)).toBe(false);
    });

    test("with the gear half off the screen, the dialog opens close to it and wholly on the screen [Test Case ID 356]", async ({
        page,
    }) => {
        await zoomUntilFormatGearIsOutOfView(page);
        await scrollFormatGearPartlyIntoView(page);
        await openFormatDialog(page);
        const placement = await getFormatDialogPlacement(page);
        expectDialogCloseToGear(placement);
        expectDialogEntirelyOnScreen(placement);
    });

    test("the Format dialog can be dragged [Test Case ID 356]", async ({
        page,
    }) => {
        const before = (await getFormatDialogPlacement(page)).dialog!;
        await dragFormatDialog(page, DRAG.dx, DRAG.dy);
        const after = (await getFormatDialogPlacement(page)).dialog!;
        expect(
            Math.abs(after.left - before.left - DRAG.dx),
        ).toBeLessThanOrEqual(DRAG_TOLERANCE_PX);
        expect(Math.abs(after.top - before.top - DRAG.dy)).toBeLessThanOrEqual(
            DRAG_TOLERANCE_PX,
        );
    });
});
