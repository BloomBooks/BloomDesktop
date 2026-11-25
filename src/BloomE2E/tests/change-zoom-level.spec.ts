// The Edit tab's zoom: the − and + buttons in the top bar and Ctrl+mouse wheel over the page change
// it, it stops at 30% and 300%, it scales the page being edited and nothing around it, and Bloom
// keeps it for the next time it runs. Automates the manual test "Change Zoom Level"
// (Test Case ID 353).
//
// The tests are serial: each starts from the book, and the zoom, that the one before it left.

import { expect, test } from "../fixtures/bloomTest";
import { editBook, makeBookFromTemplate } from "../helpers/bookMaking";
import { showToolbox } from "../helpers/toolbox";
import {
    clickZoomButton,
    clickZoomButtonAtEndOfRange,
    expectPageDrawnAtZoom,
    getSizeOfPageBeingEdited,
    getSizesOfUnzoomedParts,
    getZoom,
    getZoomShownInTopBar,
    setZoom,
    turnWheelToZoom,
} from "../helpers/workspace";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "change-zoom-level", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The range the manual test names.
const kSmallestZoom = 30;
const kLargestZoom = 300;
// What one click of a zoom button, or one notch of the wheel, changes the zoom by.
const kStep = 10;

let bookFolder: string;

// How large the page being edited and the parts around it were at 100%, before any zooming.
let pageSizeAtFullSize: { width: number; height: number };
let partsAroundPage: Awaited<ReturnType<typeof getSizesOfUnzoomedParts>>;

/**
 * Check that the Edit tab is at `zoom` in every place a person sees it: Bloom reports it, the top
 * bar shows it, the page being edited is drawn at it, and so is that much larger or smaller than at
 * 100%, while the top bar, the page list and the toolbox are the size they were.
 */
async function expectZoomedTo(page: Page, zoom: number, when: string) {
    expect((await getZoom(page)).zoom, `${when}: the zoom Bloom reports`).toBe(
        zoom,
    );
    await expect
        .poll(() => getZoomShownInTopBar(page), {
            message: `${when}: the zoom the top bar shows`,
        })
        .toBe(zoom);
    await expectPageDrawnAtZoom(page, zoom);
    const pageSize = await getSizeOfPageBeingEdited(page);
    // Within a pixel: layout rounds a scaled page to device pixels.
    expect(
        Math.abs(pageSize.width - (pageSizeAtFullSize.width * zoom) / 100),
        `${when}: the page being edited should be ${zoom}% as wide as at 100% (${pageSizeAtFullSize.width}px), and is ${pageSize.width}px`,
    ).toBeLessThanOrEqual(1);
    expect(
        Math.abs(pageSize.height - (pageSizeAtFullSize.height * zoom) / 100),
        `${when}: the page being edited should be ${zoom}% as high as at 100% (${pageSizeAtFullSize.height}px), and is ${pageSize.height}px`,
    ).toBeLessThanOrEqual(1);
    expect(
        await getSizesOfUnzoomedParts(page),
        `${when}: the top bar, the page list and the toolbox should be the size they were at 100%`,
    ).toEqual(partsAroundPage);
}

test.describe("the Edit tab's zoom", () => {
    test("makes a book and measures it at 100% [Test Case ID 353]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await showToolbox(page);
        const range = await getZoom(page);
        // A new settings folder means Bloom starts at its default zoom.
        expect(range.zoom).toBe(100);
        expect(range.minZoom).toBe(kSmallestZoom);
        expect(range.maxZoom).toBe(kLargestZoom);
        await expectPageDrawnAtZoom(page, 100);
        pageSizeAtFullSize = await getSizeOfPageBeingEdited(page);
        partsAroundPage = await getSizesOfUnzoomedParts(page);
        await expectZoomedTo(page, 100, "Before any zooming");
    });

    test("the − and + buttons zoom the page being edited, and nothing around it [Test Case ID 353]", async ({
        page,
    }) => {
        expect(await clickZoomButton(page, "in")).toBe(100 + kStep);
        await expectZoomedTo(page, 100 + kStep, "After one click of +");
        expect(await clickZoomButton(page, "in")).toBe(100 + 2 * kStep);
        await expectZoomedTo(page, 100 + 2 * kStep, "After two clicks of +");

        expect(await clickZoomButton(page, "out")).toBe(100 + kStep);
        expect(await clickZoomButton(page, "out")).toBe(100);
        expect(await clickZoomButton(page, "out")).toBe(100 - kStep);
        await expectZoomedTo(page, 100 - kStep, "After three clicks of −");

        expect(await clickZoomButton(page, "in")).toBe(100);
        await expectZoomedTo(page, 100, "After a click of + back to 100%");
    });

    test("Ctrl+mouse wheel zooms the page being edited, and nothing around it [Test Case ID 353]", async ({
        page,
    }) => {
        expect((await getZoom(page)).zoom).toBe(100);

        expect(await turnWheelToZoom(page, 1)).toBe(100 + kStep);
        await expectZoomedTo(page, 100 + kStep, "After one notch in");
        expect(await turnWheelToZoom(page, -1)).toBe(100);
        expect(await turnWheelToZoom(page, -1)).toBe(100 - kStep);
        await expectZoomedTo(page, 100 - kStep, "After two notches out");

        // A quick spin, which once froze Bloom (BL-16762).
        expect(await turnWheelToZoom(page, 6)).toBe(100 + 5 * kStep);
        await expectZoomedTo(page, 100 + 5 * kStep, "After a spin in");
        expect(await turnWheelToZoom(page, -5)).toBe(100);
        await expectZoomedTo(page, 100, "After a spin back out to 100%");
    });

    test("the zoom stops at 30% and at 300%, and going on past either does no harm [Test Case ID 353]", async ({
        page,
    }) => {
        // The smallest zoom, with the button and then with the wheel.
        await setZoom(page, kSmallestZoom + kStep);
        expect(await clickZoomButton(page, "out")).toBe(kSmallestZoom);
        await expectZoomedTo(page, kSmallestZoom, "At the smallest zoom");
        expect(await clickZoomButtonAtEndOfRange(page, "out")).toBe(
            kSmallestZoom,
        );
        expect(await clickZoomButtonAtEndOfRange(page, "out")).toBe(
            kSmallestZoom,
        );
        await expectZoomedTo(
            page,
            kSmallestZoom,
            "After clicking − at the smallest zoom",
        );
        // One step back up shows that the clicks past the end left the zoom at the end.
        expect(await clickZoomButton(page, "in")).toBe(kSmallestZoom + kStep);

        expect(await turnWheelToZoom(page, -1)).toBe(kSmallestZoom);
        expect(await turnWheelToZoom(page, -1)).toBe(kSmallestZoom);
        expect(await turnWheelToZoom(page, -8)).toBe(kSmallestZoom);
        await expectZoomedTo(
            page,
            kSmallestZoom,
            "After turning the wheel out at the smallest zoom",
        );
        expect(await turnWheelToZoom(page, 1)).toBe(kSmallestZoom + kStep);
        await expectZoomedTo(
            page,
            kSmallestZoom + kStep,
            "After one notch in from the smallest zoom",
        );

        // The largest zoom, with the button and then with the wheel.
        await setZoom(page, kLargestZoom - kStep);
        expect(await clickZoomButton(page, "in")).toBe(kLargestZoom);
        await expectZoomedTo(page, kLargestZoom, "At the largest zoom");
        expect(await clickZoomButtonAtEndOfRange(page, "in")).toBe(
            kLargestZoom,
        );
        expect(await clickZoomButtonAtEndOfRange(page, "in")).toBe(
            kLargestZoom,
        );
        await expectZoomedTo(
            page,
            kLargestZoom,
            "After clicking + at the largest zoom",
        );
        expect(await clickZoomButton(page, "out")).toBe(kLargestZoom - kStep);

        expect(await turnWheelToZoom(page, 1)).toBe(kLargestZoom);
        expect(await turnWheelToZoom(page, 1)).toBe(kLargestZoom);
        expect(await turnWheelToZoom(page, 8)).toBe(kLargestZoom);
        await expectZoomedTo(
            page,
            kLargestZoom,
            "After turning the wheel in at the largest zoom",
        );
        expect(await turnWheelToZoom(page, -1)).toBe(kLargestZoom - kStep);
        await expectZoomedTo(
            page,
            kLargestZoom - kStep,
            "After one notch out from the largest zoom",
        );
    });

    test("Bloom keeps the zoom after it is closed and opened again [Test Case ID 353]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // A zoom that is neither the default nor an end of the range, given with the button and
        // then, after a first restart, with the wheel.
        await setZoom(page, 120);
        expect(await clickZoomButton(page, "in")).toBe(130);

        let shell = await bloomApp.quitAndRestart();
        await editBook(shell, bookFolder);
        await expectZoomedTo(shell, 130, "After closing and opening Bloom");

        expect(await turnWheelToZoom(shell, -6)).toBe(70);
        shell = await bloomApp.quitAndRestart();
        await editBook(shell, bookFolder);
        await expectZoomedTo(
            shell,
            70,
            "After closing and opening Bloom a second time",
        );
    });
});
