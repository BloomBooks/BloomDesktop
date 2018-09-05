/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="collectionSettings.d.ts" />
/// <reference path="bloomQtipUtils.ts" />
/// <reference path="../../typings/jquery.qtipSecondary.d.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />
import { BloomApi } from "../../utils/bloomApi";

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { IsPageXMatter } from "../js/bloomEditing";
import bloomQtipUtils from "./bloomQtipUtils";

declare function GetSettings(): any; //c# injects this

export default class BloomHintBubbles {
    // Add (yellow) hint bubbles from (usually) label.bubble elements.
    // If adding them to divs that we have already made source bubbles for (listed in divsThatHaveSourceBubbles),
    // then instead of making a new hint bubble make a new tab (parallel to languages) in the source bubble
    // at the corresponding index in contentOfBubbleDivs.
    public static addHintBubbles(
        container: HTMLElement,
        divsThatHaveSourceBubbles: Array<Element>,
        contentOfBubbleDivs: Array<Element>
    ): void {
        //Handle <label>-defined hint bubbles on mono fields, that is divs that aren't in the context of a
        //bloom-translationGroup (those should have a single <label> for the whole group).
        //Notice that the <label> inside an editable div is in a precarious position, it could get
        //edited away by the user. So we are moving the contents into a data-hint attribute on the field.
        //Yes, it could have been placed there in the 1st place, but the <label> approach is highly readable,
        //so it is preferred when making new templates by hand.
        $(container)
            .find(".bloom-editable:visible label.bubble")
            .each(function() {
                var labelElement = $(this);
                var whatToSay = labelElement.text();
                if (!whatToSay) return;

                var enclosingEditableDiv = labelElement.parent();
                enclosingEditableDiv.attr("data-hint", labelElement.text());
                labelElement.remove();

                //attach the bubble, this editable only, then remove it
                // since it's not in a translation group we don't have to worry about the parent group having a source bubble.
                BloomHintBubbles.MakeHelpBubble(
                    $(enclosingEditableDiv),
                    labelElement
                );
            });

        // Having a <label class='bubble'> inside a div.bloom-translationGroup gives a hint bubble outside each of
        // the fields, with some template-filling and localization for each.
        // Note that in Version 1.0, we didn't have this <label> ability but we had @data-hint.
        // Using <label> instead of the attribute makes the html much easier to read, write, and add additional
        // behaviors through classes
        // the addBack allows for the option that container itself is a translation group, which can happen
        // when reconstructing source bubbles after the user selects in a pull-down.
        $(container)
            .find(".bloom-translationGroup")
            .addBack(".bloom-translationGroup")
            .each((i, group) => {
                var groupElement = $(group);
                var labelElement = groupElement.find("label.bubble"); // may be more than one
                var whatToSay = labelElement.text();
                if (!whatToSay) {
                    return;
                }

                if (this.wantHelpBubbleOnGroup(groupElement)) {
                    // attach the bubble to the whole group...otherwise it would be oddly
                    // duplicated on all of them
                    BloomHintBubbles.MakeHelpBubbleOrAddToSource(
                        groupElement,
                        labelElement,
                        divsThatHaveSourceBubbles,
                        contentOfBubbleDivs
                    );
                } else {
                    //attach the bubble, separately, to every visible field inside the group
                    groupElement
                        .find("div.bloom-editable:visible")
                        .each((i, elt) => {
                            BloomHintBubbles.MakeHelpBubbleOrAddToSource(
                                $(elt),
                                labelElement,
                                divsThatHaveSourceBubbles,
                                contentOfBubbleDivs
                            );
                        });
                }
            });

        $(container)
            .find("*.bloom-imageContainer > label.bubble")
            .each(function() {
                var labelElement = $(this);
                var imageContainer = $(this).parent();
                var whatToSay = labelElement.text();
                if (!whatToSay) return;
                // These can't have source bubbles, no need to look for that.
                BloomHintBubbles.MakeHelpBubble(imageContainer, labelElement);
            });

        //This is the "low-level" way to get a hint bubble, cramming it all into a data-hint attribute.
        //It is used by the "high-level" way in the monolingual case where we don't have a bloom-translationGroup,
        //and need a place to preserve the contents of the <label>, which is in danger of being edited away.
        $(container)
            .find("*[data-hint]")
            .each(function() {
                var whatToSay = $(this).attr("data-hint"); //don't use .data(), as that will trip over any } in the hint and try to interpret it as json
                if (!whatToSay) return;

                if (whatToSay.startsWith("*")) {
                    whatToSay = whatToSay.substring(1, 1000);
                }

                if (whatToSay.length == 0 || $(this).css("display") == "none")
                    return;

                BloomHintBubbles.MakeHelpBubbleOrAddToSource(
                    $(this),
                    $(this),
                    divsThatHaveSourceBubbles,
                    contentOfBubbleDivs
                );
            });
    }

    public static InsertHintIntoBubbleDiv(
        bubbleDiv: JQuery,
        elementThatHasSourceBubble: JQuery,
        elementWithBubbleAttributes: JQuery
    ) {
        var headers = bubbleDiv.find("ul");

        // This is a preliminary version of the content, since we don't yet have the right list of
        // preferred languages. We'll fix it later. It needs to give us a language-list-independent
        // idea of whether the hint is empty, however.
        var whatToSay = this.getHintContent(
            elementThatHasSourceBubble,
            elementWithBubbleAttributes
        );
        if (whatToSay != null && whatToSay.startsWith("*"))
            whatToSay = whatToSay.substr(1);
        if (!whatToSay) return; // just forget adding a hint if there's no text.
        // Don't use the corresponding svg from artwork here. Somehow it causes about a 4 second delay (on a fast workstation)
        headers.prepend(
            "<li id='hint'><a class='sourceTextTab' href='#hint'><img src='/bloom/images/information-i.png'/></a></li>"
        );
        var nav = $(headers.parent());
        whatToSay =
            whatToSay +
            this.getPossibleHyperlink(
                elementWithBubbleAttributes,
                elementThatHasSourceBubble,
                whatToSay
            );
        // This is bizarre. We modified "jquery.easytabs.js" to target @lang attributes, rather than ids.
        // This allows us to have multiple source bubbles each with the same languages, whereas
        // it would be invalid to have duplicate ids. Here, to make the inserted div the tab that
        // shows for the tab with href='#hint', we have to pretend that is is in a language
        // called hint. See comments in BloomSourceBubbles.MakeSourceTextDivForGroup().
        var content = $(
            "<div lang='hint' class='sourceText'><p>" + whatToSay + "</p></div>"
        );
        nav.after(content);
        // whatToSay may not actually be what we want. To make it what we want, we need the real list of
        // preferred languages. So we will update it after we get that.
        // We can't wait until we have the language list to insert the main hint element into the source bubble,
        // because doing that causes the manipulations that easytabs does to the results of this method
        // to skip the hint tab. (At least, when you're not stepping through the code.)
        BloomApi.get("bubbleLanguages", result => {
            let preferredLangs: Array<string> = (<any>result.data).langs;
            whatToSay = this.getHintContent(
                elementThatHasSourceBubble,
                elementWithBubbleAttributes,
                preferredLangs
            );
            if (whatToSay != null && whatToSay.startsWith("*"))
                whatToSay = whatToSay.substr(1);
            if (!whatToSay) return;
            var hyperlink = this.getPossibleHyperlink(
                elementWithBubbleAttributes,
                elementThatHasSourceBubble,
                whatToSay
            );
            content.find("p").text(whatToSay);
            if (hyperlink.length > 0) content.find("p").append(hyperlink);
        });
    }

    private static wantHelpBubbleOnGroup(groupElement: JQuery) {
        // For xMatter, we always want to show a hint for each field.
        return (
            !IsPageXMatter(groupElement) &&
            // Otherwise, show a hint for each field only if this class requests it.
            !groupElement.hasClass("bloom-showHintOnEach")
        );
    }

    // Update placement and content of tooltips on a group and/or all its children. This is used for user-defined hint
    // bubbles and doesn't handle all the options of the full routine.
    public static updateQtipPlacement(
        groupElement: JQuery,
        whatToSaySource: string
    ) {
        groupElement.qtip("destroy");
        let children = groupElement.find("div.bloom-editable:visible");
        children.qtip("destroy");
        if (!whatToSaySource) {
            return;
        }
        if (this.wantHelpBubbleOnGroup(groupElement)) {
            let whatToSay = theOneLocalizationManager.insertLangIntoHint(
                whatToSaySource,
                groupElement
            );
            this.makeHintBubbleCore(
                groupElement,
                whatToSay,
                !whatToSay.startsWith("*")
            ); // should we support * here?
        } else {
            children.each((i, target) => {
                let whatToSay = theOneLocalizationManager.insertLangIntoHint(
                    whatToSaySource,
                    $(target)
                );
                this.makeHintBubbleCore(
                    $(target),
                    whatToSay,
                    !whatToSay.startsWith("*")
                ); // should we support * here?
            });
        }
    }

    // By default, add a help/hint bubble to the targetElement.
    // If the target element (or a close relative that occupies roughly the same space) was given a
    // source bubble, don't add a hint one. Instead, add the hint to the source bubble.
    // Elements that have source bubbles are passed as a list, and in another list, the
    // corresponding bubble content divs to which the hints should be added.
    // It's tempting here to try to detect directly whether a div already has some kind of bubble.
    // This is difficult for a couple of reasons. First, we unfortunately save in the file the
    // qtip attributes that get added like aria-describedby='qtip-0' and has-qtip='true'.
    // Because of that, I tried checking to see whether the document really has a div whose ID
    // matches the aria-described by. But that failed, except when debugging; I infer that
    // there's some delay between the call that starts the process of adding the qtip and when
    // it actually comes into existence. Even apart from this, I'm not sure that a saved
    // aria-described by couldn't accidentally match a qtip created for another div. So
    // it's more reliable to have the source bubbles code figure out exactly what divs
    // it puts bubbles on and pass that in as a list.
    private static MakeHelpBubbleOrAddToSource(
        targetElement: JQuery,
        elementWithBubbleAttributes: JQuery,
        divsThatHaveSourceBubbles: Array<Element>,
        contentOfBubbleDivs: Array<Element>
    ) {
        // If the element we want to put a hint on IS one of the groups that has source bubbles, add it to that
        // group's source bubble.
        var index = divsThatHaveSourceBubbles.indexOf(targetElement.get(0));
        if (index >= 0) {
            this.InsertHintIntoBubbleDiv(
                $(contentOfBubbleDivs[index]),
                targetElement,
                elementWithBubbleAttributes
            );
            return;
        }
        for (var i = 0; i < divsThatHaveSourceBubbles.length; i++) {
            // If the element we want to put a hint on is a PARENT of one of the groups that has source bubbles,
            // add it to that group's source bubble
            // hints are sometimes put on a parent div (e.g., creditsRow on bottom of cover page)
            if (
                divsThatHaveSourceBubbles[i].parentNode == targetElement.get(0)
            ) {
                this.InsertHintIntoBubbleDiv(
                    $(contentOfBubbleDivs[i]),
                    targetElement,
                    elementWithBubbleAttributes
                );
                return;
            }
            // If the element we want to put a hint on is a CHILD of one of the groups that has source bubbles,
            // we'll put the hint for the first visible child into the source bubble and leave the others.
            // This corresponds to a group with "Show the hint bubble on each language field in the group."
            // The first, vernacular, field in the group has the source bubble and gets the hint for the
            // vernacular bloom-editable div inserted into it. The others don't have source bubbles
            // and get their normal hints.
            if (
                divsThatHaveSourceBubbles[i] ==
                targetElement.get(0).parentElement
            ) {
                // Is it the first visible?
                var firstVisible = $(divsThatHaveSourceBubbles[i])
                    .find(":visible")
                    .get(0);
                if (firstVisible === targetElement.get(0)) {
                    this.InsertHintIntoBubbleDiv(
                        $(contentOfBubbleDivs[i]),
                        targetElement,
                        elementWithBubbleAttributes
                    );
                    return;
                } else {
                    this.MakeHelpBubble(
                        targetElement,
                        elementWithBubbleAttributes
                    );
                    return;
                }
            }
        }
        // And if targetElement is not related in any of these ways to any of the divs that have source bubbles,
        // go ahead and give it its own help/hint bubble.
        BloomApi.get("bubbleLanguages", result => {
            const orderedLangsForBubble: Array<string> = (<any>result.data)
                .langs;
            this.MakeHelpBubble(
                targetElement,
                elementWithBubbleAttributes,
                orderedLangsForBubble
            );
        });
    }

    private static getHintContent(
        elementToAttachBubbleTo: JQuery,
        elementWithBubbleAttributes: JQuery,
        preferredLangs?: Array<string>
    ): string | null {
        var doNotLocalize = false;
        var whatToSay = elementToAttachBubbleTo.attr("data-hint");
        if (!whatToSay)
            whatToSay = elementWithBubbleAttributes.attr("data-hint");
        if (!whatToSay) {
            // look in the content of one or more sources
            if (!preferredLangs) {
                preferredLangs = ["en", "fr"]; // just for safety; any caller that cares should supply a list.
            }
            var bestSourceIndex = 0; // use first source if no langs match or there is only one
            var bestLangIndex = preferredLangs.length; // pretend the best lang we found is beyond end
            for (var i = 0; i < elementWithBubbleAttributes.length; i++) {
                var item = elementWithBubbleAttributes.eq(i);
                var lang = item.attr("lang");
                if (!lang) {
                    continue;
                }
                if (!elementWithBubbleAttributes.text()) {
                    // We'd prefer a non-empty hint in a less desirable language to an empty one in a more desirable one.
                    // This is also important because we want this routine to return an empty string only
                    // if NO hint is available in ANY language. This matters because the routine may be used with an incorrect
                    // list of languages to find out whether we actually have a hint.
                    continue;
                }
                // Found at least one source with a lang attr. Assume any localization of this
                // bubble is embedded in the document, and don't look in Bloom resources.
                // (Sources with lang attrs come from end users creating templates with hint bubbles.)
                doNotLocalize = true;
                var index = preferredLangs.indexOf(lang);
                if (index === -1) {
                    index = preferredLangs.length;
                }
                if (index < bestLangIndex) {
                    // best yet
                    bestSourceIndex = i;
                    bestLangIndex = index;
                }
            }
            whatToSay = elementWithBubbleAttributes.eq(bestSourceIndex).text();
        }

        // no empty bubbles
        if (!whatToSay) return null;

        // get the localized string
        if (doNotLocalize) {
            // still need to substitute {lang} if any
            whatToSay = theOneLocalizationManager.insertLangIntoHint(
                whatToSay,
                elementToAttachBubbleTo
            );
        } else {
            whatToSay = theOneLocalizationManager.getLocalizedHint(
                whatToSay,
                elementToAttachBubbleTo
            );
        }
        return whatToSay;
    }

    // Make a help bubble for an element we have determined doesn't have a source bubble we should add it to.
    //show those bubbles if the item is empty, or if it's not empty, then if it is in focus OR the mouse is over the item
    private static MakeHelpBubble(
        targetElement: JQuery,
        elementWithBubbleAttributes: JQuery,
        preferredLangs?: Array<string>
    ) {
        var target = $(targetElement);
        var source = $(elementWithBubbleAttributes);

        if (target.css("display") === "none") return; //don't put tips if they can't see it.

        if (target.css("border-bottom-color") === "transparent") return; //don't put tips if they can't edit it. That's just confusing

        // Anybody know why this was here!? BL-1125 complains about this very thing.
        //if (target.hasClass('coverBottomBookTopic'))
        //    pos.adjust = { y: -20 };

        //temporarily disabling this; the problem is that its more natural to put the hint on enclosing 'translationgroup' element, but those elements are *never* empty.
        //maybe we could have this logic, but change this logic so that for all items within a translation group, they get their a hint from a parent, and then use this isempty logic
        //at the moment, the logic is all around whoever has the data-hint
        //var shouldShowAlways = $(this).is(':empty'); //if it was empty when we drew the page, keep the tooltip there
        var shouldShowAlways = true;

        // get the default text/stringId
        var whatToSay = this.getHintContent(target, source, preferredLangs);

        // no empty bubbles
        if (!whatToSay) return;

        // determine onFocusOnly
        var onFocusOnly = whatToSay.startsWith("*");
        // We seem to need a delay to get a reliable result from mightCauseHorizontallyOverlappingBubbles(); see comment there.
        setTimeout(() => {
            onFocusOnly =
                onFocusOnly ||
                source.hasClass("bloom-showOnlyWhenTargetHasFocus") ||
                bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles(target);

            if (whatToSay != null && whatToSay.startsWith("*"))
                whatToSay = whatToSay.substr(1);
            if (!whatToSay) return; // no empty bubbles
            var functionCall = source.data("functiononhintclick");
            if (functionCall) {
                if (
                    functionCall === "bookMetadataEditor" &&
                    !BloomHintBubbles.canChangeBookLicense()
                )
                    return;
                shouldShowAlways = true;

                if (functionCall.indexOf("(") > 0)
                    functionCall = "javascript:" + functionCall + ";";

                whatToSay =
                    "<a href='" + functionCall + "'>" + whatToSay + "</a>";
            }
            whatToSay =
                whatToSay +
                this.getPossibleHyperlink(source, target, whatToSay);
            if (onFocusOnly) {
                shouldShowAlways = false;
            }
            this.makeHintBubbleCore(target, whatToSay, shouldShowAlways);
        }, bloomQtipUtils.horizontalOverlappingBubblesDelay);
    }

    // Handle a second line in the bubble which links to something like a javascript function
    private static getPossibleHyperlink(
        bubbleSource: JQuery,
        target: JQuery,
        whatToSay: string
    ): string {
        var linkText = bubbleSource.attr("data-link-text");
        var linkTarget = bubbleSource.attr("data-link-target");
        if (linkText && linkTarget) {
            linkText = theOneLocalizationManager.getLocalizedHint(
                linkText,
                target
            );
            if (linkTarget.indexOf("(") > 0)
                linkTarget = "javascript:" + linkTarget + ";";
            return "<br><a href='" + linkTarget + "'>" + linkText + "</a>";
        }
        return "";
    }

    private static makeHintBubbleCore(
        target: JQuery,
        whatToSay: string,
        shouldShowAlways: boolean
    ) {
        var pos = {
            at: "right center",
            my: "left center",
            viewport: $(window),
            adjust: { method: "none" },
            container: bloomQtipUtils.qtipZoomContainer()
        };

        var theClasses = "ui-tooltip-shadow ui-tooltip-plain";
        var hideEvents = shouldShowAlways ? false : "focusout mouseleave";

        target.qtip({
            content: whatToSay,
            position: pos,
            show: {
                event: "focusin mouseenter",
                ready: shouldShowAlways //would rather have this kind of dynamic thing, but it isn't right: function(){$(this).is(':empty')}//
            },
            hide: {
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
        if (GetSettings().isSourceCollection) return true;

        // meta[@name='lockedDownAsShell' and @content='true'], if exists, return false
        var lockedAsShell = $(document).find('meta[name="lockedDownAsShell"]');
        if (
            lockedAsShell.length > 0 &&
            lockedAsShell.attr("content").toLowerCase() == "true"
        )
            return false;
        // meta[@name='canChangeLicense'] and @content='false'], if exists, return false
        var canChange = $(document).find('meta[name="canChangeLicense"]');
        if (
            canChange.length > 0 &&
            canChange.attr("content").toLowerCase() == "false"
        )
            return false;

        // Otherwise return true
        return true;
    }
}
