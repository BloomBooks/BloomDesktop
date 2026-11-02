// A collection's alphabet may include characters that are not letters at all, such as # and *,
// and they can be taught at a stage like any other letter (BL-10446). The dialog builds its letter
// grid from whatever is typed on the Letters tab, so this checks that such characters survive
// that trip and the save.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    acceptReaderSetup,
    enableDecodableReaderTool,
    getAlphabetLetters,
    getSavedReaderSettings,
    openDecodableStagesSetup,
    openReaderSetupTab,
    selectStageLetter,
    setAlphabetLetters,
} from "../helpers/readerSetup";

// One save per file; see the note in decodable-reader-drops-empty-stage.spec.ts.

test.use({
    collectionSpec: { name: "reader-non-alphabetic", languages: ["en"] },
});

test("builds a book with the Decodable Reader tool turned on", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
});

test("# and * can be letters of the alphabet and taught at a stage [Test Case ID 472]", async ({
    page,
}) => {
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "letters");
    const alphabet = await getAlphabetLetters(page);
    // sanity check: neither is a letter yet, or the test would prove nothing
    expect(alphabet.split(/\s+/)).not.toContain("#");
    expect(alphabet.split(/\s+/)).not.toContain("*");

    await setAlphabetLetters(page, `${alphabet} # *`);
    await openReaderSetupTab(page, "stages");
    await selectStageLetter(page, "#");
    await selectStageLetter(page, "*");
    await acceptReaderSetup(page);

    const saved = await getSavedReaderSettings(page);
    expect(saved.letters.split(" "), "The saved alphabet lost # or *.").toEqual(
        expect.arrayContaining(["#", "*"]),
    );
    expect(
        saved.stages[0].letters.split(" "),
        "The first stage did not keep # and * as letters it teaches.",
    ).toEqual(expect.arrayContaining(["#", "*"]));
});
