/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../typings/jquery.gridly.d.ts" />
///<reference path="../../typings/toastr/toastr.d.ts"/>
/// <reference path="../../lib/localizationManager/localizationManager.ts" />

// This is one of the root files for our webpack build, the root from which
// pageThumbnailListBundle.js is built. Currently, contrary to our usual practice,
// this bundle is one of two loaded by pageThumbnailList.pug. It is imported last,
// so things it exports are accessible from outside the bundle using FrameExports.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

import * as toastr from "toastr";
import * as $ from "jquery";
import "../../modified_libraries/gridly/jquery.gridly.js";
import { SetImageElementUrl } from "../js/bloomImages";
import "errorHandler";
import WebSocketManager from "../../utils/WebSocketManager";

const timerName = "thumbnailInterval";
const kWebsocketContext = "pageThumbnailList";

let thumbnailTimerInterval = 200; // yes, this actually is not a constant!
let listenerFunction;

$(window).ready(function() {
    $(".gridly").gridly({
        base: 35, // px
        gutter: 10, // px
        columns: 4,
        callbacks: {
            reordered: reorder
        }
    });
    jQuery(".gridItem").click(function(e) {
        // adding "preventDefault()"" here and the cursor css might make the
        // invisibleThumbnailCover unneccessary, but all of it together should be plenty
        // of defense against the user getting unwanted results by clicking on thumbnails.
        e.stopPropagation();
        e.preventDefault();
        fireCSharpEvent("gridClick", $(this).attr("id"));
    });

    // start the thumbnail timer
    const timerSetting = document.body.dataset[timerName];
    if (timerSetting) thumbnailTimerInterval = parseInt(timerSetting, 10); //reviewslog: was timerSetting.value, but timerSetting is a string)

    // This timeout expires before the main page is displayed to the user, so we're using
    // thumbnailTimerInterval * 2 for the first interval to give the UI more time to catch up.
    setTimeout(loadNextThumbnail, thumbnailTimerInterval * 2);

    jQuery("#menu").click(function(event) {
        event.stopPropagation();
        fireCSharpEvent(
            "menuClicked",
            $(this)
                .parent()
                .parent()
                .attr("id")
        );
    });

    let localizedNotification = "";

    // This function will be hooked up (after we set localizedNotification properly)
    // to be called when C# sends messages through the web socket.
    // We need a named function because it looks cleaner and we use it to remove the
    // listener when we shut down.
    listenerFunction = event => {
        if (event.id === "saving") {
            toastr.info(localizedNotification, "", {
                positionClass: "toast-top-left",
                preventDuplicates: true,
                showDuration: 300,
                hideDuration: 300,
                timeOut: 1000,
                extendedTimeOut: 1000,
                showEasing: "swing",
                showMethod: "fadeIn",
                hideEasing: "linear",
                hideMethod: "fadeOut",
                messageClass: "toast-for-saved-message",
                iconClass: ""
            });
        }
    };

    theOneLocalizationManager
        .asyncGetText("EditTab.SavingNotification", "Saving...", "")
        .done(savingNotification => {
            localizedNotification = savingNotification;
            WebSocketManager.addListener(kWebsocketContext, listenerFunction);
        });
});

export function stopListeningForSave() {
    WebSocketManager.closeSocket(kWebsocketContext);
}

function fireCSharpEvent(eventName, eventData) {
    const event = new MessageEvent(eventName, {
        bubbles: true,
        cancelable: true,
        data: eventData
    });
    top.document.dispatchEvent(event);
}

function loadNextThumbnail() {
    // The "thumb-src" attribute is added to the img tags on the server while the page is being built. The value
    // of the "src" attribute is copied into it and then the "src" attribute is set to an empty string so the
    // images can be loaded here in a controlled manner so as not to overwhelm system memory.
    const nextImg = jQuery("body")
        .find("*[thumb-src]")
        .first();

    // stop processing if there are no more images
    if (!nextImg || nextImg.length === 0) return;

    const img = nextImg[0];

    // adding this to the query string tells the server to generate a thumbnail from the image file
    let src = img.getAttribute("thumb-src");
    if (src && src.indexOf("?") >= 0) {
        // already has a query (e.g., at one point we had optional=true for branding)
        src = src + "&thumbnail=1";
    } else {
        src = src + "?thumbnail=1";
    }
    img.removeAttribute("thumb-src");

    SetImageElementUrl(img, src);

    // This delay is needed because the processing of larger images was causing out-of-memory errors if several
    // images were requested before the server had finished processing previous images requests. We are also
    // attempting to prevent the browser from timing out and showing the alt text instead of the image.
    // 2015-06-04: The interval is now a variable rather than a constant in order to make the initial Edit tab
    // load more responsive, especially on low-end hardware.
    setTimeout(loadNextThumbnail, thumbnailTimerInterval);
}

function reorder(elements) {
    let ids = "";
    elements.each(function() {
        const id = $(this).attr("id");
        if (id) ids += "," + id;
    });
    fireCSharpEvent("gridReordered", ids);
}
