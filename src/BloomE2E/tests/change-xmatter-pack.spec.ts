// Changing the collection's front/back matter pack in the Collection Settings dialog changes the
// books already in the collection. Automates the manual test "Change Xmatter Pack" [Test Case ID
// 651]: a person chooses another style on the Front & Back Matter page, presses Restart, and
// finds that a book made before the change now has that pack's front and back matter pages.
//
// xmatter-packs.spec.ts checks the pages of every pack, choosing each by rewriting the
// .bloomCollection; collection-settings-front-back-matter.spec.ts checks that the dialog saves the
// choice. This test joins the two: the choice made in the dialog reaches the book.
//
// The pack is chosen by its value, not by the name the dialog shows, so the test does not depend
// on the UI language.

import type { Page } from "@playwright/test";
import { expect, test, type IBloomApp } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import {
    addPage,
    findBookFolder,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    visitXmatterPages,
    type IShownXmatterPage,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    chooseCollectionSettingsListChoiceByValue,
    getCollectionSettingsOkLabel,
    openCollectionSettings,
    readSavedCollectionSetting,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "change-xmatter-pack", languages: ["en"] },
});

const BOOK_TITLE = "Change Xmatter Pack Test";

/** A pack, by its key (its folder name without "-XMatter"), and the pages it gives a book. */
interface IPack {
    key: string;
    pages: string[];
}

// From each pack's .pug in src/content/templates/xMatter, as in xmatter-packs.spec.ts.
const PAPER_SAVER: IPack = {
    key: "Factory",
    pages: [
        "frontCover",
        "credits",
        "titlePage",
        "insideBackCover",
        "outsideBackCover",
    ],
};
const TRADITIONAL: IPack = {
    key: "Traditional",
    pages: [
        "frontCover",
        "insideFrontCover",
        "titlePage",
        "credits",
        "insideBackCover",
        "outsideBackCover",
    ],
};
const SUPER_PAPER_SAVER: IPack = {
    key: "SuperPaperSaver",
    pages: ["frontCover", "titlePage", "credits", "outsideBackCover"],
};

/** Check that the Edit tab showed exactly this pack's pages, each styled by this pack. */
function expectPackPages(shown: IShownXmatterPage[], pack: IPack): void {
    expect(
        shown.map((p) => p.xmatterPage),
        `The book does not have ${pack.key}'s front and back matter pages. ` +
            `Shown: ${shown.map((p) => p.caption).join(", ")}.`,
    ).toEqual(pack.pages);
    for (const p of shown) {
        expect(
            p.stylesheets,
            `The "${p.caption}" page was not styled by ${pack.key}-XMatter.css. ` +
                `Its stylesheets: ${p.stylesheets.join(", ")}.`,
        ).toContain(`${pack.key}-XMatter.css`);
    }
}

/**
 * Choose this pack on the Front & Back Matter page of the Collection Settings dialog and press
 * Restart, as a person does. Returns the reopened shell page.
 */
async function choosePackInSettings(
    bloomApp: IBloomApp,
    page: Page,
    pack: IPack,
): Promise<Page> {
    await switchTab(page, "collection");
    await openCollectionSettings(page);
    await showCollectionSettingsPage(
        page,
        await getUiString(
            page,
            "CollectionSettingsDialog.FrontBackMatterPage",
            "Front & Back Matter",
        ),
    );
    await chooseCollectionSettingsListChoiceByValue(
        page,
        await getUiString(
            page,
            "CollectionSettingsDialog.FrontBackMatterPage.Style",
            "Style",
        ),
        pack.key,
    );
    // Every book has to be rebuilt with the new pack, so OK becomes Restart.
    await expect.poll(() => getCollectionSettingsOkLabel(page)).toBe("Restart");
    return restartFromCollectionSettings(bloomApp);
}

/** Show the book in the Edit tab and report its front and back matter pages. */
async function showBookXmatter(
    page: Page,
    bookFolder: string,
): Promise<IShownXmatterPage[]> {
    await switchTab(page, "collection");
    await selectBook(page, bookFolder);
    await switchTab(page, "edit");
    return visitXmatterPages(page);
}

test("choosing another front/back matter style in Settings changes the books in the collection [Test Case ID 651]", async ({
    page,
    bloomApp,
}) => {
    test.setTimeout(300000);

    // The collection starts on Factory, which the dialog calls Paper Saver.
    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "XMatterPack"),
    ).toBe(PAPER_SAVER.key);

    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
    // One content page, so the book has an inside for the front and back matter to wrap.
    await addPage(page, "Just Text");
    // Leave the page being edited, so the title reaches the file and names the book's folder.
    const cover = (await getPages(page)).find((p) => !p.isContentPage);
    await goToPage(page, cover!.id);
    const bookFolder = await findBookFolder(page, BOOK_TITLE);

    // Before any change, the book has the collection's pack.
    expectPackPages(await showBookXmatter(page, bookFolder), PAPER_SAVER);

    // A pack that adds a page the book did not have (the inside front cover) and reorders others.
    let reopened = await choosePackInSettings(bloomApp, page, TRADITIONAL);
    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "XMatterPack"),
    ).toBe(TRADITIONAL.key);
    expectPackPages(await showBookXmatter(reopened, bookFolder), TRADITIONAL);

    // A pack that takes pages away (both inside covers): the book loses them.
    reopened = await choosePackInSettings(
        bloomApp,
        reopened,
        SUPER_PAPER_SAVER,
    );
    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "XMatterPack"),
    ).toBe(SUPER_PAPER_SAVER.key);
    expectPackPages(
        await showBookXmatter(reopened, bookFolder),
        SUPER_PAPER_SAVER,
    );

    // The book's content page came through both changes.
    const pages = await getPages(reopened);
    expect(
        pages.filter((p) => p.isContentPage),
        "the book's content page should survive changing its front/back matter",
    ).toHaveLength(1);
});
