"use strict";

function PasteTextIntoDiv(id, text) {
    var fullId = "#" + id;
    $(fullId).prop('selected', 'selected');
    $(fullId).trigger('paste');
}

describe("Overflow Tests", function () {
    // most perplexingly, jasmine doesn't reset the dom between tests
    beforeEach(function () {
        $('body').html('');
    });

    it("basic empty field doesn't overflow", function () {
        // setup test
        $('body').append("<div id='testTarget' class='bloom-editable'></div><div id='textToCopy'>A</div>");
        AddOverflowHandler();
        $("#textToCopy").prop('selected', 'selected');
        $("#textToCopy").trigger("copy");
        $("#testTarget").trigger("paste"); // SUT
        console.log('Pasted text: '+$('#testTarget').text());
        var overflowing = $("#testTarget").hasClass('overflow');
        expect(overflowing).toBe(false);
    });
});