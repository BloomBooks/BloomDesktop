"use strict";

var consoleDef = false;

jQuery.fn.RunTests = function() {
    $.each(this, RunTest);
};

var RunTest = function(index, value) {
    var testHtml = $(value);
    var nameAttr = testHtml.attr("name");
    if(typeof nameAttr === 'undefined')
        nameAttr = '***** This test needs a name! *****';
    if(consoleDef)
        console.log('\nBeginning test # '+ index + ' ' + nameAttr);
    var overflowing = testHtml.IsOverflowing();
    var testExpectation = testHtml.hasClass('expectToOverflow');
    if(consoleDef) {
        console.log('  scrollH: ' + testHtml[0].scrollHeight + ' clientH: ' + testHtml[0].clientHeight);
        console.log('    Height: ' + testHtml.height());
        var styleAttr = testHtml.attr("style");
        if(typeof styleAttr === 'undefined')
            styleAttr = 'No styles';
        console.log('   Test Style: ' + styleAttr);
        var cs = window.getComputedStyle(testHtml[0], null);
        var lineH = cs.getPropertyValue('line-height');
        var fontS = cs.getPropertyValue('font-size');
        var font = cs.getPropertyValue('font-family');
        var padding = cs.getPropertyValue('padding');
        console.warn('     Computed Style: line-height ' + lineH + ' font-size ' + fontS + ' padding ' + padding);
        console.warn('     Overflow: ' + overflowing + ' font: ' + font);
    }
    expect(overflowing).toBe(testExpectation);
};

// Uses jasmine-query-1.3.1.js
describe("Overflow Tests", function () {
    jasmine.getFixtures().fixturesPath = 'base/test/fixtures';

    it("Check test page for overflows", function() {
        loadFixtures('OverflowTestPage.htm');
        expect($('#jasmine-fixtures')).toBeTruthy();
        if(window.console && window.console.log) {
            consoleDef = true;
            console.log('Commencing Overflow tests...');
        }
        $(".myTest").RunTests();
    });
});