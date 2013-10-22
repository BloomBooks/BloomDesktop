/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="toolbar/toolbar.d.ts"/>
var StyleEditor = (function () {
    function StyleEditor(supportFilesRoot) {
        this._supportFilesRoot = supportFilesRoot;
        //        this.styleElement = <HTMLElement><any>($(doc).find(".styleEditorStuff").first()); //the <any> here is to turn off the typscript process erro
        //        if (!this.styleElement) {
        //            var s = $('<style id="documentStyles" class="styleEditorStuff" type="text/css"></style>');
        //            $(doc).find("head").append(s);
        //            this.styleElement = $(doc).find('.styleEditorStuff')[0];
        //        }
        var sheet = this.GetOrCreateCustomStyleSheet();
    }
    StyleEditor.GetStyleClassFromElement = function GetStyleClassFromElement(target) {
        var c = $(target).attr("class");
        if(!c) {
            c = "";
        }
        var classes = c.split(' ');
        for(var i = 0; i < classes.length; i++) {
            if(classes[i].indexOf('-style') > 0) {
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
    StyleEditor.GetStyleNameForElement = function GetStyleNameForElement(target) {
        var styleName = this.GetStyleClassFromElement(target);
        if(!styleName) {
            var parentPage = ($(target).closest(".bloom-page")[0]);
            // Books created with the original (0.9) version of "Basic Book", lacked "x-style" but had all pages starting with an id of 5dcd48df (so we can detect them)
            var pageLineage = $(parentPage).attr('data-pagelineage');
            if((pageLineage) && pageLineage.substring(0, 8) == '5dcd48df') {
                styleName = "default-style";
                $(target).addClass(styleName);
            } else {
                return null;
            }
        }
        return styleName;
    };
    StyleEditor.prototype.ChangeSize = function (target, change) {
        var styleName = StyleEditor.GetStyleNameForElement(target);
        if(!styleName) {
            return;
        }
        var rule = this.GetOrCreateRuleForStyle(styleName);
        var sizeString = (rule).style.fontSize;
        if(!sizeString) {
            sizeString = $(target).css("font-size");
        }
        var units = sizeString.substr(sizeString.length - 2, 2);
        sizeString = (parseInt(sizeString) + change).toString()//notice that parseInt ignores the trailing units
        ;
        (rule).style.setProperty("font-size", sizeString + units, "important");
    };
    StyleEditor.prototype.GetOrCreateCustomStyleSheet = function () {
        for(var i = 0; i < document.styleSheets.length; i++) {
            if((document.styleSheets[i]).ownerNode.id == "customBookStyles") {
                return document.styleSheets[i];
            }
        }
        //alert("Will make customBookStyles Sheet:" + document.head.outerHTML);
        var newSheet = document.createElement('style');
        newSheet.id = "customBookStyles";
        document.getElementsByTagName('head')[0].appendChild(newSheet);
        newSheet.title = "customBookStyles";
        return newSheet;
    };
    StyleEditor.prototype.GetOrCreateRuleForStyle = function (styleName) {
        var styleSheet = this.GetOrCreateCustomStyleSheet();
        var x = (styleSheet).cssRules;
        for(var i = 0; i < x.length; i++) {
            if(x[i].cssText.indexOf(styleName) > -1) {
                return x[i];
            }
        }
        (styleSheet).insertRule('.' + styleName + ' {}', 0);
        return x[0];//new guy is first
        
    };
    StyleEditor.prototype.AttachToBox = //Make a toolbox off to the side (implemented using qtip), with elements that can be dragged
    //onto the page
    function (targetBox) {
        if(!StyleEditor.GetStyleNameForElement(targetBox)) {
            return;
        }
        if(this._previousBox != null) {
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
        $(targetBox).after('<div id="formatButton"  style="top: ' + t + '" class="bloom-ui" title="Change text size. Affects all similar boxes in this document"><img src="' + this._supportFilesRoot + '/img/cogGrey.svg"></div>');
        $('#formatButton').toolbar({
            content: '#format-toolbar',
            position: //position: 'left',//nb: toolbar's June 2013 code, pushes the toolbar out to the left by 1/2 the width of the parent object, easily putting it in negative territory!
            'left',
            hideOnClick: false
        });
        var editor = this;
        $('#formatButton').on("toolbarItemClick", function (event, whichButton) {
            if(whichButton.id == "smaller") {
                editor.MakeSmaller(targetBox);
            }
            if(whichButton.id == "bigger") {
                editor.MakeBigger(targetBox);
            }
        });
    };
    StyleEditor.prototype.DetachFromBox = function (element) {
        //  StyleEditor.CleanupElement(element);
            };
    StyleEditor.CleanupElement = function CleanupElement(element) {
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
//@ sourceMappingURL=StyleEditor.js.map
