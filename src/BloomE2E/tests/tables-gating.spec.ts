// What happens to a book that has a table in it when tables cannot be made: below the Pro
// subscription tier tables need, and with the "Tables" experiment turned off.
//
// The rule this file measures is the canvas rule: localize, don't create. Someone who receives a
// book with a table in it must be able to work on it -- type in its text cells, replace the
// picture in a picture cell, publish a derivative of it -- while everything that would make or
// reshape a table is withheld: the "+" buttons, the row, column and table pills, the Cell menu,
// a boundary drag, Duplicate on the element's toolbar, the Table item in the Canvas tool's
// palette, and the Table entry in the page layout controls. Below Pro the last two explain
// themselves (a dimmed entry with a subscription badge, and the dialog naming the tier); with the
// experiment off they are simply absent, because an experiment nobody has turned on should not
// advertise itself.
//
// Three states, one Bloom, in order. Each test puts Bloom into the state it is about by quitting
// it and starting it again: the tier comes from a real subscription code in the .bloomCollection,
// which Bloom reads as it opens the collection, and the experiment comes from an --e2e environment
// variable, which it reads as it starts. So a state change is a restart, and the page object a
// test was handed before one is dead afterwards -- hence `bloomApp.page` rather than the `page`
// fixture in the tests after the first.
//
// tables-core.spec.ts is the file to read first when tables break. This one says nothing about
// whether tables work; it is entirely about what is withheld, and every assertion here should
// fail if a gate is removed.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    closeAddPageDialog,
    getAddPageOffer,
    openAddPageDialog,
    selectPageInAddPageDialog,
} from "../helpers/addPageDialog";
import { readBook } from "../helpers/bookHtml";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromBookInCollection,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    closeCanvasElementMenu,
    dragPaletteItemOntoCanvas,
    getCanvasElementMenuItems,
    getCanvasElementToolbarButtonCount,
    getPaletteItemsOffered,
} from "../helpers/canvasElements";
import { selectBook } from "../helpers/collection";
import {
    getFeatureStatus,
    kProSubscriptionCode,
    restartWithCollectionSettings,
} from "../helpers/collectionSettings";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import {
    clickTableSectionType,
    getSectionTypesOffered,
    getTableSectionTypeOffer,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";
import {
    getPublishDestinationsOffered,
    getPublishingBlockedNoticeText,
    isPublishingBlockedNoticeShowing,
    openPublishDestination,
    openPublishTab,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    closeSubscriptionDialog,
    expectNoSubscriptionDialog,
    waitForSubscriptionDialog,
} from "../helpers/requiresSubscription";
import {
    cell,
    cellVideoBox,
    clickCell,
    closeAnyMenu,
    expectBoundaryDragDoesNothing,
    expectCellsTile,
    expectTablesRenderAsGrids,
    getCellText,
    getTableShape,
    measureChrome,
    rightClickCellExpectingNoCellMenu,
    rightClickCellTextExpectingNoCellMenu,
    selectTableElement,
    setCellContentType,
    typeInCell,
    waitForTableAttached,
} from "../helpers/tables";
import { chooseVideoFile, SHORT_VIDEO } from "../helpers/videos";
import { switchTab } from "../helpers/workspace";

// The collection starts on the Pro tier with the experiment on, because the book under test has to
// be built before it can be received: a table can only be made where tables are allowed.
test.use({
    collectionSpec: {
        name: "tables-gating",
        languages: ["en"],
        subscriptionCode: kProSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test.describe.configure({ mode: "serial" });

const FIXTURES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
);
// The picture the table is built with, and the one that replaces it below Pro. Two different
// files, so that "the picture was replaced" is a real assertion rather than a repeat of itself.
const FIRST_IMAGE = Path.join(FIXTURES, "images", "bird.png");
const REPLACEMENT_IMAGE = Path.join(FIXTURES, "images", "blue-square.png");

// The book built as Pro, and the pages of it that matter: the Canvas page holding the table, and a
// text page whose layout a person may change, which is where the page layout controls offer a
// Table entry. Set by the first test.
let originalBook: string;
let tablePage: IBookPage;
let videoTablePage: IBookPage;
let textPage: IBookPage;
// The folder of the derivative the first test makes below Pro; the video-table test reads it.
let derivativeFolder: string;

test.describe("a book with a table where tables cannot be made", () => {
    test("below Pro, a derivative's table can be filled in but not restructured", async ({
        page,
        step,
        bloomApp,
    }) => {
        // This test pays for launching Bloom, building the book, and one restart.
        test.setTimeout(600000);

        await step(
            "Check this collection may use tables to begin with",
            async () => {
                // The book has to be built before it can be received, so the first half of this test
                // needs both gates open. A failure here says the fixture could never have been made.
                const tableFeature = await getFeatureStatus(page, "table");
                expect(
                    {
                        enabled: tableFeature.enabled,
                        visible: tableFeature.visible,
                    },
                    `Building the fixture needs the Pro tier (enabled) and the "tables" experiment ` +
                        `(visible). Bloom's whole answer: ${JSON.stringify(tableFeature)}`,
                ).toEqual({ enabled: true, visible: true });
            },
        );

        await step("Make a book with a table on a Canvas page", async () => {
            originalBook = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Canvas");
            [tablePage] = await getContentPages(page);
            await goToPage(page, tablePage.id);
            // Into the upper left, so that the table and the toolbar Bloom draws under a selected
            // element both stay on the page.
            await dragPaletteItemOntoCanvas(page, "table", {
                xFraction: 0.3,
                yFraction: 0.25,
            });
            await waitForTableAttached(page);
        });

        await step("Fill the table with text and a picture", async () => {
            // The picture last: putting one in a cell leaves a drawing surface over the table
            // that takes every press afterwards, so nothing that needs a cell click can follow
            // it until the page is rebuilt (AUTOMATION-DEBT.md).
            await typeInCell(page, 0, 0, "en", "Apple");
            await typeInCell(page, 0, 1, "en", "Banana");
            await setCellContentType(page, 1, 0, "image");
            await chooseImageFile(page, FIRST_IMAGE, await cell(page, 1, 0));
            await reloadPageBeingEdited(page);
            await waitForTableAttached(page);
        });

        await step("Put a video in a table on a page of its own", async () => {
            // A video cell belongs in the fixture: it is one of the things the gating has to
            // leave alone. It goes in a table on its own page so that the last test in this
            // file can ask about that table's element menu on its own.
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Canvas");
            videoTablePage = (await getContentPages(page)).find(
                (p) => !before.includes(p.id),
            )!;
            await goToPage(page, videoTablePage.id);
            await dragPaletteItemOntoCanvas(page, "table", {
                xFraction: 0.3,
                yFraction: 0.25,
            });
            await waitForTableAttached(page);
            await setCellContentType(page, 0, 0, "video");
            await chooseVideoFile(
                page,
                await cellVideoBox(page, 0, 0),
                SHORT_VIDEO,
            );
        });

        await step(
            "Add a text page, for the page layout controls",
            async () => {
                // The Table entry this test asks about is offered by an empty section of a page whose
                // layout a person may change; the Canvas page has no such list.
                const before = (await getContentPages(page)).map((p) => p.id);
                await addPage(page, "Just Text");
                textPage = (await getContentPages(page)).find(
                    (p) => !before.includes(p.id),
                )!;
                // Leaving the page is what makes Bloom write the book out, and the restart below kills
                // Bloom rather than asking it to quit, so anything not written would be lost.
                await goToPage(page, textPage.id);
                await switchTab(page, "collection");
            },
        );

        const belowPro = await step(
            "Restart the collection below Pro",
            async () => {
                // Bloom reads the tier out of the .bloomCollection as it opens the collection and then
                // keeps it, so the only way to change it is to quit, rewrite the file, and start
                // again. No subscription code at all means the Basic tier. See
                // restartWithCollectionSettings.
                return restartWithCollectionSettings(bloomApp, {
                    languages: ["en"],
                });
            },
        );
        if (!belowPro) throw new Error("Bloom never restarted below Pro.");

        await step(
            "Check tables are now off the tier but still visible",
            async () => {
                // Which of the two gates is shut matters for everything below: below Pro the table
                // surfaces explain themselves, where with the experiment off they vanish. A failure
                // here means the restart did not take, and every assertion after it would be meaningless.
                const tableFeature = await getFeatureStatus(belowPro, "table");
                expect(
                    {
                        enabled: tableFeature.enabled,
                        visible: tableFeature.visible,
                    },
                    `Below Pro the table feature should be visible but not enabled. Bloom's whole ` +
                        `answer: ${JSON.stringify(tableFeature)}`,
                ).toEqual({ enabled: false, visible: true });
            },
        );

        const derivative = await step(
            "Make a derivative of the book, the way someone who received it does",
            async () => {
                const folder = await makeBookFromBookInCollection(
                    belowPro,
                    originalBook,
                );
                const contents = await readBook(belowPro, folder);
                // The page whose table holds the picture, not the one whose table holds the
                // video: the video table has a test of its own at the end of this file.
                const withTable = contents.pages.find(
                    (p) =>
                        p.tables.length > 0 &&
                        p.tables.every((t) => t.videoSources.length === 0),
                );
                if (!withTable)
                    throw new Error(
                        `The derivative at ${folder} has no page with a table on it, so the ` +
                            `book it was made from was not saved as expected.`,
                    );
                const withoutTable = contents.pages.find(
                    (p) => p.tables.length === 0,
                );
                if (!withoutTable)
                    throw new Error(
                        `The derivative at ${folder} has no page without a table, so the text ` +
                            `page was not copied.`,
                    );
                derivativeFolder = folder;
                return { folder, table: withTable.id, text: withoutTable.id };
            },
        );
        if (!derivative) throw new Error("The derivative was never made.");

        await step("Open the page with the table on it", async () => {
            await goToPage(belowPro, derivative.table);
            await waitForTableAttached(belowPro);
            const shape = await getTableShape(belowPro);
            expect(
                { rows: shape.rows, columns: shape.columns },
                "The derivative should show the two by two table it inherited.",
            ).toEqual({ rows: 2, columns: 2 });
            await expectCellsTile(belowPro);
            expect(
                await getCellText(belowPro, 0, 0, "en"),
                "The table should still hold the text it was built with.",
            ).toBe("Apple");
        });

        await step("Type in a text cell", async () => {
            // The whole point of the rule: the person who received this book has to be able to
            // translate it.
            await typeInCell(belowPro, 0, 0, "en", "Pomme");
            expect(
                await getCellText(belowPro, 0, 0, "en"),
                "A text cell should take typing below Pro.",
            ).toBe("Pomme");
        });

        await step("Replace the picture in the picture cell", async () => {
            const pictureCell = await cell(belowPro, 1, 0);
            await chooseImageFile(belowPro, REPLACEMENT_IMAGE, pictureCell);
            expect(
                (await getImagePlacement(belowPro, pictureCell)).fileName,
                "A picture cell should take a new picture below Pro.",
            ).toBe("blue-square.png");
            await reloadPageBeingEdited(belowPro);
            await waitForTableAttached(belowPro);
        });

        await step("Check clicking a cell raises no chrome", async () => {
            await clickCell(belowPro, 0, 0);
            const chrome = await measureChrome(belowPro);
            expect(
                chrome.map((piece) => piece.name),
                "Selecting a cell below Pro should raise no + buttons and no pills, because " +
                    "every one of them restructures the table.",
            ).toEqual([]);
        });

        await step(
            "Check right-clicking a text cell offers Bloom's text menu, not the Cell menu",
            async () => {
                const menus = await rightClickCellTextExpectingNoCellMenu(
                    belowPro,
                    0,
                    0,
                    "en",
                );
                expect(
                    menus.cell,
                    "The Cell menu is entirely structural (content type, merge, insert), so below " +
                        "Pro it should not open.",
                ).toBe(false);
                expect(
                    {
                        row: menus.row,
                        column: menus.column,
                        table: menus.table,
                    },
                    "None of the table's other menus should open either; every command in them " +
                        "restructures the table.",
                ).toEqual({ row: false, column: false, table: false });
                // The cell holds an ordinary Bloom text box, so with the Cell menu withheld the
                // right-click should reach the menu that text gets anywhere else. See
                // findParagraphForTextContextMenu in noIndent.ts, which has to let a paragraph
                // inside a cell through even though a table on a Canvas page sits inside a canvas
                // element.
                expect(
                    menus.bloomText,
                    "A text cell should still offer Bloom's own text context menu, so that the " +
                        "person who received the book is not left with no menu at all.",
                ).toBe(true);
                // The menu stays up until something takes it down, and a MUI menu's backdrop
                // takes every press aimed at the page underneath it.
                await closeAnyMenu(belowPro);
            },
        );

        await step(
            "Check right-clicking a picture cell offers the picture's own menu",
            async () => {
                // The other half of the same rule: a cell's content keeps the menu that content
                // has anywhere else. A picture cell holds a bloom-canvas, so the press should
                // reach the canvas element handling that any picture gets, rather than being
                // left to a Cell menu that this tier never opens. See isPressInsideTable in
                // CanvasElementPointerInteractions.ts.
                const menus = await rightClickCellExpectingNoCellMenu(
                    belowPro,
                    1,
                    0,
                );
                expect(
                    { cell: menus.cell, canvasElement: menus.canvasElement },
                    "A picture in a cell should offer the menu a picture offers, and not the " +
                        "Cell menu.",
                ).toEqual({ cell: false, canvasElement: true });
                await closeAnyMenu(belowPro);
            },
        );

        await step(
            "Check the element toolbar offers no Duplicate",
            async () => {
                // The table's own canvas element, not a cell: the menu belongs to whichever
                // element is active, and clicking a cell does not make the table's element the
                // active one.
                await closeAnyMenu(belowPro);
                await selectTableElement(belowPro);
                const items = await getCanvasElementMenuItems(belowPro);
                const ids = items.map((item) => item.id);
                expect(
                    ids,
                    "Duplicating a table is making one, so it should not be on offer below Pro. " +
                        `The menu offers: ${ids.join(", ") || "(nothing)"}.`,
                ).not.toContain("EditTab.Toolbox.ComicTool.Options.Duplicate");
                expect(
                    ids,
                    "Delete should stay: someone must always be able to get rid of something they " +
                        "cannot edit.",
                ).toContain("Common.Delete");
                await closeCanvasElementMenu(belowPro);
                expect(
                    await getCanvasElementToolbarButtonCount(belowPro),
                    "The toolbar should show Delete and the ... button, and no Duplicate.",
                ).toBe(2);
            },
        );

        await step(
            "Check dragging a column boundary does nothing",
            async () => {
                await clickCell(belowPro, 0, 0);
                const shape = await getTableShape(belowPro);
                expect(
                    shape.columnWidths.every((width) => width === "fill"),
                    `Sanity check before the drag: both columns should still be sharing the space, ` +
                        `but they are ${shape.columnWidths.join(", ")}.`,
                ).toBe(true);
                await expectBoundaryDragDoesNothing(belowPro, "column", 0, -40);
            },
        );

        await step(
            "Check the Canvas tool's palette offers no table",
            async () => {
                const offered = await getPaletteItemsOffered(belowPro);
                expect(
                    offered,
                    `The palette should not offer a table below Pro. It offers: ` +
                        `${offered.join(", ") || "(nothing)"}.`,
                ).not.toContain("table");
                expect(
                    offered.length,
                    "Sanity check: the palette should still be offering its other items, so a " +
                        "missing table item means the table item is gated and not that the palette " +
                        "failed to draw.",
                ).toBeGreaterThan(0);
            },
        );

        await step(
            "Check the page layout controls offer a dimmed, badged Table entry",
            async () => {
                await goToPage(belowPro, derivative.text);
                await setChangeLayoutMode(belowPro, true);
                // The list of types is offered only for a section with nothing in it yet, so make
                // one. This is also how a person adds a table to a page that already has text.
                await splitSection(belowPro, "right", 0);
                const offer = await getTableSectionTypeOffer(belowPro, 1);
                expect(
                    offer.offered,
                    `Below Pro the Table entry should stay on offer, so that a person can see ` +
                        `that tables exist. The section offers: ` +
                        `${(await getSectionTypesOffered(belowPro, 1)).join(", ")}.`,
                ).toBe(true);
                expect(
                    offer.needsSubscription,
                    "The Table entry should be dimmed and carry the subscription badge.",
                ).toBe(true);
                expect(
                    offer.opacity,
                    `The dimming should be visible: the entry's opacity is ${offer.opacity}.`,
                ).toBeLessThan(1);
            },
        );

        await step(
            "Check clicking that entry opens the subscription dialog and makes no table",
            async () => {
                await clickTableSectionType(belowPro, 1);
                await waitForSubscriptionDialog(
                    belowPro,
                    "Clicking the dimmed Table entry",
                );
                await closeSubscriptionDialog(belowPro);
                expect(
                    await getSectionTypesOffered(belowPro, 1),
                    "The section should still be empty and still offering its list of types, " +
                        "because the click explained itself instead of making a table.",
                ).toContain("table");
                await setChangeLayoutMode(belowPro, false);
            },
        );

        await step(
            "Check the Alphabet Book page cannot be added from the Add Page dialog",
            async () => {
                // The ready-made page whose whole content is a table. It stays in the dialog, so
                // that a person can see it, but the dialog offers the subscription notice in place
                // of its Add Page button.
                await openAddPageDialog(belowPro);
                await selectPageInAddPageDialog(belowPro, "Alphabet Book");
                const offer = await getAddPageOffer(belowPro);
                expect(
                    offer,
                    "Selecting the Alphabet Book page below Pro should offer the notice that " +
                        "names the subscription it needs, and no way to add it.",
                ).toEqual({
                    addButtonOffered: false,
                    requiresSubscriptionNotice: true,
                });
                await closeAddPageDialog(belowPro);
            },
        );

        await step(
            "Check a BloomPUB preview of the derivative still draws the table",
            async () => {
                // A derivative publishes: the tier blocks publishing an original that uses a
                // feature above it, not a book someone received. And what it publishes has to be
                // the table, drawn as a grid.
                await openPublishDestination(belowPro, "BloomPUB");
                const player = await showBloomPubPreview(belowPro);
                await expectTablesRenderAsGrids(player, 1, derivative.table);
                await switchTab(belowPro, "collection");
            },
        );
    });

    // A table is a table whatever its cells hold. inferCanvasElementType once tested for a
    // bloom-videoContainer anywhere in the element before it tested for a bloom-table, so a table
    // with a video cell resolved to the "video" type and got a video's toolbar, with Duplicate
    // ungated because only the table type's rules gate it. Below Pro that let a person duplicate
    // a table after all, as long as it held a video.
    test("below Pro, a table holding a video is still treated as a table", async ({
        bloomApp,
        step,
    }) => {
        const page = bloomApp.page;
        if (!derivativeFolder)
            throw new Error(
                "The derivative test has to run first; it makes the book this test opens.",
            );

        const videoPageId = await step(
            "Find the derivative's page whose table holds the video",
            async () => {
                // The derivative's own page, not the original's: a derivative's pages are copies.
                const contents = await readBook(page, derivativeFolder);
                const withVideo = contents.pages.find((p) =>
                    p.tables.some((t) => t.videoSources.length > 0),
                );
                if (!withVideo)
                    throw new Error(
                        `The derivative at ${derivativeFolder} has no page whose table holds a ` +
                            `video, so the video page was not copied.`,
                    );
                return withVideo.id;
            },
        );
        if (!videoPageId) throw new Error("The video page was never found.");

        await step("Open that page in the Edit tab", async () => {
            // The previous test left Bloom on the Collection tab with the derivative selected.
            await switchTab(page, "edit");
            await goToPage(page, videoPageId);
            await waitForTableAttached(page);
        });

        await step("Check the table's element menu is a table's", async () => {
            await selectTableElement(page);
            const ids = (await getCanvasElementMenuItems(page)).map(
                (item) => item.id,
            );
            expect(
                ids,
                "A table with a video cell should offer the table's own commands, and no " +
                    "Duplicate below Pro.",
            ).not.toContain("EditTab.Toolbox.ComicTool.Options.Duplicate");
        });
    });

    // The plan's remaining case, pasting a copied table below Pro, is not reachable and is
    // not meant to be: copying a table is itself a Pro command (the table pill's Copy Table),
    // so this tier can never get a table onto the clipboard in the first place.

    test("below Pro, the original book cannot be published at all", async ({
        bloomApp,
        step,
    }) => {
        const page = bloomApp.page;

        await step("Select the book the table was made in", async () => {
            await switchTab(page, "collection");
            await selectBook(page, originalBook);
        });

        await step("Open the Publish tab", async () => {
            await openPublishTab(page);
        });

        await step(
            "Check publishing is blocked, as it is for canvas",
            async () => {
                // An original that uses a feature above the collection's tier cannot be published in
                // any form; the notice replaces the whole list of destinations. This is the rule
                // canvas originals follow, and the reason a derivative (above) publishes and this
                // does not.
                expect(
                    await isPublishingBlockedNoticeShowing(page),
                    "The Publish tab should say this book uses a feature the subscription does not " +
                        "include.",
                ).toBe(true);
                const destinations = await getPublishDestinationsOffered(page);
                expect(
                    destinations,
                    `No destination should be reachable, but the tab offers: ` +
                        `${destinations.join(", ")}.`,
                ).toEqual([]);
                // The notice has to name the feature and the page, or it tells the person nothing
                // they can act on. Its words are localized, so this only checks it is not empty.
                expect(
                    (await getPublishingBlockedNoticeText(page)).length,
                    "The notice should say something.",
                ).toBeGreaterThan(0);
            },
        );
    });

    test("with the tables experiment off, the table is frozen and nothing offers one", async ({
        bloomApp,
        step,
    }) => {
        test.setTimeout(600000);

        const withoutExperiment = await step(
            "Restart on Pro with the experiment off",
            async () => {
                // Pro again, so that nothing here can be blamed on the tier: this test is about
                // the experiment alone. The experiment is turned off by naming no features at
                // launch, because the saved setting lives in a user.config shared with the
                // developer's own Bloom (see ExperimentalFeatures.cs).
                return restartWithCollectionSettings(
                    bloomApp,
                    {
                        languages: ["en"],
                        subscriptionCode: kProSubscriptionCode,
                    },
                    { experimentalFeatures: [] },
                );
            },
        );
        if (!withoutExperiment)
            throw new Error("Bloom never restarted without the experiment.");

        await step("Check tables are on the tier but not visible", async () => {
            const tableFeature = await getFeatureStatus(
                withoutExperiment,
                "table",
            );
            expect(
                {
                    enabled: tableFeature.enabled,
                    visible: tableFeature.visible,
                },
                `With the experiment off and the tier restored, tables should be enabled but ` +
                    `not visible. Bloom's whole answer: ${JSON.stringify(tableFeature)}`,
            ).toEqual({ enabled: true, visible: false });
        });

        await step("Open the page with the table on it", async () => {
            await selectBook(withoutExperiment, originalBook);
            await switchTab(withoutExperiment, "edit");
            await goToPage(withoutExperiment, tablePage.id);
            await waitForTableAttached(withoutExperiment);
            const shape = await getTableShape(withoutExperiment);
            expect(
                { rows: shape.rows, columns: shape.columns },
                "A table in a book should still be drawn when the experiment that made it is off.",
            ).toEqual({ rows: 2, columns: 2 });
            await expectCellsTile(withoutExperiment);
        });

        await step("Type in a text cell", async () => {
            await typeInCell(withoutExperiment, 0, 1, "en", "Cherry");
            expect(
                await getCellText(withoutExperiment, 0, 1, "en"),
                "A text cell should take typing with the experiment off.",
            ).toBe("Cherry");
        });

        await step("Check clicking a cell raises no chrome", async () => {
            await clickCell(withoutExperiment, 0, 0);
            const chrome = await measureChrome(withoutExperiment);
            expect(
                chrome.map((piece) => piece.name),
                "With the experiment off the table should carry no chrome at all.",
            ).toEqual([]);
        });

        await step(
            "Check right-clicking a cell opens no Cell menu",
            async () => {
                const menus = await rightClickCellExpectingNoCellMenu(
                    withoutExperiment,
                    0,
                    0,
                );
                expect(
                    {
                        cell: menus.cell,
                        row: menus.row,
                        column: menus.column,
                        table: menus.table,
                    },
                    "With the experiment off, none of the table's own menus should open.",
                ).toEqual({
                    cell: false,
                    row: false,
                    column: false,
                    table: false,
                });
                await closeAnyMenu(withoutExperiment);
            },
        );

        await step(
            "Check dragging a column boundary does nothing",
            async () => {
                await clickCell(withoutExperiment, 0, 0);
                await expectBoundaryDragDoesNothing(
                    withoutExperiment,
                    "column",
                    0,
                    -40,
                );
            },
        );

        await step(
            "Check the Canvas tool's palette offers no table",
            async () => {
                const offered = await getPaletteItemsOffered(withoutExperiment);
                expect(
                    offered,
                    `An experiment nobody has turned on should not advertise itself, so the palette ` +
                        `should not offer a table. It offers: ${offered.join(", ")}.`,
                ).not.toContain("table");
            },
        );

        await step(
            "Check the page layout controls offer no Table entry at all",
            async () => {
                await goToPage(withoutExperiment, textPage.id);
                await setChangeLayoutMode(withoutExperiment, true);
                await splitSection(withoutExperiment, "right", 0);
                const offer = await getTableSectionTypeOffer(
                    withoutExperiment,
                    1,
                );
                expect(
                    offer.offered,
                    `With the experiment off the Table entry should be gone, not dimmed: there ` +
                        `is no subscription to buy that would turn an experiment on. The section ` +
                        `offers: ${(await getSectionTypesOffered(withoutExperiment, 1)).join(", ")}.`,
                ).toBe(false);
                await setChangeLayoutMode(withoutExperiment, false);
            },
        );

        await step(
            "Check nothing offered to sell a subscription along the way",
            async () => {
                // Everything above was done at the Pro tier, so nothing in it had any reason to
                // mention a subscription. A dialog here would mean Bloom was treating an
                // experiment that is off as a feature the tier lacks.
                await expectNoSubscriptionDialog(
                    withoutExperiment,
                    "Working on a table with the experiment off",
                );
            },
        );

        await step("Leave Bloom on the Collection tab", async () => {
            await switchTab(withoutExperiment, "collection");
        });
    });
});
