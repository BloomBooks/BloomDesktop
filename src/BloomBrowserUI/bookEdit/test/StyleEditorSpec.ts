/// <reference path="../js/StyleEditor.ts" />
/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jasmine/jasmine.d.ts"/>

/*
/// <reference path="../../lib/jquery-1.9.1.js"/>
*/

function GetStylesAfterMakeBigger() {
	var target = $(document).find('.fooStyle');
	var editor = new StyleEditor(<HTMLElement><any>document);
	editor.MakeBigger(<HTMLElement><any>target);
	return $(document).find('.styleEditorStuff').html();
}

describe("StyleEditor", function () {
	it("constructor does not make a documentStyles style if one already exists", function () {
		var s = $('<style id="documentStyles" class="styleEditorStuff" type="text/css"></style>');
		$(document).find("head").append(s);
		var editor = new StyleEditor(<HTMLElement><any>document);
		expect($(document).find('.styleEditorStuff').length).toEqual(1);
	});
	it("constructor adds a style with id documentStyles", function () {
		var editor = new StyleEditor(<HTMLElement><any>document);
		expect($(document).find('.styleEditorStuff').length).toEqual(1);
	});
	it("MakeBigger creates a style for the correct class if it is missing", function () {
		$('body').append("<div class='ignore fooStyle ignoreMeToo '></div>");
		expect(GetStylesAfterMakeBigger()).toContain(<Object>"fooStyle");
	});
	it("MakeBigger doesn't make a duplicate style if there is already one there", function () {
		$('body').append("<div class='ignore fooStyle ignoreMeToo '></div>");
		expect(GetStylesAfterMakeBigger().split('fooStyle').length - 1).toEqual(1);
	});
});
