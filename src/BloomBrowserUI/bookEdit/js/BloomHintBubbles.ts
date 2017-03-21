/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="collectionSettings.d.ts" />
/// <reference path="bloomQtipUtils.ts" />
/// <reference path="../../typings/jquery.qtipSecondary.d.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />
import axios = require('axios');
import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
import bloomQtipUtils from './bloomQtipUtils';

declare function GetSettings(): any; //c# injects this

export default class BloomHintBubbles {

    // Add (yellow) hint bubbles from (usually) label.bubble elements
    public static addHintBubbles(container: HTMLElement, sourceBubbleDivs: Array<Element>): void {
        //Handle <label>-defined hint bubbles on mono fields, that is divs that aren't in the context of a
        //bloom-translationGroup (those should have a single <label> for the whole group).
        //Notice that the <label> inside an editable div is in a precarious position, it could get
        //edited away by the user. So we are moving the contents into a data-hint attribute on the field.
        //Yes, it could have been placed there in the 1st place, but the <label> approach is highly readable,
        //so it is preferred when making new templates by hand.
        $(container).find(".bloom-editable:visible label.bubble").each(function () {
            var labelElement = $(this);
            var whatToSay = labelElement.text();
            if (!whatToSay)
                return;

            var enclosingEditableDiv = labelElement.parent();
            enclosingEditableDiv.attr('data-hint', labelElement.text());
            labelElement.remove();

            //attach the bubble, this editable only, then remove it
            BloomHintBubbles.MakeHelpBubble($(enclosingEditableDiv), labelElement);
        });

        // Having a <label class='bubble'> inside a div.bloom-translationGroup gives a hint bubble outside each of
        // the fields, with some template-filling and localization for each.
        // Note that in Version 1.0, we didn't have this <label> ability but we had @data-hint.
        // Using <label> instead of the attribute makes the html much easier to read, write, and add additional
        // behaviors through classes
        axios.get('/bloom/bubbleLanguages').then(result => {
            let preferredLangs: Array<string> = (<any>result.data).langs;
            $(container).find('.bloom-translationGroup').each((i, group) => {
                var groupElement = $(group);
                // if the group was given a source bubble, don't add another.
                // It's tempting here to try to detect directly whether it already has some kind of bubble.
                // This is difficult for a couple of reasons. First, we unfortunately save in the file the
                // qtip attributes that get added like aria-describedby='qtip-0' and has-qtip='true'.
                // Because of that, I tried checking to see whether the document really has a div whose ID
                // matches the aria-described by. But that failed, except when debugging; I infer that
                // there's some delay between the call that starts the process of adding the qtip and when
                // it actually comes into existence. Even apart from this, I'm not sure that a saved
                // aria-described by couldn't accidentally match a qtip created for another div. So
                // it's more reliable to have the source bubbles code figure out exactly what divs
                // it puts bubbles on.
                if (sourceBubbleDivs.indexOf(group) >= 0) {
                    return;
                }
                var labelElement = groupElement.find('label.bubble'); // may be more than one
                var whatToSay = labelElement.text();
                if (!whatToSay) {
                    return;
                }

                if (this.wantHelpBubbleOnGroup(groupElement)) {
                    // attach the bubble to the whole group...otherwise it would be oddly
                    // duplicated on all of them
                    BloomHintBubbles.MakeHelpBubble(groupElement, labelElement, preferredLangs);
                } else {
                    //attach the bubble, separately, to every visible field inside the group
                    groupElement.find("div.bloom-editable:visible").each(function () {
                        BloomHintBubbles.MakeHelpBubble($(this), labelElement, preferredLangs);
                    });
                }
            });
        });

        $(container).find("*.bloom-imageContainer > label.bubble").each(function () {
            var labelElement = $(this);
            var imageContainer = $(this).parent();
            var whatToSay = labelElement.text();
            if (!whatToSay)
                return;

            BloomHintBubbles.MakeHelpBubble(imageContainer, labelElement);
        });

        //This is the "low-level" way to get a hint bubble, cramming it all into a data-hint attribute.
        //It is used by the "high-level" way in the monolingual case where we don't have a bloom-translationGroup,
        //and need a place to preserve the contents of the <label>, which is in danger of being edited away.
        $(container).find("*[data-hint]").each(function () {
            var whatToSay = $(this).attr("data-hint");//don't use .data(), as that will trip over any } in the hint and try to interpret it as json
            if (!whatToSay)
                return;

            if (whatToSay.startsWith("*")) {
                whatToSay = whatToSay.substring(1, 1000);
            }

            if (whatToSay.length == 0 || $(this).css('display') == 'none')
                return;

            BloomHintBubbles.MakeHelpBubble($(this), $(this));
        });
    }

    static wantHelpBubbleOnGroup(groupElement: JQuery) {
        return groupElement.attr('data-default-languages').toLowerCase() === 'auto' && !groupElement.hasClass("bloom-showHintOnEach");
    }

    // Update placement and content of tooltips on a group and/or all its children. This is used for user-defined hint
    // bubbles and doesn't handle all the options of the full routine.
    public static updateQtipPlacement(groupElement: JQuery, whatToSaySource: string) {
        groupElement.qtip('destroy');
        let children = groupElement.find("div.bloom-editable:visible");
        children.qtip('destroy');
        if (!whatToSaySource) {
            return;
        }
        if (this.wantHelpBubbleOnGroup(groupElement)) {
            let whatToSay = theOneLocalizationManager.getLocalizedHint(whatToSaySource, groupElement);
            this.makeHintBubbleCore(groupElement, whatToSay, !whatToSay.startsWith("*")); // should we support * here?
        }
        else {
            children.each((i, target) => {
                let whatToSay = theOneLocalizationManager.getLocalizedHint(whatToSaySource, $(target));
                this.makeHintBubbleCore($(target), whatToSay, !whatToSay.startsWith("*")); // should we support * here?
            })
        }
    }

    //show those bubbles if the item is empty, or if it's not empty, then if it is in focus OR the mouse is over the item
    private static MakeHelpBubble(targetElement: JQuery, elementWithBubbleAttributes: JQuery, preferredLangs?: Array<string>) {
        var target = $(targetElement);
        var source = $(elementWithBubbleAttributes);

        if (target.css('display') === 'none')
            return; //don't put tips if they can't see it.

        if (target.css('border-bottom-color') === 'transparent')
            return; //don't put tips if they can't edit it. That's just confusing

        // Anybody know why this was here!? BL-1125 complains about this very thing.
        //if (target.hasClass('coverBottomBookTopic'))
        //    pos.adjust = { y: -20 };

        //temporarily disabling this; the problem is that its more natural to put the hint on enclosing 'translationgroup' element, but those elements are *never* empty.
        //maybe we could have this logic, but change this logic so that for all items within a translation group, they get their a hint from a parent, and then use this isempty logic
        //at the moment, the logic is all around whoever has the data-hint
        //var shouldShowAlways = $(this).is(':empty'); //if it was empty when we drew the page, keep the tooltip there
        var shouldShowAlways = true;

        // get the default text/stringId
        var doNotLocalize = false;
        var whatToSay = target.attr('data-hint');
        if (!whatToSay) whatToSay = source.attr('data-hint');
        if (!whatToSay) { // look in the content of one or more sources
            if (!preferredLangs) {
                preferredLangs = ['en', 'fr']; // just for safety; any caller that cares should supply a list.
            }
            var bestSourceIndex = 0; // use first source if no langs match or there is only one
            var bestLangIndex = preferredLangs.length; // pretend the best lang we found is beyond end
            if (source.length > 1) {
                for (var i = 0; i < source.length; i++) {
                    var item = source.eq(i);
                    var lang = item.attr('lang');
                    if (!lang) {
                        continue;
                    }
                    // Found at least one source with a lang attr. Assume any localization of this
                    // bubble is embedded in the document, and don't look in Bloom resources.
                    doNotLocalize = true;
                    var index = preferredLangs.indexOf(lang);
                    if (index === -1) {
                        index = preferredLangs.length;
                    }
                    if (index < bestLangIndex) { // best yet
                        bestSourceIndex = i;
                        bestLangIndex = index;
                    }
                }
            }
            whatToSay = source.eq(bestSourceIndex).text();
        }

        // no empty bubbles
        if (!whatToSay) return;

        // determine onFocusOnly
        var onFocusOnly = whatToSay.startsWith('*');
        onFocusOnly = onFocusOnly || source.hasClass('bloom-showOnlyWhenTargetHasFocus') || bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles(target);

        // get the localized string
        if (doNotLocalize) {
            // still need to substitute {lang} if any
            whatToSay = theOneLocalizationManager.insertLangIntoHint(whatToSay, target);
        }
        else {
            if (whatToSay.startsWith('*')) whatToSay = whatToSay.substr(1);
            whatToSay = theOneLocalizationManager.getLocalizedHint(whatToSay, target);
        }

        var functionCall = source.data("functiononhintclick");
        if (functionCall) {
            if (functionCall === 'bookMetadataEditor' && !BloomHintBubbles.canChangeBookLicense())
                return;
            shouldShowAlways = true;

            if (functionCall.indexOf('(') > 0)
                functionCall = 'javascript:' + functionCall + ';';

            whatToSay = "<a href='" + functionCall + "'>" + whatToSay + "</a>";
        }
        // Handle a second line in the bubble which links to something like a javascript function
        var linkText = source.attr('data-link-text');
        var linkTarget = source.attr('data-link-target');
        if (linkText && linkTarget) {
            linkText = theOneLocalizationManager.getLocalizedHint(linkText, target);
            if (linkTarget.indexOf('(') > 0)
                linkTarget = 'javascript:' + linkTarget + ';';
            whatToSay = whatToSay + "<br><a href='" + linkTarget + "'>" + linkText + "</a>";
        }
        if (onFocusOnly) {
            shouldShowAlways = false;
        }
        this.makeHintBubbleCore(target, whatToSay, shouldShowAlways);
    }

    private static makeHintBubbleCore(target: JQuery, whatToSay: string, shouldShowAlways: boolean) {

        var pos = {
            at: 'right center',
            my: 'left center',
            viewport: $(window),
            adjust: { method: 'none' }
        };

        var theClasses = 'ui-tooltip-shadow ui-tooltip-plain';
        var hideEvents = shouldShowAlways ? false : 'focusout mouseleave';

        target.qtip({
            content: whatToSay,
            position: pos,
            show: {
                event: 'focusin mouseenter',
                ready: shouldShowAlways //would rather have this kind of dynamic thing, but it isn't right: function(){$(this).is(':empty')}//
            }
            , hide: {
                event: hideEvents
            },
            style: {
                classes: theClasses
            }
        });
    }

    private static canChangeBookLicense(): boolean {
        // First, need to look in .bloomCollection file for <IsSourceCollection> value
        // if 'true', return true.
        if (GetSettings().isSourceCollection)
            return true;

        // meta[@name='lockedDownAsShell' and @content='true'], if exists, return false
        var lockedAsShell = $(document).find('meta[name="lockedDownAsShell"]');
        if (lockedAsShell.length > 0 && lockedAsShell.attr('content').toLowerCase() == 'true')
            return false;
        // meta[@name='canChangeLicense'] and @content='false'], if exists, return false
        var canChange = $(document).find('meta[name="canChangeLicense"]');
        if (canChange.length > 0 && canChange.attr('content').toLowerCase() == 'false')
            return false;

        // Otherwise return true
        return true;
    }
}
