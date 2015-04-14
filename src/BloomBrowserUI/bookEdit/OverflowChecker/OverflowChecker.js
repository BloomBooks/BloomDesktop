/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../StyleEditor/StyleEditor.ts" />
var OverflowChecker = (function () {
    function OverflowChecker() {
    }
    // When a div is overfull, these handlers will add the overflow class so it gets a red background or something
    // But this function should just do some basic checks and ADD the HANDLERS!
    OverflowChecker.prototype.AddOverflowHandlers = function (container) {
        //NB: for some historical reason in March 2014 the calendar still uses textareas
        var queryElementsThatCanOverflow = ".bloom-editable, textarea";
        var editablePageElements = $(container).find(queryElementsThatCanOverflow);
        //first, check to see if the stylesheet is going to give us overflow even for a single character:
        editablePageElements.each(function () {
            OverflowChecker.CheckOnMinHeight(this);
        });
        //Add the handler so that when the elements change, we test for overflow
        editablePageElements.on("keyup paste", function (e) {
            var target = e.target;
            // Give the browser time to get the pasted text into the DOM first, before testing for overflow
            // GJM -- One place I read suggested that 0ms would work, it just needs to delay one 'cycle'.
            //        At first I was concerned that this might slow typing, but it doesn't seem to.
            setTimeout(function () {
                OverflowChecker.MarkOverflowInternal(target);
                //REVIEW: why is this here, in the overflow detection?
                // This will make sure that any language tags on this div stay in position with editing.
                // Reposition all language tips, not just the tip for this item because sometimes the edit moves other controls.
                $(queryElementsThatCanOverflow).qtip('reposition');
            }, 100); // 100 milliseconds
            e.stopPropagation();
        });
        // Add another handler so that when the user resizes an origami pane, we check the overflow again
        $(container).find(".split-pane-component-inner").bind('_splitpaneparentresize', function () {
            var $this = $(this);
            $this.find(queryElementsThatCanOverflow).each(function () {
                OverflowChecker.MarkOverflowInternal(this);
            });
        });
        // Right now, test to see if any are already overflowing
        editablePageElements.each(function () {
            OverflowChecker.MarkOverflowInternal(this);
        });
    };
    // Actual testable determination of Type I overflow or not
    // 'public' for testing (2 types of overflow are defined in MarkOverflowInternal below)
    OverflowChecker.IsOverflowingSelf = function (element) {
        // Ignore Topic divs as they are chosen from a list
        if (element.hasAttribute('data-book') && element.getAttribute('data-book') == "topic") {
            return false;
        }
        // If css has "overflow: visible;", scrollHeight is always 2 greater than clientHeight.
        // This is because of the thin grey border on a focused input box.
        // In fact, the focused grey border causes the same problem in detecting the bottom of a marginBox
        // so we'll apply the same 'fudge' factor to both comparisons.
        var focusedBorderFudgeFactor = 2;
        //The "basic book" template has a "Just Text" page which does some weird things to get vertically-centered
        //text. I don't know why, but this makes the clientHeight 2 pixels larger than the scrollHeight once it
        //is beyond its minimum height. We can detect that we're using this because it has this "firefoxHeight" data
        //element. This problem also shows up (and is detectable the same way) in Big Book. Except it turns out the
        //number of pixels to fudge is related to the point size. I think at base it's a preferred line spacing issue.
        var growFromCenterVerticalFudgeFactor = 0;
        if ($(element).data('firefoxheight')) {
            var fontSizeRemnant = new StyleEditor("/bloom/bookEdit").GetCalculatedFontSizeInPoints(element) - 22;
            if (fontSizeRemnant > 0) {
                growFromCenterVerticalFudgeFactor = (fontSizeRemnant / 5) + 1;
            }
        }
        //In the Picture Dictionary template, all words have a scrollHeight that is 3 greater than the client height.
        //In the Headers of the Term Intro of the SHRP C1 P3 Pupil's book, scrollHeight = clientHeight + 6!!! Sigh.
        // the focussedBorderFudgeFactor takes care of 2 pixels, this adds one more.
        var shortBoxFudgeFactor = 4;
        return element.scrollHeight > element.clientHeight + focusedBorderFudgeFactor + growFromCenterVerticalFudgeFactor + shortBoxFudgeFactor || element.scrollWidth > element.clientWidth + focusedBorderFudgeFactor;
    };
    // Actual testable determination of Type II overflow or not
    // 'public' for testing (2 types of overflow are defined in MarkOverflowInternal below)
    OverflowChecker.IsOverflowingMargins = function (element) {
        // Ignore Topic divs as they are chosen from a list
        if (element.hasAttribute('data-book') && element.getAttribute('data-book') == "topic") {
            return false;
        }
        // We want to prevent an inner div from expanding past the borders set by any containing marginBox class.
        var marginBoxParent = $(element).parents('.marginBox');
        var parentBottom;
        if (marginBoxParent && marginBoxParent.length > 0)
            parentBottom = $(marginBoxParent[0]).offset().top + $(marginBoxParent[0]).outerHeight(true);
        else
            parentBottom = 999999;
        var elemTop = $(element).offset().top;
        var elemBottom = elemTop + $(element).outerHeight(false);
        // console.log("Offset top: " + elemTop + " Outer Height: " + $(element).outerHeight(false));
        // If css has "overflow: visible;", scrollHeight is always 2 greater than clientHeight.
        // This is because of the thin grey border on a focused input box.
        // In fact, the focused grey border causes the same problem in detecting the bottom of a marginBox
        // so we'll apply the same 'fudge' factor to both comparisons.
        var focusedBorderFudgeFactor = 2;
        return elemBottom > parentBottom + focusedBorderFudgeFactor;
    };
    // Checks for overflow on a bloom-page and adds/removes the proper class
    // N.B. This function is specifically designed to be called from within AddOverflowHandler()
    // but is also called from within StyleEditor (and therefore public)
    OverflowChecker.MarkOverflowInternal = function (box) {
        // There are two types of overflow that we need to check.
        // 1-When we're called by a handler on an element, we need to check that that element
        // doesn't overflow internally (i.e. has too much stuff to fit in itself).
        // 2-We also need to check that this element and any OTHER elements on the page
        // haven't been pushed outside the margins
        // Type 1 Overflow
        var $box = $(box);
        $box.removeClass('overflow');
        if (OverflowChecker.IsOverflowingSelf(box) || OverflowChecker.HasImmediateSplitParentThatOverflows($box)) {
            $box.addClass('overflow');
        }
        var container = $box.closest('.marginBox');
        //NB: for some historical reason in March 2014 the calendar still uses textareas
        var queryElementsThatCanOverflow = ".bloom-editable, textarea";
        var editablePageElements = $(container).find(queryElementsThatCanOverflow);
        // Type 2 Overflow - We'll check ALL of these for overflow past marginBox
        editablePageElements.each(function () {
            var $this = $(this);
            if (OverflowChecker.IsOverflowingMargins($this[0])) {
                $box.addClass('overflow'); // probably typing in the focused element caused this
                $this.addClass('overflow'); // but it's this one that is actually overflowing
            }
            else {
                if (!OverflowChecker.IsOverflowingSelf($this[0]) && !OverflowChecker.HasImmediateSplitParentThatOverflows($this)) {
                    $this.removeClass('overflow'); // might be a remnant from earlier overflow
                }
            }
        });
        OverflowChecker.UpdatePageOverflow(container.closest('.bloom-page'));
    }; // end MarkOverflowInternal
    OverflowChecker.HasImmediateSplitParentThatOverflows = function (jQueryBox) {
        //now, thing is, while the text may fit in our box, our box may not fit our parent. Or grandparent, etc.
        //It could be that we could just do this on up the hierarchy? For now, here's the case we know is important,
        // in the Origami pages, where the space allocated could be too small.
        var splitterParents = jQueryBox.parents('.split-pane-component-inner');
        if (splitterParents.length == 0) {
            return false;
        }
        return OverflowChecker.IsOverflowingSelf(splitterParents[0]);
    };
    // Make sure there are no boxes with class 'overflow' on the page before removing
    // the page-level overflow marker 'pageOverflows', or add it if there are.
    OverflowChecker.UpdatePageOverflow = function (page) {
        var $page = $(page);
        if (!$page.find('.overflow').length)
            $page.removeClass('pageOverflows');
        else
            $page.addClass('pageOverflows');
    };
    // Checks a couple of situations where we might need to modify min-height
    // If necessary, this will do the modification
    OverflowChecker.CheckOnMinHeight = function (box) {
        var $box = $(box);
        var overflowy = $box.css("overflow-y");
        if (overflowy == 'hidden') {
            // On custom pages we hide overflow in the y direction. This sometimes shows a scroll bar.
            // It can show prematurely when there is only one line of text unless we force min-height
            // to be exactly line-height. I don't know why. See BL-1034 premature scroll bars
            // (Note: although line-height can have other units than min-height, the css function
            // (at least in FF) always returns px, so we can just copy it).
            $box.css("min-height", $box.css("line-height"));
        }
        else {
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
            $box.css('min-height', '');
            var lineHeight = parseFloat($box.css("line-height"));
            var minHeight = parseFloat($box.css("min-height"));
            // We do this comparison so that if the template designer has set a larger min-height,
            // we don't mess with it.
            if (minHeight < lineHeight) {
                $box.css("min-height", lineHeight + 0.01);
            }
        }
        // Remove any left-over warning about min-height is less than lineHeight (from earlier version of Bloom)
        $box.removeClass('Layout-Problem-Detected');
    }; // end CheckOnMinHeight
    return OverflowChecker;
})(); // end class OverflowChecker
//# sourceMappingURL=OverflowChecker.js.map