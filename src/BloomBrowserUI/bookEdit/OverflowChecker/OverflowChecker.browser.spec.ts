import { afterEach, describe, it, expect, vi } from "vitest";
import OverflowChecker from "./OverflowChecker";
import {
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "../js/canvasElementManager/CanvasElementManager";
import OverflowAncestorFixture from "./OverflowAncestorFixture.html?raw";
import OverflowFixture from "./OverflowFixture.html?raw";
import OverflowMarginFixture from "./OverflowMarginFixture.html?raw";

// These need real layout (scrollHeight, clientHeight, offsetTop, and the canvas that
// MeasureText draws on), so they run in the "browser" vitest project, not jsdom.
// Each fixture has elements with class "myTest" and a "name" attribute; those that should
// overflow also have class "expectToOverflow". The fixtures assume Arial is installed.
// The .html fixtures are compiled from the .pug beside them (see the note there).

interface IFixtureCase {
    index: number;
    name: string;
    expectToOverflow: boolean;
}

// Lists the test cases in a fixture, so that each one is reported as its own test.
function casesIn(fixture: string): IFixtureCase[] {
    const doc = new DOMParser().parseFromString(fixture, "text/html");
    return Array.from(doc.querySelectorAll(".myTest")).map((e, index) => ({
        index,
        name: e.getAttribute("name") ?? `unnamed test #${index}`,
        expectToOverflow: e.classList.contains("expectToOverflow"),
    }));
}

// Puts the fixture in the document and returns its test elements. It goes in a container
// rather than replacing the whole body, so it leaves alone anything else there, such as
// the hidden div MeasureText reuses between calls.
function loadFixture(fixture: string): HTMLElement[] {
    let root = document.getElementById("fixtureRoot");
    if (!root) {
        root = document.createElement("div");
        root.id = "fixtureRoot";
        document.body.appendChild(root);
    }
    root.innerHTML = fixture;
    return Array.from(root.querySelectorAll<HTMLElement>(".myTest"));
}

// Puts the fixture in the document and returns its index'th test element.
function loadTestElement(fixture: string, index: number): HTMLElement {
    return loadFixture(fixture)[index];
}

// What to report when a case fails, since the numbers are what you need to work out why.
function describeMeasurements(element: HTMLElement): string {
    const style = window.getComputedStyle(element);
    return (
        `scrollHeight ${element.scrollHeight}, clientHeight ${element.clientHeight}, ` +
        `font ${style.font}, style "${element.getAttribute("style") ?? ""}"`
    );
}

describe("OverflowChecker.IsOverflowingSelf", () => {
    const cases = casesIn(OverflowFixture);
    it("found the fixture's test cases", () => {
        expect(cases.length).toBe(8);
    });
    it.each(cases)("$name", (testCase) => {
        const element = loadTestElement(OverflowFixture, testCase.index);
        expect(
            OverflowChecker.IsOverflowingSelf(element),
            describeMeasurements(element),
        ).toBe(testCase.expectToOverflow);
    });
    // Bloom checks all the boxes on a page one after another, and MeasureText keeps state
    // between calls, so also check every case in turn in one document.
    it("gives the same answers checking every case in turn", () => {
        const elements = loadFixture(OverflowFixture);
        expect(elements.length).toBe(cases.length);
        const results = elements.map((e) => ({
            name: e.getAttribute("name"),
            overflows: OverflowChecker.IsOverflowingSelf(e),
        }));
        expect(results).toEqual(
            cases.map((c) => ({ name: c.name, overflows: c.expectToOverflow })),
        );
    });
});

describe("OverflowChecker.overflowingAncestor", () => {
    describe.each([
        { fixtureName: "margins", fixture: OverflowMarginFixture, count: 5 },
        {
            fixtureName: "fixed ancestors",
            fixture: OverflowAncestorFixture,
            count: 7,
        },
    ])("$fixtureName", ({ fixture, count }) => {
        const cases = casesIn(fixture);
        it("found the fixture's test cases", () => {
            expect(cases.length).toBe(count);
        });
        it.each(cases)("$name", (testCase) => {
            const element = loadTestElement(fixture, testCase.index);
            expect(
                OverflowChecker.overflowingAncestor(element) !== null,
                describeMeasurements(element),
            ).toBe(testCase.expectToOverflow);
        });
    });
});

describe("OverflowChecker.AdjustSizeOrMarkOverflowSoon", () => {
    afterEach(() => {
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    // Puts one bloom-page per entry of `boxesPerPage` in the document, each with a margin box
    // holding that many text boxes, and returns the boxes page by page.
    function loadPages(boxesPerPage: number[]): HTMLElement[][] {
        const pages = boxesPerPage
            .map(
                (count) =>
                    `<div class="bloom-page"><div class="marginBox" style="width:25em;height:400px">` +
                    `<div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on">Short.</div></div>`.repeat(
                        count,
                    ) +
                    `</div></div>`,
            )
            .join("");
        loadFixture(pages);
        return Array.from(
            document.querySelectorAll<HTMLElement>("#fixtureRoot .marginBox"),
        ).map((marginBox) =>
            Array.from(
                marginBox.querySelectorAll<HTMLElement>(".bloom-editable"),
            ),
        );
    }

    // The page-wide check measures every box in the margin box, so one per box would make a page
    // of n boxes do n * n measurements: over ten seconds of work for a page with a 28-cell table
    // (BL-16816).
    it("checks each box itself, and the page once, when all the boxes come due together", () => {
        const [boxes] = loadPages([6]);
        expect(boxes.length).toBe(6);
        vi.useFakeTimers();
        const selfChecks = vi
            .spyOn(OverflowChecker, "AdjustSizeAndMarkSelfOverflow")
            .mockImplementation(() => {});
        const pageChecks = vi
            .spyOn(OverflowChecker, "CheckPageAncestorOverflow")
            .mockImplementation(() => {});

        boxes.forEach((box) =>
            OverflowChecker.AdjustSizeOrMarkOverflowSoon(box),
        );
        vi.advanceTimersByTime(999);
        expect(
            selfChecks,
            "nothing should be checked before the second is up",
        ).not.toHaveBeenCalled();

        // Just the timers that are due: the boxes' own. (Fake timers give a timer made during
        // this run a delay of at least 1ms, where the browser runs it as soon as these are done,
        // so the page-wide check is run as a separate step below.)
        vi.runOnlyPendingTimers();
        expect(selfChecks.mock.calls.map((call) => call[0])).toEqual(boxes);
        expect(
            pageChecks,
            "the page-wide check should wait until every box has checked itself",
        ).not.toHaveBeenCalled();

        vi.runOnlyPendingTimers();
        expect(pageChecks).toHaveBeenCalledTimes(1);
    });

    it("checks each margin box once when boxes on two pages come due together", () => {
        const [firstPage, secondPage] = loadPages([3, 2]);
        expect([firstPage.length, secondPage.length]).toEqual([3, 2]);
        vi.useFakeTimers();
        vi.spyOn(
            OverflowChecker,
            "AdjustSizeAndMarkSelfOverflow",
        ).mockImplementation(() => {});
        const pageChecks = vi
            .spyOn(OverflowChecker, "CheckPageAncestorOverflow")
            .mockImplementation(() => {});

        [...firstPage, ...secondPage].forEach((box) =>
            OverflowChecker.AdjustSizeOrMarkOverflowSoon(box),
        );
        vi.runOnlyPendingTimers();
        vi.runOnlyPendingTimers();

        const marginBoxesChecked = pageChecks.mock.calls.map((call) =>
            call[0].closest(".marginBox"),
        );
        expect(marginBoxesChecked).toEqual([
            firstPage[0].closest(".marginBox"),
            secondPage[0].closest(".marginBox"),
        ]);
    });

    // The waiting check holds on to the last box that asked for it, but the other boxes still
    // need their page checked if that one is deleted.
    it("still checks the page when the last box to come due is deleted before the check runs", () => {
        const [boxes] = loadPages([3]);
        const marginBox = boxes[0].closest(".marginBox");
        vi.useFakeTimers();
        vi.spyOn(
            OverflowChecker,
            "AdjustSizeAndMarkSelfOverflow",
        ).mockImplementation(() => {});
        const pageChecks = vi
            .spyOn(OverflowChecker, "CheckPageAncestorOverflow")
            .mockImplementation(() => {});

        boxes.forEach((box) =>
            OverflowChecker.AdjustSizeOrMarkOverflowSoon(box),
        );
        vi.runOnlyPendingTimers();
        expect(
            pageChecks,
            "the page-wide check should still be waiting",
        ).not.toHaveBeenCalled();
        boxes[2].closest(".bloom-translationGroup")!.remove();
        vi.runOnlyPendingTimers();

        expect(pageChecks).toHaveBeenCalledTimes(1);
        expect(pageChecks.mock.calls[0][0].isConnected).toBe(true);
        expect(pageChecks.mock.calls[0][0].closest(".marginBox")).toBe(
            marginBox,
        );
    });

    // Bloom has been seen getting a window resize event about every 110 ms for many seconds, each
    // of which asks again for every box on the page. If each request restarted the box's second,
    // no box would be checked until the events stopped.
    it("checks a box a second after it was first asked, however often it is asked again", () => {
        const [[box]] = loadPages([1]);
        vi.useFakeTimers();
        const selfChecks = vi
            .spyOn(OverflowChecker, "AdjustSizeAndMarkSelfOverflow")
            .mockImplementation(() => {});
        vi.spyOn(
            OverflowChecker,
            "CheckPageAncestorOverflow",
        ).mockImplementation(() => {});

        OverflowChecker.AdjustSizeOrMarkOverflowSoon(box);
        for (let elapsed = 100; elapsed < 1000; elapsed += 100) {
            vi.advanceTimersByTime(100);
            OverflowChecker.AdjustSizeOrMarkOverflowSoon(box);
        }
        expect(
            selfChecks,
            "nothing should be checked before the second is up",
        ).not.toHaveBeenCalled();
        vi.advanceTimersByTime(100);
        expect(
            selfChecks,
            "the box should be checked one second after the first request",
        ).toHaveBeenCalledTimes(1);

        // A request after that check needs a check of its own.
        OverflowChecker.AdjustSizeOrMarkOverflowSoon(box);
        vi.advanceTimersByTime(1000);
        expect(selfChecks).toHaveBeenCalledTimes(2);
    });
});

// A text box in a table cell has to fit its cell, not the table's own box or the canvas element
// the table fills, which can be shorter than its rows once a row has been made taller by dragging
// its boundary. It still has to fit the marginBox.
describe("OverflowChecker.overflowingAncestor in a table cell", () => {
    // A table filling a 40px canvas element, whose single 120px row runs well past it, holding a
    // cell with a text box.
    function loadCellTextBox(
        textBoxStyle: string,
        marginBoxHeight = 600,
    ): HTMLElement {
        const [element] = loadFixture(`
            <div class="bloom-page">
                <div class="marginBox" style="height: ${marginBoxHeight}px">
                    <div class="bloom-canvas-element" style="height: 40px">
                        <div class="bloom-table" style="display: grid; height: 100%; grid-template-rows: 120px">
                            <div class="bloom-cell" style="overflow: clip">
                                <div class="bloom-translationGroup">
                                    <div class="myTest bloom-editable" style="${textBoxStyle}">text</div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>`);
        return element;
    }

    it("does not report text that fits its cell but not the table's box", () => {
        const element = loadCellTextBox("height: 80px");
        const canvasElement = element.closest<HTMLElement>(
            ".bloom-canvas-element",
        )!;
        expect(
            element.getBoundingClientRect().bottom,
            "The text box should reach past the table's box and its canvas element, or this tests nothing.",
        ).toBeGreaterThan(canvasElement.getBoundingClientRect().bottom + 10);
        expect(OverflowChecker.overflowingAncestor(element)).toBe(null);
    });

    it("reports text that does not fit its cell", () => {
        const element = loadCellTextBox("height: 200px");
        expect(OverflowChecker.overflowingAncestor(element)).toBe(
            element.closest(".bloom-cell"),
        );
    });

    it("reports text that fits its cell but runs past the marginBox", () => {
        const element = loadCellTextBox("height: 110px", 100);
        const cell = element.closest<HTMLElement>(".bloom-cell")!;
        expect(
            element.getBoundingClientRect().bottom,
            "The text box should fit its cell, or this tests the cell instead.",
        ).toBeLessThanOrEqual(cell.getBoundingClientRect().bottom);
        expect(OverflowChecker.overflowingAncestor(element)).toBe(
            element.closest(".marginBox"),
        );
    });

    it("reports text in a nested table that fits its cell but not the outer table's cell", () => {
        // An outer table whose 100px row holds a nested table, whose own 160px row runs past
        // the outer cell.
        const [element] = loadFixture(`
            <div class="bloom-page">
                <div class="marginBox" style="height: 600px">
                    <div class="bloom-canvas-element" style="height: 300px">
                        <div class="bloom-table" style="display: grid; height: 100%; grid-template-rows: 100px 100px">
                            <div class="bloom-cell" data-content-type="table">
                                <div class="bloom-table" style="display: grid; height: 100%; grid-template-rows: 160px">
                                    <div class="bloom-cell" style="overflow: clip">
                                        <div class="bloom-translationGroup">
                                            <div class="myTest bloom-editable" style="height: 140px">text</div>
                                        </div>
                                    </div>
                                </div>
                            </div>
                            <div class="bloom-cell"></div>
                        </div>
                    </div>
                </div>
            </div>`);
        const innerCell = element.closest<HTMLElement>(".bloom-cell")!;
        const outerCell =
            innerCell.parentElement!.closest<HTMLElement>(".bloom-cell")!;
        expect(
            element.getBoundingClientRect().bottom,
            "The text should fit its own cell, or this tests the inner cell instead.",
        ).toBeLessThanOrEqual(innerCell.getBoundingClientRect().bottom);
        expect(OverflowChecker.overflowingAncestor(element)).toBe(outerCell);
    });
});

describe("OverflowChecker.AdjustSizeAndMarkSelfOverflow", () => {
    afterEach(() => {
        vi.clearAllTimers();
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    // The rebuild of the page's scroll bars that is waiting, if any. MeasureText keeps a timer
    // of its own, so counting timers would not tell the two apart.
    const checker = OverflowChecker as unknown as {
        fixScrollBarsTimeout: number;
    };

    // Puts in the document a box whose text does not fit it, with the given extra classes, and
    // sets up what its check needs.
    function setUpOverflowingBox(classes: string): HTMLElement {
        const [editable] = loadFixture(`
            <div class="bloom-page">
                <div class="marginBox" style="height: 600px">
                    <div class="myTest bloom-editable bloom-visibility-code-on ${classes}"
                        style="height: 20px; width: 100px; font: 16px Arial; overflow: hidden">${"words ".repeat(40)}</div>
                </div>
            </div>`);
        vi.useFakeTimers();
        initializeCanvasElementManager();
        // The box is not in a canvas element, so its size stays as it is.
        vi.spyOn(
            theOneCanvasElementManager,
            "adjustSizeOfContainingCanvasElementToMatchContent",
        ).mockImplementation((_element, overflowY) => overflowY);
        clearTimeout(checker.fixScrollBarsTimeout);
        checker.fixScrollBarsTimeout = 0;
        return editable;
    }

    // Checks the box with no rebuild waiting, and says whether the check asked for one.
    function rebuildIsWaitingAfterCheck(editable: HTMLElement): boolean {
        clearTimeout(checker.fixScrollBarsTimeout);
        checker.fixScrollBarsTimeout = 0;
        OverflowChecker.AdjustSizeAndMarkSelfOverflow(editable);
        return checker.fixScrollBarsTimeout !== 0;
    }

    // A row drag checks a table's text on every frame, and rebuilding the scroll bars of a page
    // with a big table takes seconds on a slow computer, so a check that changes nothing must not
    // ask for a rebuild.
    it("asks for the scroll bars to be rebuilt only when a box's text starts or stops fitting", () => {
        const editable = setUpOverflowingBox("");

        expect(
            rebuildIsWaitingAfterCheck(editable),
            "The text does not fit, so the first check should ask for a rebuild.",
        ).toBe(true);
        expect(
            editable.classList.contains("overflow"),
            "The box should be marked, or this tests nothing.",
        ).toBe(true);
        expect(
            rebuildIsWaitingAfterCheck(editable),
            "A check of text that still does not fit should not ask for a rebuild.",
        ).toBe(false);
        editable.textContent = "words";
        expect(
            rebuildIsWaitingAfterCheck(editable),
            "A check of text that now fits should ask for a rebuild.",
        ).toBe(true);
        expect(editable.classList.contains("overflow")).toBe(false);
        expect(
            rebuildIsWaitingAfterCheck(editable),
            "A check of text that still fits should not ask for a rebuild.",
        ).toBe(false);
    });

    // A title box is never marked, but the page gives it a scroll bar when its text does not fit.
    it("asks for a rebuild when the text of a box that is never marked stops fitting", () => {
        const editable = setUpOverflowingBox("bloom-padForOverflow");

        expect(
            rebuildIsWaitingAfterCheck(editable),
            "The text does not fit, so the first check should ask for a rebuild.",
        ).toBe(true);
        expect(
            editable.classList.contains("overflow"),
            "A bloom-padForOverflow box should not be marked, or this tests the mark instead.",
        ).toBe(false);
        editable.textContent = "words";
        expect(
            rebuildIsWaitingAfterCheck(editable),
            "A check of text that now fits should ask for a rebuild.",
        ).toBe(true);
    });

    // While a drag goes on, a rebuild that a cell asked for waits until the checks stop.
    it("postpones a rebuild that is waiting when a check changes nothing", () => {
        const editable = setUpOverflowingBox("");
        OverflowChecker.AdjustSizeAndMarkSelfOverflow(editable);
        const waiting = checker.fixScrollBarsTimeout;
        expect(
            waiting,
            "The first check should have asked for a rebuild, or this tests nothing.",
        ).not.toBe(0);

        OverflowChecker.AdjustSizeAndMarkSelfOverflow(editable);

        expect(
            checker.fixScrollBarsTimeout,
            "The rebuild should still be waiting.",
        ).not.toBe(0);
        expect(
            checker.fixScrollBarsTimeout,
            "The rebuild should wait again from the start.",
        ).not.toBe(waiting);
    });
});

describe("OverflowChecker.AdjustSizeOrMarkOverflowOfAll", () => {
    afterEach(() => {
        vi.clearAllTimers();
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    // A canvas element holding a text box in each of two languages, both too small for their text.
    function setUpTwoLanguageCanvasElement(): HTMLElement[] {
        const boxStyle =
            "height: 20px; width: 100px; font: 16px Arial; overflow: hidden";
        const boxes = loadFixture(`
            <div class="bloom-page">
                <div class="marginBox" style="height: 600px">
                    <div class="bloom-canvas" style="height: 500px">
                        <div class="bloom-canvas-element" style="height: 40px">
                            <div class="bloom-translationGroup">
                                <div class="myTest bloom-editable bloom-visibility-code-on" style="${boxStyle}">${"words ".repeat(10)}</div>
                                <div class="myTest bloom-editable bloom-visibility-code-on" style="${boxStyle}">${"words ".repeat(10)}</div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>`);
        vi.useFakeTimers();
        initializeCanvasElementManager();
        return boxes;
    }

    // Growing a canvas element to fit one box's text gives the boxes of its other languages
    // more room too, so the overflow the batch read for them beforehand no longer holds.
    it("measures a box again once another box has resized their canvas element", () => {
        const [first, second] = setUpTwoLanguageCanvasElement();
        const canvasElement = first.closest<HTMLElement>(
            ".bloom-canvas-element",
        )!;
        expect(
            OverflowChecker.getSelfOverflowAmounts(second)[1],
            "The second box's text should not fit it at first, or this tests nothing.",
        ).toBeGreaterThan(0);
        const overflowGivenFor = new Map<HTMLElement, number>();
        vi.spyOn(
            theOneCanvasElementManager,
            "adjustSizeOfContainingCanvasElementToMatchContent",
        ).mockImplementation((element, overflowY) => {
            overflowGivenFor.set(element, overflowY);
            if (element === first) {
                // What growing the canvas element does to both of its boxes.
                canvasElement.style.height = "320px";
                first.style.height = "150px";
                second.style.height = "150px";
                return 0;
            }
            return overflowY;
        });

        OverflowChecker.AdjustSizeOrMarkOverflowOfAll([first, second]);

        expect(
            overflowGivenFor.get(first),
            "The first box should have been checked while its text did not fit.",
        ).toBeGreaterThan(0);
        expect(
            overflowGivenFor.get(second),
            "The second box's text fits once the canvas element has grown.",
        ).toBeLessThanOrEqual(0);
        expect(second.classList.contains("overflow")).toBe(false);
    });

    // Puts in the document a margin box holding `count` boxes too small for their text.
    function loadPageOfTooFullBoxes(count: number): HTMLElement[] {
        const box =
            `<div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" ` +
            `style="height: 10px; width: 100px; font: 16px Arial; overflow: hidden">${"words ".repeat(10)}</div></div>`;
        loadFixture(
            `<div class="bloom-page"><div class="marginBox">${box.repeat(count)}</div></div>`,
        );
        vi.useFakeTimers();
        initializeCanvasElementManager();
        // The boxes are not in canvas elements, so their sizes stay as they are.
        vi.spyOn(
            theOneCanvasElementManager,
            "adjustSizeOfContainingCanvasElementToMatchContent",
        ).mockImplementation((_element, overflowY) => overflowY);
        return Array.from(
            document.querySelectorAll<HTMLElement>(
                "#fixtureRoot .bloom-editable",
            ),
        );
    }

    // BL-1260: Bloom does not check a page with more than 30 boxes, and AddOverflowHandlers clears
    // its marks, so marks put there by a table check would never be cleared. A page can also grow
    // past 30 while it is open (a row added to a table): its earlier marks go then.
    it("marks nothing on a page with more than 30 boxes, and clears a page that grows past 30", () => {
        const boxes = loadPageOfTooFullBoxes(30);
        OverflowChecker.AdjustSizeOrMarkOverflowOfAll(boxes);
        expect(
            boxes.filter((box) => box.classList.contains("overflow")).length,
            "A page of 30 too-full boxes should be marked, or this tests nothing.",
        ).toBe(30);

        const group = boxes[0].parentElement!;
        const thirtyFirst = group.cloneNode(true) as HTMLElement;
        group.after(thirtyFirst);
        const all = Array.from(
            document.querySelectorAll<HTMLElement>(
                "#fixtureRoot .bloom-editable",
            ),
        );
        expect(all.length).toBe(31);
        OverflowChecker.AdjustSizeOrMarkOverflowOfAll(all);

        expect(
            all.filter((box) => box.classList.contains("overflow")),
            "No box on a page of 31 should be marked.",
        ).toEqual([]);

        // Back under the limit (a row deleted), a check of one box, as a table's check asks,
        // marks the whole page again: nothing else would restore the marks just cleared.
        thirtyFirst.remove();
        OverflowChecker.AdjustSizeOrMarkOverflowOfAll([boxes[0]]);
        expect(
            boxes.filter((box) => box.classList.contains("overflow")).length,
            "Every box on the page, not only the one checked, should be marked again.",
        ).toBe(30);
    });
});
