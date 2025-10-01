import { describe, it, expect, beforeEach } from "vitest";
/// <reference path="./StyleEditor.ts" />
/// <reference path="../../typings/jquery/jquery.d.ts" />

/*/// <reference path="../../lib/jquery-1.9.1.js"/>*/

import StyleEditor from "./StyleEditor";

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

function GetUserModifiedStyleSheet(): any {
    for (let i = 0; i < document.styleSheets.length; i++) {
        if (document.styleSheets[i].title == "userModifiedStyles")
            return <CSSStyleSheet>document.styleSheets[i];
    }
    // this is not a valid constructor
    //return new CSSStyleSheet();
    return {};
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
    const x: CSSRuleList = (<CSSStyleSheet>GetUserModifiedStyleSheet())
        .cssRules;

    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf("foo-style") > -1) {
            return x[i];
        }
    }
    return null;
}

function GetRuleForNormalStyle(): CSSRule | null {
    const x: CSSRuleList = (<CSSStyleSheet>GetUserModifiedStyleSheet())
        .cssRules;
    if (!x) return null;

    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf("normal-style") > -1) {
            return x[i];
        }
    }
    return null;
}

function GetRuleForCoverTitleStyle(): CSSRule | null {
    const x: CSSRuleList = (<CSSStyleSheet>GetUserModifiedStyleSheet())
        .cssRules;
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
    const x = (<CSSStyleSheet>GetUserModifiedStyleSheet()).cssRules;
    const count = 0;
    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf(selector) > -1) {
            return x[i];
        }
    }
    return null;
}

function HasRuleMatchingThisSelector(selector: string): boolean {
    const x = (<CSSStyleSheet>GetUserModifiedStyleSheet()).cssRules;
    let count = 0;
    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf(selector) > -1) {
            ++count;
        }
    }
    return count > 0;
}

function countFooStyleRules(): number {
    const x: CSSRuleList = (<CSSStyleSheet>GetUserModifiedStyleSheet())
        .cssRules;

    let count = 0;
    for (let i = 0; i < x.length; i++) {
        if (x[i].cssText.indexOf("foo-style") > -1) {
            ++count;
        }
    }
    return count;
}

describe("StyleEditor", () => {
    // most perplexingly, jasmine doesn't reset the dom between tests
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

    it("When the element has an @lang, and already has a rule, MakeBigger replaces the existing rule", () => {
        $("head").append(
            "<style title='userModifiedStyles'>.foo-style[lang='xyz']{ font-size: 8pt ! important; }</style>",
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

    it("When the element has an @lang, and already has a rule, ChangeSizeAbsolute replaces the existing rule", () => {
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
});
