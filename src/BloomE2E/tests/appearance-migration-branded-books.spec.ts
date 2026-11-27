// Real branded books from Bloom Library, made before Bloom 6.0, still look right after Bloom moves
// them to the appearance system: each gets the theme Bloom's migrations name for its custom CSS,
// keeps its margins, shows every picture and text box the old CSS showed, with no text that no
// longer fits and no picture that fails to load, its branding still shows, and it opens for
// editing without a problem report.
//
// Automates the manual test "Branding Appearance Auto-Migration" (Test Case ID 93), which asks a
// tester to open a couple of Education for Life books and a couple of books of other brandings
// (Little Zebra, PNG-WesternProvinceELearning-2020) and see that the migration does not break them.
//
// The books are fetched from Bloom Library when the test runs (helpers/bloomLibraryDownload.ts),
// as the case's tester downloads them, so the test needs Bloom Library to be reachable. Of the
// case's two Education for Life books, one has since been migrated and uploaded again, so it no
// longer tests the migration, and the other has a speech bubble, which keeps a book on the legacy
// theme on purpose (AppearanceSettings.GetThemeAndSubstituteCss). That one is still tested, for
// keeping its old CSS; two other public books made from Education for Life templates stand in for
// the migrations, picked because Bloom's migrations name them as examples.
//
// "Doesn't break" is judged against the same book under its old CSS: before opening a book, the
// test copies it with a comment added to its customBookStyles.css, which Bloom then no longer
// recognizes, so the copy stays on the legacy theme and shows the book as Bloom 5.6 laid it out.
// A theme is not meant to reproduce the old CSS to the millimetre (Education for Life's rounded
// e-book theme gives the cover picture more room, for example), so the test checks the margins
// closely and, for the rest, that nothing went missing, overflowed or failed to load.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test, type IBloomApp } from "../fixtures/bloomTest";
import {
    copyBookKeepingItsOldCss,
    countPagesOnDisk,
    findAppearanceMigration,
    kLegacyTheme,
    readAppearanceJson,
    readMaintenanceLevel,
    readPageLayouts,
    waitForBookLaidOut,
    type IAppearanceMigration,
    type IPageLayout,
} from "../helpers/appearanceMigration";
import { downloadBookFromBloomLibrary } from "../helpers/bloomLibraryDownload";
import { editBook, getContentPages } from "../helpers/bookMaking";
import {
    collectionBookPreviewOf,
    readBookId,
    selectBook,
} from "../helpers/collection";
import { setBranding } from "../helpers/collectionSettings";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "appearance-migration", languages: ["en"] },
});

/** How far a margin of a migrated page may be from where the old CSS put it, in millimetres. */
const kMarginToleranceMm = 1;

/** A Bloom Library book the test opens, and what the test expects of it. */
interface IBookCase {
    /** What the book is, for the test's title. */
    name: string;
    /** The id in the book's Bloom Library link, https://bloomlibrary.org/book/<id>. */
    bloomLibraryId: string;
    /** The branding the book was made under, which the collection is put under. */
    branding: string;
    /**
     * The migration Bloom's appearanceMigrations folder has for the book's customBookStyles.css,
     * or undefined when Bloom must not migrate it.
     */
    migration?: string;
    /** The theme the book must end up with. */
    theme: string;
}

const kBooks: IBookCase[] = [
    {
        // From the case: https://bloomlibrary.org/Little-Zebra-Books/book/TVZrgv19gy
        name: "Little Zebra's Friends",
        bloomLibraryId: "TVZrgv19gy",
        branding: "Little-Zebra",
        migration: "little-zebra-1",
        theme: "default",
    },
    {
        // "A Seed Flew", made from Education for Life's first e-book template.
        name: "an Education for Life e-book",
        bloomLibraryId: "MIuiH5oqUI",
        branding: "Education-For-Life",
        migration: "efl-ebook-1",
        theme: "edge-to-edge",
    },
    {
        // "Dad and I", made from Education for Life's rounded-border template.
        name: "an Education for Life rounded-border book",
        bloomLibraryId: "D6DobBXzEl",
        branding: "Education-For-Life",
        migration: "efl-rounded",
        theme: "rounded-border-ebook",
    },
    {
        // From the case: https://bloomlibrary.org/book/B1YGzcJTML. It has a speech bubble.
        name: "an Education for Life book with a speech bubble",
        bloomLibraryId: "B1YGzcJTML",
        branding: "Education-For-Life",
        theme: kLegacyTheme,
    },
    {
        // From the case: https://bloomlibrary.org/book/o6ey7DCHpP. It has no custom CSS.
        name: "a PNG-WesternProvinceELearning-2020 book",
        bloomLibraryId: "o6ey7DCHpP",
        branding: "PNG-WesternProvinceELearning-2020",
        theme: "default",
    },
];

/**
 * Select the book in `bookFolder` under `branding`, which brings it up to date (and so migrates
 * it), and measure every page of it in the Collections tab's preview.
 */
async function openAndMeasure(
    page: Page,
    bookFolder: string,
    branding: string,
): Promise<IPageLayout[]> {
    await switchTab(page, "collection");
    await setBranding(page, branding);
    await selectBook(page, bookFolder);
    const bookId = readBookId(bookFolder);
    const preview = collectionBookPreviewOf(page, bookId);
    await waitForBookLaidOut(preview, bookId, countPagesOnDisk(bookFolder));
    return readPageLayouts(preview);
}

/** Check that every picture on every page of `layouts` loaded. */
function expectEveryPictureLoaded(layouts: IPageLayout[], what: string): void {
    expect(
        layouts.filter((p) => p.brokenPictures.length > 0),
        `every picture of ${what} should load`,
    ).toEqual([]);
}

/** The book's appearance settings, as Bloom wrote them when it brought the book up to date. */
function readBookAppearance(bookFolder: string): Record<string, unknown> {
    const path = Path.join(bookFolder, "appearance.json");
    expect(
        fs.existsSync(path),
        `${bookFolder} should have an appearance.json once Bloom has opened it`,
    ).toBe(true);
    return readAppearanceJson(path);
}

/** Download the book into the collection Bloom has open, restarting Bloom so it sees it. */
async function downloadIntoCollection(
    bloomApp: IBloomApp,
    book: IBookCase,
    alsoBeforeStart: (folder: string) => void = () => {},
): Promise<string> {
    let folder = "";
    await bloomApp.restart(async () => {
        folder = await downloadBookFromBloomLibrary(
            book.bloomLibraryId,
            bloomApp.collectionDir,
        );
        alsoBeforeStart(folder);
    });
    return folder;
}

for (const book of kBooks.filter((b) => b.migration)) {
    test(`Bloom migrates ${book.name} to the appearance system without breaking it [Test Case ID 93]`, async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(300000);

        let oldCssCopy = "";
        let migration: IAppearanceMigration | undefined;
        const folder = await step(
            "Download the book from Bloom Library, with a copy that keeps its old CSS",
            () =>
                downloadIntoCollection(bloomApp, book, (downloaded) => {
                    expect(
                        readMaintenanceLevel(downloaded),
                        "the book on Bloom Library should not be migrated yet",
                    ).toBeLessThan(4);
                    migration = findAppearanceMigration(
                        fs.readFileSync(
                            Path.join(downloaded, "customBookStyles.css"),
                            "utf8",
                        ),
                    );
                    oldCssCopy = copyBookKeepingItsOldCss(
                        downloaded,
                        `${Path.basename(downloaded)} with old CSS`,
                        "customBookStyles.css",
                    );
                }),
        );
        if (!folder) throw new Error("The book was not downloaded.");
        expect(
            migration?.name,
            `Bloom should have a migration for the book's customBookStyles.css`,
        ).toBe(book.migration);
        const page = bloomApp.page;

        const oldLayouts = await step(
            "Open the copy that keeps its old CSS",
            async () => {
                const layouts = await openAndMeasure(
                    page,
                    oldCssCopy,
                    book.branding,
                );
                // Without this the copy would not show the book as its old CSS lays it out.
                expect(
                    readBookAppearance(oldCssCopy).cssThemeName,
                    "Bloom should keep the copy, whose CSS it does not recognize, on the legacy theme",
                ).toBe(kLegacyTheme);
                return layouts;
            },
        );
        if (!oldLayouts) throw new Error("The copy was not measured.");

        const layouts = await step("Open the book", () =>
            openAndMeasure(page, folder, book.branding),
        );
        if (!layouts) throw new Error("The book was not measured.");

        await step(
            "Check Bloom gave the book its migration's theme, settings and CSS",
            () => {
                expect(readMaintenanceLevel(folder)).toBeGreaterThanOrEqual(4);
                const appearance = readBookAppearance(folder);
                expect(appearance.cssThemeName).toBe(book.theme);
                for (const [key, value] of Object.entries(
                    migration!.settings,
                )) {
                    expect(
                        appearance[key],
                        `the book's appearance setting ${key} should be what ${book.migration} says`,
                    ).toEqual(value);
                }
                expect(
                    fs.readFileSync(
                        Path.join(folder, "customBookStyles2.css"),
                        "utf8",
                    ),
                    `the book's customBookStyles2.css should be ${book.migration}'s customBookStyles.css`,
                ).toBe(migration!.replacementCss);
            },
        );

        await step(
            "Check every page keeps its margins and shows what the old CSS showed",
            () => {
                expect(layouts.map((p) => p.id)).toEqual(
                    oldLayouts.map((p) => p.id),
                );
                layouts.forEach((p, index) => {
                    const old = oldLayouts[index];
                    const where = `page ${index + 1} (${p.label})`;
                    expect(
                        [p.width, p.height],
                        `${where} should keep its size`,
                    ).toEqual([old.width, old.height]);
                    // Both are drawn in the same preview pane, so a page drawn larger or smaller
                    // as a whole (which the millimetre numbers cannot show) changes this.
                    expect(
                        Math.abs(p.pixelWidth - old.pixelWidth),
                        `${where} should be drawn as wide as under the old CSS (${old.pixelWidth} px), not ${p.pixelWidth} px`,
                    ).toBeLessThanOrEqual(1);
                    expect(
                        p.pictures.length,
                        `${where} should show as many picture slots as under the old CSS`,
                    ).toBe(old.pictures.length);
                    expect(
                        p.shownPictures,
                        `${where} should show the pictures it showed under the old CSS`,
                    ).toEqual(old.shownPictures);
                    expect(
                        p.textGroups.length,
                        `${where} should show as many text boxes as under the old CSS`,
                    ).toBe(old.textGroups.length);
                    expect(
                        p.brandingPictures,
                        `${where} should show the branding pictures it showed under the old CSS`,
                    ).toEqual(old.brandingPictures);
                    expect(
                        !!p.marginBox,
                        `${where} should have a margin box if the old CSS gave it one`,
                    ).toBe(!!old.marginBox);
                    if (p.marginBox && old.marginBox) {
                        for (const side of [
                            "left",
                            "top",
                            "width",
                            "height",
                        ] as const) {
                            expect(
                                Math.abs(
                                    p.marginBox[side] - old.marginBox[side],
                                ),
                                `${where}'s margin box ${side} should be within ${kMarginToleranceMm} mm of the old CSS's ` +
                                    `(${old.marginBox[side]} mm), not ${p.marginBox[side]} mm`,
                            ).toBeLessThanOrEqual(kMarginToleranceMm);
                        }
                    }
                });
                expect(
                    layouts.flatMap((p) => p.brandingPictures),
                    `the book should show ${book.branding}'s branding pictures`,
                ).not.toEqual([]);
                // Text that fit its box under the old CSS must still fit. (A big cover title can
                // reach a little past its box under either; that is the old CSS's look, not the
                // migration's doing.)
                expect(
                    layouts.flatMap((p, index) =>
                        p.overflowingTexts
                            .filter(
                                (text) =>
                                    !oldLayouts[
                                        index
                                    ].overflowingTexts.includes(text),
                            )
                            .map((text) => `page ${index + 1}: "${text}"`),
                    ),
                    "no text that fits its box under the old CSS should overflow it once migrated",
                ).toEqual([]);
                expectEveryPictureLoaded(
                    oldLayouts,
                    "the copy with its old CSS",
                );
                expectEveryPictureLoaded(layouts, "the migrated book");
            },
        );

        await step("Open the book for editing", async () => {
            await editBook(page, folder);
            expect(
                (await getContentPages(page)).length,
                "the Edit tab should list the book's content pages",
            ).toBeGreaterThan(0);
        });

        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem while migrating and opening the books",
        ).toEqual([]);
    });
}

for (const book of kBooks.filter((b) => !b.migration)) {
    test(`Bloom opens ${book.name} on the ${book.theme} theme without breaking it [Test Case ID 93]`, async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(300000);

        const folder = await step("Download the book from Bloom Library", () =>
            downloadIntoCollection(bloomApp, book, (downloaded) =>
                expect(
                    readMaintenanceLevel(downloaded),
                    "the book on Bloom Library should not be migrated yet",
                ).toBeLessThan(4),
            ),
        );
        if (!folder) throw new Error("The book was not downloaded.");
        const page = bloomApp.page;

        const layouts = await step("Open the book", () =>
            openAndMeasure(page, folder, book.branding),
        );
        if (!layouts) throw new Error("The book was not measured.");

        await step("Check the book's theme, and that nothing broke", () => {
            expect(readMaintenanceLevel(folder)).toBeGreaterThanOrEqual(4);
            expect(readBookAppearance(folder).cssThemeName).toBe(book.theme);
            expect(layouts.length).toBe(countPagesOnDisk(folder));
            expectEveryPictureLoaded(layouts, "the book");
        });

        await step("Open the book for editing", async () => {
            await editBook(page, folder);
            expect(
                (await getContentPages(page)).length,
                "the Edit tab should list the book's content pages",
            ).toBeGreaterThan(0);
        });

        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem while opening the book",
        ).toEqual([]);
    });
}
