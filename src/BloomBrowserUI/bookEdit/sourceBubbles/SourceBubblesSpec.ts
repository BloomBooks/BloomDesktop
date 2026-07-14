/// <reference path="./BloomSourceBubbles.tsx" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import $ from "jquery";

vi.mock("../../utils/bloomApi", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("../../utils/bloomApi")>();
    return {
        ...actual,
        postJson: vi.fn(),
        postString: vi.fn(),
    };
});

import { postString } from "../../utils/bloomApi";
import BloomSourceBubbles from "./BloomSourceBubbles";

const mockedPostString = vi.mocked(postString);

describe("SourceBubbles", () => {
    const originalGetSettings = (window as any).GetSettings;

    // reset fixture
    beforeEach(() => {
        $("body").html("");
        mockedPostString.mockReset();
    });
    afterEach(() => {
        $("body").html("");
        (window as any).GetSettings = originalGetSettings;
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

    it("MakeSourceTextDivForGroup keeps non-preferred languages in alphabetical order", () => {
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='sw'>Swahili text</div>",
                "   <div class='bloom-editable' lang='fr'>French text</div>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);

        const oldGetSettings = (window as any).GetSettings;
        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "",
            defaultSourceLanguage2: "",
            currentCollectionLanguage2: "",
            currentCollectionLanguage3: "",
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        (window as any).GetSettings = oldGetSettings;

        const listItems = result.find("nav ul li");
        expect(listItems.length).toBe(3);
        expect(listItems[0].getAttribute("id")).toBe("es");
        expect(listItems[1].getAttribute("id")).toBe("fr");
        expect(listItems[2].getAttribute("id")).toBe("sw");
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

    // --- AI source bubble display tests ---
    //
    // AI-translated source divs (lang tag like "fr-x-ai-deepl") are written into the book
    // by a C# batch process that runs before the book is opened for editing. The front end
    // is display-only: it never requests a translation, never writes an AI div, and never
    // removes one from the live .bloom-translationGroup. These tests exercise only that
    // display behavior.

    it("MakeSourceTextDivForGroup shows one tab per AI-provider div, alongside the normal source tabs", () => {
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "   <div class='bloom-editable bloom-ai-translation' lang='fr-x-ai-deepl' data-ai-fingerprint='abc'>Texte français DeepL</div>",
                "   <div class='bloom-editable bloom-ai-translation' lang='fr-x-ai-google' data-ai-fingerprint='def'>Texte français Google</div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);

        // sanity check: both AI divs are actually present with distinct text before we build the bubble
        expect(
            $("body").find("#testTarget div[lang='fr-x-ai-deepl']").text(),
        ).toBe("Texte français DeepL");
        expect(
            $("body").find("#testTarget div[lang='fr-x-ai-google']").text(),
        ).toBe("Texte français Google");

        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "en",
            defaultSourceLanguage2: "",
            currentCollectionLanguage2: "",
            currentCollectionLanguage3: "",
            allowAiSourceBubbles: true,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        // English is vernacular (no tab); es, fr-x-ai-deepl and fr-x-ai-google each get a tab
        const listItems = result.find("nav ul li");
        expect(listItems.length).toBe(3);
        expect(result.find("li#es").length).toBe(1);
        expect(result.find("li#fr-x-ai-deepl").length).toBe(1);
        expect(result.find("li#fr-x-ai-google").length).toBe(1);
        expect(
            result.find("div.source-text[lang='fr-x-ai-deepl']")[0]
                .childNodes[0].textContent,
        ).toBe("Texte français DeepL");
        expect(
            result.find("div.source-text[lang='fr-x-ai-google']")[0]
                .childNodes[0].textContent,
        ).toBe("Texte français Google");
    });

    it("getLanguageDisplayName labels an AI tab with the language name and mapped provider name", () => {
        const getLanguageDisplayName = (BloomSourceBubbles as any)
            .getLanguageDisplayName as (langTag: string) => string;

        expect(getLanguageDisplayName("fr-x-ai-deepl")).toBe(
            "AI français (DeepL)",
        );
        expect(getLanguageDisplayName("fr-x-ai-google")).toBe(
            "AI français (Google Translate)",
        );
        expect(getLanguageDisplayName("fr-x-ai-alpha2")).toBe(
            "AI français (SIL Alpha2)",
        );
        // an unrecognized provider id is shown as-is
        expect(getLanguageDisplayName("fr-x-ai-mystery")).toBe(
            "AI français (mystery)",
        );
    });

    it("getLanguageDisplayName falls back to the browser's language names for tags Bloom doesn't know", () => {
        const getLanguageDisplayName = (BloomSourceBubbles as any)
            .getLanguageDisplayName as (langTag: string) => string;

        // "de" is not one of the mocked collection languages, so Bloom's own lookup fails;
        // Intl.DisplayNames should supply "German" rather than showing the raw tag.
        expect(getLanguageDisplayName("de-x-ai-deepl")).toBe(
            "AI German (DeepL)",
        );
    });

    it("MakeSourceTextDivForGroup shows the AI icon only on AI tabs, with a provider-labeled tab", () => {
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='fr'>French text</div>",
                "   <div class='bloom-editable bloom-ai-translation' lang='fr-x-ai-deepl'>Texte français DeepL</div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);

        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "en",
            defaultSourceLanguage2: "",
            currentCollectionLanguage2: "",
            currentCollectionLanguage3: "",
            allowAiSourceBubbles: true,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        const aiTab = result.find("li#fr-x-ai-deepl a.sourceTextTab");
        expect(aiTab.length).toBe(1);
        expect(aiTab.find("svg[data-testid='AutoAwesomeIcon']").length).toBe(1);
        expect(aiTab.text().trim()).toBe("AI français (DeepL)");

        const nonAiTab = result.find("li#fr a.sourceTextTab");
        expect(nonAiTab.length).toBe(1);
        expect(nonAiTab.find("svg[data-testid='AutoAwesomeIcon']").length).toBe(
            0,
        );
    });

    it("MakeSourceTextDivForGroup hides AI tabs when allowAiSourceBubbles is false, without touching the live group", () => {
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "   <div class='bloom-editable bloom-ai-translation' lang='fr-x-ai-deepl'>Texte français DeepL</div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);
        const liveGroup = $("body").find("#testTarget");

        // sanity check: the AI div is present in the live book DOM before we build the bubble
        expect(liveGroup.find("div[lang='fr-x-ai-deepl']").length).toBe(1);

        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "en",
            defaultSourceLanguage2: "",
            currentCollectionLanguage2: "",
            currentCollectionLanguage3: "",
            allowAiSourceBubbles: false,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            liveGroup[0],
        );

        expect(result.find("li#fr-x-ai-deepl").length).toBe(0);
        expect(result.find("div[lang='fr-x-ai-deepl']").length).toBe(0);
        expect(result.find("li#es").length).toBe(1);
        // the book's own DOM must be untouched even though the setting is off
        expect(liveGroup.find("div[lang='fr-x-ai-deepl']").length).toBe(1);
        expect(liveGroup.find("div[lang='fr-x-ai-deepl']").text()).toBe(
            "Texte français DeepL",
        );
    });

    it("MakeSourceTextDivForGroup does not show an empty AI div as a tab", () => {
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>English text</div>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "   <div class='bloom-editable bloom-ai-translation' lang='fr-x-ai-deepl'></div>",
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);

        // sanity check: the AI div really is empty before we build the bubble
        expect(
            $("body")
                .find("#testTarget div[lang='fr-x-ai-deepl']")
                .text()
                .trim(),
        ).toBe("");

        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "en",
            defaultSourceLanguage2: "",
            currentCollectionLanguage2: "",
            currentCollectionLanguage3: "",
            allowAiSourceBubbles: true,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        expect(result.find("li#fr-x-ai-deepl").length).toBe(0);
        expect(result.find("div[lang='fr-x-ai-deepl']").length).toBe(0);
        expect(result.find("li#es").length).toBe(1);
    });

    it("AI source bubble tabs are remembered as the default source language", () => {
        const aiLanguageTag = "id-x-ai-deepl";
        const testHtml = $(
            [
                "<div id='testTarget' class='bloom-translationGroup'>",
                "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
                `   <div class='bloom-editable bloom-ai-translation' lang='${aiLanguageTag}'>Bahasa Indonesia</div>`,
                "</div>",
            ].join("\n"),
        );
        $("body").append(testHtml);
        (window as any).GetSettings = () => ({
            defaultSourceLanguage: "tpi",
            defaultSourceLanguage2: "",
            currentCollectionLanguage2: "",
            currentCollectionLanguage3: "",
            allowAiSourceBubbles: true,
        });

        const result = BloomSourceBubbles.MakeSourceTextDivForGroup(
            $("body").find("#testTarget")[0],
        );

        const aiTab = result.find(`li#${CSS.escape(aiLanguageTag)} a`);
        expect(aiTab.length).toBe(1);
        aiTab.get(0)?.dispatchEvent(new MouseEvent("click", { bubbles: true }));

        expect(mockedPostString).toHaveBeenCalledWith(
            "editView/sourceTextTab",
            aiLanguageTag,
        );
    });

    it("styled dropdown handles clicks on the dropdown list item", () => {
        const qtipId = "qtip-0";
        const groupHtml = $(
            [
                `<div id='testTarget' class='bloom-translationGroup' aria-describedby='${qtipId}'>`,
                "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
                "   <div class='bloom-editable' lang='fr'>French text</div>",
                "   <div class='bloom-editable' lang='es'>Spanish text</div>",
                "</div>",
            ].join("\n"),
        );
        const qtip = $(`<div id='${qtipId}' class='qtip'></div>`);
        const divForBubble = BloomSourceBubbles.CreateDropdownIfNecessary(
            $(
                [
                    "<div class='bloom-translationGroup'>",
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
            ),
        );
        qtip.append(divForBubble);
        $("body").append(groupHtml);
        $("body").append(qtip);
        const dropdownItem = divForBubble.find(".dropdown-list li[lang='es']");

        expect(dropdownItem.length).toBe(1);

        dropdownItem
            .get(0)
            ?.dispatchEvent(new MouseEvent("click", { bubbles: true }));

        expect(mockedPostString).toHaveBeenCalledWith(
            "editView/sourceTextTab",
            "es",
        );
    });
});
