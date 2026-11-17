import { afterEach, beforeEach, describe, expect, it } from "vitest";
import * as React from "react";
import { act } from "react";
import { renderRoot, unmountRoot } from "../../../../utils/reactRender";
import { ResetLanguageDataInstance } from "../libSynphony/synphony_lib";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import ReadersSynphonyWrapper from "../ReadersSynphonyWrapper";
import { LeveledReaderStats } from "./LeveledReaderToolControls";

// These need real layout (column widths, scrollWidth), so they run in the "browser"
// vitest project, not jsdom. Localization is bypassed there, so the headers start out
// in English; a test puts long translations in by replacing their text, the way a
// translation arriving from Bloom would.

// Tell React that this environment supports act(), as vitest.setup.ts does for jsdom.
(globalThis as unknown as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT =
    true;

// The width the stats get in the toolbox: 200px less the panel's padding and the
// scrolling region's scrollbar gutter.
const kStatsWidthPx = 158;

let container: HTMLDivElement;

beforeEach(() => {
    ResetLanguageDataInstance();
    getTheOneReaderToolsModel().clearForTest();
    const api = new ReadersSynphonyWrapper();
    getTheOneReaderToolsModel().synphony = api;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const settings: any = {
        letters: "a b c d e f g h i j k l m n o p q r s t u v w x y z",
        stages: [],
        levels: [{ maxWordsPerSentence: 10, thingsToRemember: [""] }],
    };
    api.loadSettings(settings);
    getTheOneReaderToolsModel().setLevelNumber(1, true);

    container = document.createElement("div");
    container.style.width = `${kStatsWidthPx}px`;
    container.style.font = "12px Arial";
    document.body.appendChild(container);
    act(() => {
        renderRoot(
            React.createElement(LeveledReaderStats, {
                bookStats: {
                    levelNumber: 1,
                    pageCount: 1,
                    actualWordsPerPage: 12,
                    actualWordsPerSentence: 2,
                    actualWordCount: 3,
                    actualWordsPerPageBook: 4,
                    actualUniqueWords: 5,
                    actualSentencesPerPage: 8,
                    actualSentenceCount: 12,
                    actualLettersPerWord: 4,
                    actualMaxWordsPerSentence: 16,
                    actualMaxGlyphsPerWord: 6,
                    actualAverageWordsPerSentence: 9.04,
                    actualAverageWordsPerPage: 7,
                    actualAverageGlyphsPerWord: 2.2,
                    actualAverageSentencesPerPage: 1.5,
                },
            }),
            container,
        );
    });
});

afterEach(() => {
    unmountRoot(container);
    container.remove();
});

// The column header cells (one per section) whose text is currently `text`.
function headerCells(text: string): HTMLElement[] {
    const cells = Array.from(container.querySelectorAll("span"))
        .filter((span) => span.textContent === text)
        .map((span) => span.closest("div")!);
    // A localized Span is a span within a span, so each cell is found twice.
    return Array.from(new Set(cells));
}

// Let the headers' ResizeObserver/MutationObserver re-fits, which wait a frame, happen.
async function afterReflow(): Promise<void> {
    for (let i = 0; i < 3; i++) {
        await new Promise((resolve) => requestAnimationFrame(resolve));
    }
}

function expectNothingWiderThanTheToolbox(): void {
    for (const element of Array.from(container.querySelectorAll("*"))) {
        const right = element.getBoundingClientRect().right;
        expect(
            right,
            `"${element.textContent}" ends past the toolbox's edge`,
        ).toBeLessThanOrEqual(container.getBoundingClientRect().right + 0.5);
    }
}

function expectHeadersToLineUp(cells: HTMLElement[]): void {
    const lefts = cells.map((cell) =>
        Math.round(cell.getBoundingClientRect().left),
    );
    expect(new Set(lefts).size, `header columns at ${lefts}`).toBe(1);
}

describe("LeveledReaderStats column headers", () => {
    it("fit at full size in English, lined up in every section", async () => {
        await afterReflow();
        const max = headerCells("Max");
        const actual = headerCells("Actual");
        // Sanity check: one header pair per section.
        expect(max.length).toBe(4);
        expect(actual.length).toBe(4);
        for (const cell of [...max, ...actual]) {
            expect(cell.scrollWidth).toBeLessThanOrEqual(cell.clientWidth);
            expect(cell.style.fontSize).toBe("");
        }
        expectHeadersToLineUp(max);
        expectHeadersToLineUp(actual);
        expectNothingWiderThanTheToolbox();
    });

    it("shrink and then break long single-word translations instead of overflowing", async () => {
        await afterReflow();
        const max = headerCells("Max");
        const actual = headerCells("Actual");
        act(() => {
            max.forEach((cell) => {
                cell.querySelector("span")!.textContent = "Максимальный";
            });
            actual.forEach((cell) => {
                cell.querySelector("span")!.textContent = "Актуальный";
            });
        });
        await afterReflow();

        for (const cell of [...max, ...actual]) {
            expect(
                cell.scrollWidth,
                `"${cell.textContent}" is wider than its column`,
            ).toBeLessThanOrEqual(cell.clientWidth);
            expect(parseFloat(getComputedStyle(cell).fontSize)).toBe(8);
        }
        expectHeadersToLineUp(max);
        expectHeadersToLineUp(actual);
        expectNothingWiderThanTheToolbox();
    });
});
