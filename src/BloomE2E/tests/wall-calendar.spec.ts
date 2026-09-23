// Making a Wall Calendar and setting it up in the page: the "Calendar Setup" dialog a new calendar
// book asks, the month grids that lay themselves out, the round button over a grid and its menu,
// the names the collection learns, picture days, and a calendar grid on the canvas of any book.
// Automates Notion test case 818, "Wall Calendar: set up and edit the month grids".
//
// The month grids are bloom-tables, and tables are a Pro-tier feature behind the "tables"
// experiment, so the collection has both; tables-core.spec.ts explains how each is given. What a
// person below Pro, with the experiment off, may do with a calendar is in
// wall-calendar-basic-tier.spec.ts.
//
// The collection's own language is Haitian Creole, which the Wall Calendar template seeds no names
// for. In an English collection the English seed IS the box the user types in, so no name is ever
// missing and there is nothing for the collection to learn. The people this feature is for write
// calendars in languages like this one.
//
// The tests are serial and build on each other, in this order, because some of what they do cannot
// be undone:
//  1. Book A is made and its Calendar Setup is cancelled, so A stays unset for test 10.
//  2. Book B is made and set up for 2027 with Monday first. Tests 2 to 9 work in B.
//  9. Typing names teaches the collection, which test 10 relies on.
// 10. Book A is opened again: the dialog comes back, offering what the collection learned.
// 11. A Basic Book gets a calendar grid on its canvas.
// So a failure part way through leaves the later tests failing on setup, and the first failure is
// the one to read.
//
// Set BLOOM_E2E_SCREENSHOT_DIR to a folder to have the run save the pictures the Notion card shows
// under each verification (helpers/screenshot.ts, saveScreenshotIfAsked). Otherwise it saves none.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    answerCalendarSetup,
    calendarGrid,
    calendarPageShown,
    calendarSetupDialog,
    calendarSetupOkButton,
    chooseCalendarSetupFirstDayOfWeek,
    chooseFromCalendarGridMenu,
    clickCalendarSetupCancel,
    clickCalendarSetupOk,
    clickDayNumber,
    closeCalendarGridMenu,
    getCalendarGrid,
    getCalendarGridPages,
    getCalendarGridSubmenu,
    getCalendarGridTableIndex,
    getCalendarSettings,
    getDayCellPosition,
    getMonthName,
    getSmallTextBoxToolbar,
    getWeekdayName,
    isDayNoteTooSmallForInBoxAffordances,
    isShowNeighboringMonthDatesChecked,
    openCalendarGridMenu,
    openCellMenuFromSmallTextBoxToolbar,
    openMenuPanel,
    smallTextBoxToolbar,
    toggleShowNeighboringMonthDates,
    typeCalendarSetupYear,
    waitForCalendarGridLaidOut,
    waitForCalendarSetupDialog,
    waitForFirstMonthGridShown,
    monthDayNumbers,
    dayNumbersOf,
    type ICalendarGridState,
} from "../helpers/calendar";
import {
    dragPaletteItemOntoCanvas,
    getCanvasElementMenuLabels,
    closeCanvasElementMenu,
} from "../helpers/canvasElements";
import { selectBook } from "../helpers/collection";
import {
    getFeatureStatus,
    kEnterpriseSubscriptionCode,
} from "../helpers/collectionSettings";
import {
    closeFormatDialog,
    formatDialogPanel,
    getStyleShown,
    openFormatDialog,
    waitForFormatGear,
} from "../helpers/formatDialog";
import { chooseImageFile } from "../helpers/images";
import { saveScreenshotIfAsked } from "../helpers/screenshot";
import {
    cell,
    clickCell,
    closeAnyMenu,
    expectPictureInsideCell,
    getCellMenuContentTypes,
    getOpenCellMenuContentTypes,
    getOpenMenus,
    measureChrome,
    openCellMenu,
    selectTableElement,
    setCellContentType,
    typeInCell,
} from "../helpers/tables";
import { switchTab } from "../helpers/workspace";

/** The collection's own language: Haitian Creole, which the template seeds no names for. */
const kLanguage = "hat";

test.use({
    collectionSpec: {
        name: "wall-calendar",
        languages: [kLanguage, "en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test.describe.configure({ mode: "serial" });

/** The year book B is set up for. Not this year, so a grid that ignored the answer would show. */
const kYear = 2027;
const kSunday = 0;
const kMonday = 1;
const kJanuary = 0;
const kFebruary = 1;
const kMarch = 2;
const kNovember = 10;

/** The names typed in Haitian Creole, which the collection must learn. */
const kMondayName = "Lendi";
const kJanuaryName = "Janvye";
/** A note typed in one day box. */
const kDayNote = "Fèt";

/** The picture a day box gets. Small, and shipped with the suite. */
const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

/** The Calendar Setup dialog's title, from DistFiles/localization/en/BloomMediumPriority.xlf. */
const kSetupTitle = "Calendar Setup";
/** The grid menu's rows that are UI strings rather than the grid's own values. */
const kFirstDayRow = "First Day of Week";
const kNeighborsRow = "Show Neighboring Month Dates";

const kMonthNames = [
    "January",
    "February",
    "March",
    "April",
    "May",
    "June",
    "July",
    "August",
    "September",
    "October",
    "November",
    "December",
];

// Book A, which test 1 leaves unset and test 10 sets up; book B, which tests 2 to 9 work in.
let bookA: string;
let gridPagesOfB: IBookPage[];

test.describe("a Wall Calendar set up in the page", () => {
    test("cancelling the Calendar Setup dialog leaves the calendar unset [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        // This test also pays for launching Bloom and copying the 24-page template.
        test.setTimeout(360000);
        await step("Check that this collection may use tables", async () => {
            // The grids are tables. A missing gate here would show up later as grids that cannot
            // be changed, which would be mistaken for a broken calendar.
            const tableFeature = await getFeatureStatus(page, "table");
            expect(
                {
                    enabled: tableFeature.enabled,
                    visible: tableFeature.visible,
                },
                `The calendar grids are tables, which need the Pro tier (enabled) and the ` +
                    `"tables" experiment (visible). Bloom's answer: ${JSON.stringify(tableFeature)}`,
            ).toEqual({ enabled: true, visible: true });
        });

        await step("Make a book from the Wall Calendar template", async () => {
            bookA = await makeBookFromTemplate(page, "Wall Calendar");
        });

        await step(
            "Check that the Calendar Setup dialog comes up",
            async () => {
                const dialog = await waitForCalendarSetupDialog(page);
                // From June on the dialog offers next year, because that is the calendar people
                // are making then.
                const today = new Date();
                const offeredYear =
                    today.getMonth() >= 5
                        ? today.getFullYear() + 1
                        : today.getFullYear();
                expect(dialog).toEqual({
                    title: kSetupTitle,
                    year: String(offeredYear),
                    firstDayOfWeek: "Sunday",
                    okEnabled: true,
                });
                await saveScreenshotIfAsked(
                    [calendarSetupDialog(page)],
                    "01-setup-dialog",
                );
            },
        );

        await step("Check that OK waits for a four-digit year", async () => {
            await typeCalendarSetupYear(page, "20");
            await expect(
                calendarSetupOkButton(page),
                "OK should be disabled while the year is not four digits.",
            ).toBeDisabled();
            await saveScreenshotIfAsked(
                [calendarSetupDialog(page)],
                "02-setup-dialog-short-year",
            );
        });

        await step("Cancel the dialog", async () => {
            await clickCalendarSetupCancel(page);
        });

        await step("Check that January's grid has no days in it", async () => {
            const [january] = await getCalendarGridPages(page);
            await goToPage(page, january.id);
            const grid = await getCalendarGrid(page);
            expect(
                grid.laidOutFor,
                "A cancelled calendar should not have been laid out.",
            ).toBe(undefined);
            expect(
                dayNumbersOf(grid).filter((n) => n !== ""),
                "A cancelled calendar should show no day numbers.",
            ).toEqual([]);
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "03-unset-january",
            );
        });
    });

    test("OK lays January out for the chosen year and first day of the week [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        test.setTimeout(300000);
        await step(
            "Make a second book from the Wall Calendar template",
            async () => {
                // From the Collection tab, as a person does: asked from the Edit tab, Bloom makes
                // the book but the Edit tab never loads a page of it (see
                // makeBookFromBookInCollection in helpers/bookMaking.ts).
                await switchTab(page, "collection");
                await makeBookFromTemplate(page, "Wall Calendar");
            },
        );

        await step(
            "Answer the dialog: 2027, Monday first, then OK",
            async () => {
                await waitForCalendarSetupDialog(page);
                await typeCalendarSetupYear(page, String(kYear));
                await chooseCalendarSetupFirstDayOfWeek(page, "Monday");
                await saveScreenshotIfAsked(
                    [calendarSetupDialog(page)],
                    "04-setup-dialog-answered",
                );
                await clickCalendarSetupOk(page);
            },
        );

        await step(
            "Check that Bloom goes to January, laid out for 2027 with Monday first",
            async () => {
                gridPagesOfB = await waitForFirstMonthGridShown(page);
                expect(
                    gridPagesOfB.length,
                    "A Wall Calendar has twelve month grids.",
                ).toBe(12);
                const grid = await waitForCalendarGridLaidOut(page, {
                    year: kYear,
                    month: kJanuary,
                    firstDayOfWeek: kMonday,
                });
                // Monday first: the columns run Monday to Sunday.
                expect(grid.weekdayColumns).toEqual([1, 2, 3, 4, 5, 6, 0]);
                // 1 January 2027 is a Friday, the fifth column of a Monday-first week.
                expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(4, 31));
                await saveScreenshotIfAsked(
                    [calendarPageShown(page)],
                    "05-january-2027",
                );
            },
        );
    });

    test("each month grid lays itself out when it is opened [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("Go to February, and check it", async () => {
            await goToPage(page, gridPagesOfB[kFebruary].id);
            const grid = await waitForCalendarGridLaidOut(page, {
                year: kYear,
                month: kFebruary,
                firstDayOfWeek: kMonday,
            });
            // 1 February 2027 is a Monday, and 2027 is not a leap year: four full rows.
            expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(0, 28));
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "06-february-2027",
            );
        });

        await step("Go to November, and check it", async () => {
            await goToPage(page, gridPagesOfB[kNovember].id);
            const grid = await waitForCalendarGridLaidOut(page, {
                year: kYear,
                month: kNovember,
                firstDayOfWeek: kMonday,
            });
            // 1 November 2027 is a Monday as well; its 30 days take five rows.
            expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(0, 30));
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "06b-november-2027",
            );
        });
    });

    test("the grid's button changes its month, year and first day of the week [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("Go to January and open the grid's menu", async () => {
            await goToPage(page, gridPagesOfB[kJanuary].id);
            await waitForCalendarGridLaidOut(page, {
                year: kYear,
                month: kJanuary,
                firstDayOfWeek: kMonday,
            });
            expect(await openCalendarGridMenu(page)).toEqual([
                kFirstDayRow,
                "January",
                String(kYear),
                kNeighborsRow,
            ]);
            await saveScreenshotIfAsked(
                [calendarGrid(page), openMenuPanel(page)],
                "07-grid-menu",
            );
        });

        await step("Check the month submenu, then choose March", async () => {
            const months = await getCalendarGridSubmenu(page, "January");
            expect(months).toEqual({
                choices: kMonthNames,
                checked: ["January"],
            });
            await saveScreenshotIfAsked(
                [calendarGrid(page), openMenuPanel(page)],
                "08-month-submenu",
            );
            await chooseFromCalendarGridMenu(page, "January", "March");
            const grid = await waitForCalendarGridLaidOut(page, {
                year: kYear,
                month: kMarch,
                firstDayOfWeek: kMonday,
            });
            // 1 March 2027 is a Monday.
            expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(0, 31));
            expect((await openCalendarGridMenu(page))[1]).toBe("March");
            await closeCalendarGridMenu(page);
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "09-march-on-january-page",
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

        await step(
            "Choose 2028 from the year submenu, and back to 2027",
            async () => {
                await chooseFromCalendarGridMenu(
                    page,
                    String(kYear),
                    String(kYear + 1),
                );
                const grid = await waitForCalendarGridLaidOut(page, {
                    year: kYear + 1,
                    month: kJanuary,
                    firstDayOfWeek: kMonday,
                });
                // 1 January 2028 is a Saturday, the sixth column of a Monday-first week.
                expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(5, 31));
                await saveScreenshotIfAsked(
                    [calendarPageShown(page)],
                    "10-january-2028",
                );
                await chooseFromCalendarGridMenu(
                    page,
                    String(kYear + 1),
                    String(kYear),
                );
                await waitForCalendarGridLaidOut(page, {
                    year: kYear,
                    month: kJanuary,
                    firstDayOfWeek: kMonday,
                });
            },
        );

        await step(
            "Choose Sunday as the first day of the week, and back to Monday",
            async () => {
                await chooseFromCalendarGridMenu(page, kFirstDayRow, "Sunday");
                const grid = await waitForCalendarGridLaidOut(page, {
                    year: kYear,
                    month: kJanuary,
                    firstDayOfWeek: kSunday,
                });
                expect(grid.weekdayColumns).toEqual([0, 1, 2, 3, 4, 5, 6]);
                // Friday is the sixth column of a Sunday-first week.
                expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(5, 31));
                await saveScreenshotIfAsked(
                    [calendarPageShown(page)],
                    "11-january-sunday-first",
                );
                await chooseFromCalendarGridMenu(page, kFirstDayRow, "Monday");
                await waitForCalendarGridLaidOut(page, {
                    year: kYear,
                    month: kJanuary,
                    firstDayOfWeek: kMonday,
                });
            },
        );
    });

    test("Show Neighboring Month Dates fills the boxes outside the month [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("Turn Show Neighboring Month Dates on", async () => {
            await openCalendarGridMenu(page);
            expect(await isShowNeighboringMonthDatesChecked(page)).toBe(false);
            await toggleShowNeighboringMonthDates(page);
        });

        await step(
            "Check that the four boxes before 1 January show 28 to 31 December, faded",
            async () => {
                let grid: ICalendarGridState | undefined;
                await expect
                    .poll(async () => {
                        grid = await getCalendarGrid(page);
                        return dayNumbersOf(grid).slice(0, 5);
                    })
                    .toEqual(["28", "29", "30", "31", "1"]);
                expect(
                    grid!.days.slice(0, 5).map((d) => [d.neighbor, d.faded]),
                ).toEqual([
                    [true, true],
                    [true, true],
                    [true, true],
                    [true, true],
                    [false, false],
                ]);
                await saveScreenshotIfAsked(
                    [calendarPageShown(page)],
                    "12-neighboring-dates",
                );
                await openCalendarGridMenu(page);
                expect(await isShowNeighboringMonthDatesChecked(page)).toBe(
                    true,
                );
                await saveScreenshotIfAsked(
                    [calendarGrid(page), openMenuPanel(page)],
                    "12b-neighbors-checked",
                );
            },
        );

        await step("Turn it off again", async () => {
            await toggleShowNeighboringMonthDates(page);
            await expect
                .poll(async () => dayNumbersOf(await getCalendarGrid(page)))
                .toEqual(monthDayNumbers(4, 31));
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "12c-neighbors-off",
            );
        });
    });

    test("a day number takes the Format gear and has its own style [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step("Click the number of 3 January", async () => {
            await clickDayNumber(page, 3);
        });

        await step(
            "Check that the Format dialog shows the Calendar Day Number style",
            async () => {
                await waitForFormatGear(page);
                await saveScreenshotIfAsked(
                    [calendarGrid(page)],
                    "13a-day-number-gear",
                );
                await openFormatDialog(page);
                expect(await getStyleShown(page)).toBe("Calendar Day Number");
                await saveScreenshotIfAsked(
                    [formatDialogPanel(page)],
                    "13-day-number-format",
                );
                await closeFormatDialog(page);
            },
        );
    });

    test("a day box's note shows its language and format gear below the box [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        const tableIndex = await getCalendarGridTableIndex(page);
        const { row, column } = await getDayCellPosition(page, 15);

        await step("Type a note in the box of 15 January", async () => {
            await typeInCell(
                page,
                row,
                column,
                kLanguage,
                kDayNote,
                tableIndex,
            );
        });

        await step("Check the toolbar below the box", async () => {
            expect(
                await isDayNoteTooSmallForInBoxAffordances(page, 15),
                "A day box is too small for the language name and gear inside it.",
            ).toBe(true);
            const toolbar = await getSmallTextBoxToolbar(page);
            expect({
                showing: toolbar.showing,
                hasFormatButton: toolbar.hasFormatButton,
                hasCellMenuButton: toolbar.hasCellMenuButton,
            }).toEqual({
                showing: true,
                hasFormatButton: true,
                hasCellMenuButton: true,
            });
            expect(
                toolbar.languageName,
                "The toolbar should name the note's language.",
            ).not.toBe("");
            await saveScreenshotIfAsked(
                [
                    await cell(page, row, column, tableIndex),
                    smallTextBoxToolbar(page),
                ],
                "14-small-box-toolbar",
            );
        });

        await step(
            "Open the Cell menu from the toolbar's ... button",
            async () => {
                await openCellMenuFromSmallTextBoxToolbar(page);
                expect(await getOpenCellMenuContentTypes(page)).toEqual([
                    "text",
                    "image",
                ]);
                await saveScreenshotIfAsked(
                    [
                        await cell(page, row, column, tableIndex),
                        openCellMenu(page),
                    ],
                    "14b-cell-menu-from-toolbar",
                );
                await closeAnyMenu(page);
            },
        );
    });

    test("a day box can hold a picture and keeps its number [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        const tableIndex = await getCalendarGridTableIndex(page);
        const { row, column } = await getDayCellPosition(page, 25);

        await step(
            "Click the box of 25 January, and check it offers no row, column or table controls",
            async () => {
                await clickCell(page, row, column, tableIndex);
                expect(
                    (await measureChrome(page)).map((piece) => piece.name),
                    "A calendar grid's shape belongs to the calendar, so it offers no table controls.",
                ).toEqual([]);
                await saveScreenshotIfAsked(
                    [calendarGrid(page)],
                    "15a-day-selected-no-table-controls",
                );
            },
        );

        await step(
            "Right-click it: the Cell menu offers only text and picture",
            async () => {
                expect(
                    await getCellMenuContentTypes(
                        page,
                        row,
                        column,
                        tableIndex,
                    ),
                ).toEqual(["text", "image"]);
                const menus = await getOpenMenus(page);
                expect(menus, "Only the Cell menu should open.").toEqual({
                    cell: true,
                    row: false,
                    column: false,
                    table: false,
                    canvasElement: false,
                    bloomText: false,
                });
                await saveScreenshotIfAsked(
                    [
                        await cell(page, row, column, tableIndex),
                        openCellMenu(page),
                    ],
                    "15-day-cell-menu",
                );
                await closeAnyMenu(page);
            },
        );

        await step(
            "Make the box a picture and choose a picture for it",
            async () => {
                await setCellContentType(
                    page,
                    row,
                    column,
                    "image",
                    tableIndex,
                );
                await chooseImageFile(
                    page,
                    IMAGE_FILE,
                    await cell(page, row, column, tableIndex),
                );
                await expectPictureInsideCell(page, row, column, tableIndex);
                await saveScreenshotIfAsked(
                    [await cell(page, row, column, tableIndex)],
                    "15b-picture-chosen",
                );
            },
        );

        await step(
            "Check that the box still shows 25, after the page is opened again",
            async () => {
                // AUTOMATION-DEBT.md, "A picture in a cell leaves a drawing surface that swallows
                // every press": the rebuild is also a fair check that the picture day was saved.
                await reloadPageBeingEdited(page);
                await waitForCalendarGridLaidOut(page, {
                    year: kYear,
                    month: kJanuary,
                    firstDayOfWeek: kMonday,
                });
                expect(dayNumbersOf(await getCalendarGrid(page))).toEqual(
                    monthDayNumbers(4, 31),
                );
                await expectPictureInsideCell(page, row, column, tableIndex);
                await saveScreenshotIfAsked(
                    [await cell(page, row, column, tableIndex)],
                    "16-picture-day",
                );
            },
        );
    });

    test("names typed on one month fill in on the others, and the collection learns them [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        await step(
            "On January, type the name of Monday and the name of the month",
            async () => {
                // Nothing has named Monday or January yet, so a name found later can only have
                // come from the typing below, not from a seed or an earlier run.
                const before = await getCalendarSettings(page);
                expect(
                    [
                        before.dayNames[kMonday] ?? "",
                        before.monthNames[kJanuary] ?? "",
                    ],
                    "The collection already had names for Monday and January before any were typed.",
                ).toEqual(["", ""]);
                expect(await getWeekdayName(page, kMonday, kLanguage)).toBe("");
                const grid = await getCalendarGrid(page);
                const mondayColumn = grid.weekdayColumns.indexOf(kMonday);
                await typeInCell(
                    page,
                    0,
                    mondayColumn,
                    kLanguage,
                    kMondayName,
                    await getCalendarGridTableIndex(page),
                );
                await typeInGroup(
                    page,
                    ".calendarMonthName",
                    kLanguage,
                    kJanuaryName,
                );
                await saveScreenshotIfAsked(
                    [calendarPageShown(page)],
                    "17-names-typed-on-january",
                );
            },
        );

        await step(
            "Go to February: Monday has the name typed on January, the month does not",
            async () => {
                await goToPage(page, gridPagesOfB[kFebruary].id);
                await waitForCalendarGridLaidOut(page, {
                    year: kYear,
                    month: kFebruary,
                    firstDayOfWeek: kMonday,
                });
                expect(await getWeekdayName(page, kMonday, kLanguage)).toBe(
                    kMondayName,
                );
                expect(
                    await getMonthName(page, kLanguage),
                    "February's title is February's own, so January's name must not appear there.",
                ).toBe("");
                await saveScreenshotIfAsked(
                    [calendarPageShown(page)],
                    "18-february-has-monday",
                );
            },
        );

        await step(
            "Check that the collection remembers both names",
            async () => {
                await expect
                    .poll(async () => {
                        const settings = await getCalendarSettings(page);
                        return [
                            settings.dayNames[kMonday],
                            settings.monthNames[kJanuary],
                        ];
                    })
                    .toEqual([kMondayName, kJanuaryName]);
            },
        );
    });

    test("a calendar that was cancelled asks again, offering what the collection learned [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        test.setTimeout(240000);
        await step("Open the first calendar book again", async () => {
            await switchTab(page, "collection");
            await selectBook(page, bookA);
            await switchTab(page, "edit");
        });

        await step(
            "Check that the dialog comes back, with Monday first",
            async () => {
                const dialog = await waitForCalendarSetupDialog(page);
                expect(dialog.title).toBe(kSetupTitle);
                expect(
                    dialog.firstDayOfWeek,
                    "The collection remembers the first day chosen for book B.",
                ).toBe("Monday");
                await saveScreenshotIfAsked(
                    [calendarSetupDialog(page)],
                    "19-setup-dialog-again",
                );
            },
        );

        await step("Answer it with 2028 and check January", async () => {
            await answerCalendarSetup(page, kYear + 1, "Monday");
            await waitForFirstMonthGridShown(page);
            const grid = await waitForCalendarGridLaidOut(page, {
                year: kYear + 1,
                month: kJanuary,
                firstDayOfWeek: kMonday,
            });
            expect(dayNumbersOf(grid)).toEqual(monthDayNumbers(5, 31));
            expect(await getWeekdayName(page, kMonday, kLanguage)).toBe(
                kMondayName,
            );
            expect(await getMonthName(page, kLanguage)).toBe(kJanuaryName);
            await saveScreenshotIfAsked(
                [calendarPageShown(page)],
                "20-book-a-january-2028",
            );
        });
    });

    test("the Canvas tool puts a calendar grid for this month on any canvas [Test Case ID 818]", async ({
        page,
        step,
    }) => {
        test.setTimeout(240000);
        // Read once, so that both checks agree even if the run crosses midnight at a month's end.
        const today = new Date();
        const year = today.getFullYear();
        const month = today.getMonth();
        let canvasPage: IBookPage;
        await step("Make a Basic Book with a Canvas page", async () => {
            // From the Collection tab; see the note in the second test.
            await switchTab(page, "collection");
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Canvas");
            [canvasPage] = await getContentPages(page);
            await goToPage(page, canvasPage.id);
        });

        await step("Drag the Calendar icon onto the canvas", async () => {
            await dragPaletteItemOntoCanvas(page, "calendar");
        });

        await step(
            "Check the grid is this month, with Monday first as the collection says",
            async () => {
                const grid = await waitForCalendarGridLaidOut(page, {
                    year,
                    month,
                    firstDayOfWeek: kMonday,
                });
                const leading =
                    (new Date(year, month, 1).getDay() - kMonday + 7) % 7;
                const days = new Date(year, month + 1, 0).getDate();
                expect(dayNumbersOf(grid)).toEqual(
                    monthDayNumbers(leading, days),
                );
                await saveScreenshotIfAsked(
                    [calendarGrid(page)],
                    "21-canvas-calendar",
                );
            },
        );

        await step(
            "Check the grid's canvas menu has the calendar commands",
            async () => {
                await selectTableElement(
                    page,
                    await getCalendarGridTableIndex(page),
                );
                const labels = await getCanvasElementMenuLabels(page);
                expect(labels).toEqual(
                    expect.arrayContaining([
                        kFirstDayRow,
                        kMonthNames[month],
                        String(year),
                        kNeighborsRow,
                    ]),
                );
                await saveScreenshotIfAsked(
                    [calendarGrid(page), openMenuPanel(page)],
                    "22-canvas-calendar-menu",
                );
                await closeCanvasElementMenu(page);
            },
        );
    });
});
