// How the Decodable Reader tool counts the words of the collection's sample texts, as its "Sort by
// frequency" button shows them. Automates the manual test "Sample Word Frequency Calculation"
// (Test Case ID 445).
//
// The counts come from the files in the Sample Texts folder, not from the book. The panel lists a stage's words and its sight words together; sorted by
// frequency, the most frequent come first, and words that occur equally often are in alphabetical
// order.
//
// One test, because it saves the setup dialog: a save costs the next test in the same file its
// shell document ("Saving the reader settings costs the next test in the file its shell document"
// in AUTOMATION-DEBT.md).

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    acceptReaderSetup,
    enableDecodableReaderTool,
    getSavedReaderSettings,
    openDecodableStagesSetup,
    openReaderSetupTab,
    setSampleTexts,
    setTypedSampleWords,
    useKnownReaderStages,
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
 * nothing else are rare (five times, once capitalized and twice with punctuation after it), rear
 * (three times), ear and era (twice each) and are (once). The sight word "the" is there four times,
 * and the other sight words not at all. "dog" is the most frequent word of all, but needs d, o and
 * g, so stage 1 must not list it.
 */
const kSampleTexts = {
    "first story.txt": "Rare rear ear. the rare, era the dog dog dog!",
    "second story.txt":
        "rare. rear rear ear! era rare the the are rare dog dog dog",
};

/** Typed on the Sample Words tab: decodable at stage 1, and in no sample text. */
const kTypedWord = "err";

/** The stage 1 words with how often each occurs, most frequent first. */
const kExpectedByFrequency = [
    "rare", // 5
    "the", // 4, a sight word
    "rear", // 3
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
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
    await useKnownReaderStages(page);
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
                    "every sample text, a capitalized word and one with punctuation after it as " +
                    "the same word, with words of equal frequency in alphabetical order.",
            })
            .toEqual(kExpectedByFrequency);
    });
});
