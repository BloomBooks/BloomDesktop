// The answers of a Comprehension Quiz page are spaced the same in the PDF as in the Edit tab
// (BL-10019).
// Automates Test Case ID 524, "Quiz Paragraph Spacing", in the Notion test inventory.
//
// The PDF lays the page out again from the saved book, without the Edit tab's style sheets. In
// BL-10019 a rule meant to hide the language label of an answer in the Edit tab matched in the PDF
// too, where it added an empty line after each paragraph, so the answers drifted apart. The test
// fills three answers, one long enough to take two lines, and measures where each line of each
// answer starts down the page in the Edit tab and in the Simple PDF. The distance from one
// answer's first line to the next answer's, and between the two lines of the long answer, must be
// the same in both. Both are compared in millimetres: the Edit tab's page is scaled to the height
// of the PDF's paper.
//
// The PDF gives the baseline of each line, the Edit tab the top of its line box. The two differ
// by the same amount on every line of one font and size, so the distances between lines compare.
// That is why the question, which is larger, is not measured against the answers.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    choosePdfMode,
    getPdfPreviewPageBoxes,
    getPdfPreviewPageCount,
    getPdfPreviewPageDrawing,
    type IPdfTextRun,
} from "../helpers/pdfPrint";

test.use({
    collectionSpec: {
        name: "quiz-paragraph-spacing",
        languages: ["en"],
        // Games are a Pro feature: without a subscription Bloom leaves the quiz page out of the PDF.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The Add Page label of the quiz page in the Activity template. */
const kQuizPageLabel = "Comprehension Quiz";

/**
 * The translation group of a quiz page's part whose style is `style`. The template gives the group
 * the style's class, and Bloom puts it on the group's boxes too.
 */
function groupWithStyle(style: string): string {
    return `.bloom-translationGroup:is(.${style}, :has(> .${style}))`;
}
const kQuestionGroup = groupWithStyle("QuizQuestion-style");
const kAnswerGroup = groupWithStyle("QuizAnswer-style");

const kQuestion = "What did the moon take?";
/** The answers, each on one line but the second, which is long enough to take two. */
const kAnswers = [
    "His hat",
    "The cap that the little boy had left on the hill beside the river",
    "Nothing",
];

/** How far apart, in millimetres, the PDF and the Edit tab may put the same two lines. */
const kToleranceMm = 0.5;

/**
 * Where each line of each filled answer starts down the quiz page in the Edit tab: the top of its
 * line box, as a fraction of the page's height from its top. One list for each answer, in the
 * order of the answers.
 */
async function getEditAnswerLineTops(
    page: Page,
    answerCount: number,
): Promise<number[][]> {
    return editablePageFrame(page)
        .locator(".bloom-page")
        .evaluate((bloomPage, answerCount) => {
            const pageBox = bloomPage.getBoundingClientRect();
            const fromTop = (px: number) => (px - pageBox.top) / pageBox.height;
            const answers = Array.from(
                bloomPage.querySelectorAll(
                    ".checkbox-and-textbox-choice .bloom-editable.bloom-visibility-code-on",
                ),
            ).slice(0, answerCount);
            return answers.map((answer) => {
                const tops: number[] = [];
                const walker = document.createTreeWalker(
                    answer,
                    NodeFilter.SHOW_TEXT,
                );
                for (let n = walker.nextNode(); n; n = walker.nextNode()) {
                    if (!n.textContent?.trim()) continue;
                    const range = document.createRange();
                    range.selectNodeContents(n);
                    for (const rect of Array.from(range.getClientRects()))
                        if (!tops.some((t) => Math.abs(t - rect.top) < 1))
                            tops.push(rect.top);
                }
                return tops.sort((a, b) => a - b).map(fromTop);
            });
        }, answerCount);
}

/** A text without its spaces, which the PDF may draw as pieces of their own or leave out. */
function squeeze(text: string): string {
    return text.replace(/\s+/g, "");
}

/**
 * The pieces of text a PDF page draws, as lines from the top of the paper down: the pieces that
 * share a baseline, from left to right, with that baseline in millimetres from the top of the
 * paper. The PDF draws a line in many pieces, as small as one letter.
 */
function joinRunsIntoLines(
    runs: IPdfTextRun[],
    paperHeight: number,
): { text: string; baselineFromTop: number }[] {
    const lines: { runs: IPdfTextRun[]; y: number }[] = [];
    for (const run of runs) {
        const line = lines.find((l) => Math.abs(l.y - run.y) < 0.2);
        if (line) line.runs.push(run);
        else lines.push({ runs: [run], y: run.y });
    }
    return lines
        .sort((a, b) => b.y - a.y)
        .map((line) => ({
            text: line.runs
                .sort((a, b) => a.x - b.x)
                .map((run) => run.text)
                .join(""),
            baselineFromTop: paperHeight - line.y,
        }));
}

/** The distances between consecutive numbers of a list. */
function steps(values: number[]): number[] {
    return values.slice(1).map((value, i) => value - values[i]);
}

test("quiz answers are spaced the same in the PDF as in the Edit tab [Test Case ID 524]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);
    const page = bloomApp.page;

    await step("Make a book with a filled quiz page", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, kQuizPageLabel, 1);
        const [quizPage] = await getContentPages(page);
        await goToPage(page, quizPage.id);
        await typeInGroup(page, kQuestionGroup, "en", kQuestion);
        for (const [index, answer] of kAnswers.entries())
            await typeInGroup(page, kAnswerGroup, "en", answer, index);
        // An answer with the focus shows its language label, which only the Edit tab draws; with
        // the focus in the question, none of the answers does.
        await clickInGroup(page, kQuestionGroup, "en");
        await waitForSnapshotStreamIdle(page);
    });

    const editTops = await step("Measure the answers in the Edit tab", () =>
        getEditAnswerLineTops(page, kAnswers.length),
    );
    if (!editTops) throw new Error("The answers were not measured.");

    await step("Compare the answers in the Simple PDF", async () => {
        await choosePdfMode(page, "Simple");
        const [{ paper }] = await getPdfPreviewPageBoxes(page);
        const paperHeight = paper.top - paper.bottom;
        // The Edit tab's page is the book's page, which the PDF's paper is the size of.
        const edit = editTops.map((tops) => tops.map((t) => t * paperHeight));

        const drawings = [];
        for (let i = 0; i < (await getPdfPreviewPageCount(page)); i++)
            drawings.push(await getPdfPreviewPageDrawing(page, i));
        const quizPages = drawings.filter((drawing) =>
            squeeze(drawing.texts.map((run) => run.text).join("")).includes(
                squeeze(kQuestion),
            ),
        );
        expect(
            quizPages.length,
            "The PDF should draw the quiz question on one page.",
        ).toBe(1);
        const lines = joinRunsIntoLines(quizPages[0].texts, paperHeight);
        // Where each line of each answer starts down the paper, from its top, in millimetres: the
        // run of consecutive lines whose words are the answer's.
        const pdf = kAnswers.map((answer) => {
            for (let first = 0; first < lines.length; first++)
                for (let last = first; last < lines.length; last++) {
                    const words = squeeze(
                        lines
                            .slice(first, last + 1)
                            .map((line) => line.text)
                            .join(""),
                    );
                    if (words === squeeze(answer))
                        return lines
                            .slice(first, last + 1)
                            .map((line) => line.baselineFromTop);
                    if (!squeeze(answer).startsWith(words)) break;
                }
            throw new Error(
                `The PDF does not draw the answer "${answer}" on lines of its own. Its lines: ` +
                    JSON.stringify(lines.map((line) => line.text)),
            );
        });

        expect(
            pdf.map((lines) => lines.length),
            "Each answer should take as many lines in the PDF as in the Edit tab.",
        ).toEqual(edit.map((lines) => lines.length));
        expect(
            edit[1].length,
            "The second answer should take two lines, so the spacing of lines within an answer is measured.",
        ).toBe(2);

        const editFirstLines = edit.map((lines) => lines[0]);
        const pdfFirstLines = pdf.map((lines) => lines[0]);
        const editGaps = steps(editFirstLines);
        const pdfGaps = steps(pdfFirstLines);
        for (const [index, gap] of pdfGaps.entries())
            expect(
                Math.abs(gap - editGaps[index]),
                `The PDF should put answer ${index + 2} as far below answer ${index + 1} as the ` +
                    `Edit tab does: ${editGaps[index].toFixed(2)}mm in the Edit tab, ` +
                    `${gap.toFixed(2)}mm in the PDF (BL-10019).`,
            ).toBeLessThan(kToleranceMm);
        const editLinePitch = steps(edit[1])[0];
        const pdfLinePitch = steps(pdf[1])[0];
        expect(
            Math.abs(pdfLinePitch - editLinePitch),
            `The PDF should space the two lines of the long answer as the Edit tab does: ` +
                `${editLinePitch.toFixed(2)}mm in the Edit tab, ${pdfLinePitch.toFixed(2)}mm in the PDF.`,
        ).toBeLessThan(kToleranceMm);
    });
});
