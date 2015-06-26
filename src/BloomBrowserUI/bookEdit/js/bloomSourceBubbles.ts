/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="bloomQtipUtils.ts" />
/// <reference path="../StyleEditor/StyleEditor.ts" />

interface qtipInterface extends JQuery {
    qtip(options: any): JQuery;
    qtipSecondary(options: any): JQuery;
}

interface easytabsInterface extends JQuery {
    easytabs(options: any): JQuery;
}

interface arraySort extends JQuery {
    sort(compare: (a: HTMLElement, b: HTMLElement)=>number): JQuery;
}

declare function GetStyleClassFromElement(element: HTMLElement): string;

class bloomSourceBubbles {

    //:empty is not quite enough... we don't want to show bubbles if all there is is an empty paragraph
    private static hasNoText(obj: HTMLElement): boolean {
        //if(typeof (obj) == 'HTMLTextAreaElement') {
        //    return $.trim($(obj).text()).length == 0;
        //}
        return $.trim($(obj).text()).length == 0;
    }

    //Sets up the (currently yellow) qtip bubbles that give you the contents of the box in the source languages
    // param 'group' is a .bloom-translationGroup DIV
    public static MakeSourceTextDivForGroup(group: HTMLElement): JQuery {
        // Copy source texts out to their own div, where we can make a bubble with tabs out of them
        // We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too,
        var divForBubble = $(group).clone();
        $(divForBubble).removeAttr('style');
        $(divForBubble).removeClass(); //remove them all
        $(divForBubble).addClass("ui-sourceTextsForBubble");

        //make the source texts in the bubble read-only and remove any user font size adjustments
        $(divForBubble).find("textarea, div").each(function() {
            //don't want empty items in the bubble
            if(bloomSourceBubbles.hasNoText(this)) {
                $(this).remove();
                return true; // skip to next iteration of each()
            }
            $(this).attr("readonly", "readonly");
            $(this).removeClass('bloom-editable');
            $(this).attr("contenteditable", "false");

            // don't want red in source text bubbles
            $(this).removeClass('overflow');
            $(this).removeClass('thisOverflowingParent');
            $(this).removeClass('childOverflowingThis');

            var styleClass = GetStyleClassFromElement(this);
            if (styleClass)
                $(this).removeClass(styleClass);
            $(this).addClass("source-text");
        });

        //don't want the vernacular or languages in use for bilingual/trilingual boxes to be shown in the bubble
        $(divForBubble).find("*.bloom-content1, *.bloom-content2, *.bloom-content3").each(function () {
            $(this).remove();
        });

        //in case some formatting didn't get cleaned up
        StyleEditor.CleanupElement(divForBubble);

        //if there are no languages to show in the bubble, bail out now
        if ($(divForBubble).find("textarea, div").length == 0)
            return null;

        var vernacularLang = localizationManager.getVernacularLang();

        //make the li's for the source text elements in this new div, which will later move to a tabbed bubble
        // divForBubble is a single cloned bloom-translationGroup, so no need for .each() here
        var $this = $(divForBubble[0]);
        $this.prepend('<nav><ul class="editTimeOnly bloom-ui"></ul></nav>'); // build the tabs here

        // First, sort the divs (and/or textareas) alphabetically by language code
        var items = $this.find("textarea, div");
        (<arraySort>items).sort(function(a, b) {
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

        items = bloomSourceBubbles.SmartOrderSourceTabs(items); // BL-2357

        var shellEditingMode = false;
        var list = $this.find('ul');
        items.each(function() {
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
    } // end MakeSourceTextDivForGroup()

    private static SmartOrderSourceTabs(items):JQuery {
        // BL-2357 Do some smart ordering of source language tabs
        var settingsObject = GetSettings();
        var defaultSrcLang = settingsObject.defaultSourceLanguage;
        items = bloomSourceBubbles.DoSafeReplaceInList(items, defaultSrcLang, 0);
        var language2 = settingsObject.currentCollectionLanguage2;
        var language3 = settingsObject.currentCollectionLanguage3;
        if (language2 && language2 != defaultSrcLang) {
            items = bloomSourceBubbles.DoSafeReplaceInList(items, language2, 1);
        }
        if (language3 && language3 != defaultSrcLang) {
            items = bloomSourceBubbles.DoSafeReplaceInList(items, language3, 2);
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
    // Then turns that bundle into a qtip bubble attached to 'group'.
    // Then makes sure the tooltips are setup correctly.
    // Made this public in order to test what feeds into it.
    public static TurnDivIntoTabbedBubbleWithToolTips(group: HTMLElement, divForBubble: JQuery): void {
        var $group = $(group);
        //now turn that new div into a set of tabs
        if (divForBubble.find("li").length > 0) {
            (<easytabsInterface>divForBubble).easytabs({
                animate: false,
                tabs: "> nav > ul > li"
            });
        }
        else {
            divForBubble.remove();//no tabs, so hide the bubble
            return;
        }

        var showEvents = false;
        var hideEvents = false;
        var showEventsStr;
        var hideEventsStr;
        var shouldShowAlways = true;

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

            var $this: qtipInterface = <qtipInterface>$(this);

            $this.qtip({
                position: {
                    my: 'left top',
                    at: 'right top',
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
                        height: 10
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
                            if (maxHeight < 50) maxHeight = 50;

                            $tip.css('max-height', maxHeight);
                            $tip.addClass('passive-bubble');
                            $tip.attr('data-max-height', maxHeight)
                        }
                    },
                }
            });

            bloomSourceBubbles.SetupTooltips($this);
        });
    }

    private static SetupTooltips(editableDiv: JQuery): void
    {
        // BL-878: show the full-size tool tip when the text area has focus
        editableDiv.find('.bloom-editable').focus(function(event) {

            // reset tool tips that may be expanded
            var $body = $('body');
            $body.find('.qtip[data-max-height]').each(function(idx, obj) {
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
    }
}