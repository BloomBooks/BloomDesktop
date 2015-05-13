/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
var bloomQtipUtils = (function () {
    function bloomQtipUtils() {
    }
    bloomQtipUtils.cleanupBubbles = function () {
        // remove the div's which qtip makes for the tips themselves
        $("div.qtip").each(function () {
            $(this).remove();
        });
        // remove the attributes qtips adds to the things being annotated
        $("*[aria-describedby]").each(function () {
            $(this).removeAttr("aria-describedby");
        });
        $("*[ariasecondary-describedby]").each(function () {
            $(this).removeAttr("ariasecondary-describedby");
        });
    };
    bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles = function (element) {
        //We can't actually know for sure if overlapping would happen, but
        //we can be very conservative and say that if the text
        //box isn't taking up the whole width, it *might* cause
        //an overlap
        if ($(element).hasClass('bloom-alwaysShowBubble')) {
            return false;
        }
        var availableWidth = $(element).closest(".marginBox").width();
        var kTolerancePixels = 10; //if the box is just a tiny bit smaller, there's not going to be anything to overlap
        return $(element).width() < (availableWidth - kTolerancePixels);
    };
    bloomQtipUtils.setQtipZindex = function () {
        if ($.fn.qtip)
            $.fn.qtip.zindex = 15000;
        //gives an error $.fn.qtip.plugins.modal.zindex = 1000000 - 20;
    };
    bloomQtipUtils.repositionPictureDictionaryTooltips = function (container) {
        // add drag and resize ability where elements call for it
        //   $(".bloom-draggable").draggable({containment: "parent"});
        $(container).find(".bloom-draggable").draggable({ containment: "parent", handle: '.bloom-imageContainer', stop: function (event, ui) {
            $(this).find('.wordsDiv').find('div').each(function () {
                $(this).qtip('reposition');
            });
        } //yes, this repositions *all* qtips on the page. Yuck.
         });
        // Without this "handle" restriction, clicks on the text boxes don't work.
        // NB: ".moveButton" is really what we wanted, but didn't work, probably because the button is only created
        //   on the mouseEnter event, and maybe that's too late.
        // Later note: using a real button just absorbs the click event. Other things work better
        //   http://stackoverflow.com/questions/10317128/how-to-make-a-div-contenteditable-and-draggable
    };
    return bloomQtipUtils;
})();
//# sourceMappingURL=bloomQtipUtils.js.map