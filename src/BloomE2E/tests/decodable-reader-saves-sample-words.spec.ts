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
    acceptReaderSetup,
    cancelReaderSetup,
    enableDecodableReaderTool,
    getSavedReaderSettings,
    getTypedSampleWords,
    openDecodableStagesSetup,
    openReaderSetupTab,
    setTypedSampleWords,
} from "../helpers/readerSetup";

// One test per file, deliberately. Saving the reader settings leaves a second workspace-root
// document behind, and the next test in the same file then attaches to the one Bloom is not
// driving -- the "a test can attach to a shell document Bloom does not drive" entry in
// AUTOMATION-DEBT.md. Until that is fixed, a file may contain at most one save.

test.use({ collectionSpec: { name: "reader-save-words", languages: ["en"] } });

test("builds a book with the Decodable Reader tool turned on", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
});

// Typing words one per line is the natural thing to do, and it used to be saved verbatim: the
// stored list is later split on spaces, so "cat\nsat" became the single nonsense word "cat\nsat"
// and stopped counting as decodable. Newlines must become separators on the way to disk.
test("sample words typed one per line are saved as separate words", async ({
    page,
}) => {
    await openDecodableStagesSetup(page);

    await openReaderSetupTab(page, "sampleWords");
    await setTypedSampleWords(page, "zebra\nquokka\nzebra");

    await acceptReaderSetup(page);

    const saved = await getSavedReaderSettings(page);
    const words = saved.moreWords.split(" ").filter((w) => w);
    // Separate words, and the repeat dropped, as the legacy dialog did.
    expect(words).toEqual(["zebra", "quokka"]);
    expect(saved.moreWords).not.toContain("\n");

    // And the dialog shows them back as words rather than as one run-together string.
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "sampleWords");
    expect(
        (await getTypedSampleWords(page)).split(" ").filter((w) => w),
    ).toEqual(["zebra", "quokka"]);

    // Leave the dialog shut: it is modal, and a test that leaves it up blocks the next one.
    await cancelReaderSetup(page);
});
