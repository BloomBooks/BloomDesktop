"use strict";

jQuery.fn.RunTests = function() {
    $.each(this, RunTest);
};

var RunTest = function(index, value) {
    var nameAttr = $(value).attr("name");
    var name;
    if(typeof nameAttr === 'undefined')
        name = '***** This test needs a name! *****';
    else
        name = nameAttr;
    console.log('Beginning test # '+ index + ' ' + name);
    var overflowing = $(value).IsOverflowing();
    console.log("Overflow: " + overflowing);
    expect(overflowing).toBe($(value).hasClass('expectToOverflow'));
};

// Uses jasmine-query-1.3.1.js
describe("Overflow Tests", function () {
    jasmine.getFixtures().fixturesPath = 'base/test/fixtures';

    it("Check test page for overflows", function() {
        loadFixtures('OverflowTestPage.htm');
        expect($('#jasmine-fixtures')).toBeTruthy();
        console.log("Commencing Overflow tests...");
        $(".myTest").RunTests();
    });
});