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
import { apiGet, apiGetJson } from "./api";
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
function buildLongText(minimumLength: number): string {
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
                timeout: 30000,
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
    return flowBox(page, boxIndex, language).evaluate((box) =>
        box.classList.contains("overflow"),
    );
}

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
 * Returns the size in points that it asked for.
 */
export async function doubleFontSizeOfBox(
    page: Page,
    boxIndex: number,
    language = "en",
): Promise<number> {
    const box = flowBox(page, boxIndex, language);
    await box.waitFor({ state: "visible", timeout: 30000 });
    await clickIntoBox(box);
    await expect(
        box,
        `Clicking box ${boxIndex} did not give it the focus, so it would show no format gear.`,
    ).toBeFocused({ timeout: 15000 });

    // The dialog works in points and the box is drawn in pixels, at 96 dpi in the page frame.
    const pixels = await getRenderedFontSize(box);
    const points = Math.round((pixels * 72) / 96);
    const doubled = points * 2;
    await setFontSizeWithFormatDialog(page, box, doubled);
    await waitForReflowIdle(page);
    return doubled;
}

/**
 * Click a box to give it the focus. An empty box that could take an earlier box's text carries
 * its offer as a button in its centre, and a click there would take the offer, so such a box is
 * clicked near its top left corner instead. Any other box is clicked in the centre: the top edge
 * of a linked box carries the band that says its text flows in, which is not the box.
 */
async function clickIntoBox(box: Locator): Promise<void> {
    const offer = box
        .locator("xpath=..")
        .locator(":scope > .bloom-flow-continue");
    if ((await offer.count()) > 0) {
        await box.click({ position: { x: 6, y: 6 } });
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
