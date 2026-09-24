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
    addStage,
    enableDecodableReaderTool,
    getSavedReaderSettings,
    getStageCount,
    openDecodableStagesSetup,
} from "../helpers/readerSetup";

// One test per file, deliberately. Saving the reader settings leaves a second workspace-root
// document behind, and the next test in the same file then attaches to the one Bloom is not
// driving -- the "a test can attach to a shell document Bloom does not drive" entry in
// AUTOMATION-DEBT.md. Until that is fixed, a file may contain at most one save.

test.use({ collectionSpec: { name: "reader-empty-stage", languages: ["en"] } });

test("builds a book with the Decodable Reader tool turned on", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
});

// The dialog always offers a stage to type into, so it is easy to leave an empty one behind. The
// legacy dialog dropped those on save and renumbered the rest; that rule was deliberately
// restored, and it only shows up in the file that gets written.
test("a stage left empty is dropped on save and the rest renumbered", async ({
    page,
}) => {
    await openDecodableStagesSetup(page);

    const before = await getStageCount(page);
    // sanity check: the collection arrives with stages to renumber around
    expect(before).toBeGreaterThan(0);

    expect(await addStage(page)).toBe(before + 1);
    // deliberately type nothing into it

    await acceptReaderSetup(page);

    const saved = await getSavedReaderSettings(page);
    expect(saved.stages).toHaveLength(before);
    // Renumbered with no gap where the dropped stage was.
    expect(saved.stages.map((stage) => stage.name)).toEqual(
        saved.stages.map((_, index) => (index + 1).toString()),
    );
});
