/// <reference path="./BloomSourceBubbles.tsx" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>

import BloomSourceBubbles from "./BloomSourceBubbles";
"use strict";

describe("SourceBubbles", () => {
    // reset fixture
    beforeEach(() => {
        $("body").html("");
    });
    afterEach(() => {
        $("body").html("");
    });
    it("Run MakeSourceTextDivForGroup with pre-defined settings", () => {
        // TODO: Testing is a bit hampered by not being able (currently) to put test values
        // into the cSharpDependencyInjector version of GetSettings(). Someday it might
        // be worth modifying that file so that tests can setup their own values for:

        // defaultSourceLanguage ('en' in tests; also marked vernacular)
        // currentCollectionLanguage2 ('tpi' in tests)
        // currentCollectionLanguage3 ('fr' in tests)

        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='fr'>French text</div>",
                "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
                "</div>"
            ].join("\n")
        );
        $("body").append(testHtml);
        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0]
        );

        // English in test is vernacular, so no tab for it
        // Tok Pisin tab gets moved to first place, since it is currentCollectionLanguage2
        // French is second, since it is currentCollectionLanguage3
        // So result should contain:
        // <nav>
        //   <ul>
        //     <li id='tpi'><a class="sourceTextTab" href="#tpi">Tok Pisin</a></li>
        //     <li id='fr'><a class="sourceTextTab" href="#fr">français</a></li>
        //     <li id='es'><a class=\"sourceTextTab\" href=\"#es\">español</a></li>
        //   </ul>
        // </nav>
        // <div class='bloom-editable' lang='es'>Spanish text</div> (order not important here)
        // <div class='bloom-editable' lang='fr'>French text</div>
        // <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>
        const listItems = result.find("nav ul li");
        expect(listItems.length).toBe(3);
        expect(listItems.first().html()).toBe(
            '<a class="sourceTextTab" href="#tpi">Tok Pisin</a>'
        );
        expect(result.find("li#fr").html()).toBe(
            '<a class="sourceTextTab" href="#fr">français</a>'
        );
        expect(listItems.last().html()).toBe(
            '<a class="sourceTextTab" href="#es">español</a>'
        );
        expect(result.find("div.source-text").length).toBe(3);
    });

    it("Run CreateDropdownIfNecessary with pre-defined settings", () => {
        var testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <nav>",
                "     <ul>",
                "       <li id='tpi'><a class='sourceTextTab' href='#tpi'>Tok Pisin</a></li>",
                "       <li id='fr'><a class='sourceTextTab' href='#fr'>français</a></li>",
                "       <li id='es'><a class='sourceTextTab' href='#es'>español</a></li>",
                "    </ul>",
                "   </nav>",
                "   <div class='source-text' lang='es'>Spanish text</div>",
                "   <div class='source-text' lang='fr'>French text</div>",
                "   <div class='source-text' lang='tpi'>Tok Pisin text</div>",
                "</div>"
            ].join("\n")
        );
        $("body").append(testHtml);
        var result = BloomSourceBubbles.CreateDropdownIfNecessary(
            $("body").find("#testTarget")
        );
        // result should contain:
        // <nav>
        //   <ul>
        //     <li id='tpi'><a class="sourceTextTab" href="#tpi">Tok Pisin</a></li>
        //     <li id='fr'><a class="sourceTextTab" href="#fr">français</a></li>
        //     <li class='dropdown-menu'>
        //       <div>1</div>
        //       <ul class='dropdown-list'>
        //         <li lang='es'><a class="sourceTextTab" href="#es">español</a></li>
        //       </ul>
        //     </li>
        //   </ul>
        // </nav>
        // <div class='bloom-editable' lang='es'>Spanish text</div> (order not important here)
        // <div class='bloom-editable' lang='fr'>French text</div>
        // <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>
        var listItems = result.find("nav > ul > li");
        expect(listItems.length).toBe(3);
        expect(listItems.first().html()).toBe(
            '<a class="sourceTextTab" href="#tpi">Tok Pisin</a>'
        );
        var frenchTab = result.find("li#fr");
        expect(frenchTab.html()).toBe(
            '<a class="sourceTextTab" href="#fr">français</a>'
        );
        var dropdown = listItems.last();
        expect(dropdown.hasClass("dropdown-menu")).toBe(true);
        var dropItems = dropdown.find("ul li");
        expect(dropItems.length).toBe(1);
        expect(dropItems.first().html()).toBe(
            '<a class="sourceTextTab" href="#es">español</a>'
        );
        var dropChildren = dropdown.children();
        expect(dropChildren.length).toBe(2); // including div holding number and ul holding dropdown items
        var topLevelDivs = $("#testTarget > div");
        expect(topLevelDivs.length).toBe(3);
    });

    it("CreateDropdownIfNecessary doesn't if doesn't need to", () => {
        var testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <nav>",
                "     <ul>",
                "       <li id='tpi'><a class='sourceTextTab' href='#tpi'>Tok Pisin</a></li>",
                "       <li id='fr'><a class='sourceTextTab' href='#fr'>français</a></li>",
                "    </ul>",
                "   </nav>",
                "   <div class='source-text' lang='fr'>French text</div>",
                "   <div class='source-text' lang='tpi'>Tok Pisin text</div>",
                "</div>"
            ].join("\n")
        );
        $("body").append(testHtml);
        var result = BloomSourceBubbles.CreateDropdownIfNecessary(
            $("body").find("#testTarget")
        );
        var listItems = result.find("nav > ul > li");
        expect(listItems.length).toBe(2); // this is why we don't need a dropdown
        expect(listItems.first().html()).toBe(
            '<a class="sourceTextTab" href="#tpi">Tok Pisin</a>'
        );
        var frenchTab = listItems.last();
        expect(frenchTab.html()).toBe(
            '<a class="sourceTextTab" href="#fr">français</a>'
        );
        var srcTexts = result.find(".source-text");
        expect(srcTexts.length).toBe(2);
    });
});
