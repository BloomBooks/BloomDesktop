// Pasting into a text box while the Talking Book tool is open in By Sentence mode. Bloom has marked
// the box's sentences for recording, so a copy of that text carries the markers with it, and pasting
// it back must not put one marker inside another. That was BL-10291 (fixed in 5.1): the nested
// markers threw script errors and made every later paste slower.
//
// This also pins, for the toolbox infrastructure rewrite (BL-16608), the page-editing code that
// runs when a person pastes, which the rewrite moved: the keys pressed here are the real Ctrl+C and
// Ctrl+V, so Bloom's own paste handling on the text box runs as it does for a person.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { copyAllTextIn, moveCaretToEnd, pressKeys } from "../helpers/keys";
import {
    countNestedNarrationSentences,
    getNarrationSentences,
    openToolboxWithTalkingBook,
    setRecordingMode,
    waitForNarrationSentencesToCover,
} from "../helpers/talkingBook";

const SENTENCE = "Hi.";

test.use({
    collectionSpec: { name: "talking-book-paste", languages: ["en"] },
});

test("pasting a marked sentence with Talking Book open does not nest its markers [Test Case ID 517]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text", 1);
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, ".bloom-translationGroup", "en", SENTENCE);
    await openToolboxWithTalkingBook(page);
    await setRecordingMode(page, "By Sentence");
    // sanity check: the tool has marked the sentence, and nothing is nested yet
    expect(await getNarrationSentences(page)).toHaveLength(1);
    expect(await countNestedNarrationSentences(page)).toBe(0);

    const box = await clickInGroup(page, ".bloom-translationGroup", "en");
    await copyAllTextIn(box, "the text box");
    await moveCaretToEnd(box);
    await pressKeys(page, ["Control+v", "Control+v"]);

    // sanity check: both pastes really arrived
    await expect(box).toHaveText(SENTENCE.repeat(3), {
        timeout: 15000,
        useInnerText: true,
    });
    await waitForNarrationSentencesToCover(page, SENTENCE.repeat(3));
    expect(
        await countNestedNarrationSentences(page),
        "Pasting a marked sentence should not put a sentence marker inside another.",
    ).toBe(0);

    // The card goes on to paste a few more times: the original bug compounded with each paste.
    await moveCaretToEnd(box);
    await pressKeys(page, ["Control+v", "Control+v", "Control+v"]);
    await expect(box).toHaveText(SENTENCE.repeat(6), {
        timeout: 15000,
        useInnerText: true,
    });
    await waitForNarrationSentencesToCover(page, SENTENCE.repeat(6));
    expect(await countNestedNarrationSentences(page)).toBe(0);
});
