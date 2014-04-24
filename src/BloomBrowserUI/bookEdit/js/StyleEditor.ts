/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="toolbar/toolbar.d.ts"/>

class StyleEditor {

    private _previousBox: Element;
    private _supportFilesRoot: string;
    private MIN_FONT_SIZE: number = 7;

    constructor(supportFilesRoot: string) {
        this._supportFilesRoot = supportFilesRoot;

        var sheet = this.GetOrCreateUserModifiedStyleSheet();
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
        // For awhile between v1 and v2 we used 'coverTitle' in Factory-XMatter
        // In case this is one of those books, we'll replace it with 'coverTitle-style'
        var coverTitleClass = 'coverTitle';
        if ($(target).hasClass(coverTitleClass)) {
            $(target).removeClass(coverTitleClass);
            var newStyleName = 'coverTitle-style';
            $(target).addClass(newStyleName);
            return newStyleName;
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
                styleName = "normal-style";
                $(target).addClass(styleName);
            }
            else {
                return null;
            }
        }
        // For awhile between v1 and v2 we used 'default-style' in Basic Book
        // In case this is one of those books, we'll replace it with 'normal-style'
        if (styleName == 'default-style') {
            $(target).removeClass(styleName);
            styleName = 'normal-style';
            $(target).addClass(styleName);
        }
        return styleName;
    }

    static GetLangValueOrNull(target: HTMLElement): string {
        var langAttr = $(target).attr("lang");
        if(!langAttr)
            return null;
        return langAttr.valueOf().toString();
    }

    ChangeSize(target: HTMLElement, change: number) {
        var styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName)
            return;
        var fontSize = this.GetCalculatedFontSizeInPoints(target);
        var langAttrValue = StyleEditor.GetLangValueOrNull(target);
        var rule: CSSStyleRule = this.GetOrCreateRuleForStyle(styleName, langAttrValue);
        var units = 'pt';
        var sizeString = (fontSize + change).toString();
        if (parseInt(sizeString) < this.MIN_FONT_SIZE)
            return; // too small, quietly don't do it!
        rule.style.setProperty("font-size", sizeString + units, "important");
        // alert("New size rule: " + rule.cssText);
        // Now update tooltip
        var toolTip = this.GetToolTip(target, styleName);
        this.AddQtipToElement($('#formatButton'), toolTip);
    }

    GetCalculatedFontSizeInPoints(target: HTMLElement): number {
        var sizeInPx = $(target).css('font-size');
        return this.ConvertPxToPt(parseInt(sizeInPx));
    }

    ChangeSizeAbsolute(target: HTMLElement, newSize: number) {
        var styleName = StyleEditor.GetStyleNameForElement(target); // finds 'x-style' class or null
        if (!styleName) {
            alert('ChangeSizeAbsolute called on an element with invalid style class.');
            return;
        }
        if (newSize < this.MIN_FONT_SIZE) { // newSize is expected to come from a combobox entry by the user someday
            alert('ChangeSizeAbsolute called with too small a point size.');
            return;
        }
        var langAttrValue = StyleEditor.GetLangValueOrNull(target);
        var rule: CSSStyleRule = this.GetOrCreateRuleForStyle(styleName, langAttrValue);
        var units = "pt";
        var sizeString: string = newSize.toString();
        rule.style.setProperty("font-size", sizeString + units, "important");
        // Now update tooltip
        var toolTip = this.GetToolTip(target, styleName);
        this.AddQtipToElement($('#formatButton'), toolTip);
    }

    GetOrCreateUserModifiedStyleSheet(): StyleSheet {
        //note, this currently just makes an element in the document, not a separate file
        for (var i = 0; i < document.styleSheets.length; i++) {
            if ((<StyleSheet>(<any>document.styleSheets[i]).ownerNode).title == "userModifiedStyles") {
                // alert("Found userModifiedStyles sheet: i= " + i + ", title= " + (<StyleSheet>(<any>document.styleSheets[i]).ownerNode).title + ", sheet= " + document.styleSheets[i].ownerNode.textContent);
                return <StyleSheet><any>document.styleSheets[i];
            }
        }
        // alert("Will make userModifiedStyles Sheet:" + document.head.outerHTML);

        var newSheet = document.createElement('style');
        document.getElementsByTagName("head")[0].appendChild(newSheet);
        newSheet.title = "userModifiedStyles";
        newSheet.type = "text/css";
        // alert("newSheet: " + document.head.innerHTML);

        return <StyleSheet><any>newSheet;
    }

    GetOrCreateRuleForStyle(styleName: string, langAttrValue: string): CSSStyleRule {
        var styleSheet = this.GetOrCreateUserModifiedStyleSheet();
        var x: CSSRuleList = (<any>styleSheet).cssRules;
        var styleAndLang = styleName;
        if(langAttrValue && langAttrValue.length > 0)
            styleAndLang = styleName + '[lang="' + langAttrValue + '"]';
        else
            styleAndLang = styleName + ":not([lang])";

        for (var i = 0; i < x.length; i++) {
            if (x[i].cssText.indexOf(styleAndLang) > -1) {
                return <CSSStyleRule> x[i];
            }
        }
        (<CSSStyleSheet>styleSheet).insertRule('.'+styleAndLang + "{ }", x.length);

        return <CSSStyleRule> x[x.length - 1]; //new guy is last
    }

    ConvertPxToPt(pxSize: number): number {
        var tempDiv = document.createElement('div');
        tempDiv.style.width='1000pt';
        document.body.appendChild(tempDiv);
        var ratio = 1000/tempDiv.clientWidth;
        document.body.removeChild(tempDiv);
        tempDiv = null;
        return Math.round(pxSize*ratio);
    }

    GetToolTip(targetBox: HTMLElement, styleName: string): string {
        styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
        var box = $(targetBox);
        var sizeString = box.css('font-size'); // always returns computed size in pixels
        var pxSize = parseInt(sizeString); // strip off units and parse
        var ptSize = this.ConvertPxToPt(pxSize);
        var lang = box.attr('lang');
        return "Changes the text size for all boxes carrying the style \'"+styleName+"\' and language \'"+lang+"\'.\nCurrent size is "+ptSize+"pt.";
    }

    AddQtipToElement(element: JQuery, toolTip: string) {
        element.qtip( {
            content: toolTip,
            show: {
                event: 'click mouseenter'
            },
            hide: 'focusout'
        });
    }

    AttachToBox(targetBox: HTMLElement) {
        var styleName = StyleEditor.GetStyleNameForElement(targetBox);
        if (!styleName)
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

        var toolTip = this.GetToolTip(targetBox, styleName);
        var bottom = $(targetBox).position().top + $(targetBox).height();
        var t = bottom + "px";
        $(targetBox).after('<div id="formatButton"  style="top: '+t+'" class="bloom-ui"><img src="' + this._supportFilesRoot + '/img/cogGrey.svg"></div>');
        var formatButton = $('#formatButton');
        this.AddQtipToElement(formatButton, toolTip);
        formatButton.toolbar({
            content: '#format-toolbar',
            position: 'left',
            hideOnClick: false
        });

        var editor = this;
        formatButton.on("toolbarItemClick", function (event, whichButton) {
            if (whichButton.id == "smaller") {
                editor.MakeSmaller(targetBox);
            }
            if (whichButton.id == "bigger") {
                editor.MakeBigger(targetBox);
            }
            formatButton.trigger('click'); // This re-displays the qtip with the new value.
        });
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