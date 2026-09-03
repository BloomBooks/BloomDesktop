import { describe, it, expect, beforeEach, vi } from "vitest";
/// <reference path="./StyleEditor.ts" />
/// <reference path="../../typings/jquery/jquery.d.ts" />

/*/// <reference path="../../lib/jquery-1.9.1.js"/>*/

import StyleEditor from "./StyleEditor";
import $ from "jquery";

//this was getting html, but just setting the rules actually doesn't touch the html
//function GetStylesAfterMakeBigger(): string {
//    var target = $(document).find('.fooStyle');
//    var editor = new StyleEditor(<HTMLElement><any>document);
//    editor.MakeBigger(<HTMLElement><any>target);
//    return (<HTMLElement>GetUserModifiedStyleSheet().ownerNode).outerHTML;
//}

// This test file has a somewhat messy history. Once, we had up and down arrows for font size,
// and the editor itself had MakeBigger and MakeSmaller functions which manipulated style rules.
// We had lots of tests for MakeBigger, which also tested other aspects of the style editor
// functions. Then we changed to a combo box, and later made some changes to the author/translate
// behavior distinction. To preserve as many of the tests as still made sense, I made a version
// of MakeBigger that uses surviving code paths.
function MakeBigger(shouldSetDefaultRule = true) {
    MakeBigger2("#testTarget", shouldSetDefaultRule);
}

function MakeBigger2(target: string, shouldSetDefaultRule = true) {
    const oldSize = GetFontSize(target);
    ChangeSizeAbsolute(target, oldSize + 2, shouldSetDefaultRule);
}

function GetFontSize(target: string): number {
    const jQueryTarget = $(document).find(target);
    const editor = new StyleEditor(
        "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
    );
    return editor.GetCalculatedFontSizeInPoints(<HTMLElement>jQueryTarget[0]);
}

function ChangeSizeAbsolute(
    target: string,
    newSize: number,
    shouldSetDefaultRule = true,
) {
    const jQueryTarget = $(document).find(target);
    const editor = new StyleEditor(
        "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
    );
    editor.boxBeingEdited = jQueryTarget.get(0);
    editor.changeSizeInternal(newSize + "pt", shouldSetDefaultRule);
}

function GetUserModifiedStyleSheet(): CSSStyleSheet | undefined {
    for (let i = 0; i < document.styleSheets.length; i++) {
        if (document.styleSheets[i].title === "userModifiedStyles")
            return <CSSStyleSheet>document.styleSheets[i];
    }
    return undefined;
}

function GetFooStyleRuleFontSize(): number {
    const sizeString = $(".foo-style").css("font-size");
    return parseInt(sizeString.substr(0, sizeString.length - 2));
}

function GetFontSizeRuleByLang(lang: string): number {
    const rule = GetRuleMatchingSelector('.foo-style[lang="' + lang + '"]');
    if (rule == null) return -1;
    return ParseRuleForFontSize(rule.cssText);
}

function ParseRuleForFontSize(ruleText: string): number {
    const ruleString = "font-size: ";
    const beginPoint = ruleText.indexOf(ruleString) + ruleString.length;
    //var endPoint = ruleText.indexOf(' !important');
    const endPoint = ruleText.indexOf(" !");
    if (beginPoint < 1 || endPoint < beginPoint) return -1;
    const sizeString = ruleText.substr(beginPoint, endPoint - beginPoint);
    return parseFloat(sizeString); // parseFloat() handles units fine!
}

function GetRuleForFooStyle(): CSSRule | null {
    const x = GetUserModifiedStyleSheet()?.cssRules;
    if (!x) return null;

    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf("foo-style") > -1) {
            return x[i];
        }
    }
    return null;
}

function GetRuleForNormalStyle(): CSSRule | null {
    const x = GetUserModifiedStyleSheet()?.cssRules;
    if (!x) return null;

    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf("normal-style") > -1) {
            return x[i];
        }
    }
    return null;
}

function GetRuleForCoverTitleStyle(): CSSRule | null {
    const x = GetUserModifiedStyleSheet()?.cssRules;
    if (!x) return null;
    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf("Title-On-Cover-style") > -1) {
            return x[i];
        }
    }
    return null;
}

function GetCalculatedFontSize(target: string): number {
    const jQueryTarget = $(document).find(target);
    const editor = new StyleEditor(
        "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
    );
    return editor.GetCalculatedFontSizeInPoints(<HTMLElement>jQueryTarget[0]);
}

function GetRuleMatchingSelector(selector: string): CSSRule | null {
    const sheet = GetUserModifiedStyleSheet();
    const x = sheet?.cssRules;
    if (!x) return null;
    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf(selector) > -1) {
            return x[i];
        }
    }
    return null;
}

function HasRuleMatchingThisSelector(selector: string): boolean {
    const sheet = GetUserModifiedStyleSheet();
    const x = sheet?.cssRules;
    if (!x) return false;
    let count = 0;
    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf(selector) > -1) {
            ++count;
        }
    }
    return count > 0;
}

function countFooStyleRules(): number {
    const x = GetUserModifiedStyleSheet()?.cssRules;
    if (!x) return 0;

    let count = 0;
    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf("foo-style") > -1) {
            ++count;
        }
    }
    return count;
}

describe("StyleEditor", () => {
    // most perplexingly, jasmine doesn't reset the dom between tests. (Not sure about vitest)
    beforeEach(() => {
        $('style[title="userModifiedStyles"]').remove();
        $("body").html("");
    });

    // the constructor no longer creates the "userModifiedStyles" element
    //it("constructor does not make a userModifiedStyles style if one already exists", function () {
    //  var editor1 = new StyleEditor("");
    //  var editor2 = new StyleEditor("");
    //  var count = 0;
    //  for (var i = 0; i < document.styleSheets.length; i++) {
    //    if (document.styleSheets[i].title == "userModifiedStyles")
    //      ++count;
    //  }
    //  expect(count).toEqual(1);
    //});

    // the constructor no longer creates the "userModifiedStyles" element
    //it("constructor adds a stylesheet with title userModifiedStyles", function () {
    //  var editor = new StyleEditor("");
    //  expect(GetUserModifiedStyleSheet()).not.toBeNull();
    //});

    it("MakeBigger creates a style for the correct class if it is missing", () => {
        $("body").append(
            "<div id='testTarget' class='ignore foo-style ignoreMeToo '></div>",
        );
        MakeBigger();
        expect(GetRuleForFooStyle()).not.toBeNull();
    });

    // MakeBigger() isn't really used anymore, we do things differently now.
    // it("MakeBigger makes the text of the target style bigger", function () {
    //     $('body').append("<div id='testTarget' class='ignore foo-style ignoreMeToo '></div>");
    //     var originalSize = GetCalculatedFontSize('#testTarget');
    //     MakeBigger();
    //     expect(GetCalculatedFontSize('#testTarget')).toBe(originalSize + 2);
    //     MakeBigger();
    //     expect(GetCalculatedFontSize('#testTarget')).toBe(originalSize + 4);
    // });

    //note originally i was just letting everything be changeable, regardless. The problem is that then things like title
    //and subtitle were getting conflated. So that is a future enhancement; for now, I'm keeping things simple by saying
    //I have to have an explict x-style in the @class, except in the special case of known legacy pages, which all started with the same bit of guid
    it("MakeBigger does nothing if no x-style classes, and ancestor is not a known old-format basic-book page", () => {
        $("body").append(
            "<div class='bloom-page' data-pagelineage='123-blah-blah'><div id='testTarget'>i don't want to get bigger</div></div>",
        );
        MakeBigger();
        expect(GetRuleForNormalStyle()).toBeNull();
    });

    // Handle books created with the original (0.9) version of "Basic Book", which lacked "x-style" but had all pages starting with an id of 5dcd48df (so we can detect them)
    it("MakeBigger adds normal-style if there are no x-style classes, but ancestor is a known old-format basic-book page", () => {
        $("body").append(
            "<div  class='bloom-page'  data-pagelineage='5dcd48df-blah-blah'><div id='testTarget'>i want to get bigger</div></div>",
        );
        MakeBigger();
        expect(GetRuleForNormalStyle()).not.toBeNull();
    });

    it("MakeBigger can add a new rule without removing other rules", () => {
        $("body").append(
            "<div id='testTarget' class='blah-style'></div><div id='testTarget2' class='normal-style'></div>",
        );
        MakeBigger2("#testTarget2");
        MakeBigger();
        expect(GetRuleForNormalStyle()).not.toBeNull();
    });

    it("MakeBigger doesn't make duplicate styles if there are already two there (default and lang-specific)", () => {
        $("body").append(
            "<div id='testTarget' class='ignore foo-style ignoreMeToo '></div>",
        );
        MakeBigger();
        expect(countFooStyleRules()).toBe(2); // default rule, language-specific rule
        MakeBigger();
        MakeBigger();

        expect(countFooStyleRules()).toBe(2); // no more
    });

    it("MakeBigger doesn't make a duplicate style for a language if there is already one there", () => {
        $("body").append(
            "<div id='testTarget' class='ignore foo-style ignoreMeToo '></div>",
        );
        MakeBigger(false);
        MakeBigger(false);
        MakeBigger(false);

        expect(countFooStyleRules()).toBe(1);
    });

    it("When not editing an L1 block, MakeBigger adds rules that only affect the given language", () => {
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>",
        );
        MakeBigger2("#testTarget", false);
        const x = (<CSSStyleSheet>GetUserModifiedStyleSheet()).cssRules;

        let count = 0;
        for (let i = 0; i < x.length; i++) {
            if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
                ++count;
            }
        }
        expect(count).toBe(1);
    });

    it("When the element does not have @lang, MakeBigger adds rules that apply only when there is no @lang", () => {
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>",
        );
        MakeBigger2("#testTarget2");

        expect(HasRuleMatchingThisSelector("normal-style:not([lang])")).toBe(
            true,
        );
    });

    // Skipped because currently we're running in jsdom. Making use of the existing rule depends on
    // getComputedStyle, which jsdom does not support. ChatGpt thinks it also depends on actual
    // dom element sizes, which jsdom also does not support. Attempts to polyfill proved difficult.
    // We may at some point try again to run this test using a real browser.
    it.skip("When the element has an @lang, and already has a rule, MakeBigger replaces the existing rule", () => {
        $("head").append(
            "<style title='userModifiedStyles'>.foo-style[lang='xyz']{ font-size: 8pt !important; }</style>",
        );
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>",
        );
        MakeBigger2("#testTarget");
        const x = (<CSSStyleSheet>GetUserModifiedStyleSheet()).cssRules;

        let count = 0;
        for (let i = 0; i < x.length; i++) {
            if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
                ++count;
            }
        }
        expect(count).toBe(1);
        expect(GetFontSizeRuleByLang("xyz")).toBe(10);
    });

    it("When the element does not have @lang, ChangeSizeAbsolute adds rules that apply only when there is no @lang", () => {
        $("body").append(
            "<div id='testTarget' class='foo-style'></div><div id='testTarget2' class='normal-style' lang='xyz'></div>",
        );
        ChangeSizeAbsolute("#testTarget", 20);

        expect(HasRuleMatchingThisSelector("foo-style:not([lang])")).toBe(true);
        const rule = GetRuleForFooStyle();
        expect(rule).not.toBeNull();
        if (rule != null) expect(ParseRuleForFontSize(rule.cssText)).toBe(20);
    });

    it("putAudioHiliteRulesInDom stores audio highlight legacy properties", () => {
        const editor = new StyleEditor(
            "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
        );

        const sentenceSelector = "foo-style span.ui-audioCurrent";
        const paddedSentenceSelector =
            "foo-style span.ui-audioCurrent > span.ui-enableHighlight";
        const paragraphSelector = "foo-style.ui-audioCurrent p";

        // sanity check that the rules do not yet exist
        expect(HasRuleMatchingThisSelector(sentenceSelector)).toBeFalsy();
        expect(HasRuleMatchingThisSelector(paddedSentenceSelector)).toBeFalsy();
        expect(HasRuleMatchingThisSelector(paragraphSelector)).toBeFalsy();

        editor.putAudioHiliteRulesInDom(
            "foo-style",
            "rgb(1, 2, 3)",
            "rgb(4, 5, 6)",
        );

        const sentenceRule = GetRuleMatchingSelector(sentenceSelector);
        const paddedSentenceRule = GetRuleMatchingSelector(
            paddedSentenceSelector,
        );
        const paragraphRule = GetRuleMatchingSelector(paragraphSelector);

        expect(sentenceRule?.cssText).toContain(
            "background-color: rgb(4, 5, 6)",
        );
        expect(sentenceRule?.cssText).toContain("color: rgb(1, 2, 3)");
        expect(paddedSentenceRule?.cssText).toContain(
            "background-color: rgb(4, 5, 6)",
        );
        expect(paddedSentenceRule?.cssText).toContain("color: rgb(1, 2, 3)");
        expect(paragraphRule?.cssText).toContain(
            "background-color: rgb(4, 5, 6)",
        );
        expect(paragraphRule?.cssText).toContain("color: rgb(1, 2, 3)");
    });

    it("getAudioHiliteProps reads colors from audio highlight legacy properties", () => {
        const editor = new StyleEditor(
            "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
        );

        const origProps = editor.getAudioHiliteProps("foo-style");

        expect(origProps?.hiliteTextColor).not.toBe("rgb(1, 2, 3)");
        expect(origProps?.hiliteBgColor).not.toBe("rgb(4, 5, 6)");

        editor.putAudioHiliteRulesInDom(
            "foo-style",
            "rgb(1, 2, 3)",
            "rgb(4, 5, 6)",
        );

        const props = editor.getAudioHiliteProps("foo-style");

        expect(props.hiliteTextColor).toBe("rgb(1, 2, 3)");
        expect(props.hiliteBgColor).toBe("rgb(4, 5, 6)");
    });

    // Skipped because currently we're running in jsdom. Making use of the existing rule depends on
    // getComputedStyle, which jsdom does not support. ChatGpt thinks it also depends on actual
    // dom element sizes, which jsdom also does not support. Attempts to polyfill proved difficult.
    // We may at some point try again to run this test using a real browser.
    it.skip("When the element has an @lang, and already has a rule, ChangeSizeAbsolute replaces the existing rule", () => {
        $("head").append(
            "<style title='userModifiedStyles'>.foo-style[lang='xyz']{ font-size: 8pt ! important; }</style>",
        );
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>",
        );
        ChangeSizeAbsolute("#testTarget", 20);
        const x = (<CSSStyleSheet>GetUserModifiedStyleSheet()).cssRules;

        let count = 0;
        for (let i = 0; i < x.length; i++) {
            if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
                ++count;
            }
        }
        expect(count).toBe(1);
        expect(GetFontSizeRuleByLang("xyz")).toBe(20);
    });

    it("When the element has an @lang, but no existing rule, ChangeSizeAbsolute adds rules that only affect the given language", () => {
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>",
        );
        ChangeSizeAbsolute("#testTarget", 20);
        const x = (<CSSStyleSheet>GetUserModifiedStyleSheet()).cssRules;

        let count = 0;
        for (let i = 0; i < x.length; i++) {
            if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
                ++count;
            }
        }
        expect(count).toBe(1);
        expect(GetFontSizeRuleByLang("xyz")).toBe(20);
    });

    it("If a 'default-style' slips through, make it 'normal-style'", () => {
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='default-style'></div>",
        );
        MakeBigger2("#testTarget2");

        expect(GetRuleForNormalStyle()).not.toBeNull();
    });

    it("If a 'coverTitle' slips through, make it 'Title-On-Cover-style'", () => {
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='coverTitle'></div>",
        );
        MakeBigger2("#testTarget2");

        expect(GetRuleForCoverTitleStyle()).not.toBeNull();
    });

    it("changeWordSpace still updates the style rules after UpdateControlsToReflectAppliedStyle fails partway", () => {
        // GetSettings is normally injected into the page by C#.
        (globalThis as any).GetSettings = () => ({
            languageForNewTextBoxes: "xyz",
        });
        try {
            $("body").append(
                "<div id='testTarget' class='foo-style' lang='xyz'></div>" +
                    "<select id='word-space-select'><option>Normal</option><option>Wide</option><option>Extra Wide</option></select>",
            );
            const editor = new StyleEditor(
                "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
            );
            editor.boxBeingEdited = $("#testTarget").get(0);
            // cleanupAfterStyleChange does page-layout work (overflow checking, box
            // resizing) that requires real browser layout, not jsdom; it is irrelevant
            // to the rule-writing and control-guard behavior under test here.
            vi.spyOn(editor, "cleanupAfterStyleChange").mockImplementation(
                () => {},
            );

            const select = document.getElementById(
                "word-space-select",
            ) as HTMLSelectElement;
            select.selectedIndex = 1; // Wide
            editor.changeWordSpace();
            // sanity check: the control works before anything goes wrong
            expect(
                GetRuleMatchingSelector('.foo-style[lang="xyz"]')?.cssText,
            ).toContain("word-spacing: 5pt");

            // Simulate a failure occurring somewhere in the middle of
            // UpdateControlsToReflectAppliedStyle (which runs when the user applies a
            // different style in the Format dialog, after it sets ignoreControlChanges).
            vi.spyOn(editor, "changeCanvasElementProps").mockImplementation(
                () => {
                    throw new Error("simulated failure");
                },
            );
            expect(() =>
                editor.UpdateControlsToReflectAppliedStyle(""),
            ).toThrow("simulated failure");

            // The dialog controls must not be left permanently dead: choosing a new
            // word spacing must still change the document.
            select.selectedIndex = 2; // Extra Wide
            editor.changeWordSpace();
            expect(
                GetRuleMatchingSelector('.foo-style[lang="xyz"]')?.cssText,
            ).toContain("word-spacing: 10pt");
        } finally {
            delete (globalThis as any).GetSettings;
        }
    });

    // The Format dialog's Color control follows the same rule as bold, size and spacing: a change
    // made on a box in the collection's first language is for the style as a whole, so it goes
    // into the language-independent rule too (BL-16803). Font family is the deliberate exception.
    it("changeColor on a first-language box writes the color to the language-specific and the language-independent rules", () => {
        (globalThis as any).GetSettings = () => ({
            languageForNewTextBoxes: "xyz",
        });
        try {
            $("body").append(
                "<div id='testTarget' class='foo-style' lang='xyz'></div>" +
                    "<div id='colorSelectButton'></div>",
            );
            const editor = new StyleEditor(
                "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
            );
            editor.boxBeingEdited = $("#testTarget").get(0);
            vi.spyOn(editor, "cleanupAfterStyleChange").mockImplementation(
                () => {},
            );
            // sanity check: nothing has written a color yet
            expect(GetRuleMatchingSelector("color:")).toBeNull();

            editor.changeColor("rgb(255, 22, 22)");

            expect(
                GetRuleMatchingSelector('.foo-style[lang="xyz"]')?.cssText,
            ).toContain("color: rgb(255, 22, 22)");
            expect(GetRuleMatchingSelector(".foo-style {")?.cssText).toContain(
                "color: rgb(255, 22, 22)",
            );
            expect(
                $("#colorSelectButton").attr("style"),
                "the dialog's color button shows the new color",
            ).toContain("rgb(255, 22, 22)");
        } finally {
            delete (globalThis as any).GetSettings;
        }
    });

    it("changeColor on a box in another language writes the color only to that language's rule", () => {
        (globalThis as any).GetSettings = () => ({
            languageForNewTextBoxes: "xyz",
        });
        try {
            $("body").append(
                "<div id='testTarget' class='foo-style' lang='abc'></div>" +
                    "<div id='colorSelectButton'></div>",
            );
            const editor = new StyleEditor(
                "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
            );
            editor.boxBeingEdited = $("#testTarget").get(0);
            vi.spyOn(editor, "cleanupAfterStyleChange").mockImplementation(
                () => {},
            );

            editor.changeColor("rgb(255, 22, 22)");

            expect(
                GetRuleMatchingSelector('.foo-style[lang="abc"]')?.cssText,
            ).toContain("color: rgb(255, 22, 22)");
            expect(
                GetRuleMatchingSelector(".foo-style {"),
                "no language-independent rule should be written for a non-L1 box",
            ).toBeNull();
        } finally {
            delete (globalThis as any).GetSettings;
        }
    });

    // The controls createStyle copies into the new style. The values do not matter here; they
    // only have to exist, because updateStyle reads every one of them.
    const formatDialogControlsHtml =
        "<select id='size-select'><option selected>12</option></select>" +
        "<select id='line-height-select'><option selected>1.5</option></select>" +
        "<select id='word-space-select'><option selected>Normal</option><option>Wide</option><option>Extra Wide</option></select>" +
        "<select id='para-spacing-select'><option selected>0</option></select>" +
        "<div id='bold'></div><div id='italic'></div><div id='underline'></div>" +
        "<div id='indent-none' class='selectedIcon'></div><div id='position-left' class='selectedIcon'></div>" +
        "<div id='colorSelectButton'></div>" +
        "<select id='styleSelect'></select>" +
        "<div id='style-group' class='state-enteringStyle'></div>" +
        "<input id='style-select-input' value='Bar'>";

    // A box's font normally comes from the collection's language settings, not from its style,
    // so a new style should say nothing about the font unless the old style set one explicitly.
    it("createStyle copies a font the old style set explicitly for the box's language", () => {
        (globalThis as any).GetSettings = () => ({
            languageForNewTextBoxes: "xyz",
        });
        try {
            $("body").append(
                "<div id='testTarget' class='foo-style' lang='xyz'></div>" +
                    formatDialogControlsHtml,
            );
            const editor = new StyleEditor(
                "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
            );
            editor.boxBeingEdited = $("#testTarget").get(0);
            vi.spyOn(editor, "cleanupAfterStyleChange").mockImplementation(
                () => {},
            );
            editor.changeFont("Arial");
            // sanity check: the old style now names the font for this language
            expect(
                GetRuleMatchingSelector('.foo-style[lang="xyz"]')?.cssText,
            ).toContain("font-family: Arial");

            // runFormatDialog fills the style list when the dialog opens; createStyle adds to it.
            (editor as any).styles = [];
            editor.createStyle();

            expect($("#testTarget").attr("class")).toContain("Bar-style");
            expect(
                GetRuleMatchingSelector('.Bar-style[lang="xyz"]')?.cssText,
            ).toContain("font-family: Arial");
            // The font stays per language: the language-independent rule says nothing about it.
            expect(
                GetRuleMatchingSelector(".Bar-style {")?.cssText,
            ).not.toContain("font-family");
        } finally {
            delete (globalThis as any).GetSettings;
        }
    });

    it("createStyle leaves the font to the language's default when the old style did", () => {
        (globalThis as any).GetSettings = () => ({
            languageForNewTextBoxes: "xyz",
        });
        try {
            $("body").append(
                "<div id='testTarget' class='foo-style' lang='xyz'></div>" +
                    formatDialogControlsHtml,
            );
            const editor = new StyleEditor(
                "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
            );
            editor.boxBeingEdited = $("#testTarget").get(0);
            vi.spyOn(editor, "cleanupAfterStyleChange").mockImplementation(
                () => {},
            );
            // sanity check: nothing names a font yet
            expect(GetRuleMatchingSelector("font-family")).toBeNull();

            // runFormatDialog fills the style list when the dialog opens; createStyle adds to it.
            (editor as any).styles = [];
            editor.createStyle();

            expect($("#testTarget").attr("class")).toContain("Bar-style");
            // The new style got the other settings...
            expect(
                GetRuleMatchingSelector('.Bar-style[lang="xyz"]')?.cssText,
            ).toContain("font-size: 12pt");
            // ...but no font, in any of its rules.
            expect(GetRuleMatchingSelector("font-family")).toBeNull();
        } finally {
            delete (globalThis as any).GetSettings;
        }
    });

    it("UpdateControlsToReflectAppliedStyle passes the real highlight colors to changeHiliteProps", () => {
        $("body").append(
            "<div id='testTarget' class='foo-style' lang='xyz'></div>",
        );
        const editor = new StyleEditor(
            "file://" + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit",
        );
        editor.boxBeingEdited = $("#testTarget").get(0);
        // cleanupAfterStyleChange does page-layout work that needs real browser layout.
        vi.spyOn(editor, "cleanupAfterStyleChange").mockImplementation(
            () => {},
        );
        // Give the style a custom audio-highlight text and background color.
        editor.putAudioHiliteRulesInDom(
            "foo-style",
            "rgb(1, 2, 3)",
            "rgb(4, 5, 6)",
        );
        // sanity check that getFormatValues sees those colors
        expect(editor.getFormatValues().hiliteTextColor).toBe("rgb(1, 2, 3)");

        const spy = vi.spyOn(editor, "changeHiliteProps");
        editor.UpdateControlsToReflectAppliedStyle("");
        // The highlight controls must be re-rendered with the style's actual highlight
        // colors, not with the ordinary text color in the hiliteTextColor slot (which
        // both displayed wrongly and could then be written back into the book).
        expect(spy).toHaveBeenCalledWith(
            "rgb(1, 2, 3)",
            "rgb(4, 5, 6)",
            expect.any(String),
        );
    });
});
