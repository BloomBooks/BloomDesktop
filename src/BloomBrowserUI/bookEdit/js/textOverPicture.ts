// This class uses the websocket similar to audioRecording.ts in order to add and delete floating
// text boxes over images. These floating text boxes are intended for use in making comic books,
// but could also be useful in the case of children's books where there is space for text within
// the bounds of a picture.
///<reference path="../../typings/jquery/jquery.d.ts"/>

import { fireCSharpEditEvent } from "./bloomEditing";
import { EditableDivUtils } from "./editableDivUtils";

class TextOverPicture {
    public initializeTextOverPicture(): void {
        this.getWebSocket().onmessage = event => {
            var e = JSON.parse(event.data);
            var locationArray = e.payload.split(",");
            if (e.id === "addTextBox")
                this.addFloatingTextBox(locationArray[0], locationArray[1]);
            if (e.id === "deleteTextBox")
                this.deleteFloatingTextBox(locationArray[0], locationArray[1]);
        };
    }

    private getWebSocket(): WebSocket {
        if (typeof window.top["webSocket"] == "undefined") {
            //currently we use a different port for this websocket, and it's the main port + 1
            const websocketPort = parseInt(window.location.port) + 1;
            //NB: testing shows that our webSocketServer does receive a close notification when this window goes away
            window.top["webSocket"] = new WebSocket("ws://127.0.0.1:" + websocketPort.toString());
        }
        return window.top["webSocket"];
    }

    // x and y are the location of the mouse when right-clicking to create the context menu
    private addFloatingTextBox(x: number, y: number) {
        var marginBox = $(document).find("div.marginBox");
        if (marginBox && marginBox.length > 0) {
            // add a draggable cartoon bubble to the html dom of the current page
            var scale = EditableDivUtils.getPageScale();
            var xOffset = (x / scale - marginBox[0].offsetLeft);
            var yOffset = (y / scale - marginBox[0].offsetTop);
            var location = "left: " + xOffset + "px; top: " + yOffset + "px;";
            var classes = "bloom-cartoonText bloom-translationGroup bloom-leadingElement normal-style";
            var html = "<div style='" + location + "' class='" + classes + "' data-default-languages='V' draggable='true'></div>";
            var firstMarginBoxChild = marginBox.first().children().first();
            $(html).insertBefore(firstMarginBoxChild);
            fireCSharpEditEvent("preparePageForEditingAfterOrigamiChangesEvent", ""); // save changes; gets things filled in
            $(document).find("div.marginBox .bloom-cartoonText .bloom-editable").first().focus();
        }
    }

    // x and y are the location of the mouse when right-clicking to create the context menu
    private deleteFloatingTextBox(x: number, y: number) {
        var focusedBubble = $(document.elementFromPoint(x, y)).closest("div.bloom-cartoonText");
        if (focusedBubble && focusedBubble.length > 0) {
            focusedBubble.remove();
        } else {
            $(document).find("div.marginBox div.bloom-cartoonText").first().remove();
        }
    }

    // make any added text-over-picture bubbles draggable
    // called by bloomEditing
    public makeTextOverPictureDraggable() {
        $("body").find(".bloom-cartoonText").each((i, bubble) => {
            var styleLocationArray = $(bubble).attr("style").match(/\d+/g); // something like "left: 288px; top: 370px;"
            var x = styleLocationArray[0];
            var y = styleLocationArray[1];
            var image = $(document.elementFromPoint(parseInt(x, 10), parseInt(y, 10))).closest(".bloom-imageContainer");
            $(bubble).draggable({ containment: image });
        });
    }
}

export var theOneTextOverPicture: TextOverPicture;

export function initializeTextOverPicture() {
    if (theOneTextOverPicture)
        return;
    theOneTextOverPicture = new TextOverPicture();
    theOneTextOverPicture.initializeTextOverPicture();
}

