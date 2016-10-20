/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../js/bloomQtipUtils.ts" />
/// <reference path="../StyleEditor/StyleEditor.ts" />
/// <reference path="../../typings/jquery.qtipSecondary.d.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />
/// <reference path="../../typings/jquery.easytabs.d.ts" />
import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
import StyleEditor from "../StyleEditor/StyleEditor";
import bloomQtipUtils from '../js/bloomQtipUtils';
import '../../lib/jquery.easytabs.js'; //load into global space

declare function GetSettings() : any; //c# injects this

export default class BloomSourceBubbles {

    //:empty is not quite enough... we don't want to show bubbles if all there is is an empty paragraph
    private static hasNoText(obj: HTMLElement): boolean {
        //if(typeof (obj) == 'HTMLTextAreaElement') {
        //    return $.trim($(obj).text()).length == 0;
        //}
        return $.trim($(obj).text()).length == 0;
    }

    // This is the method that should be called from bloomEditing to create tabbed source bubbles
    // for translation.
    // param 'group' is a .bloom-translationGroup DIV
    // optional param 'newIso' is defined when the user clicks on a language in the dropdown box
    public static ProduceSourceBubbles(group: HTMLElement, newIso?: string): void {
        var divForBubble = BloomSourceBubbles.MakeSourceTextDivForGroup(group, newIso);
        if(divForBubble == null) return;

        // Do easytabs transformation on the cloned div 'divForBubble' with the first tab selected,
        divForBubble = BloomSourceBubbles.CreateTabsFromDiv(divForBubble);
        if (divForBubble == null) return;

        // If divForBubble contains more than two languages, create a dropdown menu to contain the
        // extra possibilities. The menu will show (x), where x is the number of items in the dropdown.
        divForBubble = BloomSourceBubbles.CreateDropdownIfNecessary(divForBubble);

        // Turns the tabbed and linked div bundle into a qtip bubble attached to the bloom-translationGroup (group).
        // Also makes sure the tooltips are setup correctly.
        BloomSourceBubbles.CreateAndShowQtipBubbleFromDiv(group, divForBubble);

        BloomSourceBubbles.HideLabelsThatWouldBeUnderfoot(group);
    }

    // Cleans up a clone of the original translationGroup
    // and sets up the list items with anchors that will become the tabs to jump to linked source text
    // param 'group' is a .bloom-translationGroup DIV
    // optional param 'newIso' is defined when the user clicks on a language in the dropdown box
    // This method is only public for testing
    public static MakeSourceTextDivForGroup(group: HTMLElement, newIso?: string): JQuery {
        // Copy source texts out to their own div, where we can make a bubble with tabs out of them
        // We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too,
        var divForBubble = $(group).clone();
        divForBubble.removeAttr('style');
        divForBubble.removeClass(); //remove them all
        divForBubble.addClass("ui-sourceTextsForBubble");

        //make the source texts in the bubble read-only and remove any user font size adjustments
        divForBubble.find("textarea, div").each(function() {
            //don't want empty items in the bubble
            var $this = $(this);
            if(BloomSourceBubbles.hasNoText(this)) {
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

            var styleClass = StyleEditor.GetStyleClassFromElement(this);
            if (styleClass)
                $this.removeClass(styleClass);

            // remove any CustomPage min-height styles (they conflict with the source bubble css)
            BloomSourceBubbles.RemoveCustomPageAdditions($this);

            $this.addClass("source-text");
        });

        //don't want the vernacular or languages in use for bilingual/trilingual boxes to be shown in the bubble
        divForBubble.find("*.bloom-content1, *.bloom-content2, *.bloom-content3").each(function () {
            $(this).remove();
        });

        // BL-2734 Don't show national book title in bubble, since it's already visible on the page
        divForBubble.find('*.bloom-contentNational1').each(function () {
            if ($(this).attr('data-book') === 'bookTitle') {
                $(this).remove();
            }
        });
        // BL-3888 Don't show regional book title in bubble, since it's already visible on the title page
        divForBubble.find('*.bloom-contentNational2').each(function () {
            if ($(this).attr('data-book') === 'bookTitle') {
                $(this).remove();
            }
        });

        //in case some formatting didn't get cleaned up
        StyleEditor.CleanupElement(divForBubble);

        //if there are no languages to show in the bubble, bail out now
        if (divForBubble.find("textarea, div").length == 0)
            return null;

        var vernacularLang = theOneLocalizationManager.getVernacularLang();

        // Make the li's for the source text elements in this new div, which will later move to a tabbed bubble
        // divForBubble is a single cloned bloom-translationGroup, so no need for .each() here
        var $this = divForBubble.first();
        $this.prepend('<nav><ul class="editTimeOnly bloom-ui"></ul></nav>'); // build the tabs here

        // First, sort the divs (and/or textareas) alphabetically by language code
        var items = $this.find("textarea, div");
        items.sort(function(a, b) {
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
        items = BloomSourceBubbles.SmartOrderSourceTabs(items, newIso);

        var shellEditingMode = false;
        var list = $this.find('nav ul');
        items.each(function() {
            var iso = $(this).attr('lang');
            if (iso) {
                var languageName = theOneLocalizationManager.getLanguageName(iso);
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
    } // end MakeSourceTextDivForGroup()

    private static RemoveCustomPageAdditions(editableDiv: JQuery):void {
        var styleAttr = editableDiv.attr('style');
        if (!styleAttr) return;

        editableDiv.css("min-height", "");
    }

    // 'Smart' orders the tabs putting the latest viewed language first, followed by others in the collection
    // param 'items' is an alphabetical list of all the divs of different languages to be used as tabs
    // optional param 'newIso' is defined when the user clicks on a language in the dropdown box
    private static SmartOrderSourceTabs(items, newIso?: string):JQuery {
        // BL-2357 Do some smart ordering of source language tabs
        var settingsObject = GetSettings();
        var defaultSrcLang = settingsObject.defaultSourceLanguage;
        var destination = 0;
        if(newIso) defaultSrcLang = newIso;
        var newItems = BloomSourceBubbles.DoSafeReplaceInList(items, defaultSrcLang, destination);
        if($(newItems).attr('lang') == defaultSrcLang) { // .attr() just gets the first one
            destination++;
            items = newItems;
        }
        var language2 = settingsObject.currentCollectionLanguage2;
        var language3 = settingsObject.currentCollectionLanguage3;
        if (language2 && language2 != defaultSrcLang) {
            newItems = BloomSourceBubbles.DoSafeReplaceInList(items, language2, destination);
            if($(newItems[destination]).attr('lang') == language2) {
                destination++;
                items = newItems;
            }
        }
        if (language3 && language3 != defaultSrcLang) {
            newItems = BloomSourceBubbles.DoSafeReplaceInList(items, language3, destination);
            if($(newItems[destination]).attr('lang') == language3) {
                items = newItems;
            }
        }
        return items;
    }

    private static DoSafeReplaceInList(items:JQuery, langCode:String, position:number):JQuery {
        // if items contains a div with langCode, then try to put it at the position specified in the list
        // (unless it already occurs at an earlier position).
        var moveFrom = 0;
        var objToMove;
        var itemArray = items.toArray();
        items.each(function(idx, obj) {
            var iso = $(this).attr('lang');
            if(iso == langCode && position < idx) {
                moveFrom = idx;
                objToMove = obj;
                return false; // break out of .each()
            }
        });
        if(moveFrom > 0) {
            itemArray.splice(moveFrom, 1); // removes the objToMove from the array
            itemArray.splice(position, 0, objToMove); // puts objToMove back in at position
            items = $(itemArray);
        }
        return items;
    }

    // Turns the cloned div 'divForBubble' into a tabbed bundle with the first tab, corresponding to
    // defaultSourceLanguage, selected.
    // N.B.: Sorting the last used source language first means we no longer need to specify which tab is selected.
    private static CreateTabsFromDiv(divForBubble: JQuery): JQuery {
        //now turn that new div into a set of tabs
        if (divForBubble.find('nav li').length > 0) {
            divForBubble.easytabs({
                animate: false,
                tabs: "> nav > ul > li"
            });
        }
        else {
            divForBubble.remove();//no tabs, so hide the bubble
            return null;
        }
        return divForBubble;
    }

    // If divForBubble contains more than two languages, create a dropdown button
    // to contain the extra possibilities
    // This method is only public for testing
    public static CreateDropdownIfNecessary(divForBubble:JQuery):JQuery {
        var FIRST_SELECT_OPTION = 3;
        var tabs = divForBubble.find('nav li'); // may be li elements in the content
        if (tabs.length < FIRST_SELECT_OPTION) return divForBubble; // no change

        var dropMenu = "<li class='dropdown-menu'><div>0</div><ul class='dropdown-list'></ul></li>";
        divForBubble.find('nav ul').append(dropMenu);
        var container = divForBubble.find(".dropdown-list");
        tabs.each(function(idx) {
            if(idx < FIRST_SELECT_OPTION - 1) return true; // continue to next iteration of .each()
            var $this = $(this);
            var link = $this.find('a').clone(true); // 'true' to keep easytab click event
            var iso = link.attr('href').substring(1); // strip off hashmark
            var listItem = "<li lang='"+iso+"'></li>";
            container.append(listItem);
            container.find('li[lang="'+iso+'"]').append(link);
            $this.addClass('removeThisOne');
        });

        tabs.remove('.removeThisOne');

        container.find('li').each(function () {
            this.addEventListener("click", BloomSourceBubbles.styledSelectChangeHandler, false);
        });

        // BL-2390 Add number of extra tabs to visible part of dropdown
        divForBubble.find(".dropdown-menu div").text((tabs.length - FIRST_SELECT_OPTION + 1).toString());
        return divForBubble;
    }

    private static styledSelectChangeHandler(event) {
        var newIso = event.target.href.split('#')[1];

        // Figure out which qtip we're in and go find the associated bloom-translationGroup
        var qtip = $(event.target).closest('.qtip').attr('id');
        var group = $(document).find('.bloom-translationGroup[aria-describedby="'+qtip+'"]');

        // Redo creating the source bubbles with the selected language first
        if(group && group.length > 0) // should be
            BloomSourceBubbles.ProduceSourceBubbles(group[0], newIso);
    }

    // Turns the tabbed and linked div bundle into a qtip bubble attached to the bloom-translationGroup (group).
    // Also makes sure the tooltips are setup correctly.
    private static CreateAndShowQtipBubbleFromDiv(group: HTMLElement, divForBubble: JQuery): void {
        var showEvents = false;
        var hideEvents = false;
        var showEventsStr;
        var hideEventsStr;
        var shouldShowAlways = true;

        var $group = $(group);
        if(bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles($group)) {
            showEvents = true;
            showEventsStr = 'focusin';
            hideEvents = true;
            hideEventsStr = 'focusout';
            shouldShowAlways = false;
        }

        // turn that tab thing into a bubble, and attach it to the original div ("group")
        $group.each(function () {
            // var targetHeight = Math.max(55, $(this).height()); // This ensures we get at least one line of the source text!

            var $this: JQuery = $(this);

            $this.qtip({
                position: {
                    my: 'left top',
                    at: 'right top',
                    adjust: {
                        x: 0,
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
                    show: function(event, api) {
                        // don't need to do this if there is only one editable area
                        var $body: JQuery = $('body');
                        if ($body.find("*.bloom-translationGroup").not(".bloom-readOnlyInTranslationMode").length < 2)
                            return;

                        // BL-878: set the tool tips to not be larger than the text area so they don't overlap each other
                        var $tip = api.elements.tooltip;
                        var $div = $body.find('[aria-describedby="' + $tip.attr('id') + '"]');
                        var maxHeight = $div.height();
                        if ($tip.height() > maxHeight) {

                            // make sure to show a minimum size
                            if (maxHeight < 70) maxHeight = 70;

                            $tip.css('max-height', maxHeight);
                            $tip.addClass('passive-bubble');
                            $tip.attr('data-max-height', maxHeight);
                        }
                    }
                }
            });

            BloomSourceBubbles.SetupTooltips($this);
        });
    }

    private static SetupTooltips(editableDiv: JQuery): void
    {
        // BL-878: show the full-size tool tip when the text area has focus
        editableDiv.find('.bloom-editable').focus(function(event) {

            // reset tool tips that may be expanded
            var $body = $('body');
            $body.find('.qtip').each(function(idx, obj) {
                var $thisTip = $(obj);
                $thisTip.addClass('passive-bubble');
                var maxHeight = $thisTip.attr('data-max-height');
                if(maxHeight)
                    $thisTip.css('max-height', parseInt(maxHeight));
            });

            // show the full tip, if needed
            var tipId = (<Element>event.target.parentNode).getAttribute('aria-describedby');
            var $tip = $body.find('#' + tipId);
            $tip.removeClass('passive-bubble');
            var maxHeight = $tip.attr('data-max-height');
            if (maxHeight) {
                $tip.css('max-height', '');
            }
        });
    }

    private static HideLabelsThatWouldBeUnderfoot(groupElement: HTMLElement) {
        $(groupElement).find("label.bubble").each( (index, element) => {
            $(element).remove();
        });
    }
}