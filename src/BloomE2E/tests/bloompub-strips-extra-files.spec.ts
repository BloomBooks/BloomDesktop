// A .bloompub carries only the files a reader needs: Bloom copies a book into it through a white
// list (BookFileFilter, configured by BloomPubMaker.MakeFilter), so whatever else a person or a
// program left in the book's folder stays out of it.
//
// Automates the manual test "BloomPUB Strips Extra Files" (Test Case ID 196), which came from
// BL-8956: a user found a big temp file inside a BloomPUB.
//
// The test puts files of every kind the white list leaves out into a book's folder, beside a
// narration file the book uses, makes the book's .bloompub, and reads the zip. Each stray file
// is one the list rejects for its own reason:
//   - a file at the top of the folder whose extension is not on the list (a temp file, a document);
//   - a second .htm, which is not the book's own;
//   - a picture in a folder the list does not take;
//   - an .mp3 in the audio folder that no sentence of the book plays;
//   - a .wav beside the narration .mp3 of a sentence the book does play;
//   - a video in the video folder that no page plays;
//   - thumbnail-70.png and thumbnail-256.png, pictures that uploading to Bloom Library leaves and
//     a BloomPUB has no use for.
// Bloom applies the white list twice: when it copies the book into a staging folder
// (PublishHelper.MakeDeviceXmatterTempBook) and when it zips that folder (BloomPubMaker.MakeFilter).
// The first copy already drops most stray files. The two thumbnails pass it (each a .png at the top
// of the folder), and Bloom's own image cleanup spares files whose names start with "thumbnail",
// so only the zip's filter, which rejects those two names, keeps them out.
// The narration .mp3 the book plays must come through, so the audio folder is filtered file by
// file and not dropped whole. The test also checks every stray file is still in the book's folder
// once the BloomPUB is made, so that it is the white list that kept each out, not something that
// tidied the folder first.
//
// A .bloompub saved from Publish: BloomPUB goes through a native save dialog, out of the test's
// reach. "Make All BloomPUBs from Collection" makes the same file through the same BloomPubMaker,
// with no dialog, so the test uses that; it needs an enterprise subscription. In a collection with
// no bookshelf, Make All BloomPUBs fails before it makes anything (BL-17197), so the collection
// has one, and the test unticks "Produce a .bloomshelf file".

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    makeAllBloomPubs,
    openMakeAllBloomPubsDialog,
    readZip,
    setMakeAllBloomPubsOption,
} from "../helpers/bulkBloomPub";
import { selectBook } from "../helpers/collection";
import {
    kEnterpriseSubscriptionCode,
    kTestBookshelves,
} from "../helpers/collectionSettings";
import {
    addNarration,
    getNarrationSentences,
    openToolboxWithTalkingBook,
    sampleNarrationFile,
} from "../helpers/talkingBook";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "bloompub-strips-extra-files",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
        bookshelf: kTestBookshelves[0],
    },
});

const BOOK_TITLE = "Stray Files Book";

/** A file the white list leaves out: where it goes in the book's folder, and why it stays out. */
interface IStrayFile {
    /** The path from the book's folder, with forward slashes, as a zip names its entries. */
    path: string;
    why: string;
    /** The file to copy there; otherwise a few bytes of text are written. */
    copyOf?: string;
}

/** The stray files that do not depend on the narration's file name. */
const STRAY_FILES: IStrayFile[] = [
    {
        path: "~WRL0001.tmp",
        why: "a temp file at the top of the folder (BL-8956)",
    },
    {
        path: "notes about this book.docx",
        why: "a document at the top of the folder",
    },
    { path: "old copy.htm", why: "an .htm that is not the book's own" },
    {
        path: "spare pictures/unused picture.png",
        why: "a picture in a folder the white list does not take",
    },
    {
        path: "audio/unused narration.mp3",
        why: "an .mp3 that no sentence of the book plays",
        copyOf: sampleNarrationFile,
    },
    {
        path: "video/unused video.mp4",
        why: "a video that no page of the book plays",
        copyOf: "fixtures/videos/short.mp4",
    },
    {
        path: "thumbnail-70.png",
        why: "a thumbnail that uploading to Bloom Library leaves, which only the zip's filter rejects",
        copyOf: "fixtures/images/blue-square.png",
    },
    {
        path: "thumbnail-256.png",
        why: "a thumbnail that uploading to Bloom Library leaves, which only the zip's filter rejects",
        copyOf: "fixtures/images/blue-square.png",
    },
];

/** Put a stray file in the book's folder. */
function writeStrayFile(bookFolder: string, stray: IStrayFile): void {
    const target = Path.join(bookFolder, ...stray.path.split("/"));
    fs.mkdirSync(Path.dirname(target), { recursive: true });
    if (stray.copyOf) fs.copyFileSync(Path.resolve(stray.copyOf), target);
    else fs.writeFileSync(target, `A stray file: ${stray.why}.\n`);
}

test("A BloomPUB leaves out the files not on Bloom's white list [Test Case ID 196]", async ({
    bloomApp,
    step,
}) => {
    // The Make All BloomPUBs helpers find the command and its dialog by their English labels, and
    // a fresh Bloom takes the operating system's UI language.
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    const made = await step(
        "Make a book with one narrated sentence",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
            await addPage(page, "Just Text");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                "One sentence to narrate.",
            );
            // Opening the toolbox puts the Talking Book tool to work, which marks the sentence
            // with the id its narration file is named after.
            await openToolboxWithTalkingBook(page);
            const sentences = await getNarrationSentences(page);
            expect(
                sentences.map((sentence) => sentence.languageTag),
                "The Talking Book tool did not mark the one English sentence.",
            ).toEqual(["en"]);
            const folder = await findBookFolder(page, BOOK_TITLE);
            const [narrationId] = await addNarration(page, folder, "en");
            // Leave the content page, so Bloom saves it with the sentence's id.
            await goToPage(page, (await getPages(page))[0].id);
            return { folder, narrationId };
        },
    );
    if (!made) throw new Error("The book was not made.");
    const { folder: bookFolder, narrationId } = made;
    const narrationFile = `audio/${narrationId}.mp3`;

    const strayFiles: IStrayFile[] = [
        ...STRAY_FILES,
        {
            path: `audio/${narrationId}.wav`,
            why: "a .wav beside the narration the book plays",
            copyOf: sampleNarrationFile,
        },
    ];

    await step("Put stray files in the book's folder", async () => {
        await switchTab(page, "collection");
        for (const stray of strayFiles) writeStrayFile(bookFolder, stray);
    });

    await step("Open Make All BloomPUBs from Collection", async () => {
        await selectBook(page, bookFolder);
        await openMakeAllBloomPubsDialog(page);
        await setMakeAllBloomPubsOption(page, "bookshelfFile", false);
        await setMakeAllBloomPubsOption(page, "bloomBundle", false);
    });

    const result = await step("Make the BloomPUB", () =>
        makeAllBloomPubs(page),
    );
    if (!result) throw new Error("Make All BloomPUBs did not run.");

    const bloomPubName = `${Path.basename(bookFolder)}.bloompub`;
    const bloomPub = result.files.get(bloomPubName);
    if (!bloomPub) {
        throw new Error(
            `Make All BloomPUBs made no ${bloomPubName}. It made: ` +
                `${[...result.files.keys()].join(", ")}. The progress box said:\n${result.log}`,
        );
    }
    const entries = readZip(bloomPub).map((entry) => entry.name);
    const listed = `The BloomPUB holds: ${entries.join(", ")}`;

    await step("Check the BloomPUB holds the book and its narration", () => {
        expect(entries, listed).toContain("meta.json");
        expect(
            entries.filter((entry) => entry.endsWith(".htm")),
            `The BloomPUB should hold the book's own page file, index.htm, and no other. ${listed}`,
        ).toEqual(["index.htm"]);
        expect(
            entries,
            `The BloomPUB should hold the narration the book plays. ${listed}`,
        ).toContain(narrationFile);
    });

    await step("Check the BloomPUB holds none of the stray files", () => {
        for (const stray of strayFiles) {
            const fileName = stray.path.split("/").at(-1)!;
            expect(
                entries.filter((entry) => entry.split("/").at(-1) === fileName),
                `The BloomPUB should leave out ${stray.path}, ${stray.why}. ${listed}`,
            ).toEqual([]);
        }
    });

    await step("Check the stray files are still in the book's folder", () => {
        for (const stray of strayFiles) {
            expect(
                fs.existsSync(Path.join(bookFolder, ...stray.path.split("/"))),
                `${stray.path} (${stray.why}) is gone from the book's folder, so its absence ` +
                    "from the BloomPUB proves nothing about the white list.",
            ).toBe(true);
        }
    });
});
