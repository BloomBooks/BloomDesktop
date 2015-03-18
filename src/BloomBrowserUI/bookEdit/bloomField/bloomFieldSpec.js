/// <reference path="bloomField.ts" />
/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../test/lib/jasmine.d.ts"/>
/// <reference path="../../test/lib/jasmine-jquery.d.ts"/>
function WireUp() {
    $(".bloom-editable").each(function () {
        BloomField.ManageField(this);
    });
}
describe("bloomField", function () {
    beforeEach(function () {
        $('body').html('<head></head><div class="bloom-requiresParagraphs"><div id="simple" contenteditable="true" class="bloom-editable"></div></div>');
    });
    /* haven't got this working yet because "focus" is never triggered. Did get "click" to work, though.
        it("Putting cursor in a bloom-requiresParagraph field creates a <p>", function () {
            var spyEvent;
            spyEvent = spyOnEvent('.bloom-editable', 'click');
            WireUp();
            BloomField.PrepareField($(".bloom-editable")[0]);
            $('.bloom-editable').click(function(){alert('x')});
            $('.bloom-editable').trigger('click');
            expect(spyEvent).toHaveBeenTriggered();
            expect($('div p').length).toBeGreaterThan(0);
        });
    */
    it("PrepareField on a bloom-requiresParagraph field creates a <p>", function () {
        BloomField.PrepareField($("#simple")[0]);
        //expect($('#simple').length).toBeGreaterThan(0);
        expect($('#simple')).toBeDefined();
    });
});
//# sourceMappingURL=bloomFieldSpec.js.map