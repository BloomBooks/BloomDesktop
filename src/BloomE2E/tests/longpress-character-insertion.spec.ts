// Holding a letter down and choosing one of its special characters puts that character where the
// caret is, and leaves the caret straight after it (BL-12889, where the caret jumped to an earlier
// word or line).
//
// A person can choose the character three ways: clicking it, turning the mouse wheel to it, or
// pressing the shortcut key shown beside it. Each test uses one of them, in a text box of three
// paragraphs, with the caret at the end, in the middle and at the beginning of the middle
// paragraph in turn.
//
// Each insertion is checked twice: where the caret is, and where the next letter typed lands.

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import type { StepFunction } from "../helpers/caption";
import { typeWithKeys } from "../helpers/keys";
import {
    chooseLongPressCharacterWithClick,
    chooseLongPressCharacterWithMouseWheel,
    chooseLongPressCharacterWithShortcut,
    getCaretPlace,
    getParagraphTexts,
    holdKeyUntilLongPressPanelShows,
    putCaretInParagraph,
} from "../helpers/longPress";

test.use({
    collectionSpec: {
        name: "longpress-character-insertion",
        languages: ["en"],
    },
});

const GROUP = ".bloom-translationGroup";
const LANG = "en";
// No letter here is the one held down or the mark, so each can be found in the result.
const PARAGRAPHS = ["first line", "second line", "third line"];
// The paragraph the special characters go into: neither the first nor the last of the box.
const MIDDLE = 1;
// The letter held down, and the special character of it that each test chooses. It is not the
// first one offered, so that the mouse wheel has to turn more than once.
const HELD_KEY = "a";
const SPECIAL = "à";
// The letter typed after each insertion, to show where the caret is.
const MARK = "X";

/** A way of choosing SPECIAL from the long-press panel, given the text box being typed in. */
type Choose = (page: Page, box: Locator) => Promise<void>;

/** Make a book with a Just Text page that holds PARAGRAPHS, and return its text box, focused. */
async function makePageWithParagraphs(page: Page): Promise<Locator> {
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    await goToPage(page, (await getContentPages(page))[0].id);
    await typeInGroup(page, GROUP, LANG, PARAGRAPHS.join("\n"));
    const box = await clickInGroup(page, GROUP, LANG);
    expect(
        await getParagraphTexts(box),
        "The text box should hold the three paragraphs before anything is inserted.",
    ).toEqual(PARAGRAPHS);
    return box;
}

/**
 * Put the caret after `offset` characters of the middle paragraph, hold the letter down, choose the
 * special character, and check that it replaced the letter there, that the caret is straight after
 * it, and that the next letter typed lands there. Then take both out again, so that the next place
 * starts from the same text.
 */
async function insertAndCheck(
    page: Page,
    box: Locator,
    offset: number,
    where: string,
    choose: Choose,
): Promise<void> {
    const line = PARAGRAPHS[MIDDLE];
    const withParagraph = (text: string) => [
        PARAGRAPHS[0],
        text,
        PARAGRAPHS[2],
    ];
    await putCaretInParagraph(box, MIDDLE, offset);

    const offered = await holdKeyUntilLongPressPanelShows(page, HELD_KEY);
    expect(
        offered.indexOf(SPECIAL),
        `"${SPECIAL}" should be offered for "${HELD_KEY}", and not first, or the mouse ` +
            `wheel test turns only once. Offered: ${offered.join(" ")}.`,
    ).toBeGreaterThan(0);
    expect(
        await getParagraphTexts(box),
        `Holding "${HELD_KEY}" with the caret ${where} should have typed one "${HELD_KEY}" there.`,
    ).toEqual(
        withParagraph(line.slice(0, offset) + HELD_KEY + line.slice(offset)),
    );

    await choose(page, box);

    const withSpecial = line.slice(0, offset) + SPECIAL + line.slice(offset);
    expect(
        await getParagraphTexts(box),
        `"${SPECIAL}" should have replaced the "${HELD_KEY}" typed ${where}.`,
    ).toEqual(withParagraph(withSpecial));
    expect(
        await getCaretPlace(box),
        `With the caret ${where}, it should be straight after the inserted "${SPECIAL}".`,
    ).toEqual({ paragraph: MIDDLE, offset: offset + SPECIAL.length });

    await typeWithKeys(page, MARK);
    await expect
        .poll(() => getParagraphTexts(box), {
            timeout: 15000,
            message:
                `With the caret ${where}, the letter typed after inserting "${SPECIAL}" ` +
                `should land straight after it.`,
        })
        .toEqual(
            withParagraph(
                line.slice(0, offset) + SPECIAL + MARK + line.slice(offset),
            ),
        );

    await box.press("Backspace");
    await box.press("Backspace");
    await expect
        .poll(() => getParagraphTexts(box), {
            timeout: 15000,
            message: `Backspace twice should have taken "${SPECIAL}${MARK}" out again.`,
        })
        .toEqual(PARAGRAPHS);
}

/** Insert the special character at the end, in the middle and at the beginning of the line. */
async function insertAtEachPlace(
    page: Page,
    step: StepFunction,
    how: string,
    choose: Choose,
): Promise<void> {
    let box!: Locator;
    await step("Make a page with three paragraphs of text", async () => {
        box = await makePageWithParagraphs(page);
    });
    const line = PARAGRAPHS[MIDDLE];
    const places = [
        { offset: line.length, where: "at the end of the line" },
        { offset: 3, where: "in the middle of the line" },
        { offset: 0, where: "at the beginning of the line" },
    ];
    for (const place of places)
        await step(`Insert a special character ${place.where} ${how}`, () =>
            insertAndCheck(page, box, place.offset, place.where, choose),
        );
}

test("a long-press character chosen with a mouse click goes in at the caret [Test Case ID 291]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await insertAtEachPlace(page, step, "with a mouse click", (page) =>
        chooseLongPressCharacterWithClick(page, HELD_KEY, SPECIAL),
    );
});

test("a long-press character chosen with the mouse wheel goes in at the caret [Test Case ID 291]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await insertAtEachPlace(page, step, "with the mouse wheel", (page, box) =>
        chooseLongPressCharacterWithMouseWheel(page, HELD_KEY, SPECIAL, box),
    );
});

test("a long-press character chosen with its shortcut key goes in at the caret [Test Case ID 291]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await insertAtEachPlace(page, step, "with its shortcut key", (page) =>
        chooseLongPressCharacterWithShortcut(page, HELD_KEY, SPECIAL),
    );
});
