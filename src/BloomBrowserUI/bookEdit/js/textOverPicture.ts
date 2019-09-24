// This class makes it possible to add and delete textboxes that float over images. These floating
// textboxes are intended for use in making comic books, but could also be useful in the case of
// any book that uses a picture where there is space for text within the bounds of the picture.
// In order to be accessible via a right-click context menu that c# generates, it listens on a websocket
// that the Bloom C# uses (in Browser.cs).
///<reference path="../../typings/jquery/jquery.d.ts"/>

import { EditableDivUtils } from "./editableDivUtils";
import { BloomApi } from "../../utils/bloomApi";
import WebSocketManager from "../../utils/WebSocketManager";
import Comical from "bubble-edit/comical";
import Bubble from "bubble-edit/bubble";
import { BubbleSpec, BubbleSpecPattern, Tip } from "bubble-edit/bubbleSpec";
import { BubblesTool } from "../toolbox/bubbles/comicBubblesControls";

const kWebsocketContext = "textOverPicture";
// references to "TOP" in the code refer to the actual TextOverPicture box installed in the Bloom page.
export class TextOverPictureManager {
    private activeBubble: Bubble | undefined;
    private notifyBubbleChange: (x: BubbleSpec | undefined) => void;

    public initializeTextOverPictureManager(): void {
        WebSocketManager.addListener(kWebsocketContext, messageEvent => {
            const msg = messageEvent.message;
            if (msg) {
                const locationArray = msg.split(","); // mouse right-click coordinates
                if (messageEvent.id === "addTextBox")
                    this.addFloatingTOPBox(
                        +locationArray[0],
                        +locationArray[1]
                    );
                if (messageEvent.id === "deleteTextBox")
                    this.deleteFloatingTOPBox(
                        +locationArray[0],
                        +locationArray[1]
                    );
            }
        });
    }

    public turnOnBubbleEditing(): void {
        Array.from(
            document.getElementsByClassName("bloom-imageContainer")
        ).forEach(e => e.classList.add("bloom-hideImageButtons"));
        // todo: select one of them...make sure this doesn't conflict with any other strategy
        // for selecting one we just added.
        // todo: do this for the selected element, not just the first.
        const textOverPictureElems = document.getElementsByClassName(
            "bloom-textOverPicture"
        );
        if (textOverPictureElems.length > 0) {
            const activeElement = textOverPictureElems[0] as HTMLElement;
            this.activeBubble = new Bubble(activeElement);
            const editable = textOverPictureElems[0].getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )[0] as HTMLElement;
            editable.focus();
            Comical.convertBubbleJsonToCanvas(activeElement!.parentElement!);
            Array.from(
                document.getElementsByClassName("bloom-editable")
            ).forEach(element => {
                // tempting to use focusin on the bubble elements here,
                // but that's not in FF45 (starts in 52)
                element.addEventListener("focus", () => {
                    // If we focus something on the page that isn't in a bubble, we need to switch
                    // to having no active bubble element. Note: we don't want to use focusout
                    // on the bubble elements, because then we lose the active element while clicking
                    // on controls in the toolbox (and while debugging).
                    // Todo: check what happens when a bubble is removed.
                    const bubbleElement = element.closest(
                        ".bloom-textOverPicture"
                    );
                    if (bubbleElement) {
                        this.setActiveElement(bubbleElement as HTMLElement);
                    } else {
                        this.setActiveElement(undefined);
                    }
                });
            });
        }
    }

    private setActiveElement(element: HTMLElement | undefined) {
        if (element == undefined) {
            this.activeBubble = undefined;
            return;
        }

        this.activeBubble = new Bubble(element);
        if (this.notifyBubbleChange) {
            this.notifyBubbleChange(this.getSelectedItemBubbleSpec());
        }
    }

    public turnOffBubbleEditing(): void {
        const canvas = document.getElementsByClassName(
            "bubble-edit-generated"
        )[0];
        if (canvas && canvas.parentElement) {
            Comical.convertCanvasToSvgImg(canvas.parentElement as HTMLElement);
        }
        Array.from(
            document.getElementsByClassName("bloom-hideImageButtons")
        ).forEach(e => e.classList.remove("bloom-hideImageButtons"));
    }

    public prepareToSavePage(): void {
        // Review: do we need to call turnOffComicaling, or is that done in time anyway?
    }

    public cleanUp(): void {
        WebSocketManager.closeSocket(kWebsocketContext);
    }

    public getSelectedItemBubbleSpec(): BubbleSpec | undefined {
        if (!this.activeBubble) {
            return undefined;
        }
        return this.activeBubble.spec;
    }

    public requestBubbleChangeNotification(
        notifier: (bubble: BubbleSpec | undefined) => void
    ): void {
        this.notifyBubbleChange = notifier;
    }

    public updateSelectedItemBubbleSpec(
        newBubbleProps: BubbleSpecPattern
    ): void {
        if (!this.activeBubble) {
            return;
        }
        // Figure out a default that will supply any necessary properties not
        // specified in data, including a tail in a default position
        const defaultData = Bubble.getDefaultBubble(
            this.activeBubble.content,
            newBubbleProps.style!
        );
        const oldData: BubbleSpec = this.activeBubble.spec;
        // We get the default bubble for this style and parent to provide
        // any properties that have never before occurred for this bubble,
        // particularly a default tail placement if it was previously 'none'.
        // Any values already in oldData override these; for example, if
        // this bubble has ever had a tail, we'll keep its last known position.
        // Finally, any values present in data override anything else.
        const mergedBubble = {
            ...defaultData,
            ...oldData,
            ...(newBubbleProps as BubbleSpec)
        };
        this.activeBubble.setBubbleSpec(mergedBubble);
        Comical.update(this.activeBubble!.content.parentElement!);
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private addFloatingTOPBox(mouseX: number, mouseY: number) {
        const container = this.getImageContainerFromMouse(mouseX, mouseY);
        if (!container || container.length === 0) {
            return; // don't add a TOP box if we can't find the containing imageContainer
        }
        // add a draggable text bubble to the html dom of the current page
        const editableDivClasses =
            "bloom-editable bloom-content1 bloom-visibility-code-on normal-style";
        const editableDivHtml =
            "<div class='" + editableDivClasses + "' ><p></p></div>";
        const transGroupDivClasses =
            "bloom-translationGroup bloom-leadingElement normal-style";
        const transGroupHtml =
            "<div class='" +
            transGroupDivClasses +
            "' data-default-languages='V'>" +
            editableDivHtml +
            "</div>";
        const handleHtml = "<div class='bloom-dragHandleTOP'></div>";
        const wrapperHtml =
            "<div class='bloom-textOverPicture'>" +
            handleHtml +
            transGroupHtml +
            "</div>";
        // add textbox as first child of .bloom-imageContainer
        const firstContainerChild = container.children().first();
        const wrapperBox = $(wrapperHtml).insertBefore(firstContainerChild);
        // initial mouseX, mouseY coordinates are relative to viewport
        this.calculateAndFixInitialLocation(
            wrapperBox,
            container,
            mouseX,
            mouseY
        );

        const contentElement = wrapperBox.get(0);
        const bubbleSpec: BubbleSpec = {
            version: "1.0",
            style: "speech",
            tips: [Bubble.makeDefaultTip(contentElement)],
            level: 1
        };
        const bubble = new Bubble(contentElement);
        bubble.setBubbleSpec(bubbleSpec);
        // Plausibly at this point we might call Comical.update() to get the new
        // bubble drawn. But reloading the page achieves the same thing.

        // I tried to do without this... it didn't work. This causes page changes to get saved and fills
        // things in for editing.
        // It causes EditingModel.RethinkPageAndReloadIt() to get run... which eventually causes
        // makeTextOverPictureBoxDraggableClickableAndResizable to get called by bloomEditing.ts.
        BloomApi.postThatMightNavigate("common/saveChangesAndRethinkPageEvent");
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private getImageContainerFromMouse(mouseX: number, mouseY: number): JQuery {
        const clickElement = document.elementFromPoint(mouseX, mouseY);
        if (!clickElement) {
            // method not specified to return null
            return $();
        }
        return $(clickElement).closest(".bloom-imageContainer");
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private calculateAndFixInitialLocation(
        wrapperBox: JQuery,
        container: JQuery,
        mouseX: number,
        mouseY: number
    ) {
        const scale = EditableDivUtils.getPageScale();
        const containerPosition = container[0].getBoundingClientRect();
        const xOffset = (mouseX - containerPosition.left) / scale;
        const yOffset = (mouseY - containerPosition.top) / scale;
        const location = "left: " + xOffset + "px; top: " + yOffset + "px;";
        wrapperBox.attr("style", location);
        this.calculatePercentagesAndFixTextboxPosition(wrapperBox); // translate px to %
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    private deleteFloatingTOPBox(mouseX: number, mouseY: number) {
        const clickedElement = document.elementFromPoint(mouseX, mouseY);
        if (clickedElement) {
            const textElement = clickedElement.closest(
                ".bloom-textOverPicture"
            );
            if (textElement && textElement.parentElement) {
                const parent = textElement.parentElement;
                parent.removeChild(textElement);
                Comical.update(parent);
            }
        }
    }

    private makeTOPBoxDraggableAndClickable(
        thisTOPBox: JQuery,
        scale: number
    ): void {
        const image = this.getImageContainer(thisTOPBox);
        const imagePos = image[0].getBoundingClientRect();
        const wrapperBoxRectangle = thisTOPBox[0].getBoundingClientRect();
        // Containment, drag and stop work when scaled (zoomed) as long as the page has been saved since the zoom
        // factor was last changed. Therefore we force reconstructing the page
        // in the EditingView.Zoom setter (in C#).
        thisTOPBox.draggable({
            // adjust containment by scaling
            containment: [
                imagePos.left,
                imagePos.top,
                imagePos.left + imagePos.width - wrapperBoxRectangle.width,
                imagePos.top + imagePos.height - wrapperBoxRectangle.height
            ],
            drag: (event, ui) => {
                ui.helper.children(".bloom-editable").blur();
                ui.position.top = ui.position.top / scale;
                ui.position.left = ui.position.left / scale;
            },
            handle: ".bloom-dragHandleTOP",
            stop: (event, ui) => {
                const target = event.target;
                if (target) {
                    this.calculatePercentagesAndFixTextboxPosition($(target));
                }
            }
        });

        thisTOPBox.find(".bloom-editable").click(function(e) {
            this.focus();
        });
    }

    // Make any added TextOverPictureManager textboxes draggable, clickable, and resizable.
    // Called by bloomEditing.ts.
    public makeTextOverPictureBoxDraggableClickableAndResizable() {
        // get all textOverPicture elements
        const textOverPictureElems = $("body").find(".bloom-textOverPicture");
        if (textOverPictureElems.length === 0) {
            return; // if there aren't any, quit before we hurt ourselves!
        }
        const scale = EditableDivUtils.getPageScale();

        textOverPictureElems.resizable({
            stop: (event, ui) => {
                const target = event.target;
                if (target) {
                    // Resizing also changes size and position to pixels. Fix it.
                    this.calculatePercentagesAndFixTextboxPosition($(target));
                    // There was a problem where resizing a box messed up its draggable containment,
                    // so now after we resize we go back through making it draggable and clickable again.
                    this.makeTOPBoxDraggableAndClickable($(target), scale);
                }
            }
        });

        this.makeTOPBoxDraggableAndClickable(textOverPictureElems, scale);
    }

    private calculatePercentagesAndFixTextboxPosition(wrapperBox: JQuery) {
        const scale = EditableDivUtils.getPageScale();
        const container = wrapperBox.closest(".bloom-imageContainer");
        const pos = wrapperBox.position();
        // the textbox is contained by the image, and it's actual positioning is now based on the imageContainer too.
        // so we will position by percentage of container size.
        const containerSize = {
            height: container.height(),
            width: container.width()
        };
        wrapperBox
            .css("left", (pos.left / scale / containerSize.width) * 100 + "%")
            .css("top", (pos.top / scale / containerSize.height) * 100 + "%")
            .css(
                "width",
                (wrapperBox.width() / containerSize.width) * 100 + "%"
            )
            .css(
                "height",
                (wrapperBox.height() / containerSize.height) * 100 + "%"
            );
    }

    // Gets the bloom-imageContainer that hosts this TextOverPictureManager textbox.
    // The imageContainer will define the dragging boundaries for the textbox.
    private getImageContainer(wrapperBox: JQuery): JQuery {
        return wrapperBox.parent(".bloom-imageContainer").first();
    }
}

export let theOneTextOverPictureManager: TextOverPictureManager;

export function initializeTextOverPictureManager() {
    if (theOneTextOverPictureManager) return;
    theOneTextOverPictureManager = new TextOverPictureManager();
    theOneTextOverPictureManager.initializeTextOverPictureManager();
}
