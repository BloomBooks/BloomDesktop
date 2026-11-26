// The scroll bar of a text box with more text than fits stays at the box's right edge: in the Edit
// tab while the page is zoomed and scrolled, and in the BloomPUB preview after the reader turns
// away from the page and back, where it scrolls the box to its last line.
// Automates the manual test "Player Scroll Bar Position" (Test Case ID 726).
//
// The scroll bar is niceScroll's rail, which niceScroll places beside the box itself (see
// helpers/textBoxScrollBars.ts), so it has to follow the box whenever the box moves or the zoom
// changes its size. The case's "changes width relative to the zoom factor" is checked as the
// rail's width as a share of the page's width staying what it was at 100%.
//
// Not checked: the case's step of putting the preview in full screen. Under a test, WebView2
// refuses bloom-player's requestFullscreen ("TypeError: not granted"), with or without
// --dont-disturb. The scroll to the last line that the case checks in full screen is checked in
// the preview as it is.
//
// The tests are serial and share one A5 book with one Just Text page.

import type { Frame, Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    activePreviewPage,
    getActivePreviewPageId,
    isPreviewOnLastPage,
    turnPreviewPage,
} from "../helpers/bloomPlayer";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForPageToSettle,
    type IBookPage,
} from "../helpers/bookMaking";
import { right } from "../helpers/geometry";
import { getTextBoxView, scrollTextBox } from "../helpers/overflowMarks";
import { scrollPageFrameBy } from "../helpers/pageView";
import {
    getPreviewTextBox,
    openPublishDestination,
    showBloomPubPreview,
    showPreviewPage,
} from "../helpers/publish";
import {
    getLastLineOf,
    getScrollBarPlacementOf,
    waitForScrollBarOf,
    type IScrollBarPlacement,
} from "../helpers/textBoxScrollBars";
import {
    getWindowSize,
    resizeWindow,
    restoreWindowSize,
} from "../helpers/windowSize";
import {
    clickZoomButton,
    expectPageDrawnAtZoom,
    getSizeOfPageBeingEdited,
    getZoom,
    setZoom,
} from "../helpers/workspace";

test.use({
    collectionSpec: { name: "player-scroll-bar-position", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const GROUP = ".bloom-translationGroup";
const LANG = "en";
// The case's paragraph. It is typed four times, which overflows an A5 page.
const PARAGRAPH =
    "The Greek king Midas did a good deed for a Satyr. This prompted Dionysus, the god of wine, to grant him a wish. Midas asked for everything he touched to turn to gold. Dionysus warned him not to do so, but Midas could not be swayed. Midas excitedly started touching everything and turning them into gold. Soon, he became hungry. But he couldn’t eat anything because even his food turned to gold. His beloved daughter saw him in distress and ran to hug him. However, she, too, turned to gold. He realized then the golden touch was not a blessing.";
const LAST_WORD = "blessing.";
const kParagraphs = 4;
// How far apart two edges may be and still count as the same, in screen pixels. niceScroll sets
// the rail's place in whole pixels, and the zoom scales the box by fractions of one.
const kTolerance = 2;

/** The book's one content page. Set by the first test. */
let textPage: IBookPage;
/** The rail's width as a share of the page's width, at 100%. Set by the first test. */
let railShareOfPageAt100: number;
/** The zoom the Edit tab had before this test changed it. Set by the first test. */
let zoomBefore: number;

function textBox(page: Page): Locator {
    return editablePageFrame(page)
        .locator(GROUP)
        .first()
        .locator(`.bloom-editable[lang="${LANG}"]`)
        .first();
}

async function pageWidth(page: Page): Promise<number> {
    return (await getSizeOfPageBeingEdited(page)).width;
}

/**
 * Wait until the scroll bar nearest `box` is drawn at the box's right edge, from the box's top
 * to its bottom, and return where it is. niceScroll moves its rail a moment after the box moves,
 * so this waits for it; a rail that never gets there fails, with where it is in the message.
 */
async function expectScrollBarAtRightOfBox(
    box: Locator,
    when: string,
): Promise<IScrollBarPlacement> {
    let placement: IScrollBarPlacement | undefined;
    const describe = (p: IScrollBarPlacement | undefined) =>
        p
            ? `rail x ${p.rail.x.toFixed(1)}..${right(p.rail).toFixed(1)}, y ${p.rail.y.toFixed(1)}..${(p.rail.y + p.rail.height).toFixed(1)}; box x ${p.box.x.toFixed(1)}..${right(p.box).toFixed(1)}, y ${p.box.y.toFixed(1)}..${(p.box.y + p.box.height).toFixed(1)}`
            : "no scroll bar";
    await expect
        .poll(
            async () => {
                placement = await getScrollBarPlacementOf(box);
                if (!placement) return "no scroll bar";
                const { rail, box: b } = placement;
                const atRight =
                    Math.abs(right(rail) - right(b)) <= kTolerance &&
                    rail.x < right(b);
                const sameHeight =
                    Math.abs(rail.y - b.y) <= kTolerance &&
                    Math.abs(rail.y + rail.height - (b.y + b.height)) <=
                        kTolerance;
                return atRight && sameHeight
                    ? "at the right"
                    : describe(placement);
            },
            {
                timeout: 15000,
                message: `${when}: the scroll bar should be drawn along the right edge of the text box`,
            },
        )
        .toBe("at the right");
    return placement!;
}

/** The rail's width as a share of the width of the page being edited. */
async function railShareOfPage(
    page: Page,
    placement: IScrollBarPlacement,
): Promise<number> {
    return placement.rail.width / (await pageWidth(page));
}

/**
 * Expect the rail to be as wide, for the width of the page, as it was at 100%, give or take the
 * pixel that niceScroll rounds its width to.
 */
async function expectRailScaledWithPage(
    page: Page,
    placement: IScrollBarPlacement,
    when: string,
): Promise<void> {
    const width = await pageWidth(page);
    expect(
        Math.abs(placement.rail.width - railShareOfPageAt100 * width),
        `${when}: the scroll bar should be ${(railShareOfPageAt100 * width).toFixed(1)}px wide, as wide for this ${width.toFixed(0)}px page as it was at 100%, and is ${placement.rail.width.toFixed(1)}px`,
    ).toBeLessThanOrEqual(kTolerance);
}

/**
 * Scroll the Edit tab's page down from its top, as a person does. On a screen tall enough to show
 * the whole zoomed page there is nothing to scroll, so the window is first made shorter than the
 * page, as a person with a smaller screen has it; restoreWindowSize puts it back.
 */
async function scrollEditTabDown(page: Page): Promise<void> {
    // From the top, since an earlier scroll at a larger zoom can leave the page at its bottom.
    await scrollPageFrameBy(page, -100000);
    if ((await scrollPageFrameBy(page, 400)) > 0) return;
    const window = await getWindowSize(page);
    const pageHeight = (await getSizeOfPageBeingEdited(page)).height;
    await resizeWindow(page, {
        width: window.width,
        height: Math.round(pageHeight / 2),
    });
    await scrollPageFrameBy(page, -100000);
    expect(
        await scrollPageFrameBy(page, 400),
        "test setup: with the window half as tall as the page, the page should scroll",
    ).toBeGreaterThan(0);
}

/** Wait until the zoom changes the box's rail with it: niceScroll resizes it a moment later. */
async function expectRailFollowsZoom(page: Page, when: string): Promise<void> {
    await expect(async () => {
        const placement = await expectScrollBarAtRightOfBox(
            textBox(page),
            when,
        );
        await expectRailScaledWithPage(page, placement, when);
    }, `${when}: the scroll bar did not follow the zoom`).toPass({
        timeout: 15000,
    });
}

/** The page of the BloomPUB preview that is showing the text page. */
function previewBox(player: Frame): Locator {
    return getPreviewTextBox(activePreviewPage(player), "The Greek king Midas");
}

let player: Frame;

test.describe("Player Scroll Bar Position", () => {
    test("builds a Basic Book whose Just Text page has four paragraphs, more than fits [Test Case ID 726]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
        [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        zoomBefore = (await getZoom(page)).zoom;
        if (zoomBefore !== 100) await setZoom(page, 100);

        const box = await clickInGroup(page, GROUP, LANG);
        for (let i = 0; i < kParagraphs; i++) {
            if (i > 0) await page.keyboard.press("Enter");
            // The last word is typed with real key presses, because Bloom checks a box for
            // overflow when a key is released.
            await page.keyboard.insertText(
                i < kParagraphs - 1
                    ? PARAGRAPH
                    : PARAGRAPH.slice(0, -LAST_WORD.length),
            );
        }
        await page.keyboard.type(LAST_WORD);
        await expect(
            box.locator("p"),
            "test setup: the box should hold four paragraphs",
        ).toHaveCount(kParagraphs, { timeout: 15000 });
        await waitForScrollBarOf(box, true, "The text box");
        const placement = await expectScrollBarAtRightOfBox(box, "At 100%");
        railShareOfPageAt100 = await railShareOfPage(page, placement);
        expect(
            railShareOfPageAt100,
            "test setup: the scroll bar should be a narrow strip",
        ).toBeLessThan(0.1);
    });

    test("zoomed in four steps, the scroll bar grows with the page and stays at the box's right edge when the page is scrolled [Test Case ID 726]", async ({
        page,
    }) => {
        for (let i = 0; i < 4; i++) await clickZoomButton(page, "in");
        expect((await getZoom(page)).zoom).toBe(140);
        await expectPageDrawnAtZoom(page, 140);
        await expectRailFollowsZoom(page, "Zoomed in to 140%");

        await scrollEditTabDown(page);
        await expectScrollBarAtRightOfBox(
            textBox(page),
            "Zoomed in to 140%, with the page scrolled down",
        );
    });

    test("zoomed out two steps, the scroll bar shrinks with the page and stays at the box's right edge when the page is scrolled [Test Case ID 726]", async ({
        page,
    }) => {
        for (let i = 0; i < 2; i++) await clickZoomButton(page, "out");
        expect((await getZoom(page)).zoom).toBe(120);
        await expectPageDrawnAtZoom(page, 120);
        await expectRailFollowsZoom(page, "Zoomed out to 120%");

        await scrollEditTabDown(page);
        await expectScrollBarAtRightOfBox(
            textBox(page),
            "Zoomed out to 120%, with the page scrolled down",
        );
        await restoreWindowSize(page);
        // Bloom keeps the zoom as a user setting.
        await setZoom(page, zoomBefore);
    });

    test("the BloomPUB preview shows the paragraph, with its scroll bar at the right after turning away from the page and back [Test Case ID 726]", async ({
        page,
    }) => {
        test.setTimeout(240000);
        await waitForPageToSettle(page);
        await openPublishDestination(page, "BloomPUB");
        player = await showBloomPubPreview(page);
        await expect(
            activePreviewPage(player),
            "The book should appear in the preview.",
        ).toBeVisible({ timeout: 30000 });

        await showPreviewPage(player, textPage.id);
        await expect(
            previewBox(player),
            "The preview's text page should show the paragraph.",
        ).toBeVisible();
        await expect(previewBox(player)).toContainText(PARAGRAPH, {
            useInnerText: true,
        });

        // The case turns to the next page and back. The preview leaves out a back cover with
        // nothing on it, which can make the text page the last one; then the way away and back
        // is through the page before it.
        const awayAndBack = (await isPreviewOnLastPage(player))
            ? (["previous", "next"] as const)
            : (["next", "previous"] as const);
        for (const direction of awayAndBack)
            await turnPreviewPage(player, direction);
        expect(
            await getActivePreviewPageId(player),
            `test setup: ${awayAndBack.join(" then ")} should come back to the text page`,
        ).toBe(textPage.id);

        await waitForScrollBarOf(
            previewBox(player),
            true,
            "The text box in the preview",
        );
        await expectScrollBarAtRightOfBox(
            previewBox(player),
            "In the preview, after Next and Previous",
        );
    });

    test("the scroll bar scrolls the text box to its last line, and stays at the box's right edge [Test Case ID 726]", async ({
        page,
    }) => {
        const box = previewBox(player);
        const before = await getLastLineOf(box);
        expect(
            before.lineBottom,
            "test setup: before scrolling, the last line should be below the bottom of the text box",
        ).toBeGreaterThan(before.boxBottom);

        await scrollTextBox(page, box, "end");
        await expectScrollBarAtRightOfBox(
            box,
            "In the preview, scrolled to the end",
        );
        expect(
            (await getTextBoxView(box)).scrollableFurther,
            "The text box should be scrolled to its end",
        ).toBeLessThan(2);
        const after = await getLastLineOf(box);
        expect(
            after.lineBottom,
            "Scrolled to the end, the last line of the text should show whole",
        ).toBeLessThanOrEqual(after.boxBottom + 1);
    });
});
