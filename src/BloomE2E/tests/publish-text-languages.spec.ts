// The Text Languages check box list on the Publish tab: which languages of a book's text a
// publication may carry. Automates the manual test "Text Languages Publish List".
//
// The list appears on both the Web and the BloomPUB screens, and both write the same setting
// (BookInfo.PublishSettings.BloomLibrary.TextLangs), so several of these tests check that the two
// screens agree.
//
// THE ORDER OF THESE TESTS IS LOAD-BEARING, which is why they are serial. Bloom computes a
// language's check state from the defaults only until something clicks its box; from then on the
// value is Include or Exclude and is never recomputed. So every test of default behavior has to
// run before the test that clicks a box, and the file would quietly stop testing the defaults if
// they were reordered.

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
import { selectBook } from "../helpers/collection";
import { restartWithCollectionSettings } from "../helpers/collectionSettings";
import {
    clickTextLanguage,
    expectTextLanguageRows,
    expectTextLanguageRowsInAnyOrder,
    getLanguagesInBook,
    getPreviewLanguages,
    getTextLanguageRows,
    getTooltipForLanguage,
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

// The collection starts with German as Language 2 so that the book's cover title can be typed in
// German: the cover title box shows Language 1 and Language 2 only. Once the German title is in,
// the test rewrites the collection as English, French and Spanish, which leaves German present in
// the front matter and nowhere else — the state that proves a language with no content-page text
// gets no check box at all.
const COLLECTION_NAME = "text-languages";
const STARTING_LANGUAGES = ["en", "de", "fr"];
const FINAL_LANGUAGES = ["en", "fr", "es"];

test.use({
    collectionSpec: { name: COLLECTION_NAME, languages: STARTING_LANGUAGES },
});

test.describe.configure({ mode: "serial" });

// The title of the book every test in this file works on.
const BOOK_TITLE = "Text Languages Test";

// The book's folder. Built once, by the first test. Bloom renames the folder to match the title,
// so this is read back from Bloom rather than kept from when the book was made.
let bookFolder: string;

test.describe("the Text Languages publish list", () => {
    test("builds a book with three complete languages and a German front matter title", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);

        await makeBookFromTemplate(page, "Basic Book");

        // The cover title, in English and in German. Nothing else in the book will be German.
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await typeInGroup(page, ".bookTitle", "de", "Sprachentest");

        // Two content pages, each with English and French. Two, because a language is
        // "incomplete" when SOME content page lacks it: with one page there would be no
        // difference between an incomplete language and an absent one.
        await addPage(page, "Just Text", 2);
        await setContentLanguages(page, ["en", "fr"]);

        // Give each page its own words, so a later failure names the page it came from.
        const contentPages = await getContentPages(page);
        expect(contentPages.length).toBe(2);
        await goToPage(page, contentPages[0].id);
        await typeInGroup(page, ".bloom-translationGroup", "en", "One");
        await typeInGroup(page, ".bloom-translationGroup", "fr", "Un");
        await goToPage(page, contentPages[1].id);
        await typeInGroup(page, ".bloom-translationGroup", "en", "Two");
        await typeInGroup(page, ".bloom-translationGroup", "fr", "Deux");

        // Leave the page before the restart. The fixture stops Bloom by killing the process, so
        // anything typed on the page still showing is never written to the file.
        const coverBeforeRestart = (await getPages(page)).find(
            (p) => !p.isContentPage,
        );
        await goToPage(page, coverBeforeRestart!.id);

        // Now swap German out for Spanish. Collection settings have no API and their dialog is a
        // WinForms surface CDP cannot reach, so the way to change them is to quit Bloom, rewrite
        // the .bloomCollection, and start again. See AUTOMATION-DEBT.md.
        const newPage = await restartWithCollectionSettings(bloomApp, {
            languages: FINAL_LANGUAGES,
        });
        bookFolder = await findBookFolder(newPage, BOOK_TITLE);
        await selectBook(newPage, bookFolder);
        await switchTab(newPage, "edit");

        // Spanish on both content pages, so all three languages are complete.
        await setContentLanguages(newPage, ["en", "fr", "es"]);
        const pagesAfterRestart = await getContentPages(newPage);
        await goToPage(newPage, pagesAfterRestart[0].id);
        await typeInGroup(newPage, ".bloom-translationGroup", "es", "Uno");
        await goToPage(newPage, pagesAfterRestart[1].id);
        await typeInGroup(newPage, ".bloom-translationGroup", "es", "Dos");

        // Back to the cover, which is what makes Bloom save the page just typed, and then back to
        // showing one language, so English is the only required language.
        const cover = (await getPages(newPage)).find((p) => !p.isContentPage);
        await goToPage(newPage, cover!.id);
        await setContentLanguages(newPage, ["en"]);

        // Sanity check the book this whole file rests on, so a later failure means the list is
        // wrong rather than that the book was never built properly. The Publish tab has to be open
        // first: publish/languagesInBook answers for the book the Publish tab holds, and there is
        // no such book until the tab is entered.
        await openPublishDestination(newPage, "Web");
        const languages = await getLanguagesInBook(newPage);
        expect(languages.map((l) => l.code)).toEqual(["en", "fr", "es"]);
        expect(languages.filter((l) => !l.complete).map((l) => l.code)).toEqual(
            [],
        );
    });

    test("shows every language of the text, and only those, checked by default [Test Case ID 169]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        await openPublishDestination(page, "Web");

        // German is absent because it has no text on a content page; English is disabled because
        // the book shows it, which makes it required.
        await expectTextLanguageRows(
            page,
            [
                {
                    name: "English",
                    incomplete: false,
                    checked: true,
                    disabled: true,
                },
                {
                    name: "French",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
                {
                    name: "Spanish",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
            ],
            "The Web screen never showed the three complete languages, all checked.",
        );

        // The BloomPUB screen shows the same list, because both screens read one setting.
        await openPublishDestination(page, "BloomPUB");
        await expectTextLanguageRows(
            page,
            [
                {
                    name: "English",
                    incomplete: false,
                    checked: true,
                    disabled: true,
                },
                {
                    name: "French",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
                {
                    name: "Spanish",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
            ],
            "The BloomPUB screen disagreed with the Web screen about the text languages.",
        );
    });

    test("explains in a tooltip why a required language cannot be cleared [Test Case ID 169]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        await openPublishDestination(page, "Web");

        expect(await getTooltipForLanguage(page, "English")).toBe(
            "This is disabled because this language is currently shown in the book, so it is required.",
        );
        expect(await getTooltipForLanguage(page, "French")).toBe(
            "Select this if you want readers to be able to choose to read the book in this language.",
        );
    });

    test("makes a language required while the book shows it [Test Case ID 169]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;

        await switchTab(page, "edit");
        await setContentLanguages(page, ["en", "fr"]);
        await openPublishDestination(page, "Web");
        expect(
            (await getLanguagesInBook(page)).find((l) => l.code === "fr")
                ?.required,
        ).toBe(true);
        await expectTextLanguageRows(
            page,
            [
                {
                    name: "English",
                    incomplete: false,
                    checked: true,
                    disabled: true,
                },
                {
                    name: "French",
                    incomplete: false,
                    checked: true,
                    disabled: true,
                },
                {
                    name: "Spanish",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
            ],
            "French did not become required when the book started showing it.",
        );

        // Showing all three makes all three required.
        await switchTab(page, "edit");
        await setContentLanguages(page, ["en", "fr", "es"]);
        await openPublishDestination(page, "Web");
        expect((await getTextLanguageRows(page)).every((r) => r.disabled)).toBe(
            true,
        );

        // Back to one language: only English stays required.
        await switchTab(page, "edit");
        await setContentLanguages(page, ["en"]);
        await openPublishDestination(page, "Web");
        await expectTextLanguageRows(
            page,
            [
                {
                    name: "English",
                    incomplete: false,
                    checked: true,
                    disabled: true,
                },
                {
                    name: "French",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
                {
                    name: "Spanish",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
            ],
            "French and Spanish stayed required after the book stopped showing them.",
        );
    });

    test("marks a language whose translation is incomplete, and leaves it unchecked [Test Case ID 169]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        test.setTimeout(180000);

        // Take the French text off the second content page. French is then on one page but not
        // the other, which is what "incomplete" means.
        await switchTab(page, "edit");
        await setContentLanguages(page, ["en", "fr"]);
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[1].id);
        await typeInGroup(page, ".bloom-translationGroup", "fr", "");
        // Leave the page before anything else, so the cleared box is written to the file.
        await goToPage(page, contentPages[0].id);
        await setContentLanguages(page, ["en"]);

        await openPublishDestination(page, "Web");
        // An incomplete language sorts below the complete ones.
        await expectTextLanguageRows(
            page,
            [
                {
                    name: "English",
                    incomplete: false,
                    checked: true,
                    disabled: true,
                },
                {
                    name: "Spanish",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
                {
                    name: "French",
                    incomplete: true,
                    checked: false,
                    disabled: false,
                },
            ],
            "French was not marked incomplete, unchecked, and sorted last.",
        );

        // A language the book shows is required even when its translation is incomplete.
        await switchTab(page, "edit");
        await setContentLanguages(page, ["en", "fr"]);
        await openPublishDestination(page, "Web");
        const french = (await getTextLanguageRows(page)).find(
            (r) => r.name === "French",
        );
        expect(french).toEqual({
            name: "French",
            incomplete: true,
            checked: true,
            disabled: true,
        });

        // Put the French text back, so the tests after this one start from a complete book.
        await switchTab(page, "edit");
        await goToPage(page, contentPages[1].id);
        await typeInGroup(page, ".bloom-translationGroup", "fr", "Deux");
        await goToPage(page, contentPages[0].id);
        await setContentLanguages(page, ["en"]);
    });

    // KNOWN FLAKE, diagnosed, and BL-16806 is the card that fixes it -- so if you are here
    // because a nightly went red on this test, that is the known cause and there is a fix in
    // flight; nothing new to chase.
    //
    // The assertion captured: this test failed once in six full runs on
    // 2026-09-01, and again on CI run 33665790357 (2026-09-02, 12 passed / 1 failed). The
    // assertion that fails is the language NAME, and the whole point of the test:
    //
    //     Expected: español      Received: espagnol
    //
    // "espagnol" is French for Spanish. It is not a timing race in the publish list, and not the
    // editView/topBar/contentLanguageUsageChange suspicion, which never explained it: Bloom asks
    // LibPalaso for the name of the dropped language "in" the collection's metadata language, and
    // that call memoizes into a process-wide static dictionary that can hand back a name which
    // does not correspond to what was asked. So the answer depends on who asked for a language
    // name earlier in that run of Bloom. BL-16806 has the evidence. Everything else about the row
    // (unchecked, not incomplete, enabled) is right every time.
    //
    // Left running deliberately. It is a real nondeterminism in what Bloom shows a user, and the
    // one test that catches it; silencing it would only hide the bug the nightly just found.
    test("keeps a language that the collection no longer has, under its own name [Test Case ID 169]", async ({
        bloomApp,
    }) => {
        test.setTimeout(180000);

        // Drop Spanish from the collection. The book still has Spanish text, so the language stays
        // in the list; but the collection no longer supplies a name for it, so Bloom falls back to
        // the name the language calls itself.
        const withoutSpanish = await restartWithCollectionSettings(bloomApp, {
            languages: ["en", "fr"],
        });
        await selectBook(withoutSpanish, bookFolder);
        await openPublishDestination(withoutSpanish, "Web");
        // In any order: where a language the collection no longer names sits in the list is not
        // part of what this test is about.
        await expectTextLanguageRowsInAnyOrder(
            withoutSpanish,
            [
                {
                    name: "English",
                    incomplete: false,
                    checked: true,
                    disabled: true,
                },
                {
                    name: "French",
                    incomplete: false,
                    checked: true,
                    disabled: false,
                },
                {
                    name: "español",
                    incomplete: false,
                    checked: false,
                    disabled: false,
                },
            ],
            "Spanish did not stay in the list, unchecked and under its own name.",
        );

        // Put Spanish back, for the test that follows.
        const withSpanish = await restartWithCollectionSettings(bloomApp, {
            languages: FINAL_LANGUAGES,
        });
        await selectBook(withSpanish, bookFolder);
    });

    test("keeps the choice a person makes, across screens and across a restart [Test Case ID 169]", async ({
        bloomApp,
    }) => {
        test.setTimeout(180000);

        // THE ACTION UNDER TEST: a real click on a real check box.
        await openPublishDestination(bloomApp.page, "Web");
        await clickTextLanguage(bloomApp.page, "Spanish");
        await expect
            .poll(
                async () =>
                    (await getLanguagesInBook(bloomApp.page)).find(
                        (l) => l.code === "es",
                    )?.includeText,
                {
                    message:
                        "Clearing the Spanish box did not change the setting.",
                },
            )
            .toBe(false);

        // The BloomPUB screen shows the same setting, because there is only one.
        await openPublishDestination(bloomApp.page, "BloomPUB");
        expect(
            (await getTextLanguageRows(bloomApp.page)).find(
                (r) => r.name === "Spanish",
            )?.checked,
        ).toBe(false);

        // The publication itself carries only the languages left checked.
        const player = await showBloomPubPreview(bloomApp.page);
        expect((await getPreviewLanguages(player)).sort()).toEqual([
            "English",
            "French",
        ]);

        // Put Spanish back, and the publication carries all three. bloom-player names a language
        // the way the language names itself, so Spanish appears as "español (Spanish)".
        await clickTextLanguage(bloomApp.page, "Spanish");
        const playerWithSpanish = await showBloomPubPreview(bloomApp.page);
        // Sorted, because the order bloom-player lists them in is not what this test is about.
        expect((await getPreviewLanguages(playerWithSpanish)).sort()).toEqual(
            ["English", "French", "español (Spanish)"].sort(),
        );
        await clickTextLanguage(bloomApp.page, "Spanish");

        // It survives leaving the tab and coming back.
        await switchTab(bloomApp.page, "collection");
        await openPublishDestination(bloomApp.page, "Web");
        expect(
            (await getTextLanguageRows(bloomApp.page)).find(
                (r) => r.name === "Spanish",
            )?.checked,
        ).toBe(false);

        // And it survives quitting Bloom, because it is written into the book.
        const afterRestart = await bloomApp.restart();
        await selectBook(afterRestart, bookFolder);
        await openPublishDestination(afterRestart, "Web");
        expect(
            (await getTextLanguageRows(afterRestart)).find(
                (r) => r.name === "Spanish",
            )?.checked,
        ).toBe(false);
    });
});
