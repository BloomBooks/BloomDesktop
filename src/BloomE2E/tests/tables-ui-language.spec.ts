// A table's own UI in the user's UI language: the round menu buttons and "+" buttons with their
// tooltips, the Row, Column, Table and Cell menus, and the Paint format badge. All of it is drawn
// by the bloom-table library, which asks Bloom for its wording when the first table on a page is
// attached and shows nothing of itself until the answer has come (BL-17108).
//
// Most of this runs in Pseudo-English, the UI language Bloom offers on developer builds, in which
// every string that goes through localization comes back in square brackets. So the tests need no
// translation to exist: wording with no brackets round it never asked Bloom for a translation, and
// that is the failure they look for. One test runs in French instead, where a string with no
// translation yet must fall back to its English rather than to nothing or to its id.
//
// Two tests hold back one of the two answers a table waits for as a page opens, so that they can
// look at the table in the moment before it arrives; see holdBackTableStartupAnswer.
//
// Two things about the setup are load-bearing:
//
//  - The book and its pages are made in English, before the UI language changes, because the
//    helpers that make them find templates by their English names.
//  - The tests are serial and share one book and one Bloom, whose UI language each test inherits
//    from the one before. So the first failure is the one to read.
//
// These tests have no "[Test Case ID N]" tag because no row in the Notion test inventory covers
// the UI language of tables yet. See README.md for the convention.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    dragPaletteItemOntoCanvas,
    getPaletteItemTooltip,
} from "../helpers/canvasElements";
import { selectBook } from "../helpers/collection";
import {
    getFeatureStatus,
    kEnterpriseSubscriptionCode,
    restartWithCollectionSettings,
} from "../helpers/collectionSettings";
import { addTableInChangeLayout } from "../helpers/origami";
import {
    getPublishingBlockedNoticeText,
    isPublishingBlockedNoticeShowing,
    openPublishTab,
} from "../helpers/publish";
import {
    chooseBorderScope,
    clickTableMenuControl,
    closeAnyMenu,
    countTableButtonsAfterMovingOntoTable,
    endPaintFormat,
    expectTableButtonsShowing,
    getBorderScopesOffered,
    getTableCount,
    getTableUiTexts,
    holdBackTableStartupAnswer,
    openTableMenu,
    rightClickCell,
    setCellContentType,
    showTableButtons,
    startPaintFormat,
    waitForTableAttached,
    type ITableUiText,
} from "../helpers/tables";
import {
    choosePseudoEnglishUiLanguage,
    chooseUiLanguage,
    getCurrentUiLanguageTag,
    getUiStrings,
    isPseudoEnglish,
    kPseudoEnglishUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "tables-ui-language",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test.describe.configure({ mode: "serial" });

// The strings the tests have to find a control by, or expect to read, each by its id in Bloom's
// localization files with its English. getUiStrings turns these into the wording of whichever UI
// language Bloom is in, so no test matches on English.
const kStringsById = {
    "EditTab.Table.Size.Fixed": "Fixed size",
    "EditTab.Table.Units.Millimetres": "{0}mm",
    "EditTab.Table.PaintFormat.Command": "Paint format",
    "EditTab.Table.PaintFormat.End": "End format painting",
    "EditTab.Table.PaintFormat.EndWithShortcut": "End format painting (Esc)",
    "EditTab.Table.Format.Padding.Text": "Padding between border and text",
    "EditTab.Table.Format.Padding.Image": "Padding between border and image",
    "Feature.Table": "Table",
} as const;

// A book whose only subscription feature is a table, for the Publish tab's notice to name. (A
// table on a Canvas page would have the notice name Canvas, which comes first.)
let tableOnlyBookFolder: string;
// The book the other tests work in, and its pages: the cover, a Canvas page that gets a table
// from the Canvas tool, and a page that gets one through Change Layout. All set by the first test.
let bookFolder: string;
let coverPage: IBookPage;
let canvasPage: IBookPage;
let layoutPage: IBookPage;

/** The wording of the table UI that is showing, as a list a failure message can print. */
function describeTexts(texts: ITableUiText[]): string[] {
    return texts.map((t) => `"${t.text}" (${t.where})`);
}

/**
 * Read the table UI that is showing and assert that every word of it went through Bloom's
 * localization, and that no control is nameless. `what` says which part of the UI is showing.
 * Returns what it read, for the assertions particular to one test.
 */
async function expectTableUiIsPseudoEnglish(
    page: Parameters<typeof getTableUiTexts>[0],
    what: string,
    atLeast: number,
): Promise<string[]> {
    const texts = await getTableUiTexts(page);
    // Sanity check that there was something to read: an empty list would pass everything below.
    expect(
        texts.length,
        `${what} should be showing at least ${atLeast} pieces of wording, but the table UI ` +
            `shows: ${describeTexts(texts).join(", ") || "(nothing)"}`,
    ).toBeGreaterThanOrEqual(atLeast);
    expect(
        describeTexts(texts.filter((t) => t.text === "")),
        `${what} has controls with no name at all.`,
    ).toEqual([]);
    expect(
        describeTexts(texts.filter((t) => !isPseudoEnglish(t.text))),
        `${what} shows wording that did not come from Bloom's localization: in Pseudo-English ` +
            `every localized string is in square brackets, and these are not.`,
    ).toEqual([]);
    return texts.map((t) => t.text);
}

test.describe("a table's own UI, in the user's UI language", () => {
    test("in Pseudo-English, the first table on a Canvas page shows its buttons with localized tooltips", async ({
        bloomApp,
        step,
    }) => {
        // This test also pays for launching Bloom, making the book, and a change of UI language.
        test.setTimeout(420000);

        await step("Make the two books, in English", async () => {
            if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
                await setUiStateViaApi(bloomApp, "en", false);
            const page = bloomApp.page;
            // Sanity check the two gates on tables, so that a table with no buttons later is not
            // mistaken for a table with no wording.
            const tableFeature = await getFeatureStatus(page, "table");
            expect(
                {
                    enabled: tableFeature.enabled,
                    visible: tableFeature.visible,
                },
                `Tables need the Pro tier (enabled) and the "tables" experiment (visible). ` +
                    `Bloom's whole answer: ${JSON.stringify(tableFeature)}`,
            ).toEqual({ enabled: true, visible: true });

            tableOnlyBookFolder = await makeBookFromTemplate(
                page,
                "Basic Book",
            );
            await addPage(page, "Just Text");
            const [tablePage] = await getContentPages(page);
            await goToPage(page, tablePage.id);
            await addTableInChangeLayout(page);

            bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Canvas");
            await addPage(page, "Just Text");
            [coverPage] = await getPages(page);
            [canvasPage, layoutPage] = await getContentPages(page);
            await switchTab(page, "collection");
        });

        await step("Change the UI language to Pseudo-English", async () => {
            await choosePseudoEnglishUiLanguage(bloomApp);
            expect(await getCurrentUiLanguageTag(bloomApp.page)).toBe(
                kPseudoEnglishUiLanguageTag,
            );
        });

        const page = bloomApp.page;

        await step("Open the Canvas page, which has no table", async () => {
            await selectBook(page, bookFolder);
            await switchTab(page, "edit");
            await goToPage(page, canvasPage.id);
            expect(
                await getTableCount(page),
                "The Canvas page should start with no table.",
            ).toBe(0);
        });

        await step(
            "Check the Canvas tool's Table icon has a localized tooltip",
            async () => {
                const tooltip = await getPaletteItemTooltip(page, "table");
                expect(
                    tooltip.length,
                    "The Table icon's tooltip should say something.",
                ).toBeGreaterThan(2);
                expect(
                    isPseudoEnglish(tooltip),
                    `The Table icon's tooltip, "${tooltip}", did not come from Bloom's ` +
                        `localization.`,
                ).toBe(true);
            },
        );

        await step("Drag the Table icon onto the canvas", async () => {
            await dragPaletteItemOntoCanvas(page, "table", {
                xFraction: 0.3,
                yFraction: 0.25,
            });
            await waitForTableAttached(page);
        });

        await step(
            "Move onto the table and check its buttons and their tooltips",
            async () => {
                // This is the first table on the page, so this is also the check that a table
                // added to a page with none asks for its wording and then shows its buttons.
                await showTableButtons(page);
                // Three round menu buttons and two "+" buttons, each with a tooltip and a name.
                await expectTableUiIsPseudoEnglish(
                    page,
                    "The table with the pointer on it",
                    10,
                );
            },
        );
    });

    for (const which of ["row", "column", "table"] as const) {
        test(`in Pseudo-English, the ${which} menu is localized under every border tab`, async ({
            bloomApp,
            step,
        }) => {
            const page = bloomApp.page;
            await step(`Open the ${which} menu`, async () => {
                await openTableMenu(page, which);
            });
            const scopes = await getBorderScopesOffered(page);
            expect(
                scopes,
                `The ${which} menu should offer all four border tabs.`,
            ).toEqual(["all", "outer", "inner", "brush"]);

            const wordingUnderTab = new Map<string, string>();
            for (const scope of scopes) {
                await step(`Click the "${scope}" border tab`, async () => {
                    await chooseBorderScope(page, scope);
                    const texts = await expectTableUiIsPseudoEnglish(
                        page,
                        `The ${which} menu with the "${scope}" border tab chosen`,
                        30,
                    );
                    wordingUnderTab.set(scope, texts.join("|"));
                });
            }
            // The explanation under the tabs is what changes from tab to tab, so four tabs that
            // read alike would mean it had stopped following them.
            expect(
                new Set(wordingUnderTab.values()).size,
                `The ${which} menu should read differently under each of its border tabs.`,
            ).toBe(scopes.length);

            await step(
                "Put the border tabs back and close the menu",
                async () => {
                    await chooseBorderScope(page, "all");
                    await closeAnyMenu(page);
                },
            );
        });
    }

    test("in Pseudo-English, a row given a fixed size shows the size with its localized unit", async ({
        bloomApp,
        step,
    }) => {
        const page = bloomApp.page;
        const strings = await getUiStrings(page, kStringsById);
        await step("Open the row menu and choose a fixed size", async () => {
            await openTableMenu(page, "row");
            await clickTableMenuControl(
                page,
                strings["EditTab.Table.Size.Fixed"],
            );
        });
        await step("Check the size reads as a number and a unit", async () => {
            const texts = await expectTableUiIsPseudoEnglish(
                page,
                "The row menu with a fixed size chosen",
                30,
            );
            // The localized pattern for a size in millimetres, e.g. "[{0}mm]", with a number
            // where the pattern has its {0}.
            const [before, after] =
                strings["EditTab.Table.Units.Millimetres"].split("{0}");
            const sizes = texts.filter(
                (text) =>
                    text.startsWith(before) &&
                    text.endsWith(after) &&
                    /^\d+([.,]\d+)?$/.test(
                        text.slice(before.length, text.length - after.length),
                    ),
            );
            expect(
                sizes,
                `The row menu should show the row's size in the form ` +
                    `"${strings["EditTab.Table.Units.Millimetres"]}". It shows: ` +
                    texts.join(", "),
            ).toHaveLength(1);
            await closeAnyMenu(page);
        });
    });

    test("in Pseudo-English, the Cell menu is localized and names what the cell holds", async ({
        bloomApp,
        step,
    }) => {
        const page = bloomApp.page;
        const strings = await getUiStrings(page, kStringsById);
        const textPadding = strings["EditTab.Table.Format.Padding.Text"];
        const imagePadding = strings["EditTab.Table.Format.Padding.Image"];

        await step("Right-click a text cell", async () => {
            const menus = await rightClickCell(page, 0, 0);
            expect(menus.cell, "The Cell menu should have opened.").toBe(true);
            const texts = await expectTableUiIsPseudoEnglish(
                page,
                "The Cell menu of a text cell",
                30,
            );
            expect(
                texts,
                "A text cell's padding should be described as padding round text.",
            ).toContain(textPadding);
            await closeAnyMenu(page);
        });

        await step("Make another cell hold a picture", async () => {
            await setCellContentType(page, 0, 1, "image");
        });

        await step("Right-click the picture cell", async () => {
            const menus = await rightClickCell(page, 0, 1);
            expect(menus.cell, "The Cell menu should have opened.").toBe(true);
            const texts = await expectTableUiIsPseudoEnglish(
                page,
                "The Cell menu of a picture cell",
                30,
            );
            expect(
                texts,
                "A picture cell's padding should be described as padding round an image.",
            ).toContain(imagePadding);
            expect(
                texts,
                "A picture cell's padding should not be described as padding round text.",
            ).not.toContain(textPadding);
            await closeAnyMenu(page);
        });
    });

    test("in Pseudo-English, the Paint format badge and its tooltip are localized", async ({
        bloomApp,
        step,
    }) => {
        const page = bloomApp.page;
        const strings = await getUiStrings(page, kStringsById);
        await step("Choose Paint format from the row menu", async () => {
            await openTableMenu(page, "row");
            await startPaintFormat(
                page,
                strings["EditTab.Table.PaintFormat.Command"],
            );
        });
        await step("Check the badge above the table", async () => {
            // The badge's words and its accessible name are the same string; its tooltip is the
            // one that also names the Esc key.
            const texts = await expectTableUiIsPseudoEnglish(
                page,
                "The table with format painting on",
                3,
            );
            expect(
                texts,
                "The badge should say how to end format painting.",
            ).toContain(strings["EditTab.Table.PaintFormat.End"]);
            expect(texts, "The badge's tooltip should mention Esc.").toContain(
                strings["EditTab.Table.PaintFormat.EndWithShortcut"],
            );
        });
        await step("End format painting", async () => {
            await endPaintFormat(page);
        });
    });

    test("in Pseudo-English, the first table added to a page in Change Layout shows localized buttons and menus", async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(180000);
        const page = bloomApp.page;
        await step("Open the page that has no table", async () => {
            await goToPage(page, layoutPage.id);
            expect(
                await getTableCount(page),
                "The Just Text page should start with no table.",
            ).toBe(0);
        });
        await step(
            "Add a table with the Table link in Change Layout",
            async () => {
                await addTableInChangeLayout(page);
            },
        );
        await step("Move onto the table and check its buttons", async () => {
            await showTableButtons(page);
            await expectTableUiIsPseudoEnglish(
                page,
                "The new table with the pointer on it",
                10,
            );
        });
        await step("Open its Table menu and check that too", async () => {
            await openTableMenu(page, "table");
            await expectTableUiIsPseudoEnglish(
                page,
                "The new table's Table menu",
                30,
            );
            await closeAnyMenu(page);
        });
    });

    test("a table shows its buttons under a resting pointer when Bloom's answer about the subscription comes late", async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(180000);
        const page = bloomApp.page;
        await step("Leave the page that has the table", async () => {
            await goToPage(page, coverPage.id);
        });
        const held = await holdBackTableStartupAnswer(page, "permission");
        try {
            await step(
                "Open the page with the table, with the answer held back",
                async () => {
                    await goToPage(page, canvasPage.id);
                    await waitForTableAttached(page);
                    await held.onlyTheseAreMissing();
                },
            );
            await step("Move onto the table straight away", async () => {
                // Until Bloom has said this user may change the shape of tables, the table shows
                // none of the buttons that do so. This is also the sanity check that the answer
                // really was held back: with it, the buttons would be showing by now.
                expect(
                    await countTableButtonsAfterMovingOntoTable(page),
                    "The table should show no buttons before Bloom has said they are allowed.",
                ).toBe(0);
            });
            await step(
                "Let the answer through, and do not move the pointer",
                async () => {
                    await held.release();
                    await expectTableButtonsShowing(page);
                },
            );
        } finally {
            await held.release();
        }
    });

    test("in Pseudo-English, a table shows no buttons until its wording has arrived, and then shows them localized", async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(180000);
        const page = bloomApp.page;
        await step("Leave the page that has the table", async () => {
            await goToPage(page, coverPage.id);
        });
        const held = await holdBackTableStartupAnswer(page, "wording");
        try {
            await step(
                "Open the page with the table, with its wording held back",
                async () => {
                    await goToPage(page, canvasPage.id);
                    await waitForTableAttached(page);
                    await held.onlyTheseAreMissing();
                },
            );
            await step("Move onto the table straight away", async () => {
                // The table has English of its own to fall back on, and showing that now would
                // mean the user sees English turn into their language a moment later.
                expect(
                    await countTableButtonsAfterMovingOntoTable(page),
                    "The table should show no buttons before it has their wording.",
                ).toBe(0);
            });
            await step(
                "Let the wording through, and do not move the pointer",
                async () => {
                    await held.release();
                    await expectTableButtonsShowing(page);
                    await expectTableUiIsPseudoEnglish(
                        page,
                        "The table once its wording has arrived",
                        10,
                    );
                },
            );
        } finally {
            await held.release();
        }
    });

    test("in French, nothing in the table's buttons or menus is blank or shows a string's id", async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(240000);
        await step("Change the UI language to French", async () => {
            await goToPage(bloomApp.page, coverPage.id);
            await switchTab(bloomApp.page, "collection");
            await chooseUiLanguage(bloomApp, "fr");
        });
        const page = bloomApp.page;
        await step("Open the page with the table", async () => {
            await selectBook(page, bookFolder);
            await switchTab(page, "edit");
            await goToPage(page, canvasPage.id);
            await waitForTableAttached(page);
        });

        // A string that has no French yet falls back to its English. What must never show is
        // nothing at all, or the string's own id, which is what a lookup that found neither
        // would leave behind.
        const expectReadable = async (what: string, atLeast: number) => {
            const texts = await getTableUiTexts(page);
            expect(
                texts.length,
                `${what} should be showing at least ${atLeast} pieces of wording.`,
            ).toBeGreaterThanOrEqual(atLeast);
            expect(
                describeTexts(texts.filter((t) => t.text === "")),
                `${what} has controls with no name at all.`,
            ).toEqual([]);
            expect(
                describeTexts(
                    texts.filter((t) => /^\w+(\.\w+)+$/.test(t.text)),
                ),
                `${what} shows the id of a string in place of its wording.`,
            ).toEqual([]);
        };

        await step("Check the table's buttons", async () => {
            await showTableButtons(page);
            await expectReadable("The table with the pointer on it", 10);
        });
        for (const which of ["row", "column", "table"] as const) {
            await step(`Check the ${which} menu`, async () => {
                await openTableMenu(page, which);
                await expectReadable(`The ${which} menu`, 30);
                await closeAnyMenu(page);
            });
        }
        await step("Check the Cell menu", async () => {
            const menus = await rightClickCell(page, 0, 0);
            expect(menus.cell, "The Cell menu should have opened.").toBe(true);
            await expectReadable("The Cell menu", 30);
            await closeAnyMenu(page);
        });
    });

    test("in Pseudo-English, below Pro, the Publish tab's notice names the Table feature in the UI language", async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(300000);
        await step(
            "Change the UI language back to Pseudo-English",
            async () => {
                await goToPage(bloomApp.page, coverPage.id);
                await switchTab(bloomApp.page, "collection");
                await choosePseudoEnglishUiLanguage(bloomApp);
            },
        );
        await step("Restart Bloom with no subscription", async () => {
            // No subscription code at all means the Basic tier, which is below the one tables
            // need. See restartWithCollectionSettings.
            await restartWithCollectionSettings(bloomApp, {
                languages: ["en"],
            });
            expect(
                await getCurrentUiLanguageTag(bloomApp.page),
                "Bloom should still be in Pseudo-English after the restart.",
            ).toBe(kPseudoEnglishUiLanguageTag);
        });
        const page = bloomApp.page;
        const strings = await getUiStrings(page, kStringsById);
        // Sanity check that the name the notice is expected to use is itself localized, or the
        // assertion below would be satisfied by plain English.
        expect(
            strings["Feature.Table"],
            "Bloom should have a Pseudo-English name for the Table feature.",
        ).not.toBe(kStringsById["Feature.Table"]);

        await step(
            "Open the Publish tab on the book whose only feature is a table",
            async () => {
                await selectBook(page, tableOnlyBookFolder);
                await openPublishTab(page);
            },
        );
        await step("Check the notice names the feature", async () => {
            expect(
                await isPublishingBlockedNoticeShowing(page),
                "The Publish tab should say this book uses a feature the subscription does " +
                    "not include.",
            ).toBe(true);
            expect(
                await getPublishingBlockedNoticeText(page),
                "The notice should name the Table feature in the UI language.",
            ).toContain(strings["Feature.Table"]);
        });
    });
});
