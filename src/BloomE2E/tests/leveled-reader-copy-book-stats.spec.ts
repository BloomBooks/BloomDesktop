// The Leveled Reader's COPY BOOK STATS button, at the bottom of its panel, puts the book's
// statistics on the clipboard as two tab-separated rows, a header and the book's figures, ready to
// paste into a spreadsheet (ReadersApi.HandleCopyBookStatsToClipboard). Once, clicking it copied
// nothing (BL-10928).
//
// Automates the manual test "Copy Book Stats" (Test Case ID 469): click COPY BOOK STATS (bottom of
// the Leveled Reader toolbox) for a leveled book. The case used the book attached to BL-10928;
// this test makes a leveled book of its own with two pages of text whose figures are worked out
// below, so that the averages over pages are not just the one page's figures.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import {
    addPage,
    getContentPages,
    goToPage,
    pasteTextInGroup,
} from "../helpers/bookMaking";
import { getClipboard } from "../helpers/clipboard";
import {
    getReaderPhase,
    leveledReaderStat,
    makeLeveledBookWithTextPage,
} from "../helpers/readerTools";
import { toolboxFrame } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "leveled-reader-copy-book-stats",
        languages: ["en"],
    },
});

const GROUP = ".bloom-translationGroup";
const LANG = "en";

// 9 words in 2 sentences; the longest word is "away" (4 letters), the longest sentence 6 words.
const FIRST_PAGE_TEXT = "The cat sat. The big dog ran far away.";
// 4 words in 1 sentence.
const SECOND_PAGE_TEXT = "A dog can run.";

// The columns Bloom copies, in its order, with what the two pages above should give. The words
// are 13 in all, of 11 different words ("The" and "dog" come twice). They have 36 letters among
// them, for a word's length counts each of the language's multi-letter graphemes as one letter
// (ReaderToolsModel.getWordLength), and an English collection has "th": "The" has 2.
const EXPECTED_FIGURES: [string, string][] = [
    ["Number of Pages with Text", "2"],
    ["Total Number of Words", "13"],
    ["Total Number of Sentences", "3"],
    ["Average No of Words per Page with Text", "6.5"], // 13 / 2
    ["Average No of Sentences per Page with Text", "1.5"], // 3 / 2
    ["Number of Unique Words", "11"],
    ["Average Word Length", "2.8"], // 36 / 13
    ["Average Sentence Length", "4.3"], // 13 / 3
    ["Maximum Word Length", "4"],
    ["Maximum Sentence Length", "6"],
];

test("COPY BOOK STATS puts the book's leveled reader statistics on the clipboard (Test Case ID 469)", async ({
    page,
}) => {
    const bookFolder = await makeLeveledBookWithTextPage(page);
    const level = await getReaderPhase(page, "leveledReader");
    await pasteTextInGroup(page, GROUP, LANG, FIRST_PAGE_TEXT);

    const [firstPage] = await getContentPages(page);
    await addPage(page, "Just Text");
    const secondPage = (await getContentPages(page)).find(
        (p) => p.id !== firstPage.id,
    );
    if (!secondPage)
        throw new Error("test setup: adding a Just Text page added no page.");
    await goToPage(page, secondPage.id);
    await pasteTextInGroup(page, GROUP, LANG, SECOND_PAGE_TEXT);

    // The panel's book figures come from a pass over the whole book after the paste; the button
    // copies what that pass worked out, so wait until it has counted both pages.
    await expect(
        leveledReaderStat(page, "words-in-book"),
        "The Leveled Reader should count the words of both pages before its stats are copied.",
    ).toHaveText("13", { timeout: 15000 });

    // The clipboard still holds the second page's text, from pasting it.
    expect((await getClipboard(page)).text).toBe(SECOND_PAGE_TEXT);

    const label = await getUiString(
        page,
        "EditTab.Toolbox.LeveledReaderTool.CopyBookStatistics",
        "Copy Book Stats",
    );
    await toolboxFrame(page).getByRole("button", { name: label }).click();

    let copied = "";
    await expect
        .poll(
            async () => {
                copied = (await getClipboard(page)).text ?? "";
                return copied !== SECOND_PAGE_TEXT;
            },
            {
                timeout: 10000,
                message:
                    "Clicking COPY BOOK STATS should replace what the clipboard held with the book's statistics.",
            },
        )
        .toBe(true);

    const rows = copied.split(/\r?\n/);
    expect(
        rows,
        "Bloom copies a header row and one row of figures.",
    ).toHaveLength(2);
    const headers = rows[0].split("\t");
    const figures = rows[1].split("\t");
    expect(
        figures,
        "The row of figures should have one figure for each header.",
    ).toHaveLength(headers.length);
    const copiedStats = Object.fromEntries(
        headers.map((header, i) => [header, figures[i]]),
    );

    // A book whose title is still empty takes its folder's name as its title. Bloom quotes the
    // title and the level, so that a spreadsheet takes each as text.
    expect(copiedStats).toEqual({
        "Book Title": `"${Path.basename(bookFolder)}"`,
        Level: `"Level ${level}"`,
        ...Object.fromEntries(EXPECTED_FIGURES),
    });
    expect(headers, "Bloom copies the columns in this order.").toEqual([
        "Book Title",
        "Level",
        ...EXPECTED_FIGURES.map(([header]) => header),
    ]);
});
