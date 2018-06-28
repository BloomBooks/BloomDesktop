/// <reference path="BloomField.ts" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import BloomField from "./BloomField";

function WireUp() {
    $(".bloom-editable").each(function() {
        BloomField.ManageField(this);
    });
}

/* this doesn't work since we retired the bloom-requiresParagraph tag and started using ckeditor.
    However, if you look in a browser, it does in fact get a paragraph.
describe("bloomField", function () {
    beforeEach(function () {
        $('body').html('<head></head><div class="bloom-requiresParagraph"><div id="simple" contenteditable="true" class="bloom-editable"></div></div>');
    });

    it("Putting cursor in a bloom-requiresParagraph field creates a <p>", function () {
        WireUp();
        expect($('div p').length).toBeGreaterThan(0);
    });
});*/
