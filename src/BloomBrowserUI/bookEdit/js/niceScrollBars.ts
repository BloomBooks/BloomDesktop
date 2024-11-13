import "jquery.nicescroll";

// This constant string was copied from bloom-player with minimal changes and should be in a common library.
const kSelectorForPotentialNiceScrollElements =
    ".bloom-translationGroup:not(.bloom-imageDescription) .bloom-editable.bloom-visibility-code-on, " +
    ".scrollable"; // we added .scrollable for branding cases where the boilerplate text also needs to scroll

// Add a "nice" scrollbar to the page if the content overflows.
// This function was copied from bloom-player with minimal changes and should be in a common library.
export function addScrollbarsToPage(bloomPage: Element): void {
    // Expected behavior for cover: "on the cover, which is has a very dynamic layout, we just don't do scrollbars"
    if (bloomPage.classList.contains("cover")) {
        return;
    }
    // on a browser so obsolete that it doesn't have IntersectionObserver (e.g., IE or Safari before 12.2),
    // we just won't get scrolling.
    if ("IntersectionObserver" in window) {
        // Attach overlaid scrollbar to all editables except textOverPictures (e.g. comics)
        // Expected behavior for comic bubbles:  "we want overflow to show, but not generate scroll bars"
        let scrollBlocks: HTMLElement[] = [];
        let countOfObserversExpectedToReport = 0;
        let countOfObserversThatHaveReported = 0;
        $(bloomPage)
            .find(kSelectorForPotentialNiceScrollElements)
            .each((index, elt) => {
                // Process the blocks that are possibly overflowing.
                // Blocks that are overflowing will be configured to use niceScroll
                // so the user can scroll and see everything. That is costly, because
                // niceScroll leaks event listeners every time it is called. So we don't
                // want to use it any more than we need to.  (Is this true?) Also, niceScroll
                // somehow fails to work when our vertical alignment classes are applied;
                // probably something to do with the bloom-editables being display:flex
                // to achieve vertical positioning. We can safely remove those classes
                // if the block is overflowing, because there's no excess white space
                // to distribute.
                // Note: there are complications Bloom desktop handles in determining
                // accurately whether a block is overflowing. We don't handle those here.
                // If it is close enough to overflow to get a scroll bar, it's close
                // enough not to care whether extra white space is at the top, bottom,
                // or split (hence we can safely remove classes used for that).
                // And we'll risk sometimes adding niceScroll when we could (just)
                // have done without it.  Using the same code in Bloom Desktop and bloom-player
                // ensures consistent scrollbar behavior between the two.
                const firstChild = elt.firstElementChild;
                const lastChild = elt.lastElementChild;
                if (!firstChild || !lastChild) {
                    // no children, can't be overflowing
                    return;
                }

                // We need to know the scale of the page, because nicescroll doesn't handle
                // scaling when it comes to the padding that we add to the top and left of
                // the translationGroup element.  See BL-13796.
                let scale = 1;
                const scaleContainer = elt.closest(
                    "#page-scaling-container"
                ) as HTMLElement;
                if (scaleContainer) {
                    const scaleValue = scaleContainer.style.transform;
                    if (scaleValue && scaleValue.startsWith("scale(")) {
                        scale = parseFloat(
                            scaleValue.substring(6, scaleValue.length - 1)
                        );
                    }
                }
                let topAdjust = 0;
                let leftAdjust = 0;
                if (scale !== 1) {
                    const compStyles = window.getComputedStyle(
                        elt.parentElement!
                    );
                    const topPadding =
                        compStyles.getPropertyValue("padding-top") ?? "0";
                    const leftPadding =
                        compStyles.getPropertyValue("padding-left") ?? "0";
                    topAdjust = parseFloat(topPadding) * (scale - 1);
                    leftAdjust = parseFloat(leftPadding) * (scale - 1);
                }

                // We don't really want continuous observation, but this is an elegant
                // way to find out whether each child is entirely contained within its
                // parent. Unlike computations involving coordinates, we don't have to
                // worry about whether borders, margins, and padding are included in
                // various measurements. We do need to check the first as well as the
                // last child, because if text is aligned bottom, any overflow will be
                // at the top.
                const observer = new IntersectionObserver(
                    (entries, ob) => {
                        // called more-or-less immediately for each child, but after the
                        // loop creates them all.
                        entries.forEach(entry => {
                            countOfObserversThatHaveReported++;
                            ob.unobserve(entry.target); // don't want to keep getting them, or leak observers
                            const isBubble = !!entry.target.closest(
                                ".bloom-textOverPicture"
                            );
                            // In bloom desktop preview, we set width to 200% and then scale down by 50%.
                            // This can lead to intersection ratios very slightly less than 1, probably due
                            // to pixel rounding of some sort, when in fact the content fits comfortably.
                            // For example, in one case we got a boundingClientRect 72.433 high
                            // and an intersectionRect 72.416, for a ratio of 0.9998.
                            // If a block is 1000 pixels high and really overflowing by 1 pixel, the ratio
                            // will be 0.999. I think it's safe to take anything closer to 1 than that as
                            // 'not overflowing'.
                            let overflowing = entry.intersectionRatio < 0.999;

                            if (overflowing && isBubble) {
                                // We want to be less aggressive about putting scroll bars on bubbles.
                                // Most of the time, a bubble is very carefully sized to just fit the
                                // text. But the intersection observer wants it to fit a certain amount
                                // of white space as well. We want a scroll bar if it's overflowing
                                // really badly for some reason, but that's much more the exception
                                // than the rule, so better a little clipping when the bubble is badly
                                // sized than a scroll bar that isn't needed in one that is just right.
                                // Example: a bubble which appears to fit perfectly, 3 lines high:
                                // its clientHeight is 72; containing bloom-editable's is 59;
                                // lineHeight is 24px. IntersectionRatio computes to 59/72,
                                // which makes the 'overflow' 13. A ratio of 0.5 as we originally
                                // proposed would give us a scroll bar we don't want.
                                let maxBubbleOverflowLineFraction = 0.6;
                                if (
                                    entry.target !=
                                        entry.target.parentElement
                                            ?.firstElementChild ||
                                    entry.target !=
                                        entry.target.parentElement!
                                            .lastElementChild
                                ) {
                                    // Bubbles are center-aligned vertically. If this is not the only
                                    // child,the first and last will overflow above and below by about the
                                    // same amount. So we're only really looking at half the overflow on this para,
                                    // and should reduce the threshold.
                                    maxBubbleOverflowLineFraction /= 2;
                                }
                                const overflow =
                                    (1 - entry.intersectionRatio) *
                                    entry.target.clientHeight;
                                const lineHeightPx = window.getComputedStyle(
                                    entry.target
                                ).lineHeight;
                                const lineHeight = parseFloat(
                                    // remove the trailing "px"
                                    lineHeightPx.substring(
                                        0,
                                        lineHeightPx.length - 2
                                    )
                                );
                                overflowing =
                                    overflow >
                                    lineHeight * maxBubbleOverflowLineFraction;
                            }
                            if (
                                overflowing &&
                                scrollBlocks.indexOf(
                                    entry.target.parentElement!
                                ) < 0
                            ) {
                                scrollBlocks.push(entry.target.parentElement!);
                                // remove classes incompatible with niceScroll
                                const group = entry.target.parentElement!
                                    .parentElement!;
                                if (
                                    group.classList.contains(
                                        "bloom-vertical-align-center"
                                    )
                                ) {
                                    group.classList.remove(
                                        "bloom-vertical-align-center"
                                    );
                                    group.classList.add(
                                        "bloom-vertical-align-center-removed"
                                    );
                                }
                                if (
                                    group.classList.contains(
                                        "bloom-vertical-align-bottom"
                                    )
                                ) {
                                    group.classList.remove(
                                        "bloom-vertical-align-bottom"
                                    );
                                    group.classList.add(
                                        "bloom-vertical-align-bottom-removed"
                                    );
                                }
                                if (isBubble) {
                                    // This is a way of forcing it not to be display-flex, which doesn't
                                    // work with the nice-scroll-bar library we're using.
                                    // That library messes with the element style, so it seemed safer
                                    // not to do that myself.
                                    entry.target.parentElement!.classList.add(
                                        "scrolling-bubble"
                                    );
                                }
                            }
                            if (
                                countOfObserversThatHaveReported ===
                                countOfObserversExpectedToReport
                            ) {
                                // configure nicescroll...ideally only once for all of them
                                $(scrollBlocks).niceScroll({
                                    autohidemode: false,
                                    railoffset: {
                                        top: -topAdjust,
                                        left: -leftAdjust
                                    },
                                    cursorwidth: "12px",
                                    cursorcolor: "#000000",
                                    cursoropacitymax: 0.1,
                                    cursorborderradius: "12px" // Make the corner more rounded than the 5px default.
                                });
                                setupSpecialMouseTrackingForNiceScroll(
                                    bloomPage
                                );
                                scrollBlocks = []; // Just in case it's possible to get callbacks before we created them all.
                            }
                        });
                    },
                    { root: elt }
                );
                countOfObserversExpectedToReport++;
                observer.observe(firstChild);
                if (firstChild !== lastChild) {
                    countOfObserversExpectedToReport++;
                    observer.observe(lastChild);
                }
            });
    }
}
// This function was copied from bloom-player with minimal changes and should be in a common library.
function setupSpecialMouseTrackingForNiceScroll(bloomPage: Element) {
    bloomPage.removeEventListener("pointerdown", listenForPointerDown); // only want one!
    bloomPage.addEventListener("pointerdown", listenForPointerDown);
}
// If the mouse down is in the thumb of a NiceScroll, we don't want to get a click
// event later even if the mouse up is outside that element.
// This function was copied from bloom-player with minimal changes and should be in a common library.
function listenForPointerDown(ev: PointerEvent) {
    if (
        ev.target instanceof HTMLDivElement &&
        (ev.target as HTMLDivElement).classList.contains("nicescroll-cursors")
    ) {
        (ev.target as HTMLDivElement).setPointerCapture(ev.pointerId);
    }
}
// This function was copied from bloom-player with minimal changes and should be in a common library.
export function cleanupNiceScroll() {
    // Doing this cleanup is unfortunate overhead, but niceScrolls stick around too much,
    // including when the page divs they are on are removed because the page is not the
    // current page. This leads to performance issues, including the scrollbar getting darker
    // and darker as the nicescroll elements build up in the HTML.  This may also be the source
    // of the event listener leaks that was mentioned in an earlier comment.
    $("div.bloom-page")[0]
        ?.querySelectorAll(kSelectorForPotentialNiceScrollElements)
        .forEach(group => {
            // The "as" cast is crucial here for this code to work.  For some reason,
            // the type returned by getNiceScroll() is not interpreted correctly and
            // the code silently fails to work, with length always being 0.
            // (bloom-player uses "as any" in this context, but "as JQuery" seems to
            // work as well and doesn't trigger an eslint warning.)
            const groupNiceScroll = ($(
                group
            ).getNiceScroll() as unknown) as JQuery;
            if (groupNiceScroll && groupNiceScroll.length > 0) {
                groupNiceScroll.remove();
            }
            // Remove classes added to make the niceScroll work, and restore
            // classes that were removed to make the niceScroll work.
            if (group.classList.contains("scrolling-bubble")) {
                group.classList.remove("scrolling-bubble");
            }
            const groupParent = group.parentElement;
            if (!groupParent) return; // this should never happen, but just in case
            if (
                groupParent.classList.contains(
                    "bloom-vertical-align-center-removed"
                )
            ) {
                groupParent.classList.remove(
                    "bloom-vertical-align-center-removed"
                );
                groupParent.classList.add("bloom-vertical-align-center");
            }
            if (
                groupParent.classList.contains(
                    "bloom-vertical-align-bottom-removed"
                )
            ) {
                groupParent.classList.remove(
                    "bloom-vertical-align-bottom-removed"
                );
                groupParent.classList.add("bloom-vertical-align-bottom");
            }
            // Remove more debris left by niceScroll. See BL-14052.
            (group as HTMLElement).style.overflow = "";
            (group as HTMLElement).style.outline = "";
            (group as HTMLElement).style.width = "";
        });
}
