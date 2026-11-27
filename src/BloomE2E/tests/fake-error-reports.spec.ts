// Bloom's error reporting, driven by its fake test errors. When the Edit tab shows a book whose title
// is one of a few special ones, Bloom raises a made-up problem of a matching kind
// (ErrorReportUtils.CheckForFakeTestErrors), in a Debug build or with the BloomSandbox environment
// variable set. The manual case gets those titles from the "Fake Error Report Collection"; the
// books in it matter only for their titles, so this test gives a book each title in turn.
// launchBloom sets BloomSandbox for every e2e run, so the Release build that the suite runs on
// raises them too.
//
// Each dialog's kind, title, message and buttons are checked through the DOM. Not checked: how the
// dialogs look against the case's screenshots (BL-12226, an extra scroll bar, is a layout matter);
// Submit, which an automated run must never press, since it sends a report to Bloom's servers; and
// a notice's Report and extra buttons ("The fake notices' Report and extra buttons are not
// pressed" in AUTOMATION-DEBT.md).

import type { Page } from "@playwright/test";
import type { IProblemDialogWatcher } from "../fixtures/problemDialogWatcher";
import { expect, test, type IBloomApp } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import {
    editablePageFrame,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    doThroughOneProblemDialog,
    type IProblemDialogButton,
    type IProblemDialogShown,
} from "../helpers/problemDialogs";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "fake-error-reports", languages: ["en"] },
});

// The message of every fake problem (fakeProblemMessage in ErrorReportUtils.cs), and of the
// exception that some of them report (GenerateFakeException). Bloom does not localize either.
const kFakeProblem = "Fake problem for development/testing purposes";
const kFakeException = "Fake exception for development/testing purposes";

// The two long messages, built as ErrorReportUtils.cs builds them, as the dialog reads them with
// runs of white space made one space.
function repeatedUntil(minimumLength: number, separatorLength: number) {
    const pieces: string[] = [];
    while (
        pieces.length * (kFakeProblem.length + separatorLength) <
        minimumLength
    )
        pieces.push(kFakeProblem);
    return pieces.join(" ");
}
// Appended with a space after each, until the text is at least 3000 characters long.
const kLongMessage = repeatedUntil(3000, 1);
// Appended as lines (each ends in "\r\n"), until the text is at least 500 characters long.
const kScrollbarMessage = repeatedUntil(500, 2);

// Buttons, as describeButtons names them.
const kClose = "Common.Close";
const kReport = "ErrorReport.Report";
const kSubmit = "ReportProblemDialog.SubmitButton";
const kQuit = "ReportProblemDialog.Quit";

/**
 * Name each button by its data-testid (a BloomButton's l10n key), which says which button it is in
 * any UI language. A button with none carries a label its caller gave, which Bloom does not
 * localize, so it is named by that label (and its id, when it has one).
 */
function describeButtons(buttons: IProblemDialogButton[]): string[] {
    return buttons.map(
        (b) => b.testId || (b.id ? `#${b.id} "${b.text}"` : `"${b.text}"`),
    );
}

interface IExpectedDialog {
    kind: IProblemDialogShown["kind"];
    level: "nonfatal" | "fatal";
    message: string;
    buttons: string[];
}

/** The title bars of the two levels of problem. */
type IDialogTitles = Record<IExpectedDialog["level"], string>;

/**
 * Put Bloom's UI in English, and return the title bars of the two levels of problem. In another UI
 * language a dialog shows its English title until the translation arrives, so a check of what it
 * showed could pass or fail on how quickly that came.
 */
async function pinEnglishAndReadDialogTitles(
    bloomApp: IBloomApp,
): Promise<IDialogTitles> {
    await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;
    return {
        nonfatal: await getUiString(
            page,
            "ReportProblemDialog.NonFatalTitle",
            "Bloom had a problem",
        ),
        fatal: await getUiString(
            page,
            "ReportProblemDialog.FatalTitle",
            "Bloom encountered an error and needs to quit",
        ),
    };
}

function expectDialog(
    titles: IDialogTitles,
    title: string,
    shown: IProblemDialogShown,
    expected: IExpectedDialog,
): void {
    const dialogTitle = titles[expected.level];
    expect(
        {
            kind: shown.kind,
            title: shown.title,
            message: shown.message,
            buttons: describeButtons(shown.buttons),
            buttonsInsideWindow: shown.buttonsInsideWindow,
        },
        `the dialog that the book "${title}" raises`,
    ).toEqual({
        kind: expected.kind,
        title: dialogTitle,
        message: expected.message,
        buttons: expected.buttons,
        buttonsInsideWindow: true,
    });
}

/**
 * Give the book being edited a title, on its cover, and show it in the Edit tab again from the
 * Collections tab, the way a person opens a book: Bloom raises a fake problem as the Edit tab shows
 * a book with one of the special titles. Returns what the one problem dialog showed, once it has
 * been closed. Unless Bloom quits (pass the process id of a Bloom that should), waits until the Edit
 * tab shows the book's page again, which Bloom does only once closing the dialog was all.
 */
async function openBookTitled(
    page: Page,
    problemDialogWatcher: IProblemDialogWatcher,
    title: string,
    bloomQuitsWithPid?: number,
): Promise<IProblemDialogShown> {
    await waitForEditablePage(page);
    // At the nightly's window size the cover title can lie under the suite's caption.
    await editablePageFrame(page)
        .locator(".bookTitle")
        .first()
        .evaluate((e) => e.scrollIntoView({ block: "center" }));
    await typeInGroup(page, ".bookTitle", "en", title);
    await switchTab(page, "collection");
    const shown = await doThroughOneProblemDialog(
        page,
        problemDialogWatcher,
        () => switchTab(page, "edit"),
        bloomQuitsWithPid,
    );
    if (!bloomQuitsWithPid) await waitForEditablePage(page);
    return shown;
}

async function makeBook(page: Page): Promise<void> {
    await switchTab(page, "collection");
    await makeBookFromTemplate(page, "Basic Book");
}

test("books with the fake notify titles show their notices, with the buttons each was given [Test Case ID 52]", async ({
    bloomApp,
    problemDialogWatcher,
}) => {
    test.setTimeout(300000);
    const titles = await pinEnglishAndReadDialogTitles(bloomApp);
    // Read after the UI language is pinned: changing it gives Bloom a new shell page.
    const page = bloomApp.page;
    await makeBook(page);
    const cases: [string, string, string[]][] = [
        ["Error NotifyUser NoReport", kFakeProblem, [kClose]],
        ["Error NotifyUser NoReport 2", kFakeProblem, [kClose]],
        ["Error NotifyUser NoReport 3", kFakeProblem, [kClose]],
        ["Error NotifyUser LongMessage", kLongMessage, [kClose]],
        ["Error NotifyUser Report NoRetry", kFakeProblem, [kClose, kReport]],
        ["Error NotifyUser Report NoRetry 2", kFakeProblem, [kClose, kReport]],
        [
            "Error NotifyUser Report Retry",
            kFakeProblem,
            ['"Retry"', kClose, kReport],
        ],
        // The legacy interface is given its own label for the Report button.
        [
            "Error NotifyUser LegacyInterface",
            kFakeProblem,
            [kClose, '#errorReportButton "CustomReport"'],
        ],
    ];
    for (const [title, message, buttons] of cases) {
        await test.step(title, async () => {
            const shown = await openBookTitled(
                page,
                problemDialogWatcher,
                title,
            );
            expectDialog(titles, title, shown, {
                kind: "notify",
                level: "nonfatal",
                message,
                buttons,
            });
        });
    }
});

test("books with the fake non-fatal report titles show the problem report dialog [Test Case ID 52]", async ({
    bloomApp,
    problemDialogWatcher,
}) => {
    test.setTimeout(300000);
    const titles = await pinEnglishAndReadDialogTitles(bloomApp);
    // Read after the UI language is pinned: changing it gives Bloom a new shell page.
    const page = bloomApp.page;
    await makeBook(page);
    const cases: [string, string][] = [
        // With no message of its own, the dialog says what the exception says.
        ["Error ReportNonFatalException", kFakeException],
        ["Error ReportNonFatalExceptionWithMessage", kFakeProblem],
        [
            "Error ReportNonFatalExceptionWithMessage Scrollbar",
            kScrollbarMessage,
        ],
        ["Error ReportNonFatalMessageWithStackTrace", kFakeProblem],
    ];
    for (const [title, message] of cases) {
        await test.step(title, async () => {
            const shown = await openBookTitled(
                page,
                problemDialogWatcher,
                title,
            );
            expectDialog(titles, title, shown, {
                kind: "report",
                level: "nonfatal",
                message,
                buttons: [kSubmit, kClose],
            });
        });
    }
});

test("books with the fake fatal titles show the fatal report dialog, and Bloom quits [Test Case ID 52]", async ({
    bloomApp,
    problemDialogWatcher,
}) => {
    test.setTimeout(480000);
    const titles = await pinEnglishAndReadDialogTitles(bloomApp);
    const cases: [string, string][] = [
        ["Error ReportFatalException", kFakeException],
        ["Error ReportFatalMessageWithStackTrace", kFakeProblem],
        ["Error ReportFatalMessageWithStackTrace Scrollbar", kScrollbarMessage],
    ];
    for (const [title, message] of cases) {
        await test.step(title, async () => {
            // A new book each time: the one before keeps its title, and showing it in the Edit
            // tab would raise its fatal problem again.
            const page = bloomApp.page;
            await makeBook(page);
            const shown = await openBookTitled(
                page,
                problemDialogWatcher,
                title,
                bloomApp.bloomPid,
            );
            expectDialog(titles, title, shown, {
                kind: "report",
                level: "fatal",
                message,
                buttons: [kSubmit, kQuit],
            });
            await bloomApp.restart();
        });
    }
});
