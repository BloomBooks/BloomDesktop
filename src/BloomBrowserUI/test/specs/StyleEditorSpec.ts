/// <reference path="../../bookEdit/js/StyleEditor.ts" />
/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jasmine/jasmine.d.ts"/>

/*
/// <reference path="../../lib/jquery-1.9.1.js"/>
*/

"use strict";

//this was getting html, but just setting the rules actually doesn't touch the html
//function GetStylesAfterMakeBigger(): string {
//    var target = $(document).find('.fooStyle');
//    var editor = new StyleEditor(<HTMLElement><any>document);
//    editor.MakeBigger(<HTMLElement><any>target);
//    return (<HTMLElement>GetUserModifiedStyleSheet().ownerNode).outerHTML;
//}

function MakeBigger() {
	var target = $(document).find('#testTarget');
	var editor = new StyleEditor('file://' + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit");
	editor.MakeBigger(<HTMLElement><any>target);
}

function MakeBigger2(target:string) {
	var jQueryTarget = $(document).find(target);
	var editor = new StyleEditor('file://' + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit");
	editor.MakeBigger(<HTMLElement><any>jQueryTarget);
}

function MakeSmaller(target:string) {
	var jQueryTarget = $(document).find(target);
	var editor = new StyleEditor('file://' + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit");
	editor.MakeSmaller(<HTMLElement><any>jQueryTarget);
}

function ChangeSizeAbsolute(target:string, newSize:number) {
	var jQueryTarget = $(document).find(target);
	var editor = new StyleEditor('file://' + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit");
	editor.ChangeSizeAbsolute(<HTMLElement><any>jQueryTarget, newSize);
}

function GetUserModifiedStyleSheet(): CSSStyleSheet {
	for (var i = 0; i < document.styleSheets.length; i++) {
		if (document.styleSheets[i].title == "userModifiedStyles")
		   return <CSSStyleSheet>(document.styleSheets[i]);
	}
	return new CSSStyleSheet();
}

function GetFooStyleRuleFontSize(): number {
	var sizeString = $('.foo-style').css("font-size");
   return parseInt(sizeString.substr(0, sizeString.length - 2));
}

function GetFontSizeRuleByLang(lang: string): number {
	var rule = GetRuleMatchingSelector('.foo-style[lang="'+lang+'"]');
	return ParseRuleForFontSize(rule.cssText);
}

function ParseRuleForFontSize(ruleText: string): number {
	var ruleString = 'font-size: ';
	var beginPoint = ruleText.indexOf(ruleString) + ruleString.length;
	var endPoint = ruleText.indexOf(' !important');
	if (beginPoint < 1 || endPoint < beginPoint)
		return null;
	var sizeString = ruleText.substr(beginPoint, endPoint - beginPoint);
	return parseFloat(sizeString); // parseFloat() handles units fine!
}

function GetRuleForFooStyle(): CSSRule {
	var x:CSSRuleList = <any>GetUserModifiedStyleSheet().cssRules;

	for (var i = 0; i < x.length; i++) {
		if (x[i].cssText.indexOf('foo-style') > -1){
			return x[i];
		}
	}
	return null;
}

function GetRuleForNormalStyle(): CSSRule {
	var x:CSSRuleList = <any>GetUserModifiedStyleSheet().cssRules;

	for (var i = 0; i < x.length; i++) {
		if (x[i].cssText.indexOf('normal-style') > -1) {
			return x[i];
		}
	}
	return null;
}

function GetRuleForCoverTitleStyle(): CSSRule {
	var x:CSSRuleList = <any>GetUserModifiedStyleSheet().cssRules;

	for (var i = 0; i < x.length; i++) {
		if (x[i].cssText.indexOf('coverTitle-style') > -1) {
			return x[i];
		}
	}
	return null;
}

function GetCalculatedFontSize(target: string): number {
	var jQueryTarget = $(document).find(target);
	var editor = new StyleEditor('file://' + "C:/dev/Bloom/src/BloomBrowserUI/bookEdit");
	return editor.GetCalculatedFontSizeInPoints(<HTMLElement><any>jQueryTarget);
}

function GetRuleMatchingSelector(selector: string): CSSRule {
	var x = GetUserModifiedStyleSheet().cssRules;
	var count = 0;
	for (var i = 0; i < x.length; i++) {
		if (x[i].cssText.indexOf(selector) > -1) {
			return x[i];
		}
	}
	return null;
}

function HasRuleMatchingThisSelector(selector: string): boolean {
	var x = GetUserModifiedStyleSheet().cssRules;
	var count = 0;
	for (var i = 0; i < x.length; i++) {
		if (x[i].cssText.indexOf(selector) > -1) {
			++count;
		}
	}
	return count > 0;
}

describe("StyleEditor", function () {
	// most perplexingly, jasmine doesn't reset the dom between tests
	beforeEach(function () {
		$('style[title="userModifiedStyles"]').remove();
		$('body').html('');
	});

	it("constructor does not make a userModifiedStyles style if one already exists", function () {
		var editor1 = new StyleEditor("");
		var editor2 = new StyleEditor("");
		var count = 0;
		for (var i = 0; i < document.styleSheets.length; i++) {
			if (document.styleSheets[i].title == "userModifiedStyles")
				++count;
		}
		expect(count).toEqual(1);
	});

	it("constructor adds a stylesheet with title userModifiedStyles", function () {
		var editor = new StyleEditor("");
		expect(GetUserModifiedStyleSheet()).not.toBeNull();
	});

	it("MakeBigger creates a style for the correct class if it is missing", function () {
		$('body').append("<div id='testTarget' class='ignore foo-style ignoreMeToo '></div>");
		MakeBigger();
		expect(GetRuleForFooStyle()).not.toBeNull();
	});

	it("MakeBigger makes the text of the target style bigger", function () {
		$('body').append("<div id='testTarget' class='ignore foo-style ignoreMeToo '></div>");
		var originalSize = GetCalculatedFontSize('#testTarget');
		MakeBigger();
		expect(GetCalculatedFontSize('#testTarget')).toBe(originalSize+2);
		MakeBigger();
		expect(GetCalculatedFontSize('#testTarget')).toBe(originalSize + 4);
	});

	//note originally i was just letting everything be changeable, regardless. The problem is that then things like title
	//and subtitle were getting conflated. So that is a future enhancement; for now, I'm keeping things simple by saying
	//I have to have an explict x-style in the @class, except in the special case of known legacy pages, which all started with the same bit of guid
	it("MakeBigger does nothing if no x-style classes, and ancestor is not a known old-format basic-book page", function () {
		$('body').append("<div class='bloom-page' data-pagelineage='123-blah-blah'><div id='testTarget'>i don't want to get bigger</div></div>");
		MakeBigger();
		expect(GetRuleForNormalStyle()).toBeNull();
	});

	// Handle books created with the original (0.9) version of "Basic Book", which lacked "x-style" but had all pages starting with an id of 5dcd48df (so we can detect them)
	it("MakeBigger adds normal-style if there are no x-style classes, but ancestor is a known old-format basic-book page", function () {
		$('body').append("<div  class='bloom-page'  data-pagelineage='5dcd48df-blah-blah'><div id='testTarget'>i want to get bigger</div></div>");
		MakeBigger();
		expect(GetRuleForNormalStyle()).not.toBeNull();
	});

	it("MakeBigger can add a new rule without removing other rules", function () {
		$('body').append("<div id='testTarget' class='blah-style'></div><div id='testTarget2' class='normal-style'></div>");
		MakeBigger2('#testTarget2');
		MakeBigger();
		expect(GetRuleForNormalStyle()).not.toBeNull();
	});

	it("MakeBigger doesn't make a duplicate style if there is already one there", function () {
		$('body').append("<div id='testTarget' class='ignore foo-style ignoreMeToo '></div>");
		MakeBigger();
		MakeBigger();
		MakeBigger();
		var x: CSSRuleList = GetUserModifiedStyleSheet().cssRules;

		var count = 0;
		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf('foo-style') > -1) {
				++count;
			}
		}
		expect(count).toBe(1);
	});

	it("When the element has an @lang, MakeBigger adds rules that only affect the given language", function () {
		 $('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>");
		MakeBigger2('#testTarget');
		var x = GetUserModifiedStyleSheet().cssRules;

		var count = 0;
		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
				++count;
			}
		}
		expect(count).toBe(1);
	});

	it("When the element does not have @lang, MakeBigger adds rules that apply only when there is no @lang", function () {
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>");
		MakeBigger2('#testTarget2');

		expect(HasRuleMatchingThisSelector("normal-style:not([lang])")).toBe(true);
	});

	it("When the element has an @lang, and already has a rule, MakeBigger replaces the existing rule", function () {
		$('head').append("<style title='userModifiedStyles'>.foo-style[lang='xyz']{ font-size: 8pt ! important; }</style>");
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>");
		MakeBigger2('#testTarget');
		var x = GetUserModifiedStyleSheet().cssRules;

		var count = 0;
		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
				++count;
			}
		}
		expect(count).toBe(1);
		expect(GetFontSizeRuleByLang('xyz')).toBe(10);
	});

	it("When the element does not have @lang, ChangeSizeAbsolute adds rules that apply only when there is no @lang", function () {
		$('body').append("<div id='testTarget' class='foo-style'></div><div id='testTarget2' class='normal-style' lang='xyz'></div>");
		ChangeSizeAbsolute('#testTarget', 20);

		expect(HasRuleMatchingThisSelector("foo-style:not([lang])")).toBe(true);
		expect(ParseRuleForFontSize(GetRuleForFooStyle().cssText)).toBe(20);
	});

	it("When the element has an @lang, and already has a rule, ChangeSizeAbsolute replaces the existing rule", function () {
		$('head').append("<style title='userModifiedStyles'>.foo-style[lang='xyz']{ font-size: 8pt ! important; }</style>");
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>");
		ChangeSizeAbsolute('#testTarget', 20);
		var x = GetUserModifiedStyleSheet().cssRules;

		var count = 0;
		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
				++count;
			}
		}
		expect(count).toBe(1);
		expect(GetFontSizeRuleByLang('xyz')).toBe(20);
	});

	it("When the element has an @lang, but no existing rule, ChangeSizeAbsolute adds rules that only affect the given language", function () {
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>");
		ChangeSizeAbsolute('#testTarget', 20);
		var x = GetUserModifiedStyleSheet().cssRules;

		var count = 0;
		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
				++count;
			}
		}
		expect(count).toBe(1);
		expect(GetFontSizeRuleByLang('xyz')).toBe(20);
	});

	it("If a 'default-style' slips through, make it 'normal-style'", function () {
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='default-style'></div>");
		MakeBigger2('#testTarget2');

		expect(GetRuleForNormalStyle()).not.toBeNull();
	});

	it("If a 'coverTitle' slips through, make it 'coverTitle-style'", function () {
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='coverTitle'></div>");
		MakeBigger2('#testTarget2');

		expect(GetRuleForCoverTitleStyle()).not.toBeNull();
	});

	it("MakeSmaller has no effect if it will be smaller than 7pt", function () {
		$('body').append("<div id='testTarget' class='blah-style'></div><div id='testTarget2' class='foo-style' lang='xyz'></div>");
		ChangeSizeAbsolute('#testTarget2', 8);
		MakeSmaller('#testTarget2');
		expect(GetRuleForFooStyle()).not.toBeNull();
		expect(GetFontSizeRuleByLang('xyz')).toBe(8);
	});

	it("When the element has a size rule in 'em's, MakeSmaller is still limited to bigger than 6pt font", function () {
		$('head').append("<style title='userModifiedStyles'>.foo-style[lang='xyz']{ font-size: 0.6em ! important; }</style>");
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>");
		MakeSmaller('#testTarget');
		var x = GetUserModifiedStyleSheet().cssRules;

		var count = 0;
		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
				++count;
			}
		}
		expect(count).toBe(1);
		expect(GetFontSizeRuleByLang('xyz')).toBe(0.6); // 0.6em -> 8pt -> smaller is 6pt (not allowed; remains 0.6em)
	});

	it("When the element has a size rule in enough 'em's, MakeSmaller will still work", function () {
		$('head').append("<style title='userModifiedStyles'>.foo-style[lang='xyz']{ font-size: 0.8em ! important; }</style>");
		$('body').append("<div id='testTarget' class='foo-style' lang='xyz'></div><div id='testTarget2' class='normal-style'></div>");
		MakeSmaller('#testTarget');
		var x = GetUserModifiedStyleSheet().cssRules;

		var count = 0;
		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf('foo-style[lang="xyz"]') > -1) {
				++count;
			}
		}
		expect(count).toBe(1);
		expect(GetFontSizeRuleByLang('xyz')).toBe(8); // 0.8em -> 10pt -> smaller is 8pt
	});
});
