/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
/// <reference path="../../lib/localizationManager.ts" />
/// <reference path="../../lib/misc-types.d.ts" />
/// <reference path="toolbar/toolbar.d.ts"/>
/// <reference path="getIframeChannel.ts"/>
var iframeChannel = getIframeChannel();

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
        // In case this is one of those books, we'll replace it with 'Title-On-Cover-style'
        var coverTitleClass = StyleEditor.updateCoverStyleName(target, 'coverTitle');

        // For awhile in v2 we used 'coverTitle-style' in Factory-XMatter
        // In case this is one of those books, we'll replace it with 'Title-On-Cover-style'
        if (!coverTitleClass)
            coverTitleClass = StyleEditor.updateCoverStyleName(target, 'coverTitle-style');

        return coverTitleClass;
    };

    StyleEditor.updateCoverStyleName = function (target, oldCoverTitleClass) {
        if ($(target).hasClass(oldCoverTitleClass)) {
            var newStyleName = 'Title-On-Cover-style';
            $(target).removeClass(oldCoverTitleClass).addClass(newStyleName);
            return newStyleName;
        }

        return null;
    };

    // obsolete?
    StyleEditor.prototype.MakeBigger = function (target) {
        this.ChangeSize(target, 2);
        $("div.bloom-editable, textarea").qtip('reposition');
    };

    // obsolete?
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

    StyleEditor.SetStyleNameForElement = function (target, newStyle) {
        var oldStyle = this.GetStyleClassFromElement(target);
        $(target).removeClass(oldStyle);
        $(target).addClass(newStyle);
    };

    StyleEditor.GetLangValueOrNull = function (target) {
        var langAttr = $(target).attr("lang");
        if (!langAttr)
            return null;
        return langAttr.valueOf().toString();
    };

    // obsolete?
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

    // Get the names that should be offered in the styles combo box.
    // Basically any defined styles without dots in their definition (except the first one).
    // (We don't allow users to create styles with dot or any other special characters.)
    StyleEditor.prototype.getFormattingStyles = function () {
        var result = [];
        for (var i = 0; i < document.styleSheets.length; i++) {
            var sheet = document.styleSheets[i];
            var rules = sheet.cssRules;
            if (rules) {
                for (var j = 0; j < rules.length; j++) {
                    var index = rules[j].cssText.indexOf('{');
                    if (index == -1)
                        continue;
                    var label = rules[j].cssText.substring(0, index);
                    var index2 = label.indexOf("-style");
                    if (index2 > 0 && label.startsWith(".")) {
                        var name = label.substring(1, index2);
                        if (name.indexOf(".") == -1) {
                            result.push(name);
                        }
                    }
                }
            }
        }

        // It's bizarre not to offer 'normal' since that's the standard initial style.
        // But in fact our default template doesn't define it.
        if (result.indexOf('normal') == -1) {
            result.push('normal');
        }
        return result;
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

        // if we are authoring a book, style changes should apply to all translations of it
        // if we are translating, changes should only apply to this language.
        // a downside of this is that when authoring in multiple languages, to get a different
        // appearance for different languages a different style must be created.
        if (!this.authorMode) {
            if (langAttrValue && langAttrValue.length > 0)
                styleAndLang = styleName + '[lang="' + langAttrValue + '"]';
            else
                styleAndLang = styleName + ":not([lang])";
        }
        for (var i = 0; i < x.length; i++) {
            if (x[i].cssText.indexOf(styleAndLang) > -1) {
                return x[i];
            }
        }
        styleSheet.insertRule('.' + styleAndLang + "{ }", x.length);

        return x[x.length - 1];
    };

    StyleEditor.prototype.ConvertPxToPt = function (pxSize, round) {
        if (typeof round === "undefined") { round = true; }
        var tempDiv = document.createElement('div');
        tempDiv.style.width = '1000pt';
        document.body.appendChild(tempDiv);
        var ratio = 1000 / tempDiv.clientWidth;
        document.body.removeChild(tempDiv);
        tempDiv = null;
        if (round)
            return Math.round(pxSize * ratio);
        else
            return pxSize * ratio;
    };

    /**
    * Get the style information off of the target element to display in the tooltip
    * @param {HTMLElement} targetBox the element with the style information
    * @param {string} styleName the style whose information we are reporting
    * @return returns the tooltip string
    */
    StyleEditor.prototype.GetToolTip = function (targetBox, styleName) {
        //Review: Gordon (JH) I'm not clear if this is still used or why, since it seems to be duplicated in AttachToBox
        styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
        styleName = styleName.replace(/-/g, ' '); //show users a space instead of dashes
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

    StyleEditor.GetClosestValueInList = function (listOfOptions, valueToMatch) {
        var lineHeight;
        for (var i = 0; i < listOfOptions.length; i++) {
            var optionNumber = parseFloat(listOfOptions[i]);
            if (valueToMatch == optionNumber) {
                lineHeight = listOfOptions[i];
                break;
            }
            if (valueToMatch <= optionNumber) {
                lineHeight = listOfOptions[i];

                // possibly it is closer to the option before
                if (i > 0) {
                    var prevOptionNumber = parseFloat(listOfOptions[i - 1]);
                    var deltaCurrent = optionNumber - valueToMatch;
                    var deltaPrevious = valueToMatch - prevOptionNumber;
                    if (deltaPrevious < deltaCurrent) {
                        lineHeight = listOfOptions[i - 1];
                    }
                }
                break;
            }
        }
        if (valueToMatch > parseFloat(listOfOptions[listOfOptions.length - 1])) {
            lineHeight = listOfOptions[listOfOptions.length - 1];
        }
        return lineHeight;
    };

    StyleEditor.prototype.getPointSizes = function () {
        return ['7', '8', '9', '10', '11', '12', '14', '16', '18', '20', '22', '24', '26', '28', '36', '48', '72'];
    };

    StyleEditor.prototype.getLineSpaceOptions = function () {
        return ['1.0', '1.1', '1.2', '1.3', '1.4', '1.5', '1.6', '1.8', '2.0', '2.5', '3.0'];
    };

    StyleEditor.prototype.getWordSpaceOptions = function () {
        return [
            localizationManager.getText('EditTab.StyleEditor.WordSpacingNormal', 'Normal'),
            localizationManager.getText('EditTab.StyleEditor.WordSpacingWide', 'Wide'),
            localizationManager.getText('EditTab.StyleEditor.WordSpacingExtraWide', 'Extra Wide')];
    };

    // Returns an object giving the current selection for each format control.
    StyleEditor.prototype.getFormatValues = function () {
        var box = $(this.boxBeingEdited);
        var sizeString = box.css('font-size');
        var pxSize = parseInt(sizeString);
        var ptSize = this.ConvertPxToPt(pxSize, false);
        var sizes = this.getPointSizes();
        ptSize = StyleEditor.GetClosestValueInList(sizes, ptSize);

        var fontName = box.css('font-family');
        if (fontName[0] == '\'' || fontName[0] == '"') {
            fontName = fontName.substring(1, fontName.length - 1); // strip off quotes
        }

        var lineHeightString = box.css('line-height');
        var lineHeightPx = parseInt(lineHeightString);
        var lineHeightNumber = Math.round(lineHeightPx / pxSize * 10) / 10.0;
        var lineSpaceOptions = this.getLineSpaceOptions();
        var lineHeight = StyleEditor.GetClosestValueInList(lineSpaceOptions, lineHeightNumber);

        var wordSpaceOptions = this.getWordSpaceOptions();

        var wordSpaceString = box.css('word-spacing');
        var wordSpacing = wordSpaceOptions[0];
        if (wordSpaceString != "0px") {
            var pxSpace = parseInt(wordSpaceString);
            var ptSpace = this.ConvertPxToPt(pxSpace);
            if (ptSpace > 7.5) {
                wordSpacing = wordSpaceOptions[2];
            } else {
                wordSpacing = wordSpaceOptions[1];
            }
        }
        var borderStyle = box.css('border-bottom-style');
        var borderColor = box.css('border-bottom-color');
        var borderRadius = box.css('border-top-left-radius');
        var backColor = box.css('background-color');

        //alert(borderStyle + ',' + borderColor + ',' + borderRadius + ',' + backColor);
        var borderChoice = "";

        // Detecting 'none' is difficult because our edit boxes inherit a faint grey border
        // Currently we use plain rgb for our official borders, and the inherited one uses rgba(0, 0, 0, 0.2).
        // Rather arbitrarily we will consider a border less than 50% opaque to be 'none'.
        if (!borderStyle || borderStyle === 'none' || !borderColor || (borderColor.toLowerCase().startsWith("rgba(") && parseFloat(borderColor.split(',')[3]) < 0.5)) {
            borderChoice = 'none';
        } else if (borderColor.toLowerCase() == 'rgb(128, 128, 128)') {
            if (parseInt(borderRadius) == 0) {
                borderChoice = 'grey';
            } else {
                borderChoice = 'grey-round';
            }
        } else if (backColor.toLowerCase() == 'rgb(211, 211, 211)') {
            borderChoice = 'black-grey';
        } else if (parseInt(borderRadius) > 0) {
            borderChoice = 'black-round';
        } else {
            borderChoice = 'black';
        }
        return { ptSize: ptSize, fontName: fontName, lineHeight: lineHeight, wordSpacing: wordSpacing, borderChoice: borderChoice };
    };

    StyleEditor.prototype.AttachToBox = function (targetBox) {
        var styleName = StyleEditor.GetStyleNameForElement(targetBox);
        if (!styleName)
            return;
        var editor = this;

        // I'm assuming here that since we're dealing with a local server, we'll get a result long before
        // the user could actually modify a style and thus need the information.
        // More dangerous is using it in getDescription. But as that is launched by a later
        // async request, I think it should be OK.
        iframeChannel.simpleAjaxGet('/bloom/authorMode', function (result) {
            editor.authorMode = result == "true";
        });

        if (this._previousBox != null) {
            StyleEditor.CleanupElement(this._previousBox);
        }
        this._previousBox = targetBox;

        //wasn't being used: var toolTip = this.GetToolTip(targetBox, styleName);
        var bottom = $(targetBox).position().top + $(targetBox).height();
        var t = bottom + "px";

        $(targetBox).after('<div id="formatButton"  style="top: ' + t + '; min-height: 21px" class="bloom-ui"><img src="' + editor._supportFilesRoot + '/img/cogGrey.svg"></div>');
        var formatButton = $('#formatButton');
        var txt = localizationManager.getText('EditTab.StyleEditorTip', 'Adjust formatting for style');
        editor.AddQtipToElement(formatButton, txt, 1500);
        formatButton.click(function () {
            iframeChannel.simpleAjaxGet('/bloom/availableFontNames', function (fontData) {
                editor.boxBeingEdited = targetBox;
                styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
                styleName = styleName.replace(/-/g, ' '); //show users a space instead of dashes
                var box = $(targetBox);
                var lang = box.attr('lang');
                lang = localizationManager.getText(lang);
                var current = editor.getFormatValues();

                //alert('font: ' + fontName + ' size: ' + sizeString + ' height: ' + lineHeight + ' space: ' + wordSpacing);
                // Enhance: lineHeight may well be something like 35px; what should we select initially?
                var fonts = fontData.split(',');
                var forTextInLang = editor.getDescription();
                editor.styles = editor.getFormattingStyles();
                if (editor.styles.indexOf(styleName) == -1) {
                    editor.styles.push(styleName);
                }
                editor.styles.sort(function (a, b) {
                    return a.toLowerCase().localeCompare(b.toLowerCase());
                });
                var borderItems = ['none', 'black', 'black-grey', 'black-round', 'grey', 'grey-round'];

                var html = '<div id="format-toolbar" style="background-color:white;opacity:1;z-index:900;position:absolute;line-height:1.8;font-family:Segoe UI" class="bloom-ui">' + '<div style="background-color:darkGrey;opacity:1;position:relative;top:0;left:0;right:0;height: 10pt"></div>' + '<div class="tab-pane" id="tabRoot">' + '<div class="tab-page" id="formatPage"><h2 class="tab">Format</h2>' + '<div>' + editor.makeSelect(fonts, 5, current.fontName, 'fontSelect', 15) + ' ' + editor.makeSelect(editor.getPointSizes(), 5, current.ptSize, 'sizeSelect') + ' ' + '<span style="white-space: nowrap">' + '<img src="' + editor._supportFilesRoot + '/img/LineSpacing.png" style="margin-left:8px;position:relative;top:6px">' + editor.makeSelect(editor.getLineSpaceOptions(), 2, current.lineHeight, 'lineHeightSelect') + ' ' + '</span>' + ' ' + '<span style="white-space: nowrap">' + '<img src="' + editor._supportFilesRoot + '/img/WordSpacing.png" style="margin-left:8px;position:relative;top:6px">' + editor.makeSelect(editor.getWordSpaceOptions(), 2, current.wordSpacing, 'wordSpaceSelect') + '</span>' + ' ' + '<span style="white-space: nowrap">' + '<div style="margin-left:5px;display:inline-block;border:2px solid black;height:10pt;width:10pt;margin-right:2px;position:relative;top:2px"></div>' + editor.makeSelect(borderItems, 0, current.borderChoice, 'borderSelect') + '</span></div>' + '<div class="format-toolbar-description" id="formatDesc">' + forTextInLang + '</div>' + '</div>' + '<div class="tab-page"><h2 class="tab">Style Name</h2>' + editor.makeEditableSelect(editor.styles, 5, styleName, 'styleSelect') + "</div>" + '</div>' + '</div>';
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
                $('#styleSelect').change(function () {
                    editor.selectStyle();
                });
                $('#styleSelectInput').alpha({ allowSpace: false });
                new WebFXTabPane($('#tabRoot').get(0), false, function (n) {
                    editor.tabSelected(n);
                });
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

    StyleEditor.prototype.getDescription = function () {
        var styleName = StyleEditor.GetStyleNameForElement(this.boxBeingEdited);
        if (styleName) {
            var index = styleName.indexOf("-style");
            if (index > 0)
                styleName = styleName.substring(0, index);
        }
        var lang = "";
        if (!this.authorMode) {
            lang = $(this.boxBeingEdited).attr('lang');
        }

        // The replace fixes the double-space typically left if language is not specified.
        // Enhance: we may need a better way of localizing when language is not specified, since just leaving it out may not
        // produce nice sentences in every language. However, we already have several translations of this string, so I'd
        // rather not change it unless we must.
        return localizationManager.getText('BookEditor.ForTextInLang', 'This formatting is for all {0} text in boxes with \'{1}\' style', lang, styleName).replace("  ", " ");
    };

    StyleEditor.prototype.tabSelected = function (n) {
        if (n != 0)
            return;

        // switching back to format tab. User may have defined a new style.
        var typedStyle = $('#styleSelectInput').val();
        if (!typedStyle) {
            // If the user didn't type a new style name, there is nothing to do.
            // We updated the format controls when the style was selected.
            return;
        }

        for (var i = 0; i < this.styles.length; i++) {
            if (typedStyle == this.styles[i]) {
                // just act as if he'd selected that item
                $('#styleSelect').val(typedStyle);
                this.selectStyle(); // surprisingly, this doesn't happen automatically
                return;
            }
        }

        // Make a new style. Initialize to all current values.
        StyleEditor.SetStyleNameForElement(this.boxBeingEdited, typedStyle + '-style');
        this.changeFont();
        this.changeSize();
        this.changeLineheight();
        this.changeWordSpace();
        this.changeBorderSelect();

        // Insert it into our list and the option control on the second page.
        this.insertOption(typedStyle);

        //$('#styleSelect option:eq(' + typedStyle + ')').prop('selected', true);
        $('#styleSelect').val(typedStyle);
    };

    StyleEditor.prototype.insertOption = function (typedStyle) {
        var newOption = $('<option value="' + typedStyle + '">' + typedStyle + '</option>');
        for (var j = 0; j < this.styles.length; j++) {
            if (typedStyle.toLowerCase() < this.styles[j].toLowerCase()) {
                this.styles.splice(j, 0, typedStyle);
                newOption.insertBefore('#styleSelect :nth-child(' + (j + 1) + ')');
                return;
            }
        }
        $('#styleSelect').append(newOption);
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

    // This version makes an elegant HTML5 input box which, when you open the page in the system browser,
    // lets you type a style name and shows a pop-up for choosing styles when you type an incomplete
    // name or clear out the box and click.
    // I thought it was worth saving the code, but
    // - there is no visual clue that there is a list to choose from, especially if the current text doesn't match the start of any styles
    // - for some baffling reason, it doesn't work at all inside Bloom.
    //makeEditableSelect(items, marginLeft, current, id) {
    //    var tempVarName = id + 'Items';
    //    var result = '<input type="text" id = "' + id + '" value="' + current + '" style="margin-left:' + marginLeft + 'px" list="' + tempVarName + '">';
    //    result += '<datalist id="' + tempVarName + '">';
    //    for (var i = 0; i < items.length; i++) {
    //        result += '<option value="' + items[i] + '">';
    //    }
    //    result += '</datalist>';
    //    return result;
    //}
    //makeEditableSelect(items, marginLeft, current, id) {
    //    var choose = localizationManager.getText('EditTab.StyleEditor.Choose', 'Choose:');
    //    var result = '<p style="margin-left:' + marginLeft + 'px"><label for="' + id + '">' + choose + '</label><select id="' + id + '">';
    //    for (var i = 0; i < items.length; i++) {
    //        var selected: string = "";
    //        if (current == items[i]) selected = ' selected';
    //        var text = items[i];
    //        result += '<option value="' + items[i] + '"' + selected + '>' + text + '</option>';
    //    }
    //    result += '</select></p>';
    //    var orNew = localizationManager.getText('EditTab.StyleEditor.OrCreateNew', 'or create new:');
    //    result += '<p style = "margin-left:' + marginLeft + 'px" ><label for= "' + id + 'Input" > ' + orNew + ' </label >';
    //    result += '<input type="text" id = "' + id + 'Input"/>';
    //    return result;
    //}
    StyleEditor.prototype.makeEditableSelect = function (items, marginLeft, current, id) {
        var choose = localizationManager.getText('EditTab.StyleEditor.Choose', 'Choose:');
        var result = '<table style="margin-left:' + marginLeft + 'px;font-size:11px"><tr><td style="text-align:end;padding-right:5px; width:30px;white-space:nowrap"><label for="' + id + '">' + choose + '</label></td><td style="padding-bottom:5px"><select id="' + id + '">';
        for (var i = 0; i < items.length; i++) {
            var selected = "";
            if (current == items[i])
                selected = ' selected';
            var text = items[i];
            result += '<option value="' + items[i] + '"' + selected + '>' + text + '</option>';
        }
        result += '</select></td></tr>';
        var orNew = localizationManager.getText('EditTab.StyleEditor.OrCreateNew', 'or create new:');
        result += '<tr><td style="text-align:end;padding-right:5px; width:30px;white-space:nowrap"><label for= "' + id + 'Input" > ' + orNew + ' </label ></td>';
        result += '<td><input type="text" id = "' + id + 'Input"/></td></tr></table>';
        return result;
    };

    StyleEditor.prototype.changeFont = function () {
        if (this.ignoreControlChanges)
            return;
        var rule = this.getStyleRule();
        var font = $('#fontSelect').val();
        rule.style.setProperty("font-family", font, "important");
        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.changeSize = function () {
        if (this.ignoreControlChanges)
            return;
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
        if (this.ignoreControlChanges)
            return;
        var rule = this.getStyleRule();
        var lineHeight = $('#lineHeightSelect').val();
        rule.style.setProperty("line-height", lineHeight, "important");
        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.changeWordSpace = function () {
        if (this.ignoreControlChanges)
            return;
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
        if (this.ignoreControlChanges)
            return;
        var rule = this.getStyleRule();
        var borderOpt = $('#borderSelect').val();
        switch (borderOpt) {
            case 'none':
                //rule.style.setProperty("border-style", "none");
                rule.style.removeProperty("border-style");
                rule.style.removeProperty("border");
                rule.style.removeProperty("border-color");
                rule.style.removeProperty("border-radius");
                rule.style.removeProperty("padding");
                rule.style.removeProperty("background-color");
                rule.style.removeProperty("box-sizing");
                break;
            case 'black':
                rule.style.setProperty("border", "1pt solid black", "important");
                rule.style.setProperty("background-color", "transparent ", "important");
                rule.style.setProperty("border-radius", "0px", "important");
                rule.style.setProperty("padding", "10px", "important");
                rule.style.setProperty("box-sizing", "border-box", "important");
                break;
            case 'black-grey':
                rule.style.setProperty("border", "1pt solid black", "important");
                rule.style.setProperty("background-color", "LightGray ", "important");
                rule.style.setProperty("border-radius", "0px", "important");
                rule.style.setProperty("padding", "10px", "important");
                rule.style.setProperty("box-sizing", "border-box", "important");
                break;
            case 'black-round':
                rule.style.setProperty("border", "1pt solid black", "important");
                rule.style.setProperty("border-radius", "10px", "important");
                rule.style.setProperty("background-color", "transparent ", "important");
                rule.style.setProperty("padding", "10px", "important");
                rule.style.setProperty("box-sizing", "border-box", "important");
                break;
            case 'grey':
                rule.style.setProperty("border", "1pt solid Grey", "important");
                rule.style.setProperty("background-color", "transparent ", "important");
                rule.style.setProperty("border-radius", "0px", "important");
                rule.style.setProperty("padding", "10px", "important");
                rule.style.setProperty("box-sizing", "border-box", "important");
                break;
            case 'grey-round':
                rule.style.setProperty("border", "1pt solid Grey", "important");
                rule.style.setProperty("border-radius", "10px", "important");
                rule.style.setProperty("background-color", "transparent ", "important");
                rule.style.setProperty("padding", "10px", "important");
                rule.style.setProperty("box-sizing", "border-box", "important");
                break;
        }

        this.cleanupAfterStyleChange();
    };

    StyleEditor.prototype.selectStyle = function () {
        var style = $('#styleSelect').val();
        $('#styleSelectInput').val(""); // we've chosen a style from the list, so we aren't creating a new one.
        StyleEditor.SetStyleNameForElement(this.boxBeingEdited, style + "-style");
        var current = this.getFormatValues();

        // There's no point in updating the style definition as a side effect of updating the controls.
        // Doing so might well even make a real change, because the controls can only be an approximation
        // of the settings that can be achieved using raw stylesheet editing.
        this.ignoreControlChanges = true;
        $('#fontSelect').val(current.fontName);
        $('#sizeSelect').val(current.ptSize);
        $('#lineHeightSelect').val(current.lineHeight);
        $('#wordSpaceSelect').val(current.wordSpacing);
        $('#borderSelect').val(current.borderChoice);
        this.ignoreControlChanges = false;
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
        $('#formatDesc').html(this.getDescription());
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
