/// <reference path="lib/jquery.d.ts" />
module Bloom {

	export class StyleEditor {

		styleElement: HTMLElement;

		constructor (doc:HTMLElement ) {
			this.styleElement = <HTMLElement>($(doc).find(".styleEditorStuff").first());
			if(this.styleElement.length ==0)
			{
				var s=$('<style id="documentStyles" class="styleEditorStuff" type="text/css"></style>')
				$(doc).find("head").append(s);
				this.styleElement = $(doc).find('.styleEditorStuff')[0];
			}
		}

		GetStyleClassFromElement (target: HTMLElement) {
			var classes = $(target).attr("class").split(' ');
			for(i = 0; i < classes.length; i++){
				if(classes[i].indexOf('Style')>0){
					return classes[i];
				}
			}
			return null;
		}

		MakeBigger (target: HTMLElement) {
			var styleName = this.GetStyleClassFromElement(target);
			//$(this.styleElement).html(styleName+ ': {color:red;}');
			$(this.styleElement).css({zIndex:'12'});
		}

	}
}