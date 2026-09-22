"use strict";
import { describe, it, expect, beforeAll, beforeEach } from "vitest";
import OverflowChecker from "../../bookEdit/OverflowChecker/OverflowChecker";
import { removeTestRoot } from "../../utils/testHelper";
import OverflowAncestorFixture from "./OverflowAncestorFixture.html?raw";
import OverflowFixture from "./OverflowFixture.html?raw";
import OverflowMarginFixture from "./OverflowMarginFixture.html?raw";
import $ from "jquery";

let consoleDef = false;

function RunTest(index, value) {
    const testHtml = $(value);
    let nameAttr = testHtml.attr("name");
    if (typeof nameAttr === "undefined")
        nameAttr = "***** This test needs a name! *****";
    if (consoleDef) console.log("\nBeginning test # " + index + " " + nameAttr);
    const overflowingSelf = OverflowChecker.IsOverflowingSelf(testHtml[0]);
    const testExpectation = testHtml.hasClass("expectToOverflow");
    if (consoleDef) {
        console.log(
            "  scrollH: " +
                testHtml[0].scrollHeight +
                " clientH: " +
                testHtml[0].clientHeight,
        );
        console.log("    Height: " + testHtml.height());
        let styleAttr = testHtml.attr("style");
        if (typeof styleAttr === "undefined") styleAttr = "No styles";
        console.log("   Test Style: " + styleAttr);
        const cs = window.getComputedStyle(testHtml[0], null);
        const lineH = cs.getPropertyValue("line-height");
        const fontS = cs.getPropertyValue("font-size");
        const font = cs.getPropertyValue("font-family");
        const padding = cs.getPropertyValue("padding");
        console.log(
            "     Computed Style: line-height " +
                lineH +
                " font-size " +
                fontS +
                " padding " +
                padding,
        );
        console.log("     OverflowSelf: " + overflowingSelf + " font: " + font);
        // added this because the failure message is not always immediately after the test output
        console.log("     Expecting: " + testExpectation);
    }
    expect(overflowingSelf).toBe(testExpectation);
}

function RunAncestorMarginTest(index: number, value: HTMLElement) {
    const testHtml = $(value);
    let nameAttr = testHtml.attr("name");
    if (typeof nameAttr === "undefined")
        nameAttr = "***** This test needs a name! *****";
    if (consoleDef) console.log("\nBeginning test # " + index + " " + nameAttr);
    const overflowingAncestor = OverflowChecker.overflowingAncestor(
        testHtml[0],
    );
    const overflowingMargins = overflowingAncestor != null;
    const testExpectation = testHtml.hasClass("expectToOverflow");
    if (consoleDef) {
        console.log(
            "  scrollH: " +
                testHtml[0].scrollHeight +
                " clientH: " +
                testHtml[0].clientHeight,
        );
        console.log("    Height: " + testHtml.height());
        let styleAttr = testHtml.attr("style");
        if (typeof styleAttr === "undefined") styleAttr = "No styles";
        console.log("   Test Style: " + styleAttr);
        const cs = window.getComputedStyle(testHtml[0], null);
        const lineH = cs.getPropertyValue("line-height");
        const fontS = cs.getPropertyValue("font-size");
        const font = cs.getPropertyValue("font-family");
        const padding = cs.getPropertyValue("padding");
        console.log(
            "     Computed Style: line-height " +
                lineH +
                " font-size " +
                fontS +
                " padding " +
                padding,
        );
        console.log(
            "     OverflowMargins: " + overflowingMargins + " font: " + font,
        );
        // added this because the failure message is not always immediately after the test output
        console.log("     Expecting: " + testExpectation);
    }
    expect(overflowingMargins).toBe(testExpectation);
}

// Uses jasmine-query-1.3.1.js
describe.skip("Overflow Tests", () => {
    // SKIPPED: These tests require actual layout calculations (scrollHeight, clientHeight, getComputedStyle with real values)
    // which jsdom cannot provide. They need a real browser with layout engine.
    // To run these tests, use the old Karma/Chrome test runner, or set up Vitest browser mode
    // (see vitest.browser.config.ts - currently has dependency resolution issues with jQuery).
    // These tests passed in Karma.

    //jasmine.getFixtures().fixturesPath = "base/bookEdit/OverflowChecker";

    // Clean up before running the test. Other test's divs can affect the font size and hence the overflow.
    beforeAll(removeTestRoot);

    // Note: Ideally, nothing else should run between loadFixtures and actually running the test.
    // That means loadFixtures() needs to be inside the it().

    it("Check test page for Self overflows (assumes Arial is installed)", () => {
        document.body.innerHTML = OverflowFixture;
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Overflow tests...");
        }
        $(".myTest").each((index, element) => RunTest(index, element));
    });

    it("Check test page for Margin overflows (assumes Arial is installed)", () => {
        document.body.innerHTML = OverflowMarginFixture;
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Margin Overflow tests...");
        }
        $(".myTest").each((index, element) =>
            RunAncestorMarginTest(index, element as HTMLElement),
        );
    });

    it("Check test page for Fixed Ancestor overflows (assumes Arial is installed)", () => {
        document.body.innerHTML = OverflowAncestorFixture;
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Fixed Ancestor Overflow tests...");
        }
        $(".myTest").each((index, element) =>
            RunAncestorMarginTest(index, element as HTMLElement),
        );
    });
});

// The padding-bottom that bloom-padForOverflow boxes measure is kept in the style attribute,
// which Bloom syncs between all copies of a data-book field. So the title, which appears on
// several xmatter pages, must only be measured on the front cover (BL-16811).
describe("OverflowChecker.shouldMeasurePaddingForOverflow", () => {
    const makeEditable = (
        pageClasses: string | null,
        dataBook: string,
    ): HTMLElement => {
        const editable = document.createElement("div");
        editable.className = "bloom-editable bloom-padForOverflow";
        editable.setAttribute("data-book", dataBook);
        if (pageClasses !== null) {
            const page = document.createElement("div");
            page.className = pageClasses;
            const group = document.createElement("div");
            group.className = "bloom-translationGroup";
            page.appendChild(group);
            group.appendChild(editable);
            document.body.appendChild(page);
        } else {
            document.body.appendChild(editable);
        }
        return editable;
    };

    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("measures the title on the front cover", () => {
        const editable = makeEditable(
            "bloom-page bloom-frontMatter frontCover outsideFrontCover",
            "bookTitle",
        );
        expect(editable.closest(".bloom-page")).not.toBeNull(); // sanity check on the setup
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            true,
        );
    });

    it("does not measure the title on the title page", () => {
        const editable = makeEditable(
            "bloom-page bloom-frontMatter titlePage",
            "bookTitle",
        );
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            false,
        );
    });

    it("does not measure a title that is on no page at all", () => {
        const editable = makeEditable(null, "bookTitle");
        expect(editable.closest(".bloom-page")).toBeNull(); // sanity check on the setup
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            false,
        );
    });

    it("still measures other padded fields wherever they are", () => {
        const editable = makeEditable(
            "bloom-page bloom-frontMatter credits",
            "author",
        );
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            true,
        );
    });
});
