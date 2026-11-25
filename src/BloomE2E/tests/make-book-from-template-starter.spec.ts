// A template a person made with Template Starter can start new books. Right-clicking the template's
// tile on the Collections tab offers "Make a book using this source"; the book it makes is blank,
// and its Add Page dialog shows the template's pages first, each under its label and with its
// thumbnail: one that Bloom made from the page, or one the template's author supplied. Automates
// the manual test "Make Book From Template Starter".
//
// The template is made the way the Template Starter's own instructions say: a title on its About
// page, pages added from Basic Book, and, for one page, a thumbnail of the author's own put in the
// template's "template" folder. The instructions also say to give each page a label of its own, but
// the label of a template page is hidden under the bar of controls above the page, so nobody can
// click it (BL-17185). The pages keep the labels they came with from Basic Book, which the dialog
// shows the same way.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import {
    addPageFromDialog,
    getAddPageDialogGroups,
    openAddPageDialog,
    type IAddPageGroup,
} from "../helpers/addPageDialog";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromBookInCollectionWithItsMenu,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";

test.use({
    collectionSpec: { name: "template-starter-book", languages: ["en"] },
});

// The template's title, which is also its folder name and the Add Page dialog's heading for its
// pages. Template Starter names every new template "My Template", and a developer's machine can
// hold a downloaded template of that name, so the test gives its template a name of its own.
const TEMPLATE_TITLE = "TC79 Custom Template";
// The template page whose thumbnail Bloom makes from the page.
const AUTOMATIC_THUMBNAIL_LABEL = "Basic Text & Image";
// The template page whose thumbnail the author supplies.
const CUSTOM_THUMBNAIL_LABEL = "Just Text";
// The author's thumbnail is an SVG of a size no thumbnail Bloom makes or ships has, so its size in
// the dialog shows that the dialog is showing this file.
const CUSTOM_THUMBNAIL_WIDTH = 137;
const CUSTOM_THUMBNAIL_HEIGHT = 191;

/**
 * The width and height of a PNG file, from its IHDR chunk, or undefined while the file is missing
 * or too short to hold them, as it is while Bloom is still writing it.
 */
function pngSize(file: string): { width: number; height: number } | undefined {
    if (!fs.existsSync(file)) return undefined;
    const header = fs.readFileSync(file).subarray(0, 24);
    if (header.length < 24) return undefined;
    if (header.subarray(1, 4).toString("latin1") !== "PNG")
        throw new Error(`${file} is not a PNG file`);
    return { width: header.readUInt32BE(16), height: header.readUInt32BE(20) };
}

/** One page of a book's HTML file on disk. */
interface IPageOnDisk {
    id: string;
    label: string;
    /** The ids of the pages this page was made from, oldest first (data-pagelineage). */
    lineage: string[];
}

/**
 * The pages in the HTML file of the book in `bookFolder`, read from disk. Parsed in the browser,
 * because Node has no HTML parser.
 */
async function readPagesOnDisk(
    page: Page,
    bookFolder: string,
): Promise<IPageOnDisk[]> {
    const htmlFiles = fs
        .readdirSync(bookFolder)
        .filter((name) => /\.html?$/i.test(name));
    if (htmlFiles.length !== 1)
        throw new Error(
            `Expected one HTML file in ${bookFolder}, found: ${htmlFiles.join(", ") || "(none)"}.`,
        );
    const html = fs.readFileSync(Path.join(bookFolder, htmlFiles[0]), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        return Array.from(dom.querySelectorAll(".bloom-page[id]")).map((p) => ({
            id: p.id,
            label: p.querySelector(".pageLabel")?.textContent?.trim() ?? "",
            lineage: (p.getAttribute("data-pagelineage") ?? "")
                .split(/[;,]/)
                .filter((id) => id),
        }));
    }, html);
}

/** Describe the dialog's groups for a failure message. */
function describeGroups(groups: IAddPageGroup[]): string {
    return groups
        .map(
            (g) =>
                `${g.title} (${g.templateBookFolder}): ` +
                g.pages.map((p) => p.label).join(", "),
        )
        .join("; ");
}

test("a book made from a Template Starter template is blank and offers the template's pages first [Test Case ID 79]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);

    const templateFolder = await step(
        "Make a template with Template Starter and give it a title",
        async () => {
            await makeBookFromTemplate(page, "Template Starter");
            // The template's title is the "Template Title" box of its "About" page. Leaving that
            // page saves it, and Bloom then renames the folder to match; wait for that before
            // adding pages, so the rename does not land in the middle of an add.
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

    await step("Add two pages to the template", async () => {
        await addPage(page, AUTOMATIC_THUMBNAIL_LABEL, 1, "Basic Book");
        await addPage(page, CUSTOM_THUMBNAIL_LABEL, 1, "Basic Book");
        expect(
            (await getContentPages(page)).length,
            "the template did not end up with the two pages added to it",
        ).toBe(2);
        // Leave the last page added, so that it is written before the template is used.
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        expect(cover, "the template has no front matter page").toBeDefined();
        await goToPage(page, cover!.id);
    });

    await step("Give one page a thumbnail of the author's own", async () => {
        const thumbnailFolder = Path.join(templateFolder, "template");
        fs.mkdirSync(thumbnailFolder, { recursive: true });
        fs.writeFileSync(
            Path.join(thumbnailFolder, `${CUSTOM_THUMBNAIL_LABEL}.svg`),
            `<svg xmlns="http://www.w3.org/2000/svg" width="${CUSTOM_THUMBNAIL_WIDTH}" ` +
                `height="${CUSTOM_THUMBNAIL_HEIGHT}" viewBox="0 0 ${CUSTOM_THUMBNAIL_WIDTH} ` +
                `${CUSTOM_THUMBNAIL_HEIGHT}"><rect width="100%" height="100%" fill="#2a7"/></svg>`,
        );
    });

    const bookFolder = await step(
        "Right-click the template and choose Make a book using this source",
        () => makeBookFromBookInCollectionWithItsMenu(page, templateFolder),
    );
    if (!bookFolder) throw new Error("No book was made from the template.");

    await step("Check that the new book is blank", async () => {
        expect(
            Path.normalize(bookFolder),
            "Bloom did not make a new book; it is showing the template",
        ).not.toBe(Path.normalize(templateFolder));
        expect(
            (await getContentPages(page)).map((p) => p.caption),
            "the book made from the template is not blank: it has content pages",
        ).toEqual([]);
    });

    await step(
        "Open Add Page and check the template's pages come first, labelled and with thumbnails",
        async () => {
            await openAddPageDialog(page);
            const groups = await getAddPageDialogGroups(page);
            const first = groups[0];
            expect(
                Path.normalize(first.templateBookFolder),
                `the first group in Add Page is not the template. Groups: ${describeGroups(groups)}`,
            ).toBe(Path.normalize(templateFolder));
            expect(first.title, "the template's group heading").toBe(
                TEMPLATE_TITLE,
            );
            expect(
                first.pages.map((p) => p.label),
                "the template's pages, under their labels",
            ).toEqual([AUTOMATIC_THUMBNAIL_LABEL, CUSTOM_THUMBNAIL_LABEL]);

            const custom = first.pages[1];
            expect(
                {
                    width: custom.naturalWidth,
                    height: custom.naturalHeight,
                },
                "the page with the author's own thumbnail is not showing that thumbnail",
            ).toEqual({
                width: CUSTOM_THUMBNAIL_WIDTH,
                height: CUSTOM_THUMBNAIL_HEIGHT,
            });

            // Bloom makes the other page's thumbnail when the dialog first asks for it, saves it in
            // the template's "template" folder, and then has the dialog load it in place of the
            // placeholder it showed meanwhile. So wait until the dialog is showing an image of the
            // saved file's size, then check that the image is that file, byte for byte: every
            // portrait thumbnail Bloom makes has the same size.
            const generated = Path.join(
                templateFolder,
                "template",
                `${AUTOMATIC_THUMBNAIL_LABEL}.png`,
            );
            await expect
                .poll(
                    async () => {
                        const expected = pngSize(generated);
                        if (!expected)
                            return `Bloom has not saved ${generated}`;
                        const shown = (await getAddPageDialogGroups(page))[0]
                            .pages[0];
                        if (
                            shown.naturalWidth !== expected.width ||
                            shown.naturalHeight !== expected.height
                        )
                            return (
                                `the dialog shows a ${shown.naturalWidth}x${shown.naturalHeight} ` +
                                `image, not Bloom's ${expected.width}x${expected.height} thumbnail`
                            );
                        const served = await page.evaluate(async (url) => {
                            const bytes = new Uint8Array(
                                await (await fetch(url)).arrayBuffer(),
                            );
                            let text = "";
                            for (let i = 0; i < bytes.length; i += 0x8000)
                                text += String.fromCharCode(
                                    ...bytes.subarray(i, i + 0x8000),
                                );
                            return btoa(text);
                        }, shown.imageUrl);
                        return served ===
                            fs.readFileSync(generated).toString("base64")
                            ? ""
                            : `the dialog's image at ${shown.imageUrl} is not the file ${generated}`;
                    },
                    {
                        timeout: 60000,
                        message:
                            "Add Page never showed the thumbnail Bloom made for the template page",
                    },
                )
                .toBe("");
        },
    );

    await step("Add a page from the template", async () => {
        await addPageFromDialog(
            page,
            AUTOMATIC_THUMBNAIL_LABEL,
            TEMPLATE_TITLE,
        );
        expect(
            (await getContentPages(page)).length,
            "the page added from the template did not reach the book",
        ).toBe(1);
        // Basic Book has a page of the same label, so check where the page came from: a page
        // Bloom adds records, last in its lineage, the id of the template page it was made from.
        const templatePage = (await readPagesOnDisk(page, templateFolder)).find(
            (p) => p.label === AUTOMATIC_THUMBNAIL_LABEL,
        );
        expect(
            templatePage,
            `the template's file has no "${AUTOMATIC_THUMBNAIL_LABEL}" page`,
        ).toBeDefined();
        await expect
            .poll(
                async () =>
                    (await readPagesOnDisk(page, bookFolder))
                        .filter(
                            (p) =>
                                p.lineage[p.lineage.length - 1] ===
                                templatePage!.id,
                        )
                        .map((p) => p.label),
                {
                    timeout: 30000,
                    message:
                        "the book's file never held a page made from the template's page",
                },
            )
            .toEqual([AUTOMATIC_THUMBNAIL_LABEL]);
    });
});
