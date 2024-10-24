/* eslint-disable no-redeclare */
/* eslint-disable @typescript-eslint/no-this-alias */
/* eslint-disable no-var */
/* eslint-disable no-undef */
/*!

Split Pane v0.4.0

Copyright (c) 2014 Simon Hagström

Released under the MIT license
https://raw.github.com/shagstrom/split-pane/master/LICENSE

*/

import { get } from "../../utils/bloomApi";
import theOneLocalizationManager from "../localizationManager/localizationManager";
import { EditableDivUtils } from "../../bookEdit/js/editableDivUtils";

(function($) {
    $.fn.splitPane = function() {
        var $splitPanes = this;
        $splitPanes.each(setMinHeightAndMinWidth);
        $splitPanes.append('<div class="split-pane-resize-shim">');
        $splitPanes
            .children(".split-pane-divider")
            .bind("mousedown touchstart", mousedownHandler);
        $splitPanes
            .children(".split-pane-divider")
            .bind("mouseenter", mouseenterHandler);
        $splitPanes
            .children(".split-pane-divider")
            .bind("dblclick", mousedblclickHandler);
        setTimeout(function() {
            // Doing this later because of an issue with Chrome (v23.0.1271.64) returning split-pane width = 0
            // and triggering multiple resize events when page is being opened from an <a target="_blank"> .
            $splitPanes.each(function() {
                $(this).bind(
                    "_splitpaneparentresize",
                    createParentresizeHandler($(this))
                );
            });
            $(window).trigger("resize");
        }, 100);
    };

    var SPLITPANERESIZE_HANDLER = "_splitpaneparentresizeHandler";

    /**
     * A special event that will "capture" a resize event from the parent split-pane or window.
     * The event will NOT propagate to grandchildren.
     */
    jQuery.event.special._splitpaneparentresize = {
        setup: function(data, namespaces) {
            var element = this,
                parent =
                    $(this)
                        .parent()
                        .closest(".split-pane")[0] || window;
            $(this).data(SPLITPANERESIZE_HANDLER, function(event) {
                var target = event.target === document ? window : event.target;
                if (target === parent) {
                    event.type = "_splitpaneparentresize";
                    jQuery.event.dispatch.apply(element, arguments);
                } else {
                    event.stopPropagation();
                }
            });
            $(parent).bind("resize", $(this).data(SPLITPANERESIZE_HANDLER));
        },
        teardown: function(namespaces) {
            var parent =
                $(this)
                    .parent()
                    .closest(".split-pane")[0] || window;
            $(parent).unbind("resize", $(this).data(SPLITPANERESIZE_HANDLER));
            $(this).removeData(SPLITPANERESIZE_HANDLER);
        }
    };

    function setMinHeightAndMinWidth() {
        var $splitPane = $(this),
            $firstComponent = $splitPane.children(
                ".split-pane-component:first"
            ),
            $divider = $splitPane.children(".split-pane-divider"),
            $lastComponent = $splitPane.children(".split-pane-component:last");
        if ($splitPane.is(".fixed-top, .fixed-bottom, .horizontal-percent")) {
            $splitPane.css(
                "min-height",
                minHeight($firstComponent) +
                    minHeight($lastComponent) +
                    $divider.height() +
                    "px"
            );
        } else {
            $splitPane.css(
                "min-width",
                minWidth($firstComponent) +
                    minWidth($lastComponent) +
                    $divider.width() +
                    "px"
            );
        }
    }

    // mousedownHandler variables that need to be accessible to mousemoveHandler or mouseUpHandler
    let moveFunction = null;
    let moveEvent;
    let $divider;
    let $resizeShim;

    function mousedownHandler(event) {
        event.preventDefault();
        $divider = $(this);
        var isTouchEvent = event.type.match(/^touch/),
            endEvent = isTouchEvent ? "touchend" : "mouseup",
            $splitPane = $divider.parent();

        $resizeShim = $divider.siblings(".split-pane-resize-shim");
        moveEvent = isTouchEvent ? "touchmove" : "mousemove";
        $resizeShim.show();
        $divider.addClass("dragged");
        if (isTouchEvent) {
            $divider.addClass("touch");
        }
        document.body.classList.add("origami-drag");
        if (isHorizontal(this)) {
            document.body.classList.add("origami-drag-horizontal");
        } else {
            document.body.classList.add("origami-drag-vertical");
        }
        moveFunction = createMousemove(
            $splitPane,
            pageXof(event),
            pageYof(event)
        );
        document.addEventListener(moveEvent, moveFunction, { capture: true });
        document.addEventListener(endEvent, mouseUpHandler, {
            capture: true,
            once: true
        });
    }

    function mouseUpHandler(event) {
        document.removeEventListener(moveEvent, moveFunction, {
            capture: true
        });
        $divider.removeClass("dragged touch");
        $resizeShim.hide();
        document.body.classList.remove("origami-drag");
    }

    function mouseenterHandler(event) {
        if (event.buttons != 0) {
            return; // typically drag in progress
        }
        const divider = event.currentTarget;
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Snap.Hint",
                "CTRL for precision. Double click to match previous page."
            )
            .done(result => {
                divider.title = result;
            });
        if (isSnappableSplitter(divider)) {
            makeSnaps(divider.parentElement, () => {
                // The stylesheet sets the position at 50%,
                // so if the element doesn't have it set explicitly as an override,
                // it will be at 50%.
                let currentOffset = 50;

                const dividerStyle = divider.getAttribute("style");
                if (dividerStyle) {
                    const regex = isHorizontal(divider)
                        ? /bottom: ([0-9.]*)%/
                        : /right: ([0-9.]*)%/;
                    const match = dividerStyle.match(regex);
                    if (match) {
                        currentOffset = parseFloat(match[1]);
                    }
                }
                const defaultLabel =
                    dividerPositionForDisplay(currentOffset) + "%";
                // In mouse enter, we'll always show a snap if we are exactly at it.
                preciseMode = false;
                const [snappedOffset, label, isSnapped] = snapTo(
                    currentOffset,
                    defaultLabel,
                    divider.parentElement.offsetHeight
                );
                let finalLabel = label;
                if (
                    isSnapped &&
                    Math.abs(snappedOffset - currentOffset) > 0.1
                ) {
                    // This position would snap, but not to where the divider now is.
                    // For hover effect it's better not to show as snapped.
                    finalLabel = defaultLabel;
                }
                divider.setAttribute("data-splitter-label", finalLabel);
            });
        }
    }

    function isSnappableSplitter(divider) {
        const classes = divider.parentElement.classList;
        return (
            classes.contains("horizontal-percent") ||
            classes.contains("vertical-percent")
        );
    }

    function mousedblclickHandler(event) {
        const divider = event.currentTarget;

        if (isSnappableSplitter(divider)) {
            makeSnaps(divider.parentElement, () => {
                const prevPageSplit = snapPoints.find(
                    x => x.id === "MatchPreviousPage"
                );
                if (prevPageSplit) {
                    // This code is similar enough to parts of createMouseMove to be annoying,
                    // but just different enough to make it difficult to pull out the bits we need.
                    const $splitPane = $(divider.parentElement);
                    const firstComponent = $splitPane.children(
                        ".split-pane-component:first"
                    )[0];
                    const lastComponent = $splitPane.children(
                        ".split-pane-component:last"
                    )[0];
                    divider.classList.add("snapped");

                    divider.setAttribute(
                        "data-splitter-label",
                        prevPageSplit.label
                    );
                    if (isHorizontal(divider)) {
                        setBottom(
                            firstComponent,
                            divider,
                            lastComponent,
                            100 - prevPageSplit.snap + "%"
                        );
                    } else {
                        setRight(
                            firstComponent,
                            divider,
                            lastComponent,
                            100 - prevPageSplit.snap + "%"
                        );
                    }
                    $splitPane.resize();
                }
            });
        }
    }
    // True if ctrl was held down when clicking the splitter. Disables snapping
    // and displays measurements more precisely.
    let preciseMode = false;

    function createParentresizeHandler($splitPane) {
        var splitPane = $splitPane[0],
            firstComponent = $splitPane.children(
                ".split-pane-component:first"
            )[0],
            divider = $splitPane.children(".split-pane-divider")[0],
            lastComponent = $splitPane.children(
                ".split-pane-component:last"
            )[0];
        if ($splitPane.is(".fixed-top")) {
            var lastComponentMinHeight = minHeight(lastComponent);
            return function(event) {
                var maxfirstComponentHeight =
                    splitPane.offsetHeight -
                    lastComponentMinHeight -
                    divider.offsetHeight;
                if (firstComponent.offsetHeight > maxfirstComponentHeight) {
                    setTop(
                        firstComponent,
                        divider,
                        lastComponent,
                        maxfirstComponentHeight + "px"
                    );
                }
                $splitPane.resize();
            };
        } else if ($splitPane.is(".fixed-bottom")) {
            var firstComponentMinHeight = minHeight(firstComponent);
            return function(event) {
                var maxLastComponentHeight =
                    splitPane.offsetHeight -
                    firstComponentMinHeight -
                    divider.offsetHeight;
                if (lastComponent.offsetHeight > maxLastComponentHeight) {
                    setBottom(
                        firstComponent,
                        divider,
                        lastComponent,
                        maxLastComponentHeight + "px"
                    );
                }
                $splitPane.resize();
            };
        } else if ($splitPane.is(".horizontal-percent")) {
            var lastComponentMinHeight = minHeight(lastComponent),
                firstComponentMinHeight = minHeight(firstComponent);
            return function(event) {
                var maxLastComponentHeight =
                    splitPane.offsetHeight -
                    firstComponentMinHeight -
                    divider.offsetHeight;
                if (lastComponent.offsetHeight > maxLastComponentHeight) {
                    setBottom(
                        firstComponent,
                        divider,
                        lastComponent,
                        (maxLastComponentHeight / splitPane.offsetHeight) *
                            100 +
                            "%"
                    );
                } else {
                    if (
                        splitPane.offsetHeight -
                            firstComponent.offsetHeight -
                            divider.offsetHeight <
                        lastComponentMinHeight
                    ) {
                        setBottom(
                            firstComponent,
                            divider,
                            lastComponent,
                            (lastComponentMinHeight / splitPane.offsetHeight) *
                                100 +
                                "%"
                        );
                    }
                }
                $splitPane.resize();
            };
        } else if ($splitPane.is(".fixed-left")) {
            var lastComponentMinWidth = minWidth(lastComponent);
            return function(event) {
                var maxFirstComponentWidth =
                    splitPane.offsetWidth -
                    lastComponentMinWidth -
                    divider.offsetWidth;
                if (firstComponent.offsetWidth > maxFirstComponentWidth) {
                    setLeft(
                        firstComponent,
                        divider,
                        lastComponent,
                        maxFirstComponentWidth + "px"
                    );
                }
                $splitPane.resize();
            };
        } else if ($splitPane.is(".fixed-right")) {
            var firstComponentMinWidth = minWidth(firstComponent);
            return function(event) {
                var maxLastComponentWidth =
                    splitPane.offsetWidth -
                    firstComponentMinWidth -
                    divider.offsetWidth;
                if (lastComponent.offsetWidth > maxLastComponentWidth) {
                    setRight(
                        firstComponent,
                        divider,
                        lastComponent,
                        maxLastComponentWidth + "px"
                    );
                }
                $splitPane.resize();
            };
        } else if ($splitPane.is(".vertical-percent")) {
            var lastComponentMinWidth = minWidth(lastComponent),
                firstComponentMinWidth = minWidth(firstComponent);
            return function(event) {
                var maxLastComponentWidth =
                    splitPane.offsetWidth -
                    firstComponentMinWidth -
                    divider.offsetWidth;
                if (lastComponent.offsetWidth > maxLastComponentWidth) {
                    setRight(
                        firstComponent,
                        divider,
                        lastComponent,
                        (maxLastComponentWidth / splitPane.offsetWidth) * 100 +
                            "%"
                    );
                } else {
                    if (
                        splitPane.offsetWidth -
                            firstComponent.offsetWidth -
                            divider.offsetWidth <
                        lastComponentMinWidth
                    ) {
                        setRight(
                            firstComponent,
                            divider,
                            lastComponent,
                            (lastComponentMinWidth / splitPane.offsetWidth) *
                                100 +
                                "%"
                        );
                    }
                }
                $splitPane.resize();
            };
        }
    }

    function createMousemove($splitPane, pageX, pageY) {
        const pageScale =
            $splitPane[0].getBoundingClientRect().width /
            $splitPane[0].offsetWidth;
        const scaledX = pageX / pageScale;
        const scaledY = pageY / pageScale;
        const splitPane = $splitPane[0],
            firstComponent = $splitPane.children(
                ".split-pane-component:first"
            )[0],
            divider = $splitPane.children(".split-pane-divider")[0],
            lastComponent = $splitPane.children(
                ".split-pane-component:last"
            )[0];
        if ($splitPane.is(".fixed-top")) {
            const firstComponentMinHeight = minHeight(firstComponent),
                maxFirstComponentHeight =
                    splitPane.offsetHeight -
                    minHeight(lastComponent) -
                    divider.offsetHeight,
                topOffset = divider.offsetTop - scaledY;
            return function(event) {
                event.preventDefault();
                if (event.buttons === 0) {
                    // somehow we missed the mouseup event, so clean up
                    mouseUpHandler(event);
                    return;
                }
                const top = Math.min(
                    Math.max(
                        firstComponentMinHeight,
                        topOffset + pageYof(event) / pageScale
                    ),
                    maxFirstComponentHeight
                );
                setTop(firstComponent, divider, lastComponent, top + "px");
                $splitPane.resize();
            };
        } else if ($splitPane.is(".fixed-bottom")) {
            const lastComponentMinHeight = minHeight(lastComponent),
                maxLastComponentHeight =
                    splitPane.offsetHeight -
                    minHeight(firstComponent) -
                    divider.offsetHeight,
                bottomOffset = lastComponent.offsetHeight + scaledY;
            return function(event) {
                event.preventDefault();
                if (event.buttons === 0) {
                    // somehow we missed the mouseup event, so clean up
                    mouseUpHandler(event);
                    return;
                }
                const bottom = Math.min(
                    Math.max(
                        lastComponentMinHeight,
                        bottomOffset - pageYof(event) / pageScale
                    ),
                    maxLastComponentHeight
                );
                setBottom(
                    firstComponent,
                    divider,
                    lastComponent,
                    bottom + "px"
                );
                $splitPane.resize();
            };
        } else if ($splitPane.is(".horizontal-percent")) {
            const splitPaneHeight = splitPane.offsetHeight,
                lastComponentMinHeight = minHeight(lastComponent),
                maxLastComponentHeight =
                    splitPaneHeight -
                    minHeight(firstComponent) -
                    divider.offsetHeight,
                bottomOffset = lastComponent.offsetHeight + scaledY;
            makeSnaps(splitPane);
            return function(event) {
                event.preventDefault();
                preciseMode = event.ctrlKey;
                const bottom = Math.min(
                    Math.max(
                        lastComponentMinHeight,
                        bottomOffset - pageYof(event) / pageScale
                    ),
                    maxLastComponentHeight
                );
                const unsnappedOffset = (bottom / splitPaneHeight) * 100;
                const defaultLabel =
                    dividerPositionForDisplay(unsnappedOffset) + "%";
                const [adjustedOffset, label, isSnapped] = snapTo(
                    unsnappedOffset,
                    defaultLabel,
                    splitPaneHeight
                );
                if (isSnapped) {
                    divider.classList.add("snapped");
                } else divider.classList.remove("snapped");

                divider.setAttribute("data-splitter-label", label);
                setBottom(
                    firstComponent,
                    divider,
                    lastComponent,
                    adjustedOffset + "%"
                );
                $splitPane.resize();
            };
        } else if ($splitPane.is(".fixed-left")) {
            const firstComponentMinWidth = minWidth(firstComponent),
                maxFirstComponentWidth =
                    splitPane.offsetWidth -
                    minWidth(lastComponent) -
                    divider.offsetWidth,
                leftOffset = divider.offsetLeft - scaledX;
            return function(event) {
                event.preventDefault();
                const left = Math.min(
                    Math.max(
                        firstComponentMinWidth,
                        leftOffset + pageXof(event) / pageScale
                    ),
                    maxFirstComponentWidth
                );
                setLeft(firstComponent, divider, lastComponent, left + "px");
                $splitPane.resize();
            };
        } else if ($splitPane.is(".fixed-right")) {
            const lastComponentMinWidth = minWidth(lastComponent),
                maxLastComponentWidth =
                    splitPane.offsetWidth -
                    minWidth(firstComponent) -
                    divider.offsetWidth,
                rightOffset = lastComponent.offsetWidth + scaledX;
            return function(event) {
                event.preventDefault();
                const right = Math.min(
                    Math.max(
                        lastComponentMinWidth,
                        rightOffset - pageXof(event) / pageScale
                    ),
                    maxLastComponentWidth
                );
                setRight(firstComponent, divider, lastComponent, right + "px");
                $splitPane.resize();
            };
        } else if ($splitPane.is(".vertical-percent")) {
            const splitPaneWidth = splitPane.offsetWidth,
                lastComponentMinWidth = minWidth(lastComponent),
                maxLastComponentWidth =
                    splitPaneWidth -
                    minWidth(firstComponent) -
                    divider.offsetWidth,
                rightOffset = lastComponent.offsetWidth + scaledX;
            makeSnaps(splitPane);
            return function(event) {
                event.preventDefault();
                preciseMode = event.ctrlKey;
                const right = Math.min(
                    Math.max(
                        lastComponentMinWidth,
                        rightOffset - pageXof(event) / pageScale
                    ),
                    maxLastComponentWidth
                );
                const unsnappedOffset = (right / splitPaneWidth) * 100;
                const defaultLabel =
                    dividerPositionForDisplay(unsnappedOffset) + "%";
                const [adjustedOffset, label, isSnapped] = snapTo(
                    unsnappedOffset,
                    defaultLabel,
                    splitPaneWidth
                );
                if (isSnapped) {
                    divider.classList.add("snapped");
                } else divider.classList.remove("snapped");

                divider.setAttribute("data-splitter-label", label);
                setRight(
                    firstComponent,
                    divider,
                    lastComponent,
                    adjustedOffset + "%"
                );
                $splitPane.resize();
            };
        }
    }

    // Make the snap points that should be used for the specified pane.
    // Invokes the callback (if any) when all snap points have been added.
    // (Some require async calls before they can be computed.)
    // (In an ideal world, we'd do this with promise or async, but BloomApi.get
    // doesn't use that so it's easier just to pass a callback.)
    // If you add more, think about order. The first one that we are close enough
    // to for a snap wins over any others that may also be in range.
    // Current order is aspect ratio, previous page, square, fixed fractions.
    // Callback is called (asynchronously) when we have finished making the snaps.
    function makeSnaps(splitPane, callback) {
        snapPoints = [];
        const firstChild = splitPane.firstElementChild;
        const firstChildSplit = getImagePercent(
            splitPane,
            firstChild,
            true,
            false
        );
        let snapsAwaited = 0; // increment each time we call makeSnapPoint and pass localCallback
        // Invoke callback when we have all the snap positions and labels.
        const localCallback = () => {
            snapsAwaited--;
            if (snapsAwaited == 0) {
                callback();
            }
        };
        if (firstChildSplit > 0) {
            snapsAwaited += 2;
            makeSnapPoint(
                firstChildSplit,
                "MatchImageAspect",
                "Fit image",
                "",
                -1,
                localCallback
            );
            makeSnapPoint(
                getImagePercent(splitPane, firstChild, true, true),
                "Square",
                "Square",
                "",
                -1,
                localCallback
            );
        }

        const lastChild = splitPane.lastElementChild;
        const lastChildSplit = getImagePercent(
            splitPane,
            lastChild,
            false,
            false
        );
        if (lastChildSplit > 0) {
            snapsAwaited += 2;
            makeSnapPoint(
                100 - lastChildSplit,
                "MatchImageAspect",
                "Fit image",
                "",
                0, // makes it before the previous 'square' if any; arbitrary whether before previous match aspect
                localCallback
            );
            // Make the bottom panel square
            makeSnapPoint(
                100 - getImagePercent(splitPane, firstChild, false, true),
                "Square",
                "Square",
                "",
                -1,
                localCallback
            );
        }

        const page = splitPane.closest(".bloom-page");
        const id = page.getAttribute("id");
        // capture the place to insert this now, before we add the general ones.
        // We want it after any aspect-ratio matches but before the square matches.
        // This divide by 2 gives us zero if there are no adjacent images, 1 if there is
        // just one, and 2 if there are two, which in each case is a position just after
        // the aspect ratio options, if any.
        const indexToInsertPrevPageSnap = snapPoints.length / 2;
        const orientation = isPaneHorizontal(splitPane)
            ? "horizontal"
            : "vertical";
        get(
            "editView/prevPageSplit?id=" + id + "&orientation=" + orientation,
            result => {
                // We should get the result before significant mouse movement happens.
                if (!result.data || result.data === "none") {
                    if (callback) callback();
                    return;
                }
                // Wants to be inserted at the current length, even though this will happen later
                // when the promise is fulfilled. (We want this to have less priority than
                // matching the aspect ratio, but more than matching one of the arbitrary splits.)
                snapsAwaited++;
                makeSnapPoint(
                    100 - parseFloat(result.data),
                    "MatchPreviousPage", // note: ID also used to find in list of snap points
                    "Matches previous page",
                    "🠈",
                    indexToInsertPrevPageSnap,
                    localCallback
                );
            }
        );

        // These general purpose ones come last, so the others win if there is overlap.
        // We're not localizing these, so don't need to pass a callback.
        makeSnapPoint(25, undefined, "¼");
        makeSnapPoint(33.333333, undefined, "⅓");
        makeSnapPoint(50, undefined, "½");
        makeSnapPoint(66.666667, undefined, "⅔");
        makeSnapPoint(75, undefined, "¾");
    }

    function makeSnapPoint(
        snapPoint,
        localizationId = null,
        label = "",
        prefixSymbol = "", // this is used to hold a unicode left-arrow that we don't want translators touching
        index = -1, // where to insert, or -1 for end
        callback // called when we have the final (possibly localized) label.
    ) {
        const item = { snap: snapPoint, label, id: localizationId };
        if (index >= 0) {
            snapPoints.splice(index, 0, item);
        } else {
            snapPoints.push(item);
        }

        if (localizationId)
            theOneLocalizationManager
                .asyncGetText("EditTab.Snap." + localizationId, label)
                .done(result => {
                    item.label = prefixSymbol + " " + result;
                    if (callback) callback();
                });
        else {
            item.label = [prefixSymbol, label].join(" ");
            if (callback) callback();
        }
    }

    function isHorizontal(divider) {
        return divider.classList.contains("horizontal-divider");
    }

    function isPaneHorizontal(splitPane) {
        const divider = splitPane.getElementsByClassName(
            "split-pane-divider"
        )[0];
        return isHorizontal(divider);
    }
    // If child is a split-pane-component containing a split-pane-container-inner
    // containing exactly one bloom-imageContainer
    // containing a picture, return the fraction of the height of splitPane which would result
    // in the image fitting perfectly. Otherwise, return -1.
    function getImagePercent(
        splitPane,
        component,
        isForFirstChildPane,
        isForSquareSplit
    ) {
        if (
            !component ||
            !component.classList.contains("split-pane-component")
        ) {
            return -1;
        }
        const child = component.firstElementChild;
        if (!child || !child.classList.contains("split-pane-component-inner")) {
            return -1;
        }
        const imageContainer = child.firstElementChild;
        if (
            !imageContainer ||
            !imageContainer.classList.contains("bloom-imageContainer")
        ) {
            return -1;
        }
        const img = Array.from(imageContainer.children).filter(
            c => c.nodeName === "IMG"
        )[0];
        if (!img) {
            return -1;
        }
        const horizontal = isPaneHorizontal(splitPane);
        const splitPaneComponent = img.closest(".split-pane-component"); // the element that has the percent
        const scale = EditableDivUtils.getPageScale();

        if (horizontal) {
            const width = splitPane.offsetWidth;
            const height = isForSquareSplit
                ? width
                : (width * img.naturalHeight) / img.naturalWidth;
            // At some point we apparently had 3px of margin on the top pane, possibly something to do
            // with the splitter control. Somewhere about 6.0 we lost it, so this correction is no longer needed.
            // I'm leaving it here commented out in case losing that margin was a mistake and we also need
            // to add it back here. (Also below).
            // if (isForFirstChildPane) {
            //     height += 3;
            // }
            // This compensates for any padding between the element on which we set the percent
            // and the image container whose height we are setting.
            // (Apart from the immediate child, it would also handle margin, but we're not
            // currently using margin to space things.)
            // (This assumes padding is set with some absolute unit, not a percentage. If the padding is a percentage,
            // this won't work quite right, though repeated attempts may improve things. Unfortunately there's no
            // obvious way even to detect percentage padding, let alone to handle it.)
            let extraHeight = 0;
            if (splitPaneComponent) {
                extraHeight =
                    (splitPaneComponent.offsetHeight -
                        imageContainer.offsetHeight) /
                    scale;
            }
            return ((height + extraHeight) * 100) / splitPane.offsetHeight;
        } else {
            const height = splitPane.offsetHeight;
            const width = isForSquareSplit
                ? height
                : (height * img.naturalWidth) / img.naturalHeight;
            // See comment above in horizontal block.
            // if (isForFirstChildPane) {
            //     width += 3;
            // }
            // This compensates for any padding between the element on which we set the percent
            // and the image container whose width we are setting. See more detailed comment on the height version.
            let extraWidth = 0;
            if (splitPaneComponent) {
                extraWidth =
                    (splitPaneComponent.offsetWidth -
                        imageContainer.offsetWidth) /
                    scale;
            }
            return ((width + extraWidth) * 100) / splitPane.offsetWidth;
        }
    }

    // Snaps are defined in terms of the percent the user sees, the percent of the upper
    // partition. Note that the 'offset' values taken and returned by snapTo are
    // instead the bottom partition. (We don't use the intial value, but it sets a type.)
    let snapPoints = [{ snap: 50, label: "half", id: "Half" }];

    function snapTo(unsnappedOffset, defaultLabel, parentSize) {
        if (preciseMode) {
            return [unsnappedOffset, defaultLabel, false];
        }
        // amount and snaps are percentages of parentHeight
        const offsetPx = (unsnappedOffset * parentSize) / 100;
        for (let i = 0; i < snapPoints.length; i++) {
            const snapPx = ((100 - snapPoints[i].snap) * parentSize) / 100;
            const delta = offsetPx - snapPx;
            // The actual dividiing line...the point that is, for example, 1/3 of the
            // distance from the top of the parent to the bottom, which we are actually
            // comparing to the mouse pointer position...is the bottom of
            // the visible splitter. So it looks more uniform if snapping takes effect
            // a little further above the line than below it.
            const splitCushion = 15;
            const extraSplitCushionAbove = 6;
            if (
                delta < splitCushion + extraSplitCushionAbove &&
                delta > -splitCushion
            ) {
                const adjusted = 100 - snapPoints[i].snap;

                // for simple snap points like "33%", "%50%" etc we don't have a label
                const labelAndNewline = snapPoints[i].label + "\x0a";

                return [
                    adjusted,
                    // true argument makes it show high precision. When snapped, we think
                    // this is useful, e.g., to see "66.7" rather than just 67, or to see
                    // more precisily what the previous page or aspect ratio snaps did.
                    labelAndNewline +
                        dividerPositionForDisplay(adjusted, true) +
                        "%",
                    true
                ];
            }
        }
        // In the default, low-res mode, we always set the percentage to a whole number.
        const snapped = Math.round(unsnappedOffset);
        // We have done a little snapping, but for purposes like showing the brighter
        // line, I think it's better to consider this unsnapped.
        return [snapped, dividerPositionForDisplay(snapped, true) + "%", false];
    }

    function pageXof(event) {
        return event.pageX || event.originalEvent.pageX;
    }

    function pageYof(event) {
        return event.pageY || event.originalEvent.pageY;
    }

    function minHeight(element) {
        return parseInt($(element).css("min-height")) || 0;
    }

    function minWidth(element) {
        return parseInt($(element).css("min-width")) || 0;
    }

    function setTop(firstComponent, divider, lastComponent, top) {
        firstComponent.style.height = top;
        divider.style.top = top;
        lastComponent.style.top = top;
    }

    function setBottom(firstComponent, divider, lastComponent, bottom) {
        firstComponent.style.bottom = bottom;
        divider.style.bottom = bottom;
        lastComponent.style.height = bottom;
    }

    function setLeft(firstComponent, divider, lastComponent, left) {
        firstComponent.style.width = left;
        divider.style.left = left;
        lastComponent.style.left = left;
    }

    function setRight(firstComponent, divider, lastComponent, right) {
        firstComponent.style.right = right;
        divider.style.right = right;
        lastComponent.style.width = right;
    }
    function dividerPositionForDisplay(amount, snapped) {
        // for displaying to the user, invert percentage (so higher  up the page is a lower number).
        // Leave one decimal place in precise mode or for a snap result, none otherwise
        if (preciseMode || snapped) return Math.round(10 * (100 - amount)) / 10;
        return Math.round(100 - amount);
    }

    function setDividerTitle(divider, amount) {
        divider.setAttribute(
            "data-splitter-label",
            dividerPositionForDisplay(amount) + "%"
        );
    }
})(jQuery);
