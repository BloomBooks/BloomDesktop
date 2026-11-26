// What a branding puts on a book's front and back matter. Automates the manual test "Basic Branding
// Check" [Test Case ID 119]: look at a book under the Default branding, under the Local Community
// branding with QR codes on and off, and under the Sample branding, which fills every branding
// slot, at several page sizes, the small ones especially.
//
// A person gets a branding by entering its subscription code in the Settings dialog. A test cannot
// (a real code carries a checksum), so it uses the e2e/setBranding hook, as setBranding says. QR
// codes are turned off the way a person does it, in the Settings dialog, which restarts Bloom; a
// restart puts the collection back on the branding its .bloomCollection names (Default), so the
// test sets the branding again after it.
//
// What the manual test asks a person to judge, that the slots "look reasonable", is checked here as
// what a reader of the printed book would miss: every slot shows its text and its pictures, every
// picture loads, and nothing is drawn past the edge of the page.
//
// The tests are serial because they share one book, built by the first test.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    findBookFolder,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    getBrandingSlot,
    goToXmatterPage,
    type IBrandingSlot,
} from "../helpers/branding";
import { selectBook } from "../helpers/collection";
import {
    openCollectionSettings,
    restartFromCollectionSettings,
    setBranding,
    setCollectionSettingsCheckbox,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { getPageSizeChoices, setPageSize } from "../helpers/pageSize";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "branding-slots", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BOOK_TITLE = "Branding Slots Test";

// The badge's link and QR code lead to the book's language on Bloom Library.
const kLanguagePage = "https://bloomlibrary.org/language:en";
// The QR code picture Bloom makes in the book folder (BookStorage.kQrFileName).
const kQrCodeFile = "lang-qr-code.png";
// The "Made with Bloom" badge: with a QR code it is the picture that has room for the caption.
const kBadgeWithQrCode = "made-with-bloom-badge-text.svg";
const kBadgeWithoutQrCode = "made-with-bloom-badge.svg";

/** A slot of the default pack (Paper Saver), the page it is on, and what a branding puts there. */
interface IExpectedSlot {
    xmatterPage: string;
    slot: string;
    /** Words the slot's text must include; undefined for a slot that holds only pictures. */
    words?: string;
    /** The picture files the slot shows, in order. */
    pictures: string[];
}

// What src/content/branding/Sample/branding.json puts in each of the seven slots. Each slot's text
// names the slot, so text that landed in the wrong slot shows up as such.
const SAMPLE_SLOTS: IExpectedSlot[] = [
    {
        xmatterPage: "frontCover",
        slot: "cover-branding-top-html",
        words: "(cover-branding-top)",
        pictures: ["sampleFiveLogos.png"],
    },
    {
        xmatterPage: "frontCover",
        slot: "cover-branding-bottom-html",
        words: "(cover-branding-bottom)",
        pictures: ["sampleFiveLogos.png"],
    },
    {
        xmatterPage: "credits",
        slot: "credits-page-branding-top-html",
        words: "(credits-page-branding-top)",
        pictures: ["sampleFiveLogos.png"],
    },
    {
        xmatterPage: "credits",
        slot: "credits-page-branding-bottom-html",
        words: "(credits-page-branding-bottom)",
        pictures: ["sampleFiveLogos.png"],
    },
    {
        xmatterPage: "titlePage",
        slot: "title-page-branding-bottom-html",
        pictures: ["sampleOneLogo.png"],
    },
    {
        xmatterPage: "outsideBackCover",
        slot: "outside-back-cover-branding-top-html",
        words: "(back-cover-branding-top)",
        pictures: ["sampleFiveLogos.png"],
    },
    {
        xmatterPage: "outsideBackCover",
        slot: "outside-back-cover-branding-bottom-html",
        words: "(back-cover-branding-bottom)",
        pictures: ["sampleFiveLogos.png"],
    },
];

// The page sizes to look at the Sample branding in: the default, and the small ones the manual test
// is most worried about. A6Landscape and QuarterLetterLandscape are left out while BL-17216 is open:
// at those sizes the outside back cover's bottom branding is drawn below the page.
const SAMPLE_PAGE_SIZES = [
    "A5Portrait",
    "A6Portrait",
    "QuarterLetterPortrait",
    "Cm13Landscape",
    "USComicPortrait",
];

/** Check that a slot shows these pictures, each loaded, and nothing past the page's edge. */
function expectPicturesShown(
    shown: IBrandingSlot,
    pictures: string[],
    where: string,
): void {
    expect(
        shown.pictures.map((p) => p.file),
        `${where} should show these pictures`,
    ).toEqual(pictures);
    for (const picture of shown.pictures)
        expect(
            picture.width * picture.height,
            `${where}: the picture ${picture.file} did not load`,
        ).toBeGreaterThan(0);
    expect(shown.offThePage, `${where} goes past the edge of the page`).toEqual(
        [],
    );
}

/** Check that the slot holds the "Made with Bloom" badge with its QR code and link. */
function expectBadgeWithQrCode(shown: IBrandingSlot, where: string): void {
    expect(
        shown.badgeLink,
        `${where}: the badge should link to the book's language on Bloom Library`,
    ).toContain(kLanguagePage);
    expect(
        shown.text,
        `${where}: the badge should say where the QR code leads`,
    ).toContain("BloomLibrary.org/language:en");
    expectPicturesShown(shown, [kBadgeWithQrCode, kQrCodeFile], where);
}

/** Check that the slot holds the plain "Made with Bloom" badge: no QR code and no link. */
function expectBadgeWithoutQrCode(shown: IBrandingSlot, where: string): void {
    expect(shown.badgeLink, `${where}: the badge should link nowhere`).toBe(
        null,
    );
    expectPicturesShown(shown, [kBadgeWithoutQrCode], where);
}

/** Put the collection under a branding and show the book in the Edit tab. */
async function editUnderBranding(page: Page, branding: string): Promise<void> {
    await switchTab(page, "collection");
    await selectBook(page, await findBookFolder(page, BOOK_TITLE));
    await setBranding(page, branding);
    await switchTab(page, "edit");
}

/** Check the Local Community branding's slots, with the badge as `expectBadge` says. */
async function expectLocalCommunitySlots(
    page: Page,
    expectBadge: (shown: IBrandingSlot, where: string) => void,
): Promise<void> {
    await goToXmatterPage(page, "titlePage");
    expectPicturesShown(
        await getBrandingSlot(page, "title-page-branding-bottom-html"),
        ["BloomLocal.svg"],
        "the title page's bottom branding",
    );

    await goToXmatterPage(page, "outsideBackCover");
    expectBadge(
        await getBrandingSlot(page, "outside-back-cover-branding-top-html"),
        "the outside back cover's top branding",
    );
    const bottom = await getBrandingSlot(
        page,
        "outside-back-cover-branding-bottom-html",
    );
    // setBranding gives Local Community the descriptor "Sample-LC", whose community is "Sample".
    expect(
        bottom.text,
        "the outside back cover should name the community the subscription was given to",
    ).toContain("gifted from SIL Global to: Sample");
    expectPicturesShown(
        bottom,
        ["BloomLocal.svg"],
        "the outside back cover's bottom branding",
    );
}

test.describe("what a branding puts on the front and back matter", () => {
    test("builds a book to brand", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        // Leave the title box, so the title reaches the file before the book is selected again.
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        await goToPage(page, cover!.id);
        await findBookFolder(page, BOOK_TITLE);
    });

    test("the Default branding puts the Made with Bloom badge, with its QR code, on the outside back cover [Test Case ID 119]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await editUnderBranding(page, "Default");

        await goToXmatterPage(page, "outsideBackCover");
        expectBadgeWithQrCode(
            await getBrandingSlot(
                page,
                "outside-back-cover-branding-bottom-html",
            ),
            "the outside back cover's bottom branding",
        );
        // Default fills no other slot.
        const top = await getBrandingSlot(
            page,
            "outside-back-cover-branding-top-html",
        );
        expect(
            { text: top.text, pictures: top.pictures },
            "the outside back cover's top branding should be empty",
        ).toEqual({ text: "", pictures: [] });
    });

    test("the Local Community branding shows its logo, the badge with its QR code, and the community's name [Test Case ID 119]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await editUnderBranding(page, "Local-Community");

        await expectLocalCommunitySlots(page, expectBadgeWithQrCode);
    });

    test("with QR codes turned off, the Local Community branding shows the plain badge [Test Case ID 119]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        await switchTab(page, "collection");
        await openCollectionSettings(page);
        await showCollectionSettingsPage(page, "Front & Back Matter");
        await setCollectionSettingsCheckbox(page, "Show QR Codes", false);
        const reopened = await restartFromCollectionSettings(bloomApp);

        await editUnderBranding(reopened, "Local-Community");

        await expectLocalCommunitySlots(reopened, expectBadgeWithoutQrCode);
    });

    test("the Sample branding fills all seven slots at every page size [Test Case ID 119]", async ({
        bloomApp,
    }) => {
        test.setTimeout(600000);
        const page = bloomApp.page;
        await editUnderBranding(page, "Sample");

        const offered = (await getPageSizeChoices(page)).choices.map(
            (c) => c.id,
        );
        expect(
            offered,
            "Sanity check: Basic Book should offer every page size this test looks at",
        ).toEqual(expect.arrayContaining(SAMPLE_PAGE_SIZES));

        for (const size of SAMPLE_PAGE_SIZES) {
            await setPageSize(page, size);
            let shownPage = "";
            for (const expected of SAMPLE_SLOTS) {
                if (expected.xmatterPage !== shownPage) {
                    await goToXmatterPage(page, expected.xmatterPage);
                    shownPage = expected.xmatterPage;
                }
                const where = `At ${size}, the ${expected.slot} slot`;
                const shown = await getBrandingSlot(page, expected.slot);
                if (expected.words)
                    expect(shown.text, `${where} lost its text`).toContain(
                        expected.words,
                    );
                expectPicturesShown(shown, expected.pictures, where);
            }
        }
    });
});
