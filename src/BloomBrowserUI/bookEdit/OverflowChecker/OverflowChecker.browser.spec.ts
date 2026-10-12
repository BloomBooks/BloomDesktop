import { afterEach, describe, it, expect, vi } from "vitest";
import OverflowChecker from "./OverflowChecker";
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
});
