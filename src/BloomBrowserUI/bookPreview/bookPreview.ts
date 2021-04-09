import * as $ from "jquery";
import "jquery.hasAttr.js"; //reviewSlog for CenterVerticallyInParent
import "errorHandler";

$.fn.CenterVerticallyInParent = function() {
    return this.each(function(i) {
        //TODO: this height() thing is a mystery. Whereas Firebug will say the box is, say, 53px, this will say 675, so that no centering is possible
        var ah = $(this).height();
        var ph = $(this)
            .parent()
            .height();
        var mh = Math.ceil((ph - ah) / 2);
        $(this).css("margin-top", mh);
    });
};

$(document).ready(() => {
    $("textarea").focus(function() {
        $(this).attr("readonly", "readonly");
    });

    // Preview is currently used for the main document window when not editing, but also for the template pages view.
    // We want the disabledVisual style for the main page when not editing, but not in the template pages chooser.
    // Pages in the latter view are embedded in a .gridItem div, so we use that to suppress adding the style.
    $(".bloom-page:not('.gridItem .bloom-page')").mouseenter(function() {
        $(this).addClass("disabledVisual");
    });
    $(".bloom-page:not('.gridItem .bloom-page')").mouseleave(function() {
        $(this).removeClass("disabledVisual");
    });

    //Allow labels and separators to be marked such that if the user doesn't fill in a value, the label will be invisible when published.
    //NB: why in cleanup? it's not ideal, but if it gets called after each editing session, then things will be left in the proper state.
    //If we ever get into jscript at publishing time, well then this could go there.
    $("*.bloom-doNotPublishIfParentOtherwiseEmpty").each(function() {
        if (
            $(this)
                .parent()
                .find("*:empty").length > 0
        ) {
            $(this).addClass("bloom-hideWhenPublishing");
        } else {
            $(this).removeClass("bloom-hideWhenPublishing");
        }
    });

    //--------------------------------
    //keep divs vertically centered (yes, I first tried *all* the css approaches available at the time,
    // they didn't work for our situation). (2016)There may be more options nowadays.

    //TODO: this is't working yet, (see CenterVerticallyInParent) but in any case one todo is to trigger
    //on something different. When the user invokes "layout-style-SplitAcrossPages" mode (e.g. via the menu in the publish tab),
    //then we want to impose this on text that wouldn't
    //normally have it (e.g. it might be normally centered top or bottom).
    //Put another way, we need to eventually do this centering based on the page style, not the class on the element.
    //Like I say, it doesn't work yet anyhow...

    $(".bloom-centerVertically").CenterVerticallyInParent();
});
