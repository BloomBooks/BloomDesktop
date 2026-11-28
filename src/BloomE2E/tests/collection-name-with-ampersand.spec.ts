// A collection whose name and place names hold an ampersand: "&" is special in XML, in HTML and in
// WinForms labels, and Bloom has choked on it before (BL-9732: a location with "&" left the books
// of a collection unopenable). The name and places must survive into the collection's settings
// file, the books made in it, and what Bloom publishes from them.
//
// Automates the manual test "Collection Name With Ampersand" (Test Case ID 115). The case creates
// the collection with these names in the New Collection wizard, a WinForms dialog that the suite
// cannot open (AUTOMATION-DEBT.md, "WinForms surfaces are invisible to CDP"). Collection Settings
// sets the same three places and the collection's name, so this test gives them there and checks
// the rest of the case from that point on. "Publish" is checked as PDF, ePUB and BloomPUB; an
// upload to BloomLibrary.org is not made.

import type { Page } from "@playwright/test";
import * as fs from "fs";
import * as Path from "path";
import { expect, test } from "../fixtures/bloomTest";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    editablePageFrame,
    editBook,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    makeAllBloomPubs,
    openMakeAllBloomPubsDialog,
    readZip,
    setMakeAllBloomPubsOption,
} from "../helpers/bulkBloomPub";
import { apiGet } from "../helpers/api";
import {
    cancelCollectionSettings,
    describeCollectionFolder,
    getCollectionSettingsText,
    kEnterpriseSubscriptionCode,
    kTestBookshelves,
    openCollectionSettings,
    readSavedCollectionSetting,
    restartFromCollectionSettings,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    getEpubPackageSpineHrefs,
    getEpubXhtmlText,
    openEpubPublishScreen,
    readEpubPreviewContentFile,
    readEpubPreviewPackage,
    showEpubPreview,
} from "../helpers/epubPublish";
import {
    choosePdfMode,
    getPdfPreviewPageCount,
    getPdfPreviewPageDrawing,
} from "../helpers/pdfPrint";
import { goToTitlePage } from "../helpers/titlePage";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

const kCollectionName = "Rivers & Hills Books";
const kPlaces = {
    Country: "Trinidad & Tobago",
    Province: "Rivers & Lakes",
    District: "Hills & Valleys",
};
// The title page lists the places from the smallest out, with Bloom's English list separator.
const kLocation = `${kPlaces.District}, ${kPlaces.Province}, ${kPlaces.Country}`;

test.use({
    collectionSpec: {
        name: "ampersand-collection",
        languages: ["en"],
        // Publishing an ePUB needs Pro, and Make All BloomPUBs needs Enterprise.
        subscriptionCode: kEnterpriseSubscriptionCode,
        // With no bookshelf, Make All BloomPUBs fails before it makes anything (BL-17197).
        bookshelf: kTestBookshelves[0],
    },
});

test("a collection named with an ampersand, in a place named with ampersands, saves, reopens, makes books and publishes them [Test Case ID 115]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(480000);

    // The helpers find the Settings dialog's pages and fields by their English names. Switching
    // the UI language can replace the shell page, so the page is read from bloomApp after it.
    await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    await step(
        `In Collection Settings, name the collection "${kCollectionName}" and give it a country, province and district with ampersands`,
        async () => {
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Advanced");
            await setCollectionSettingsText(
                page,
                "Collection Name",
                kCollectionName,
            );
            await showCollectionSettingsPage(page, "Front & Back Matter");
            for (const [field, value] of Object.entries(kPlaces))
                await setCollectionSettingsText(page, field, value);
            await expect
                .poll(() => getCollectionSettingsText(page, "Country"))
                .toEqual({ value: kPlaces.Country, enabled: true });
        },
    );

    // A step returns undefined only when it is marked fixme, which these are not.
    let shell = (await step(
        "Click Restart, so Bloom saves the settings, renames the collection and reopens it",
        () => restartFromCollectionSettings(bloomApp, kCollectionName),
    ))!;

    await step(
        "Check the collection's folder and settings file carry the name, the file keeps the places, and Bloom shows them",
        async () => {
            expect(describeCollectionFolder(bloomApp.collectionDir)).toEqual({
                name: kCollectionName,
                settingsFiles: [`${kCollectionName}.bloomCollection`],
            });
            // The settings file is XML, so each "&" must be written as an entity there.
            for (const [element, value] of Object.entries(kPlaces))
                expect(
                    readSavedCollectionSetting(bloomApp.collectionDir, element),
                    `the .bloomCollection's <${element}>`,
                ).toBe(value.replace(/&/g, "&amp;"));
            expect(
                (
                    await apiGet(
                        shell,
                        "collections/getCurrentEditableCollectionName",
                    )
                ).body,
            ).toBe(kCollectionName);
            await openCollectionSettings(shell);
            await showCollectionSettingsPage(shell, "Advanced");
            expect(
                (await getCollectionSettingsText(shell, "Collection Name"))
                    .value,
            ).toBe(kCollectionName);
            await showCollectionSettingsPage(shell, "Front & Back Matter");
            for (const [field, value] of Object.entries(kPlaces))
                expect(
                    (await getCollectionSettingsText(shell, field)).value,
                    `the ${field} field`,
                ).toBe(value);
            await cancelCollectionSettings(shell);
        },
    );

    const bookFolders = (await step(
        "Make two books, and check each title page shows the places",
        async () => {
            const folders: string[] = [];
            for (let i = 0; i < 2; i++) {
                // The books get no title, so each keeps the folder it was made with.
                folders.push(await makeBookFromTemplate(shell, "Basic Book"));
                await goToTitlePage(shell);
                expect(
                    await readShownLocation(shell),
                    `the title page of book ${i + 1} in the Edit tab`,
                ).toBe(kLocation);
            }
            expect(new Set(folders).size).toBe(2);
            return folders;
        },
    ))!;

    shell = (await step(
        "Quit Bloom and start it again, and check each book still opens and keeps the places",
        async () => {
            // BL-9732: a location with "&" left Bloom unable to read the books it had saved.
            const reopened = await bloomApp.quitAndRestart();
            for (const [i, folder] of bookFolders.entries()) {
                expect(
                    readLocationInHtml(
                        fs.readFileSync(bookHtmlPath(folder), "utf8"),
                    ),
                    `book ${i + 1}'s HTML on disk`,
                ).toBe(kLocation);
                await editBook(reopened, folder);
                await goToTitlePage(reopened);
                expect(
                    await readShownLocation(reopened),
                    `the title page of book ${i + 1} after the restart`,
                ).toBe(kLocation);
            }
            return reopened;
        },
    ))!;

    await step(
        "Publish the second book as a PDF, and check its title page prints the places",
        async () => {
            await switchTab(shell, "publish");
            await choosePdfMode(shell, "Simple");
            const pageTexts: string[] = [];
            const count = await getPdfPreviewPageCount(shell);
            for (let i = 0; i < count; i++)
                pageTexts.push(
                    linesOf(
                        (await getPdfPreviewPageDrawing(shell, i)).texts,
                    ).join("\n"),
                );
            expect(
                pageTexts.filter((text) =>
                    text
                        .split("\n")
                        .map(withoutSpaces)
                        .includes(withoutSpaces(kLocation)),
                ),
                `the PDF's pages:\n${pageTexts.join("\n----\n")}`,
            ).toHaveLength(1);
            expect(bloomApp.takeNonFatalProblems()).toEqual([]);
        },
    );

    await step(
        "Publish it as an ePUB, and check its title page shows the places",
        async () => {
            // The preview stages the ePUB's files, and Save... zips that staging folder as it is
            // (EpubMaker.ZipAndSaveEpub); Save itself opens the system's save dialog.
            await openEpubPublishScreen(shell);
            await showEpubPreview(shell);
            const packageText = await readEpubPreviewPackage(shell);
            const pageTexts: string[] = [];
            for (const href of await getEpubPackageSpineHrefs(
                shell,
                packageText,
            ))
                pageTexts.push(
                    await getEpubXhtmlText(
                        shell,
                        await readEpubPreviewContentFile(shell, href),
                    ),
                );
            expect(
                pageTexts.filter((text) => text.includes(kLocation)),
                `the ePUB's pages:\n${pageTexts.join("\n----\n")}`,
            ).toHaveLength(1);
            expect(bloomApp.takeNonFatalProblems()).toEqual([]);
        },
    );

    await step(
        "Make BloomPUBs of both books, and check each one's title page has the places",
        async () => {
            await openMakeAllBloomPubsDialog(shell);
            await setMakeAllBloomPubsOption(shell, "bookshelfFile", false);
            await setMakeAllBloomPubsOption(shell, "bloomBundle", false);
            const result = await makeAllBloomPubs(shell);
            for (const folder of bookFolders) {
                const name = `${Path.basename(folder)}.bloompub`;
                const bloomPub = result.files.get(name);
                if (!bloomPub)
                    throw new Error(
                        `Make All BloomPUBs made no ${name}. It made: ` +
                            `${[...result.files.keys()].join(", ")}. The progress box said:\n${result.log}`,
                    );
                const htmlEntries = readZip(bloomPub).filter((entry) =>
                    entry.name.endsWith(".htm"),
                );
                expect(
                    htmlEntries.map((entry) => entry.name),
                    `${name} should hold one .htm`,
                ).toHaveLength(1);
                expect(
                    readLocationInHtml(htmlEntries[0].content.toString("utf8")),
                    `the title page in ${name}`,
                ).toBe(kLocation);
            }
            expect(bloomApp.takeNonFatalProblems()).toEqual([]);
        },
    );
});

/** The places that the title page the Edit tab is showing lists, as it shows them. */
async function readShownLocation(page: Page): Promise<string> {
    const location = editablePageFrame(page).locator(
        ".bloom-page [data-library='languageLocation']",
    );
    await expect(location).toHaveCount(1);
    return (await location.innerText()).trim();
}

/**
 * The places that the title page of a book's HTML (the book's own, or a published copy of it)
 * lists, with entities decoded. Throws unless there is exactly one title-page location.
 */
function readLocationInHtml(html: string): string {
    const matches = [
        ...html.matchAll(
            /<div[^>]*data-library="languageLocation"[^>]*>([\s\S]*?)<\/div>/g,
        ),
    ].filter((match) => /class="[^"]*\blangName\b/.test(match[0]));
    if (matches.length !== 1)
        throw new Error(
            `Expected one title-page location in the HTML, found ${matches.length}.`,
        );
    return decodeEntities(matches[0][1].replace(/<[^>]*>/g, "")).trim();
}

function decodeEntities(text: string): string {
    return text
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, '"')
        .replace(/&#39;/g, "'")
        .replace(/&amp;/g, "&");
}

/**
 * A line of PDF text with its spaces taken out: Chromium's PDF draws many fonts one glyph per
 * run and leaves spaces as gaps, so the words of a line can be compared only without them.
 */
function withoutSpaces(text: string): string {
    return text.replace(/\s+/g, "");
}

/**
 * The lines of text a PDF page draws, top to bottom: runs that share a baseline (within half a
 * millimetre) are joined left to right.
 */
function linesOf(runs: { text: string; x: number; y: number }[]): string[] {
    const lines: { y: number; runs: { text: string; x: number }[] }[] = [];
    for (const run of runs) {
        const line = lines.find((l) => Math.abs(l.y - run.y) < 0.5);
        if (line) line.runs.push(run);
        else lines.push({ y: run.y, runs: [run] });
    }
    return lines
        .sort((a, b) => b.y - a.y)
        .map((line) =>
            line.runs
                .sort((a, b) => a.x - b.x)
                .map((r) => r.text)
                .join(" ")
                .trim(),
        );
}
