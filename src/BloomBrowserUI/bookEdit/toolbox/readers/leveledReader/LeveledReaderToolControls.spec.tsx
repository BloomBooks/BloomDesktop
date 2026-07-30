import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { act } from "react";
import { renderRoot, unmountRoot } from "../../../../utils/reactRender";
import { ResetLanguageDataInstance } from "../libSynphony/synphony_lib";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import ReadersSynphonyWrapper from "../ReadersSynphonyWrapper";
import { LeveledReaderStats } from "./LeveledReaderToolControls";

// The level we test against. Deliberately leaves most limits unset (0 = "this
// level does not limit that statistic") so that we also cover the "no limit"
// case, and so that the two statistics under test have limits that the book
// stats below exceed.
const kMaxWordsPerSentence = 10;
const kMaxGlyphsPerWord = 5;
const kMaxAverageWordsPerSentence = 9;

// Each value is unique so a cell can be located by its text.
const bookStats = {
    levelNumber: 1,
    pageCount: 1,
    actualWordsPerPage: 1,
    actualWordsPerSentence: 2,
    actualWordCount: 3,
    actualWordsPerPageBook: 4,
    actualUniqueWords: 5,
    actualSentencesPerPage: 8,
    actualSentenceCount: 12,
    actualLettersPerWord: 4.5, // reported as an integer elsewhere; kept distinct here
    // Over kMaxWordsPerSentence: "This Book / longest sentence" must be orange (BL-16408).
    actualMaxWordsPerSentence: 16,
    // Over kMaxGlyphsPerWord: "Word Lengths / max in book" must be orange (BL-16628).
    actualMaxGlyphsPerWord: 6,
    // Rounds to "9.0" but is really over kMaxAverageWordsPerSentence, so orange.
    actualAverageWordsPerSentence: 9.04,
    actualAverageWordsPerPage: 7,
    actualAverageGlyphsPerWord: 2.2,
    actualAverageSentencesPerPage: 1.5,
};

// Find the single element whose own text is exactly the given string. Used to
// locate a stats cell by the number it displays.
function getCellShowing(container: HTMLElement, text: string): HTMLElement {
    const matches = Array.from(container.querySelectorAll("div")).filter(
        (div) => div.textContent?.trim() === text,
    );
    if (matches.length !== 1) {
        fail(
            `Expected exactly one cell showing "${text}", found ${matches.length}. The test data may no longer be unique.`,
        );
    }
    return matches[0] as HTMLElement;
}

// Emotion puts each css block in a class of its own, so we read the color out of
// the stylesheet rule for the cell's class rather than from an inline style.
function getColorOf(cell: HTMLElement): string {
    const allCss = Array.from(document.querySelectorAll("style"))
        .map((style) => style.textContent ?? "")
        .join("\n");
    for (const className of Array.from(cell.classList)) {
        const rule = new RegExp(`\\.${className}\\s*\\{([^}]*)\\}`).exec(
            allCss,
        );
        const color = rule && /color:\s*([^;}]+)/.exec(rule[1]);
        if (color) {
            return color[1].trim();
        }
    }
    fail(
        `Found no color rule for cell "${cell.textContent}" (classes: ${cell.className}).`,
    );
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
            ],
        };
        const api = new ReadersSynphonyWrapper();
        getTheOneReaderToolsModel().synphony = api;
        api.loadSettings(settings);
        getTheOneReaderToolsModel().setLevelNumber(1, true);

        // Sanity check: without these limits the color assertions below would
        // pass for the wrong reason.
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

    it("shows a statistic that is over the level's limit in orange", () => {
        // "This Book / longest sentence" and "Word Lengths / max in book" both
        // exceed a limit of the current level. They regressed to green in the
        // React conversion because their limit was hardcoded to 0 (BL-16408).
        expect(getColorOf(getCellShowing(container, "16"))).toBe("orange");
        expect(getColorOf(getCellShowing(container, "6"))).toBe("orange");
    });

    it("shows a statistic that is within the level's limit in lightgreen", () => {
        // "Word Lengths / this page" shares max in book's limit and is under it.
        expect(getColorOf(getCellShowing(container, "4.5"))).toBe("lightgreen");
    });

    it("shows a statistic with no limit for this level in lightgreen", () => {
        // Nothing limits words per page in this level, so no value is too large.
        expect(getColorOf(getCellShowing(container, "1"))).toBe("lightgreen");
    });

    it("leaves the Max cell blank for the two rows that share the limit above them", () => {
        // Pre-React behavior we are keeping: the limit is used for coloring but
        // not displayed on these rows, so it appears exactly once per grid.
        const maxCellsShowingTheSentenceLimit = Array.from(
            container.querySelectorAll("div"),
        ).filter(
            (div) => div.textContent?.trim() === `${kMaxWordsPerSentence}`,
        );
        expect(maxCellsShowingTheSentenceLimit.length).toBe(1);
        const maxCellsShowingTheGlyphLimit = Array.from(
            container.querySelectorAll("div"),
        ).filter((div) => div.textContent?.trim() === `${kMaxGlyphsPerWord}`);
        // kMaxGlyphsPerWord is also the value of actualUniqueWords, so the Max
        // cell of "Word Lengths / this page" plus that one actual value = 2.
        expect(maxCellsShowingTheGlyphLimit.length).toBe(2);
    });

    it("compares averages at full precision, not as displayed", () => {
        // 9.04 displays as "9.0" but is over the level's average limit of 9.
        expect(getColorOf(getCellShowing(container, "9.0"))).toBe("orange");
    });
});
