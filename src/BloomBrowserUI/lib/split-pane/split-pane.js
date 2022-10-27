/*!

Split Pane v0.4.0

Copyright (c) 2014 Simon Hagström

Released under the MIT license
https://raw.github.com/shagstrom/split-pane/master/LICENSE

*/

import { BloomApi } from "../../utils/bloomApi";

(function($) {
    $.fn.splitPane = function() {
        var $splitPanes = this;
        $splitPanes.each(setMinHeightAndMinWidth);
        $splitPanes.append('<div class="split-pane-resize-shim">');
        $splitPanes
            .children(".split-pane-divider")
            .bind("mousedown touchstart", mousedownHandler);
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

    function mousedownHandler(event) {
        event.preventDefault();
        var isTouchEvent = event.type.match(/^touch/),
            moveEvent = isTouchEvent ? "touchmove" : "mousemove",
            endEvent = isTouchEvent ? "touchend" : "mouseup",
            $divider = $(this),
            $splitPane = $divider.parent(),
            $resizeShim = $divider.siblings(".split-pane-resize-shim");
        $resizeShim.show();
        $divider.addClass("dragged");
        if (isTouchEvent) {
            $divider.addClass("touch");
        }
        $(document).on(
            moveEvent,
            createMousemove($splitPane, pageXof(event), pageYof(event))
        );
        $(document).one(endEvent, function(event) {
            $(document).unbind(moveEvent);
            $divider.removeClass("dragged touch");
            $resizeShim.hide();
        });
    }

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
                const bottom = Math.min(
                    Math.max(
                        lastComponentMinHeight,
                        bottomOffset - pageYof(event) / pageScale
                    ),
                    maxLastComponentHeight
                );
                const amount1 = (bottom / splitPaneHeight) * 100;
                const defaultLabel = amountForDisplay(amount1) + "%";
                const [amount, label] = snapTo(
                    amount1,
                    defaultLabel,
                    splitPaneHeight
                );
                divider.title = label;
                setBottom(firstComponent, divider, lastComponent, amount + "%");
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
            return function(event) {
                event.preventDefault();
                const right = Math.min(
                    Math.max(
                        lastComponentMinWidth,
                        rightOffset - pageXof(event) / pageScale
                    ),
                    maxLastComponentWidth
                );
                const amount = (right / splitPaneWidth) * 100;
                setDividerTitle(divider, amount);
                setRight(firstComponent, divider, lastComponent, amount + "%");
                $splitPane.resize();
            };
        }
    }

    function makeSnaps(splitPane) {
        snaps = [];
        const firstChild = splitPane.firstElementChild;
        const firstChildSplit = getImagePercent(
            splitPane,
            firstChild,
            true,
            false
        );
        if (firstChildSplit > 0) {
            snaps.push({
                snap: firstChildSplit,
                label: "Matches image proportion"
            });
            snaps.push({
                snap: getImagePercent(splitPane, firstChild, true, true),
                label: "Square"
            });
        }

        const lastChild = splitPane.lastElementChild;
        const lastChildSplit = getImagePercent(
            splitPane,
            lastChild,
            false,
            false
        );
        if (lastChildSplit > 0) {
            snaps.push({
                snap: 100 - lastChildSplit,
                label: "Matches image proportion"
            });
            snaps.push({
                snap: 100 - getImagePercent(splitPane, firstChild, false, true),
                label: "Square"
            });
        }

        const page = splitPane.closest(".bloom-page");
        const id = page.getAttribute("id");
        BloomApi.get("pageList/prevPageSplit?id=" + id, result => {
            if (!result.data || result.data === "none") {
                return;
            }
            snaps.push({
                snap: 100 - parseFloat(result.data),
                label: "Matches previous page"
            });
        });
    }

    // If child is a split-pane-component containing a split-pane-container-inner
    // containing exactly one bloom-imageContainer
    // containing a picture, return the fraction of the height of splitPane which would result
    // in the image fitting perfectly. Otherwise, return -1.
    function getImagePercent(splitPane, component, topPane, square) {
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
        const width = splitPane.offsetWidth;
        let height = square
            ? width
            : (width * img.naturalHeight) / img.naturalWidth;
        if (topPane) {
            // 3px of margin on top pane in split that we need to leave room for
            height += 3;
        }
        return (height * 100) / splitPane.offsetHeight;
    }

    // Snaps are defined in terms of the percent the user sees, the percent of the upper
    // partition. Note that the 'amount' values taken and returned by snapTo are
    // instead the bottom partition.
    let snaps = [
        { snap: 25, label: "quarter" },
        { snap: 50, label: "half" },
        { snap: 75, label: "three-quarter" }
    ];

    function snapTo(amount, defaultLabel, parentHeight) {
        // We want to be within 4px.
        // amount and snaps are percentages of parentHeight
        const amountPx = (amount * parentHeight) / 100;
        for (let i = 0; i < snaps.length; i++) {
            const snapPx = ((100 - snaps[i].snap) * parentHeight) / 100;
            const delta = amountPx - snapPx;
            // If the mouse is below the splitter, the drag cursor goes away
            // and if there is an image below it the hover effects for the image
            // show up. But it can be quite some distance above before this happens.
            // We don't want that effect while dragging; we want to only snap
            // to a position that won't trigger the cursor being treated as outside
            // the splitter. So we snap in the range from just below to well above.
            if (delta < 10 && delta > -1) {
                const adjusted = 100 - snaps[i].snap;
                return [
                    adjusted,
                    snaps[i].label + " " + amountForDisplay(adjusted) + "%"
                ];
            }
        }
        return [amount, defaultLabel];
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
    function amountForDisplay(amount) {
        return Math.round(10 * (100 - amount)) / 10;
    }

    function setDividerTitle(divider, amount) {
        // for displaying to the user, invert percentage (so higher  up the page is a lower number) leave only one decimal place
        divider.title = amountForDisplay(amount) + "%";
    }
})(jQuery);
