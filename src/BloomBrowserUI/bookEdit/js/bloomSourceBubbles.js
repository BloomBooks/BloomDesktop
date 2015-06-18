/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="bloomQtipUtils.ts" />
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
    //Sets up the (currently yellow) qtip bubbles that give you the contents of the box in the source languages
    bloomSourceBubbles.MakeSourceTextDivForGroup = function (group) {
        // Copy source texts out to their own div, where we can make a bubble with tabs out of them
        // We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too,
        var divForBubble = $(group).clone();
        $(divForBubble).removeAttr('style');
        $(divForBubble).removeClass(); //remove them all
        $(divForBubble).addClass("ui-sourceTextsForBubble");
        //make the source texts in the bubble read-only and remove any user font size adjustments
        $(divForBubble).find("textarea, div").each(function () {
            //don't want empty items in the bubble
            if (bloomSourceBubbles.hasNoText(this)) {
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
            return;
        /* removed june 12 2013 was dying with new jquery as this was Window and that had no OwnerDocument
           $(this).after(divForBubble);
         */
        var selectorOfDefaultTab = "ul>li:first-child";
        var vernacularLang = localizationManager.getVernacularLang();
        //make the li's for the source text elements in this new div, which will later move to a tabbed bubble
        // divForBubble is a single cloned bloom-translationGroup, so no need for .each() here
        var $this = $(divForBubble[0]);
        $this.prepend('<nav><ul class="editTimeOnly bloom-ui"></ul></nav>');
        var list = $this.find('ul');
        //nb: Jan 2012: we modified "jquery.easytabs.js" to target @lang attributes, rather than ids.  If that change gets lost,
        //it's just a one-line change.
        var items = $this.find("textarea, div");
        items.sort(function (a, b) {
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
        var shellEditingMode = false;
        items.each(function () {
            var iso = $(this).attr('lang');
            if (iso) {
                var languageName = localizationManager.getLanguageName(iso);
                if (!languageName)
                    languageName = iso;
                var shouldShowOnPage = (iso === vernacularLang) || $(this).hasClass('bloom-contentNational1') || $(this).hasClass('bloom-contentNational2') || $(this).hasClass('bloom-content2') || $(this).hasClass('bloom-content3');
                // in translation mode, don't include the vernacular in the tabs, because the tabs are being moved to the bubble
                if (iso !== "z" && (shellEditingMode || !shouldShowOnPage)) {
                    $(list).append('<li id="' + iso + '"><a class="sourceTextTab" href="#' + iso + '">' + languageName + '</a></li>');
                    if (iso === GetSettings().defaultSourceLanguage) {
                        selectorOfDefaultTab = "li#" + iso; //selectorOfDefaultTab="li:#"+iso; this worked in jquery 1.4
                    }
                }
            }
        });
        //now turn that new div into a set of tabs
        if ($(divForBubble).find("li").length > 0) {
            $(divForBubble).easytabs({
                animate: false,
                defaultTab: selectorOfDefaultTab
            });
        }
        else {
            $(divForBubble).remove(); //no tabs, so hide the bubble
            return;
        }
        // Make sure selected tab is in view
        var showEvents = false;
        var hideEvents = false;
        var showEventsStr;
        var hideEventsStr;
        var shouldShowAlways = true;
        if (bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles($(group))) {
            showEvents = true;
            showEventsStr = 'focusin';
            hideEvents = true;
            hideEventsStr = 'focusout';
            shouldShowAlways = false;
        }
        // turn that tab thing into a bubble, and attach it to the original div ("group")
        $(group).each(function () {
            // var targetHeight = Math.max(55, $(this).height()); // This ensures we get at least one line of the source text!
            var $this = $(this);
            $this.qtip({
                position: {
                    my: 'left top',
                    at: 'right top',
                    adjust: {
                        x: 10,
                        y: 0
                    }
                },
                content: $(divForBubble),
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
                            if (maxHeight < 50)
                                maxHeight = 50;
                            $tip.css('max-height', maxHeight);
                            $tip.addClass('passive-bubble');
                            $tip.attr('data-max-height', maxHeight);
                        }
                    }
                }
            });
            // BL-878: show the full-size tool tip when the text area has focus
            $this.find('.bloom-editable').focus(function (event) {
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
                //event.stopPropagation();
                //event.preventDefault();
            });
        });
    };
    return bloomSourceBubbles;
})();
//# sourceMappingURL=bloomSourceBubbles.js.map