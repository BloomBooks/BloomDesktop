import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { act } from "react";
import { renderRoot, unmountRoot } from "../../../../utils/reactRender";
import { ResetLanguageDataInstance } from "../libSynphony/synphony_lib";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import ReadersSynphonyWrapper from "../ReadersSynphonyWrapper";
import { LeveledReaderStats } from "./LeveledReaderToolControls";

// The level we test against. It deliberately leaves most limits unset (0 means
// "this level does not limit that statistic") so we also cover the no-limit case.
const kMaxWordsPerSentence = 10;
const kMaxGlyphsPerWord = 5;
const kMaxAverageWordsPerSentence = 9;
// A second level, used to check that the limits are re-read when the level changes.
// Its glyph limit is high enough that the statistic that is too large for level 1
// is acceptable here.
const kLevel2MaxGlyphsPerWord = 20;

const bookStats = {
    levelNumber: 1,
    pageCount: 1,
    actualWordsPerPage: 12, // no limit for this level
    actualWordsPerSentence: 2,
    actualWordCount: 3,
    actualWordsPerPageBook: 4,
    actualUniqueWords: 5,
    actualSentencesPerPage: 8,
    actualSentenceCount: 12,
    actualLettersPerWord: 4, // under kMaxGlyphsPerWord
    // Over kMaxWordsPerSentence: "This Book / longest sentence" (BL-16408).
    actualMaxWordsPerSentence: 16,
    // Over kMaxGlyphsPerWord: "Word Lengths / max in book" (BL-16628).
    actualMaxGlyphsPerWord: 6,
    // Rounds to "9.0" but is really over kMaxAverageWordsPerSentence.
    actualAverageWordsPerSentence: 9.04,
    actualAverageWordsPerPage: 7,
    actualAverageGlyphsPerWord: 2.2,
    actualAverageSentencesPerPage: 1.5,
};

// Row labels and section headers are localized, and in tests the localization
// manager is mocked, so each renders as its l10n key. That makes the key the
// most precise way to find a row.
const kKeyPrefix = "EditTab.Toolbox.LeveledReaderTool.";

// Each stats grid lays its cells out as a flat list of children: one or two
// section headers, then the Max/Actual header row, then three cells per
// statistic (label, max, actual). Find a row by its label and return the other
// two cells of that row.
function getRow(
    container: HTMLElement,
    sectionHeaderKeySuffix: string,
    rowLabelKeySuffix: string,
): { maxCell: HTMLElement; actualCell: HTMLElement } {
    const grids = Array.from(container.firstElementChild!.children);
    const grid = grids.find((g) =>
        // Only the leading children are section headers; a row label never is.
        Array.from(g.children)
            .slice(0, 2)
            .some(
                (child) =>
                    child.textContent === kKeyPrefix + sectionHeaderKeySuffix,
            ),
    );
    if (!grid) {
        fail(`Found no stats grid headed "${sectionHeaderKeySuffix}".`);
    }
    const cells = Array.from(grid.children);
    const labelIndex = cells.findIndex(
        (cell) => cell.textContent === kKeyPrefix + rowLabelKeySuffix,
    );
    if (labelIndex < 0) {
        fail(
            `Found no "${rowLabelKeySuffix}" row in the "${sectionHeaderKeySuffix}" grid.`,
        );
    }
    return {
        maxCell: cells[labelIndex + 1] as HTMLElement,
        actualCell: cells[labelIndex + 2] as HTMLElement,
    };
}

describe("LeveledReaderStats", () => {
    let container: HTMLDivElement;

    beforeEach(() => {
        ResetLanguageDataInstance();
        getTheOneReaderToolsModel().clearForTest();

        const settings: any = {
            letters: "a b c d e f g h i j k l m n o p q r s t u v w x y z",
            stages: [],
            levels: [
                {
                    maxWordsPerSentence: kMaxWordsPerSentence,
                    maxGlyphsPerWord: kMaxGlyphsPerWord,
                    maxAverageWordsPerSentence: kMaxAverageWordsPerSentence,
                    thingsToRemember: [""],
                },
                {
                    maxGlyphsPerWord: kLevel2MaxGlyphsPerWord,
                    thingsToRemember: [""],
                },
            ],
        };
        const api = new ReadersSynphonyWrapper();
        getTheOneReaderToolsModel().synphony = api;
        api.loadSettings(settings);
        getTheOneReaderToolsModel().setLevelNumber(1, true);

        // Sanity check: without these limits the assertions below would pass for
        // the wrong reason.
        expect(
            getTheOneReaderToolsModel().maxWordsPerSentenceOnThisPage(),
        ).toBe(kMaxWordsPerSentence);
        expect(getTheOneReaderToolsModel().maxGlyphsPerWord()).toBe(
            kMaxGlyphsPerWord,
        );

        container = document.createElement("div");
        document.body.appendChild(container);
        act(() => {
            renderRoot(<LeveledReaderStats bookStats={bookStats} />, container);
        });
    });

    afterEach(() => {
        unmountRoot(container);
        container.remove();
    });

    it("marks a statistic that is over the level's limit as too large", () => {
        // These two rows regressed to "acceptable" in the React conversion
        // because their limit was hardcoded to 0 (BL-16408, BL-16628).
        const longestSentence = getRow(
            container,
            "ThisBook",
            "MaxSentenceLength",
        );
        expect(longestSentence.actualCell.textContent).toBe("16");
        expect(longestSentence.actualCell.classList).toContain("tooLarge");

        const maxInBook = getRow(container, "WordLengths", "MaxInBook");
        expect(maxInBook.actualCell.textContent).toBe("6");
        expect(maxInBook.actualCell.classList).toContain("tooLarge");
    });

    it("marks a statistic that is within the level's limit as acceptable", () => {
        // "Word Lengths / this page" shares max in book's limit and is under it.
        const thisPage = getRow(container, "WordLengths", "ThisPageLC");
        expect(thisPage.actualCell.textContent).toBe("4");
        expect(thisPage.actualCell.classList).toContain("acceptable");
    });

    it("marks a statistic with no limit for this level as acceptable", () => {
        // Nothing limits words per page in this level, so no value is too large.
        const perPage = getRow(container, "ThisPage", "PerPage");
        expect(perPage.actualCell.textContent).toBe("12");
        expect(perPage.actualCell.classList).toContain("acceptable");
    });

    it("shows the level's limit in the Max cell, except on the two rows that share the limit above them", () => {
        // Pre-React behavior we are keeping: on these two rows the limit decides
        // the color but is not displayed, because the row above already shows it.
        expect(
            getRow(container, "WordLengths", "ThisPageLC").maxCell.textContent,
        ).toBe(`${kMaxGlyphsPerWord}`);
        expect(
            getRow(container, "WordLengths", "MaxInBook").maxCell.textContent,
        ).toBe("");
        expect(
            getRow(container, "ThisPage", "PerSentence").maxCell.textContent,
        ).toBe(`${kMaxWordsPerSentence}`);
        expect(
            getRow(container, "ThisBook", "MaxSentenceLength").maxCell
                .textContent,
        ).toBe("");
    });

    it("compares averages at full precision, not as displayed", () => {
        // 9.04 displays as "9.0" but is over the level's average limit of 9.
        const average = getRow(container, "ThisBook", "Average");
        expect(average.actualCell.textContent).toBe("9.0");
        expect(average.actualCell.classList).toContain("tooLarge");
    });

    it("leaves the Max cell blank when this level has no limit for a statistic", () => {
        expect(
            getRow(container, "ThisPage", "PerPage").maxCell.textContent,
        ).toBe("");
    });

    it("re-reads the level's limits when the level changes", () => {
        // The conversion dropped the tests that covered this, and the whole fix
        // depends on the limits being current whenever we render.
        const atLevel1 = getRow(container, "WordLengths", "MaxInBook");
        expect(atLevel1.actualCell.classList).toContain("tooLarge");

        getTheOneReaderToolsModel().setLevelNumber(2, true);
        act(() => {
            renderRoot(<LeveledReaderStats bookStats={bookStats} />, container);
        });

        // 6 is within level 2's much larger glyph limit, so it is no longer flagged.
        const atLevel2 = getRow(container, "WordLengths", "MaxInBook");
        expect(atLevel2.actualCell.textContent).toBe("6");
        expect(atLevel2.actualCell.classList).toContain("acceptable");
        // "this page" displays the new level's limit.
        expect(
            getRow(container, "WordLengths", "ThisPageLC").maxCell.textContent,
        ).toBe(`${kLevel2MaxGlyphsPerWord}`);
    });
});
