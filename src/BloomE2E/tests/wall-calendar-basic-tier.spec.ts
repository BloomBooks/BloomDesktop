// A Wall Calendar in the hands of someone without a subscription: a collection on the Basic tier,
// with the "tables" experiment off. Part of Notion test case 818, "Wall Calendar: set up and edit
// the month grids"; wall-calendar.spec.ts covers the same calendar on the Pro tier.
//
// The month grids are bloom-tables, and tables are a Pro-tier feature behind the "tables"
// experiment. What that means for a calendar is a product decision, made on 2026-09-23 for
// BL-16777: the Wall Calendar works at every tier. Anyone can set one up in the Calendar Setup
// dialog, change a grid's month, year and first day of the week, type the names, and publish it to
// PDF. Only picture days need Pro and the experiment, because turning a day box into a picture is a
// change to the table's cells. A publishing block below Pro would be a bug.
//
// The tests are serial and share one book, made in the first.

import { expect, test } from "../fixtures/bloomTest";
import {
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    answerCalendarSetup,
    calendarGrid,
    calendarPageShown,
    chooseFromCalendarGridMenu,
    dayNumbersOf,
    getCalendarGridTableIndex,
    getCalendarSettings,
    getCalendarGrid,
    getDayCellPosition,
    getWeekdayName,
    monthDayNumbers,
    waitForCalendarGridLaidOut,
    waitForFirstMonthGridShown,
} from "../helpers/calendar";
import { getFeatureStatus } from "../helpers/collectionSettings";
import {
    getPublishDestinationsOffered,
    getPublishingBlockedNoticeText,
    isPublishingBlockedNoticeShowing,
    openPublishTab,
    publishDestinationList,
} from "../helpers/publish";
import { saveScreenshotIfAsked } from "../helpers/screenshot";
import {
    closeAnyMenu,
    rightClickCellExpectingNoCellMenu,
    typeInCell,
} from "../helpers/tables";
import { switchTab } from "../helpers/workspace";

/** The collection's own language: Haitian Creole, as in wall-calendar.spec.ts. */
const kLanguage = "hat";

test.use({
    // No subscription code, so the Basic tier; no experiments named, so "tables" is off.
    collectionSpec: {
        name: "wall-calendar-basic",
        languages: [kLanguage, "en"],
    },
    experimentalFeatures: [],
});

test.describe.configure({ mode: "serial" });

const kYear = 2027;
const kMonday = 1;
const kJanuary = 0;
const kFebruary = 1;
const kMarch = 2;
const kMondayName = "Lendi";

let gridPages: IBookPage[];

test.describe("a Wall Calendar on the Basic tier", () => {
    test("is set up with the Calendar Setup dialog [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        // This test also pays for launching Bloom and copying the 24-page template.
        test.setTimeout(360000);
        await step(
            "Check that this collection may not make tables",
            async () => {
                // So that everything below is known to happen without the table feature. `enabled` is
                // the tier and `visible` the experiment; both must be off for this file to mean
                // anything.
                const tableFeature = await getFeatureStatus(page, "table");
                expect(
                    {
                        enabled: tableFeature.enabled,
                        visible: tableFeature.visible,
                    },
                    `This file is about a collection with neither the Pro tier nor the "tables" ` +
                        `experiment. Bloom's answer: ${JSON.stringify(tableFeature)}`,
                ).toEqual({ enabled: false, visible: false });
            },
        );

        await step(
            "Make a Wall Calendar book and answer the dialog: 2027, Monday first",
            async () => {
                await makeBookFromTemplate(page, "Wall Calendar");
                await answerCalendarSetup(page, kYear, "Monday");
            },
        );

        await step(
            "Check that Bloom goes to January, laid out for 2027 with Monday first",
            async () => {
                gridPages = await waitForFirstMonthGridShown(page);
                const grid = await waitForCalendarGridLaidOut(page, {
                    year: kYear,
                    month: kJanuary,
                    firstDayOfWeek: kMonday,
                });
                // 1 January 2027 is a Friday, the fifth column of a Monday-first week.
                expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(4, 31));
                await saveScreenshotIfAsked(
                    [calendarPageShown(page)],
                    "23-basic-january-2027",
                );
            },
        );
    });

    test("the grid's button still changes the month [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("Choose March from the grid's menu", async () => {
            await chooseFromCalendarGridMenu(page, "January", "March");
            const grid = await waitForCalendarGridLaidOut(page, {
                year: kYear,
                month: kMarch,
                firstDayOfWeek: kMonday,
            });
            // 1 March 2027 is a Monday.
            expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(0, 31));
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "24-basic-march",
            );
        });

        await step("Put the grid back to January", async () => {
            await chooseFromCalendarGridMenu(page, "March", "January");
            await waitForCalendarGridLaidOut(page, {
                year: kYear,
                month: kJanuary,
                firstDayOfWeek: kMonday,
            });
        });
    });

    test("a weekday name typed on one month still fills in on another [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("On January, type the name of Monday", async () => {
            // Nothing has named Monday yet, so the name February shows later can only have come
            // from the typing below.
            expect(
                (await getCalendarSettings(page)).dayNames[kMonday] ?? "",
                "The collection already had a name for Monday before one was typed.",
            ).toBe("");
            expect(await getWeekdayName(page, kMonday, kLanguage)).toBe("");
            const grid = await getCalendarGrid(page);
            await typeInCell(
                page,
                0,
                grid.weekdayColumns.indexOf(kMonday),
                kLanguage,
                kMondayName,
                await getCalendarGridTableIndex(page),
            );
        });

        await step("Go to February: its Monday has the same name", async () => {
            await goToPage(page, gridPages[kFebruary].id);
            await waitForCalendarGridLaidOut(page, {
                year: kYear,
                month: kFebruary,
                firstDayOfWeek: kMonday,
            });
            expect(await getWeekdayName(page, kMonday, kLanguage)).toBe(
                kMondayName,
            );
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "24b-basic-february-lendi",
            );
        });
    });

    test("a day box offers no Cell menu, so no picture days [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("Right-click the box of 25 February", async () => {
            const { row, column } = await getDayCellPosition(page, 25);
            const menus = await rightClickCellExpectingNoCellMenu(
                page,
                row,
                column,
                await getCalendarGridTableIndex(page),
            );
            expect(
                menus.cell,
                "Changing a day box to a picture changes the table, which needs Pro and the " +
                    "experiment, so the Cell menu must not open.",
            ).toBe(false);
            await saveScreenshotIfAsked(
                [calendarGrid(page)],
                "25-basic-no-cell-menu",
            );
            await closeAnyMenu(page);
        });
    });

    test("the calendar can be published [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("Open the Publish tab", async () => {
            await openPublishTab(page);
        });

        await step(
            "Check that nothing blocks publishing, and PDF & Print is offered",
            async () => {
                expect(
                    await isPublishingBlockedNoticeShowing(page),
                    `A Wall Calendar may be published at every tier, but the Publish tab says: ` +
                        `"${await getPublishingBlockedNoticeText(page)}"`,
                ).toBe(false);
                expect(await getPublishDestinationsOffered(page)).toContain(
                    "PDF & Print",
                );
                await saveScreenshotIfAsked(
                    [publishDestinationList(page)],
                    "26-basic-publish",
                );
            },
        );

        await step("Go back to the Edit tab", async () => {
            await switchTab(page, "edit");
        });
    });
});
