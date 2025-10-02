/// <reference path="./BloomSourceBubbles.tsx" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import $ from "jquery";
import BloomSourceBubbles from "./BloomSourceBubbles";

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
                "   <div class='bloom-editable' lang='es'>  Spanish   text  </div>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='fr'>French&nbsp;&nbsp; &nbsp; text</div>",
                "   <div class='bloom-editable' lang='tpi'><p>Tok Pisin text</p></div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);
        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
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
            '<a class="sourceTextTab" href="#tpi">Tok Pisin</a>',
        );
        expect(result.find("li#fr").html()).toBe(
            '<a class="sourceTextTab" href="#fr">français</a>',
        );
        expect(listItems.last().html()).toBe(
            '<a class="sourceTextTab" href="#es">español</a>',
        );
        expect(result.find("div.source-text").length).toBe(3);
        expect(
            result.find("div.source-text[lang=es]")[0].childNodes[0]
                .textContent,
        ).toBe(" Spanish text ");
        expect(
            result.find("div.source-text[lang=fr]")[0].childNodes[0]
                .textContent,
        ).toBe("French text");
        expect(
            result.find("div.source-text[lang=tpi]")[0].childNodes[0]
                .textContent,
        ).toBe("Tok Pisin text");
    });

    it("Run MakeSourceTextDivForGroup with pre-defined settings including defaultSourceLanguage2", () => {
        // Test tab ordering with both defaultSourceLanguage and defaultSourceLanguage2
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='fr'>French text</div>",
                "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);

        // Mock GetSettings() to return both source languages
        // Original test already assumes defaultSourceLanguage = 'en'
        // Here we'll pretend 'fr' was the second most recently used source language
        const oldGetSettings = (window as any).GetSettings;
        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "en",
            defaultSourceLanguage2: "fr",
            currentCollectionLanguage2: "tpi",
            currentCollectionLanguage3: null,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        // Restore original GetSettings
        (window as any).GetSettings = oldGetSettings;

        // English is vernacular so no tab, French should be first since it's defaultSourceLanguage2,
        // Tok Pisin second since it's currentCollectionLanguage2, Spanish last alphabetically
        const listItems = result.find("nav ul li");
        expect(listItems.length).toBe(3);
        expect(listItems[0].getAttribute("id")).toBe("fr");
        expect(listItems[1].getAttribute("id")).toBe("tpi");
        expect(listItems[2].getAttribute("id")).toBe("es");
    });

    it("Run CreateDropdownIfNecessary with pre-defined settings", () => {
        const testHtml = $(
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
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);
        const result = BloomSourceBubbles.CreateDropdownIfNecessary(
            $("body").find("#testTarget"),
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
        const listItems = result.find("nav > ul > li");
        expect(listItems.length).toBe(3);
        expect(listItems.first().html()).toBe(
            '<a class="sourceTextTab" href="#tpi">Tok Pisin</a>',
        );
        const frenchTab = result.find("li#fr");
        expect(frenchTab.html()).toBe(
            '<a class="sourceTextTab" href="#fr">français</a>',
        );
        const dropdown = listItems.last();
        expect(dropdown.hasClass("dropdown-menu")).toBe(true);
        const dropItems = dropdown.find("ul li");
        expect(dropItems.length).toBe(1);
        expect(dropItems.first().html()).toBe(
            '<a class="sourceTextTab" href="#es">español</a>',
        );
        const dropChildren = dropdown.children();
        expect(dropChildren.length).toBe(2); // including div holding number and ul holding dropdown items
        const topLevelDivs = $("#testTarget > div");
        expect(topLevelDivs.length).toBe(3);
    });

    it("CreateDropdownIfNecessary doesn't if doesn't need to", () => {
        const testHtml = $(
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
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);
        const result = BloomSourceBubbles.CreateDropdownIfNecessary(
            $("body").find("#testTarget"),
        );
        const listItems = result.find("nav > ul > li");
        expect(listItems.length).toBe(2); // this is why we don't need a dropdown
        expect(listItems.first().html()).toBe(
            '<a class="sourceTextTab" href="#tpi">Tok Pisin</a>',
        );
        const frenchTab = listItems.last();
        expect(frenchTab.html()).toBe(
            '<a class="sourceTextTab" href="#fr">français</a>',
        );
        const srcTexts = result.find(".source-text");
        expect(srcTexts.length).toBe(2);
    });

    it("MakeSourceTextDivForGroup handles missing defaultSourceLanguage2 content", () => {
        // Test when defaultSourceLanguage2 refers to a language not in the group
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);

        const oldGetSettings = (window as any).GetSettings;
        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "en",
            defaultSourceLanguage2: "fr", // fr content doesn't exist in the group
            currentCollectionLanguage2: "tpi",
            currentCollectionLanguage3: null,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        // Restore original GetSettings
        (window as any).GetSettings = oldGetSettings;

        // Should just ignore the missing fr and continue with normal ordering
        const listItems = result.find("nav ul li");
        expect(listItems.length).toBe(2);
        expect(listItems[0].getAttribute("id")).toBe("tpi"); // collection lang 2
        expect(listItems[1].getAttribute("id")).toBe("es"); // alphabetical
    });

    it("MakeSourceTextDivForGroup handles when defaultSourceLanguage2 equals defaultSourceLanguage", () => {
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='fr'>French text</div>",
                "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);

        const oldGetSettings = (window as any).GetSettings;
        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "fr",
            defaultSourceLanguage2: "fr", // Same as defaultSourceLanguage
            currentCollectionLanguage2: "tpi",
            currentCollectionLanguage3: null,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        // Restore original GetSettings
        (window as any).GetSettings = oldGetSettings;

        // fr should only appear once
        const listItems = result.find("nav ul li");
        expect(listItems.length).toBe(3);
        expect(listItems[0].getAttribute("id")).toBe("fr"); // first source lang
        expect(listItems[1].getAttribute("id")).toBe("tpi"); // collection lang 2
        expect(listItems[2].getAttribute("id")).toBe("es"); // alphabetical
    });
});
