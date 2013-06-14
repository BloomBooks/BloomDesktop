/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="toolbar/toolbar.d.ts"/>
var StyleEditor = (function () {
    function StyleEditor() {
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
    StyleEditor.prototype.AddStyleEditBoxes = //Make a toolbox off to the side (implemented using qtip), with elements that can be dragged
    //onto the page
    function (bookEditRoot) {
        var self = this;
        $("div.bloom-editable:visible").each(function () {
            var targetBox = this;
            var styleName = StyleEditor.GetStyleNameForElement(targetBox);
            if(!styleName) {
                return;
            }
            //  i couldn't get the nice icomoon icon font/style.css system to work in Bloom or stylizer
            //            $(this).after('<div id="format-toolbar" style="opacity:0; display:none;"><a class="smallerFontButton" id="smaller">a</a><a id="bigger" class="largerFontButton" ><i class="bloom-icon-FontSize"></i></a></div>');
            //          $(this).after('<div id="format-toolbar" style="opacity:0; display:none;"><a class="smallerFontButton" id="smaller"><img src="' + bookEditRoot + '/img/FontSizeLetter.svg"></a><a id="bigger" class="largerFontButton" ><img src="' + bookEditRoot + '/img/FontSizeLetter.svg"></a></div>');
            $(this).after('<div id="format-toolbar" class="ui" style="opacity:0; display:none;"><a class="smallerFontButton" id="smaller">x</a><a id="bigger" class="largerFontButton" >y</a></div>');
            //add a button to bring up the formatting toolbar
            //            $(this).after('<div id="formatButton" title="Change text size. Affects all similar boxes in this document"><i class="bloom-icon-cog"></i></div>');
            $(this).after('<div id="formatButton"  class="ui" title="Change text size. Affects all similar boxes in this document"><img src="' + bookEditRoot + '/img/cogGrey.svg"></div>');
            $('#formatButton').toolbar({
                content: '#format-toolbar',
                position: //position: 'left',//nb: toolbar's June 2013 code, for some reason, pushes the toolbar out to the left by 1/2 the width of the parent object, easily putting it in negative territory!
                'bottom',
                hideOnClick: true
            });
            $('#formatButton').on("toolbarItemClick", function (event, whichButton) {
                if(whichButton.id == "smaller") {
                    var editor = new StyleEditor();
                    editor.MakeSmaller(targetBox);
                }
                if(whichButton.id == "bigger") {
                    var editor = new StyleEditor();
                    editor.MakeBigger(targetBox);
                }
            });
        });
    };
    StyleEditor.Cleanup = function Cleanup() {
        $(".ui").each(function () {
            $(this).remove();
        });
    };
    StyleEditor.CleanupElement = function CleanupElement(element) {
        $(element).find(".ui").each(function () {
            $(this).remove();
        });
    };
    return StyleEditor;
})();
//@ sourceMappingURL=StyleEditor.js.map
