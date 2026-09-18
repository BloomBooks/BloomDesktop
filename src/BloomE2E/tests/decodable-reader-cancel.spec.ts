// What the Decodable Reader setup dialog writes when you press OK, and what it leaves alone when
// you press Cancel.
//
// This is the part of the dialog where a mistake costs a user their work rather than merely
// looking wrong, and it is all new code. The dialog's pure helpers are unit-tested; what these
// tests add is the whole round trip — dialog, across the toolbox/workspace frame boundary, into
// the collection's settings file, and back out again on reopen. (BL-16607)

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    cancelReaderSetup,
    enableDecodableReaderTool,
    getAlphabetLetters,
    getSavedReaderSettings,
    getStageSightWords,
    openDecodableStagesSetup,
    openReaderSetupTab,
    setAlphabetLetters,
    setStageSightWords,
    setTypedSampleWords,
} from "../helpers/readerSetup";

// One test per file, deliberately. Saving the reader settings leaves a second workspace-root
// document behind, and the next test in the same file then attaches to the one Bloom is not
// driving -- the "a test can attach to a shell document Bloom does not drive" entry in
// AUTOMATION-DEBT.md. Until that is fixed, a file may contain at most one save.

test.use({ collectionSpec: { name: "reader-cancel", languages: ["en"] } });

test("builds a book with the Decodable Reader tool turned on", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
});

// Cancel has to mean cancel: the dialog edits a copy of the settings, and nothing about that copy
// may reach disk unless OK is pressed.
test("Cancel leaves the saved settings untouched", async ({ page }) => {
    await openDecodableStagesSetup(page);
    const original = await getSavedReaderSettings(page);

    // Change something on every tab, then abandon it all.
    await openReaderSetupTab(page, "letters");
    await setAlphabetLetters(page, "q w x");
    await openReaderSetupTab(page, "sampleWords");
    await setTypedSampleWords(page, "discarded words");
    await openReaderSetupTab(page, "stages");
    await setStageSightWords(page, "discarded sight words");

    await cancelReaderSetup(page);

    const after = await getSavedReaderSettings(page);
    expect(after.letters).toBe(original.letters);
    expect(after.moreWords).toBe(original.moreWords);
    expect(after.stages).toHaveLength(original.stages.length);
    expect(after.stages[0].sightWords).toBe(original.stages[0].sightWords);

    // And reopening shows the original values, not the abandoned edits.
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "letters");
    expect(await getAlphabetLetters(page)).toBe(original.letters);
    await openReaderSetupTab(page, "stages");
    expect(await getStageSightWords(page)).toBe(original.stages[0].sightWords);
});
