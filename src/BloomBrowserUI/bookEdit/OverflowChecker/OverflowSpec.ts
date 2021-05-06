"use strict";
/// <reference path="../../typings/jasmine/jasmine.d.ts"/>
/// <reference path="../../typings/jasmine-jquery/jasmine-jquery.d.ts"/>

import "jasmine-jquery";
import OverflowChecker from "../../bookEdit/OverflowChecker/OverflowChecker";
import { removeTestRoot } from "../../utils/testHelper";

var consoleDef = false;

function RunTest(index, value) {
    var testHtml = $(value);
    var nameAttr = testHtml.attr("name");
    if (typeof nameAttr === "undefined")
        nameAttr = "***** This test needs a name! *****";
    if (consoleDef) console.log("\nBeginning test # " + index + " " + nameAttr);
    var overflowingSelf = OverflowChecker.IsOverflowingSelf(testHtml[0]);
    var testExpectation = testHtml.hasClass("expectToOverflow");
    if (consoleDef) {
        console.log(
            "  scrollH: " +
                testHtml[0].scrollHeight +
                " clientH: " +
                testHtml[0].clientHeight
        );
        console.log("    Height: " + testHtml.height());
        var styleAttr = testHtml.attr("style");
        if (typeof styleAttr === "undefined") styleAttr = "No styles";
        console.log("   Test Style: " + styleAttr);
        var cs = window.getComputedStyle(testHtml[0], null);
        var lineH = cs.getPropertyValue("line-height");
        var fontS = cs.getPropertyValue("font-size");
        var font = cs.getPropertyValue("font-family");
        var padding = cs.getPropertyValue("padding");
        console.log(
            "     Computed Style: line-height " +
                lineH +
                " font-size " +
                fontS +
                " padding " +
                padding
        );
        console.log("     OverflowSelf: " + overflowingSelf + " font: " + font);
        // added this because the failure message is not always immediately after the test output
        console.log("     Expecting: " + testExpectation);
    }
    expect(overflowingSelf).toBe(testExpectation);
}

function RunAncestorMarginTest(index: number, value: HTMLElement) {
    var testHtml = $(value);
    var nameAttr = testHtml.attr("name");
    if (typeof nameAttr === "undefined")
        nameAttr = "***** This test needs a name! *****";
    if (consoleDef) console.log("\nBeginning test # " + index + " " + nameAttr);
    var overflowingAncestor = OverflowChecker.overflowingAncestor(testHtml[0]);
    var overflowingMargins = overflowingAncestor != null;
    var testExpectation = testHtml.hasClass("expectToOverflow");
    if (consoleDef) {
        console.log(
            "  scrollH: " +
                testHtml[0].scrollHeight +
                " clientH: " +
                testHtml[0].clientHeight
        );
        console.log("    Height: " + testHtml.height());
        var styleAttr = testHtml.attr("style");
        if (typeof styleAttr === "undefined") styleAttr = "No styles";
        console.log("   Test Style: " + styleAttr);
        var cs = window.getComputedStyle(testHtml[0], null);
        var lineH = cs.getPropertyValue("line-height");
        var fontS = cs.getPropertyValue("font-size");
        var font = cs.getPropertyValue("font-family");
        var padding = cs.getPropertyValue("padding");
        console.log(
            "     Computed Style: line-height " +
                lineH +
                " font-size " +
                fontS +
                " padding " +
                padding
        );
        console.log(
            "     OverflowMargins: " + overflowingMargins + " font: " + font
        );
        // added this because the failure message is not always immediately after the test output
        console.log("     Expecting: " + testExpectation);
    }
    expect(overflowingMargins).toBe(testExpectation);
}

// Uses jasmine-query-1.3.1.js
describe("Overflow Tests", () => {
    jasmine.getFixtures().fixturesPath = "base/bookEdit/OverflowChecker";

    // Clean up before running the test. Other test's divs can affect the font size and hence the overflow.
    beforeAll(removeTestRoot);

    // these tests are only reliable when tested with Firefox
    if (navigator.userAgent.indexOf("Firefox") === -1) {
        console.log("Overflow tests are only run on Firefox.");
        return;
    }

    // Note: Ideally, nothing else should run between loadFixtures and actually running the test.
    // That means loadFixtures() needs to be inside the it().

    it("Check test page for Self overflows", () => {
        loadFixtures("OverflowFixture.html");
        expect($("#jasmine-fixtures")).toBeTruthy();
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Overflow tests...");
        }
        $(".myTest").each((index, element) => RunTest(index, element));
    });

    it("Check test page for Margin overflows", () => {
        loadFixtures("OverflowMarginFixture.html");
        expect($("#jasmine-fixtures")).toBeTruthy();
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Margin Overflow tests...");
        }
        $(".myTest").each((index, element) =>
            RunAncestorMarginTest(index, element as HTMLElement)
        );
    });

    it("Check test page for Fixed Ancestor overflows", () => {
        loadFixtures("OverflowAncestorFixture.html");
        expect($("#jasmine-fixtures")).toBeTruthy();
        if (window.console) {
            consoleDef = true;
            console.log("Commencing Fixed Ancestor Overflow tests...");
        }
        $(".myTest").each((index, element) =>
            RunAncestorMarginTest(index, element as HTMLElement)
        );
    });
});
