/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../StyleEditor/StyleEditor.ts" />
/// <reference path="../js/bloomQtipUtils.ts" />

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import bloomQtipUtils from "../js/bloomQtipUtils";
import { MeasureText } from "../../utils/measureText";
import { theOneCanvasElementManager } from "../js/CanvasElementManager";
import { playingBloomGame } from "../toolbox/games/DragActivityTabControl";
import { addScrollbarsToPage, cleanupNiceScroll } from "bloom-player";
import { isInDragActivity } from "../toolbox/games/GameInfo";

interface qtipInterface extends JQuery {
    qtip(options: string): JQuery;
}

// logically a function of OverflowChecker, but it doesn't need any member variables, and with the
// way the old JQuery code here is messing with 'this', it's easier to just call it as an independent function
function getElementsThatCanOverflowOrNeedToBeResized(
    container: HTMLElement
): HTMLElement[] {
    //NB: for some historical reason in March 2014 the calendar still uses textareas
    // I think .bloom-visibility-code-on is more reliable here than :visible;
    // possibly this is set up before everything has been computed to be visible?
    // Note: this can probably be done with querySelectorAll, but a simple substitution
    // doesn't work (not a valid selector).  I think the problem is that querySelectorAll
    // doesn't know how to handle the :visible selector. So sticking with JQuery for now.
    const queryElementsThatCanOverflow =
        ".bloom-editable.bloom-visibility-code-on, textarea:visible";
    const potentialResults = $(container)
        .find(queryElementsThatCanOverflow)
        .toArray() as HTMLElement[];
    const results: HTMLElement[] = potentialResults.filter((x: HTMLElement) => {
        // Ignore things in game targets, they are duplicates that don't need to show overflow
        if (x.closest("[data-target-of]")) {
            return false;
        }
        return true;
    });
    return results;
}

export default class OverflowChecker {
    private;

    // When a div is overfull, these handlers will add the overflow class so it gets a red background or something
    // But this function should just do some basic checks and ADD the HANDLERS!
    public AddOverflowHandlers(container: HTMLElement) {
        const $editablePageElements = $(
            getElementsThatCanOverflowOrNeedToBeResized(container)
        );

        // BL-1260: disable overflow checking for pages with too many elements
        if ($editablePageElements.length > 30) {
            // since we're not going to check it, remove any indications from
            // previous checking. (Normal code also removes some qtips. I don't
            // think those survive from one page load to the next, so we don't
            // need to remove them here.)
            const cleanup = (className: string) => {
                Array.from(
                    container.getElementsByClassName(className)
                ).forEach(x => x.classList.remove(className));
            };
            cleanup("overflow");
            cleanup("thisOverflowingParent");
            cleanup("childOverflowingThis");
            cleanup("Layout-Problem-Detected");
            cleanup("pageOverflows");

            return;
        }

        //Add the handler so that when the elements change, we test for overflow
        $editablePageElements.on("keyup paste", e => {
            // Don't test for overflow on navigation keys.
            if (e.keyCode >= 33 && e.keyCode <= 40) {
                return;
            }

            // BL-2892 There's no guarantee that the paste target isn't inside one of the editablePageElements
            // If we allow an embedded paste target (e.g. <p>) to get tested for overflow, it will overflow artificially.
            const editable = $(e.target).closest($editablePageElements)[0];
            // Give the browser time to get the pasted text into the DOM first, before testing for overflow
            // GJM -- One place I read suggested that 0ms would work, it just needs to delay one 'cycle'.
            //        At first I was concerned that this might slow typing, but it doesn't seem to.
            setTimeout(() => {
                if (e.type === "paste") {
                    // If we're pasting, show as much of the text as possible even it can't all be shown.
                    // See BL-14632.
                    OverflowChecker.AdjustSizeOrMarkOverflow(
                        editable,
                        false,
                        true
                    );
                } else {
                    OverflowChecker.AdjustSizeOrMarkOverflow(editable);
                }

                //REVIEW: why is this here, in the overflow detection?

                // This will make sure that any language tags on this div stay in position with editing.
                // Reposition all language tips, not just the tip for this item because sometimes the edit moves other controls.
                (<qtipInterface>(
                    $(
                        getElementsThatCanOverflowOrNeedToBeResized(
                            document.body
                        )
                    )
                )).qtip("reposition");
            }, 100); // 100 milliseconds
            e.stopPropagation();
        });

        // Add another handler so that when the user resizes an origami pane, we check the overflow again
        $(container)
            .find(".split-pane-component-inner")
            .bind("_splitpaneparentresize", function() {
                const $this = $(this);
                $(getElementsThatCanOverflowOrNeedToBeResized($this[0])).each(
                    function() {
                        OverflowChecker.AdjustSizeOrMarkOverflowSoon(this);
                    }
                );
            });

        // Turn off any overflow indicators that might have been leftover from before
        $(container)
            .find(".overflow, .thisOverflowingParent, .childOverflowingThis")
            .each(function() {
                $(this).removeClass(
                    "overflow thisOverflowingParent childOverflowingThis"
                );
            });

        // Checking for overflow is time-consuming for complex pages, and doesn't HAVE to be done
        // before we display and allow editing. We set up a timeout for each one, which allows
        // other events to get in between, and also, if multiple sources request an overflow check
        // on the same element during startup, only one of them really happens. The delay is also
        // helpful in letting the page stabilize before we start resizing overlays.
        for (const editable of $editablePageElements.get()) {
            OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable);
        }
    }

    // Actual testable determination of Type I overflow or not
    // 'public' for testing (2 types of overflow are defined in AdjustSizeOrMarkOverflow below)
    public static IsOverflowingSelf(element: HTMLElement): boolean {
        const [overflowX, overflowY] = OverflowChecker.getSelfOverflowAmounts(
            element
        );
        return overflowX > 0 || overflowY > 0;
    }
    public static getSelfOverflowAmounts(
        element: HTMLElement
    ): [number, number] {
        // Ignore Topic divs as they are chosen from a list
        if (
            element.hasAttribute("data-book") &&
            element.getAttribute("data-book") == "topic"
        ) {
            return [0, 0];
        }

        if (
            $(element).css("display") === "none" ||
            $(element).css("display") === "inline"
        )
            return [0, 0]; //display:inline always returns zero width, so there's no way to know if it's overflowing

        // I'm going to leave this comment and variable as a reminder that at one point we had to fudge things a
        // little to make it look right. This might be because we weren't correctly preventing flex grow and shrink
        // before measuring the content height.
        // At present we don't seem to need a fudge factor to get an accurate indication of overflow or to make
        // a good decision about whether to grow the box.
        // Original comment:
        //In the Picture Dictionary template, all words have a scrollHeight that is 3 greater than the client height.
        //In the Headers of the Term Intro of the SHRP C1 P3 Pupil's book, scrollHeight = clientHeight + 6!!! Sigh.
        // the focussedBorderFudgeFactor takes care of 2 pixels, this adds one more.
        const shortBoxFudgeFactor = 0;

        // Fonts like Andika New Basic have internal metric data that indicates
        // a descent a bit larger than what letters typically have...I think
        // intended to leave room for diacritics. Gecko calculates the space
        // needed to render the text as including this, so it is part of the
        // element.scrollHeight. But in the absence of such diacritics, the extra
        // space is not needed. This is particularly annoying for auto-sized elements
        // like the front cover title, which (due to line-height specs) may auto-size
        // to leave room for the actually visible text, but not the extra white space
        // the font calls for (and which we reduced the line height to hide).
        // To avoid spuriously reporting overflow in such cases (BL-6338), we adjust
        // for the discrepancy.
        const measurements = MeasureText.getDescentMeasurementsOfBox(element);
        // console.log(
        //     "actual descent: " +
        //         measurements.actualDescent +
        //         " layout descent: " +
        //         measurements.layoutDescent +
        //         " font descent: " +
        //         measurements.fontDescent
        // );
        const fontFudgeFactor = Math.max(
            measurements.fontDescent - measurements.actualDescent,
            0
        );

        const overflowY =
            this.contentHeight(element) -
            fontFudgeFactor -
            (element.clientHeight + shortBoxFudgeFactor);
        // console.log(
        //     "overflowY: " +
        //         overflowY +
        //         " contentHeight: " +
        //         this.contentHeight(element) +
        //         " fontFudgeFactor: " +
        //         fontFudgeFactor +
        //         " clientHeight: " +
        //         element.clientHeight +
        //         " shortBoxFudgeFactor: " +
        //         shortBoxFudgeFactor
        // );
        const overflowX = element.scrollWidth - element.clientWidth;

        return [overflowX, overflowY];
    }

    private static contentHeight(element: HTMLElement): number {
        if (element.childNodes.length === 0) {
            return 0;
        }

        // Use a temporary clone so we don't modify the actual element.
        // Otherwise, the text box can get scrolled back to the top when the user types. See BL-13942.
        const elementClone = element.cloneNode(true) as HTMLElement;
        elementClone.style.visibility = "hidden";
        elementClone.style.position = "absolute"; // prevent any inadvertent layout effects
        // position:absolute is not enough for accurate layout, we also need to make sure the width
        // of the cloned element is the same as the original.  This is because position:absolute
        // makes the clone's containing box include the area used by the parent's padding: see
        // https://stackoverflow.com/questions/17115344/absolute-positioning-ignoring-padding-of-parent.
        // Height is not as critical because either the scrollHeight is accurate (which we detect),
        // or we calculate the height ourselves below.  See BL-14053.
        elementClone.style.width = element.clientWidth + "px";
        element.parentElement!.appendChild(elementClone);

        let result = 0;
        try {
            let maxContentBottom = 0;

            // First, we need to make sure that the size and position of the children are not affected
            // by flex grow or shrink. Currently, there is no reason to set these back because we are
            // operating on a temporary clone.

            // Note that there may be no children, even though there are childNodes. We can only set style
            // for elements, but we need to measure all the nodes (at least for one unit test).
            const children = Array.from(elementClone.children);
            children.forEach((e: HTMLElement) => {
                e.style.flexGrow = "0";
                e.style.flexShrink = "0";
            });
            // The element itself must not shrink, lest its scrollHeight or clientHeight be inaccurate.
            elementClone.style.flexGrow = "0";
            elementClone.style.flexShrink = "0";
            // scrollHeight is a reliable way to get the information if it is greater than the clientHeight,
            // and means we don't have to worry about scroll position.  (scrollHeight is never less than
            // clientHeight.  See https://developer.mozilla.org/en-US/docs/Web/API/Element/scrollHeight.)
            // (One odd situation is when lineHeight is small, e.g., less than 1.3 for Andika. In that
            // situation, a paragraph whose height is otherwise unconstrained is not high enough to show
            // descenders, and even with a single line of text its scrollHeight is greater than clientHeight.
            // Our auto-sizing code handles this, making a canvas element big enough to show the descenders
            // (if any are actually present).
            // However, when the bloom-editable is empty, we don't get the extra scrollHeight we are adjusting
            // for. There's a kludge to partly handle this in adjustSizeOfContainingCanvasElementToMatchContent,
            // but there may be a better way to handle things. I think part of the problem may be that if there
            // is no actual text, the browser can't really pick a font to display it, so some measurements
            // can't be precisely made.)
            result = elementClone.scrollHeight;
            if (elementClone.scrollHeight <= elementClone.clientHeight) {
                // But if scrollHeight is less than or equal to clientHeight, we use our own algorithm.
                elementClone.childNodes.forEach((x: HTMLElement) => {
                    if (!(x instanceof HTMLElement)) return; // not an element; I think this is redundant
                    if (window.getComputedStyle(x).position === "absolute")
                        return; // special element like format button
                    // Not sure if offsetTop can ever be negative, now that I prevented flexShrink.
                    // But in case it is, we don't want to shrink the content height. And often there is only one
                    // child so we'll get an accurate figure this way even if there is some scrolling.
                    const xbottom = Math.max(x.offsetTop, 0) + x.offsetHeight;
                    if (xbottom > maxContentBottom) {
                        maxContentBottom = xbottom;
                    }
                });
                result = maxContentBottom;
            }
        } finally {
            // Remove the temporary clone we added.
            element.parentElement!.removeChild(elementClone);
        }
        return result;
    }

    // Actual testable determination of Type II overflow or not
    // 'public' for testing (2 types of overflow are defined in AdjustSizeOrMarkOverflow below)
    // returns nearest ancestor that this element overflows
    public static overflowingAncestor(
        element: HTMLElement
    ): HTMLElement | null {
        // Ignore Topic divs as they are chosen from a list
        if (
            element.hasAttribute("data-book") &&
            element.getAttribute("data-book") == "topic"
        ) {
            return null;
        }
        // We want to prevent an inner div from expanding past the borders set by any fixed containing element.
        const parents = $(element).parents();
        if (!parents) {
            return null;
        }
        // A zoom on the body affects offset but not outerHeight, which messes things up if we don't
        // account for it. It's better to correct offset so we don't need to also adjust the fudge factors.
        // Computing scale from the element can be a problem if it is a small element near the bottom
        // of the page, since rounding errors in the element's scale can have a considerable effect
        // when applied to its top. The biggest scaled div from which we can reliably compute the most
        // accurate scaling factor is the page div.  (This is inside both the div that handles page
        // zooming and the the div that does scaling for full bleed.)
        const scaledElt = document.getElementsByClassName(
            "bloom-page"
        )[0] as HTMLElement;
        const scaleY =
            scaledElt.getBoundingClientRect().height / scaledElt.offsetHeight;
        for (let i = 0; i < parents.length; i++) {
            // search ancestors starting with nearest
            const currentAncestor = $(parents[i]);
            const parentBottom =
                (currentAncestor.offset()?.top ?? 0) / scaleY +
                currentAncestor.outerHeight(true);
            const elemTop = ($(element).offset()?.top ?? 0) / scaleY;
            const elemBottom = elemTop + $(element).outerHeight(false);
            // console.log("Offset top: " + elemTop + " Outer Height: " + $(element).outerHeight(false));
            // If css has "overflow: visible;", scrollHeight is always 2 greater than clientHeight.
            // This is because of the thin grey border on a focused input box.
            // In fact, the focused grey border causes the same problem in detecting the bottom of a marginBox
            // so we'll apply the same 'fudge' factor to both comparisons.
            const focusedBorderFudgeFactor = 2;

            if (elemBottom > parentBottom + focusedBorderFudgeFactor) {
                return currentAncestor[0];
            }
            if (currentAncestor.hasClass("marginBox")) {
                break; // Don't check anything outside of marginBox
            }
        }
        return null;
    }

    // We want to check for overflow, but we can't afford the delay right now.
    // (checking all elements on a complex page can take several seconds, and this
    // is called on every mouse move when adjusting origami). We will wait a second.
    // If we're asked to check the same element again before that second is up, we'll
    // cancel that timer and start a new one. When we don't get a new request for a
    // whole second, we'll do the check.
    // A further benefit of this delay is that a single mouse movement could resize
    // several origami elements, and the loop over all of them used to be in the
    // handling of one mouse move. Now, each timeout will be its own event, and
    // hopefully other events can be processed in between if they occur.
    public static AdjustSizeOrMarkOverflowSoon(editable: HTMLElement) {
        const timeOut = (editable as any).overflowCheckTimeout;
        if (timeOut) {
            clearTimeout(timeOut);
        }
        (editable as any).overflowCheckTimeout = setTimeout(() => {
            this.CheckOnMinHeight(editable);
            OverflowChecker.AdjustSizeOrMarkOverflow(editable);
        }, 1000);
    }

    // Checks for overflow on a bloom-page and adds/removes the proper class
    // N.B. This function is specifically designed to be called from within AddOverflowHandler()
    // but is also called from within StyleEditor (and therefore public)
    public static AdjustSizeOrMarkOverflow(
        editable: HTMLElement,
        doNotShrink?: boolean,
        growAsMuchAsPossible?: boolean
    ) {
        // There are two types of overflow that we need to check.
        // 1-When we're called by a handler on an element, we need to check that that element
        // doesn't overflow internally (i.e. has too much stuff to fit in itself).
        // 2-We also need to check that this element and any OTHER elements on the page
        // haven't been pushed outside the margins

        // Type 1 Overflow
        const $editable = $(editable);
        if ($editable.hasClass("overflow")) {
            OverflowChecker.RemoveOverflowQtip($editable);
        }

        // We used to remove the "overflow" class unconditionally, then add it back if needed.
        // But in 6.0, that started causing the box to scroll back to the top sometimes. See BL-13942.
        // Now we leave it in place until we determine if it should be there.
        // $box.removeClass("overflow");

        $editable.removeClass("thisOverflowingParent");
        $editable.off("mousemove.overflow");
        $editable.off("mouseleave.overflow");
        $editable.parents(".childOverflowingThis").each((dummy, parent) => {
            OverflowChecker.RemoveOverflowQtip($(parent));
        });
        OverflowChecker.RemoveOverflowQtip($editable);
        $editable.parents().removeClass("childOverflowingThis");

        const preventOverflowY = editable.classList.contains(
            "bloom-padForOverflow"
        );

        if (preventOverflowY) {
            editable.style.paddingBottom = "0";
            const measurements = MeasureText.getDescentMeasurementsOfBox(
                editable
            );
            const excessDescent =
                measurements.actualDescent - measurements.layoutDescent;
            if (excessDescent > 0) {
                editable.style.paddingBottom =
                    "" + Math.ceil(excessDescent) + "px";
            }
        }

        // ENHANCE: The overflow detection doesn't work right immediately if you add one line too much (such that it overflows),
        //          then backspace to remove the newly added line. It still indicates overflow (because it was was scrolled down, I guess).
        //          However, if you press the up arrow long enough until you get it to scroll back up, it will reset to Not Overflowing.
        //          Reloading the page will also clear it.
        const overflowAmounts = OverflowChecker.getSelfOverflowAmounts(
            editable
        );
        const overflowX = overflowAmounts[0];
        let overflowY = overflowAmounts[1];
        overflowY = theOneCanvasElementManager.adjustSizeOfContainingCanvasElementToMatchContent(
            editable,
            overflowY,
            doNotShrink,
            growAsMuchAsPossible
        );
        if (preventOverflowY) {
            // The usual fairly crude calculation may indicate it's overflowing, but
            // above we did a much more precise calculation and gave it just enough padding
            // to prevent it (if necessary).
            // It's likely that the calls above to getSelfOverflowAmounts and adjustSizeOfContainingCanvasElementToMatchContent
            // are redundant in this case. The latter only applies to canvas element boxes, which are unlikely
            // to be bloom-padForOverflow. However, I can't guarantee that a bloom-padForOverflow box
            // can't overflow horizontally. It seemed safest to leave the existing code alone and just
            // prevent the spurious overflow markup.
            overflowY = 0;
        }

        let skipType2Overflow = false;
        if (isInDragActivity(editable)) {
            // We decided that overflow was just causing too many problems as we tried to wrap
            // up drag activities. So for now, we are just turning off overflow reporting completely
            // in drag activities. See BL-14783.
            // Actually, we would love to just turn off overflow checking completely, but at this
            // point, it is integrated with the code which resizes the canvas element.
            // That's also why we can't just filter out these elements in getElementsThatCanOverflowOrNeedToBeResized.
            overflowY = 0;
            skipType2Overflow = true;
        }

        if (overflowY > 0 || overflowX > 0) {
            $editable.addClass("overflow");
            const page = $editable.closest(".bloom-page");
            if (overflowY > 0 && page.length) {
                cleanupNiceScroll();
                addScrollbarsToPage(page[0]);
            }

            if ($editable.parents("[class*=Device]").length === 0) {
                // don't show an overflow warning if we have scrolling available
                theOneLocalizationManager
                    .asyncGetText(
                        "EditTab.Overflow",
                        "This box has more text than will fit",
                        ""
                    )
                    .done(overflowText => {
                        $editable.qtip({
                            content:
                                '<img data-overflow="true" height="20" width="20" style="vertical-align:middle" src="/bloom/images/Attention.svg">' +
                                overflowText,
                            show: { event: "mouseenter" },
                            hide: { event: "mouseleave" },
                            position: {
                                my: "top right",
                                at: "right bottom",
                                container: bloomQtipUtils.qtipZoomContainer()
                            }
                        });
                    });
            }
        } else {
            $editable.removeClass("overflow");
            const page = $editable.closest(".bloom-page");
            if (page.length) {
                cleanupNiceScroll();
            }
        }

        if (skipType2Overflow) return;

        const container = $editable.closest(".marginBox");
        const quizPage = $(container).closest(".simple-comprehension-quiz");
        const editablePageElements = $(
            getElementsThatCanOverflowOrNeedToBeResized(container.get(0))
        );

        // Type 2 Overflow - We'll check ALL of these for overflow past any ancestor
        editablePageElements.each(function() {
            const $this = $(this);
            const overflowingAncestor = OverflowChecker.overflowingAncestor(
                $this[0]
            );
            if (overflowingAncestor == null) {
                if (!OverflowChecker.IsOverflowingSelf($this[0])) {
                    $this.removeClass("overflow"); // might be a remnant from earlier overflow
                    $this.removeClass("thisOverflowingParent");
                }
            } else {
                const $overflowingAncestor = $(overflowingAncestor);
                // We may already have a qtip on this parent in the form of a hint or source
                // bubble.  We don't want to override that qtip with an overflow warning since
                // we have other indications of overflow available on the child.
                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-6295.
                const oldQtip = OverflowChecker.GetQtipContent(
                    $overflowingAncestor
                );
                if (oldQtip && !OverflowChecker.DoesQtipMarkOverflow(oldQtip)) {
                    return; // don't override existing qtip (probably hint or source bubble)
                }
                // BL-1261: don't want the typed-in box to be marked overflow just because it made another box
                // go past the margins
                // $box.addClass('overflow'); // probably typing in the focused element caused this
                if (quizPage.length) {
                    // We want to ignore overflow on quiz pages.  See https://issues.bloomlibrary.org/youtrack/issue/BL-9952.
                    return;
                }
                $this.addClass("thisOverflowingParent"); // but it's this one that is actually overflowing
                $overflowingAncestor.addClass("childOverflowingThis");
                theOneLocalizationManager
                    .asyncGetText(
                        "EditTab.OverflowContainer",
                        "A container on this page is overflowing",
                        ""
                    )
                    .done(overflowText => {
                        $overflowingAncestor.qtip({
                            content:
                                '<img data-overflow="true" height="20" width="20" style="vertical-align:middle" src="/bloom/images/Attention.svg">' +
                                overflowText,
                            show: { event: "enterBorder" }, // nonstandard events triggered by mouse move in code below
                            hide: { event: "leaveBorder" },
                            position: {
                                my: "top right",
                                at: "right bottom",
                                container: bloomQtipUtils.qtipZoomContainer()
                            }
                        });
                    });
                let showing = false;
                $overflowingAncestor.on("mousemove.overflow", event => {
                    if (overflowingAncestor == null) return; // prevent bad static analysis
                    const bounds = overflowingAncestor.getBoundingClientRect();
                    const scaleY =
                        bounds.height / overflowingAncestor.offsetHeight;
                    const offsetY = (event.clientY - bounds.top) / scaleY;
                    // The cursor is likely to be a text cursor at this point, so it's hard to point exactly at the line. If the mouse is close,
                    // show the tooltip.
                    const shouldShow =
                        offsetY >= $overflowingAncestor.innerHeight() - 10 &&
                        offsetY <=
                            $overflowingAncestor.outerHeight(false) + 10 &&
                        // I don't like this module knowing about this, but how else to hide it?
                        !playingBloomGame(overflowingAncestor);
                    if (shouldShow && !showing) {
                        showing = true;
                        $overflowingAncestor.trigger("enterBorder");
                    } else if (!shouldShow && showing) {
                        showing = false;
                        $overflowingAncestor.trigger("leaveBorder");
                    }
                });
                $overflowingAncestor.on("mouseleave.overflow", () => {
                    if (showing) {
                        showing = false;
                        $overflowingAncestor.trigger("leaveBorder");
                    }
                });
            }
        });
        OverflowChecker.UpdatePageOverflow(container.closest(".bloom-page"));
    } // end AdjustSizeOrMarkOverflow

    // Destroy any qtip on this element that marks overflow, but leave other qtips alone.
    // This restriction is an attempt not to remove bloom hint and source bubbles.
    private static RemoveOverflowQtip(element: JQuery) {
        const qtipContent = OverflowChecker.GetQtipContent(element);
        if (OverflowChecker.DoesQtipMarkOverflow(qtipContent)) {
            element.qtip("destroy");
        }
    }

    // Test whether this qtip (if it exists) marks an overflow condition.
    private static DoesQtipMarkOverflow(qtipContent: any): boolean {
        if (!qtipContent) {
            return false;
        }
        // This is the most reliable way I can find to detect data-overflow tooltips,
        // at least on canvas element boxes. By experiment in the debugger, qtipContent does
        // not seem to be an element at all, but some sort of configuration object
        // for the qtip, so trying to retrieve it as an attribute (as the code below does)
        // does not work.
        // Note that qtipContent.text can be an object or a string, so we need to check
        // that the startsWith method exists before calling it.  (The check verifies that
        // it's a string.)  See BL-13126.
        if (
            qtipContent.text &&
            qtipContent.text.startsWith &&
            qtipContent.text.startsWith('<img data-overflow="true"')
        ) {
            return true;
        }
        // keeping this out of paranoia...old code that no longer seems to work,
        // but I'm not sure it's never needed.
        if ($(qtipContent).attr("data-overflow") == "true") {
            return true;
        }
        return false;
    }

    // Return any qtip content attached to this element, or null if none is attached.
    private static GetQtipContent(element: JQuery): any {
        const qtipData = element.data("qtip");
        if (qtipData) {
            const options = qtipData.options;
            if (options) {
                return options.content;
            }
        }
        return null;
    }

    private static GetScrollInsteadOfOverflow(page: HTMLElement): boolean {
        const $page = $(page);
        return (
            $page.hasClass("Device16x9Portrait") ||
            $page.hasClass("Device16x9Landscape")
        );
    }
    // Make sure there are no boxes with class 'overflow' or 'thisOverflowingParent' on the page before removing
    // the page-level overflow marker 'pageOverflows', or add it if there are.
    private static UpdatePageOverflow(page) {
        // TODO: Investigate BL-6686. It seems that it takes more clicks to propagate the pageOverflows class onto a FrontCover page than a normal page??? Repro in both 4.4 and 4.5
        const $page = $(page);
        if (
            !$page.find(".overflow").length &&
            !$page.find(".thisOverflowingParent").length
        )
            $page.removeClass("pageOverflows");
        else $page.addClass("pageOverflows");

        // BL-11949: books with device layouts can ignore overflows because we'll show a scrollbar
        if (this.GetScrollInsteadOfOverflow(page)) {
            $page.removeClass("pageOverflows");
            // note, we don't yet remove the bubble that says there is too much text. This code is already spaghetti enough, I didn't want to pay that price at this time. --JH
        }
    }

    // Checks a couple of situations where we might need to modify min-height
    // If necessary, this will do the modification
    private static CheckOnMinHeight(box) {
        const $box = $(box);
        const overflowy = $box.css("overflow-y");
        if (overflowy == "hidden") {
            // On custom pages we hide overflow in the y direction. This sometimes shows a scroll bar.
            // It can show prematurely when there is only one line of text unless we force min-height
            // to be exactly line-height. I don't know why. See BL-1034 premature scroll bars
            // (Note: although line-height can have other units than min-height, the css function
            // (at least in FF) always returns px, so we can just copy it).
            $box.css("min-height", $box.css("line-height"));
        } else {
            // We want a min-height that is at least enough to display one line; otherwise we
            // get confusing overflow indications when just a single character is typed.
            // This problem can now be caused not just by template designers, but by end users
            // setting line-spacing or font-size bigger than the template designer expected.
            // So rather than making an ugly warning we just make sure every box is big enough to
            // show at least one line of text.
            // Note: we must use floats here; it's easy to get a situation where lineHeight works out
            // to say 50.05px, if we then set lineHeight to 50, the div's scrollHeight is 51 and
            // it's clientHeight (from min-height) is 50, and it is considered overflowing.
            // (There's a fudgeFactor in the overflow code that might prevent this, but using
            // floats seems safer.)
            // First get rid of any min-height fudge added locally in the past; if we don't do
            // this we can never reduce min-height even if the user reduces line-spacing or font size.
            // Enhance: the previous behavior of displaying a warning might be more useful for
            // template designers.
            // Enhance: it would be nice to redo this and overflow marking when the user changes
            // box format.
            $box.css("min-height", "");
            const lineHeight = parseFloat($box.css("line-height"));
            const minHeight = parseFloat($box.css("min-height"));
            // We do this comparison so that if the template designer has set a larger min-height,
            // we don't mess with it.
            if (minHeight < lineHeight) {
                $box.css("min-height", lineHeight + 0.01);
            }
        }
        // Remove any left-over warning about min-height is less than lineHeight (from earlier version of Bloom)
        $box.removeClass("Layout-Problem-Detected");
    } // end CheckOnMinHeight
} // end class OverflowChecker
