// The reader tools remembering where each book is up to: every book keeps its own decodable stage
// and leveled-reader level, and a new book starts on the stage or level the person last chose.
//
// These pin the toolbox's save-and-restore path, which the toolbox infrastructure rewrite (BL-16608)
// reworked and which no unit test reaches: the stage or level is saved into the book when the person
// changes it, restored when the book is opened again, and remembered as the default for the next new
// book. The subtle rule, from the manual card, is that restoring a book's stage when it is opened must
// NOT make that the default: only choosing a stage with the arrows does.
//
// Every test here moves off stage or level 1 on purpose, so that a restore which silently fell back
// to the first stage or level would fail rather than pass by coincidence. The tool restores a
// book's stage or level a moment after it appears, so what it shows after a book is opened is
// polled for rather than read once.

import { test } from "../fixtures/bloomTest";
import { editBook, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    expectReaderToolToShow,
    makeBasicBookWithReaderTool,
    openReaderTool,
    setReaderPhase,
} from "../helpers/readerTools";

test.use({
    collectionSpec: { name: "reader-stage-and-level", languages: ["en"] },
});

test("each book remembers its own decodable stage [Test Case ID 442]", async ({
    page,
}) => {
    const firstBook = await makeBasicBookWithReaderTool(
        page,
        "decodableReader",
    );
    await setReaderPhase(page, "decodableReader", 2);
    const secondBook = await makeBasicBookWithReaderTool(
        page,
        "decodableReader",
    );
    await setReaderPhase(page, "decodableReader", 4);

    await editBook(page, firstBook);
    await openReaderTool(page, "decodableReader");
    await expectReaderToolToShow(
        page,
        "decodableReader",
        2,
        "The first book should come back on the stage it was left on.",
    );

    await editBook(page, secondBook);
    await openReaderTool(page, "decodableReader");
    await expectReaderToolToShow(
        page,
        "decodableReader",
        4,
        "The second book should come back on its own stage, not the first book's.",
    );
});

test("each book remembers its own level [Test Case ID 442]", async ({
    page,
}) => {
    const firstBook = await makeBasicBookWithReaderTool(page, "leveledReader");
    await setReaderPhase(page, "leveledReader", 2);
    const secondBook = await makeBasicBookWithReaderTool(page, "leveledReader");
    await setReaderPhase(page, "leveledReader", 3);

    await editBook(page, firstBook);
    await openReaderTool(page, "leveledReader");
    await expectReaderToolToShow(
        page,
        "leveledReader",
        2,
        "The first book should come back on the level it was left on.",
    );

    await editBook(page, secondBook);
    await openReaderTool(page, "leveledReader");
    await expectReaderToolToShow(
        page,
        "leveledReader",
        3,
        "The second book should come back on its own level, not the first book's.",
    );
});

test("a new book starts on the stage last chosen, not the one last seen [Test Case ID 441]", async ({
    page,
}) => {
    const bookLeftOnStage2 = await makeBasicBookWithReaderTool(
        page,
        "decodableReader",
    );
    await setReaderPhase(page, "decodableReader", 2);
    await makeBasicBookWithReaderTool(page, "decodableReader");
    await setReaderPhase(page, "decodableReader", 4);
    // Look at the first book again. Its stage 2 is restored, which must not become the default.
    await editBook(page, bookLeftOnStage2);
    await openReaderTool(page, "decodableReader");
    // sanity check: the restore we are guarding against really did show stage 2
    await expectReaderToolToShow(
        page,
        "decodableReader",
        2,
        "The first book should have come back on stage 2 before the new book was made.",
    );

    await makeBookFromTemplate(page, "Decodable Reader");
    await openReaderTool(page, "decodableReader");

    await expectReaderToolToShow(
        page,
        "decodableReader",
        4,
        "A new decodable book should start on the stage last chosen with the arrows (4), " +
            "not on the stage of the book last looked at (2), nor on stage 1.",
    );
});

test("a new book starts on the level last chosen [Test Case ID 460]", async ({
    page,
}) => {
    await makeBasicBookWithReaderTool(page, "leveledReader");
    await setReaderPhase(page, "leveledReader", 3);

    // makeBasicBookWithReaderTool waits for the new book to show the level Bloom reports as its
    // default, so what this test adds is that the default really is the level chosen above.
    await makeBasicBookWithReaderTool(page, "leveledReader");

    await expectReaderToolToShow(
        page,
        "leveledReader",
        3,
        "A new book should start on the level last chosen, not on level 1.",
    );
});
