// This class makes it possible to add and delete textboxes that float over images. These floating
// textboxes are intended for use in making comic books, but could also be useful in the case of
// any book that uses a picture where there is space for text within the bounds of the picture.
// In order to be accessible via a right-click context menu that c# generates, it listens on a websocket
// that the Bloom C# uses (in Browser.cs).
///<reference path="../../typings/jquery/jquery.d.ts"/>

import { EditableDivUtils } from "./editableDivUtils";
import { BloomApi } from "../../utils/bloomApi";
import WebSocketManager from "../../utils/WebSocketManager";
import { Comical, Bubble, BubbleSpec, BubbleSpecPattern } from "comicaljs";

const kWebsocketContext = "textOverPicture";
// references to "TOP" in the code refer to the actual TextOverPicture box installed in the Bloom page.
export class TextOverPictureManager {
    private activeElement: HTMLElement | undefined;
    private isCalloutEditingOn: boolean = false;
    private notifyBubbleChange:
        | ((x: BubbleSpec | undefined) => void)
        | undefined;

    public initializeTextOverPictureManager(): void {
        WebSocketManager.addListener(kWebsocketContext, messageEvent => {
            const msg = messageEvent.message;
            if (msg) {
                const locationArray = msg.split(","); // mouse right-click coordinates
                if (messageEvent.id === "addTextBox")
                    this.addFloatingTOPBoxAndReloadPage(
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

    public getIsCalloutEditingOn(): boolean {
        return this.isCalloutEditingOn;
    }

    public turnOnBubbleEditing(): void {
        if (this.isCalloutEditingOn === true) {
            return; // Already on. No work needs to be done
        }
        this.isCalloutEditingOn = true;

        Array.from(
            document.getElementsByClassName("bloom-imageContainer")
        ).forEach(e => e.classList.add("bloom-hideImageButtons"));
        // todo: select the right one...in particular, currently we just select the first one.
        // This is reasonable when just coming to the page, and when we add a new TOP,
        // we make the new one the first in its parent, so with only one image container
        // the new one gets selected after we refresh. However, once we have more than one
        // image container, I don't think the new TOP box will get selected if it's not on
        // the first image.
        // todo: make sure comical is turned on for the right parent, in case there's more than one image on the page?
        const textOverPictureElems = document.getElementsByClassName(
            "bloom-textOverPicture"
        );
        if (textOverPictureElems.length > 0) {
            this.activeElement = textOverPictureElems[0] as HTMLElement;
            const editable = textOverPictureElems[0].getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )[0] as HTMLElement;
            editable.focus();
            Comical.convertBubbleJsonToCanvas(
                this.activeElement!.parentElement!
            );
            Comical.activateElement(this.activeElement);
            Array.from(
                document.getElementsByClassName("bloom-editable")
            ).forEach(element => {
                // tempting to use focusin on the bubble elements here,
                // but that's not in FF45 (starts in 52)

                // Don't use an arrow function as an event handler here. These can never be identified as duplicate event listeners, so we'll end up with tons of duplicates
                element.addEventListener(
                    "focus",
                    TextOverPictureManager.onFocusSetActiveElement
                );
            });
        }

        // turn on drag-and-drop support for bubbles from comical toolbox
        Array.from(
            document.getElementsByClassName("bloom-imageContainer")
        ).forEach((container: HTMLElement) => {
            // This suppresses the default behavior, which is to forbid dragging things to
            // an element, but only if the source of the drag is a bloom bubble.
            container.ondragover = ev => {
                if (ev.dataTransfer && ev.dataTransfer.getData("bloomBubble")) {
                    ev.preventDefault();
                }
            };
            // Controls what happens when a bloom bubble is dropped. We get the style
            // set in CalloutControls.ondragstart() and make a bubble with that style
            // at the drop position.
            container.ondrop = ev => {
                ev.preventDefault();
                const style = ev.dataTransfer
                    ? ev.dataTransfer.getData("bloomBubble")
                    : "speech";
                this.addFloatingTOPBox(ev.clientX, ev.clientY, style);
                BloomApi.postThatMightNavigate(
                    "common/saveChangesAndRethinkPageEvent"
                );
            };
        });
    }

    // Event Handler to be called when something relevant on the page frame gets focus.  Will set the active textOverPicture element.
    public static onFocusSetActiveElement(event: Event) {
        const focusedElement = event.currentTarget as Element; // The current target is the element we attached the event listener to
        if (focusedElement.classList.contains("bloom-editable")) {
            // If we focus something on the page that isn't in a bubble, we need to switch
            // to having no active bubble element. Note: we don't want to use focusout
            // on the bubble elements, because then we lose the active element while clicking
            // on controls in the toolbox (and while debugging).

            // We don't think this function ever gets called when it's not initialized, but it doesn't
            // hurt to make sure.
            initializeTextOverPictureManager();

            const bubbleElement = focusedElement.closest(
                ".bloom-textOverPicture"
            );
            if (bubbleElement) {
                theOneTextOverPictureManager.setActiveElement(
                    bubbleElement as HTMLElement
                );
            } else {
                theOneTextOverPictureManager.setActiveElement(undefined);
            }
        }
    }

    public getActiveElement() {
        return this.activeElement;
    }

    private setActiveElement(element: HTMLElement | undefined) {
        this.activeElement = element;
        if (this.notifyBubbleChange) {
            this.notifyBubbleChange(this.getSelectedItemBubbleSpec());
        }
    }

    public turnOffBubbleEditing(): void {
        if (this.isCalloutEditingOn === false) {
            return; // Already off. No work needs to be done.
        }
        this.isCalloutEditingOn = false;

        const canvas = document.getElementsByClassName("comical-editing")[0];
        if (canvas && canvas.parentElement) {
            Comical.convertCanvasToSvgImg(canvas.parentElement as HTMLElement);
        }
        Array.from(
            document.getElementsByClassName("bloom-hideImageButtons")
        ).forEach(e => e.classList.remove("bloom-hideImageButtons"));

        // Clean up event listeners that we no longer need
        Array.from(document.getElementsByClassName("bloom-editable")).forEach(
            element => {
                element.removeEventListener(
                    "focus",
                    TextOverPictureManager.onFocusSetActiveElement
                );
            }
        );
    }

    public cleanUp(): void {
        WebSocketManager.closeSocket(kWebsocketContext);
    }

    public getSelectedItemBubbleSpec(): BubbleSpec | undefined {
        if (!this.activeElement) {
            return undefined;
        }
        return Bubble.getBubbleSpec(this.activeElement);
    }

    public requestBubbleChangeNotification(
        notifier: (bubble: BubbleSpec | undefined) => void
    ): void {
        this.notifyBubbleChange = notifier;
    }

    public detachBubbleChangeNotification(): void {
        this.notifyBubbleChange = undefined;
    }

    public updateSelectedItemBubbleSpec(
        newBubbleProps: BubbleSpecPattern
    ): void {
        if (!this.activeElement) {
            return;
        }

        const activeBubble = new Bubble(this.activeElement);
        activeBubble.mergeWithNewBubbleProps(newBubbleProps);
        Comical.update(this.activeElement.parentElement!);
    }

    // Note: After reloading the page, you can't have any of your other code execute safely
    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    public addFloatingTOPBoxAndReloadPage(mouseX: number, mouseY: number) {
        this.addFloatingTOPBox(mouseX, mouseY);

        // I tried to do without this... it didn't work. This causes page changes to get saved and fills
        // things in for editing.
        // It causes EditingModel.RethinkPageAndReloadIt() to get run... which eventually causes
        // makeTextOverPictureBoxDraggableClickableAndResizable to get called by bloomEditing.ts.
        BloomApi.postThatMightNavigate("common/saveChangesAndRethinkPageEvent");
    }

    // Adds a new text-over-picture element as a child of the specified {parentElement}
    //    (It is a child in the sense that the Comical library will recognize it as a child)
    // {offsetX}/{offsetY} is the offset in position from the parent to the child elements
    //    (i.e., offsetX = child.left - parent.left)
    //    (remember that positive values of Y are further to the bottom)
    // Note: After reloading the page, you can't have any of your other code execute safely
    public addChildTOPBoxAndReloadPage(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number
    ): void {
        const parentBoundingRect = parentElement.getBoundingClientRect();
        let newX = parentBoundingRect.left + offsetX;
        let newY = parentBoundingRect.top + offsetY;

        // // Ensure newX and newY is within the bounds of the container.
        const container = parentElement.closest(".bloom-imageContainer");
        if (!container) {
            toastr.warning("Failed to create child element.");
            return;
        }
        const containerBoundingRect = container.getBoundingClientRect();

        const bufferPixels = 15;
        if (newX < containerBoundingRect.left) {
            newX = containerBoundingRect.left + bufferPixels;
        } else if (
            newX + parentElement.clientWidth >
            containerBoundingRect.right
        ) {
            // ENHANCE: parentElement.clientWidth is just an estimate of the size of the child's width.
            //          It would be better if we could actually plug in the real value of the child's width
            newX = containerBoundingRect.right - parentElement.clientWidth;
        }

        if (newY < containerBoundingRect.top) {
            newY = containerBoundingRect.top + bufferPixels;
        } else if (
            newY + parentElement.clientHeight >
            containerBoundingRect.bottom
        ) {
            // ENHANCE: parentElement.clientHeight is just an estimate of the size of the child's height.
            //          It would be better if we could actually plug in the real value of the child's height
            newY = containerBoundingRect.bottom - parentElement.clientHeight;
        }

        const childElement = this.addFloatingTOPBox(newX, newY);
        if (!childElement) {
            toastr.info("Failed to place a new child callout.");
            return;
        }

        Comical.initializeChild(childElement, parentElement);

        // Need to reload the page to get it editable/draggable/etc,
        // and to get the Comical bubbles consistent with the new bubble specs
        BloomApi.postThatMightNavigate("common/saveChangesAndRethinkPageEvent");
    }

    public addFloatingTOPBox(
        mouseX: number,
        mouseY: number,
        style?: string
    ): HTMLElement | undefined {
        const container = this.getImageContainerFromMouse(mouseX, mouseY);
        if (!container || container.length === 0) {
            return undefined; // don't add a TOP box if we can't find the containing imageContainer
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
        const bubbleSpec: BubbleSpec = Bubble.getDefaultBubbleSpec(
            contentElement,
            style || "speech"
        );
        const bubble = new Bubble(contentElement);
        bubble.setBubbleSpec(bubbleSpec);
        // Plausibly at this point we might call Comical.update() to get the new
        // bubble drawn. But if we reload the page, that achieves the same thing.

        return contentElement;
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

                // Check if we're deleting the active bubble. If so, gotta clean up the state.
                if (textElement == this.getActiveElement()) {
                    this.setActiveElement(undefined);
                }
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
