// A text box with more text than fits keeps its scroll bar whatever the page size, but Bloom marks
// it as overflowing only on paper page sizes: on the Device and Ebook sizes a reader scrolls the
// text, so too much text is not a problem there.
// Automates the manual test "Device Overflow Scroll Bars" (Test Case ID 377).
//
// The manual case names the marks Bloom used to show (a red box, a tooltip, a "!" on the page's
// thumbnail). Since BL-17109 the marks are an "Overflowing" label where the text is cut off and a
// red zigzag on the thumbnail (see overflow-marks.spec.ts); this test checks for those.
//
// The thumbnail's mark is read only after the page has been clicked in the page list, because
// that is when Bloom updates it (the case's own automation note asks for exactly that).
//
// One A5 book with one Basic Text & Image page. The tests are serial.

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import { copyToClipboard, pasteFromClipboard } from "../helpers/clipboard";
import {
    expectNoOverflowMarks,
    expectTextBoxMarkedAsOverflowing,
    fillTextBoxPastItsBottom,
    getTextBoxView,
    getThumbnailOverflow,
    scrollTextBox,
} from "../helpers/overflowMarks";
import { getPageSize, setPageSize } from "../helpers/pageSize";
import { selectPage } from "../helpers/pageThumbnails";
import { setTextBoxVerticalAlignment } from "../helpers/textBoxProperties";
import {
    typeAtEndOfGroup,
    waitForTextBoxScrollBar,
    type VerticalAlignment,
} from "../helpers/textBoxScrollBars";

test.use({
    collectionSpec: { name: "device-overflow-scroll-bars", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const GROUP = ".bloom-translationGroup";
const LANG = "en";
/** The Device and Ebook sizes the case goes through, on which overflow is not marked. */
const SCROLLING_SIZES = [
    "Device16x9Portrait",
    "Ebook2x3Portrait",
    "Device16x9Landscape",
    "Ebook7x5Landscape",
];

/** The book's one content page. Set by the first test. */
let textPage: IBookPage;

async function textBox(page: Page): Promise<Locator> {
    await goToPage(page, textPage.id);
    return clickInGroup(page, GROUP, LANG);
}

/** Expect the text box to have its scroll bar. The case asks for it at every step. */
async function expectScrollBar(page: Page): Promise<void> {
    await waitForTextBoxScrollBar(page, GROUP, LANG, true);
}

/**
 * The text box of the page being shown, clicked into. Unlike textBox it does not go to the page,
 * because going there reloads it, and loading a page works out its overflow again: a mark that
 * typing failed to update would then come back and hide the failure.
 */
async function textBoxAsItIs(page: Page): Promise<Locator> {
    return clickInGroup(page, GROUP, LANG);
}

/**
 * Expect the box marked as overflowing and its scroll bar, as the page is now, and then, once the
 * page has been clicked in the page list, the zigzag on its thumbnail.
 */
async function expectMarkedOverflowing(
    page: Page,
    what: string,
): Promise<void> {
    const box = await textBoxAsItIs(page);
    await expectScrollBar(page);
    await scrollTextBox(page, box, "top");
    await expectTextBoxMarkedAsOverflowing(page, box, what);
    await selectPage(page, textPage.id);
    await expect
        .poll(
            async () => (await getThumbnailOverflow(page, textPage.id)).zigzag,
            {
                timeout: 15000,
                message: `${what}: the page's thumbnail should have the overflow zigzag.`,
            },
        )
        .toBe(true);
}

/**
 * Expect no overflow mark on the page as it is now, though the box still overflows, and none on
 * its thumbnail once the page has been clicked in the page list.
 */
async function expectNotMarked(page: Page, what: string): Promise<void> {
    const box = await textBoxAsItIs(page);
    expect(
        (await getTextBoxView(box)).hidden,
        `test setup: ${what}, the box should still have text out of sight`,
    ).toBeGreaterThan(1);
    await expectScrollBar(page);
    await expectNoOverflowMarks(
        page,
        `${what}: the page should show no overflow mark.`,
    );
    await selectPage(page, textPage.id);
    await expect
        .poll(
            async () => (await getThumbnailOverflow(page, textPage.id)).zigzag,
            {
                timeout: 15000,
                message: `${what}: the page's thumbnail should not have the overflow zigzag.`,
            },
        )
        .toBe(false);
}

/** Set the text box's vertical alignment with the Text Box Properties dialog of Change Layout. */
async function setVerticalAlignment(
    page: Page,
    alignment: VerticalAlignment,
): Promise<void> {
    await goToPage(page, textPage.id);
    await setTextBoxVerticalAlignment(page, alignment);
    // While the box has its scroll bar, Bloom stops aligning the text and keeps the setting as
    // bloom-vertical-align-<alignment>-removed (niceScrollCleanup.ts), so either class shows that
    // the setting took.
    await expect
        .poll(
            async () =>
                editablePageFrame(page)
                    .locator(GROUP)
                    .first()
                    .evaluate((group) => [...group.classList].join(" ")),
            {
                timeout: 15000,
                message: `The text box did not get the ${alignment} alignment.`,
            },
        )
        .toMatch(
            new RegExp(`\\bbloom-vertical-align-${alignment}(-removed)?\\b`),
        );
}

test.describe("Device Overflow Scroll Bars", () => {
    test("builds an A5 book with a Basic Text & Image page", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        expect(await getPageSize(page)).toBe("A5Portrait");
    });

    test("too much text on an A5 page is marked as overflowing and has a scroll bar, also after more is typed and pasted [Test Case ID 377]", async ({
        page,
    }) => {
        const box = await textBox(page);
        await expectNoOverflowMarks(
            page,
            "test setup: an empty box should not be marked",
        );
        await fillTextBoxPastItsBottom(page, box, "lines of text", 8);
        await expectMarkedOverflowing(
            page,
            "with the box filled past its bottom",
        );

        await typeAtEndOfGroup(
            page,
            GROUP,
            LANG,
            "\nAnother paragraph of ",
            "text.",
        );
        await expectMarkedOverflowing(
            page,
            "after another paragraph was typed",
        );

        await textBox(page);
        await copyToClipboard(page, {
            text: "A pasted paragraph that makes the text longer still.",
        });
        await pasteFromClipboard(page);
        await expect(box).toContainText("A pasted paragraph", {
            timeout: 15000,
        });
        await expectMarkedOverflowing(page, "after more text was pasted");
    });

    test("on Device and Ebook sizes the marks go, and on A6 they come back; the scroll bar stays [Test Case ID 377]", async ({
        page,
    }) => {
        await setPageSize(page, "Device16x9Portrait");
        await expectNotMarked(page, "at Device 16x9 Portrait");

        await setPageSize(page, "A6Portrait");
        await expectMarkedOverflowing(page, "at A6 Portrait");

        for (const size of SCROLLING_SIZES.slice(1)) {
            await setPageSize(page, size);
            await expectNotMarked(page, `at ${size}`);
        }
    });

    test("centering or bottom-aligning the text on an Ebook page does not bring the marks back [Test Case ID 377]", async ({
        page,
    }) => {
        expect(await getPageSize(page)).toBe("Ebook7x5Landscape");
        for (const alignment of ["center", "bottom"] as const) {
            await setVerticalAlignment(page, alignment);
            await expectNotMarked(page, `with the text aligned ${alignment}`);
        }
    });
});
