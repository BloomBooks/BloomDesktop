/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../js/bloomQtipUtils.ts" />
/// <reference path="../StyleEditor/StyleEditor.ts" />
var bloomSourceBubbles = (function () {
    function bloomSourceBubbles() {
    }
    //:empty is not quite enough... we don't want to show bubbles if all there is is an empty paragraph
    bloomSourceBubbles.hasNoText = function (obj) {
        //if(typeof (obj) == 'HTMLTextAreaElement') {
        //    return $.trim($(obj).text()).length == 0;
        //}
        return $.trim($(obj).text()).length == 0;
    };
    // This is the method that should be called from bloomEditing to create tabbed source bubbles
    // for translation.
    // param 'group' is a .bloom-translationGroup DIV
    // optional param 'newIso' is defined when the user clicks on a language in the dropdown box
    bloomSourceBubbles.ProduceSourceBubbles = function (group, newIso) {
        var divForBubble = bloomSourceBubbles.MakeSourceTextDivForGroup(group, newIso);
        if (divForBubble == null)
            return;
        // Do easytabs transformation on the cloned div 'divForBubble' with the first tab selected,
        divForBubble = bloomSourceBubbles.CreateTabsFromDiv(divForBubble);
        if (divForBubble == null)
            return;
        // If divForBubble contains more than two languages, create a dropdown menu to contain the
        // extra possibilities. The menu will show (x), where x is the number of items in the dropdown.
        divForBubble = bloomSourceBubbles.CreateDropdownIfNecessary(divForBubble);
        // Turns the tabbed and linked div bundle into a qtip bubble attached to the bloom-translationGroup (group).
        // Also makes sure the tooltips are setup correctly.
        bloomSourceBubbles.CreateAndShowQtipBubbleFromDiv(group, divForBubble);
    };
    // Cleans up a clone of the original translationGroup
    // and sets up the list items with anchors that will become the tabs to jump to linked source text
    // param 'group' is a .bloom-translationGroup DIV
    // optional param 'newIso' is defined when the user clicks on a language in the dropdown box
    // This method is only public for testing
    bloomSourceBubbles.MakeSourceTextDivForGroup = function (group, newIso) {
        // Copy source texts out to their own div, where we can make a bubble with tabs out of them
        // We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too,
        var divForBubble = $(group).clone();
        divForBubble.removeAttr('style');
        divForBubble.removeClass(); //remove them all
        divForBubble.addClass("ui-sourceTextsForBubble");
        //make the source texts in the bubble read-only and remove any user font size adjustments
        divForBubble.find("textarea, div").each(function () {
            //don't want empty items in the bubble
            var $this = $(this);
            if (bloomSourceBubbles.hasNoText(this)) {
                $this.remove();
                return true; // skip to next iteration of each()
            }
            $this.attr("readonly", "readonly");
            $this.removeClass('bloom-editable');
            $this.attr("contenteditable", "false");
            // don't want red in source text bubbles
            $this.removeClass('overflow');
            $this.removeClass('thisOverflowingParent');
            $this.removeClass('childOverflowingThis');
            var styleClass = GetStyleClassFromElement(this);
            if (styleClass)
                $this.removeClass(styleClass);
            // remove any CustomPage min-height styles (they conflict with the source bubble css)
            bloomSourceBubbles.RemoveCustomPageAdditions($this);
            $this.addClass("source-text");
        });
        //don't want the vernacular or languages in use for bilingual/trilingual boxes to be shown in the bubble
        divForBubble.find("*.bloom-content1, *.bloom-content2, *.bloom-content3").each(function () {
            $(this).remove();
        });
        //in case some formatting didn't get cleaned up
        StyleEditor.CleanupElement(divForBubble);
        //if there are no languages to show in the bubble, bail out now
        if (divForBubble.find("textarea, div").length == 0)
            return null;
        var vernacularLang = localizationManager.getVernacularLang();
        // Make the li's for the source text elements in this new div, which will later move to a tabbed bubble
        // divForBubble is a single cloned bloom-translationGroup, so no need for .each() here
        var $this = divForBubble.first();
        $this.prepend('<nav><ul class="editTimeOnly bloom-ui"></ul></nav>'); // build the tabs here
        // First, sort the divs (and/or textareas) alphabetically by language code
        var items = $this.find("textarea, div");
        items.sort(function (a, b) {
            //nb: Jan 2012: we modified "jquery.easytabs.js" to target @lang attributes, rather than ids.  If that change gets lost,
            //it's just a one-line change.
            var keyA = $(a).attr('lang');
            var keyB = $(b).attr('lang');
            if (keyA === vernacularLang)
                return -1;
            if (keyB === vernacularLang)
                return 1;
            if (keyA < keyB)
                return -1;
            if (keyA > keyB)
                return 1;
            return 0;
        });
        // BL-2357
        items = bloomSourceBubbles.SmartOrderSourceTabs(items, newIso);
        var shellEditingMode = false;
        var list = $this.find('ul');
        items.each(function () {
            var iso = $(this).attr('lang');
            if (iso) {
                var languageName = localizationManager.getLanguageName(iso);
                if (!languageName)
                    languageName = iso;
                var shouldShowOnPage = (iso === vernacularLang) /* could change that to 'bloom-content1' */ || $(this).hasClass('bloom-contentNational1') || $(this).hasClass('bloom-contentNational2') || $(this).hasClass('bloom-content2') || $(this).hasClass('bloom-content3');
                // in translation mode, don't include the vernacular in the tabs, because the tabs are being moved to the bubble
                if (iso !== "z" && (shellEditingMode || !shouldShowOnPage)) {
                    $(list).append('<li id="' + iso + '"><a class="sourceTextTab" href="#' + iso + '">' + languageName + '</a></li>');
                }
            }
        });
        return divForBubble;
    }; // end MakeSourceTextDivForGroup()
    bloomSourceBubbles.RemoveCustomPageAdditions = function (editableDiv) {
        var styleAttr = editableDiv.attr('style');
        if (!styleAttr)
            return;
        editableDiv.css("min-height", "");
    };
    // 'Smart' orders the tabs putting the latest viewed language first, followed by others in the collection
    // param 'items' is an alphabetical list of all the divs of different languages to be used as tabs
    // optional param 'newIso' is defined when the user clicks on a language in the dropdown box
    bloomSourceBubbles.SmartOrderSourceTabs = function (items, newIso) {
        // BL-2357 Do some smart ordering of source language tabs
        var settingsObject = GetSettings();
        var defaultSrcLang = settingsObject.defaultSourceLanguage;
        var destination = 0;
        if (newIso)
            defaultSrcLang = newIso;
        var newItems = bloomSourceBubbles.DoSafeReplaceInList(items, defaultSrcLang, destination);
        if ($(newItems).attr('lang') == defaultSrcLang) {
            destination++;
            items = newItems;
        }
        var language2 = settingsObject.currentCollectionLanguage2;
        var language3 = settingsObject.currentCollectionLanguage3;
        if (language2 && language2 != defaultSrcLang) {
            newItems = bloomSourceBubbles.DoSafeReplaceInList(items, language2, destination);
            if ($(newItems[destination]).attr('lang') == language2) {
                destination++;
                items = newItems;
            }
        }
        if (language3 && language3 != defaultSrcLang) {
            newItems = bloomSourceBubbles.DoSafeReplaceInList(items, language3, destination);
            if ($(newItems[destination]).attr('lang') == language3) {
                items = newItems;
            }
        }
        return items;
    };
    bloomSourceBubbles.DoSafeReplaceInList = function (items, langCode, position) {
        // if items contains a div with langCode, then try to put it at the position specified in the list
        // (unless it already occurs at an earlier position).
        var moveFrom = 0;
        var objToMove;
        var itemArray = items.toArray();
        items.each(function (idx, obj) {
            var iso = $(this).attr('lang');
            if (iso == langCode && position < idx) {
                moveFrom = idx;
                objToMove = obj;
                return false; // break out of .each()
            }
        });
        if (moveFrom > 0) {
            itemArray.splice(moveFrom, 1); // removes the objToMove from the array
            itemArray.splice(position, 0, objToMove); // puts objToMove back in at position
            items = $(itemArray);
        }
        return items;
    };
    // Turns the cloned div 'divForBubble' into a tabbed bundle with the first tab, corresponding to
    // defaultSourceLanguage, selected.
    // N.B.: Sorting the last used source language first means we no longer need to specify which tab is selected.
    bloomSourceBubbles.CreateTabsFromDiv = function (divForBubble) {
        //now turn that new div into a set of tabs
        if (divForBubble.find("li").length > 0) {
            divForBubble.easytabs({
                animate: false,
                tabs: "> nav > ul > li"
            });
        }
        else {
            divForBubble.remove(); //no tabs, so hide the bubble
            return null;
        }
        return divForBubble;
    };
    // If divForBubble contains more than two languages, create a dropdown button
    // to contain the extra possibilities
    // This method is only public for testing
    bloomSourceBubbles.CreateDropdownIfNecessary = function (divForBubble) {
        var FIRST_SELECT_OPTION = 3;
        var tabs = divForBubble.find("li");
        if (tabs.length < FIRST_SELECT_OPTION)
            return divForBubble; // no change
        var dropMenu = "<li class='dropdown-menu'><div>0</div><ul class='dropdown-list'></ul></li>";
        divForBubble.find("ul").append(dropMenu);
        var container = divForBubble.find(".dropdown-list");
        tabs.each(function (idx) {
            if (idx < FIRST_SELECT_OPTION - 1)
                return true; // continue to next iteration of .each()
            var $this = $(this);
            var link = $this.find('a').clone(true); // 'true' to keep easytab click event
            var iso = link.attr('href').substring(1); // strip off hashmark
            var listItem = "<li lang='" + iso + "'></li>";
            container.append(listItem);
            container.find('li[lang="' + iso + '"]').append(link);
            $this.addClass('removeThisOne');
        });
        tabs.remove('.removeThisOne');
        container.find('li').each(function () {
            this.addEventListener("click", bloomSourceBubbles.styledSelectChangeHandler, false);
        });
        // BL-2390 Add number of extra tabs to visible part of dropdown
        divForBubble.find(".dropdown-menu div").text((tabs.length - FIRST_SELECT_OPTION + 1).toString());
        return divForBubble;
    };
    bloomSourceBubbles.styledSelectChangeHandler = function (event) {
        var newIso = event.target.href.split('#')[1];
        // Figure out which qtip we're in and go find the associated bloom-translationGroup
        var qtip = $(event.target).closest('.qtip').attr('id');
        var group = $(document).find('.bloom-translationGroup[aria-describedby="' + qtip + '"]');
        // Redo creating the source bubbles with the selected language first
        if (group && group.length > 0)
            bloomSourceBubbles.ProduceSourceBubbles(group[0], newIso);
    };
    // Turns the tabbed and linked div bundle into a qtip bubble attached to the bloom-translationGroup (group).
    // Also makes sure the tooltips are setup correctly.
    bloomSourceBubbles.CreateAndShowQtipBubbleFromDiv = function (group, divForBubble) {
        var showEvents = false;
        var hideEvents = false;
        var showEventsStr;
        var hideEventsStr;
        var shouldShowAlways = true;
        var $group = $(group);
        if (bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles($group)) {
            showEvents = true;
            showEventsStr = 'focusin';
            hideEvents = true;
            hideEventsStr = 'focusout';
            shouldShowAlways = false;
        }
        // turn that tab thing into a bubble, and attach it to the original div ("group")
        $group.each(function () {
            // var targetHeight = Math.max(55, $(this).height()); // This ensures we get at least one line of the source text!
            var $this = $(this);
            $this.qtip({
                position: {
                    my: 'left top',
                    at: 'top right',
                    adjust: {
                        x: 10,
                        y: 0
                    }
                },
                content: divForBubble,
                show: {
                    event: (showEvents ? showEventsStr : showEvents),
                    ready: shouldShowAlways
                },
                style: {
                    tip: {
                        corner: true,
                        width: 10,
                        height: 10,
                        mimic: 'left center',
                        offset: 20
                    },
                    classes: 'ui-tooltip-green ui-tooltip-rounded uibloomSourceTextsBubble'
                },
                hide: (hideEvents ? hideEventsStr : hideEvents),
                events: {
                    show: function (event, api) {
                        // don't need to do this if there is only one editable area
                        var $body = $('body');
                        if ($body.find("*.bloom-translationGroup").not(".bloom-readOnlyInTranslationMode").length < 2)
                            return;
                        // BL-878: set the tool tips to not be larger than the text area so they don't overlap each other
                        var $tip = api.elements.tooltip;
                        var $div = $body.find('[aria-describedby="' + $tip.attr('id') + '"]');
                        var maxHeight = $div.height();
                        if ($tip.height() > maxHeight) {
                            // make sure to show a minimum size
                            if (maxHeight < 70)
                                maxHeight = 70;
                            $tip.css('max-height', maxHeight);
                            $tip.addClass('passive-bubble');
                            $tip.attr('data-max-height', maxHeight);
                        }
                    }
                }
            });
            bloomSourceBubbles.SetupTooltips($this);
        });
    };
    bloomSourceBubbles.SetupTooltips = function (editableDiv) {
        // BL-878: show the full-size tool tip when the text area has focus
        editableDiv.find('.bloom-editable').focus(function (event) {
            // reset tool tips that may be expanded
            var $body = $('body');
            $body.find('.qtip[data-max-height]').each(function (idx, obj) {
                var $thisTip = $(obj);
                $thisTip.css('max-height', parseInt($thisTip.attr('data-max-height')));
                $thisTip.css('z-index', 15001);
                $thisTip.addClass('passive-bubble');
            });
            // show the full tip, if needed
            var tipId = event.target.parentNode.getAttribute('aria-describedby');
            var $tip = $body.find('#' + tipId);
            var maxHeight = $tip.attr('data-max-height');
            if (maxHeight) {
                $tip.css('max-height', '');
                $tip.css('z-index', 15002);
                $tip.removeClass('passive-bubble');
            }
        });
    };
    return bloomSourceBubbles;
})();
//# sourceMappingURL=bloomSourceBubbles.js.map