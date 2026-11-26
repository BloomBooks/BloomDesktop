// A Talking Book in which two sentences share one id, as books damaged by a bug in older versions
// of Bloom do (BL-9467, BL-9503). Automates the manual test "Repair Duplicate Audio IDs" (Test
// Case ID 509).
//
// Bloom names a sentence's narration file after the sentence's id, so two sentences with one id
// play one recording, and recording either overwrites the other's. When Bloom opens such a book
// (here, when it starts with the book selected) it gives each duplicate a new id, copies the shared recording to the new id's file,
// and tells the person that they may need to record some text again
// (Book.FixDuplicateAudioIdInBookPages).
//
// The manual case opens the damaged book attached to BL-9467. This test makes its own: it narrates
// a page of two sentences, then, while Bloom is stopped, gives the second sentence the first one's
// id in the book's .htm, which is the damage that book has.
//
// Under --e2e Bloom shows the message to nobody and writes it to stderr instead (NonFatalProblem),
// so the test reads it with bloomApp.takeNonFatalProblems().

import * as fs from "node:fs";
import { expect, test } from "../fixtures/bloomTest";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    editBook,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    getStagedNarrationIds,
    openPublishDestination,
    stageBloomPub,
} from "../helpers/publish";
import {
    addNarration,
    getNarrationSentences,
    narrationFilePath,
    openToolboxWithTalkingBook,
} from "../helpers/talkingBook";

test.use({
    collectionSpec: { name: "duplicate-audio-ids", languages: ["en"] },
});

const BOOK_TITLE = "Duplicate Audio Ids";

// The start of what Bloom tells the person once it has repaired the book (Book.cs).
const REPAIR_MESSAGE =
    "There was a problem with recordings in this Talking Book, which was caused by a bug in an older version of Bloom.";

test("repairs duplicate audio ids once, and the book then publishes [Test Case ID 509]", async ({
    bloomApp,
}) => {
    test.setTimeout(300000);
    let page = bloomApp.page;

    // A book with one content page of two sentences, each with its own narration.
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
    await addPage(page, "Just Text", 1);
    const contentPageId = (await getContentPages(page))[0].id;
    await goToPage(page, contentPageId);
    await typeInGroup(page, ".bloom-translationGroup", "en", "One. Two.");
    await openToolboxWithTalkingBook(page);
    const sentences = await getNarrationSentences(page);
    expect(
        sentences.map((s) => s.text.trim()),
        "The Talking Book tool did not mark the page's two sentences.",
    ).toEqual(["One.", "Two."]);
    const [firstId, secondId] = sentences.map((s) => s.id);
    let bookFolder = await findBookFolder(page, BOOK_TITLE);
    await addNarration(page, bookFolder, "en");

    // Leaving the page makes Bloom write it to the book's .htm.
    const cover = (await getPages(page)).find((p) => !p.isContentPage);
    await goToPage(page, cover!.id);
    const htmlPath = bookHtmlPath(bookFolder);
    await expect
        .poll(() => fs.readFileSync(htmlPath, "utf8").includes(secondId), {
            message: "Bloom never saved the page with its two sentences.",
            timeout: 30000,
        })
        .toBe(true);

    // The damage: the second sentence gets the first one's id, and its own recording is gone.
    page = await bloomApp.restart(() => {
        const html = fs.readFileSync(htmlPath, "utf8");
        fs.writeFileSync(htmlPath, html.split(secondId).join(firstId));
        fs.rmSync(narrationFilePath(bookFolder, secondId));
    });
    // Bloom opens the book that it had selected when it stopped, which repairs it, and says so.
    // Every problem reported since the restart is kept, so one that came before the repair
    // message still fails the check below.
    const problemsAfterRestart: string[] = [];
    await expect
        .poll(
            () => {
                problemsAfterRestart.push(...bloomApp.takeNonFatalProblems());
                return problemsAfterRestart.some((p) =>
                    p.includes(REPAIR_MESSAGE),
                );
            },
            {
                message:
                    "Bloom did not say that it repaired the book's recordings.",
                timeout: 60000,
            },
        )
        .toBe(true);
    expect(
        problemsAfterRestart,
        "Bloom reported something besides the repair, or reported the repair more than once.",
    ).toEqual([expect.stringContaining(REPAIR_MESSAGE)]);

    bookFolder = await findBookFolder(page, BOOK_TITLE);
    await editBook(page, bookFolder);
    await goToPage(page, contentPageId);
    const repaired = await getNarrationSentences(page);
    expect(repaired.map((s) => s.text.trim())).toEqual(["One.", "Two."]);
    expect(
        repaired[0].id,
        "The first sentence with the shared id lost its id.",
    ).toBe(firstId);
    const newId = repaired[1].id;
    expect(
        newId,
        "The second sentence still shares the first one's id.",
    ).not.toBe(firstId);
    // Its old id would mean that the book was never damaged in the first place.
    expect(
        newId,
        "The second sentence has the id that the test took away from it.",
    ).not.toBe(secondId);
    // The second sentence keeps the recording it played before: Bloom copies the shared file.
    expect(
        fs
            .readFileSync(narrationFilePath(bookFolder, newId))
            .equals(fs.readFileSync(narrationFilePath(bookFolder, firstId))),
        "The second sentence's new id has no copy of the recording that it shared.",
    ).toBe(true);

    // Leaving the book and coming back finds nothing more to repair.
    await editBook(page, bookFolder);
    await goToPage(page, contentPageId);
    expect(
        (await getNarrationSentences(page)).map((s) => s.id),
        "The repaired ids did not last.",
    ).toEqual([firstId, newId]);
    expect(
        bloomApp.takeNonFatalProblems(),
        "Bloom reported a problem again when the book was opened again.",
    ).toEqual([]);

    // Leaving the book saved the repair: a Bloom that loads the book afresh from its file finds
    // the repaired ids and nothing to repair.
    page = await bloomApp.restart();
    await editBook(page, bookFolder);
    await goToPage(page, contentPageId);
    expect(
        (await getNarrationSentences(page)).map((s) => s.id),
        "The repaired ids were not saved in the book's file.",
    ).toEqual([firstId, newId]);
    expect(
        bloomApp.takeNonFatalProblems(),
        "Bloom reported a problem when it loaded the repaired book from its file.",
    ).toEqual([]);

    // Publishing it as a BloomPUB carries both recordings. A crash or an error here shows as a
    // problem dialog, which fails the test (failOnBloomProblem).
    await openPublishDestination(page, "BloomPUB");
    const stagedFolder = await stageBloomPub(page);
    expect(getStagedNarrationIds(stagedFolder)).toEqual(
        [firstId, newId].sort(),
    );
    expect(
        bloomApp.takeNonFatalProblems(),
        "Bloom reported a problem while it made the BloomPUB.",
    ).toEqual([]);
});
