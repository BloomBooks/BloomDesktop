/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="toolbar/toolbar.d.ts"/>
var StyleEditor = (function () {
    function StyleEditor(supportFilesRoot) {
        this._supportFilesRoot = supportFilesRoot;

        var sheet = this.GetOrCreateUserModifiedStyleSheet();
    }
    StyleEditor.GetStyleClassFromElement = function (target) {
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
    };

    StyleEditor.prototype.MakeBigger = function (target) {
        this.ChangeSize(target, 2);
    };
    StyleEditor.prototype.MakeSmaller = function (target) {
        this.ChangeSize(target, -2);
    };

    StyleEditor.GetStyleNameForElement = function (target) {
        var styleName = this.GetStyleClassFromElement(target);
        if (!styleName) {
            var parentPage = ($(target).closest(".bloom-page")[0]);

            // Books created with the original (0.9) version of "Basic Book", lacked "x-style" but had all pages starting with an id of 5dcd48df (so we can detect them)
            var pageLineage = $(parentPage).attr('data-pagelineage');
            if ((pageLineage) && pageLineage.substring(0, 8) == '5dcd48df') {
                styleName = "normal-style";
                $(target).addClass(styleName);
            } else {
                return null;
            }
        }
        return styleName;
    };

    StyleEditor.GetLangValueOrNull = function (target) {
        var langAttr = $(target).attr("lang");
        if (!langAttr)
            return null;
        return langAttr.valueOf().toString();
    };

    StyleEditor.prototype.ChangeSize = function (target, change) {
        var styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName)
            return;
        var langAttrValue = StyleEditor.GetLangValueOrNull(target);
        var rule = this.GetOrCreateRuleForStyle(styleName, langAttrValue);
        var sizeString = rule.style.fontSize;
        if (!sizeString)
            sizeString = $(target).css("font-size");
        var units = sizeString.substr(sizeString.length - 2, 2);
        sizeString = (parseInt(sizeString) + change).toString(); //notice that parseInt ignores the trailing units
        rule.style.setProperty("font-size", sizeString + units, "important");
        // alert("New size rule: " + rule.cssText);
    };

    StyleEditor.prototype.ChangeSizeAbsolute = function (target, newSize) {
        var styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName)
            return;
        if (newSize < 6)
            return;
        var langAttrValue = StyleEditor.GetLangValueOrNull(target);
        var rule = this.GetOrCreateRuleForStyle(styleName, langAttrValue);
        var units = "pt";
        var sizeString = newSize.toString();
        rule.style.setProperty("font-size", sizeString + units, "important");
    };

    StyleEditor.prototype.GetOrCreateUserModifiedStyleSheet = function () {
        for (var i = 0; i < document.styleSheets.length; i++) {
            if (document.styleSheets[i].ownerNode.title == "userModifiedStyles") {
                // alert("Found userModifiedStyles sheet: i= " + i + ", title= " + (<StyleSheet>(<any>document.styleSheets[i]).ownerNode).title + ", sheet= " + document.styleSheets[i].ownerNode.textContent);
                return document.styleSheets[i];
            }
        }

        // alert("Will make userModifiedStyles Sheet:" + document.head.outerHTML);
        var newSheet = document.createElement('style');
        document.getElementsByTagName("head")[0].appendChild(newSheet);
        newSheet.title = "userModifiedStyles";
        newSheet.type = "text/css";

        // alert("newSheet: " + document.head.innerHTML);
        return newSheet;
    };

    StyleEditor.prototype.GetOrCreateRuleForStyle = function (styleName, langAttrValue) {
        var styleSheet = this.GetOrCreateUserModifiedStyleSheet();
        var x = styleSheet.cssRules;
        var styleAndLang = styleName;
        if (langAttrValue && langAttrValue.length > 0)
            styleAndLang = styleName + '[lang="' + langAttrValue + '"]';
        else
            styleAndLang = styleName + ":not([lang])";

        for (var i = 0; i < x.length; i++) {
            if (x[i].cssText.indexOf(styleAndLang) > -1) {
                return x[i];
            }
        }
        styleSheet.insertRule('.' + styleAndLang + "{ }", x.length);

        return x[x.length - 1];
    };

    StyleEditor.prototype.ConvertPxToPt = function (pxSize) {
        var tempDiv = document.createElement('div');
        tempDiv.style.width = '1000pt';
        document.body.appendChild(tempDiv);
        var ratio = 1000 / tempDiv.clientWidth;
        document.body.removeChild(tempDiv);
        tempDiv = null;
        return pxSize * ratio;
    };

    StyleEditor.prototype.GetToolTip = function (targetBox, styleName) {
        styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
        var box = $(targetBox);
        var sizeString = box.css('font-size');
        var pxSize = parseInt(sizeString.substr(0, sizeString.length - 2));
        var ptSize = Math.round(this.ConvertPxToPt(pxSize));
        var lang = box.attr('lang');
        return "Changes the text size for all boxes carrying the style \'" + styleName + "\' and language \'" + lang + "\'.\nCurrent size is " + ptSize + "pt.";
    };

    StyleEditor.prototype.AttachToBox = function (targetBox) {
        var styleName = StyleEditor.GetStyleNameForElement(targetBox);
        if (!styleName)
            return;

        if (this._previousBox != null) {
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
        $(targetBox).after('<div id="formatButton"  style="top: ' + t + '" class="bloom-ui"><img src="' + this._supportFilesRoot + '/img/cogGrey.svg"></div>');
        var formatButton = $('#formatButton');
        formatButton.attr('title', toolTip);
        formatButton.toolbar({
            content: '#format-toolbar',
            //position: 'left',//nb: toolbar's June 2013 code, pushes the toolbar out to the left by 1/2 the width of the parent object, easily putting it in negative territory!
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
        });
    };

    StyleEditor.CleanupElement = function (element) {
        //NB: we're placing these controls *after* the target, not inside it; that's why we go up to parent
        $(element).parent().find(".bloom-ui").each(function () {
            $(this).remove();
        });
        $(".tool-container").each(function () {
            $(this).remove();
        });
    };
    return StyleEditor;
})();
//# sourceMappingURL=StyleEditor.js.map
