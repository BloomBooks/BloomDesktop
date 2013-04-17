/// <reference path="../../lib/jquery.d.ts" />

class StyleEditor {

	constructor() {
//        this.styleElement = <HTMLElement><any>($(doc).find(".styleEditorStuff").first()); //the <any> here is to turn off the typscript process erro
//        if (!this.styleElement) {
//            var s = $('<style id="documentStyles" class="styleEditorStuff" type="text/css"></style>');
//            $(doc).find("head").append(s);
//            this.styleElement = $(doc).find('.styleEditorStuff')[0];
		//        }
		var sheet = this.GetOrCreateCustomStyleSheet();
	}

	GetStyleClassFromElement(target: HTMLElement) {
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
	ChangeSize(target: HTMLElement, change:number) {
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
				return;
			}
		}
		var rule = this.GetOrCreateRuleForStyle(styleName);
		var sizeString: string = (<any>rule).style.fontSize;
		if (!sizeString)
			sizeString = $(target).css("font-size");
		var units = sizeString.substr(sizeString.length - 2, 2);
		sizeString = (parseInt(sizeString) + change).toString(); //notice that parseInt ignores the trailing units
		(<any>rule).style.setProperty("font-size", sizeString + units, "important");
	}

	GetOrCreateCustomStyleSheet(): StyleSheet {
		for (var i = 0; i < document.styleSheets.length; i++) {
			if (document.styleSheets[i].title == "customBookStyles")
				return document.styleSheets[i];
		}
		var newSheet = document.createElement('style');
		newSheet.id = "customBookStyles";
		document.getElementsByTagName('head')[0].appendChild(newSheet);
		newSheet.title = "customBookStyles";
		return <StyleSheet><any>newSheet;
	}

	GetOrCreateRuleForStyle(styleName: string): CSSRule {
		var styleSheet = this.GetOrCreateCustomStyleSheet();
		var x: CSSRuleList = (<any>styleSheet).cssRules;

		for (var i = 0; i < x.length; i++) {
			if (x[i].cssText.indexOf(styleName) > -1) {
				return x[i];
			}
		}
		(<any>styleSheet).insertRule('.'+styleName+' {}', 0)

		return x[i];
	}

}