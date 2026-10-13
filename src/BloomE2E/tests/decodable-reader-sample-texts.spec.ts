// The Sample Words tab's view of the collection's Sample Texts folder, and the matching-words
// preview that is built from what Bloom reads out of it.
//
// Both cross the frame boundary that makes this dialog unusual: the dialog runs in the workspace
// document, while the word data and the readable-file rules live in the toolbox frame and are
// reached through the toolbox bundle. That boundary is invisible to the unit tests, and it is
// where the conversion's real bugs have been. (BL-16607)

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    cancelReaderSetup,
    changeSavedReaderSettings,
    enableDecodableReaderTool,
    getMatchingWords,
    getSampleTextFiles,
    isShowingNoSampleTextsMessage,
    openDecodableStagesSetup,
    openReaderSetupTab,
    selectStageLetter,
    getSavedReaderSettings,
    setSampleTexts,
    setTypedSampleWords,
    useKnownReaderStages,
} from "../helpers/readerSetup";

test.use({
    collectionSpec: { name: "reader-sample-texts", languages: ["en"] },
});

test("builds a book with the Decodable Reader tool turned on, and known stages", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
    await useKnownReaderStages(page);
});

// The old dialog listed files it could not read, with a reason. The React conversion dropped
// them silently at first, which left a user staring at a folder they had just filled and a list
// that ignored it. Listing them again — and treating .TXT the same as .txt — is the fix.
test("unreadable sample texts are listed with a reason, and .TXT counts as readable", async ({
    page,
    bloomApp,
}) => {
    setSampleTexts(bloomApp.collectionDir, {
        "lower.txt": "cat sat mat",
        "UPPER.TXT": "dog log fog",
        "notes.docx": "not really a docx",
        READMEWITHOUTEXTENSION: "no extension",
    });

    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "sampleWords");

    const files = await getSampleTextFiles(page);
    const byName = (n: string) => files.find((f) => f.name.startsWith(n));

    // sanity check: nothing was dropped from the list
    expect(files.length, `listed: ${files.map((f) => f.name).join(", ")}`).toBe(
        4,
    );

    expect(byName("lower.txt")?.validType).toBe(true);
    // A capitalised extension is just as readable as a lower-case one.
    expect(byName("UPPER.TXT")?.validType).toBe(true);

    // The two Bloom cannot read are shown, each saying why, rather than vanishing.
    expect(byName("notes.docx")?.validType).toBe(false);
    expect(byName("notes.docx")?.explanation).toContain("Cannot read");
    expect(byName("READMEWITHOUTEXTENSION")?.validType).toBe(false);
    expect(byName("READMEWITHOUTEXTENSION")?.explanation).toContain("TXT");

    // Leave the dialog shut: it is modal, and a test that leaves it up blocks the next one.
    await cancelReaderSetup(page);
});

// An empty folder used to leave a blank area with no explanation at all.
test("an empty Sample Texts folder says so", async ({ page, bloomApp }) => {
    setSampleTexts(bloomApp.collectionDir, {}); // exists, and is empty

    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "sampleWords");

    expect(await getSampleTextFiles(page)).toHaveLength(0);
    expect(await isShowingNoSampleTextsMessage(page)).toBe(true);

    await cancelReaderSetup(page);
});

// The matching-words preview is the only panel fed from the toolbox frame's Synphony data rather
// than from the dialog's own copy of the settings, so it is the one that breaks when the
// cross-frame lookups are wrong -- which is exactly what happened with the special-symbol lookup.
//
// What it checks is that the preview RESPONDS: a word from a sample text that needs a letter the
// stage has not taught stays out, and appears as soon as that letter is added. Asserting a fixed
// list instead would say little, because the preview also always lists the stage's sight words.
test("the matching-words preview gains a sample-text word when its letter is taught", async ({
    page,
    bloomApp,
}) => {
    // "art" and "tear" need only letters the first stage already teaches, plus "t"; "dog" needs
    // letters it never teaches, so it should never appear.
    setSampleTexts(bloomApp.collectionDir, { "words.txt": "art tear dog" });

    const saved = await getSavedReaderSettings(page);
    const stageLetters = saved.stages[0].letters
        .split(" ")
        .filter((letter) => letter);
    // sanity check: this collection's first stage teaches a, e and r, and not yet t -- the words
    // above are chosen around that. useKnownReaderStages sets those stages; if it changes, this is
    // what to update.
    expect(
        stageLetters.sort(),
        `The first stage teaches ${saved.stages[0].letters}, so the words this test uses no longer fit.`,
    ).toEqual(["a", "e", "r"]);

    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "stages");

    const before = await getMatchingWords(page);
    expect(
        before,
        "'art' needs a t, which this stage has not taught yet.",
    ).not.toContain("art");
    expect(before).not.toContain("tear");

    await selectStageLetter(page, "t");

    await expect
        .poll(async () => (await getMatchingWords(page)).includes("art"), {
            timeout: 30000,
            message:
                "Teaching 't' never brought 'art' -- a word straight out of the sample text file -- into the preview.",
        })
        .toBe(true);

    const after = await getMatchingWords(page);
    expect(after).toContain("tear");
    // sanity check: a word needing letters this stage still has not taught stays out
    expect(after).not.toContain("dog");

    await cancelReaderSetup(page);
});

// Sample words can come from the words typed on the Sample Words tab, from the Sample Texts
// folder, or from both, and non-ASCII words must survive the trip from a UTF-8 file. "é" is not in
// this collection's alphabet, so the test adds it and teaches it, with "t", at the first stage.
// That changes the saved settings for the rest of the file, so this test stays last.
test("sample words come from typed words, the Sample Texts folder, or both, including non-ASCII words [Test Case ID 444]", async ({
    page,
    bloomApp,
}) => {
    await changeSavedReaderSettings(page, (settings) => {
        settings.letters = `${settings.letters} é`;
        settings.stages[0].letters = `${settings.stages[0].letters} t é`;
        settings.moreWords = "";
    });
    // sanity check: the setup reached the settings file
    expect(
        (await getSavedReaderSettings(page)).stages[0].letters.split(" "),
    ).toContain("é");

    // The Sample Texts folder only.
    setSampleTexts(bloomApp.collectionDir, { "words.txt": "tré" });
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "stages");
    await expect
        .poll(async () => (await getMatchingWords(page)).includes("tré"), {
            timeout: 30000,
            message:
                "'tré', the only word in the Sample Texts folder, never reached the preview.",
        })
        .toBe(true);
    await cancelReaderSetup(page);

    // Typed words only.
    setSampleTexts(bloomApp.collectionDir, {});
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "sampleWords");
    await setTypedSampleWords(page, "été");
    await openReaderSetupTab(page, "stages");
    await expect
        .poll(async () => (await getMatchingWords(page)).includes("tré"), {
            timeout: 30000,
            message:
                "'tré' is still in the preview after the Sample Texts folder was emptied.",
        })
        .toBe(false);
    expect(
        await getMatchingWords(page),
        "'été', typed on the Sample Words tab, is not in the preview.",
    ).toContain("été");
    await cancelReaderSetup(page);

    // Both together.
    setSampleTexts(bloomApp.collectionDir, { "words.txt": "tré" });
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "sampleWords");
    await setTypedSampleWords(page, "été");
    await openReaderSetupTab(page, "stages");
    await expect
        .poll(
            async () => {
                const words = await getMatchingWords(page);
                return words.includes("tré") && words.includes("été");
            },
            {
                timeout: 30000,
                message:
                    "With words both typed and in the Sample Texts folder, the preview did not show one from each.",
            },
        )
        .toBe(true);
    await cancelReaderSetup(page);
});
