
var OverflowChecker = (function () {
    function OverflowChecker() {
    }
    OverflowChecker.prototype.AddOverflowHandlers = function (container) {
        var queryElementsThatCanOverflow = ".bloom-editable, textarea";
        var editablePageElements = $(container).find(queryElementsThatCanOverflow);

        editablePageElements.each(function () {
            OverflowChecker.CheckOnMinHeight(this);
        });

        editablePageElements.on("keyup paste", function (e) {
            var target = e.target;

            setTimeout(function () {
                OverflowChecker.MarkOverflowInternal(target);

                $(queryElementsThatCanOverflow).qtip('reposition');
            }, 100);
            e.stopPropagation();
        });

        $(container).find(".split-pane-component-inner").bind('_splitpaneparentresize', function () {
            var $this = $(this);
            $this.find(queryElementsThatCanOverflow).each(function () {
                OverflowChecker.MarkOverflowInternal(this);
            });
        });

        editablePageElements.each(function () {
            OverflowChecker.MarkOverflowInternal(this);
        });
    };

    OverflowChecker.IsOverflowingSelf = function (element) {
        if (element.hasAttribute('data-book') && element.getAttribute('data-book') == "topic") {
            return false;
        }

        if ($(element).css('display') === 'none' || $(element).css('display') === 'inline')
            return false;

        var focusedBorderFudgeFactor = 2;

        var growFromCenterVerticalFudgeFactor = 0;
        if ($(element).data('firefoxheight')) {
            var fontSizeRemnant = new StyleEditor("/bloom/bookEdit").GetCalculatedFontSizeInPoints(element) - 22;
            if (fontSizeRemnant > 0) {
                growFromCenterVerticalFudgeFactor = (fontSizeRemnant / 5) + 1;
            }
        }

        var shortBoxFudgeFactor = 4;

        return element.scrollHeight > element.clientHeight + focusedBorderFudgeFactor + growFromCenterVerticalFudgeFactor + shortBoxFudgeFactor || element.scrollWidth > element.clientWidth + focusedBorderFudgeFactor;
    };

    OverflowChecker.IsOverflowingMargins = function (element) {
        if (element.hasAttribute('data-book') && element.getAttribute('data-book') == "topic") {
            return false;
        }

        var marginBoxParent = $(element).parents('.marginBox');
        var parentBottom;
        if (marginBoxParent && marginBoxParent.length > 0)
            parentBottom = $(marginBoxParent[0]).offset().top + $(marginBoxParent[0]).outerHeight(true);
        else
            parentBottom = 999999;
        var elemTop = $(element).offset().top;
        var elemBottom = elemTop + $(element).outerHeight(false);

        var focusedBorderFudgeFactor = 2;

        return elemBottom > parentBottom + focusedBorderFudgeFactor;
    };

    OverflowChecker.MarkOverflowInternal = function (box) {
        var $box = $(box);
        $box.removeClass('overflow');
        $box.removeClass('marginOverflow');
        if (OverflowChecker.IsOverflowingSelf(box) || OverflowChecker.HasImmediateSplitParentThatOverflows($box)) {
            $box.addClass('overflow');
        }

        var container = $box.closest('.marginBox');

        var queryElementsThatCanOverflow = ".bloom-editable, textarea";
        var editablePageElements = $(container).find(queryElementsThatCanOverflow);

        editablePageElements.each(function () {
            var $this = $(this);
            if (OverflowChecker.IsOverflowingMargins($this[0])) {
                $this.addClass('marginOverflow');
            } else {
                if (!OverflowChecker.IsOverflowingSelf($this[0]) && !OverflowChecker.HasImmediateSplitParentThatOverflows($this)) {
                    $this.removeClass('overflow');
                    $this.removeClass('marginOverflow');
                }
            }
        });
        OverflowChecker.UpdatePageOverflow(container.closest('.bloom-page'));
    };

    OverflowChecker.HasImmediateSplitParentThatOverflows = function (jQueryBox) {
        var splitterParents = jQueryBox.parents('.split-pane-component-inner');
        if (splitterParents.length == 0) {
            return false;
        }
        return OverflowChecker.IsOverflowingSelf(splitterParents[0]);
    };

    OverflowChecker.UpdatePageOverflow = function (page) {
        var $page = $(page);
        if (!($page.find('.overflow').length) && !($page.find('.marginOverflow').length))
            $page.removeClass('pageOverflows');
        else
            $page.addClass('pageOverflows');
    };

    OverflowChecker.CheckOnMinHeight = function (box) {
        var $box = $(box);
        var overflowy = $box.css("overflow-y");
        if (overflowy == 'hidden') {
            $box.css("min-height", $box.css("line-height"));
        } else {
            $box.css('min-height', '');
            var lineHeight = parseFloat($box.css("line-height"));
            var minHeight = parseFloat($box.css("min-height"));

            if (minHeight < lineHeight) {
                $box.css("min-height", lineHeight + 0.01);
            }
        }

        $box.removeClass('Layout-Problem-Detected');
    };
    return OverflowChecker;
})();
