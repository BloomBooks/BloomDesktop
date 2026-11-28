// The header of a Comprehension Quiz page ("Check Your Understanding") takes a couple of lines of
// text at whatever size a person gives it in the Format dialog, and the BloomPUB preview, which is
// Bloom Player itself, shows all of it at that size with no scroll bar (BL-9021: a header of more
// than one line used to get one).
//
// The header is given text that runs to several lines, its size is made larger and then smaller,
// and each time the preview's header is checked: it holds the text, at the chosen size, on more
// than one line, with none of it out of sight and no scroll bar beside it.
//
// How the header looks at each size is not checked; that is for the eye.
//
// Automates the manual test "Quiz Header Text" (Test Case ID 530).

import type { Frame, Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { activePreviewPage } from "../helpers/bloomPlayer";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForPageToSettle,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseFontSize,
    closeFormatDialog,
    getFontSizeShown,
    openFormatDialog,
    showFormatDialogTab,
} from "../helpers/formatDialog";
import {
    getPreviewTextBox,
    openPublishDestination,
    showBloomPubPreview,
    showPreviewPage,
} from "../helpers/publish";
import {
    retypeGroupWithKeys,
    waitForScrollBarOf,
} from "../helpers/textBoxScrollBars";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "quiz-header-text",
        languages: ["en"],
        // The Comprehension Quiz is a game, and games need a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const LANG = "en";
/** The Add Page label of the comprehension quiz page. */
const kQuizPageLabel = "Comprehension Quiz";
// The template gives the group the style's class, and Bloom moves it onto the group's boxes.
const kHeaderGroup =
    ".bloom-translationGroup:is(.GameHeader-style, :has(> .GameHeader-style))";
const kQuestionGroup =
    ".bloom-translationGroup:is(.QuizQuestion-style, :has(> .QuizQuestion-style))";
const QUESTION = "Where did the goat go?";
// A couple of lines' worth at the header's default size, and more at a larger one.
const HEADER_TEXT =
    "Read the story about the clever little goat once more, then answer these questions about where she went and what she did there.";
const HEADER_START = "Read the story about the clever little goat";
const kLargerSizePt = 28;
const kSmallerSizePt = 12;

/** CSS pixels in a point. */
const kPxPerPt = 96 / 72;

/** Give the header the font size `points` through the Format dialog, as a person does. */
async function setHeaderFontSize(page: Page, points: number): Promise<void> {
    await clickInGroup(page, kHeaderGroup, LANG);
    await openFormatDialog(page);
    await showFormatDialogTab(page, "characters");
    await chooseFontSize(page, points);
    expect(
        await getFontSizeShown(page),
        `The Format dialog should show the header's new size, ${points}pt`,
    ).toBe(points);
    await closeFormatDialog(page);
}

/** The font size of the header box `box` locates, in points, as the browser draws it. */
async function getFontSizePt(box: Locator): Promise<number> {
    const px = await box.evaluate((element) =>
        parseFloat(
            getComputedStyle(element.querySelector("p") ?? element).fontSize,
        ),
    );
    return Math.round((px / kPxPerPt) * 10) / 10;
}

/** How many lines a text box's text runs to, and how many of them its box is tall enough to show. */
interface IHeaderFit {
    lines: number;
    linesShown: number;
}

/**
 * Count the lines of the text box `box` locates, and how many whole lines its height holds. Counted
 * in lines rather than pixels, because the glyphs of the last line stand a few pixels below its
 * line box, which the box's scrollHeight counts as overflow though nothing is out of sight.
 */
async function getHeaderFit(box: Locator): Promise<IHeaderFit> {
    return box.evaluate((element) => {
        const e = element as HTMLElement;
        const lineHeight = parseFloat(
            getComputedStyle(e.querySelector("p") ?? e).lineHeight,
        );
        const tops = new Set<number>();
        const walker = e.ownerDocument.createTreeWalker(
            e,
            NodeFilter.SHOW_TEXT,
        );
        const range = e.ownerDocument.createRange();
        for (let n = walker.nextNode(); n; n = walker.nextNode()) {
            if (!n.textContent?.trim()) continue;
            range.selectNodeContents(n);
            for (const r of Array.from(range.getClientRects()))
                tops.add(Math.round(r.top));
        }
        // The box may be scaled on screen (bloom-player fits the page to its frame), so compare
        // its own unscaled height with the unscaled line height.
        return {
            lines: tops.size,
            linesShown: Math.min(
                tops.size,
                Math.floor((e.clientHeight + 1) / lineHeight),
            ),
        };
    });
}

/**
 * On the quiz page of the BloomPUB preview, expect the header to show all of HEADER_TEXT, at
 * `points`, on more than one line, with no scroll bar.
 */
async function expectPreviewHeaderShowsAll(
    player: Frame,
    quizPageId: string,
    points: number,
): Promise<void> {
    await showPreviewPage(player, quizPageId);
    const header = getPreviewTextBox(activePreviewPage(player), HEADER_START);
    await expect(
        header,
        `At ${points}pt, the preview's quiz page should show the header`,
    ).toBeVisible({ timeout: 30000 });
    await expect(header).toHaveText(HEADER_TEXT, { useInnerText: true });
    expect(
        await getFontSizePt(header),
        `The preview should draw the header at the size chosen for it, ${points}pt`,
    ).toBe(points);

    const fit = await getHeaderFit(header);
    expect(
        fit.lines,
        `test setup: at ${points}pt the header's text should run to more than one line`,
    ).toBeGreaterThan(1);
    expect(
        fit.linesShown,
        `At ${points}pt, the preview's header box should be tall enough for all ${fit.lines} lines of its text`,
    ).toBe(fit.lines);
    await waitForScrollBarOf(
        header,
        false,
        `At ${points}pt, the preview's quiz header`,
    );
}

test("The quiz header shows several lines of text at the size chosen, with no scroll bar, in the BloomPUB preview [Test Case ID 530]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);
    let quizPageId = "";

    await step(
        "Make a book with a Comprehension Quiz page, and type a couple of lines into its header",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            const before = new Set(
                (await getContentPages(page)).map((p) => p.id),
            );
            await addPage(page, kQuizPageLabel);
            const added = (await getContentPages(page)).filter(
                (p) => !before.has(p.id),
            );
            expect(
                added.length,
                `test setup: adding a "${kQuizPageLabel}" page should add one page`,
            ).toBe(1);
            quizPageId = added[0].id;
            await goToPage(page, quizPageId);
            // Typed with real key presses, because Bloom checks a box for overflow when a key is
            // released.
            await retypeGroupWithKeys(page, kHeaderGroup, LANG, HEADER_TEXT);
            // A question too, as a real quiz has. The header alone gives the book no text
            // language (its group is left out of the book's language list), and the Publish
            // screens list no languages for a book with none.
            await typeInGroup(page, kQuestionGroup, LANG, QUESTION);
        },
    );

    await step(`Make the header larger, ${kLargerSizePt}pt`, async () => {
        await setHeaderFontSize(page, kLargerSizePt);
        await waitForPageToSettle(page);
    });

    await step(
        `In the BloomPUB preview, the header shows all its text at ${kLargerSizePt}pt, with no scroll bar`,
        async () => {
            await switchTab(page, "publish");
            await openPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            await expectPreviewHeaderShowsAll(
                player,
                quizPageId,
                kLargerSizePt,
            );
        },
    );

    await step(`Make the header smaller, ${kSmallerSizePt}pt`, async () => {
        await switchTab(page, "edit");
        await goToPage(page, quizPageId);
        await setHeaderFontSize(page, kSmallerSizePt);
        await waitForPageToSettle(page);
    });

    await step(
        `In the rebuilt BloomPUB preview, the header shows all its text at ${kSmallerSizePt}pt, with no scroll bar`,
        async () => {
            await switchTab(page, "publish");
            await openPublishDestination(page, "BloomPUB");
            const rebuilt = await showBloomPubPreview(page);
            await expectPreviewHeaderShowsAll(
                rebuilt,
                quizPageId,
                kSmallerSizePt,
            );
        },
    );
});
