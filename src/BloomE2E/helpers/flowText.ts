// Text that flows from one text box into the next: the offer an empty box makes to continue an
// overflowing one, the chain the two boxes then belong to, and Unlink.
//
// A "box index" here counts the ordinary (normal-style) text boxes of the page being edited, in
// the order the text flows through them, which is document order. So on an "Image in Middle"
// page, box 0 is the one above the picture and box 1 the one below it. Front and back matter
// have none, and a box inside a canvas element is not one: flow works only on the boxes of the
// page's own layout.
//
// Every reader here waits for the flow to settle first (waitForReflowIdle), because a pass runs
// on an animation frame after the typing that caused it. A reader that did not wait would see
// the page halfway through a move, which is the classic way for one of these tests to be flaky.

import { expect, type Locator, type Page } from "@playwright/test";
import { apiGet, apiGetJson, apiPost } from "./api";
import {
    addPageWithId,
    editablePageFrame,
    getContentPages,
    goToPage,
    reloadPageBeingEdited,
    waitForEditablePage,
} from "./bookMaking";
import {
    getRenderedFontSize,
    setFontSizeWithFormatDialog,
} from "./formatDialog";
import { pageListFrame } from "./pageList";
import type { ICollectionSpec } from "../fixtures/launchBloom";

/**
 * The collection every flow-text spec opens, and the reason all of them run on ONE Bloom.
 *
 * Import this object and pass it, rather than writing an equal-looking literal in each file:
 *
 *     test.use({ collectionSpec: kFlowTextCollection });
 *
 * Playwright decides whether the next file can keep the worker (and so the Bloom the worker-scoped
 * fixture launched) from the worker hash it computes for each file, and that hash is built from the
 * IDENTITY of every worker-scoped test.use value, not from its content: it keys the values in a Map
 * (registrationId in playwright/lib/common/fixtures.js). Two files passing two deep-equal literals
 * therefore get two workers, and so two Blooms, and each launch costs the better part of a minute.
 * One imported object is the whole of what makes these ten files share one launch. (A Map compares
 * a string by its value, which is why two files that both say collectionName: "basic" share a Bloom
 * without doing anything special, and why a collectionSpec has to be handled this way instead.)
 *
 * Sharing one Bloom is safe here because a flow-text test's state is its book, and every one of
 * these files makes its own book with makeBookFromTemplate before it does anything else:
 *
 * - The settings these tests change, "run the waiting refits when the page changes" and
 *   "automatically add and remove pages", are the book's own (book.UserPrefs), so one file's choice
 *   cannot reach another file's book.
 * - The queue of waiting refits is Bloom's, not the book's, but Bloom empties it whenever a
 *   different book is selected (FlowTextApi's SelectionChanged handler, which exists because a
 *   queued walk names its chain and page by id and those ids survive into a copy of the book). So
 *   a file that deliberately ends with a refit still waiting, as flow-text-reflow-pending does,
 *   cannot leave it to run against the next file's book.
 * - The books pile up in the collection, which nothing here minds: each test reads the chains of
 *   the selected book alone, and no test counts the collection's books. They are not deleted
 *   because Bloom's deleteBook opens a WinForms confirmation dialog, which would hang the run.
 *
 * A test that needs something else of its collection — other languages, a subscription code —
 * needs its own spec, and pays for its own Bloom.
 */
export const kFlowTextCollection: ICollectionSpec = {
    name: "flow-text",
    languages: ["en"],
};

/** The Basic Book template page with a text box, a picture, and a second text box. */
const kImageInMiddlePageId = "adcd48df-e9ab-4a07-afd4-6a24d0398383";

/** The Basic Book template page that is one text box and nothing else. */
const kJustTextPageId = "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb";

/**
 * More text than the top box of a two-box page can hold, as one paragraph, so that the flow has
 * to split a paragraph rather than move a whole one. Shared by every test in this area, so that
 * "too long" means one thing: a test that made up its own text would be one page-size change
 * away from testing nothing.
 */
export const kTextTooLongForOneBox =
    "The rain had been falling since before dawn and the path down to the river was " +
    "already a stream of its own. Grandmother watched from the doorway while the children " +
    "pulled on their boots, and she said nothing at all about the mud, which was how they " +
    "knew she had decided to let them go. By the time they reached the bend where the old " +
    "bridge used to stand, the water was loud enough that they had to shout, and the eldest " +
    "of them stood a while on the last dry stone and looked at the place where the planks " +
    "had been, as though the river might be persuaded to give them back if somebody waited " +
    "there long enough and did not say anything foolish about it.";

/**
 * How many words each sentence of the built text holds. Declared before the text that uses it:
 * kTextForSeveralPages is built as the module loads.
 */
const kWordsPerSentence = 12;

/**
 * More text than three text-box pages can hold, as one paragraph. Every word says where it comes
 * in the whole run, so a word that goes missing or arrives twice at a join between two boxes
 * shows up as a break in the counting rather than as text that merely looks plausible.
 */
export const kTextForSeveralPages = buildLongText(6000);

/**
 * A paragraph of numbered sentences, at least `minimumLength` characters long. Sentence 7 reads
 * `S7: w0073 w0074 ... w0084.`, so both the sentence and each of its words carry a running
 * number. The words are five characters long, near the average for English, so the boxes break
 * the text at about the places real text would break.
 *
 * The wording is fixed, so the same text goes in on every run and the boxes break it in the same
 * places. assertRunIsIntact is what reads it back.
 */
export function buildLongText(minimumLength: number): string {
    const sentences: string[] = [];
    let length = 0;
    let word = 1;
    for (let number = 1; length < minimumLength; number++) {
        const words: string[] = [];
        for (let i = 0; i < kWordsPerSentence; i++, word++) {
            words.push("w" + String(word).padStart(4, "0"));
        }
        const sentence = `S${number}: ${words.join(" ")}.`;
        sentences.push(sentence);
        length += sentence.length + 1;
    }

    return sentences.join(" ");
}

/**
 * Assert that the text of a chain of boxes, read in flow order, is still exactly the text that
 * went in: no word lost and none repeated, however the boxes divided it.
 *
 * The box texts are joined with a space and the whitespace of both sides is then collapsed,
 * because a box may keep or drop the space at the point the text was cut, and which of the two
 * happened is not what this checks. Everything else has to match character for character.
 *
 * On failure it names the first character the two disagree at and prints 40 characters of each
 * side around it, which is what tells you which word went missing and at which join.
 */
export function assertRunIsIntact(
    actualTexts: string[],
    expected: string,
): void {
    const actual = collapseWhitespace(actualTexts.join(" "));
    const wanted = collapseWhitespace(expected);
    if (actual === wanted) return;

    let at = 0;
    while (
        at < actual.length &&
        at < wanted.length &&
        actual[at] === wanted[at]
    )
        at++;
    const window = (text: string) => {
        const from = Math.max(0, at - 20);
        return JSON.stringify(text.slice(from, from + 40));
    };
    throw new Error(
        `The run of text is no longer what went in. It first differs at character ${at} ` +
            `(of ${wanted.length} expected, ${actual.length} found).\n` +
            `  expected around there: ${window(wanted)}\n` +
            `  found around there:    ${window(actual)}\n` +
            `The text was read from ${actualTexts.length} box(es), of lengths ` +
            `${actualTexts.map((text) => text.length).join(", ")}.`,
    );
}

/**
 * Assert that no indexed word of the run appears twice. Use this where part of the run has been
 * deleted, so the text that should remain is not known word for word, but a word in two places
 * still means the flow copied it rather than moved it.
 */
export function assertNoWordAppearsTwice(actualTexts: string[]): void {
    const counts = new Map<string, number>();
    for (const word of collapseWhitespace(actualTexts.join(" ")).split(" ")) {
        if (!/^w\d{4}$/.test(word)) continue;
        counts.set(word, (counts.get(word) ?? 0) + 1);
    }

    const repeated = Array.from(counts.entries())
        .filter(([, count]) => count > 1)
        .map(([word, count]) => `${word} (${count} times)`);
    if (repeated.length) {
        throw new Error(
            `${repeated.length} word(s) of the run are in the book more than once, so the ` +
                `flow copied text rather than moving it. The first few: ` +
                `${repeated.slice(0, 6).join(", ")}.`,
        );
    }
}

const kZeroWidthCharacters = new RegExp(
    "[" + String.fromCharCode(0x200b, 0x200c) + "]",
    "g",
);

/**
 * Whitespace, however much of it and of whatever kind, counts as one space. The two zero-width
 * characters that live in an edited box go: U+200C, which the mark that says where a box's text
 * stops fitting is made of, and U+200B, the editor's own end-of-paragraph filler. Neither is any
 * part of the text, and Bloom takes both out of what it publishes.
 */
function collapseWhitespace(text: string): string {
    return text.replace(kZeroWidthCharacters, "").replace(/\s+/g, " ").trim();
}

/** One translation group of a flow chain, as e2e/flowText/chains reports it. */
export interface IFlowChainGroup {
    /** The id of the page the group is on, which is what goToPage takes. */
    pageId: string;
    /** Where that page comes in the book, counting from zero. */
    pageIndex: number;
    /** Where the group comes among the page's translation groups, counting from zero. */
    indexInPage: number;
    /** The text of the group's box in each language, by language tag. */
    textByLang: Record<string, string>;
}

/** One chain of linked text boxes in the whole book. */
export interface IFlowChain {
    chainId: string;
    groups: IFlowChainGroup[];
}

/**
 * Wait until nothing is moving text any more: no pass is pending in the browser, C# has no
 * cross-page work outstanding, and Bloom is editing the page rather than loading it.
 *
 * Call this after anything that changes the text or the links, before reading the result.
 *
 * It waits only for work that is RUNNING. A refit of the pages the browser is not editing that
 * is merely waiting to be run leaves this function satisfied, because nothing is going to run it
 * until the user changes pages or asks for it: waiting for that would wait for ever. So a test
 * that reads a page other than the one it is editing has to run the waiting refit first, with
 * clickReflowNow or runPendingReflow, or by turning a page.
 */
export async function waitForReflowIdle(page: Page): Promise<void> {
    await waitForEditablePage(page);
    // Which character falls on which line depends on the fonts, so the page settles a second
    // time once the real font replaces the fallback. Wait for the fonts before anything else,
    // or that second pass starts after this function has said the page is quiet.
    await page
        .frame({ name: "page" })
        ?.evaluate(() => document.fonts.ready.then(() => true))
        .catch(() => true);

    // Both sides have to be quiet, and stay quiet. The browser marks the page while it works
    // and Bloom answers for the work it does on pages the browser cannot see, but a pass that
    // is about to start shows in neither, so one quiet reading proves nothing.
    let quietRounds = 0;
    await expect
        .poll(
            async () => {
                const marked = await editablePageFrame(page)
                    .locator(".bloom-page[data-flow-reflowing]")
                    .count()
                    .catch(() => 1);
                const idle =
                    (await apiGet(page, "e2e/flowText/isIdle")).body === "true";
                quietRounds = marked === 0 && idle ? quietRounds + 1 : 0;
                return quietRounds;
            },
            {
                // A refit lays out every page of a chain off-screen, one at a time, and a chain
                // of a dozen pages takes well over half a minute. This only caps how long a
                // failure takes to report: quiet rounds are what say the flow is idle.
                timeout: 180000,
                intervals: [100, 150, 200, 250, 250, 500],
                message:
                    "The flow never went quiet: the page kept the reflowing mark, or Bloom " +
                    "kept reporting cross-page work.",
            },
        )
        .toBeGreaterThanOrEqual(4);
}

/**
 * Build the page these tests are about: a content page with two ordinary text boxes, one above
 * the other, and a picture between them. That is Basic Book's "Image in Middle", and it is the
 * simplest page on which text can flow from one box to another without leaving the page.
 *
 * Returns the id of the page it added, and leaves the Edit tab showing it.
 */
export async function makeTwoBoxFlowPage(page: Page): Promise<string> {
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPageWithId(page, kImageInMiddlePageId);
    const added = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    );
    if (!added)
        throw new Error(
            'Bloom reported no new content page after adding "Image in Middle".',
        );
    await goToPage(page, added.id);
    await waitForReflowIdle(page);
    return added.id;
}

/**
 * Add a page that is one text box and nothing else, which is where text that crosses pages
 * goes. Returns the id of the page it added, and leaves the Edit tab showing it.
 */
export async function addJustTextPage(page: Page): Promise<string> {
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPageWithId(page, kJustTextPageId);
    const added = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    );
    if (!added)
        throw new Error(
            'Bloom reported no new content page after adding "Just Text".',
        );
    await goToPage(page, added.id);
    await waitForReflowIdle(page);
    return added.id;
}

/** How many content pages the book has: front and back matter do not count. */
export async function getPageCount(page: Page): Promise<number> {
    return (await getContentPages(page)).length;
}

/**
 * Put a body of text into a box the way a person pastes it: the box is given the focus, whatever
 * it held is selected, and the text arrives in one go rather than a character at a time.
 *
 * Returns when the flow has settled, so the caller can read where the text went.
 */
export async function pasteText(
    page: Page,
    boxIndex: number,
    text: string,
    language = "en",
): Promise<void> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    await clickIntoBox(box);
    await expect(
        box,
        `Clicking box ${boxIndex} did not give it the focus, so the text would go elsewhere.`,
    ).toBeFocused({ timeout: 15000 });
    await box.press("Control+A");
    await page.keyboard.insertText(text);
    await waitForReflowIdle(page);
}

/**
 * The words on this box's offer to continue an earlier box's text, or undefined when it makes
 * none. The label says where the text is now, so a test about a source on another page reads it.
 */
export async function getContinueButtonLabel(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<string | undefined> {
    await waitForReflowIdle(page);
    const button = continueButton(page, boxIndex, language);
    if ((await button.count()) === 0) {
        return undefined;
    }

    return (await button.textContent()) ?? undefined;
}

/**
 * Build the page these tests start from: a two-box page whose top box holds more text than
 * fits, with the second box linked to it so that the extra text has flowed down into it.
 *
 * Returns the id of that page, and leaves the Edit tab showing it.
 */
export async function makeLinkedTwoBoxPage(page: Page): Promise<string> {
    const pageId = await makeTwoBoxFlowPage(page);
    await typeParagraphAtEnd(page, 0, kTextTooLongForOneBox);
    await clickContinueText(page, 1);
    return pageId;
}

/**
 * One ordinary text box of the page being edited, by its place in the flow order. `language` is
 * the language tag of the box inside the translation group; a page shows one box per content
 * language, and each language's text flows through its own boxes.
 */
export function flowBox(
    page: Page,
    boxIndex: number,
    language = "en",
): Locator {
    return editablePageFrame(page)
        .locator(
            `.bloom-translationGroup > .bloom-editable.normal-style.bloom-visibility-code-on[lang="${language}"]`,
        )
        .nth(boxIndex);
}

/**
 * The text of every ordinary text box of the page being edited, in flow order. This is how a
 * test says where the text ended up without knowing how it got there.
 */
export async function getBoxTexts(
    page: Page,
    language = "en",
): Promise<string[]> {
    await waitForReflowIdle(page);
    return editablePageFrame(page)
        .locator(
            `.bloom-translationGroup > .bloom-editable.normal-style.bloom-visibility-code-on[lang="${language}"]`,
        )
        .evaluateAll((boxes) =>
            boxes.map((box) => {
                // Two zero-width characters live in an edited box without being any part of
                // what the reader sees: the mark that says where the text stops fitting
                // (U+200C), and the filler CKEditor keeps at the end of a paragraph it owns
                // (U+200B). hasOverflowMarker is what asks about the mark itself.
                const invisible = new RegExp(
                    "[" + String.fromCharCode(8203, 8204) + "]",
                    "g",
                );
                return (box.textContent ?? "").replace(invisible, "");
            }),
        );
}

/**
 * The text of each paragraph of one box of the page being edited, in order. A test that compares
 * a whole run of text needs the paragraphs apart, because the boundary between two paragraphs is
 * a place words could go missing just as a boundary between two boxes is.
 */
export async function getBoxParagraphTexts(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<string[]> {
    await waitForReflowIdle(page);
    return flowBox(page, boxIndex, language)
        .locator(":scope > p")
        .evaluateAll((paragraphs) =>
            paragraphs.map((paragraph) => {
                // Two zero-width characters live in an edited box without being any part of
                // what the reader sees: the mark that says where the text stops fitting
                // (U+200C), and the filler CKEditor keeps at the end of a paragraph it owns
                // (U+200B). hasOverflowMarker is what asks about the mark itself.
                const invisible = new RegExp(
                    "[" + String.fromCharCode(8203, 8204) + "]",
                    "g",
                );
                return (paragraph.textContent ?? "").replace(invisible, "");
            }),
        );
}

/**
 * The whole run of text of the book's one chain, one entry per box, in the order the text flows
 * through the boxes, with the paragraphs of each box separated by a space. This is what
 * assertRunIsIntact reads.
 *
 * It visits each page of the chain in turn, which is also what saves the page being edited, so
 * the caller gets what is on disk rather than what C# last knew. It leaves the Edit tab on the
 * LAST page of the chain: a caller that carries on editing says which page it wants next.
 *
 * Throws when the book holds no chain, or more than one: a test that reads a run of text is a
 * test about one run of text.
 */
export async function getRunTexts(
    page: Page,
    language = "en",
): Promise<string[]> {
    // A refit of the pages the browser is not editing that is still waiting to be run would make
    // this a reading of what those pages held before the change that asked for it. Visiting them
    // below is a page turn, which is one of the two things that starts such a refit, so the
    // reading would race it: run it first instead.
    await runPendingReflow(page);
    // Leaving the page and coming back is what writes it into the book, so that the chain C#
    // reports below includes whatever has just been typed.
    await reloadPageBeingEdited(page);
    const chains = await getBookChains(page);
    if (chains.length !== 1)
        throw new Error(
            `The book holds ${chains.length} chains of linked text boxes, not one. ` +
                `Their ids: ${chains.map((chain) => chain.chainId).join(", ") || "(none)"}.`,
        );

    const texts: string[] = [];
    for (const group of chains[0].groups) {
        await goToPage(page, group.pageId);
        const paragraphs = await getBoxParagraphTexts(
            page,
            group.indexInPage,
            language,
        );
        texts.push(paragraphs.join(" "));
    }
    return texts;
}

/**
 * Does this box carry the mark that says where its text stops fitting? OverflowChecker puts it
 * in, and it is what a later empty box offers to continue from.
 */
export async function hasOverflowMarker(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<boolean> {
    await waitForReflowIdle(page);
    return (
        (await flowBox(page, boxIndex, language)
            .locator("span.bloom-overflowStart")
            .count()) > 0
    );
}

/**
 * How many characters of a box's text fit in it: the number of characters before the mark that
 * says where the text stops fitting. Throws when the box carries no such mark, which means it
 * holds no more than fits.
 *
 * This is how a test asks how much a page holds, so that it can then put in a known amount more
 * than that rather than guessing at a page's capacity.
 */
export async function getOverflowMarkerOffset(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<number> {
    await waitForReflowIdle(page);
    const offset = await flowBox(page, boxIndex, language).evaluate((box) => {
        const marker = box.querySelector("span.bloom-overflowStart");
        if (!marker) return -1;
        const range = box.ownerDocument.createRange();
        range.setStart(box, 0);
        range.setEndBefore(marker);
        const invisible = new RegExp(
            "[" + String.fromCharCode(8203, 8204) + "]",
            "g",
        );
        return range.toString().replace(invisible, "").length;
    });
    if (offset < 0)
        throw new Error(
            `Box ${boxIndex} carries no mark saying where its text stops fitting, so it holds ` +
                `no more than fits. Put more text in it first.`,
        );
    return offset;
}

/**
 * Build the pages that a run of text crossing pages needs: two "Just Text" pages, linked, whose
 * first page holds all it can and whose second page holds only the little that did not fit.
 *
 * The amount of text is worked out from the page rather than fixed, by asking the first page
 * where its text stops fitting and then putting in that much plus a little. A test that fixed
 * the amount would be one page-size change away from testing nothing.
 *
 * `paragraphCount` says how many paragraphs the text is typed as; a test that puts the caret in
 * the middle of the text needs more than one. Returns the two page ids and the paragraphs that
 * went in, which is what assertRunIsIntact is given. Leaves the Edit tab on the second page.
 */
export async function makeTwoLinkedJustTextPages(
    page: Page,
    paragraphCount = 3,
): Promise<{
    firstPageId: string;
    secondPageId: string;
    paragraphs: string[];
}> {
    const firstPageId = await addJustTextPage(page);
    // Far more text than fits, only so that the page will say where its text stops fitting.
    await pasteText(page, 0, kTextForSeveralPages);
    const fits = await getOverflowMarkerOffset(page, 0);

    // A little more than fits, so the second page ends up with a little text rather than much.
    const paragraphs = splitAtWordBoundaries(
        kTextForSeveralPages.slice(0, fits + 40),
        paragraphCount,
    );
    await typeParagraphs(page, 0, paragraphs);

    const secondPageId = await addJustTextPage(page);
    await clickContinueText(page, 0);
    return { firstPageId, secondPageId, paragraphs };
}

/** Cut a body of text into `count` pieces of about equal length, never inside a word. */
function splitAtWordBoundaries(text: string, count: number): string[] {
    const words = text.split(" ").filter((word) => word.length > 0);
    const perPiece = Math.ceil(words.length / count);
    const pieces: string[] = [];
    for (let i = 0; i < words.length; i += perPiece) {
        pieces.push(words.slice(i, i + perPiece).join(" "));
    }
    return pieces;
}

/**
 * Is Bloom warning that this box holds more text than fits? A box that hands its extra text to
 * a following box in its chain must not: the text has somewhere to go.
 */
export async function hasOverflowWarning(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<boolean> {
    await waitForReflowIdle(page);
    // OverflowChecker puts the warning on after the flow has moved the text, and it does not
    // announce itself the way the flow does, so a reading taken the instant the flow goes quiet
    // can be a moment too early. Give the warning a little while to appear; a box that is not
    // going to be marked costs that whole while, which is why the wait is short.
    const box = flowBox(page, boxIndex, language);
    const giveUpAt = Date.now() + kWaitForOverflowWarningMs;
    for (;;) {
        const marked = await box.evaluate((element) =>
            element.classList.contains("overflow"),
        );
        if (marked || Date.now() >= giveUpAt) {
            return marked;
        }

        await page.waitForTimeout(100);
    }
}

/** How long hasOverflowWarning waits for Bloom's own overflow warning to appear. */
const kWaitForOverflowWarningMs = 2000;

/**
 * Does this box's first paragraph carry on from a paragraph that began in the box before it?
 * Such a paragraph gets no first-line indent and no top margin, which is what tells a reader
 * it is the same paragraph.
 */
export async function hasContinuationParagraph(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<boolean> {
    await waitForReflowIdle(page);
    return (
        (await flowBox(page, boxIndex, language)
            .locator(":scope > p[data-flow-continuation]")
            .count()) > 0
    );
}

/** The chain this box's group belongs to, or undefined when it is not linked to any box. */
export async function getChainId(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<string | undefined> {
    await waitForReflowIdle(page);
    const chainId = await flowBox(page, boxIndex, language).evaluate(
        (box) =>
            box
                .closest(".bloom-translationGroup")
                ?.getAttribute("data-flow-chain") ?? null,
    );
    return chainId ?? undefined;
}

/** Every chain of linked text boxes in the whole book, however many pages they cross. */
export async function getBookChains(page: Page): Promise<IFlowChain[]> {
    await waitForReflowIdle(page);
    return apiGetJson<IFlowChain[]>(page, "e2e/flowText/chains");
}

/**
 * Is this box offering to continue the text of an earlier box? The offer is a button drawn over
 * the middle of the empty box.
 */
export async function isContinueButtonShown(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<boolean> {
    await waitForReflowIdle(page);
    return continueButton(page, boxIndex, language).isVisible();
}

/**
 * Take the offer: click the button in this box that says it will continue the text of an
 * earlier one, and wait until the text has arrived and the flow has settled.
 */
export async function clickContinueText(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<void> {
    const button = continueButton(page, boxIndex, language);
    await button.waitFor({ state: "visible", timeout: 30000 });
    await button.click();
    await expect(
        button,
        "The box still offers to continue the text after the offer was taken.",
    ).toHaveCount(0, { timeout: 30000 });
    await waitForReflowIdle(page);
}

/**
 * Is this box offering to have Bloom make the pages the rest of its text needs? The offer is a
 * button at the bottom right inside the box, and it appears only where a run of text ends with
 * more text still to place.
 */
export async function isCreatePagesButtonShown(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<boolean> {
    await waitForReflowIdle(page);
    return createPagesButton(page, boxIndex, language).isVisible();
}

/**
 * The words on this box's offer to make pages for the rest of its text, or undefined when it
 * makes none.
 */
export async function getCreatePagesButtonLabel(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<string | undefined> {
    await waitForReflowIdle(page);
    const button = createPagesButton(page, boxIndex, language);
    if ((await button.count()) === 0) {
        return undefined;
    }

    return (await button.textContent()) ?? undefined;
}

/**
 * Take the offer: click the button that has Bloom make the pages the rest of this box's text
 * needs, and wait until every page has been made and filled and the flow has settled.
 *
 * Bloom lays each new page out off-screen to find where its text stops fitting, which takes a
 * second or so per page, so the wait here is long by the standards of the other helpers.
 */
export async function clickCreatePagesAndContinue(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<void> {
    const button = createPagesButton(page, boxIndex, language);
    await button.waitFor({ state: "visible", timeout: 30000 });
    await button.click();
    await expect(
        button,
        "The box still offers to make pages after the offer was taken.",
    ).toHaveCount(0, { timeout: 120000 });
    await waitForReflowIdle(page);
}

/**
 * Separate this box, and every box after it, from its chain, by right-clicking its text and
 * choosing "Unlink text box" from the menu Bloom puts up. The text stays where it is.
 */
export async function unlinkTextBox(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<void> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    // The menu comes up only for a right-click on a paragraph of the text, and it is rendered
    // into the page's own document rather than into the Edit tab's shell.
    await box.locator("p").first().click({ button: "right" });
    const item = editablePageFrame(page).getByTestId(
        "EditTab.TextContextMenu.UnlinkTextBox",
    );
    await item.waitFor({ state: "visible", timeout: 30000 });
    await item.click();
    await expect
        .poll(async () => getChainId(page, boxIndex, language), {
            timeout: 30000,
            message: "The box is still linked after Unlink text box.",
        })
        .toBeUndefined();
    await waitForReflowIdle(page);
}

/**
 * Which box the caret is in, by its place in the flow order, or undefined when the caret is not
 * in one of the page's ordinary text boxes. This is how a test checks that a person's typing
 * carries on where they were typing, rather than following the text that moved away.
 */
export async function getCaretOwner(page: Page): Promise<number | undefined> {
    await waitForReflowIdle(page);
    const index = await editablePageFrame(page).evaluate(() => {
        const anchor = document.getSelection()?.anchorNode;
        const from =
            anchor instanceof Element
                ? anchor
                : (anchor?.parentElement ?? null);
        const holder = from?.closest(".bloom-editable");
        if (!holder) return -1;
        // Count only the boxes of the caret's own language: each language's text flows
        // through its own boxes, so that is the order the caller is asking about.
        const boxes = Array.from(
            document.querySelectorAll(
                ".bloom-translationGroup > .bloom-editable.normal-style.bloom-visibility-code-on" +
                    `[lang="${holder.getAttribute("lang") ?? ""}"]`,
            ),
        );
        return boxes.indexOf(holder);
    });
    return index < 0 ? undefined : index;
}

/**
 * Fill a box with several paragraphs, the way a person does: type a paragraph, press Enter, type
 * the next. Whatever the box held is replaced.
 *
 * A test that puts the caret in the middle of a box's text needs the box to have a paragraph
 * there to put it at the end of, which one long pasted paragraph does not give it.
 *
 * Returns when the flow has settled, so the caller can read where the text went.
 */
export async function typeParagraphs(
    page: Page,
    boxIndex: number,
    paragraphs: string[],
    language = "en",
): Promise<void> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    await clickIntoBox(box);
    await expect(
        box,
        `Clicking box ${boxIndex} did not give it the focus, so the text would go elsewhere.`,
    ).toBeFocused({ timeout: 15000 });
    await box.press("Control+A");
    for (let i = 0; i < paragraphs.length; i++) {
        if (i > 0) await page.keyboard.press("Enter");
        await page.keyboard.insertText(paragraphs[i]);
    }
    await waitForReflowIdle(page);
}

/**
 * Start a new paragraph after the paragraph numbered `paragraphIndex` of a box and type into it,
 * the way a person adds a paragraph in the middle of what they have written: the caret goes to
 * the end of that paragraph, Enter starts a new one, and the words go in there.
 *
 * The text is inserted in one go apart from the last 20 characters, which are typed with real key
 * presses, for the same reason as in typeParagraphAtEnd.
 *
 * Returns when the flow has settled, so the caller can read where the text went.
 */
export async function typeNewParagraphAfter(
    page: Page,
    boxIndex: number,
    paragraphIndex: number,
    text: string,
    language = "en",
): Promise<void> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    const paragraph = box.locator(":scope > p").nth(paragraphIndex);
    const found = await paragraph.count();
    if (found === 0) {
        const all = await box.locator(":scope > p").count();
        throw new Error(
            `Box ${boxIndex} has no paragraph ${paragraphIndex}; it has ${all}.`,
        );
    }
    // Click the paragraph so that the box has the focus, then put the caret at the paragraph's
    // end. The End key would not do: in a paragraph that wraps over several lines it goes to the
    // end of the line the caret is on, not to the end of the paragraph.
    await paragraph.click();
    await expect(
        box,
        `Clicking paragraph ${paragraphIndex} of box ${boxIndex} did not give the box the focus.`,
    ).toBeFocused({ timeout: 15000 });
    await paragraph.evaluate((element) => {
        const range = element.ownerDocument.createRange();
        range.selectNodeContents(element);
        range.collapse(false);
        const selection = element.ownerDocument.getSelection();
        selection?.removeAllRanges();
        selection?.addRange(range);
    });
    // Enter is a real key press, because it is Bloom's editor that turns it into a paragraph.
    await page.keyboard.press("Enter");

    const keyed = text.slice(Math.max(0, text.length - 20));
    const inserted = text.slice(0, text.length - keyed.length);
    if (inserted) await page.keyboard.insertText(inserted);
    if (keyed) await page.keyboard.type(keyed);
    await waitForReflowIdle(page);
}

/**
 * Make the text of a box twice the size it is now, through the Format dialog, and wait until the
 * flow has settled. Doubling the size is how a test makes a page hold much less text without
 * touching the text itself, so whatever no longer fits has to move on to the next box.
 *
 * Returns the new size in points. The caller is left on the page it was on.
 */
export async function doubleFontSizeOfBox(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<number> {
    return scaleFontSizeOfBox(page, boxIndex, 2, language);
}

/**
 * Multiply the font size of the style of this box by this factor through the Format dialog, and
 * return the new size in points. The style belongs to the whole book, so every box of that
 * style on every page is drawn at the new size. The caller is left on the page it was on.
 */
export async function scaleFontSizeOfBox(
    page: Page,
    boxIndex: number,
    factor: number,
    language = "en",
): Promise<number> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    await clickIntoBox(box);
    await expect(
        box,
        `Clicking box ${boxIndex} did not give it the focus, so it would show no format gear.`,
    ).toBeFocused({ timeout: 15000 });
    // The caret goes to the start of the box. A bigger font pushes the tail of the text onto
    // the next page, and a caret in that tail goes with it: Bloom turns to the next page, and
    // that page turn is what runs a waiting refit. The caller is to stay on this page.
    await box.press("Control+Home");

    // The dialog works in points and the box is drawn in pixels, at 96 dpi in the page frame.
    const pixels = await getRenderedFontSize(box);
    const points = Math.round((pixels * 72) / 96);
    const scaled = Math.round(points * factor);
    await setFontSizeWithFormatDialog(page, box, scaled);
    await waitForReflowIdle(page);
    return scaled;
}

/**
 * Click a box to give it the focus. An empty box that could take an earlier box's text carries
 * its offer as a button at its top left corner, and a click there would take the offer, so such
 * a box is clicked at the middle of its bottom edge instead (its bottom left corner holds the
 * Format cog). Any other box is clicked in the centre: the top edge of a linked box carries the
 * band that says its text flows in, which is not the box.
 */
async function clickIntoBox(box: Locator): Promise<void> {
    const offer = box
        .locator("xpath=..")
        .locator(":scope > .bloom-flow-continue");
    if ((await offer.count()) > 0) {
        const bounds = await box.boundingBox();
        if (!bounds) {
            throw new Error(
                "The box to click has no bounds, so it is not on screen.",
            );
        }
        await box.click({
            position: { x: bounds.width / 2, y: bounds.height - 6 },
        });
    } else {
        await box.click();
    }
}

/**
 * Add a paragraph of text at the end of a box, the way a person does: put the caret at the end
 * and type. The bulk of the text is inserted in one go, because the cost of a key press per
 * character grows with the length and says nothing new; the last 20 characters are typed with
 * real key presses, so that whatever in Bloom listens for a key does see some.
 *
 * Returns when the flow has settled, so the caller can read where the text went.
 */
export async function typeParagraphAtEnd(
    page: Page,
    boxIndex: number,
    text: string,
    language = "en",
): Promise<void> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    await clickIntoBox(box);
    await expect(
        box,
        `Clicking box ${boxIndex} did not give it the focus, so the typing would go elsewhere.`,
    ).toBeFocused({ timeout: 15000 });
    await box.press("Control+End");

    const keyed = text.slice(Math.max(0, text.length - 20));
    const inserted = text.slice(0, text.length - keyed.length);
    if (inserted) await page.keyboard.insertText(inserted);
    if (keyed) await page.keyboard.type(keyed);
    await waitForReflowIdle(page);
}

/**
 * Empty a box the way a person does: select everything in it and press Delete. Text that had
 * moved on to a following box comes back into the room this frees, so the caller reads both.
 */
export async function clearBox(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<void> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    await clickIntoBox(box);
    await expect(
        box,
        `Clicking box ${boxIndex} did not give it the focus, so Delete would go elsewhere.`,
    ).toBeFocused({ timeout: 15000 });
    await box.press("Control+A");
    await page.keyboard.press("Delete");
    await waitForReflowIdle(page);
}

/**
 * Put the caret at the very start of a box and press a key, and wait until the flow has
 * settled. Backspace there is the gesture that pulls text back from a box into the one above
 * it, so a test about that gesture needs the key press to land at offset 0 of that box.
 */
export async function pressKeyAtStartOfBox(
    page: Page,
    boxIndex: number,
    key: string,
    language = "en",
): Promise<void> {
    await pressKeyInBox(page, boxIndex, key, "Control+Home", language);
}

/**
 * Put the caret at the very end of a box and press a key, and wait until the flow has settled.
 * Delete there is the gesture that pulls the next box's first character back.
 */
export async function pressKeyAtEndOfBox(
    page: Page,
    boxIndex: number,
    key: string,
    language = "en",
): Promise<void> {
    await pressKeyInBox(page, boxIndex, key, "Control+End", language);
}

async function pressKeyInBox(
    page: Page,
    boxIndex: number,
    key: string,
    moveCaret: string,
    language: string,
): Promise<void> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    await clickIntoBox(box);
    await expect(
        box,
        `Clicking box ${boxIndex} did not give it the focus, so "${key}" would go elsewhere.`,
    ).toBeFocused({ timeout: 15000 });
    await box.press(moveCaret);
    await page.keyboard.press(key);
    await waitForReflowIdle(page);
}

/**
 * Put talking-book markup on a box, as Bloom's own recording tool would. A box with recorded
 * audio cannot take part in the flow: moving its text would leave the recording pointing at
 * words that are no longer there. Seeded rather than recorded, because recording needs a
 * microphone (AUTOMATION-DEBT.md, "Narration cannot be recorded in a test").
 */
export async function seedTalkingBookMarkup(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<void> {
    await flowBox(page, boxIndex, language).evaluate((box) => {
        box.setAttribute("data-audiorecordingmode", "TextBox");
        box.classList.add("audio-sentence");
        box.setAttribute("id", "e2e-audio-sentence");
    });
    await waitForReflowIdle(page);
}

/**
 * The words on this box's label saying that its text continues from a linked box on an earlier
 * page, or undefined when it wears none.
 */
export async function getFlowsFromLabel(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<string | undefined> {
    await waitForReflowIdle(page);
    const label = flowsFromLabel(page, boxIndex, language);
    if ((await label.count()) === 0) {
        return undefined;
    }

    return (await label.textContent()) ?? undefined;
}

/**
 * The words on this box's label saying that its text continues into a linked box on a later
 * page, or undefined when it wears none.
 */
export async function getFlowsToLabel(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<string | undefined> {
    await waitForReflowIdle(page);
    const label = flowsToLabel(page, boxIndex, language);
    if ((await label.count()) === 0) {
        return undefined;
    }

    return (await label.textContent()) ?? undefined;
}

/**
 * Put the pointer on the page being edited, which is what makes the flow labels show. The
 * corner is used rather than the middle, so that the pointer is on the page and on nothing that
 * answers to it.
 */
export async function hoverPageBeingEdited(page: Page): Promise<void> {
    await editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .hover({ position: { x: 4, y: 4 } });
}

/** Whether the pointer being on the page is what decides that this box's labels are showing. */
export async function areFlowLabelsShown(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<boolean> {
    await waitForReflowIdle(page);
    const labels = [
        flowsFromLabel(page, boxIndex, language),
        flowsToLabel(page, boxIndex, language),
    ];
    const shown = await Promise.all(
        labels.map(async (label) =>
            (await label.count()) > 0 ? label.isVisible() : false,
        ),
    );
    return shown.some((one) => one);
}

/**
 * Which of this box's flow labels are drawn over a line of its text. The answer is the labels'
 * test ids, so a failure names the label that is in the way; an empty list is what the labels
 * are for, since a label over the text hides the words the person is editing.
 *
 * The lines are measured with a Range over each paragraph rather than from the paragraph's own
 * box, because a paragraph's box is as wide as the column while its last line may be much
 * shorter, and a label beside that short line is not over any text.
 */
export async function getFlowLabelsOverText(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<string[]> {
    await waitForReflowIdle(page);
    return flowBox(page, boxIndex, language).evaluate((editable) => {
        const group = editable.closest(".bloom-translationGroup")!;
        const lines: DOMRect[] = [];
        Array.from(editable.querySelectorAll("p")).forEach((paragraph) => {
            const range = paragraph.ownerDocument.createRange();
            range.selectNodeContents(paragraph);
            lines.push(...Array.from(range.getClientRects()));
        });

        const labels = Array.from(
            group.querySelectorAll<HTMLElement>(
                '[data-testid="flow-text-flows-from"], [data-testid="flow-text-flows-to"]',
            ),
        );
        return labels
            .filter((label) => {
                const box = label.getBoundingClientRect();
                return lines.some(
                    (line) =>
                        box.left < line.right &&
                        box.right > line.left &&
                        box.top < line.bottom &&
                        box.bottom > line.top,
                );
            })
            .map((label) => label.getAttribute("data-testid")!);
    });
}

function flowsFromLabel(
    page: Page,
    boxIndex: number,
    language: string,
): Locator {
    // The label is a sibling of the box inside the translation group, and it says which
    // language's box it belongs to, because a group can hold one box per language.
    return flowBox(page, boxIndex, language)
        .locator(
            `xpath=following-sibling::*[@data-testid="flow-text-flows-from" and @data-flow-from-lang="${language}"]`,
        )
        .first();
}

function flowsToLabel(page: Page, boxIndex: number, language: string): Locator {
    // The label is a sibling of the box inside the translation group, and it says which
    // language's box it belongs to, because a group can hold one box per language.
    return flowBox(page, boxIndex, language)
        .locator(
            `xpath=following-sibling::*[@data-testid="flow-text-flows-to" and @data-flow-to-lang="${language}"]`,
        )
        .first();
}

function continueButton(
    page: Page,
    boxIndex: number,
    language: string,
): Locator {
    // The button is a sibling of the box inside the translation group, and it says which
    // language's box it belongs to, because a group can hold one box per language.
    return flowBox(page, boxIndex, language)
        .locator(
            `xpath=following-sibling::*[@data-testid="flow-text-continue" and @data-flow-continue-lang="${language}"]`,
        )
        .first();
}

function createPagesButton(
    page: Page,
    boxIndex: number,
    language: string,
): Locator {
    // The button is a sibling of the box inside the translation group, and it says which
    // language's box it belongs to, because a group can hold one box per language.
    return flowBox(page, boxIndex, language)
        .locator(
            `xpath=following-sibling::*[@data-testid="flow-text-create-pages" and @data-flow-create-pages-lang="${language}"]`,
        )
        .first();
}

/** The titles of the progress dialogs the flow-text work shows; see BloomMediumPriority.xlf. */
const kFlowProgressDialogTitles =
    /Flowing text across pages|Creating pages and flowing text/;

/**
 * Whether the Edit tab's progress dialog is showing one of the flow-text titles. The dialog is
 * rendered in the top document (App.tsx mounts it), not in the page iframe.
 */
export async function isProgressDialogOpen(page: Page): Promise<boolean> {
    return await page
        .getByRole("dialog")
        .filter({ hasText: kFlowProgressDialogTitles })
        .first()
        .isVisible();
}

// The bubble a chained box gets while a refit of the pages the browser is not editing waits to be
// run, and the two things the user can say about it. Text never moves across pages on its own: a
// change that leaves the later pages of a chain out of date only records that with Bloom, and the
// refit happens when the user clicks Reflow now or turns a page (see flowReflowBubble.tsx and
// FlowTextWalk).
//
// The bubble is not inside the group it belongs to: it sits beside the page, in the container that
// carries the page zoom, where the source bubbles go. So it is found by its test id anywhere in
// the page being edited, and nothing here asks which box a bubble belongs to.
//
// A bubble is there only while that group's chain has a refit waiting, and it goes when the refit
// has run. So the bubble being there is itself the answer to "does the page say a refit is
// waiting", which is what isReflowPendingShown asks, and its button can always be clicked.
//
// The test ids are kReflowBubbleTestId, kReflowOnPageChangeTestId and kReflowNowTestId in
// bookEdit/flowText/flowConstants.ts.
const kReflowBubbleTestId = "flow-reflow-bubble";
const kReflowOnPageChangeTestId = "flow-reflow-on-page-change";
const kReflowNowTestId = "flow-reflow-now";
const kAutoPagesTestId = "flow-reflow-auto-pages";

/** How long clickReflowNow watches for the refit it asked for to start. */
const kWaitForReflowToStartMs = 5000;

/** What Bloom is holding, as GET flowText/pendingWalks reports it. */
export interface IPendingWalks {
    /** Is there a refit waiting to be run? */
    pending: boolean;
    /** The chains, by chain id, that have a refit waiting. */
    chainIds: string[];
    /** Whether turning a page runs the waiting refits, which is this book's own setting. */
    reflowOnPageChange: boolean;
}

/** What Bloom is holding: the refits waiting to be run, and the setting that governs them. */
export async function getPendingWalks(page: Page): Promise<IPendingWalks> {
    return apiGetJson<IPendingWalks>(page, "flowText/pendingWalks");
}

/**
 * Is a refit of the pages the browser is not editing waiting to be run? This is what the bubble
 * is shown for, and what a test asks before reading a page it is not editing: the answer being
 * true means those pages hold what they held before the change.
 */
export async function isWalkPending(page: Page): Promise<boolean> {
    return (await getPendingWalks(page)).pending;
}

/** Whether turning a page runs the waiting refits. The book's own setting. */
export async function getReflowOnPageChange(page: Page): Promise<boolean> {
    return (
        await apiGetJson<{ value: boolean }>(
            page,
            "flowText/reflowOnPageChange",
        )
    ).value;
}

/**
 * Say whether turning a page runs the waiting refits, through Bloom's API rather than through the
 * bubble. This is for a test that needs the setting a particular way to start from, and for
 * putting it back afterwards; a test about the checkbox itself uses
 * setReflowOnPageChangeViaBubble.
 */
export async function setReflowOnPageChange(
    page: Page,
    value: boolean,
): Promise<void> {
    await apiPost(
        page,
        "flowText/reflowOnPageChange",
        JSON.stringify({ value }),
        "application/json",
    );
    expect(
        await getReflowOnPageChange(page),
        "Bloom did not take the new value of the reflow-on-page-change setting.",
    ).toBe(value);
}

/** Every bubble showing beside the page being edited. */
function reflowBubbles(page: Page): Locator {
    return editablePageFrame(page).locator(
        `[data-testid="${kReflowBubbleTestId}"]`,
    );
}

/** The first bubble showing beside the page being edited. */
function reflowBubble(page: Page): Locator {
    return reflowBubbles(page).first();
}

function reflowNowButton(page: Page): Locator {
    return reflowBubble(page).locator(`[data-testid="${kReflowNowTestId}"]`);
}

function reflowOnPageChangeCheckbox(page: Page): Locator {
    // Whichever bubble is nearest to hand: the setting is the book's, so every bubble on the page
    // shows the same value and any of them can be used to change it.
    return reflowBubble(page).locator(
        `[data-testid="${kReflowOnPageChangeTestId}"]`,
    );
}

/**
 * Does the page being edited say that a refit of the pages after it is waiting to be run? A
 * chained group has a bubble while its chain has one waiting, and no bubble once it has run, so
 * this is the page's own answer; flowText/pendingWalks, through isWalkPending, is Bloom's.
 */
export async function isReflowPendingShown(page: Page): Promise<boolean> {
    await waitForReflowIdle(page);
    return reflowBubble(page).isVisible();
}

/** How many bubbles are showing beside the page being edited: one per chained group on it. */
export async function getReflowBubbleCount(page: Page): Promise<number> {
    await waitForReflowIdle(page);
    return reflowBubbles(page).count();
}

/** The three things the bubble says. */
export interface IReflowBubbleTexts {
    /** That a refit is waiting. */
    pending: string;
    /** The words beside the checkbox that has every page change run it. */
    reflowOnPageChange: string;
    /** The words on the button that runs it now. */
    reflowNow: string;
}

/**
 * What the bubble says, so a test can check the wording an author reads. Throws when the page
 * being edited shows no bubble, which is what it shows when no refit is waiting.
 */
export async function getReflowBubbleTexts(
    page: Page,
): Promise<IReflowBubbleTexts> {
    const bubble = reflowBubble(page);
    await bubble.waitFor({ state: "visible", timeout: 30000 });
    return bubble.evaluate(
        (element, testIds) => {
            const collapse = (text: string | null | undefined) =>
                (text ?? "").replace(/\s+/g, " ").trim();
            const checkbox = element.querySelector(
                `[data-testid="${testIds.onPageChange}"]`,
            );
            return {
                // The words that say a refit is waiting are the bubble's own first line: they
                // are the only text in it that belongs to neither the checkbox nor the button.
                pending: collapse(
                    element.querySelector(":scope > div > div")?.textContent,
                ),
                reflowOnPageChange: collapse(
                    checkbox?.closest("label")?.textContent,
                ),
                reflowNow: collapse(
                    element.querySelector(`[data-testid="${testIds.now}"]`)
                        ?.textContent,
                ),
            };
        },
        { onPageChange: kReflowOnPageChangeTestId, now: kReflowNowTestId },
    );
}

/** What was seen of the refit while it ran. */
export interface IReflowNowObservations {
    /** Whether the progress dialog that stops the author editing was seen. */
    sawProgressDialog: boolean;
    /** Whether Bloom reported itself busy moving text. */
    sawBusy: boolean;
}

/**
 * Click Reflow now in the bubble, and wait until the refit it asks for is over.
 *
 * The refit is seconds of work behind a progress dialog, but a chain of two short pages can be
 * done between two readings, so neither the dialog nor Bloom's own word that it is busy is
 * waited for: both are watched for and reported, and a refit that was over before either could
 * be seen is not a failure. What the caller can rely on is that the refit is finished when this
 * returns.
 */
export async function clickReflowNow(
    page: Page,
): Promise<IReflowNowObservations> {
    const button = reflowNowButton(page);
    await button.waitFor({ state: "visible", timeout: 30000 });
    await button.click();

    const observations: IReflowNowObservations = {
        sawProgressDialog: false,
        sawBusy: false,
    };
    // The browser lets this page finish its own moves before it asks Bloom to refit the rest
    // (reflowNow, in flowReflowBubble.tsx), so the work does not start with the click.
    const watchUntil = Date.now() + kWaitForReflowToStartMs;
    while (Date.now() < watchUntil) {
        if (await isProgressDialogOpen(page)) {
            observations.sawProgressDialog = true;
        }
        if ((await apiGet(page, "e2e/flowText/isIdle")).body !== "true") {
            observations.sawBusy = true;
        }
        if (observations.sawProgressDialog || observations.sawBusy) {
            break;
        }

        await page.waitForTimeout(100);
    }

    await waitForReflowIdle(page);
    return observations;
}

/**
 * Run the refit that is waiting, if one is, and return whether there was one.
 *
 * This is for the tests that are not about the waiting itself: a test that changes something and
 * then reads a page it is not editing needs the later pages brought up to date first, whether or
 * not that particular change is one that leaves a refit waiting. flow-text-reflow-pending.spec.ts
 * is where the waiting is the thing under test.
 *
 * It clicks the button when the page being edited says a refit is waiting, and asks Bloom
 * directly when it does not: a page with no box of the waiting chain on it has no button it
 * could offer, and the button of a chain with nothing waiting is disabled. Neither
 * is the action any of those tests measures, so the API is the fastest reliable path to the state
 * they read (see the UI-vs-API policy in README.md).
 */
export async function runPendingReflow(page: Page): Promise<boolean> {
    if (!(await isWalkPending(page))) {
        return false;
    }

    if (await isReflowPendingShown(page)) {
        await clickReflowNow(page);
    } else {
        await apiPost(page, "flowText/reflowNow", "{}", "application/json");
        await waitForReflowIdle(page);
    }
    return true;
}

/**
 * Tick or untick "Reflow when you change pages" in the bubble, the way an author does, and wait
 * until Bloom holds the new value. Does nothing when the checkbox already says what is wanted.
 *
 * The click lands on the label rather than on the checkbox, which is what a person clicks: the
 * checkbox MUI draws is an input of no opacity over the box it paints.
 */
export async function setReflowOnPageChangeViaBubble(
    page: Page,
    value: boolean,
): Promise<void> {
    const checkbox = reflowOnPageChangeCheckbox(page);
    await checkbox.waitFor({ state: "attached", timeout: 30000 });
    if ((await checkbox.isChecked()) !== value) {
        await checkbox.locator("xpath=ancestor::label[1]").click();
    }

    await expect
        .poll(() => getReflowOnPageChange(page), {
            timeout: 10000,
            message:
                "Bloom never reported the reflow-on-page-change setting the checkbox was " +
                "clicked to give it.",
        })
        .toBe(value);
    expect(
        await checkbox.isChecked(),
        "The checkbox does not show the value Bloom now holds.",
    ).toBe(value);
}

/**
 * Whether Bloom adds and removes pages for a chain by itself when a refit needs them. The book's
 * own setting, which the reflow panel shows as "Automatically add & remove pages".
 */
export async function getAutoPages(page: Page): Promise<boolean> {
    return (await apiGetJson<{ value: boolean }>(page, "flowText/autoPages"))
        .value;
}

/**
 * Say whether Bloom adds and removes pages for a chain by itself, through Bloom's API rather than
 * through the panel. This is for a test that needs the setting a particular way to start from,
 * and for putting it back afterwards; a test about the checkbox itself uses setAutoPagesViaBubble.
 */
export async function setAutoPages(page: Page, value: boolean): Promise<void> {
    await apiPost(
        page,
        "flowText/autoPages",
        JSON.stringify({ value }),
        "application/json",
    );
    expect(
        await getAutoPages(page),
        "Bloom did not take the new value of the automatically-add-and-remove-pages setting.",
    ).toBe(value);
}

function autoPagesCheckbox(page: Page): Locator {
    // Whichever bubble is nearest to hand: the setting is the book's, so every bubble on the page
    // shows the same value and any of them can be used to change it.
    return reflowBubble(page).locator(`[data-testid="${kAutoPagesTestId}"]`);
}

/**
 * Whether the reflow panel on the page being edited shows "Automatically add & remove pages" as
 * ticked. The panel is up only while a refit is waiting, so the caller has to have left one
 * waiting before asking.
 */
export async function isAutoPagesChecked(page: Page): Promise<boolean> {
    const checkbox = autoPagesCheckbox(page);
    await checkbox.waitFor({ state: "attached", timeout: 30000 });
    return checkbox.isChecked();
}

/**
 * Tick or untick "Automatically add & remove pages" in the panel, the way an author does, and wait
 * until Bloom holds the new value. Does nothing when the checkbox already says what is wanted.
 *
 * The click lands on the label rather than on the checkbox, which is what a person clicks: the
 * checkbox MUI draws is an input of no opacity over the box it paints.
 */
export async function setAutoPagesViaBubble(
    page: Page,
    value: boolean,
): Promise<void> {
    const checkbox = autoPagesCheckbox(page);
    await checkbox.waitFor({ state: "attached", timeout: 30000 });
    if ((await checkbox.isChecked()) !== value) {
        await checkbox.locator("xpath=ancestor::label[1]").click();
    }

    await expect
        .poll(() => getAutoPages(page), {
            timeout: 10000,
            message:
                "Bloom never reported the automatically-add-and-remove-pages setting the " +
                "checkbox was clicked to give it.",
        })
        .toBe(value);
    expect(
        await checkbox.isChecked(),
        "The checkbox does not show the value Bloom now holds.",
    ).toBe(value);
}

/**
 * Which of these pages show the red warning triangle on their thumbnail. The triangle is drawn
 * from the page's saved HTML (the pageOverflows class, in PageThumbnail), so it is what an author
 * sees about a page they are not looking at.
 */
export async function pagesWithWarningTriangles(
    page: Page,
    pageIds: string[],
): Promise<string[]> {
    const warned: string[] = [];
    for (const pageId of pageIds) {
        const count = await pageListFrame(page)
            .locator(`.gridItem[id="${pageId}"] .pageOverflowsIcon`)
            .count();
        if (count > 0) {
            warned.push(pageId);
        }
    }
    return warned;
}

/** Wait until every one of these pages has a thumbnail drawn, so a reading of it means something. */
export async function waitForThumbnails(
    page: Page,
    pageIds: string[],
): Promise<void> {
    for (const pageId of pageIds) {
        await expect(
            pageListFrame(page).locator(
                `.gridItem[id="${pageId}"] .pageContainer .bloom-page`,
            ),
            `The thumbnail of page ${pageId} was never drawn, so nothing can be read off it.`,
        ).toHaveCount(1, { timeout: 60000 });
    }
}

/** The id of the page the Edit tab is showing. */
export async function getPageBeingEditedId(page: Page): Promise<string> {
    return (
        (await editablePageFrame(page)
            .locator(".bloom-page[id]")
            .first()
            .getAttribute("id")
            .catch(() => null)) ?? ""
    );
}
