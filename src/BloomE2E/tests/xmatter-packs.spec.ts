// The front/back matter packs Bloom ships. Automates the manual test "Try Different Xmatter
// Options": give a collection each pack in turn, and see that a book gets that pack's pages.
//
// A pack is chosen in the collection Settings dialog, a WinForms surface CDP cannot reach, so each
// test here changes the pack by rewriting the .bloomCollection and restarting Bloom (see
// AUTOMATION-DEBT.md). What the test then measures is what a tester looks at: the front and back
// matter pages the Edit tab shows for the book, in order, and the stylesheet each was shown with.
//
// Story Producer is not in the Settings dialog's list at all. It is a project-specific pack that a
// collection gets only through the Story Producer branding, so that test sets the branding instead.
//
// The tests are serial because they share one book, built by the first test.

import { expect, test } from "../fixtures/bloomTest";
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
    restartWithCollectionSettings,
    setBranding,
} from "../helpers/collectionSettings";
import { switchTab } from "../helpers/workspace";

const COLLECTION_NAME = "xmatter-packs";
const LANGUAGES = ["en"];

test.use({
    collectionSpec: { name: COLLECTION_NAME, languages: LANGUAGES },
});

test.describe.configure({ mode: "serial" });

// The title of the book every test in this file works on.
const BOOK_TITLE = "Xmatter Packs Test";

/** One pack the Settings dialog offers, and the front/back matter pages it gives a book. */
interface IXmatterPackOption {
    /** The name the Settings dialog shows, which is also the name on the manual test card. */
    option: string;
    /** The pack's key: its folder name without "-XMatter", and the name of its stylesheet. */
    key: string;
    /** The data-xmatter-page of each front and back matter page, in book order. */
    pages: string[];
}

// Each pack's pages come from its .pug in src/content/templates/xMatter. Device puts everything but
// the cover at the back, so a book with it has one front matter page and five back matter pages.
const PACKS_IN_SETTINGS: IXmatterPackOption[] = [
    {
        option: "Traditional (default)",
        key: "Traditional",
        pages: [
            "frontCover",
            "insideFrontCover",
            "titlePage",
            "credits",
            "insideBackCover",
            "outsideBackCover",
        ],
    },
    {
        option: "Paper Saver",
        key: "Factory",
        pages: [
            "frontCover",
            "credits",
            "titlePage",
            "insideBackCover",
            "outsideBackCover",
        ],
    },
    {
        option: "Super Paper Saver",
        key: "SuperPaperSaver",
        pages: ["frontCover", "titlePage", "credits", "outsideBackCover"],
    },
    {
        option: "Device",
        key: "Device",
        pages: [
            "frontCover",
            "titlePage",
            "credits",
            "insideFrontCover",
            "insideBackCover",
            "outsideBackCover",
        ],
    },
    {
        // SIL-PNG extends Traditional: the same pages, with PNG-specific credits fields.
        option: "SIL-PNG",
        key: "SIL-PNG",
        pages: [
            "frontCover",
            "insideFrontCover",
            "titlePage",
            "credits",
            "insideBackCover",
            "outsideBackCover",
        ],
    },
];

// Story Producer extends Device and adds a configuration page after the cover.
const STORY_PRODUCER: IXmatterPackOption = {
    option: "Story Producer",
    key: "StoryProducer",
    pages: [
        "frontCover",
        "spConfigurationPage",
        "titlePage",
        "credits",
        "insideFrontCover",
        "insideBackCover",
        "outsideBackCover",
    ],
};
const STORY_PRODUCER_BRANDING = "Story-Producer-App";

/** Check that the Edit tab showed exactly this pack's pages, each styled by this pack. */
function expectPackPages(
    shown: IShownXmatterPage[],
    pack: IXmatterPackOption,
): void {
    expect(
        shown.map((p) => p.xmatterPage),
        `${pack.option} did not give the book its front and back matter pages. ` +
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

test.describe("the front/back matter packs Bloom ships", () => {
    test("builds a book to try each pack on", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        // One content page, so the book has an inside for the front and back matter to wrap.
        await addPage(page, "Just Text");

        // Leave the page being edited before the first restart, so the title reaches the file.
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        await goToPage(page, cover!.id);

        // Adding the page saved the book, which also gave its folder the title. Make sure of that
        // before any test restarts Bloom on it: a book not yet saved under its title cannot be
        // found by title afterwards.
        await findBookFolder(page, BOOK_TITLE);
    });

    for (const pack of PACKS_IN_SETTINGS) {
        test(`${pack.option} gives a book its front and back matter pages [Test Case ID 66]`, async ({
            bloomApp,
        }) => {
            test.setTimeout(300000);
            const page = await restartWithCollectionSettings(bloomApp, {
                languages: LANGUAGES,
                xmatterPack: pack.key,
            });
            await selectBook(page, await findBookFolder(page, BOOK_TITLE));
            await switchTab(page, "edit");

            expectPackPages(await visitXmatterPages(page), pack);
        });
    }

    test(`${STORY_PRODUCER.option} gives a book its front and back matter pages [Test Case ID 66]`, async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // Start from the default pack, so the pages seen below can only have come from the
        // branding. The branding is applied from the Collections tab, where the book is selected
        // but not being edited.
        const page = await restartWithCollectionSettings(bloomApp, {
            languages: LANGUAGES,
        });
        await selectBook(page, await findBookFolder(page, BOOK_TITLE));
        await setBranding(page, STORY_PRODUCER_BRANDING);
        await switchTab(page, "edit");

        expectPackPages(await visitXmatterPages(page), STORY_PRODUCER);
    });
});
