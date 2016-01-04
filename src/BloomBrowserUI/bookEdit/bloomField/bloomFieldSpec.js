/// <reference path="bloomField.ts" />
/// <reference path="../../lib/jasmine/jasmine.d.ts"/>
function WireUp() {
    $(".bloom-editable").each(function () {
        BloomField.ManageField(this);
    });
}
describe("bloomField", function () {
    beforeEach(function () {
        $('body').html('<head></head><div class="bloom-requiresParagraphs"><div id="simple" contenteditable="true" class="bloom-editable"></div></div>');
    });
    it("Putting cursor in a bloom-requiresParagraph field creates a <p>", function () {
        WireUp();
        expect($('div p').length).toBeGreaterThan(0);
    });
});
//# sourceMappingURL=bloomFieldSpec.js.map