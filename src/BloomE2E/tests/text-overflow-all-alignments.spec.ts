// A text box whose text runs past its bottom by only a pixel or two is still marked as
// overflowing, whatever the paragraphs' alignment (left, center, right, justify) and whatever the
// box's vertical alignment (top, center, bottom). BL-8014 was overflow going unreported with the
// box aligned to the bottom.
// Automates the manual test "Text Overflow All Alignments" (Test Case ID 376).
//
// The box is made "just" too small the way a person makes it: by dragging the divider between the
// picture and the text of a Basic Text & Image page. Bloom checks a box for overflow when a key
// comes up in it (OverflowChecker.AddOverflowHandlers), so after each change the test makes a small
// change to the text, as the manual case asks. Each check starts from a box whose text fits and
// shows no mark, so a mark the test sees is Bloom's answer to the marginal overflow, not one left
// from before; and with nothing overflowing, a center or bottom alignment is really in force (a box
// that overflows gets a scroll bar, and Bloom then takes the alignment off). The test waits for
// marks to appear or go; it never waits a fixed time.
//
// One A5 book with one Basic Text & Image page. The tests are serial: each works on the box the
// one before it left.

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
import {
    chooseAlignment,
    closeFormatDialog,
    getTextBoxFormatting,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
    type Alignment,
} from "../helpers/formatDialog";
import { kEdgeTolerance, right } from "../helpers/geometry";
import { setChangeLayoutMode } from "../helpers/origami";
import {
    emptyTextBox,
    expectNoOverflowMarks,
    getOverflowMarks,
    getTextBoxView,
} from "../helpers/overflowMarks";
import { getPageSize } from "../helpers/pageSize";
import {
    waitForVerticalAlignment,
    type VerticalAlignment,
} from "../helpers/textBoxScrollBars";

test.use({
    collectionSpec: { name: "text-overflow-alignments", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const GROUP = ".bloom-translationGroup";
const LANG = "en";

/**
 * How much of the text, in the page's own pixels, the test leaves out of sight: the manual case's
 * "a pixel or two", plus what Bloom's measuring allows. What is out of sight is the bottom of the
 * last line's box, below its descenders. Bloom compares whole-pixel heights, and it leaves out the
 * part of the font's descent that no letter reaches (OverflowChecker.getSelfOverflowAmounts), so
 * it first marks this text at about 3 pixels (at a 24px line height). A change that made it mark
 * later than MARGINAL_OVERFLOW.most fails the test. The box's height changes in whole pixels, so
 * the range is wider than a pixel, or a drag could step over it.
 */
const MARGINAL_OVERFLOW = { least: 3.5, most: 5 };

/** How far the text of a box that fits is left short of its bottom, in the page's own pixels. */
const ROOM_TO_SPARE = 8;

/** The book's one content page. Set by the first test. */
let textPage: IBookPage;

/** The page's text box, clicked into. Does not go to the page, so nothing reloads it. */
async function textBox(page: Page): Promise<Locator> {
    return clickInGroup(page, GROUP, LANG);
}

/** How many screen pixels one of the page's pixels is, at the Edit tab's zoom. */
async function pageScale(box: Locator): Promise<number> {
    return box.evaluate(
        (e) =>
            e.getBoundingClientRect().height / (e as HTMLElement).offsetHeight,
    );
}

/**
 * How far the box's text runs past its bottom, in the page's own pixels: negative when it fits,
 * by how much room is left. Measured as getTextBoxView measures what is out of sight.
 */
async function overflowPagePixels(box: Locator): Promise<number> {
    return box.evaluate((element) => {
        const e = element as HTMLElement;
        const style = getComputedStyle(e);
        const scale = e.getBoundingClientRect().height / e.offsetHeight;
        const text = e.ownerDocument.createRange();
        text.selectNodeContents(e);
        const textHeight =
            text.getBoundingClientRect().height / scale +
            (parseFloat(style.paddingTop) || 0) +
            (parseFloat(style.paddingBottom) || 0);
        return textHeight - e.clientHeight;
    });
}

/**
 * Make a small change to the box's last paragraph, as the manual case asks, so that Bloom checks
 * it for overflow again: a space typed at the end of the text and taken out again. Both are real
 * keys, and Bloom checks a box when a key comes up in it.
 */
async function makeSmallChange(page: Page, box: Locator): Promise<void> {
    const textBefore = await box.innerText();
    await box.press("Control+End");
    await page.keyboard.press("Space");
    await page.keyboard.press("Backspace");
    await expect(
        box,
        "test setup: typing a space and deleting it should leave the text as it was",
    ).toHaveText(textBefore, { useInnerText: true, timeout: 15000 });
}

/**
 * Check that Bloom marks the box as overflowing: one label saying "Overflowing", on the box. The
 * label is what a person sees. Where exactly it sits, and the fade and wash that go with it, are
 * overflow-marks.spec.ts's business.
 */
async function expectMarkedAsOverflowing(
    page: Page,
    box: Locator,
    what: string,
): Promise<void> {
    expect(
        await overflowPagePixels(box),
        `test setup: ${what}, some of the text should be out of sight`,
    ).toBeGreaterThan(0);
    await expect(async () => {
        const view = await getTextBoxView(box);
        const marks = await getOverflowMarks(page);
        expect(
            marks.labels.map((l) => l.text),
            `${what}, the box should be marked with one label saying "Overflowing"`,
        ).toEqual(["Overflowing"]);
        const label = marks.labels[0];
        expect(
            label.rect.x,
            `${what}, the "Overflowing" label should start inside the box`,
        ).toBeGreaterThanOrEqual(view.rect.x - kEdgeTolerance);
        expect(
            right(label.rect),
            `${what}, the "Overflowing" label should end inside the box`,
        ).toBeLessThanOrEqual(right(view.rect) + kEdgeTolerance);
        expect(
            label.lineY,
            `${what}, the "Overflowing" label should be on the box`,
        ).toBeGreaterThanOrEqual(view.rect.y - kEdgeTolerance);
        expect(
            label.lineY,
            `${what}, the "Overflowing" label should be on the box`,
        ).toBeLessThanOrEqual(view.rect.y + view.rect.height + kEdgeTolerance);
    }).toPass({ timeout: 15000 });
}

/**
 * Type paragraphs into the box until the last one does not fit. Each has letters that reach below
 * the line (g, j, p, q, y), so the bottom of the last line is text. Bloom does not count the space
 * a font keeps below its letters as overflow when no letter reaches into it (BL-6338), so with
 * text such as "Line 1" a few pixels of a line out of sight are rightly not marked.
 */
async function typeParagraphsPastTheBottom(
    page: Page,
    box: Locator,
): Promise<void> {
    await emptyTextBox(box);
    for (let paragraph = 1; paragraph <= 100; paragraph++) {
        if ((await overflowPagePixels(box)) > 0) return;
        if (paragraph > 1) await box.press("Enter");
        await page.keyboard.insertText(
            `Paragraph ${paragraph}: jumpy gypsy quip`,
        );
    }
    throw new Error(
        "A hundred paragraphs did not fill the text box past its bottom, so it is not a box of fixed height.",
    );
}

/**
 * Drag the divider between the picture and the text by `dy` pixels on screen (up is negative,
 * which makes the text box taller), the way a person does, holding Ctrl so that the divider does
 * not snap to one of its preset positions.
 */
async function dragDivider(page: Page, dy: number): Promise<void> {
    const divider = editablePageFrame(page).locator(
        ".split-pane-divider.horizontal-divider",
    );
    await expect(
        divider,
        "test setup: a Basic Text & Image page should have one divider between its picture and its text",
    ).toHaveCount(1);
    const box = await divider.boundingBox();
    if (!box)
        throw new Error("The divider between picture and text is not showing.");
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.keyboard.down("Control");
    try {
        await page.mouse.down();
        await page.mouse.move(x, y + dy, { steps: 4 });
        await page.mouse.up();
    } finally {
        await page.keyboard.up("Control");
    }
}

/**
 * Drag the divider until the box's text runs past its bottom by between `least` and `most` of the
 * page's pixels (negative numbers: falls short of it by that much). Each drag aims at the middle
 * of the range from where the text is now; the page's zoom makes one pixel of the mouse more or
 * less than one of the page's.
 */
async function dragUntilOverflowIsBetween(
    page: Page,
    box: Locator,
    least: number,
    most: number,
    what: string,
): Promise<void> {
    const target = (least + most) / 2;
    for (let drag = 0; ; drag++) {
        const overflow = await overflowPagePixels(box);
        if (overflow >= least && overflow <= most) return;
        if (drag === 12)
            throw new Error(
                `Twelve drags of the divider never left ${what}: the text runs past the box's ` +
                    `bottom by ${overflow} pixels, not between ${least} and ${most}.`,
            );
        await dragDivider(page, -(overflow - target) * (await pageScale(box)));
    }
}

/**
 * Make the box tall enough for all its text, with the divider, and wait until Bloom shows no mark,
 * so that whatever mark comes next is Bloom's answer to what the test does next.
 */
async function makeTextFit(page: Page): Promise<void> {
    const box = await textBox(page);
    await dragUntilOverflowIsBetween(
        page,
        box,
        -ROOM_TO_SPARE - 2,
        -ROOM_TO_SPARE + 2,
        "room to spare below the text",
    );
    await makeSmallChange(page, box);
    // The box may still be scrolled to its last line, and Bloom's first check after it has grown
    // can still find it overflowing (see the ENHANCE note in
    // OverflowChecker.AdjustSizeAndMarkSelfOverflow); it settles a moment later.
    await expectNoOverflowMarks(
        page,
        "test setup: with room to spare below the text, Bloom should show no overflow mark",
    );
}

/**
 * Shorten the box with the divider until its text runs past the bottom by MARGINAL_OVERFLOW, make
 * the small change the manual case asks for, and check that Bloom marks the box as overflowing.
 * Expects a box whose text fits and has no mark, as makeTextFit leaves it.
 */
async function expectMarginalOverflowMarked(
    page: Page,
    what: string,
): Promise<void> {
    const box = await textBox(page);
    await dragUntilOverflowIsBetween(
        page,
        box,
        MARGINAL_OVERFLOW.least,
        MARGINAL_OVERFLOW.most,
        "the text running past the box by a pixel or two",
    );
    await makeSmallChange(page, box);
    expect(
        await overflowPagePixels(box),
        `test setup: ${what}, the small change should leave the text running past the box by a pixel or two`,
    ).toBeLessThanOrEqual(MARGINAL_OVERFLOW.most);
    await expectMarkedAsOverflowing(page, box, what);
}

/** Choose a paragraph alignment for the box with the Format dialog's Paragraph tab. */
async function setParagraphAlignment(
    page: Page,
    alignment: Alignment,
): Promise<void> {
    await textBox(page);
    await scrollFormatGearIntoView(page);
    await openFormatDialog(page);
    try {
        await showFormatDialogTab(page, "paragraph");
        await chooseAlignment(page, alignment);
    } finally {
        await closeFormatDialog(page);
    }
    await expect
        .poll(
            async () =>
                (await getTextBoxFormatting(page)).find((b) => b.lang === LANG)
                    ?.alignment,
            {
                timeout: 15000,
                message: `The box's paragraphs did not get the ${alignment} alignment.`,
            },
        )
        .toBe(alignment);
}

/** Set the box's vertical alignment with the Text Box Properties dialog of Change Layout. */
async function setVerticalAlignment(
    page: Page,
    alignment: VerticalAlignment,
): Promise<void> {
    await setChangeLayoutMode(page, true);
    const frame = editablePageFrame(page);
    await frame.locator(".textBox-identifier .formatButton").first().click();
    const dialog = frame.locator("#text-properties-dialog");
    await dialog.waitFor({ state: "visible", timeout: 15000 });
    // The dialog opens on its Language tab; the alignment buttons are on the next one. The tab is
    // found by its localization id, so the UI language does not matter.
    await dialog
        .locator('h2.tab[data-i18n="EditTab.TextBoxProperties.Alignment"]')
        .click();
    await dialog.locator(`#align-${alignment}`).click();
    await expect(dialog.locator(`#align-${alignment}`)).toHaveClass(
        /selectedIcon/,
    );
    // A click outside the dialog closes it.
    await frame.locator("body").click({ position: { x: 2, y: 2 } });
    await expect(dialog).toBeHidden({ timeout: 15000 });
    await setChangeLayoutMode(page, false);
    // The text fits, so the box has no scroll bar and the alignment is in force. (A box with a
    // scroll bar keeps the choice only as bloom-vertical-align-<alignment>-removed; see
    // niceScrollCleanup.ts.)
    await waitForVerticalAlignment(
        page,
        GROUP,
        alignment,
        `With its text fitting, the text box should be aligned to the ${alignment}.`,
    );
}

test.describe("Text Overflow All Alignments", () => {
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

    test("text that runs past the box by a pixel or two is marked as overflowing [Test Case ID 376]", async ({
        page,
        step,
    }) => {
        await step("Type paragraphs until they do not fit", async () => {
            const box = await textBox(page);
            await typeParagraphsPastTheBottom(page, box);
            expect(
                await box.locator("p").count(),
                "test setup: the text should be in several paragraphs",
            ).toBeGreaterThan(2);
        });
        await step("Drag the divider until the text fits", async () => {
            await makeTextFit(page);
        });
        await step(
            "Drag the divider until the text runs past the box by a pixel or two",
            async () => {
                await expectMarginalOverflowMarked(
                    page,
                    "with the text a pixel or two too long",
                );
            },
        );
    });

    test("marginal overflow is marked for every paragraph alignment [Test Case ID 376]", async ({
        page,
        step,
    }) => {
        for (const alignment of [
            "center",
            "right",
            "justify",
            "left",
        ] satisfies Alignment[]) {
            await step(`Align the paragraphs ${alignment}`, async () => {
                await makeTextFit(page);
                await setParagraphAlignment(page, alignment);
                await expectMarginalOverflowMarked(
                    page,
                    `with the paragraphs aligned ${alignment}`,
                );
            });
        }
    });

    test("marginal overflow is marked with the box aligned to the center and to the bottom [Test Case ID 376]", async ({
        page,
        step,
    }) => {
        for (const alignment of ["center", "bottom"] as const) {
            await step(`Align the box's text to the ${alignment}`, async () => {
                await makeTextFit(page);
                await setVerticalAlignment(page, alignment);
                await expectMarginalOverflowMarked(
                    page,
                    `with the box aligned to the ${alignment}`,
                );
            });
        }
    });
});
