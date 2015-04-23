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
        var divForBubble = $(group).clone();
        $(divForBubble).removeAttr('style');
        //make the source texts in the bubble read-only and remove any user font size adjustments
        $(divForBubble).find("textarea, div").each(function () {
            $(this).attr("readonly", "readonly");
            $(this).removeClass('bloom-editable');
            $(this).removeClass('overflow'); // don't want red in source text bubbles
            $(this).attr("contenteditable", "false");
            var styleClass = GetStyleClassFromElement(this);
            if (styleClass)
                $(this).removeClass(styleClass);
            $(this).addClass("source-text");
        });
        var vernacularLang = localizationManager.getVernacularLang();
        $(divForBubble).removeClass(); //remove them all
        $(divForBubble).addClass("ui-sourceTextsForBubble");
        //don't want empty items in the bubble
        $(divForBubble).find("textarea, div").each(function () {
            if (bloomSourceBubbles.hasNoText(this)) {
                $(this).remove();
            }
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
        /* removed june 12 2013 was dying with new jquery as this was Window and that had no OwnerDocument    $(this).after(divForBubble);*/
        var selectorOfDefaultTab = "li:first-child";
        //make the li's for the source text elements in this new div, which will later move to a tabbed bubble
        $(divForBubble).each(function () {
            $(this).prepend('<ul class="editTimeOnly bloom-ui"></ul>');
            var list = $(this).find('ul');
            //nb: Jan 2012: we modified "jquery.easytabs.js" to target @lang attributes, rather than ids.  If that change gets lost,
            //it's just a one-line change.
            var items = $(this).find("textarea, div");
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
        });
        //now turn that new div into a set of tabs
        // Review: as of 9 May 2014 the tab links have turned into bulleted links
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
            $(this).qtip({
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
                hide: (hideEvents ? hideEventsStr : hideEvents)
            });
        });
    };
    return bloomSourceBubbles;
})();
//# sourceMappingURL=bloomSourceBubbles.js.map