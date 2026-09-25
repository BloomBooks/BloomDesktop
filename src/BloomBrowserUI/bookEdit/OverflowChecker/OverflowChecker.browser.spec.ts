import { describe, it, expect } from "vitest";
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
