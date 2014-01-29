/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="toolbar/toolbar.d.ts"/>

class StyleEditor {

	private _previousBox: Element;
	private _supportFilesRoot: string;

	constructor(supportFilesRoot: string) {
		this._supportFilesRoot = supportFilesRoot;

//        this.styleElement = <HTMLElement><any>($(doc).find(".styleEditorStuff").first()); //the <any> here is to turn off the typscript process erro
//        if (!this.styleElement) {
//            var s = $('<style id="documentStyles" class="styleEditorStuff" type="text/css"></style>');
//            $(doc).find("head").append(s);
//            this.styleElement = $(doc).find('.styleEditorStuff')[0];
		//        }
		var sheet = this.GetOrCreateCustomStyleSheet();
	}

	static GetStyleClassFromElement(target: HTMLElement) {
		var c = $(target).attr("class");
		if (!c)
			c = "";
		var classes = c.split(' ');

		for (var i = 0; i < classes.length; i++) {
			if (classes[i].indexOf('-style') > 0) {
				return classes[i];
			}
		}
		return null;
	}

	MakeBigger(target: HTMLElement) {
		this.ChangeSize(target, 2);
	}
	MakeSmaller(target: HTMLElement) {
		this.ChangeSize(target, -2);
	}

	static GetStyleNameForElement(target: HTMLElement): string {
		var styleName = this.GetStyleClassFromElement(target);
		if (!styleName) {
			var parentPage: HTMLDivElement = <HTMLDivElement><any> ($(target).closest(".bloom-page")[0]);
			// Books created with the original (0.9) version of "Basic Book", lacked "x-style" but had all pages starting with an id of 5dcd48df (so we can detect them)
			var pageLineage = $(parentPage).attr('data-pagelineage');
			if ((pageLineage) && pageLineage.substring(0, 8) == '5dcd48df') {
				styleName = "default-style";
				$(target).addClass(styleName);
			}
			else {
				return null;
			}
		}
		return styleName;
	}

	ChangeSize(target: HTMLElement, change: number) {
		var styleName = StyleEditor.GetStyleNameForElement(target);
		if (!styleName)
			return;
		var rule: CSSStyleRule = this.GetOrCreateRuleForStyle(styleName);
		var sizeString: string = (<any>rule).style.fontSize;
		if (!sizeString)
			sizeString = $(target).css("font-size");
		var units = sizeString.substr(sizeString.length - 2, 2);
		sizeString = (parseInt(sizeString) + change).toString(); //notice that parseInt ignores the trailing units
		rule.style.setProperty("font-size", sizeString + units, "important");
	}

	GetOrCreateCustomStyleSheet(): StyleSheet {
		//note, this currently just makes an element in the document, not a separate file
		for (var i = 0; i < document.styleSheets.length; i++) {
			if ((<any>document.styleSheets[i]).ownerNode.id == "customBookStyleElement")
				return document.styleSheets[i];
		}
		//alert("Will make customBookStyles Sheet:" + document.head.outerHTML);

		var newSheet = document.createElement('style');
		newSheet.id = "customBookStyleElement";
		document.getElementsByTagName('head')[0].appendChild(newSheet);
		newSheet.title = "customBookStyleElement";

		return <StyleSheet><any>newSheet;
	}

	GetOrCreateRuleForStyle(styleName: string): CSSStyleRule {
		var styleSheet = this.GetOrCreateCustomStyleSheet();
		var x: CSSRuleList = (<any>styleSheet).cssRules;

		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf(styleName) > -1) {
				return <CSSStyleRule> x[i];
			}
		}
		(<any>styleSheet).insertRule('.'+styleName+' {}', 0)

		return <CSSStyleRule> x[0];      //new guy is first
	}


	AttachToBox(targetBox: HTMLElement) {
		if (!StyleEditor.GetStyleNameForElement(targetBox))
			return;

		if (this._previousBox!=null)
		{
			StyleEditor.CleanupElement(this._previousBox);
		}
		this._previousBox = targetBox;

		//REVIEW: we're putting it in the target div, but at the moment we are using exactly the same bar for each editable box, could just have
		//one for the whole document

		//NB: we're placing these *after* the target, don't want to mess with having a div inside our text (if that would work anyhow)

		//  i couldn't get the nice icomoon icon font/style.css system to work in Bloom or stylizer
		//            $(targetBox).after('<div id="format-toolbar" style="opacity:0; display:none;"><a class="smallerFontButton" id="smaller">a</a><a id="bigger" class="largerFontButton" ><i class="bloom-icon-FontSize"></i></a></div>');
		$(targetBox).after('<div id="format-toolbar" class="bloom-ui" style="opacity:0; display:none;"><a class="smallerFontButton" id="smaller"><img src="' + this._supportFilesRoot + '/img/FontSizeLetter.svg"></a><a id="bigger" class="largerFontButton" ><img src="' + this._supportFilesRoot + '/img/FontSizeLetter.svg"></a></div>');


		var bottom = $(targetBox).position().top + $(targetBox).height();
		var t = bottom + "px";
		$(targetBox).after('<div id="formatButton"  style="top: '+t+'" class="bloom-ui" title="Change text size. Affects all similar boxes in this document"><img src="' + this._supportFilesRoot + '/img/cogGrey.svg"></div>');

		$('#formatButton').toolbar({
			content: '#format-toolbar',
			//position: 'left',//nb: toolbar's June 2013 code, pushes the toolbar out to the left by 1/2 the width of the parent object, easily putting it in negative territory!
			position: 'left',
			hideOnClick: false
		});

		var editor = this;
		$('#formatButton').on("toolbarItemClick", function (event, whichButton) {
			if (whichButton.id == "smaller") {
				editor.MakeSmaller(targetBox);
			}
			if (whichButton.id == "bigger") {
				editor.MakeBigger(targetBox);
			}
		});
	  }

	DetachFromBox(element) {
	  //  StyleEditor.CleanupElement(element);
	}

	static CleanupElement(element) {
		//NB: we're placing these controls *after* the target, not inside it; that's why we go up to parent
		$(element).parent().find(".bloom-ui").each(function () {
			$(this).remove();
		});
		$(".tool-container").each(function () {
			$(this).remove();
		});
	}
}