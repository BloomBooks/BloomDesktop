"use strict";

describe("Overflow Tests", function () {
    /*beforeEach(function () {
    });*/

    it("Check test page for overflows", function() {
        $(document).load('test/OverflowTestPage.html');
        $(".testTarget").each(function () {
            var overflowing = $(this).IsOverflowing();
            expect(overflowing).toBe($(this).hasClass('expectToPass'));
        });
    });
});