// A book template that a person made shows on the Collections tab with a dashed black border
// around its thumbnail, so that it stands out from the books made from it. Bloom draws the border
// into the template's thumbnail.png when it makes the thumbnail (BookThumbNailer, for a book whose
// meta.json says it is suitable for making shells). Automates the manual test "Custom Templates
// Dashed Thumbnail Border".
//
// The template is made with Template Starter, as people make them, and a book is made from it.
// Both have the same cover (the picture placeholder), so the only thing that should tell their
// thumbnails apart is the border: the template's thumbnail has dashes along its top and left
// edges, and the book's has no black there at all.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    findBookFolder,
    getPages,
    goToPage,
    makeBookFromBookInCollection,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    countDashes,
    readBookId,
    readBookTileThumbnailEdges,
    type IThumbnailEdges,
} from "../helpers/collection";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "custom-template-thumbnail", languages: ["en"] },
});

// Template Starter names every new template "My Template", and a developer's machine can hold a
// downloaded template of that name, so the test gives its template a name of its own.
const TEMPLATE_TITLE = "TC76 Custom Template";
// A dashed edge has at least this many separate dashes; a 2-pixel dash pattern along a thumbnail
// edge of any size Bloom makes has many more.
const MIN_DASHES = 3;

function describeEdges(edges: IThumbnailEdges): string {
    return (
        `${edges.width}x${edges.height}; top edge runs (black first) ` +
        `${edges.topRuns.join(",")}; left edge runs ${edges.leftRuns.join(",")}`
    );
}

test("a custom template's thumbnail has a dashed border and a book made from it has none [Test Case ID 76]", async ({
    page,
    step,
}) => {
    test.setTimeout(400000);

    const templateFolder = await step(
        "Make a template with Template Starter and give it a title",
        async () => {
            await makeBookFromTemplate(page, "Template Starter");
            // The template's title is the "Template Title" box of its "About" page. Leaving that
            // page saves it, and Bloom then renames the folder to match.
            const pages = await getPages(page);
            const aboutPage = pages.find((p) => p.caption === "About");
            expect(
                aboutPage,
                `Template Starter made a book with no "About" page. Its pages: ` +
                    pages.map((p) => p.caption).join(", "),
            ).toBeDefined();
            await goToPage(page, aboutPage!.id);
            await typeInGroup(page, ".title", "en", TEMPLATE_TITLE);
            await goToPage(page, pages[0].id);
            const folder = await findBookFolder(page, TEMPLATE_TITLE);
            expect(
                Path.basename(folder),
                "Bloom did not rename the template's folder to match its new title",
            ).toBe(TEMPLATE_TITLE);
            return folder;
        },
    );
    if (!templateFolder) throw new Error("No template was made.");

    const bookFolder = await step("Make a book from the template", () =>
        makeBookFromBookInCollection(page, templateFolder),
    );
    if (!bookFolder) throw new Error("No book was made from the template.");
    expect(
        Path.normalize(bookFolder),
        "Bloom did not make a new book; it is showing the template",
    ).not.toBe(Path.normalize(templateFolder));

    await step("Go to the Collections tab", () =>
        switchTab(page, "collection"),
    );

    await step(
        "Check that the template's thumbnail has a dashed border",
        async () => {
            const edges = await readBookTileThumbnailEdges(
                page,
                templateFolder,
                readBookId(templateFolder),
            );
            expect(
                countDashes(edges.topRuns),
                `the top edge of the template's thumbnail is not dashed: ${describeEdges(edges)}`,
            ).toBeGreaterThanOrEqual(MIN_DASHES);
            expect(
                countDashes(edges.leftRuns),
                `the left edge of the template's thumbnail is not dashed: ${describeEdges(edges)}`,
            ).toBeGreaterThanOrEqual(MIN_DASHES);
        },
    );

    await step(
        "Check that the thumbnail of the book made from it has no border",
        async () => {
            const edges = await readBookTileThumbnailEdges(
                page,
                bookFolder,
                readBookId(bookFolder),
            );
            expect(
                countDashes(edges.topRuns) + countDashes(edges.leftRuns),
                `the thumbnail of the book made from the template has black on its top or left edge: ${describeEdges(edges)}`,
            ).toBe(0);
        },
    );
});
