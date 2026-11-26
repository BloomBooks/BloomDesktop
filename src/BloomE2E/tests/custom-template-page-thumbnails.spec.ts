// A template's author can replace the page thumbnails that Bloom made for the template's pages with
// pictures of their own: an SVG or a PNG of the same name in the template's "template" folder. A
// book made from the template then shows the author's pictures in its Add Page dialog. Automates
// the manual test "Custom Template Page Thumbnail".
//
// The manual test first has the tester make the template and leave it, so that Bloom has made a
// PNG thumbnail for each of its pages, and then replace those PNG files. Bloom makes them when the
// Add Page dialog first asks for them, so the test opens the dialog in the template to get them.
// One page's PNG is replaced by an SVG; the other page's PNG is overwritten by a PNG of the
// author's own (the manual test's "also test with png"). Bloom prefers an SVG to a PNG, and uses a
// template's PNG as it is in any book but the template itself (FindOrGenerateThumbnail in
// PageTemplatesApi.cs).

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    closeAddPageDialog,
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
import { makeBlackSquareOnWhitePng } from "../helpers/paintedPixels";
import { readPngSize } from "../helpers/screenshot";

test.use({
    collectionSpec: {
        name: "custom-template-page-thumbnails",
        languages: ["en"],
    },
});

// The template's title, which is also its folder name and the Add Page dialog's heading for its
// pages. A developer's machine can hold a downloaded template called "My Template", Template
// Starter's own name for a new template, so the test gives its template a name of its own.
const TEMPLATE_TITLE = "TC78 Custom Template";
// The template page whose thumbnail the author replaces with an SVG.
const SVG_THUMBNAIL_LABEL = "Basic Text & Image";
// The template page whose thumbnail the author replaces with a PNG of their own.
const PNG_THUMBNAIL_LABEL = "Just Text";
// Each of the author's pictures has a size that no thumbnail Bloom makes or ships has, so the size
// of what the dialog shows tells the author's picture from Bloom's.
const SVG_THUMBNAIL_WIDTH = 137;
const SVG_THUMBNAIL_HEIGHT = 191;
const PNG_THUMBNAIL_SIZE = 149;

/**
 * The width and height of a PNG file, or undefined while the file is missing or too short to hold
 * them, as it is while Bloom is still writing it.
 */
function pngFileSize(
    file: string,
): { width: number; height: number } | undefined {
    if (!fs.existsSync(file)) return undefined;
    const bytes = fs.readFileSync(file);
    if (bytes.length < 24) return undefined;
    return readPngSize(bytes);
}

/** The bytes the browser gets from `url`, as base64. */
async function fetchAsBase64(page: Page, url: string): Promise<string> {
    return page.evaluate(async (url) => {
        const bytes = new Uint8Array(await (await fetch(url)).arrayBuffer());
        let text = "";
        for (let i = 0; i < bytes.length; i += 0x8000)
            text += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
        return btoa(text);
    }, url);
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

/**
 * The Add Page dialog's group for the template, which must be its first, with the template's two
 * pages in it under their labels.
 */
async function getTemplateGroup(
    page: Page,
    templateFolder: string,
): Promise<IAddPageGroup> {
    const groups = await getAddPageDialogGroups(page);
    const first = groups[0];
    expect(
        Path.normalize(first.templateBookFolder),
        `the first group in Add Page is not the template. Groups: ${describeGroups(groups)}`,
    ).toBe(Path.normalize(templateFolder));
    expect(
        first.pages.map((p) => p.label),
        "the template's pages, under their labels",
    ).toEqual([SVG_THUMBNAIL_LABEL, PNG_THUMBNAIL_LABEL]);
    return first;
}

test("a book made from a custom template shows the author's own SVG and PNG page thumbnails in Add Page [Test Case ID 78]", async ({
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
    const thumbnailFolder = Path.join(templateFolder, "template");
    const svgPageThumbnail = Path.join(
        thumbnailFolder,
        `${SVG_THUMBNAIL_LABEL}.png`,
    );
    const pngPageThumbnail = Path.join(
        thumbnailFolder,
        `${PNG_THUMBNAIL_LABEL}.png`,
    );

    await step("Add two pages to the template", async () => {
        await addPage(page, SVG_THUMBNAIL_LABEL, 1, "Basic Book");
        await addPage(page, PNG_THUMBNAIL_LABEL, 1, "Basic Book");
        expect(
            (await getContentPages(page)).length,
            "the template did not end up with the two pages added to it",
        ).toBe(2);
        // Leave the last page added, so that it is written before Bloom makes its thumbnail.
        const cover = (await getPages(page)).find((p) => !p.isContentPage);
        expect(cover, "the template has no front matter page").toBeDefined();
        await goToPage(page, cover!.id);
    });

    await step(
        "Open Add Page in the template, so that Bloom makes a PNG thumbnail of each of its pages",
        async () => {
            expect(
                [svgPageThumbnail, pngPageThumbnail].filter((f) =>
                    fs.existsSync(f),
                ),
                "the template has page thumbnails before Bloom was asked for any",
            ).toEqual([]);
            await openAddPageDialog(page);
            // Bloom saves each thumbnail it makes in the template's "template" folder, then has the
            // dialog load it in place of the placeholder it showed meanwhile. Wait until the dialog
            // shows both saved files, so that Bloom has finished with them before they are replaced.
            await expect
                .poll(
                    async () => {
                        const shown = (
                            await getTemplateGroup(page, templateFolder)
                        ).pages;
                        const problems: string[] = [];
                        [svgPageThumbnail, pngPageThumbnail].forEach(
                            (file, i) => {
                                const saved = pngFileSize(file);
                                if (!saved)
                                    problems.push(
                                        `Bloom has not saved ${file}`,
                                    );
                                else if (
                                    shown[i].naturalWidth !== saved.width ||
                                    shown[i].naturalHeight !== saved.height
                                )
                                    problems.push(
                                        `the dialog shows a ${shown[i].naturalWidth}x${shown[i].naturalHeight} ` +
                                            `image for "${shown[i].label}", not Bloom's ` +
                                            `${saved.width}x${saved.height} thumbnail`,
                                    );
                            },
                        );
                        return problems.join("; ");
                    },
                    {
                        timeout: 120000,
                        message:
                            "Bloom never made and showed thumbnails of the template's pages",
                    },
                )
                .toBe("");
            // Sanity check: the author's pictures must not have the size of Bloom's own thumbnails,
            // or their size in the dialog would not tell them apart.
            for (const file of [svgPageThumbnail, pngPageThumbnail]) {
                const saved = pngFileSize(file)!;
                expect(
                    [
                        `${SVG_THUMBNAIL_WIDTH}x${SVG_THUMBNAIL_HEIGHT}`,
                        `${PNG_THUMBNAIL_SIZE}x${PNG_THUMBNAIL_SIZE}`,
                    ],
                    `Bloom's thumbnail ${file} has the size of one of the author's pictures`,
                ).not.toContain(`${saved.width}x${saved.height}`);
            }
            await closeAddPageDialog(page);
        },
    );

    const svgFile = Path.join(thumbnailFolder, `${SVG_THUMBNAIL_LABEL}.svg`);
    await step(
        "Replace one page's PNG thumbnail with an SVG of the same name, and the other's with a PNG of the author's own",
        async () => {
            fs.rmSync(svgPageThumbnail);
            fs.writeFileSync(
                svgFile,
                `<svg xmlns="http://www.w3.org/2000/svg" width="${SVG_THUMBNAIL_WIDTH}" ` +
                    `height="${SVG_THUMBNAIL_HEIGHT}" viewBox="0 0 ${SVG_THUMBNAIL_WIDTH} ` +
                    `${SVG_THUMBNAIL_HEIGHT}"><rect width="100%" height="100%" fill="#2a7"/></svg>`,
            );
            await makeBlackSquareOnWhitePng(
                page,
                pngPageThumbnail,
                PNG_THUMBNAIL_SIZE,
            );
            expect(
                pngFileSize(pngPageThumbnail),
                "the author's PNG did not replace Bloom's thumbnail",
            ).toEqual({
                width: PNG_THUMBNAIL_SIZE,
                height: PNG_THUMBNAIL_SIZE,
            });
        },
    );

    const bookFolder = await step(
        "Leave the template and make a new book from it",
        () => makeBookFromBookInCollectionWithItsMenu(page, templateFolder),
    );
    if (!bookFolder) throw new Error("No book was made from the template.");

    await step(
        "Open Add Page and check the template's pages show the author's SVG and PNG",
        async () => {
            expect(
                Path.normalize(bookFolder),
                "Bloom did not make a new book; it is showing the template",
            ).not.toBe(Path.normalize(templateFolder));
            await openAddPageDialog(page);
            const [svgPage, pngPage] = (
                await getTemplateGroup(page, templateFolder)
            ).pages;

            expect(
                { width: svgPage.naturalWidth, height: svgPage.naturalHeight },
                `"${SVG_THUMBNAIL_LABEL}" is not showing the author's SVG`,
            ).toEqual({
                width: SVG_THUMBNAIL_WIDTH,
                height: SVG_THUMBNAIL_HEIGHT,
            });
            expect(
                await fetchAsBase64(page, svgPage.imageUrl),
                `the image of "${SVG_THUMBNAIL_LABEL}" at ${svgPage.imageUrl} is not the file ${svgFile}`,
            ).toBe(fs.readFileSync(svgFile).toString("base64"));

            expect(
                { width: pngPage.naturalWidth, height: pngPage.naturalHeight },
                `"${PNG_THUMBNAIL_LABEL}" is not showing the author's PNG`,
            ).toEqual({
                width: PNG_THUMBNAIL_SIZE,
                height: PNG_THUMBNAIL_SIZE,
            });
            expect(
                await fetchAsBase64(page, pngPage.imageUrl),
                `the image of "${PNG_THUMBNAIL_LABEL}" at ${pngPage.imageUrl} is not the file ${pngPageThumbnail}`,
            ).toBe(fs.readFileSync(pngPageThumbnail).toString("base64"));
            // Bloom must have left the author's pictures as they were, not made thumbnails over them.
            expect(
                fs.existsSync(svgPageThumbnail),
                `Bloom made a PNG thumbnail again beside the author's SVG: ${svgPageThumbnail}`,
            ).toBe(false);
            expect(
                pngFileSize(pngPageThumbnail),
                "Bloom replaced the author's PNG",
            ).toEqual({
                width: PNG_THUMBNAIL_SIZE,
                height: PNG_THUMBNAIL_SIZE,
            });
            await closeAddPageDialog(page);
        },
    );
});
