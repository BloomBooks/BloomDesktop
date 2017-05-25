/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../typings/jqueryui/jqueryui.d.ts" />
import * as JQuery from 'jquery';

export interface qtipInterface extends JQuery {
    qtip(options: any): JQuery;
}

export default class bloomQtipUtils {

    public static cleanupBubbles(): void {
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
    }

    // Tell whether an element's bubble might overlap some other element (and therefore typically should
    // only be shown when the element has focus, or possibly when the mouse is over it).
    // Currently we approximate this by testing whether a box is narrower than the containing
    // marginBox (unless it has bloom-alwaysShowBubble, in which case we don't ever want the focus-only
    // behavior so we make this routine return false).
    // Unfortunately measuring width seems to be unreliable while window layout is still in progress;
    // experience suggests wrapping calls to this method and anything that uses the result in a
    // setTimeout(..., 250).
    // 250 is a compromise. On my fast desktop, 80ms is enough (at least in my test book); 70 is not.
    // Don't want to go much over 250 or it will too be noticeable that bubbles are delayed.
    // Hopefully this will be enough even on slower machines. If not, a focus-only bubble is not a
    // complete disaster.
    // (I tried using $(document).ready()...doesn't work. Better ideas welcome!)
    public static mightCauseHorizontallyOverlappingBubbles(element: JQuery): boolean {
        if ($(element).hasClass('bloom-alwaysShowBubble')) {
            return false;
        }
        var availableWidth = $(element).closest(".marginBox").width();
        var kTolerancePixels = 10; //if the box is just a tiny bit smaller, there's not going to be anything to overlap
        return $(element).width() < (availableWidth - kTolerancePixels);
    }

    public static setQtipZindex(): void {
        if ($.fn.qtip)
            $.fn.qtip.zindex = 15000;
        //gives an error $.fn.qtip.plugins.modal.zindex = 1000000 - 20;
    }

    public static repositionPictureDictionaryTooltips(container: HTMLElement): void {
        // add drag and resize ability where elements call for it
        //   $(".bloom-draggable").draggable({containment: "parent"});
        $(container).find(".bloom-draggable").draggable({
            containment: "parent",
            handle: '.bloom-imageContainer',
            stop: function (event, ui) {
                $(this).find('.wordsDiv').find('div').each(function () {
                    (<qtipInterface>$(this)).qtip('reposition');
                })
            } //yes, this repositions *all* qtips on the page. Yuck.
        });
        // Without this "handle" restriction, clicks on the text boxes don't work.
        // NB: ".moveButton" is really what we wanted, but didn't work, probably because the button is only created
        //   on the mouseEnter event, and maybe that's too late.
        // Later note: using a real button just absorbs the click event. Other things work better
        //   http://stackoverflow.com/questions/10317128/how-to-make-a-div-contenteditable-and-draggable
    }

    // In editing most (if not all) of the qtips need to be contained by a special div that handles
    // the zooming (scaling) of the page content.  If they are not contained by this div, 1) they don't
    // zoom/scale and 2) they appear on the screen in the same location as if the page content they are
    // supposed to attach to isn't zoomed/scaled.  If they are attached further in than this special
    // div, then the bubble is squeezed to fit inside the inner zoomed area.
    public static qtipZoomContainer(): JQuery {
        return $("div#page-scaling-container");
    }
}
