/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="toolbar/toolbar.d.ts"/>

var global = getGlobalObject();

var StyleEditor = (function () {
    function StyleEditor(supportFilesRoot) {
        this.MIN_FONT_SIZE = 7;
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
    };

    StyleEditor.prototype.MakeBigger = function (target) {
        this.ChangeSize(target, 2);
        $("div.bloom-editable, textarea").qtip('reposition');
    };
    StyleEditor.prototype.MakeSmaller = function (target) {
        this.ChangeSize(target, -2);
        $("div.bloom-editable, textarea").qtip('reposition');
    };

    StyleEditor.MigratePreStyleBook = function (target) {
        var parentPage = ($(target).closest(".bloom-page")[0]);

        // Books created with the original (0.9) version of "Basic Book", lacked "x-style" but had all pages starting with an id of 5dcd48df (so we can detect them)
        var pageLineage = $(parentPage).attr('data-pagelineage');
        if ((pageLineage) && pageLineage.substring(0, 8) == '5dcd48df') {
            var styleName = "normal-style";
            $(target).addClass(styleName);
            return styleName;
        }
        return null;
    };

    StyleEditor.GetStyleNameForElement = function (target) {
        var styleName = this.GetStyleClassFromElement(target);
        if (!styleName) {
            // The style name is probably on the parent translationGroup element
            var parentGroup = ($(target).parent(".bloom-translationGroup")[0]);
            if (parentGroup) {
                styleName = this.GetStyleClassFromElement(parentGroup);
                if (styleName)
                    $(target).addClass(styleName); // add style to bloom-editable div
                else {
                    return this.MigratePreStyleBook(target);
                }
            } else {
                // No .bloom-translationGroup? Unlikely...
                return this.MigratePreStyleBook(target);
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
        var fontSize = this.GetCalculatedFontSizeInPoints(target);
        var langAttrValue = StyleEditor.GetLangValueOrNull(target);
        var rule = this.GetOrCreateRuleForStyle(styleName, langAttrValue);
        var units = 'pt';
        var sizeString = (fontSize + change).toString();
        if (parseInt(sizeString) < this.MIN_FONT_SIZE)
            return;
        rule.style.setProperty("font-size", sizeString + units, "important");
        if ($(target).IsOverflowing())
            $(target).addClass('overflow');
        else
            $(target).removeClass('overflow'); // If it's not here, this won't hurt anything.

        // alert("New size rule: " + rule.cssText);
        // Now update tooltip
        var toolTip = this.GetToolTip(target, styleName);
        this.AddQtipToElement($('#formatButton'), toolTip);
    };

    StyleEditor.prototype.GetCalculatedFontSizeInPoints = function (target) {
        var sizeInPx = $(target).css('font-size');
        return this.ConvertPxToPt(parseInt(sizeInPx));
    };

    StyleEditor.prototype.ChangeSizeAbsolute = function (target, newSize) {
        var styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName) {
            alert('ChangeSizeAbsolute called on an element with invalid style class.');
            return;
        }
        if (newSize < this.MIN_FONT_SIZE) {
            alert('ChangeSizeAbsolute called with too small a point size.');
            return;
        }
        var langAttrValue = StyleEditor.GetLangValueOrNull(target);
        var rule = this.GetOrCreateRuleForStyle(styleName, langAttrValue);
        var units = "pt";
        var sizeString = newSize.toString();
        rule.style.setProperty("font-size", sizeString + units, "important");

        // Now update tooltip
        var toolTip = this.GetToolTip(target, styleName);
        this.AddQtipToElement($('#formatButton'), toolTip);
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
        return Math.round(pxSize * ratio);
    };

    /**
    * Get the style information off of the target element to display in the tooltip
    * @param {HTMLElement} targetBox the element with the style information
    * @param {string} styleName the style whose information we are reporting
    * @return returns the tooltip string
    */
    StyleEditor.prototype.GetToolTip = function (targetBox, styleName) {
        styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
        var box = $(targetBox);
        var sizeString = box.css('font-size');
        var pxSize = parseInt(sizeString);
        var ptSize = this.ConvertPxToPt(pxSize);
        var lang = box.attr('lang');

        // localize
        var tipText = "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\nCurrent size is {2}pt.";
        return localizationManager.getText('BookEditor.FontSizeTip', tipText, styleName, lang, ptSize);
    };

    /**
    * Adds a tooltip to an element
    * @param element a JQuery object to add the tooltip to
    * @param toolTip the text of the tooltip to display
    * @param delay how many milliseconds to display the tooltip (defaults to 3sec)
    */
    StyleEditor.prototype.AddQtipToElement = function (element, toolTip, delay) {
        if (typeof delay === "undefined") { delay = 3000; }
        if (element.length == 0)
            return;
        element.qtip({
            content: toolTip,
            show: {
                event: 'click mouseenter',
                solo: true
            },
            hide: {
                event: 'unfocus',
                inactive: delay
            }
        });
    };

    StyleEditor.prototype.AttachToBox = function (targetBox) {
        var styleName = StyleEditor.GetStyleNameForElement(targetBox);
        if (!styleName)
            return;

        if (this._previousBox != null) {
            StyleEditor.CleanupElement(this._previousBox);
        }
        this._previousBox = targetBox;

        var toolTip = this.GetToolTip(targetBox, styleName);
        var bottom = $(targetBox).position().top + $(targetBox).height();
        var t = bottom + "px";

        var editor = this;
        $(targetBox).after('<div id="formatButton"  style="top: ' + t + '" class="bloom-ui"><img src="' + editor._supportFilesRoot + '/img/cogGrey.svg"></div>');
        var formatButton = $('#formatButton');
        editor.AddQtipToElement(formatButton, localizationManager.getText('EditTab.StyleEditorTip', 'Adjust formatting for style'), 1500);
        formatButton.click(function () {
            global.simpleAjaxGet('/bloom/availableFontNames', function (fontData) {
                editor.boxBeingEdited = targetBox;
                styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
                var box = $(targetBox);
                var sizeString = box.css('font-size');
                var pxSize = parseInt(sizeString);
                var ptSize = editor.ConvertPxToPt(pxSize);
                var lang = box.attr('lang');
                var fontName = box.css('font-family');
                if (fontName[0] == '\'' || fontName[0] == '"') {
                    fontName = fontName.substring(1, fontName.length - 1); // strip off quotes
                }

                var lineHeightString = box.css('line-height');
                var lineHeightPx = parseInt(lineHeightString);
                var lineHeightNumber = Math.round(lineHeightPx / pxSize * 10) / 10.0;
                var lineSpaceOptions = ['1.0', '1.1', '1.2', '1.3', '1.4', '1.5', '1.6', '1.8', '2.0', '2.5', '3.0'];
                var lineHeight;
                for (var i = 0; i < lineSpaceOptions.length; i++) {
                    var optionNumber = parseFloat(lineSpaceOptions[i]);
                    if (lineHeightNumber == optionNumber) {
                        lineHeight = lineSpaceOptions[i];
                        break;
                    }
                    if (lineHeightNumber <= optionNumber) {
                        lineHeight = lineSpaceOptions[i];
                        break;
                    }
                }
                if (lineHeightNumber > parseFloat(lineSpaceOptions[lineSpaceOptions.length - 1])) {
                    lineHeight = lineSpaceOptions[lineSpaceOptions.length - 1];
                }

                var wordSpaceOptions = [
                    localizationManager.getText('EditTab.StyleEditor.WordSpacingNormal', 'Normal'),
                    localizationManager.getText('EditTab.StyleEditor.WordSpacingWide', 'Wide'),
                    localizationManager.getText('EditTab.StyleEditor.WordSpacingExtraWide', 'Extra Wide')];
                var wordSpaceString = box.css('word-spacing');
                var wordSpacing = wordSpaceOptions[0];
                if (wordSpaceString != "0px") {
                    var pxSpace = parseInt(wordSpaceString);
                    var ptSpace = editor.ConvertPxToPt(pxSpace);
                    if (ptSpace > 7.5) {
                        wordSpacing = wordSpaceOptions[2];
                    } else {
                        wordSpacing = wordSpaceOptions[1];
                    }
                }

                //alert('font: ' + fontName + ' size: ' + sizeString + ' height: ' + lineHeight + ' space: ' + wordSpacing);
                // Enhance: lineHeight may well be something like 35px; what should we select initially?
                var fonts = fontData.split(',');
                var sizes = ['7', '8', '9', '10', '11', '12', '14', '16', '18', '20', '22', '24', '26', '28', '36', '48', '72'];
                var html = '<div id="format-toolbar" style="background-color:white;opacity:1;z-index:900;position:absolute;line-height:1.8;font-family:Segoe UI" class="bloom-ui">' + '<div style="background-color:darkGrey;opacity:1;position:relative;top:0;left:0;right:0;height: 10pt;margin-bottom: 5pt"></div>' + editor.makeSelect(fonts, 5, fontName, 'fontSelect', 15) + ' ' + editor.makeSelect(sizes, 5, ptSize, 'sizeSelect') + ' ' + '<span style="white-space: nowrap">' + '<img src="' + editor._supportFilesRoot + '/img/LineSpacing.png" style="margin-left:8px;position:relative;top:6px">' + editor.makeSelect(lineSpaceOptions, 2, lineHeight, 'lineHeightSelect') + ' ' + '</span>' + ' ' + '<span style="white-space: nowrap">' + '<img src="' + editor._supportFilesRoot + '/img/WordSpacing.png" style="margin-left:8px;position:relative;top:6px">' + editor.makeSelect(wordSpaceOptions, 2, wordSpacing, 'wordSpaceSelect') + '</span>' + ' ' + '<span style="white-space: nowrap">' + '<div style="margin-left:5px;display:inline-block;border:2px solid black;height:10pt;width:10pt;margin-right:2px;position:relative;top:2px"></div>' + editor.makeBorderSelect(box) + '</span>' + '<div style="color:grey;margin-top:10px">This formatting is for all ' + lang + ' text in boxes with \'' + styleName + '\' style</div>' + '</div>';
                $('#format-toolbar').remove(); // in case there's still one somewhere else
                $('body').after(html);
                var toolbar = $('#format-toolbar');
                toolbar.draggable();
                toolbar.css('opacity', 1.0);
                $('#fontSelect').change(function () {
                    editor.changeFont();
                });
                editor.AddQtipToElement($('#fontSelect'), localizationManager.getText('EditTab.StyleEditor.FontFaceToolTip', 'Change the font face'), 1500);
                $('#sizeSelect').change(function () {
                    editor.changeSize();
                });
                editor.AddQtipToElement($('#sizeSelect'), localizationManager.getText('EditTab.StyleEditor.FontSizeToolTip', 'Change the font size'), 1500);
                $('#lineHeightSelect').change(function () {
                    editor.changeLineheight();
                });
                editor.AddQtipToElement($('#lineHeightSelect').parent(), localizationManager.getText('EditTab.StyleEditor.LineSpacingToolTip', 'Change the spacing between lines of text'), 1500);
                $('#wordSpaceSelect').change(function () {
                    editor.changeWordSpace();
                });
                editor.AddQtipToElement($('#wordSpaceSelect').parent(), localizationManager.getText('EditTab.StyleEditor.WordSpacingToolTip', 'Change the spacing between words'), 1500);
                $('#borderSelect').change(function () {
                    editor.changeBorderSelect();
                });
                editor.AddQtipToElement($('#borderSelect').parent(), localizationManager.getText('EditTab.StyleEditor.BorderToolTip', 'Change the border and background'), 1500);
                var offset = $('#formatButton').offset();
                toolbar.offset({ left: offset.left + 30, top: offset.top - 30 });

                //alert(offset.left + "," + $(document).width() + "," + $(targetBox).offset().left);
                toolbar.width($(".bloom-page").width() - offset.left - 50);
                $('html').off('click.toolbar');
                $('html').on("click.toolbar", function (event) {
                    if (event.target != toolbar && toolbar.has(event.target).length === 0 && $(event.target.parent) != toolbar && toolbar.has(event.target).length === 0 && toolbar.is(":visible")) {
                        toolbar.remove();
                        event.stopPropagation();
                        event.preventDefault();
                    }
                });
                toolbar.on("click.toolbar", function (event) {
                    // this stops an event inside the dialog from propagating to the html element, which would close the dialog
                    event.stopPropagation();
                });
                //formatButton.toolbar({
                //    content: '#format-toolbar',
                //    position: 'right',
                //    hideOnClick: true
                //});
            });
        });

        editor.AttachLanguageTip($(targetBox), bottom);
    };

    StyleEditor.prototype.makeSelect = function (items, marginLeft, current, id, maxlength) {
        var result = '<select id="' + id + '" style="margin-left:' + marginLeft + 'px">';
        for (var i = 0; i < items.length; i++) {
            var selected = "";
            if (current == items[i])
                selected = ' selected';
            var text = items[i];
            if (maxlength && text.length > maxlength) {
                text = text.substring(0, maxlength) + "...";
            }
            result += '<option value="' + items[i] + '"' + selected + '>' + text + '</option>';
        }
        return result + '</select>';
    };

    StyleEditor.prototype.makeBorderSelect = function (box) {
        var borderStyle = box.css('border-bottom-style');
        var borderColor = box.css('border-bottom-color');
        var borderRadius = box.css('border-top-left-radius');
        var backColor = box.css('background-color');

        //alert(borderStyle + ',' + borderColor + ',' + borderRadius + ',' + backColor);
        var noneSelected = "";
        var blackSelected = "";
        var blackGreySelected = "";
        var blackRoundSelected = "";
        var greySelected = "";
        var greyRoundSelected = "";

        // Detecting 'none' is difficult because our edit boxes inherit a faint grey border
        // Currently we use plain rgb for our official borders, and the inherited one uses rgba(0, 0, 0, 0.2).
        // Rather arbitrarily we will consider a border less than 50% opaque to be 'none'.
        if (!borderStyle || borderStyle === 'none' || !borderColor || (borderColor.toLowerCase().startsWith("rgba(") && parseFloat(borderColor.split(',')[3]) < 0.5)) {
            noneSelected = ' selected';
        } else if (borderColor.toLowerCase() == 'rgb(128, 128, 128)') {
            if (parseInt(borderRadius) == 0) {
                greySelected = ' selected';
            } else {
                greyRoundSelected = ' selected';
            }
        } else if (backColor.toLowerCase() == 'rgb(211, 211, 211)') {
            blackGreySelected = ' selected';
        } else if (parseInt(borderRadius) > 0) {
            blackRoundSelected = ' selected';
        } else {
            blackSelected = ' selected';
        }

        var result = '<select id="borderSelect">';
        result += '<option value="none"' + noneSelected + '>None</option>';
        result += '<option value="black"' + blackSelected + '>Black Border</option>';
        result += '<option value="black-grey"' + blackGreySelected + '>&nbsp;&nbsp;...Grey Background</option>';
        result += '<option value="black-round"' + blackRoundSelected + '>&nbsp;&nbsp;...Rounded</option>';
        result += '<option value="grey"' + greySelected + '>Grey Border</option>';
        result += '<option value="grey-round"' + greyRoundSelected + '>&nbsp;&nbsp;...Rounded</option>';
        return result + '</select>';
    };

    StyleEditor.prototype.changeFont = function () {
        var rule = this.getStyleRule();
        var font = $('#fontSelect').val();
        rule.style.setProperty("font-family", font, "important");
        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.changeSize = function () {
        var rule = this.getStyleRule();
        var fontSize = $('#sizeSelect').val();
        var units = 'pt';
        var sizeString = fontSize.toString();
        if (parseInt(sizeString) < this.MIN_FONT_SIZE)
            return;
        rule.style.setProperty("font-size", sizeString + units, "important");
        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.changeLineheight = function () {
        var rule = this.getStyleRule();
        var lineHeight = $('#lineHeightSelect').val();
        rule.style.setProperty("line-height", lineHeight, "important");
        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.changeWordSpace = function () {
        var rule = this.getStyleRule();
        var wordSpace = $('#wordSpaceSelect').val();
        if (wordSpace === 'Wide')
            wordSpace = '5pt';
        else if (wordSpace === 'Extra Wide') {
            wordSpace = '10pt';
        }
        rule.style.setProperty("word-spacing", wordSpace, "important");
        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.changeBorderSelect = function () {
        var rule = this.getStyleRule();
        var borderOpt = $('#borderSelect').val();
        switch (borderOpt) {
            case 'none':
                //rule.style.setProperty("border-style", "none");
                rule.style.removeProperty("border-style");
                rule.style.removeProperty("border");
                rule.style.removeProperty("border-color");
                rule.style.setProperty("background-color", "transparent ");
                rule.style.setProperty("border-radius", "0px");
                break;
            case 'black':
                rule.style.setProperty("border", "1pt solid black");
                rule.style.setProperty("background-color", "transparent ");
                rule.style.setProperty("border-radius", "0px");
                break;
            case 'black-grey':
                rule.style.setProperty("border", "1pt solid black");
                rule.style.setProperty("background-color", "LightGray ");
                rule.style.setProperty("border-radius", "0px");
                break;
            case 'black-round':
                rule.style.setProperty("border", "1pt solid black");
                rule.style.setProperty("border-radius", "10px");
                rule.style.setProperty("background-color", "transparent ");
                break;
            case 'grey':
                rule.style.setProperty("border", "1pt solid Grey");
                rule.style.setProperty("background-color", "transparent ");
                rule.style.setProperty("border-radius", "0px");
                break;
            case 'grey-round':
                rule.style.setProperty("border", "1pt solid Grey");
                rule.style.setProperty("border-radius", "10px");
                rule.style.setProperty("background-color", "transparent ");
                break;
        }

        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.getStyleRule = function () {
        var target = this.boxBeingEdited;
        var styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName)
            return;
        var langAttrValue = StyleEditor.GetLangValueOrNull(target);
        return this.GetOrCreateRuleForStyle(styleName, langAttrValue);
    };

    StyleEditor.prototype.cleanupAfterStyleChange = function () {
        var target = this.boxBeingEdited;
        var styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName)
            return;
        if ($(target).IsOverflowing())
            $(target).addClass('overflow');
        else
            $(target).removeClass('overflow'); // If it's not here, this won't hurt anything.
        // alert("New size rule: " + rule.cssText);
        // Now update tooltip
        //var toolTip = this.GetToolTip(target, styleName);
        //this.AddQtipToElement($('#formatButton'), toolTip);
    };

    //Attach and detach a language tip which is used when the applicable edittable div has focus.
    //This works around a couple FF bugs with the :after pseudoelement.  See BL-151.
    StyleEditor.prototype.AttachLanguageTip = function (targetBox, bottom) {
        if ($(targetBox).attr('data-languagetipcontent')) {
            $(targetBox).after('<div style="top: ' + (bottom - 17) + 'px" class="languageTip bloom-ui">' + $(targetBox).attr('data-languagetipcontent') + '</div>');
        }
    };

    StyleEditor.prototype.DetachLanguageTip = function (element) {
        //we're placing these controls *after* the target, not inside it; that's why we go up to parent
        $(element).parent().find(".languageTip.bloom-ui").each(function () {
            $(this).remove();
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
