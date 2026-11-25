// A text box whose text runs past its bottom by only a pixel or two is still marked as
// overflowing, whatever the paragraphs' alignment (left, center, right, justify) and whatever the
// box's vertical alignment (top, center, bottom). BL-8014 was overflow going unreported with the
// box aligned to the bottom.
// Automates the manual test "Text Overflow All Alignments" (Test Case ID 376).
//
// The box is made "just" too small the way a person makes it: by dragging the divider between the
// picture and the text of a Basic Text & Image page. Bloom checks a box for overflow when a key
// comes up in it (OverflowChecker.AddOverflowHandlers), so after each change the test makes a small
// change to the text, as the manual case asks. The test waits for the marks to appear; it never
// waits to see that they do not.
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
    getOverflowMarks,
    getTextBoxView,
} from "../helpers/overflowMarks";
import { getPageSize } from "../helpers/pageSize";
import { type VerticalAlignment } from "../helpers/textBoxScrollBars";

test.use({
    collectionSpec: { name: "text-overflow-alignments", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const GROUP = ".bloom-translationGroup";
const LANG = "en";

/**
 * How much of the text was out of sight once the box was made marginally too small, in the page's
 * own pixels. Set by the second test; the later ones check that the overflow is still that.
 */
let marginalHidden: number;

/**
 * How much of the text, in the page's own pixels, the test leaves out of sight: the manual case's
 * "a pixel or two", plus what Bloom's measuring allows. What is out of sight is the bottom of the
 * last line's box, below its descenders. Bloom compares whole-pixel heights, and it leaves out
 * the part of the font's descent that no letter reaches (OverflowChecker.getSelfOverflowAmounts),
 * so it first marks this text at about 3 pixels (at a 24px line height). A change that made it
 * mark later than MARGINAL_OVERFLOW.most fails the test.
 */
const MARGINAL_OVERFLOW = { least: 3.2, most: 4 };

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

/** How much of the box's text is out of sight, in the page's own pixels. */
async function hiddenPagePixels(box: Locator): Promise<number> {
    return (await getTextBoxView(box)).hidden / (await pageScale(box));
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
        await hiddenPagePixels(box),
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
        if ((await hiddenPagePixels(box)) > 0) return;
        if (paragraph > 1) await box.press("Enter");
        await page.keyboard.insertText(
            `Paragraph ${paragraph}: jumpy gypsy quip`,
        );
    }
    throw new Error(
        "A hundred paragraphs did not fill the text box past its bottom, so it is not a box of fixed height.",
    );
}

/** Check that the overflow is still the pixel or two that makeOverflowMarginal left. */
async function expectOverflowStillMarginal(
    box: Locator,
    what: string,
): Promise<void> {
    expect(
        Math.abs((await hiddenPagePixels(box)) - marginalHidden),
        `test setup: ${what}, the text should still run past the box by the same pixel or two`,
    ).toBeLessThanOrEqual(1);
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
 * Size the text box with the divider so that its text runs past the bottom by an amount within
 * MARGINAL_OVERFLOW. Each drag aims at the middle of that range from how much is out of sight
 * now; the page's zoom makes one pixel of the mouse more or less than one of the page's. Returns
 * how much of the text is then out of sight, in the page's pixels.
 */
async function makeOverflowMarginal(page: Page, box: Locator): Promise<number> {
    const target = (MARGINAL_OVERFLOW.least + MARGINAL_OVERFLOW.most) / 2;
    for (let attempt = 1; attempt <= 12; attempt++) {
        const hidden = await hiddenPagePixels(box);
        if (
            hidden >= MARGINAL_OVERFLOW.least &&
            hidden <= MARGINAL_OVERFLOW.most
        )
            return hidden;
        // With nothing out of sight, how far the box is too tall is unknown; a pixel at a time.
        const tooMuch = hidden > 0 ? hidden - target : -1;
        await dragDivider(page, -tooMuch * (await pageScale(box)));
    }
    throw new Error(
        `Twelve drags of the divider never left between ${MARGINAL_OVERFLOW.least} and ` +
            `${MARGINAL_OVERFLOW.most} pixels of the text out of sight; ` +
            `${await hiddenPagePixels(box)} pixels are.`,
    );
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
    // While the box has a scroll bar, which a box with too much text has, Bloom stops aligning the
    // text and keeps the choice as bloom-vertical-align-<alignment>-removed
    // (niceScrollCleanup.ts), so either class shows that the choice took.
    await expect
        .poll(
            async () =>
                editablePageFrame(page)
                    .locator(GROUP)
                    .first()
                    .evaluate((group) => [...group.classList].join(" ")),
            {
                timeout: 15000,
                message: `The text box did not get the ${alignment} vertical alignment.`,
            },
        )
        .toMatch(
            new RegExp(`\\bbloom-vertical-align-${alignment}(-removed)?\\b`),
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
        const box = await textBox(page);
        await step("Type paragraphs until they do not fit", async () => {
            await typeParagraphsPastTheBottom(page, box);
            expect(
                await box.locator("p").count(),
                "test setup: the text should be in several paragraphs",
            ).toBeGreaterThan(2);
        });
        await step(
            "Drag the divider until the text runs past the box by a pixel or two",
            async () => {
                marginalHidden = await makeOverflowMarginal(page, box);
            },
        );
        await step("Check the box is marked as overflowing", async () => {
            const boxNow = await textBox(page);
            await makeSmallChange(page, boxNow);
            await expectOverflowStillMarginal(boxNow, "after the small change");
            await expectMarkedAsOverflowing(
                page,
                boxNow,
                "with the text a pixel or two too long",
            );
        });
    });

    test("marginal overflow stays marked for every paragraph alignment [Test Case ID 376]", async ({
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
                await setParagraphAlignment(page, alignment);
                const box = await textBox(page);
                await makeSmallChange(page, box);
                await expectOverflowStillMarginal(
                    box,
                    `with the paragraphs aligned ${alignment}`,
                );
                await expectMarkedAsOverflowing(
                    page,
                    box,
                    `with the paragraphs aligned ${alignment}`,
                );
            });
        }
    });

    test("marginal overflow stays marked with the box aligned to the center and to the bottom [Test Case ID 376]", async ({
        page,
        step,
    }) => {
        for (const alignment of ["center", "bottom"] as const) {
            await step(`Align the box's text to the ${alignment}`, async () => {
                await setVerticalAlignment(page, alignment);
                const box = await textBox(page);
                await makeSmallChange(page, box);
                await expectOverflowStillMarginal(
                    box,
                    `with the box aligned to the ${alignment}`,
                );
                await expectMarkedAsOverflowing(
                    page,
                    box,
                    `with the box aligned to the ${alignment}`,
                );
            });
        }
    });
});
