// "Add Image" is not offered in a field Bloom stores and rewrites for itself -- the book title
// being the one every person meets first.
//
// WHAT THIS IS ABOUT. getInlineImageActionTarget decides where the command is offered, and what
// it used to exclude was an editable inside a canvas element and an editable that is not a direct
// child of a translation group. A data-book field is neither of those, so the command was offered
// on the cover title, the credits page, and everywhere else in front and back matter. Nobody
// decided that; it was what the two exclusions left behind.
//
// Front and back matter is not stored where it is shown. BringXmatterHtmlUpToDate deletes and
// re-injects every xmatter page whenever the book is brought up to date, so cover content survives
// only through the data div: GatherDataItemsFromXElement stores the field's InnerXml, and
// SetNodeXml writes it back into EVERY element carrying the same data-book key. bookTitle is on
// both the cover and the title page, so a picture put on one became markup stored in the data div
// and written into both -- and the stored title is what names the book in the collection, the
// title bar, and AllTitles.
//
// Measured, before the exclusion was added: one picture added to the cover title produced four
// wrappers in the saved book, and the stored title read
// `<div data-bloom-inline-image-id="..." class="bloom-inlineImage bloom-inlineImageRight ...">`
// instead of the words the person typed.
//
// Bloom's own way to put a picture on a cover is a canvas element, which the person reaches from
// the same page, so nothing is taken away by not offering this one.

import * as fs from "node:fs";
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
import { bookHtmlPath } from "../helpers/bookHtml";
import { textBlockOffersAddImage } from "../helpers/inlineImages";

test.use({
    collectionSpec: { name: "inline-images-cover-title", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const TITLE_BLOCK = ".bookTitle";
// The other data-book field a person meets on the front cover.
const COVER_CREDITS_BLOCK = ".creditsRow .bloom-translationGroup";
const LANG = "en";
const BOOK_TITLE = "Inline Images Cover Title";

/** What the saved book holds: any inline image wrapper at all, and the stored title. */
const readFromDisk = (bookFolder: string) => {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return {
        wrappersInTheWholeFile: (
            html.match(/data-bloom-inline-image-id/g) ?? []
        ).length,
        // The stored title, as the data div holds it: markup and all.
        storedTitle: (html.match(
            /<div[^>]*data-book="bookTitle"[^>]*lang="en"[^>]*>([\s\S]*?)<\/div>/,
        ) ?? [, ""])[1]
            .trim()
            .slice(0, 400),
    };
};

test("Bloom does not offer to put a picture in the book title or the credits [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, TITLE_BLOCK, LANG, BOOK_TITLE);
    // Somewhere to go afterwards: leaving the cover is what makes Bloom save it, and a book made
    // from the Basic Book template starts with no content page to leave to.
    await addPage(page, "Just Text");
    const pagesAtStart = await getPages(page);
    const coverId = pagesAtStart.find((p) => !p.isContentPage)!.id;
    await goToPage(page, coverId);
    // The cover credits start empty, and the menu is opened by pointing at a spot on the text.
    await typeInGroup(page, COVER_CREDITS_BLOCK, LANG, "Written by someone");

    // THE THING UNDER TEST: the command the person would reach for on the block they are in.
    expect(
        await textBlockOffersAddImage(page, TITLE_BLOCK, LANG),
        `Bloom offered to put a picture in the book title. That field is stored in the data div ` +
            `as markup and written back into every element with the same data-book key, so the ` +
            `picture ends up on the title page as well and the wrapper's markup becomes the ` +
            `book's stored title.`,
    ).toBe(false);
    expect(
        await textBlockOffersAddImage(page, COVER_CREDITS_BLOCK, LANG),
        `Bloom offered to put a picture in the cover credits, which is a data-book field on an ` +
            `xmatter page and so has the same problem as the title.`,
    ).toBe(false);

    // And nothing about the front matter left inline image markup in the saved book. Leaving the
    // page is what makes Bloom save it; the collection learns the title at the same moment, which
    // is why findBookFolder comes after the navigation.
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    const onDisk = readFromDisk(await findBookFolder(page, BOOK_TITLE));
    expect(
        onDisk.wrappersInTheWholeFile,
        `The saved book holds ${onDisk.wrappersInTheWholeFile} inline image wrappers, though no ` +
            `picture was added anywhere.`,
    ).toBe(0);
    expect(
        onDisk.storedTitle,
        `The book's stored title holds inline image markup. It is what names this book in the ` +
            `collection, the title bar and AllTitles, and it reads: ${onDisk.storedTitle}`,
    ).not.toContain("bloom-inlineImage");
    expect(
        onDisk.storedTitle,
        "The book's stored title is not the words that were typed on the cover.",
    ).toContain(BOOK_TITLE);
});
