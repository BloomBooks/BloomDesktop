// The Music tool's three choices in one book, and what Bloom Player then plays on each page.
//
// Automates the manual test "Music Mode Options" (Test Case ID 562): try No Music, Continue Music
// and Start New Music in a single book, and preview it in Bloom Reader. Bloom Reader shows a book
// with Bloom Player, which is also what Bloom's BloomPUB preview runs, so the test plays the book
// there and reads Bloom Player's own music player on each page.
//
// The book has five text pages:
//   1. Start new music, with the first song
//   2. Continue music from the previous page
//   3. No Music
//   4. Continue music from the previous page, which after No Music means silence
//   5. Start new music, with a second song
// It checks that the tool keeps each page's choice (No Music did not persist before BL-14846),
// what Bloom saves for each page, and what plays as a reader turns the pages, forward and back.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import { goToPreviewPage } from "../helpers/bloomPlayer";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseMusicFile,
    chooseMusicOption,
    getMusicToolState,
    getPreviewMusic,
    type IPreviewMusic,
    readSavedPageMusic,
} from "../helpers/music";
import {
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { enableToolForBook } from "../helpers/toolbox";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";

test.use({
    collectionSpec: {
        name: "music-mode-options",
        languages: ["en"],
        // The Music tool needs a Pro subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const BOOK_TITLE = "Music Mode Options";
const SAMPLE_MP3 = Path.join(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "audio",
    "sample.mp3",
);
const FIRST_SONG = "tc562-first-song.mp3";
const SECOND_SONG = "tc562-second-song.mp3";

test("No Music, Continue Music and Start New Music each do what they say in Bloom Player [Test Case ID 562]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;
    // The Publish tab's destinations are found by their English names, and a fresh Bloom takes
    // the operating system's UI language.
    if ((await getCurrentUiLanguageTag(page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);

    // Two songs with names of their own, so what plays says which page started it.
    const songsFolder = testInfo.outputPath("songs");
    fs.mkdirSync(songsFolder, { recursive: true });
    const firstSong = Path.join(songsFolder, FIRST_SONG);
    const secondSong = Path.join(songsFolder, SECOND_SONG);
    fs.copyFileSync(SAMPLE_MP3, firstSong);
    fs.copyFileSync(SAMPLE_MP3, secondSong);

    const book = await step(
        "Make a book with five text pages and the Music tool",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
            await addPage(page, "Just Text", 5);
            const folder = await findBookFolder(page, BOOK_TITLE);
            await enableToolForBook(page, folder, "music");
            const content = await getContentPages(page);
            expect(content.length).toBe(5);
            return { bookFolder: folder, pages: content.map((p) => p.id) };
        },
    );
    if (!book) throw new Error("The book was never made.");
    const { bookFolder, pages } = book;

    await step("Choose the music for each page", async () => {
        const choose: (() => Promise<void>)[] = [
            () => chooseMusicFile(page, firstSong),
            () => chooseMusicOption(page, "continueMusic"),
            () => chooseMusicOption(page, "noMusic"),
            () => chooseMusicOption(page, "continueMusic"),
            () => chooseMusicFile(page, secondSong),
        ];
        for (let i = 0; i < pages.length; i++) {
            await goToPage(page, pages[i]);
            // Bloom Player leaves out a page with nothing on it.
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                `Page ${i + 1}.`,
            );
            await choose[i]();
        }
        // Leaving the last page saves it.
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        await goToPage(page, cover!.id);
    });

    await step("The tool still shows each page's choice", async () => {
        const expected = [
            { choice: "newMusic", fileName: Path.parse(FIRST_SONG).name },
            { choice: "continueMusic", fileName: "" },
            { choice: "noMusic", fileName: "" },
            { choice: "continueMusic", fileName: "" },
            { choice: "newMusic", fileName: Path.parse(SECOND_SONG).name },
        ];
        for (let i = 0; i < pages.length; i++) {
            await goToPage(page, pages[i]);
            await expect
                .poll(() => getMusicToolState(page), {
                    timeout: 15000,
                    message: `On page ${i + 1}, coming back to it, the Music tool should show the choice made there.`,
                })
                .toEqual(expected[i]);
        }
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        await goToPage(page, cover!.id);
    });

    await step(
        "Bloom saved each page's music and copied both songs",
        async () => {
            const saved = await readSavedPageMusic(page, bookFolder);
            expect(
                pages.map((id) => saved[id]),
                "The saved pages should start the first song, continue, have none, continue, and start the second song.",
            ).toEqual([FIRST_SONG, null, "none", null, SECOND_SONG]);
            for (const song of [FIRST_SONG, SECOND_SONG])
                expect(
                    fs.existsSync(Path.join(bookFolder, "audio", song)),
                    `Bloom should have copied ${song} into the book's audio folder.`,
                ).toBe(true);
        },
    );

    await selectPublishDestination(page, "BloomPUB");
    const player = await showBloomPubPreview(page);

    const expectMusicOnPage = async (
        pageNumber: number,
        file: string | undefined,
    ): Promise<IPreviewMusic> => {
        await goToPreviewPage(page, player, pages[pageNumber - 1]);
        let music: IPreviewMusic | undefined;
        await expect
            .poll(
                async () => {
                    music = await getPreviewMusic(player);
                    return file
                        ? { file: music.file, playing: music.playing }
                        : { playing: music.playing };
                },
                {
                    timeout: 15000,
                    message: file
                        ? `On page ${pageNumber}, Bloom Player should be playing ${file}.`
                        : `On page ${pageNumber}, Bloom Player should be playing no music.`,
                },
            )
            .toEqual(file ? { file, playing: true } : { playing: false });
        return music!;
    };

    await step("Turning the pages forward in Bloom Player", async () => {
        const onPage1 = await expectMusicOnPage(1, FIRST_SONG);
        const onPage2 = await expectMusicOnPage(2, FIRST_SONG);
        expect(
            onPage2.src,
            "On page 2 the first song should go on from page 1, not start over.",
        ).toBe(onPage1.src);
        await expectMusicOnPage(3, undefined);
        await expectMusicOnPage(4, undefined);
        await expectMusicOnPage(5, SECOND_SONG);
    });

    await step("Turning back in Bloom Player", async () => {
        await expectMusicOnPage(4, undefined);
        await expectMusicOnPage(3, undefined);
        await expectMusicOnPage(2, FIRST_SONG);
    });
});
