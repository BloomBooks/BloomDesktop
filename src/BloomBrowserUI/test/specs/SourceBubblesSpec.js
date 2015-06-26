/// <reference path="../../bookEdit/js/bloomSourceBubbles.ts" />
/// <reference path="../../lib/jasmine/jasmine.d.ts"/>
"use strict";
describe("bloomSourceBubbles", function () {
    // reset fixture
    beforeEach(function () {
        $('body').html('');
    });
    it("Run MakeSourceTextDivForGroup with pre-defined settings", function () {
        // TODO: Testing is a bit hampered by not being able (currently) to put test values
        // into the cSharpDependencyInjector version of GetSettings(). Someday it might
        // be worth modifying that file so that tests can setup their own values for:
        // defaultSourceLanguage ('en' in tests; also marked vernacular)
        // currentCollectionLanguage2 ('tpi' in tests)
        // currentCollectionLanguage3 ('fr' in tests)
        var testHtml = $([
            "<div id='testTarget' class='bloom-translationGroup'>",
            "   <div class='bloom-editable' lang='es'>Spanish text</div>",
            "   <div class='bloom-editable' lang='en'>English text</div>",
            "   <div class='bloom-editable' lang='fr'>French text</div>",
            "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
            "</div>"
        ].join("\n"));
        $('body').append(testHtml);
        var result = bloomSourceBubbles.MakeSourceTextDivForGroup($('body').find('#testTarget')[0]);
        var listItems = result.find('nav ul li');
        expect(listItems.length).toBe(3); // English in test is vernacular, so no tab for it
        // Tok Pisin tab gets moved to first place, since it is currentCollectionLanguage2
        expect(listItems.first().html()).toBe("<a class=\"sourceTextTab\" href=\"#tpi\">Tok Pisin</a>");
        expect(listItems.last().html()).toBe("<a class=\"sourceTextTab\" href=\"#es\">español</a>");
        expect(result.find('li#fr').html()).toBe("<a class=\"sourceTextTab\" href=\"#fr\">français</a>");
        expect(result.find('div').length).toBe(4); // including English
    });
});
//# sourceMappingURL=SourceBubblesSpec.js.map