// The marks the Edit tab draws where text does not fit (BL-17109): on a text box with more text
// than it shows, a label saying "Overflowing" on the line where the text is cut off, the last
// line that fits fading out, and, when the box is scrolled, a red wash over the lines below that
// line; on a page whose boxes together run past its margin, a red line at the margin with a label
// saying "Page overflowing" and a striped band over what is past it; and in the page list, a red
// zigzag along the bottom of the thumbnail of a page that has either.
//
// Bloom draws the marks on the page in a layer beside the page (overflowIndicators.ts), so that
// none of them is saved. Everything here reads that layer and compares it to the box or the
// margin it is about. A test never needs the layer's markup.
//
// Bloom checks a box for overflow when a key comes up in it or something is pasted
// (OverflowChecker.AddOverflowHandlers), not when text is merely inserted. So fillTextBoxPastItsBottom
// ends every line with a real Enter. (AUTOMATION-DEBT.md: "Undo and overflow checks in a table
// cell need real key presses".)

import * as fs from "fs";
import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { bookHtmlPath } from "./bookHtml";
import { bottom, kEdgeTolerance, right, type IRect } from "./geometry";
import { pressKeys, type Key } from "./keys";
import { pageListFrame } from "./pageThumbnails";

const LAYER = "#bloom-overflow-indicator-layer";

/** A label Bloom has drawn on the page: what it says, and the line it sits on. */
export interface IOverflowLabel {
    /** The words on the label, e.g. "Overflowing" or "Page overflowing", without the arrow. */
    text: string;
    /** The y of the horizontal line the label is centered on. */
    lineY: number;
    /** Where the label is. */
    rect: IRect;
}

/** Every overflow mark Bloom is showing on the page being edited, in the page frame's coordinates. */
export interface IOverflowMarks {
    labels: IOverflowLabel[];
    /** The fade over the last line that fits in a box that is not scrolled. */
    fades: IRect[];
    /** The red wash over the lines of a scrolled box that do not fit. */
    washes: IRect[];
    /** The striped band, with its red top line, over what is past the page's margin. */
    pageBands: IRect[];
}

/** Read the overflow marks Bloom is showing on the page being edited, as they are right now. */
export async function getOverflowMarks(page: Page): Promise<IOverflowMarks> {
    return editablePageFrame(page).evaluate((layerSelector) => {
        const rectsOf = (className: string) =>
            Array.from(
                document.querySelectorAll(`${layerSelector} .${className}`),
            ).map((drawing) => {
                const r = drawing.getBoundingClientRect();
                return {
                    x: r.left,
                    y: r.top,
                    width: r.width,
                    height: r.height,
                };
            });
        return {
            labels: Array.from(
                document.querySelectorAll(
                    `${layerSelector} .bloom-overflow-chip`,
                ),
            ).map((chip) => {
                const r = chip.getBoundingClientRect();
                return {
                    // The label on a box starts with a down arrow.
                    text: (chip.textContent ?? "").replace(/^↓\s*/, ""),
                    lineY: r.top + r.height / 2,
                    rect: {
                        x: r.left,
                        y: r.top,
                        width: r.width,
                        height: r.height,
                    },
                };
            }),
            fades: rectsOf("bloom-overflow-fadeLastLine"),
            washes: rectsOf("bloom-overflow-cutOffText"),
            pageBands: rectsOf("bloom-overflow-pastContainer"),
        };
    }, LAYER);
}

/** Wait until Bloom shows no overflow mark of any kind on the page being edited. */
export async function expectNoOverflowMarks(
    page: Page,
    why: string,
): Promise<void> {
    await expect
        .poll(
            async () => {
                const marks = await getOverflowMarks(page);
                return [
                    ...marks.labels.map((l) => `a label saying "${l.text}"`),
                    ...marks.fades.map(() => "a faded last line"),
                    ...marks.washes.map(() => "a red wash"),
                    ...marks.pageBands.map(() => "a band past the margin"),
                ];
            },
            { timeout: 15000, message: why },
        )
        .toEqual([]);
}

/** What a text box is showing of its text, in the page frame's coordinates. */
export interface ITextBoxView {
    /** The box, border included. */
    rect: IRect;
    /** The y of the inside of the box's bottom border, where a box that is not scrolled cuts its text off. */
    insideBottom: number;
    /** How far the box is scrolled down, on screen. Zero when it shows its first line. */
    scrolledBy: number;
    /**
     * How much of the text is out of sight in all, above and below what the box shows, on
     * screen. Zero when the text fits, even if the box is still scrolled.
     */
    hidden: number;
    /** The height of one line of the box's text, on screen. */
    lineHeight: number;
    /** The color of the box's text. */
    textColor: string;
    /**
     * How far above the bottom of what the box shows its gray language label ("English") is
     * placed, in the page's own pixels. Bloom places it 2 above. undefined when the box has no
     * language label.
     */
    languageLabelAboveBottom: number | undefined;
}

/** Measure what a text box is showing of its text. */
export async function getTextBoxView(box: Locator): Promise<ITextBoxView> {
    return box.evaluate((element) => {
        const e = element as HTMLElement;
        const r = e.getBoundingClientRect();
        // The Edit tab scales the page for its zoom, so one of the page's own pixels is not one
        // on screen.
        const scale = r.height / e.offsetHeight;
        const style = getComputedStyle(e);
        const textStyle = getComputedStyle(e.querySelector("p") ?? e);
        const lineHeight = parseFloat(textStyle.lineHeight);
        const label = getComputedStyle(e, "::after");
        const hasLabel = label.content !== "none" && label.content !== '""';
        // The height of the text itself. Not scrollHeight: the language label is placed below
        // the text by however far the box is scrolled, so while a box is scrolled its
        // scrollHeight never drops below what keeps it scrolled, even once its text fits.
        const text = e.ownerDocument.createRange();
        text.selectNodeContents(e);
        const textHeight =
            text.getBoundingClientRect().height / scale +
            (parseFloat(style.paddingTop) || 0) +
            (parseFloat(style.paddingBottom) || 0);
        return {
            rect: { x: r.left, y: r.top, width: r.width, height: r.height },
            insideBottom:
                r.bottom - (parseFloat(style.borderBottomWidth) || 0) * scale,
            scrolledBy: e.scrollTop * scale,
            hidden: Math.max(0, textHeight - e.clientHeight) * scale,
            lineHeight:
                (isNaN(lineHeight)
                    ? parseFloat(textStyle.fontSize) * 1.2
                    : lineHeight) * scale,
            textColor: textStyle.color,
            // The label is placed against the box's text, which moves up as the box scrolls, so
            // its distance from the bottom of what shows is its offset plus the scrolling.
            languageLabelAboveBottom: hasLabel
                ? parseFloat(label.bottom) + e.scrollTop
                : undefined,
        };
    });
}

/** What fillTextBoxPastItsBottom leaves out of sight: lines of text, or only empty lines. */
export type OverflowFilling = "lines of text" | "empty lines";

/**
 * Give a text box more than it can show, the way a person does: type a line and press Enter, again
 * and again, until at least `linesTooMany` lines are out of sight. With "empty lines", the typing
 * stops at the first line that does not fit and Enter alone is pressed from there on, so that
 * everything out of sight is empty. With "key by key", the text is typed a key at a time, as a
 * person types it, so that Undo can take it back.
 *
 * The box is emptied first. It is left with the caret at the end, which scrolls it to its end; use
 * scrollTextBox to show its beginning. Whether Bloom marks the box is the caller's question.
 */
export async function fillTextBoxPastItsBottom(
    page: Page,
    box: Locator,
    filling: OverflowFilling = "lines of text",
    linesTooMany = 4,
    typing: "insert the text" | "key by key" = "insert the text",
): Promise<void> {
    await emptyTextBox(box);
    const maxLines = 150;
    for (let line = 1; line <= maxLines; line++) {
        const view = await getTextBoxView(box);
        if (view.hidden >= linesTooMany * view.lineHeight - kEdgeTolerance)
            return;
        if (filling === "lines of text" || view.hidden < 1) {
            // Inserted text is quicker, but leaves nothing for Undo to take back
            // (AUTOMATION-DEBT.md: "Undo and overflow checks in a table cell need real key
            // presses").
            if (typing === "key by key")
                await page.keyboard.type(`Line ${line}`);
            else await page.keyboard.insertText(`Line ${line}`);
        }
        // A real key, so that Bloom checks the box for overflow.
        await box.press("Enter");
    }
    throw new Error(
        `${maxLines} lines did not fill the text box past its bottom, so it is not a box of fixed height.`,
    );
}

/**
 * Press a key into the text box that has the focus, again and again, until all its text fits or
 * until at least a line of it does not: for example Ctrl+Z until the lines that did not fit are
 * taken back. Each press is a real key, so Bloom checks the box for overflow after it. Throws when
 * thirty seconds of presses never get there.
 */
export async function pressUntilTextBoxFitsOrNot(
    page: Page,
    box: Locator,
    key: Key,
    until: "fits" | "does not fit",
): Promise<void> {
    await expect(
        async () => {
            await pressKeys(page, [key]);
            const view = await getTextBoxView(box);
            if (until === "fits") expect(view.hidden).toBeLessThan(1);
            else expect(view.hidden).toBeGreaterThan(view.lineHeight);
        },
        `Pressing ${key} never left the text box with text that ${until === "fits" ? "all fits" : "does not fit"}.`,
    ).toPass({ timeout: 30000 });
}

/**
 * Empty a text box with the keys a person uses, Ctrl+A and Delete, and leave the caret in it. The
 * Delete is a real key, so Bloom checks the box for overflow afterwards.
 */
export async function emptyTextBox(box: Locator): Promise<void> {
    await box.click();
    await expect(
        box,
        "Clicking the text box did not give it the focus.",
    ).toBeFocused({ timeout: 15000 });
    await box.press("Control+a");
    await box.press("Delete");
    await expect(
        box,
        "Select-all and Delete did not empty the text box.",
    ).toHaveText("", { timeout: 15000, useInnerText: true });
}

/**
 * Scroll a text box with the mouse wheel, to its first line or to its last, and wait until it is
 * there.
 */
export async function scrollTextBox(
    page: Page,
    box: Locator,
    to: "top" | "end",
): Promise<void> {
    const leftToScroll = async () => {
        const view = await getTextBoxView(box);
        const left =
            to === "top" ? view.scrolledBy : view.hidden - view.scrolledBy;
        // A box stops a fraction of a pixel short of its end when the page is zoomed.
        return left < 2 ? 0 : Math.round(left);
    };
    // Three things can lose a turn of the wheel, so each try starts from the beginning:
    //  - The wheel scrolls what the mouse is over, and at the nightly's window size a text box in
    //    the lower half of the page is below the fold. So the page is scrolled, as a person
    //    scrolls it, until the top of the box is a third of the way down the window, and the mouse
    //    put near the box's top, clear of the caption strip a run draws along the bottom. Not
    //    scrollIntoView: that also scrolls the containers inside the page that a person cannot
    //    scroll, which moves the box on the page behind Bloom's back.
    //  - Bloom scrolls a text box itself (niceScroll), and sets that up a moment after it finds
    //    the box too full. Until then the wheel scrolls the whole page, which moves the box out
    //    from under the mouse.
    //  - The scrolling is smooth, so a turn takes a moment to arrive.
    await expect(async () => {
        const left = await leftToScroll();
        if (left !== 0) {
            await box.evaluate((e) => {
                const page = e.ownerDocument.scrollingElement;
                if (page)
                    page.scrollTop +=
                        e.getBoundingClientRect().top -
                        e.ownerDocument.defaultView!.innerHeight / 3;
            });
            const size = await box.boundingBox();
            if (!size)
                throw new Error("The text box to scroll is not showing.");
            await box.hover({ position: { x: size.width / 2, y: 12 } });
            await page.mouse.wheel(0, (to === "top" ? -1 : 1) * (left + 50));
        }
        await expect.poll(leftToScroll, { timeout: 2000 }).toBe(0);
    }, `The mouse wheel never scrolled the text box to its ${to}.`).toPass({
        timeout: 30000,
    });
}

/**
 * Check that Bloom marks a text box as having more text than fits, with every mark where the box's
 * scrolling says it belongs:
 *
 *  - one label saying "Overflowing", inside the box's right edge, on the line where the text is cut
 *    off. That line is the inside of the box's bottom edge, moved up by however far the box is
 *    scrolled, so it stays between the same two lines of text;
 *  - not scrolled: the last line that fits fades out, and nothing is washed red;
 *  - scrolled: a red wash from that line down to the box's bottom, as wide as the box, and no fade;
 *  - the gray language label is placed at the bottom of what the box shows, scrolled or not.
 *
 * Waits for the marks, because Bloom draws them a moment after the typing or scrolling.
 */
export async function expectTextBoxMarkedAsOverflowing(
    page: Page,
    box: Locator,
    what: string,
): Promise<void> {
    await expect(async () => {
        const view = await getTextBoxView(box);
        const marks = await getOverflowMarks(page);
        expect(
            view.hidden,
            `test setup: ${what} should have text out of sight`,
        ).toBeGreaterThan(1);
        const cutY = view.insideBottom - view.scrolledBy;

        expect(
            marks.labels.map((l) => l.text),
            `${what} should have one label, saying "Overflowing"`,
        ).toEqual(["Overflowing"]);
        const label = marks.labels[0];
        expectSameY(label.lineY, cutY, `the "Overflowing" label on ${what}`);
        expect(
            right(label.rect),
            `the "Overflowing" label should end inside the right edge of ${what}`,
        ).toBeLessThanOrEqual(right(view.rect) + kEdgeTolerance);
        expect(
            label.rect.x,
            `the "Overflowing" label should start inside the left edge of ${what}`,
        ).toBeGreaterThanOrEqual(view.rect.x - kEdgeTolerance);

        if (view.scrolledBy < 1) {
            expect(
                marks.washes,
                `nothing in ${what} is out of place while it is not scrolled, so nothing should be washed red`,
            ).toEqual([]);
            expect(
                marks.fades,
                `the last line that fits in ${what} should fade out`,
            ).toHaveLength(1);
            const fade = marks.fades[0];
            expectSameY(bottom(fade), view.insideBottom, "the fade's bottom");
            expectSameY(fade.height, view.lineHeight, "the fade's height");
        } else {
            expect(
                marks.fades,
                `the bottom edge of ${what} is not where its text is cut off while it is scrolled, so no line should fade`,
            ).toEqual([]);
            expect(
                marks.washes,
                `the lines of ${what} that do not fit should be washed red`,
            ).toHaveLength(1);
            const wash = marks.washes[0];
            expectSameY(wash.y, cutY, "the top of the red wash");
            expectSameY(
                bottom(wash),
                view.insideBottom,
                "the bottom of the red wash",
            );
            expectSameY(wash.x, view.rect.x, "the left of the red wash");
            expectSameY(
                wash.width,
                view.rect.width,
                "the width of the red wash",
            );
        }

        expectLanguageLabelAtBottom(view, what);
    }).toPass({ timeout: 15000 });
}

/**
 * Check that the gray language label of a text box (measured by getTextBoxView) is placed at the
 * bottom of what the box shows, 2 of the page's pixels above it, however far the box is scrolled.
 * A pixel either way is allowed: a zoomed box scrolls by fractions of a pixel.
 */
export function expectLanguageLabelAtBottom(
    view: ITextBoxView,
    what: string,
): void {
    expect(
        view.languageLabelAboveBottom,
        `test setup: ${what} should have a language label`,
    ).toBeDefined();
    expect(
        Math.abs(view.languageLabelAboveBottom! - 2),
        `the language label of ${what} should sit at the bottom of what the box shows ` +
            `(2 pixels above it) but is ${view.languageLabelAboveBottom!.toFixed(1)} above it`,
    ).toBeLessThanOrEqual(1);
}

/**
 * Wait until the gray language label of `box` is at the bottom of what the box shows, as
 * expectLanguageLabelAtBottom checks. Bloom moves the label when the box reports its scrolling,
 * a moment after the box has scrolled.
 */
export async function expectLanguageLabelAtBottomOf(
    box: Locator,
    what: string,
): Promise<void> {
    await expect(async () => {
        expectLanguageLabelAtBottom(await getTextBoxView(box), what);
    }).toPass({ timeout: 5000 });
}

function expectSameY(actual: number, expected: number, what: string): void {
    expect(
        Math.abs(actual - expected),
        `${what} should be at ${expected.toFixed(1)} but is at ${actual.toFixed(1)}`,
    ).toBeLessThanOrEqual(kEdgeTolerance);
}

/**
 * Check that Bloom marks the page as too full, and no single box: a label saying "Page
 * overflowing" on the bottom of the page's margin, and a band from there down to the bottom of the
 * lowest text past it, as wide as the margin. Waits for the marks.
 */
export async function expectPageMarkedAsOverflowing(page: Page): Promise<void> {
    await expect(async () => {
        const marks = await getOverflowMarks(page);
        const { margin, lowestTextBottom } = await editablePageFrame(
            page,
        ).evaluate(() => {
            const m = document
                .querySelector(".bloom-page .marginBox")!
                .getBoundingClientRect();
            const bottoms = Array.from(
                document.querySelectorAll<HTMLElement>(
                    ".bloom-page .bloom-editable",
                ),
            )
                .filter((e) => e.getClientRects().length > 0)
                .map((e) => e.getBoundingClientRect().bottom);
            return {
                margin: {
                    x: m.left,
                    y: m.top,
                    width: m.width,
                    height: m.height,
                },
                lowestTextBottom: Math.max(...bottoms),
            };
        });
        expect(
            lowestTextBottom,
            "test setup: some text should be past the bottom of the page's margin",
        ).toBeGreaterThan(bottom(margin) + kEdgeTolerance);

        expect(
            marks.labels.map((l) => l.text),
            `the page should have one label, saying "Page overflowing", and none on a box`,
        ).toEqual(["Page overflowing"]);
        expectSameY(
            marks.labels[0].lineY,
            bottom(margin),
            `the "Page overflowing" label`,
        );
        expect(
            marks.pageBands,
            "there should be one band over what is past the margin",
        ).toHaveLength(1);
        const band = marks.pageBands[0];
        expectSameY(band.y, bottom(margin), "the top of the band");
        expectSameY(bottom(band), lowestTextBottom, "the bottom of the band");
        expectSameY(band.x, margin.x, "the left of the band");
        expectSameY(band.width, margin.width, "the width of the band");
        expect(
            [...marks.fades, ...marks.washes],
            "no single box is too full, so none should be marked",
        ).toEqual([]);
    }).toPass({ timeout: 15000 });
}

/**
 * Point the mouse at the bottom of the page's margin, where Bloom used to pop up "A container on
 * this page is overflowing", and report the text of every tooltip that is then showing which
 * mentions overflow. An empty list means none.
 */
export async function getOverflowTooltipsAtPageBottom(
    page: Page,
): Promise<string[]> {
    const frame = editablePageFrame(page);
    const margin = frame.locator(".bloom-page .marginBox").first();
    const size = await margin.evaluate((m) => ({
        width: (m as HTMLElement).getBoundingClientRect().width,
        height: (m as HTMLElement).getBoundingClientRect().height,
    }));
    // Two moves, because the old tooltip came up on a mouse move near the edge.
    for (const up of [8, 3])
        await margin.hover({
            position: { x: size.width / 2, y: size.height - up },
            force: true,
        });
    // The old tooltip faded in over a fraction of a second.
    await page.waitForTimeout(700);
    return frame
        .locator(".qtip:visible")
        .evaluateAll((tips) =>
            tips
                .map((tip) => (tip.textContent ?? "").trim())
                .filter(
                    (text) =>
                        /overflow|more text than will fit/i.test(text) || false,
                ),
        );
}

/** What a page's thumbnail in the page list shows about overflow. */
export interface IThumbnailOverflow {
    /** The red zigzag along the thumbnail's bottom edge is showing. */
    zigzag: boolean;
    /** The warning triangle Bloom used before the zigzag is showing. */
    triangle: boolean;
}

/** Read what a page's thumbnail in the page list shows about overflow, as it is right now. */
export async function getThumbnailOverflow(
    page: Page,
    pageId: string,
): Promise<IThumbnailOverflow> {
    const thumbnail = pageListFrame(page).locator(
        `#pageGridWrapper .gridItem[id="${pageId}"]`,
    );
    await thumbnail.waitFor({ timeout: 30000 });
    return thumbnail.evaluate((item) => {
        const shows = (selector: string) =>
            Array.from(item.querySelectorAll<HTMLElement>(selector)).some(
                (e) => {
                    const r = e.getBoundingClientRect();
                    const style = getComputedStyle(e);
                    return (
                        r.width > 0 &&
                        r.height > 0 &&
                        style.visibility !== "hidden" &&
                        // The zigzag is a color cut out by a mask, the triangle a picture.
                        (style.maskImage !== "none" ||
                            style.backgroundImage !== "none")
                    );
                },
            );
        return {
            zigzag: shows(".pageOverflowsMarker"),
            triangle: shows(".pageOverflowsIcon"),
        };
    });
}

/**
 * What the saved book holds of the Edit tab's overflow drawing, which is none of the book's
 * business: the layer the marks are drawn in, any drawing from it, and the note of how far a box
 * was scrolled. One line per finding; an empty list means the book is clean of them.
 *
 * Bloom has always saved the plain "overflow" and "pageOverflows" classes on a box and a page;
 * those are how the page list knows, and are not findings.
 */
export function findOverflowDrawingInSavedBook(bookFolder: string): string[] {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    const leftovers: [RegExp, string][] = [
        [/bloom-overflow-/, "something from the layer the marks are drawn in"],
        [/--bloom-scroll-top/, "how far a text box was scrolled"],
        [/--overflow-background/, "the color a mark fades into"],
    ];
    return leftovers
        .filter(([pattern]) => pattern.test(html))
        .map(([, what]) => what);
}

/**
 * What a BloomPUB preview (the frame showBloomPubPreview returns) shows of the Edit tab's overflow
 * drawing, which a published book should have none of: a drawing from the layer, the note of how
 * far a box was scrolled, or red text or a red outline on a text box. One line per finding; an
 * empty list means none. Also reports how many text boxes it looked at, so a caller can tell an
 * empty preview from a clean one.
 */
export async function findOverflowDrawingInPreview(
    player: Frame,
): Promise<{ textBoxes: number; findings: string[] }> {
    return player.evaluate(() => {
        const findings: string[] = [];
        if (
            document.querySelector(
                '[id^="bloom-overflow"], [class*="bloom-overflow-"]',
            )
        )
            findings.push("something from the layer the marks are drawn in");
        if (document.querySelector('[style*="--bloom-scroll-top"]'))
            findings.push("how far a text box was scrolled");
        const isRed = (color: string) => {
            const [r, g, b] = (color.match(/[\d.]+/g) ?? []).map(Number);
            return r > 150 && g < 100 && b < 100;
        };
        const boxes = Array.from(
            document.querySelectorAll<HTMLElement>(
                ".bloom-page .bloom-editable",
            ),
        );
        for (const box of boxes) {
            const style = getComputedStyle(box);
            if (style.outlineStyle !== "none" && isRed(style.outlineColor))
                findings.push("a red outline on a text box");
            for (const p of Array.from(box.querySelectorAll("p")))
                if (isRed(getComputedStyle(p).color)) {
                    findings.push("red text in a text box");
                    break;
                }
        }
        return { textBoxes: boxes.length, findings };
    });
}
