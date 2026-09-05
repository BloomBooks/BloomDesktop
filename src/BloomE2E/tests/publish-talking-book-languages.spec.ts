// The Talking Book Languages check box list on the Publish tab: whose narration a publication
// carries. Automates the manual test "Talking Book Languages List".
//
// The list sits under the Text Languages list on both the Web and the BloomPUB screens, and both
// screens write one setting (BookInfo.PublishSettings.BloomLibrary.AudioLangs), so several of
// these tests check that the two screens agree.
//
// The rules it enforces are not the text list's rules (see publish-text-languages.spec.ts):
// narration is never REQUIRED, a row never says "(incomplete translation)", and a language's audio
// box depends on its TEXT box -- Bloom offers audio only for a language whose text is going in.
//
// THE ORDER OF THESE TESTS IS LOAD-BEARING, which is why they are serial. Bloom computes a
// language's check state from the defaults only until something clicks its box; from then on the
// value is Include or Exclude and is never recomputed. So every test of default behavior has to
// run before the test that clicks a box.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    clickLanguage,
    expectLanguageRows,
    expectTalkingBookFeature,
    getLanguagesInBook,
    getStagedNarrationIds,
    getTooltipForLanguage,
    openPublishDestination,
    stageBloomPub,
} from "../helpers/publish";
import { selectBook } from "../helpers/collection";
import {
    addNarration,
    getNarrationSentences,
    openToolboxWithTalkingBook,
} from "../helpers/talkingBook";
import { switchTab } from "../helpers/workspace";

// German is Language 2 so that the cover title can be typed in German -- the cover title box shows
// Language 1 and Language 2 only. German then has text in the front matter and nowhere else, which
// is the state that proves a language with no content-page text gets no check box in EITHER list.
// Nothing else in the book is German, and the collection keeps it throughout, so no restart is
// needed here (unlike the text-languages test, which needs Spanish to become a collection
// language later on).
const COLLECTION_LANGUAGES = ["en", "de", "fr"];

test.use({
    collectionSpec: {
        name: "talking-book-languages",
        languages: COLLECTION_LANGUAGES,
    },
});

test.describe.configure({ mode: "serial" });

const BOOK_TITLE = "Talking Book Languages Test";

// The book's folder, and the ids of the sentences the Talking Book tool marked on its one content
// page. Both are read back from Bloom by the first test, which is the only thing that knows them.
let bookFolder: string;

// The narration file ids the book ends up with, by language. The first test learns them from the
// Talking Book tool's own markup; the last test uses them to say which mp3s a publication should
// carry.
const narrationIds: Record<string, string[]> = {};

/** The row a language with narration shows: a box a person may clear, ticked to start with. */
const narrated = (name: string) => ({
    name,
    incomplete: false,
    checked: true,
    disabled: false,
});

/**
 * The row a language with no narration shows: listed, but unticked and not clickable. Bloom lists
 * every language of the text here, not only the narrated ones, and disables the rest.
 */
const notNarrated = (name: string) => ({
    name,
    incomplete: false,
    checked: false,
    disabled: true,
});

/**
 * The row a NARRATED language shows once a person clears its box. Unticked like notNarrated, but
 * still clickable -- the narration is there to be put back. The two states look alike in a
 * screenshot and are not the same thing.
 */
const narrationExcluded = (name: string) => ({
    name,
    incomplete: false,
    checked: false,
    disabled: false,
});

test.describe("the Talking Book Languages publish list", () => {
    test("builds a book with English and French text and narration in English only", async ({
        page,
    }) => {
        test.setTimeout(300000);

        await makeBookFromTemplate(page, "Basic Book");

        // The cover title, in English and in German. Nothing else in the book will be German.
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await typeInGroup(page, ".bookTitle", "de", "Hörbuchsprachen");

        await addPage(page, "Just Text", 1);
        await setContentLanguages(page, ["en", "fr"]);
        const contentPages = await getContentPages(page);
        expect(contentPages.length).toBe(1);
        await goToPage(page, contentPages[0].id);
        await typeInGroup(page, ".bloom-translationGroup", "en", "Hello.");
        await typeInGroup(page, ".bloom-translationGroup", "fr", "Bonjour.");

        // Opening the toolbox puts the Talking Book tool to work, which is what marks each
        // sentence with the id its narration file is named after.
        await openToolboxWithTalkingBook(page);
        const sentences = await getNarrationSentences(page);
        expect(
            sentences.map((s) => s.languageTag).sort(),
            "The Talking Book tool did not mark one sentence in each language, so there is nothing to narrate.",
        ).toEqual(["en", "fr"]);

        bookFolder = await findBookFolder(page, BOOK_TITLE);

        // English gets narration; French deliberately does not, so that the first test sees both
        // a language that has audio and one that does not.
        narrationIds.en = await addNarration(page, bookFolder, "en");

        // Leave the page, which is what makes Bloom write it to the file, and go back to showing
        // one language so English is the only required TEXT language.
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        await goToPage(page, cover!.id);
        await setContentLanguages(page, ["en"]);

        // Sanity check the book this whole file rests on, so a later failure means the list is
        // wrong rather than that the book was never built properly. The Publish tab has to be open
        // first: publish/languagesInBook answers for the book that tab holds.
        await openPublishDestination(page, "Web");
        const languages = await getLanguagesInBook(page);
        expect(languages.map((l) => l.code)).toEqual(["en", "fr"]);
        expect(
            languages.filter((l) => l.containsAnyAudio).map((l) => l.code),
            "Bloom did not see narration in English and only English.",
        ).toEqual(["en"]);
    });

    test("lists every language of the text, and ticks the ones with narration [Test Case ID 170]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        await openPublishDestination(page, "Web");

        // German is absent because it has no text on a content page -- the same rule the Text
        // Languages list follows. French is present but not clickable, because the book has no
        // French narration to include.
        await expectLanguageRows(
            page,
            "audio",
            [narrated("English"), notNarrated("French")],
            "The Web screen never showed English narrated and French listed-but-unavailable.",
        );

        // The BloomPUB screen shows the same list, because both screens read one setting.
        await openPublishDestination(page, "BloomPUB");
        await expectLanguageRows(
            page,
            "audio",
            [narrated("English"), notNarrated("French")],
            "The BloomPUB screen disagreed with the Web screen about the Talking Book languages.",
        );
    });

    test("says in a tooltip why a language's narration can or cannot be included [Test Case ID 170]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        await openPublishDestination(page, "Web");

        expect(await getTooltipForLanguage(page, "audio", "English")).toBe(
            "Select this if you want to include this audio.",
        );
        expect(await getTooltipForLanguage(page, "audio", "French")).toBe(
            "This is disabled because this book does not have any audio in this language.",
        );
    });

    test("ticks a language by default as soon as the book has narration in it [Test Case ID 170]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        test.setTimeout(180000);

        // Narrate French too. Its box has never been clicked, so Bloom is still free to compute
        // its state, and a newly narrated language comes in ticked.
        await switchTab(page, "edit");
        await setContentLanguages(page, ["en", "fr"]);
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[0].id);
        await openToolboxWithTalkingBook(page);
        narrationIds.fr = await addNarration(page, bookFolder, "fr");
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        await goToPage(page, cover!.id);
        await setContentLanguages(page, ["en"]);

        await openPublishDestination(page, "Web");
        await expectLanguageRows(
            page,
            "audio",
            [narrated("English"), narrated("French")],
            "French did not become available and ticked once the book had French narration.",
        );
    });

    test("turns the Talking Book feature on for as long as some narration is going in [Test Case ID 170]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        test.setTimeout(180000);

        await openPublishDestination(page, "Web");
        await expectTalkingBookFeature(
            page,
            true,
            "The Talking Book feature was off while both languages' narration was going in.",
        );

        // THE ACTION UNDER TEST: real clicks on real check boxes. One language left is still a
        // talking book...
        await clickLanguage(page, "audio", "French");
        await expectLanguageRows(
            page,
            "audio",
            [narrated("English"), narrationExcluded("French")],
            "Clearing French's box did not leave English ticked and French clear but still clickable.",
        );
        await expectTalkingBookFeature(
            page,
            true,
            "The Talking Book feature went off while English narration was still going in.",
        );

        // ...but no languages left is not.
        await clickLanguage(page, "audio", "English");
        await expectTalkingBookFeature(
            page,
            false,
            "The Talking Book feature stayed on after every language's narration was excluded.",
        );

        // Put English back, for the tests that follow.
        await clickLanguage(page, "audio", "English");
        await expectTalkingBookFeature(
            page,
            true,
            "Ticking English again did not turn the Talking Book feature back on.",
        );
    });

    test("keeps the choice a person makes, across screens and across a restart [Test Case ID 170]", async ({
        bloomApp,
    }) => {
        test.setTimeout(180000);

        // French is the excluded one, from the test before. The BloomPUB screen shows the same
        // setting, because there is only one.
        //
        // Every check here polls (expectLanguageRows) rather than reading the rows once: the list
        // is filled from publish/languagesInBook after the screen mounts, and a single read can
        // catch it before that answer arrives. Reading once made this test fail about one run in
        // five on a loaded machine.
        await openPublishDestination(bloomApp.page, "BloomPUB");
        await expectLanguageRows(
            bloomApp.page,
            "audio",
            [narrated("English"), narrationExcluded("French")],
            "The BloomPUB screen did not show French narration left out.",
        );

        // It survives leaving the tab and coming back.
        await switchTab(bloomApp.page, "collection");
        await openPublishDestination(bloomApp.page, "Web");
        await expectLanguageRows(
            bloomApp.page,
            "audio",
            [narrated("English"), narrationExcluded("French")],
            "Leaving the Publish tab and coming back lost the choice to leave French narration out.",
        );

        // And it survives quitting Bloom, because it is written into the book.
        const afterRestart = await bloomApp.restart();
        await selectBook(afterRestart, bookFolder);
        await openPublishDestination(afterRestart, "Web");
        await expectLanguageRows(
            afterRestart,
            "audio",
            [narrated("English"), narrationExcluded("French")],
            "Restarting Bloom lost the choice to leave French narration out.",
        );
    });

    test("offers no narration for a language whose text is not being published [Test Case ID 170]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        await openPublishDestination(page, "Web");

        // French text is optional (the book shows English only), so a person may clear it -- and
        // clearing it takes French narration off the table too, since there would be no French in
        // the book to listen to.
        await clickLanguage(page, "text", "French");
        await expectLanguageRows(
            page,
            "audio",
            [narrated("English"), notNarrated("French")],
            "Excluding French text did not also make its narration unavailable.",
        );

        // Put the French text back.
        await clickLanguage(page, "text", "French");
    });

    test("puts only the ticked languages' narration into the publication [Test Case ID 170]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        test.setTimeout(300000);

        // French is still the excluded one, from the tests above. Sanity check that, so that a
        // publication carrying one language's audio means the setting worked rather than that
        // there was only ever one language to carry.
        await openPublishDestination(page, "BloomPUB");
        await expectLanguageRows(
            page,
            "audio",
            [narrated("English"), narrationExcluded("French")],
            "This test needs exactly one of the two languages ticked to prove anything.",
        );
        expect(narrationIds.en.length).toBeGreaterThan(0);
        expect(narrationIds.fr.length).toBeGreaterThan(0);

        // A BloomPUB staged now should carry the English narration and not the French.
        const withoutFrench = await stageBloomPub(page);
        expect(
            getStagedNarrationIds(withoutFrench),
            "The publication did not carry exactly the English narration.",
        ).toEqual([...narrationIds.en].sort());

        // Tick French too, and the next one carries both.
        await clickLanguage(page, "audio", "French");
        await expectLanguageRows(
            page,
            "audio",
            [narrated("English"), narrated("French")],
            "Ticking French's box again did not take.",
        );
        const withBoth = await stageBloomPub(page);
        expect(
            getStagedNarrationIds(withBoth),
            "The publication did not carry both languages' narration once both were ticked.",
        ).toEqual([...narrationIds.en, ...narrationIds.fr].sort());
    });
});
