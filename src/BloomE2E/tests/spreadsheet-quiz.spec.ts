// Comprehension Quiz pages survive a trip through a spreadsheet. A book with two quiz pages is
// exported with More > Export to Spreadsheet... and imported with More > Import Content from
// Spreadsheet... into a new blank book, which gets both quiz pages with their questions, answers and
// correct answers, and a "Check Your Understanding" heading with the same translations as the
// original's, and no more. BL-15680 was an import that gave the heading its translations again on
// top of the ones it had, so that the page's source bubble showed a crowd of tabs and Bloom became
// too slow to leave the page; so the test also shows each imported quiz page in the Edit tab and
// leaves it.
//
// Then, as the case does, it deletes the second quiz page's rows from the spreadsheet and imports
// it into the quiz book: the second page goes, and the first keeps its question, answers and
// correct answer.
//
// Automates the manual test "Spreadsheet Quiz Handling" (Test Case ID 638).
//
// Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to the
// front, under --dont-disturb too, so the test does not run where it would take a developer's
// keyboard; see "Export to Spreadsheet opens the spreadsheet" in AUTOMATION-DEBT.md. The test has
// no spreadsheet program to drive, so it deletes the rows by rewriting the file
// (helpers/spreadsheetEditing.ts). It exports only once: with Excel, a second export in the same
// session waits for ever for Excel to open it while Excel still has the first.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    editBook,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    findBookFolderById,
    readBookId,
    selectBook,
} from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { realClick } from "../helpers/realClick";
import {
    exportSelectedBookToSpreadsheet,
    importSpreadsheetIntoSelectedBook,
    type ISpreadsheetProgress,
} from "../helpers/spreadsheet";
import { deleteSpreadsheetPageRows } from "../helpers/spreadsheetEditing";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "spreadsheet-quiz",
        languages: ["en"],
        // The spreadsheet commands need a Local Community subscription or better.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The Add Page label of the quiz page in the Activity template. */
const kQuizPageLabel = "Comprehension Quiz";

/**
 * The translation group of a quiz page's part whose style is `style`. The template gives the group
 * the style's class, and Bloom puts it on the group's boxes too, so the group is the one that has
 * the class or holds a box that has it.
 */
function groupWithStyle(style: string): string {
    return `.bloom-translationGroup:is(.${style}, :has(> .${style}))`;
}
const kQuestionGroup = groupWithStyle("QuizQuestion-style");
const kAnswerGroup = groupWithStyle("QuizAnswer-style");
const kHeadingGroup = groupWithStyle("GameHeader-style");

/** What the test types on one quiz page, and which answer it marks correct. */
interface IQuizToMake {
    question: string;
    answers: string[];
    correct: number;
}

/**
 * The two quiz pages. Each has its own correct answer, at a different place, so answers or marks
 * that end up on the wrong page show.
 */
const kQuizzes: IQuizToMake[] = [
    {
        question: "Where did the cat sleep?",
        answers: ["On the roof", "Under the bed", "In the basket"],
        correct: 2,
    },
    {
        question: "What did the dog eat?",
        answers: ["A bone", "An apple", "Some rice", "Nothing"],
        correct: 0,
    },
];

/** One quiz page as the saved book has it. */
interface IQuizOnDisk {
    id: string;
    /** The page's number, which the spreadsheet gives as the page of each of its rows. */
    pageNumber: number;
    /** Whether the page is still a quiz page: its class and its activity. */
    isQuizPage: boolean;
    question: string;
    /** The English text of each answer that has some, in order. */
    answers: string[];
    /** The English text of each answer marked correct. */
    correctAnswers: string[];
    /**
     * Each text of the "Check Your Understanding" heading, as "language: text", sorted: the order
     * of a group's boxes means nothing, and the import writes them in another. Bloom fills the
     * heading with every translation it has when the page is added.
     */
    heading: string[];
}

/**
 * The content pages of the book in `bookFolder` (front and back matter left out), as its saved
 * file has them. `page` is used only as a DOM parser.
 */
async function readQuizPages(
    page: Page,
    bookFolder: string,
): Promise<IQuizOnDisk[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, kQuestionGroup, kAnswerGroup, kHeadingGroup }) => {
            /** The English box text of `group`, with the markup's spacing tidied. */
            const textOf = (group: Element) =>
                (
                    group.querySelector('.bloom-editable[lang="en"]')
                        ?.textContent ?? ""
                )
                    .replace(/[ \t\r\n\f]+/g, " ")
                    .trim();
            const dom = new DOMParser().parseFromString(source, "text/html");
            return Array.from(
                dom.querySelectorAll(
                    ".bloom-page:not(.bloom-frontMatter):not(.bloom-backMatter)",
                ),
            ).map((p) => {
                const choices = Array.from(
                    p.querySelectorAll(".checkbox-and-textbox-choice"),
                );
                const answerOf = (choice: Element) => {
                    const group = choice.querySelector(kAnswerGroup);
                    return group ? textOf(group) : "";
                };
                const question = p.querySelector(kQuestionGroup);
                const heading = p.querySelector(kHeadingGroup);
                return {
                    id: p.id,
                    pageNumber: Number(p.getAttribute("data-page-number")),
                    isQuizPage:
                        p.classList.contains("simple-comprehension-quiz") &&
                        p.getAttribute("data-activity") ===
                            "simple-checkbox-quiz",
                    question: question ? textOf(question) : "",
                    answers: choices.map(answerOf).filter((a) => a !== ""),
                    correctAnswers: choices
                        .filter((c) => c.classList.contains("correct-answer"))
                        .map(answerOf),
                    heading: heading
                        ? Array.from(
                              heading.querySelectorAll(".bloom-editable"),
                          )
                              // "z" is the template box Bloom copies for each language; "*" and
                              // "" hold no text of the book.
                              .filter(
                                  (box) =>
                                      !["z", "*", ""].includes(
                                          box.getAttribute("lang") ?? "",
                                      ),
                              )
                              .map(
                                  (box) =>
                                      `${box.getAttribute("lang")}: ${(box.textContent ?? "").trim()}`,
                              )
                              .sort()
                        : [],
                };
            });
        },
        { source: html, kQuestionGroup, kAnswerGroup, kHeadingGroup },
    );
}

/** The quiz page as `kQuizzes` says it should be saved, apart from its id and heading. */
function expectedQuiz(quiz: IQuizToMake) {
    return {
        isQuizPage: true,
        question: quiz.question,
        answers: quiz.answers,
        correctAnswers: [quiz.answers[quiz.correct]],
    };
}

/** The parts of a saved quiz page that `expectedQuiz` gives. */
function quizContent(quiz: IQuizOnDisk) {
    return {
        isQuizPage: quiz.isQuizPage,
        question: quiz.question,
        answers: quiz.answers,
        correctAnswers: quiz.correctAnswers,
    };
}

/**
 * On the quiz page being shown, type the question and the answers, and tick the correct answer's
 * box, as a person does.
 */
async function fillQuizPage(page: Page, quiz: IQuizToMake): Promise<void> {
    await typeInGroup(page, kQuestionGroup, "en", quiz.question);
    for (const [index, answer] of quiz.answers.entries())
        await typeInGroup(page, kAnswerGroup, "en", answer, index);
    const choice = editablePageFrame(page)
        .locator(".checkbox-and-textbox-choice")
        .nth(quiz.correct);
    await realClick(choice.locator("input.styled-check-box"));
    await expect(
        choice,
        `"${quiz.answers[quiz.correct]}" should be marked as the correct answer`,
    ).toHaveClass(/\bcorrect-answer\b/);
    await waitForSnapshotStreamIdle(page);
}

/**
 * Fail unless the imported heading `imported` has the same text in each language as the
 * original's heading `original`, and each language once. BL-15680's import gave a heading each of
 * its translations a second time.
 *
 * The spreadsheet has a column per language, so the import gives a language one box even where
 * the original has two (BL-17212 gives a new heading two "qaa" boxes); so the original's texts are
 * compared without their repeats.
 */
function expectHeadingImported(
    imported: string[],
    original: string[],
    where: string,
): void {
    const languages = imported.map((text) => text.split(":")[0]);
    expect(
        languages.filter((lang, index) => languages.indexOf(lang) !== index),
        `the heading of ${where} should have each language once, as the spreadsheet does (BL-15680)`,
    ).toEqual([]);
    expect(
        imported,
        `the heading of ${where} should have the original's texts`,
    ).toEqual(Array.from(new Set(original)));
}

/**
 * Import the spreadsheet at `spreadsheet` into the selected book, and fail if the import reported
 * a problem in its progress box, or Bloom reported one while it ran.
 *
 * Problems Bloom reported before the import are dropped first: showing a quiz page in the Edit tab
 * reports a missing "tp" string, because the page's heading has a box in that made-up language
 * (BL-17212), which has nothing to do with spreadsheets.
 */
async function importWithoutProblems(
    bloomApp: { page: Page; takeNonFatalProblems: () => string[] },
    spreadsheet: string,
    what: string,
): Promise<void> {
    bloomApp.takeNonFatalProblems();
    const progress: ISpreadsheetProgress =
        await importSpreadsheetIntoSelectedBook(bloomApp.page, spreadsheet);
    expect(
        [...progress.errors, ...progress.warnings],
        `${what} said:\n${progress.messages.join("\n")}`,
    ).toEqual([]);
    expect(
        bloomApp.takeNonFatalProblems(),
        `Bloom should report no problem during ${what.toLowerCase()}`,
    ).toEqual([]);
}

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

test("Comprehension Quiz pages survive export to a spreadsheet and import [Test Case ID 638]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;
    const exportFolder = testInfo.outputPath("spreadsheets");
    fs.mkdirSync(exportFolder, { recursive: true });

    const quizBookId = await step(
        "Make a book with two Comprehension Quiz pages",
        async () => {
            // The title renames the book's folder, so the book is known by its id.
            const bookId = readBookId(
                await makeBookFromTemplate(page, "Basic Book"),
            );
            await typeInGroup(page, ".bookTitle", "en", "Quiz Book");
            for (const [index, quiz] of kQuizzes.entries()) {
                // A page is added after the page being shown: the cover, then the quiz before.
                await addPage(page, kQuizPageLabel, 1);
                const quizPages = await getContentPages(page);
                expect(
                    quizPages.length,
                    `the book should have ${index + 1} quiz page(s)`,
                ).toBe(index + 1);
                await goToPage(page, quizPages[index].id);
                await fillQuizPage(page, quiz);
            }
            // Leaving the Edit tab saves the book.
            await switchTab(page, "collection");
            return bookId;
        },
    );
    if (!quizBookId) throw new Error("The book was not made.");
    const quizBook = () =>
        findBookFolderById(bloomApp.collectionDir, quizBookId);
    const original = await readQuizPages(page, quizBook());

    await step("Check the saved book has both quiz pages", () => {
        // Without this the round trip below could pass on pages that never had the content.
        expect(original.map(quizContent)).toEqual(kQuizzes.map(expectedQuiz));
        for (const [index, quiz] of original.entries()) {
            expect(
                quiz.heading,
                `quiz page ${index + 1} should have its English heading`,
            ).toContain("en: Check Your Understanding");
            // The heading's translations are what BL-15680's import added a second time, so the
            // check of the imported headings below needs some to be there: boxes in other
            // languages, and some of them translated.
            expect(
                new Set(quiz.heading.map((text) => text.split(":")[0])).size,
                `quiz page ${index + 1}'s heading should have boxes in other languages than English`,
            ).toBeGreaterThan(1);
            expect(
                quiz.heading.filter(
                    (text) => !text.endsWith(": Check Your Understanding"),
                ),
                `quiz page ${index + 1}'s heading should have some translated texts`,
            ).not.toEqual([]);
        }
    });

    const bothPages = await step("Export it to a spreadsheet", async () => {
        await selectBook(page, quizBook());
        return exportSelectedBookToSpreadsheet(page, exportFolder);
    });
    if (!bothPages) throw new Error("The book was not exported.");

    const newBookId = await step("Make a new blank book", async () => {
        return readBookId(await makeBookFromTemplate(page, "Basic Book"));
    });
    if (!newBookId) throw new Error("No new book was made.");
    const newBook = () => findBookFolderById(bloomApp.collectionDir, newBookId);

    await step("Import the spreadsheet into the new book", async () => {
        await switchTab(page, "collection");
        await selectBook(page, newBook());
        await importWithoutProblems(
            bloomApp,
            bothPages,
            "The import into the new book",
        );
    });

    await step(
        "Check the new book has both quiz pages, with the original's heading texts and no more",
        async () => {
            const imported = await readQuizPages(page, newBook());
            expect(imported.map(quizContent)).toEqual(
                kQuizzes.map(expectedQuiz),
            );
            for (const [index, quiz] of imported.entries())
                expectHeadingImported(
                    quiz.heading,
                    original[index].heading,
                    `imported quiz page ${index + 1}`,
                );
        },
    );

    await step(
        "Show each imported quiz page in the Edit tab and leave it",
        async () => {
            await editBook(page, newBook());
            const quizPageIds = (await readQuizPages(page, newBook())).map(
                (quiz) => quiz.id,
            );
            const cover = (await getPages(page))[0];
            for (const id of quizPageIds) {
                // goToPage fails when the Edit tab does not show the page within a minute, which a
                // page that makes Bloom unresponsive (BL-15680) cannot do.
                await goToPage(page, id);
                await expect(
                    editablePageFrame(page).locator(kQuestionGroup),
                    "the imported quiz page should show its question",
                ).toBeVisible();
                await goToPage(page, cover.id);
            }
            await switchTab(page, "collection");
        },
    );

    const firstPageOnly = Path.join(
        Path.dirname(bothPages),
        "Quiz Book without the second quiz.xlsx",
    );
    await step(
        "Delete the second quiz page's rows from the spreadsheet",
        () => {
            expect(
                deleteSpreadsheetPageRows(
                    bothPages,
                    original[1].pageNumber,
                    firstPageOnly,
                ),
                "the second quiz page's rows should be its heading, question and six answers",
            ).toBe(8);
        },
    );

    await step(
        "Import the spreadsheet without the second quiz page into the quiz book",
        async () => {
            await selectBook(page, quizBook());
            await importWithoutProblems(
                bloomApp,
                firstPageOnly,
                "The import into the quiz book",
            );
        },
    );

    await step(
        "Check the quiz book lost its second quiz page and kept its first",
        async () => {
            const after = await readQuizPages(page, quizBook());
            expect(
                after.map(quizContent),
                "the quiz book should have only its first quiz page, as it was",
            ).toEqual([expectedQuiz(kQuizzes[0])]);
            expect(
                after[0].id,
                "the remaining quiz page should be the book's own first quiz page, kept, not a new one",
            ).toBe(original[0].id);
            // The import fills the boxes the page already has, so the heading keeps exactly the
            // ones it had, BL-17212's second "qaa" box included, and gains none (BL-15680).
            expect(
                after[0].heading,
                "the remaining quiz page's heading should keep its texts, and gain none",
            ).toEqual(original[0].heading);
        },
    );
});
