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
    waitForEditablePage,
} from "./bookMaking";

/** The Basic Book template page with a text box, a picture, and a second text box. */
const kImageInMiddlePageId = "adcd48df-e9ab-4a07-afd4-6a24d0398383";

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
    await expect
        .poll(
            async () =>
                editablePageFrame(page)
                    .locator(".bloom-page[data-flow-reflowing]")
                    .count()
                    .catch(() => 1),
            {
                timeout: 30000,
                message:
                    "The page never finished a flow pass: it still carries the reflowing mark.",
            },
        )
        .toBe(0);
    await expect
        .poll(async () => (await apiGet(page, "e2e/flowText/isIdle")).body, {
            timeout: 30000,
            message: "Bloom never reported the flow as idle.",
        })
        .toBe("true");
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
    await box.click();
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
    await box.click();
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
