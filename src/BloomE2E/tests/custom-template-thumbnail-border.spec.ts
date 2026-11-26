// A book template that a person made shows on the Collections tab with a dashed black border
// around its thumbnail, so that it stands out from the books made from it. Bloom draws the border
// into the template's thumbnail.png when it makes the thumbnail (BookThumbNailer, for a book whose
// meta.json says it is suitable for making shells). Automates the manual test "Custom Templates
// Dashed Thumbnail Border".
//
// The template is made with Template Starter, as people make them, and a book is made from it.
// Both have the same cover (the picture placeholder), so the only thing that should tell their
// thumbnails apart is the border: the template's thumbnail has dashes along its top and left
// edges, and the book's has no black there at all, while inside the border the two are the same
// picture.

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
    countThumbnailPixelsDifferingInside,
    readBookTileThumbnail,
    type ITileThumbnail,
} from "../helpers/collection";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "custom-template-thumbnail", languages: ["en"] },
});

// Template Starter names every new template "My Template", and a developer's machine can hold a
// downloaded template of that name, so the test gives its template a name of its own.
const TEMPLATE_TITLE = "TC76 Custom Template";
// A dashed edge has at least this many separate dashes; Bloom's dash pattern along a thumbnail
// edge of any size Bloom makes has many more.
const MIN_DASHES = 3;
// Bloom's 2-pixel pen draws the border along the thumbnail's edges, so the two thumbnails are
// compared only inside a band this wide.
const BORDER_BAND = 2;

function describeEdges(edges: ITileThumbnail): string {
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

    const templateThumbnail = await step(
        "Check that the template's thumbnail has a dashed border",
        async () => {
            const thumbnail = await readBookTileThumbnail(
                page,
                templateFolder,
                readBookId(templateFolder),
            );
            expect(
                countDashes(thumbnail.topRuns),
                `the top edge of the template's thumbnail is not dashed: ${describeEdges(thumbnail)}`,
            ).toBeGreaterThanOrEqual(MIN_DASHES);
            expect(
                countDashes(thumbnail.leftRuns),
                `the left edge of the template's thumbnail is not dashed: ${describeEdges(thumbnail)}`,
            ).toBeGreaterThanOrEqual(MIN_DASHES);
            return thumbnail;
        },
    );
    if (!templateThumbnail)
        throw new Error("The template's thumbnail was not read.");

    await step(
        "Check that the book made from it has the same thumbnail with no border",
        async () => {
            const thumbnail = await readBookTileThumbnail(
                page,
                bookFolder,
                readBookId(bookFolder),
            );
            // The same picture inside the border shows that this is a thumbnail of the book's
            // cover, not an icon Bloom put in its place (its error icon has no border either).
            expect(
                `${thumbnail.width}x${thumbnail.height}`,
                "the book's thumbnail is not the size of the template's",
            ).toBe(`${templateThumbnail.width}x${templateThumbnail.height}`);
            expect(
                countThumbnailPixelsDifferingInside(
                    thumbnail,
                    templateThumbnail,
                    BORDER_BAND,
                ),
                "pixels inside the border band that differ between the book's thumbnail and the template's",
            ).toBe(0);
            expect(
                countDashes(thumbnail.topRuns) +
                    countDashes(thumbnail.leftRuns),
                `the thumbnail of the book made from the template has black on its top or left edge: ${describeEdges(thumbnail)}`,
            ).toBe(0);
        },
    );
});
