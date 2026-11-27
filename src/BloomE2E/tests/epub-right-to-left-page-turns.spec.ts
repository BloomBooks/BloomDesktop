// A book whose first language is written right to left is read from right to left, so its next
// page lies to the left. The ePUB that Bloom makes from it says so to a reader app (the spine's
// page-progression-direction is "rtl", EpubMaker.cs), and the ePUB screen's preview shows it in a
// reader whose arrows are swapped (Readium/indexRtl.html): the left arrow turns forward and the
// right one back.
//
// Automates the manual test "RTL ePUB Navigation Arrows" (Test Case ID 163). The case marks the
// language right to left in its script settings, which are now on the language's More page of the
// Languages page of Collection Settings ("This is a right to left script"), then makes the ePUB and
// checks that the arrows work the opposite way. The same book is first previewed while the language
// is left to right, so the test also shows that the arrows' direction follows the setting.
//
// Not checked: the case's "things look right", which needs a person's eyes. The case itself notes
// that hiding the arrow that does nothing on the first or last page is not addressed, so that is
// not checked either.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { addPage, makeBookFromTemplate } from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    closeCollectionSettingsLanguageMore,
    getCollectionSettingsCheckbox,
    getCollectionSettingsOkLabel,
    kEnterpriseSubscriptionCode,
    openCollectionSettings,
    openCollectionSettingsLanguageMore,
    readSavedLanguageSetting,
    restartFromCollectionSettings,
    saveCollectionSettings,
    setCollectionSettingsCheckbox,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    clickEpubPreviewArrow,
    getEpubPreviewPageAddress,
    getEpubPreviewSpine,
    isEpubPreviewReaderRightToLeft,
    openEpubPublishScreen,
    showEpubPreview,
} from "../helpers/epubPublish";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "epub-rtl-page-turns",
        languages: ["en"],
        // Publishing an ePUB needs Pro.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kLanguage1 = "Written Language 1";
const kRightToLeft = "This is a right to left script";

test("in a book whose language is right to left, the ePUB preview's left arrow turns forward and its right arrow back [Test Case ID 163]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(420000);

    // The helpers find the Settings dialog's pages and rows, and the Add Page dialog's pages, by
    // their English names. Switching the UI language can replace the shell page, so the page is
    // read from bloomApp after it.
    await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    const bookFolder = (await step(
        "Make a book with two more pages, so the preview has pages to turn through",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text", 2);
            return folder;
        },
    ))!;

    await step(
        "While the language is left to right, the preview's right arrow turns forward and its left arrow back",
        async () => {
            await openEpubPublishScreen(page);
            await showEpubPreview(page);
            expect(
                isEpubPreviewReaderRightToLeft(page),
                "sanity check: a left-to-right book is previewed in the left-to-right reader",
            ).toBe(false);
            const spine = await getEpubPreviewSpine(page);
            expect(
                spine.direction,
                "sanity check: a left-to-right book's ePUB does not say it runs right to left",
            ).not.toBe("rtl");
            await expectArrowsToTurn(page, spine.pages, "right", "left");
        },
    );

    const reopened = (await step(
        `Tick "${kRightToLeft}" on the first language's More page of Collection Settings, and click OK`,
        async () => {
            await switchTab(page, "collection");
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Languages");
            await openCollectionSettingsLanguageMore(page, kLanguage1);
            expect(
                (await getCollectionSettingsCheckbox(page, kRightToLeft))
                    .checked,
                "sanity check: English starts as left to right",
            ).toBe(false);
            await setCollectionSettingsCheckbox(page, kRightToLeft, true);
            await closeCollectionSettingsLanguageMore(page);
            // Bloom decides whether this change needs the collection reopened.
            if ((await getCollectionSettingsOkLabel(page)) === "Restart")
                return restartFromCollectionSettings(bloomApp);
            await saveCollectionSettings(page);
            return page;
        },
    ))!;
    expect(
        readSavedLanguageSetting(bloomApp.collectionDir, 1, "IsLanguageRtl"),
        "Bloom should have saved the first language as right to left.",
    ).toBe("true");

    await step(
        "Make the ePUB again: it says it runs right to left, and the preview's left arrow turns forward and its right arrow back",
        async () => {
            await selectBook(reopened, bookFolder);
            await openEpubPublishScreen(reopened);
            await showEpubPreview(reopened);
            expect(
                isEpubPreviewReaderRightToLeft(reopened),
                "A right-to-left book should be previewed in the right-to-left reader.",
            ).toBe(true);
            const spine = await getEpubPreviewSpine(reopened);
            expect(
                spine.direction,
                "The ePUB should tell a reader app that it runs right to left.",
            ).toBe("rtl");
            await expectArrowsToTurn(reopened, spine.pages, "left", "right");
        },
    );
});

/**
 * From the first page, where the preview opens, click the `forward` arrow twice and then the
 * `back` arrow twice, and expect each click to turn one page that way through `pages`, the ePUB's
 * reading order (getEpubPreviewSpine).
 */
async function expectArrowsToTurn(
    page: Page,
    pages: string[],
    forward: "left" | "right",
    back: "left" | "right",
): Promise<void> {
    expect(
        pages.length,
        "sanity check: the ePUB has at least three pages to turn through",
    ).toBeGreaterThanOrEqual(3);
    await expect
        .poll(() => getEpubPreviewPageAddress(page), {
            timeout: 30000,
            message: "The preview should open on the ePUB's first page.",
        })
        .toBe(pages[0]);
    expect(
        await clickEpubPreviewArrow(page, forward),
        `The ${forward} arrow should turn from the first page to the second.`,
    ).toBe(pages[1]);
    expect(
        await clickEpubPreviewArrow(page, forward),
        `The ${forward} arrow should turn from the second page to the third.`,
    ).toBe(pages[2]);
    expect(
        await clickEpubPreviewArrow(page, back),
        `The ${back} arrow should turn from the third page back to the second.`,
    ).toBe(pages[1]);
    expect(
        await clickEpubPreviewArrow(page, back),
        `The ${back} arrow should turn from the second page back to the first.`,
    ).toBe(pages[0]);
}
