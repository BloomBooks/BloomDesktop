/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../js/bloomQtipUtils.ts" />
/// <reference path="../StyleEditor/StyleEditor.ts" />
/// <reference path="../../typings/jquery.qtipSecondary.d.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />
/// <reference path="../../typings/jquery.easytabs.d.ts" />
// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="../js/collectionSettings.d.ts"/>
import React = require("react");
import ReactDOM = require("react-dom");
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import StyleEditor from "../StyleEditor/StyleEditor";
import bloomQtipUtils from "../js/bloomQtipUtils";
import "../../lib/jquery.easytabs.js"; //load into global space
import BloomHintBubbles from "../js/BloomHintBubbles";
import { postJson, postString } from "../../utils/bloomApi";
import CopyContentButton from "../../react_components/CopyContentButton";

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
    // optional param 'newLangTag' is defined when the user clicks on a language in the dropdown box
    // Returns the source bubble if it made one.
    public static ProduceSourceBubbles(
        group: HTMLElement,
        newLangTag?: string
    ): JQuery {
        return BloomSourceBubbles.MakeSourceTextDivForGroup(group, newLangTag);
    }

    public static MakeSourceBubblesIntoQtips(
        elementThatHasBubble: HTMLElement,
        contentsOfBubble: JQuery,
        selectLangTag?: string,
        forceShowAlwaysOnBody?: boolean
    ) {
        // Do easytabs transformation on the cloned div 'divForBubble' with the first tab selected,
        let divForBubble = BloomSourceBubbles.CreateTabsFromDiv(
            contentsOfBubble,
            selectLangTag
        );
        if (divForBubble.length === 0) return;

        // If divForBubble contains more than two languages, create a dropdown menu to contain the
        // extra possibilities. The menu will show (x), where x is the number of items in the dropdown.
        divForBubble = BloomSourceBubbles.CreateDropdownIfNecessary(
            divForBubble
        );

        // Turns the tabbed and linked div bundle into a qtip bubble attached to the elementThatHasBubble.
        // Also makes sure the tooltips are setup correctly.
        BloomSourceBubbles.CreateAndShowQtipBubbleFromDiv(
            elementThatHasBubble,
            divForBubble,
            forceShowAlwaysOnBody
        );
    }

    // Cleans up a clone of the original translationGroup
    // and sets up the list items with anchors that will become the tabs to jump to linked source text
    // param 'group' is a .bloom-translationGroup DIV
    // optional param 'newLangTag' is defined when the user clicks on a language in the dropdown box
    // This method is only public for testing
    public static MakeSourceTextDivForGroup(
        group: HTMLElement,
        newLangTag?: string
    ): JQuery {
        // Copy source texts out to their own div, where we can make a bubble with tabs out of them
        // We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too,
        const divForBubble = $(group).clone();
        divForBubble.removeAttr("style");
        divForBubble.removeClass(); //remove them all
        divForBubble.addClass("ui-sourceTextsForBubble");
        // For now, we don't want labels (hints) in the source bubbles. BL-4295 discusses possibly changing this.
        divForBubble.find("label.bubble").each((index, element) => {
            $(element).remove();
        });

        //make the source texts in the bubble read-only and remove any user font size adjustments
        divForBubble.find("textarea, div").each(function(): boolean {
            //don't want empty items in the bubble
            const $this = $(this);
            if (BloomSourceBubbles.hasNoText(this)) {
                $this.remove();
                return true; // skip to next iteration of each()
            }
            $this.attr("readonly", "readonly");
            $this.removeClass("bloom-editable");
            $this.attr("contenteditable", "false");

            // don't want red in source text bubbles
            $this.removeClass("overflow");
            $this.removeClass("thisOverflowingParent");
            $this.removeClass("childOverflowingThis");

            const styleClass = StyleEditor.GetStyleClassFromElement(this);
            if (styleClass) $this.removeClass(styleClass);

            // remove any CustomPage min-height styles (they conflict with the source bubble css)
            BloomSourceBubbles.RemoveCustomPageAdditions($this);

            $this.addClass("source-text");

            return true; // continue .each(); though this is the end of the loop, it is needed for tsconfig's 'noImplicitReturns'
        });

        //don't want languages we're already showing in the text to be shown in the bubble
        //also ignore any elements that are lang='z', which are just prototypes
        divForBubble.find("*.bloom-visibility-code-on, [lang='z']").remove();

        //in case some formatting didn't get cleaned up
        StyleEditor.CleanupElement(divForBubble);

        //if there are no languages to show in the bubble, bail out now
        if (divForBubble.find("textarea, div").length === 0) return $();

        const vernacularLang = theOneLocalizationManager.getVernacularLang();

        // Make the li's for the source text elements in this new div, which will later move to a tabbed bubble
        // divForBubble is a single cloned bloom-translationGroup, so no need for .each() here
        const $this = divForBubble.first();
        $this.prepend('<nav><ul class="editTimeOnly bloom-ui"></ul></nav>'); // build the tabs here

        // First, sort the divs (and/or textareas) alphabetically by language code
        let items = $this.find("textarea, div");
        items.sort((a, b) => {
            //nb: Jan 2012: we modified "jquery.easytabs.js" to target @lang attributes, rather than ids.  If that change gets lost,
            //it's just a one-line change.
            const keyA = $(a).attr("lang");
            const keyB = $(b).attr("lang");
            if (keyA === vernacularLang) return -1;
            if (keyB === vernacularLang) return 1;
            if (keyA < keyB) return -1;
            if (keyA > keyB) return 1;
            return 0;
        });

        // BL-2357
        items = BloomSourceBubbles.SmartOrderSourceTabs(items, newLangTag);
        const list = $this.find("nav ul");
        items.each(function() {
            const sourceElement = this as HTMLElement;
            const langTag = sourceElement.getAttribute("lang");
            if (langTag) {
                const localizedLanguageName =
                    theOneLocalizationManager.getLanguageName(langTag) ||
                    langTag;
                // This is bizarre. The href ought to be referring to the element with the specified ID,
                // which should be the tab CONTENT that should be shown for this language. But we have modified
                // easytabs (see above) so that the target (main page content div) for a tab is the element whose
                // lang attr is the value following the # in the href, rather than its id.
                // Even more bizarrely, we make the id of the list item have that value also, so that
                // the apparent target of the <a> is the <li> it resides inside. Not sure why this is
                // helpful.
                $(list).append(
                    '<li id="' +
                        langTag +
                        '"><a class="sourceTextTab" href="#' +
                        langTag +
                        '">' +
                        localizedLanguageName +
                        "</a></li>"
                );
                (list.get(
                    0
                ) as HTMLElement).lastElementChild?.firstElementChild?.addEventListener(
                    "click",
                    () => postString("editView/sourceTextTab", langTag)
                );
                // BL-8174: Add a tooltip with the language tag to the item
                sourceElement.setAttribute("title", langTag);
            }

            if (sourceElement.innerText !== "") {
                BloomSourceBubbles.eliminateMultipleWhitespace(sourceElement);

                // BL-9198 Add a copy icon to the source bubble
                // It's actually easier to figure out what to copy out here and feed it into the button
                // component.
                const elementToCopy = sourceElement.closest(
                    ".source-text"
                ) as HTMLDivElement;
                let textToCopy: string | undefined = undefined;
                if (elementToCopy) {
                    textToCopy = elementToCopy.innerText;
                }
                const buttonDiv = document.createElement("div");
                sourceElement.append(buttonDiv);
                if (textToCopy) {
                    ReactDOM.render(
                        <CopyContentButton
                            onClick={() =>
                                BloomSourceBubbles.handleCopyBubbleSourceClick(
                                    textToCopy!
                                )
                            }
                        />,
                        buttonDiv
                    );
                }
            }
        });

        return divForBubble;
    } // end MakeSourceTextDivForGroup()

    private static eliminateMultipleWhitespace(element: HTMLElement) {
        const iter = document.createNodeIterator(element, NodeFilter.SHOW_TEXT);
        let textNode;
        while ((textNode = iter.nextNode())) {
            if (textNode.textContent)
                textNode.textContent = textNode.textContent.replace(
                    /\s\s+/g,
                    " "
                );
        }
    }

    private static handleCopyBubbleSourceClick(textToCopy: string): void {
        if (textToCopy) {
            postJson("common/clipboardText", {
                text: textToCopy
            });
            //navigator.clipboard.writeText(textToCopy); simpler, but not available until FF66+
        }
    }

    private static RemoveCustomPageAdditions(editableDiv: JQuery): void {
        const styleAttr = editableDiv.attr("style");
        if (!styleAttr) return;

        editableDiv.css("min-height", "");
    }

    // 'Smart' orders the tabs putting the latest viewed language first, followed by others in the collection
    // param 'items' is an alphabetical list of all the divs of different languages to be used as tabs
    // optional param 'newLangTag' is defined when the user clicks on a language in the dropdown box
    private static SmartOrderSourceTabs(items, newLangTag?: string): JQuery {
        // BL-2357 Do some smart ordering of source language tabs
        const settingsObject = GetSettings();
        let defaultSrcLang = settingsObject.defaultSourceLanguage;
        let destination = 0;
        if (newLangTag) defaultSrcLang = newLangTag;
        let newItems = BloomSourceBubbles.DoSafeReplaceInList(
            items,
            defaultSrcLang,
            destination
        );
        if ($(newItems).attr("lang") == defaultSrcLang) {
            // .attr() just gets the first one
            destination++;
            items = newItems;
        }
        const language2 = settingsObject.currentCollectionLanguage2;
        const language3 = settingsObject.currentCollectionLanguage3;
        if (language2 && language2 != defaultSrcLang) {
            newItems = BloomSourceBubbles.DoSafeReplaceInList(
                items,
                language2,
                destination
            );
            if ($(newItems[destination]).attr("lang") == language2) {
                destination++;
                items = newItems;
            }
        }
        if (language3 && language3 != defaultSrcLang) {
            newItems = BloomSourceBubbles.DoSafeReplaceInList(
                items,
                language3,
                destination
            );
            if ($(newItems[destination]).attr("lang") == language3) {
                items = newItems;
            }
        }
        return items;
    }

    private static DoSafeReplaceInList(
        items: JQuery,
        langCode: String,
        position: number
    ): JQuery {
        // if items contains a div with langCode, then try to put it at the position specified in the list
        // (unless it already occurs at an earlier position).
        let moveFrom = 0;
        let objToMove;
        const itemArray = items.toArray();
        items.each(function(idx, obj): boolean {
            const langTag = $(this).attr("lang");
            if (langTag == langCode && position < idx) {
                moveFrom = idx;
                objToMove = obj;
                return false; // break out of .each()
            }
            return true; // continue .each(); though this is the end of the loop, it is needed for tsconfig's 'noImplicitReturns'
        });
        if (moveFrom > 0) {
            itemArray.splice(moveFrom, 1); // removes the objToMove from the array
            itemArray.splice(position, 0, objToMove); // puts objToMove back in at position
            items = $(itemArray);
        }
        return items;
    }

    // Turns the cloned div 'divForBubble' into a tabbed bundle. If selectLangTag is supplied
    // (when reconstructing after a language in the pull-down is selected), we select that
    // language; otherwise, the first tab (which might be a hint).
    private static CreateTabsFromDiv(
        divForBubble: JQuery,
        selectLangTag?: string
    ): JQuery {
        //now turn that new div into a set of tabs
        const opts: any = {
            animate: false,
            tabs: "> nav > ul > li",
            // don't need it messing with the window url, and may help prevent previous
            // selections being copied into updated qtips.
            updateHash: false
        };
        const tabs = divForBubble.find("nav li");
        if (tabs.length > 0) {
            divForBubble.easytabs(opts);
            // Don't start by displaying the hint if a translation is available.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-5420.
            if (
                !selectLangTag &&
                tabs.length > 1 &&
                tabs.first().attr("id") == "hint"
            ) {
                selectLangTag = tabs
                    .first()
                    .next()
                    .attr("id");
            }
            if (selectLangTag) {
                // Somehow easytabs.select is NOT deselecting the item it selected by default.
                // This might be a consequence of the way we mangled it to select content by
                // lang rather than id. Or it might be something to do with multiple bubbles
                // using the same element IDs. Or something I haven't figured out yet.
                // Anyway, these two lines deselect whatever was selected,
                // and the third then correctly selects the one we want.
                divForBubble.find(".active").removeClass("active");
                divForBubble.find(">div").attr("style", "display: none");
                (divForBubble as any).easytabs("select", "#" + selectLangTag);
            }
        } else {
            divForBubble.remove(); //no tabs, so hide the bubble
            return $();
        }
        return divForBubble;
    }

    // If divForBubble contains more than two languages, create a dropdown button
    // to contain the extra possibilities
    // This method is only public for testing
    public static CreateDropdownIfNecessary(divForBubble: JQuery): JQuery {
        let firstSelectOption = 3;
        const tabs = divForBubble.find("nav li"); // may be li elements in the content
        if (tabs.length && tabs.first().attr("id") == "hint") {
            firstSelectOption++; // allow hint in addition to languages.
        }
        if (tabs.length < firstSelectOption) return divForBubble; // no change

        const dropMenu =
            "<li class='dropdown-menu'><div>0</div><ul class='dropdown-list'></ul></li>";
        divForBubble.find("nav ul").append(dropMenu);
        const container = divForBubble.find(".dropdown-list");
        tabs.each(function(idx): boolean {
            if (idx < firstSelectOption - 1) return true; // continue to next iteration of .each()
            const $this = $(this);
            const link = $this.find("a").clone(false); // don't want to keep easytab click event
            const langTag = link.attr("href").substring(1); // strip off hashmark
            const listItem = "<li lang='" + langTag + "'></li>";
            container.append(listItem);
            container.find('li[lang="' + langTag + '"]').append(link);
            $this.addClass("removeThisOne");
            return true; // continue .each(); though this is the end of the loop, it is needed for tsconfig's 'noImplicitReturns'
        });

        tabs.remove(".removeThisOne");

        container.find("li").each(function() {
            this.addEventListener(
                "click",
                BloomSourceBubbles.styledSelectChangeHandler,
                false
            );
        });

        // BL-2390 Add number of extra tabs to visible part of dropdown
        divForBubble
            .find(".dropdown-menu div")
            .text((tabs.length - firstSelectOption + 1).toString());
        return divForBubble;
    }

    private static styledSelectChangeHandler(event) {
        const newLangTag = event.target.href.split("#")[1];

        // Figure out which qtip we're in and go find the associated bloom-translationGroup
        const qtip = $(event.target)
            .closest(".qtip")
            .attr("id");
        const group = $(document).find(
            '.bloom-translationGroup[aria-describedby="' + qtip + '"]'
        );

        // Redo creating the source bubbles with the selected language first
        if (group && group.length > 0) {
            // should be
            const divForBubble = BloomSourceBubbles.ProduceSourceBubbles(
                group[0],
                newLangTag
            );
            postString("editView/sourceTextTab", newLangTag);
            if (divForBubble.length !== 0) {
                BloomHintBubbles.addHintBubbles(
                    group.get(0),
                    [group.get(0)],
                    [divForBubble.get(0)]
                );
                BloomSourceBubbles.MakeSourceBubblesIntoQtips(
                    group.get(0),
                    divForBubble,
                    newLangTag
                );
            }
        }
    }

    // Arrange a mutation observer to recompute position of tooltips when something changes in
    // any of the translation groups.
    // I don't think we need worry about disposing of this. It's useful until the page is reloaded.
    // This could be better done with resizeObserver...FixGecko60.
    // Note, in that case we'd want to observer each .bloom-editable, not the whole page.
    // (The page typically will NOT change size when a text block does.)
    public static setupSizeChangedHandling(groups: HTMLElement[]) {
        const observer = new MutationObserver(mutations => {
            $(groups).qtip("reposition");
        });
        const config = {
            childList: true,
            characterData: true,
            subtree: true
        };
        observer.observe(
            document.getElementsByClassName("bloom-page")[0],
            config
        );
    }

    // Turns the tabbed and linked div bundle into a qtip bubble attached to the bloom-translationGroup (group).
    // Also makes sure the tooltips are setup correctly.
    private static CreateAndShowQtipBubbleFromDiv(
        group: HTMLElement,
        divForBubble: JQuery,
        // When this is true, the bubble is shown always, without regard to whether it might overlap
        // any others, and it is put on the body rather than the page scaling container. This us useful
        // for dialogs that are not part of the page content, like the spell-words prompt dialog.
        // Since the dialog is outside the scaling container, it isn't scaled, so scaling the bubble
        // will make it look wrong in both size and position. And it only shows when hovering the dialog,
        // so conflicting with other bubbles is not an issue.
        forceShowAlwaysOnBody?: boolean
    ): void {
        let showEvents = false;
        let hideEvents = false;
        let showEventsStr;
        let hideEventsStr;
        let shouldShowAlways = true;

        const $group = $(group);
        if (
            (!forceShowAlwaysOnBody &&
                bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles(
                    $group
                )) ||
            !$group.is(":visible")
        ) {
            showEvents = true;
            showEventsStr = "focusin";
            hideEvents = true;
            hideEventsStr = "focusout";
            shouldShowAlways = false;
        }

        // turn that tab thing into a bubble, and attach it to the original div ("group")
        $group.each(function() {
            // const targetHeight = Math.max(55, $(this).height()); // This ensures we get at least one line of the source text!

            const $this: JQuery = $(this);

            $this.qtip({
                position: {
                    my: "left top",
                    at: "right top",
                    adjust: {
                        x: 0,
                        y: 0
                    },
                    container: forceShowAlwaysOnBody
                        ? document.body
                        : bloomQtipUtils.qtipZoomContainer()
                },
                content: divForBubble,

                show: {
                    event: showEvents ? showEventsStr : showEvents,
                    ready: shouldShowAlways
                },
                style: {
                    tip: {
                        corner: true,
                        width: 10,
                        height: 10,
                        mimic: "left center",
                        offset: 20
                    },
                    classes:
                        "ui-tooltip-green ui-tooltip-rounded uibloomSourceTextsBubble"
                },
                hide: hideEvents ? hideEventsStr : hideEvents,
                events: {
                    show: (event, api) => {
                        // don't need to do this if there is only one editable area
                        const $body: JQuery = $("body");
                        if (
                            $body
                                .find("*.bloom-translationGroup")
                                .not(".bloom-readOnlyInTranslationMode")
                                .length < 2
                        )
                            return;

                        // BL-878: set the tool tips to not be larger than the text area so they don't overlap each other
                        const $tip = api.elements.tooltip;
                        const $div = $body.find(
                            '[aria-describedby="' + $tip.attr("id") + '"]'
                        );
                        let maxHeight = $div.height();
                        if ($tip.height() > maxHeight) {
                            // make sure to show a minimum size
                            if (maxHeight < 70) maxHeight = 70;

                            // This code may run AFTER the code in SetupTooltips that removes passive-bubble
                            // and max-height from a qtip whose element has focus.
                            if (
                                document.activeElement &&
                                !$.contains($div.get(0), document.activeElement)
                            ) {
                                $tip.css("max-height", maxHeight);
                                $tip.addClass("passive-bubble");
                            }
                            $tip.attr("data-max-height", maxHeight);
                        }
                    },
                    render: (event, api) => {
                        if (!api.elements.tooltip || !api.elements.tooltip[0])
                            return;
                        const paras = api.elements.tooltip[0].getElementsByTagName(
                            "p"
                        );
                        for (let i = 0; i < paras.length; i++) {
                            const p = paras[i] as HTMLElement;
                            // won't let us tab to it, but lets it get focus when we do a drag selection,
                            // which allows keyboard events to be raised and ctrl-A intercepted.
                            p.setAttribute("tabindex", "-1");
                            p.addEventListener("keydown", kevent => {
                                // When the user types <Control-A> inside a source bubble, we don't
                                // want the whole page selected.  We want just the current text of
                                // the bubble to be selected.
                                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3899.
                                // The selection code was adapted from one of the answers given on
                                // http://stackoverflow.com/questions/985272/selecting-text-in-an-element-akin-to-highlighting-with-your-mouse
                                if (kevent.ctrlKey && kevent.key === "a") {
                                    kevent.preventDefault();
                                    kevent.stopImmediatePropagation();
                                    const target = kevent.target as HTMLElement;
                                    if (!target) return; // unlikely, makes checker happy
                                    // Since the event is attached to a paragraph, the parent should be the whole bloom-editable
                                    const wholeSource = target.parentElement;
                                    if (
                                        wholeSource &&
                                        wholeSource.ownerDocument &&
                                        wholeSource.ownerDocument.defaultView
                                    ) {
                                        const selection = wholeSource.ownerDocument.defaultView.getSelection();
                                        if (selection) {
                                            const range = wholeSource.ownerDocument.createRange();
                                            range.selectNodeContents(
                                                wholeSource
                                            );
                                            selection.removeAllRanges();
                                            selection.addRange(range);
                                        }
                                    }
                                }
                            });
                        }

                        // For clicks in the dropdown menu of the source bubble's final tab, we need to prevent
                        // the default behavior in order for the click to get through to styledSelectChangeHandler()
                        // reliably.  See https://issues.bloomlibrary.org/youtrack/issue/BL-6940.
                        // But if we always prevent the default behavior, it won't be possible to select and copy
                        // text from inside the source bubble.
                        // BL-9198 Also if the user is trying to click on the copy button in the source bubble
                        // we don't want the default behavior (which makes the bubble close;
                        // how? we aren't quite sure yet).
                        api.elements.tooltip.mousedown(ev => {
                            const cls = ev.target.getAttribute("class");
                            const href = ev.target.getAttribute("href");
                            if (
                                (cls == "sourceTextTab" &&
                                    href &&
                                    href.startsWith("#")) ||
                                ev.target.closest(".source-copy-button")
                            ) {
                                ev.preventDefault();
                            }
                        });

                        // This started out as an attempt to keep the bubble from getting focus, but didn't do that
                        // reliably for some undetermined reason. It's still useful so that clicking on a tooltip focuses its element.
                        // Otherwise the bubble may stay hidden behind something else even when clicked.
                        // However, if the user drags and makes a range selection, we don't want to hide it.
                        api.elements.tooltip.click(ev => {
                            const sel = window.getSelection();
                            if (sel && !sel.isCollapsed) {
                                // user made a range selection, probably to copy. Don't mess with it.
                                return;
                            }
                            // We're going to pick an element to focus. We start by getting the element our qtip
                            // is attached to.
                            let baseElement = $("body").find(
                                "[aria-describedby='" +
                                    api.elements.tooltip.attr("id") +
                                    "']"
                            );
                            // That might be either a group or an editable div. Focus needs to go to something actually editable,
                            // so a group is not a candidate. Fortunately, source bubbles are always attached to the top
                            // of a group (relating to translating the vernacular language, which is first), so focusing
                            // to the first visible child works.
                            // (Review: This probably depends on the children actually being in the order they are displayed,
                            // not re-ordered by flex rules. Seems to be true currently at least.)
                            if (
                                baseElement.hasClass("bloom-translationGroup")
                            ) {
                                baseElement = baseElement
                                    .find(".bloom-editable:visible")
                                    .first();
                            }
                            // Apparently you can't focus a div that lacks a tabindex, even if it is contenteditable.
                            // We don't want to permanently modify the element, so cheat by giving it one temporarily.
                            // -1 won't even temporarily affect any tabbing, since it means only focusable by code.
                            const hadTabIndex = baseElement.hasAttr("tabindex");
                            if (!hadTabIndex) {
                                baseElement.attr("tabindex", "-1");
                            }
                            baseElement.focus();
                            if (!hadTabIndex) {
                                baseElement.removeAttr("tabindex");
                            }
                        });
                    }
                }
            });

            BloomSourceBubbles.SetupTooltips($this);
        });
    }

    private static SetupTooltips(editableDiv: JQuery): void {
        // BL-878: show the full-size tool tip when the text area has focus
        editableDiv.find(".bloom-editable:visible").each((i, elt) => {
            // bloomApi postDebugMessage(
            //     "DEBUG BloomSourceBubbles.SetupTooltips/setting focus and blur handlers on " +
            //         elt.outerHTML
            // );
            // BloomField.WireToCKEditor() has focus and blur handlers to add/remove the
            // passive-bubble class on qtip tooltips that are *not* Source Bubbles.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-11745.
            $(elt).focus(event => {
                // bloomApi postDebugMessage(
                //     "DEBUG BloomSourceBubbles.SetupTooltips/on focus - element=" +
                //         (event.target as Element).outerHTML
                // );
                const element = event.target as Element;
                BloomSourceBubbles.ShowSourceBubbleForElement(element);
            });
            // reset the tooltip when the text box loses focus. The "reset other tooltips" code
            // in ShowSourceBubbleForElement() below (called by the focus handler) is
            // not enough because the field receiving focus may not be one that has
            // been configured with this event.
            $(elt).blur(ev => {
                // bloomApi postDebugMessage(
                //     "DEBUG BloomSourceBubbles.SetupTooltips/on blur - element=" +
                //         (ev.target as Element).outerHTML
                // );
                const tipId = (ev.target.parentNode as Element).getAttribute(
                    "aria-describedby"
                );
                const $tip = $("body").find("#" + tipId);
                if ($tip.hasClass("qtip-focus")) {
                    // If it's the tooltip that has gotten focus, don't reset it.
                    // See https://issues.bloomlibrary.org/youtrack/issue/BL-9901.
                    return;
                }
                $tip.addClass("passive-bubble");
                const maxHeight = $tip.attr("data-max-height");
                if (maxHeight) $tip.css("max-height", parseInt(maxHeight));
            });
        });
    }

    public static ShowSourceBubbleForElement(element: Element) {
        const $body = $("body");
        // reset tool tips that may be expanded
        $body.find(".qtip").each((idx, obj) => {
            const $thisTip = $(obj);
            $thisTip.addClass("passive-bubble");
            const maxHeight = $thisTip.attr("data-max-height");
            if (maxHeight) $thisTip.css("max-height", parseInt(maxHeight));
        });
        // show the full tip, if needed
        const tipId = (element.parentNode as Element).getAttribute(
            "aria-describedby"
        );
        const $tip = $body.find("#" + tipId);
        $tip.removeClass("passive-bubble");
        const maxHeight = $tip.attr("data-max-height");
        if (maxHeight) {
            $tip.css("max-height", "");
        }
    }
}
