"use strict";
import { describe, it, expect } from "vitest";
import OverflowChecker from "../../bookEdit/OverflowChecker/OverflowChecker";
import { removeTestRoot } from "../../utils/testHelper";
import OverflowAncestorFixture from "./OverflowAncestorFixture.pug";
import OverflowFixture from "./OverflowFixture.pug";
import OverflowMarginFixture from "./OverflowMarginFixture.pug";

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
describe("Overflow Tests", () => {
    //jasmine.getFixtures().fixturesPath = "base/bookEdit/OverflowChecker";

    // Clean up before running the test. Other test's divs can affect the font size and hence the overflow.
    beforeAll(removeTestRoot);

    // Note: Ideally, nothing else should run between loadFixtures and actually running the test.
    // That means loadFixtures() needs to be inside the it().

    it("Check test page for Self overflows (assumes Arial is installed)", () => {
        document.body.innerHTML = OverflowFixture;
        expect($("#jasmine-fixtures")).toBeTruthy();
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Overflow tests...");
        }
        $(".myTest").each((index, element) => RunTest(index, element));
    });

    it("Check test page for Margin overflows (assumes Arial is installed)", () => {
        document.body.innerHTML = OverflowMarginFixture;
        expect($("#jasmine-fixtures")).toBeTruthy();
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
        expect($("#jasmine-fixtures")).toBeTruthy();
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Fixed Ancestor Overflow tests...");
        }
        $(".myTest").each((index, element) =>
            RunAncestorMarginTest(index, element as HTMLElement),
        );
    });
});
