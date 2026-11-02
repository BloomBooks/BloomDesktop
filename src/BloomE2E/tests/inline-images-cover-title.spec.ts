// Checks that "Insert Image" is not offered in a data-book field, which Bloom stores in the data
// div and writes back into the page itself. The book title is the first such field anyone meets.
//
// getInlineImageActionTarget decides where the command is offered. Besides leaving out an
// editable inside a canvas element and an editable that is not a direct child of a translation
// group, it leaves out data-book fields, which are on the cover, the credits page, and
// throughout front and back matter.
//
// Bloom does not keep front and back matter where it is shown. BringXmatterHtmlUpToDate deletes
// every xmatter page and inserts it again whenever the book is brought up to date, so cover
// content survives only through the data div: GatherDataItemsFromXElement stores the field's
// InnerXml, and SetNodeXml writes it back into every element that has the same data-book key.
// bookTitle is on both the cover and the title page, so a picture put in one would be stored as
// markup in the data div and written into both. The stored title is also what names the book in
// the collection, the title bar, and AllTitles.
//
// When the command was offered on the cover title, adding one picture there produced four
// wrappers in the saved book, and the stored title read
// `<div data-bloom-inline-image-id="..." class="bloom-inlineImage bloom-inlineImageRight ...">`
// instead of the words the person typed.
//
// To put a picture on a cover, a person uses a canvas element, which they can add from the same
// page, so leaving out this command does not take anything away.

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
import { textBlockOffersInsertImage } from "../helpers/inlineImages";

test.use({
    collectionSpec: { name: "inline-images-cover-title", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const TITLE_BLOCK = ".bookTitle";
// The other data-book field a person meets on the front cover.
const COVER_CREDITS_BLOCK = ".creditsRow .bloom-translationGroup";
const LANG = "en";
const BOOK_TITLE = "Inline Images Cover Title";

/** Reads from the saved book how many inline image wrappers it has, and its stored title. */
const readFromDisk = (bookFolder: string) => {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return {
        wrappersInTheWholeFile: (
            html.match(/data-bloom-inline-image-id/g) ?? []
        ).length,
        // The stored title exactly as the data div holds it, including any markup.
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
    // Add a content page to go to later. Bloom saves the cover when the person leaves it, and a
    // book made from the Basic Book template starts with no content page.
    await addPage(page, "Just Text");
    const pagesAtStart = await getPages(page);
    const coverId = pagesAtStart.find((p) => !p.isContentPage)!.id;
    await goToPage(page, coverId);
    // The cover credits start empty, and the menu is opened by clicking on the text, so type some.
    await typeInGroup(page, COVER_CREDITS_BLOCK, LANG, "Written by someone");

    // The thing under test: whether each block's menu offers the command.
    expect(
        await textBlockOffersInsertImage(page, TITLE_BLOCK, LANG),
        `Bloom offered to put a picture in the book title. That field is stored in the data div ` +
            `as markup and written back into every element with the same data-book key, so the ` +
            `picture ends up on the title page as well and the wrapper's markup becomes the ` +
            `book's stored title.`,
    ).toBe(false);
    expect(
        await textBlockOffersInsertImage(page, COVER_CREDITS_BLOCK, LANG),
        `Bloom offered to put a picture in the cover credits, which is a data-book field on an ` +
            `xmatter page and so has the same problem as the title.`,
    ).toBe(false);

    // The saved book must have no inline image markup in its front matter. Bloom saves the page
    // when the person leaves it, and the collection learns the title at the same moment, so
    // findBookFolder has to come after going to the other page.
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
