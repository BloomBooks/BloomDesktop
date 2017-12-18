// This class makes it possible to add and delete textboxes that float over images. These floating
// textboxes are intended for use in making comic books, but could also be useful in the case of
// any book that uses a picture where there is space for text within the bounds of the picture.
// In order to be accessible via a right-click context menu that c# generates, it listens on a websocket
// that the Bloom C# uses (in Browser.cs).
///<reference path="../../typings/jquery/jquery.d.ts"/>

import { fireCSharpEditEvent } from "./bloomEditing";
import { EditableDivUtils } from "./editableDivUtils";

const kSocketName = "webSocket";

// references to "TOP" in the code refer to the actual TextOverPicture box installed in the Bloom page.
class TextOverPictureManager {

    listenerFunction: (MessageEvent) => void;

    public initializeTextOverPictureManager(): void {
        this.listenerFunction = event => {
            var e = JSON.parse(event.data);
            var locationArray = e.payload.split(","); // mouse right-click coordinates
            if (e.id === "addTextBox")
                this.addFloatingTOPBox(locationArray[0], locationArray[1]);
            if (e.id === "deleteTextBox")
                this.deleteFloatingTOPBox(locationArray[0], locationArray[1]);
        };
        // I (GJM) ran into problems setting the onmessage handler in two places (here and in audioRecording)
        // The problem can be solved two ways: 1- by giving TextOverPictureManager's socket a different name,
        // or 2- by switching to using addEventListener("message", eventhandler), which I've done. Also, the
        // TextOverPictureManager's socket is now on the local window as opposed to window.top as in
        // audioRecording.ts.
        var socket = this.getWebSocket();
        if (socket) {
            socket.addEventListener("message", this.listenerFunction);
        }

    }

    public removeTextOverPictureListener(): void {
        var socket = this.getWebSocket();
        if (socket) {
            socket.removeEventListener("message", this.listenerFunction);
            socket.close();
        }
    }

    // N.B. Apparently when the window is shutting down, it is still possible to return from this
    // function with window[kSocketName] undefined.
    private getWebSocket(): WebSocket {
        if (!window[kSocketName]) {
            //currently we use a different port for this websocket, and it's the main port + 1
            let websocketPort = parseInt(window.location.port, 10) + 1;
            //NB: testing shows that our webSocketServer does receive a close notification when this window goes away
            window[kSocketName] = new WebSocket("ws://127.0.0.1:" + websocketPort.toString());
        }
        return window[kSocketName];
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private addFloatingTOPBox(mouseX: number, mouseY: number) {
        var container = this.getImageContainerFromMouse(mouseX, mouseY);
        if (!container || container.length === 0) {
            return; // don't add a TOP box if we can't find the containing imageContainer
        }
        // add a draggable text bubble to the html dom of the current page
        const editableDivClasses = "bloom-editable bloom-content1 bloom-visibility-code-on normal-style";
        const editableDivHtml = "<div class='" + editableDivClasses + "' ><p></p></div>";
        const transGroupDivClasses = "bloom-translationGroup bloom-leadingElement normal-style";
        const transGroupHtml = "<div class='" + transGroupDivClasses + "' data-default-languages='V'>" + editableDivHtml + "</div>";
        const handleHtml = "<div class='bloom-dragHandleTOP'></div>";
        const wrapperHtml = "<div class='bloom-textOverPicture'>" + handleHtml + transGroupHtml + "</div>";
        // add textbox as first child of .bloom-imageContainer
        var firstContainerChild = container.children().first();
        var wrapperBox = $(wrapperHtml).insertBefore(firstContainerChild);
        // initial mouseX, mouseY coordinates are relative to viewport
        this.calculateAndFixInitialLocation(wrapperBox, container, mouseX, mouseY);
        // I tried to do without this... it didn't work. This causes page changes to get saved and fills
        // things in for editing.
        // It causes EditingModel.RethinkPageAndReloadIt() to get run... which eventually causes
        // makeTextOverPictureBoxDraggableClickableAndResizable to get called by bloomEditing.ts.
        fireCSharpEditEvent("saveChangesAndRethinkPageEvent", "");
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private getImageContainerFromMouse(mouseX: number, mouseY: number): JQuery {
        return $(document.elementFromPoint(mouseX, mouseY)).closest(".bloom-imageContainer");
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private calculateAndFixInitialLocation(wrapperBox: JQuery, container: JQuery, mouseX: number, mouseY: number) {
        var scale = EditableDivUtils.getPageScale();
        var containerPosition = container[0].getBoundingClientRect();
        var xOffset = ((mouseX - containerPosition.left) / scale);
        var yOffset = ((mouseY - containerPosition.top) / scale);
        var location = "left: " + xOffset + "px; top: " + yOffset + "px;";
        wrapperBox.attr("style", location);
        this.calculatePercentagesAndFixTextboxPosition(wrapperBox); // translate px to %
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private deleteFloatingTOPBox(mouseX: number, mouseY: number) {
        var focusedBubble = $(document.elementFromPoint(mouseX, mouseY)).closest(".bloom-textOverPicture");
        if (focusedBubble && focusedBubble.length > 0) {
            focusedBubble.remove();
        }
    }

    private makeTOPBoxDraggableAndClickable(thisTOPBox: JQuery, scale: number): void {
        var image = this.getImageContainer(thisTOPBox);
        var imagePos = image[0].getBoundingClientRect();
        var wrapperBoxRectangle = thisTOPBox[0].getBoundingClientRect();
        // Containment, drag and stop work when scaled (zoomed) as long as the page has been saved since the zoom
        // factor was last changed. Therefore we force saving the page after setZoom() in bloomEditing.
        thisTOPBox.draggable({
            // adjust containment by scaling
            containment: [imagePos.left, imagePos.top,
            imagePos.left + imagePos.width - wrapperBoxRectangle.width,
            imagePos.top + imagePos.height - wrapperBoxRectangle.height],
            drag: (event, ui) => {
                ui.helper.children(".bloom-editable").blur();
                ui.position.top = (ui.position.top / scale);
                ui.position.left = (ui.position.left / scale);
            },
            handle: ".bloom-dragHandleTOP",
            stop: (event, ui) => {
                this.calculatePercentagesAndFixTextboxPosition($(event.target));
            }
        });

        thisTOPBox.find(".bloom-editable").click(function (e) {
            this.focus();
        });
    }

    // Make any added TextOverPictureManager textboxes draggable, clickable, and resizable.
    // Called by bloomEditing.ts.
    public makeTextOverPictureBoxDraggableClickableAndResizable() {
        // get all textOverPicture elements
        var textOverPictureElems = $("body").find(".bloom-textOverPicture");
        if (textOverPictureElems.length === 0) {
            return; // if there aren't any, quit before we hurt ourselves!
        }
        var scale = EditableDivUtils.getPageScale();

        textOverPictureElems.resizable({
            stop: (event, ui) => {
                // Resizing also changes size and position to pixels. Fix it.
                this.calculatePercentagesAndFixTextboxPosition($(event.target));
                // There was a problem where resizing a box messed up its draggable containment,
                // so now after we resize we go back through making it draggable and clickable again.
                this.makeTOPBoxDraggableAndClickable($(event.target), scale);
            }
        });

        this.makeTOPBoxDraggableAndClickable(textOverPictureElems, scale);

        if (textOverPictureElems.length > 0) {
            textOverPictureElems.first().find(".bloom-editable.bloom-visibility-code-on").first().focus();
        }
    }

    private calculatePercentagesAndFixTextboxPosition(wrapperBox: JQuery) {
        var scale = EditableDivUtils.getPageScale();
        var container = wrapperBox.closest(".bloom-imageContainer");
        var pos = wrapperBox.position();
        // the textbox is contained by the image, and it's actual positioning is now based on the imageContainer too.
        // so we will position by percentage of container size.
        var containerSize = {
            height: container.height(),
            width: container.width()
        };
        wrapperBox.css("left", (pos.left / scale / containerSize.width * 100) + "%")
            .css("top", (pos.top / scale / containerSize.height * 100) + "%")
            .css("width", (wrapperBox.width() / containerSize.width * 100) + "%")
            .css("height", (wrapperBox.height() / containerSize.height * 100) + "%");
    }

    // Gets the bloom-imageContainer that hosts this TextOverPictureManager textbox.
    // The imageContainer will define the dragging boundaries for the textbox.
    private getImageContainer(wrapperBox: JQuery): JQuery {
        return wrapperBox.parent(".bloom-imageContainer").first();
    }
}

export var theOneTextOverPictureManager: TextOverPictureManager;

export function initializeTextOverPictureManager() {
    if (theOneTextOverPictureManager)
        return;
    theOneTextOverPictureManager = new TextOverPictureManager();
    theOneTextOverPictureManager.initializeTextOverPictureManager();
}
