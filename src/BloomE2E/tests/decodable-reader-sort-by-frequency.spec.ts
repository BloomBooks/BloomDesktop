// How the Decodable Reader tool counts the words of the collection's sample texts, as its "Sort by
// frequency" button shows them. Automates the manual test "Sample Word Frequency Calculation"
// (Test Case ID 445).
//
// The counts come from the files in the Sample Texts folder, not from the book. The panel lists a
// stage's words and its sight words together; sorted by frequency, the most frequent come first,
// and words that occur equally often are in alphabetical order.
//
// One test, because it saves the setup dialog: a save costs the next test in the same file its
// shell document ("Saving the reader settings costs the next test in the file its shell document"
// in AUTOMATION-DEBT.md).

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    acceptReaderSetup,
    getSavedReaderSettings,
    makeDecodableBookWithKnownStages,
    openDecodableStagesSetup,
    openReaderSetupTab,
    setSampleTexts,
    setTypedSampleWords,
} from "../helpers/readerSetup";
import {
    clickDecodableSortButton,
    getDecodableStageWords,
    setReaderPhase,
} from "../helpers/readerTools";

test.use({
    collectionSpec: { name: "reader-word-frequency", languages: ["en"] },
});

/**
 * Two sample texts. At stage 1 (a, e, r, with the sight words the, of, and, to) the words that need
 * nothing else are rare (five times: once capitalized, twice with punctuation after it, and in both
 * files), area (four times), ear and era (twice each) and are (once). The sight word "the" is there
 * three times, and the other sight words not at all. "dog" is the most frequent word of all, but
 * needs d, o and g, so stage 1 must not list it.
 *
 * Every count matters to the order: had Bloom missed any one "rare", or counted only one of the
 * files, rare would no longer come before area.
 */
const kSampleTexts = {
    "first story.txt": "Rare area ear. the rare, era the dog dog dog!",
    "second story.txt":
        "rare. area area ear! era rare the are area rare dog dog dog",
};

/** Typed on the Sample Words tab: decodable at stage 1, and in no sample text. */
const kTypedWord = "err";

/**
 * Typed into the book itself. Were the book's words counted, ear and era would come first; they
 * must not be, since the frequencies are those of the sample texts.
 */
const kBookText = "ear era ear era ear era ear era ear era ear era";

/** The stage 1 words with how often the sample texts use each, most frequent first. */
const kExpectedByFrequency = [
    "rare", // 5
    "area", // 4
    "the", // 3, a sight word
    "ear", // 2
    "era", // 2
    "are", // 1
    "err", // 1, typed
    "and", // 0, a sight word in no sample text
    "of", // 0, likewise
    "to", // 0, likewise
];

test("sorting by frequency orders a stage's words by how often the sample texts use them [Test Case ID 445]", async ({
    page,
    bloomApp,
}) => {
    await makeDecodableBookWithKnownStages(page);
    const saved = await getSavedReaderSettings(page);
    // sanity check: the words above are chosen around the first stage's letters and sight words.
    // useKnownReaderStages sets them; if it changes, this is what to update.
    expect(saved.stages[0].letters.split(" ").sort()).toEqual(["a", "e", "r"]);
    expect(saved.stages[0].sightWords.split(" ").sort()).toEqual([
        "and",
        "of",
        "the",
        "to",
    ]);

    await test.step("type words into the book, which must not count", async () => {
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await typeInGroup(page, ".bloom-translationGroup", "en", kBookText);
    });

    await test.step("put two sample texts in the folder and type a word on the Sample Words tab", async () => {
        setSampleTexts(bloomApp.collectionDir, kSampleTexts);
        await openDecodableStagesSetup(page);
        await openReaderSetupTab(page, "sampleWords");
        await setTypedSampleWords(page, kTypedWord);
        await acceptReaderSetup(page);
    });

    await setReaderPhase(page, "decodableReader", 1);

    await test.step("sort alphabetically, to see that stage 1 lists the words expected", async () => {
        await clickDecodableSortButton(page, "alphabetic");
        await expect
            .poll(async () => getDecodableStageWords(page), {
                timeout: 30000,
                message:
                    "At stage 1 the panel should list the sample-text words that need only a, e " +
                    "and r, the typed word, and the stage's sight words, and not dog.",
            })
            .toEqual([...kExpectedByFrequency].sort());
    });

    await test.step("sort by frequency", async () => {
        await clickDecodableSortButton(page, "by-frequency");
        await expect
            .poll(async () => getDecodableStageWords(page), {
                timeout: 15000,
                message:
                    "Sorted by frequency, the words should come most frequent first, counting " +
                    "every sample text but not the book, and a capitalized word and one with " +
                    "punctuation after it as the same word, with words of equal frequency in " +
                    "alphabetical order.",
            })
            .toEqual(kExpectedByFrequency);
    });
});
