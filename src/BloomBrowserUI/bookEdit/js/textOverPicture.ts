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
const kComicalGeneratedClass: string = "comical-generated";

// references to "TOP" in the code refer to the actual TextOverPicture box installed in the Bloom page.
export class TextOverPictureManager {
    // The min width/height needs to be kept in sync with the corresponding values in textOverPicture.less
    public minTextBoxWidthPx = 30;
    public minTextBoxHeightPx = 30;

    private activeElement: HTMLElement | undefined;
    private isCalloutEditingOn: boolean = false;
    private notifyBubbleChange:
        | ((x: BubbleSpec | undefined) => void)
        | undefined;

    // These variables are used by the bubble's onmouse* event handlers
    private draggedBubble: Bubble | undefined; // Use Undefined to indicate that there is no active drag in progress
    private bubbleGrabOffset: { x: number; y: number } = { x: 0, y: 0 };
    private initialClickPos: {
        clickX: number;
        clickY: number;
        elementX: number;
        elementY: number;
        width: number;
        height: number;
    };
    private resizeMode: string;

    public initializeTextOverPictureManager(): void {
        // currently nothing to do; used to set up web socket listener
        // for right-click messages to add and delete TOP boxes.
        // Keeping hook in case we want it one day...
    }

    public getIsCalloutEditingOn(): boolean {
        return this.isCalloutEditingOn;
    }

    // Given the box has been determined to be overflowing vertically by
    // needToGrow pixels, if it's inside a TOP box enlarge the TOP box
    // by that much so it won't overflow.
    // (Caller may wish to do box.scrollTop = 0 to make sure the whole
    // content shows now there is room for it all.)
    // Returns true if successful; it will currently fail if box is not
    // inside a valid TOP box or if the TOP box can't grow this much while remaining
    // inside the image container. If it returns false it makes no changes
    // at all.
    public static growOverflowingBox(
        box: HTMLElement,
        needToGrow: number
    ): boolean {
        const wrapperBox = box.closest(".bloom-textOverPicture") as HTMLElement;
        if (!wrapperBox) {
            return false; // we can't fix it
        }
        const container = wrapperBox.closest(".bloom-imageContainer");
        if (!container) {
            return false; // paranoia; TOP box should always be in image container
        }
        const newHeight = wrapperBox.clientHeight + needToGrow;
        if (newHeight + wrapperBox.offsetTop > container.clientHeight) {
            return false;
        }
        wrapperBox.style.height = newHeight + "px"; // next line will change to percent
        TextOverPictureManager.calculatePercentagesAndFixTextboxPosition(
            $(wrapperBox)
        );
        return true;
    }

    public turnOnHidingImageButtons() {
        const imageContainers: HTMLElement[] = Array.from(
            document.getElementsByClassName("bloom-imageContainer") as any
        );
        imageContainers.forEach(e => e.classList.add("bloom-hideImageButtons"));
    }

    public turnOnBubbleEditing(): void {
        if (this.isCalloutEditingOn === true) {
            return; // Already on. No work needs to be done
        }
        this.isCalloutEditingOn = true;

        Comical.setActiveBubbleListener(activeElement => {
            if (activeElement) {
                var focusElements = activeElement.getElementsByClassName(
                    "bloom-visibility-code-on"
                );
                if (focusElements.length > 0) {
                    (focusElements[0] as HTMLElement).focus();
                }
            }
        });

        const imageContainers: HTMLElement[] = Array.from(
            document.getElementsByClassName("bloom-imageContainer") as any
        );
        // todo: select the right one...in particular, currently we just select the first one.
        // This is reasonable when just coming to the page, and when we add a new TOP,
        // we make the new one the first in its parent, so with only one image container
        // the new one gets selected after we refresh. However, once we have more than one
        // image container, I don't think the new TOP box will get selected if it's not on
        // the first image.
        // todo: make sure comical is turned on for the right parent, in case there's more than one image on the page?
        const textOverPictureElems: HTMLElement[] = Array.from(
            document.getElementsByClassName("bloom-textOverPicture") as any
        );
        if (textOverPictureElems.length > 0) {
            this.activeElement = textOverPictureElems[0] as HTMLElement;
            const editable = textOverPictureElems[0].getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )[0] as HTMLElement;
            editable.focus();
            Comical.startEditing(imageContainers);
            this.migrateOldTopElems(textOverPictureElems);
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
            document.addEventListener(
                "click",
                TextOverPictureManager.onDocClickClearActiveElement
            );
        }

        // turn on various behaviors for each image
        Array.from(
            document.getElementsByClassName("bloom-imageContainer")
        ).forEach((container: HTMLElement) => {
            const containerBounds = container.getBoundingClientRect(); // Assumption: the container never moves after setup
            container.addEventListener("click", event => {
                // The goal here is that if the user clicks outside any comical bubble,
                // we want none of the comical bubbles selected, so that
                // (after moving the mouse away to get rid of hover effects)
                // the user can see exactly what the final comic will look like.
                // This is a difficult and horrible kludge.
                // First problem is that this click handler is fired for a click
                // ANYWHERE in the image...none of the bubble- or TOP- related
                // click handlers preventDefault(). So we have to figure out
                // whether the click was simply on the picture, or on something
                // inside it. A first step is to ignore any clicks where the target
                // is one of the picture's children. Even that's complicated...
                // the Comical canvas covers the whole picture, so the target
                // is NEVER the picture itself. But we can at least check that
                // the target is the comical canvas itself, not something overlayed
                // on it.
                if (
                    (event.target as HTMLElement).classList.contains(
                        "comical-editing"
                    )
                ) {
                    // OK, we clicked on the canvas, but we may still have clicked on
                    // some part of a bubble rather than away from it.
                    // We now use a Comical function to determine whether we clicked
                    // on a Comical object.
                    const x = event.offsetX;
                    const y = event.offsetY;
                    if (!Comical.somethingHit(container, x, y)) {
                        // So far so good. We have now determined that we want to remove
                        // focus from anything in this image.
                        // (Enhance: should we check that something within this image
                        // is currently focused, so clicking on a picture won't
                        // arbitrarily move the focus if it's not in this image?)
                        // Leaving nothing at all selected is something of a last resort,
                        // so we first look for something we can focus that is outside the
                        // image.
                        let somethingElseToFocus = Array.from(
                            document.getElementsByClassName("bloom-editable")
                        ).filter(
                            e =>
                                !container.contains(e) &&
                                (e as HTMLElement).offsetHeight > 0 // a crude but here adequate way to pick a visible one
                        )[0] as HTMLElement;
                        if (!somethingElseToFocus) {
                            // If the page contains only images (or videos, etc...no text except bubbles
                            // then we will make something temporary and hidden to focus.
                            // There may be some alternative to this but it is the most reliable
                            // thing I can think of to remove all the focus effects from the bubbles.
                            // Even so it's not as reliable as I would like because some of those
                            // effects are produced by focus handlers that won't automatically get
                            // attached to this temporary element.
                            somethingElseToFocus = document.createElement(
                                "div"
                            );
                            container.parentElement!.insertBefore(
                                somethingElseToFocus,
                                container
                            );
                            // We give it this class so it won't persist...Bloom cleans out such
                            // elements when saving the page.
                            somethingElseToFocus.classList.add("bloom-ui");
                            // it needs to be bloom-editable to trigger the code in
                            // onFocusSetActiveElement that hides the handles on the active bubble.
                            somethingElseToFocus.classList.add(
                                "bloom-editable"
                            );
                            // These properties are necessary (or at least sufficient) to make it possible to focus it
                            somethingElseToFocus.setAttribute(
                                "contenteditable",
                                "true"
                            );
                            somethingElseToFocus.setAttribute("tabindex", "0");
                            somethingElseToFocus.style.display = "block"; // defeat rules making it display:none and hence not focusable

                            // However, we don't actually want to see it; these rules
                            // (somewhat redundantly) make it have no size and be positioned
                            // off-screen.
                            somethingElseToFocus.style.width = "0";
                            somethingElseToFocus.style.height = "0";
                            somethingElseToFocus.style.overflow = "hidden";
                            somethingElseToFocus.style.position = "absolute";
                            somethingElseToFocus.style.left = "-1000px";

                            // And we want the usual behavior when it gets focus!
                            somethingElseToFocus.addEventListener(
                                "focus",
                                TextOverPictureManager.onFocusSetActiveElement
                            );
                        }
                        somethingElseToFocus.focus();
                    }
                }
            });

            this.setDragAndDropHandlers(container);
            this.setMouseDragHandlers(container, containerBounds);
        });
    }

    migrateOldTopElems(textOverPictureElems: HTMLElement[]): void {
        textOverPictureElems.forEach(top => {
            if (!top.getAttribute("data-bubble")) {
                const bubbleSpec = Bubble.getDefaultBubbleSpec(top, "none");
                new Bubble(top).setBubbleSpec(bubbleSpec);
                // it would be nice to do this only once, but there MIGHT
                // be TOP elements in more than one image container...too complicated,
                // and this only happens once per TOP.
                Comical.update(top.closest(
                    ".bloom-imageContainer"
                ) as HTMLElement);
            }
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

    private static onDocClickClearActiveElement(event: Event) {
        const clickedElement = event.target as Element; // most local thing clicked on
        if (clickedElement.closest(".bloom-imageContainer")) {
            // We have other code to handle setting and clearing Comical handles
            // if the click is inside a Comical area.
            return;
        }
        // If we clicked in the document outside a Comical picture
        // we don't want anything Comical to be active.
        // (We don't use a blur event for this because we don't want to unset
        // the active element for clicks outside the content window, e.g., on the
        // toolbox controls, or even in a debug window. This event handler is
        // attached to the page frame document.)
        theOneTextOverPictureManager.setActiveElement(undefined);
    }

    public getActiveElement() {
        return this.activeElement;
    }

    private setActiveElement(element: HTMLElement | undefined) {
        if (this.activeElement === element) {
            return;
        }
        this.activeElement = element;
        if (this.notifyBubbleChange) {
            this.notifyBubbleChange(this.getSelectedItemBubbleSpec());
        }
        Comical.activateElement(this.activeElement);
    }

    // drag-and-drop support for bubbles from comical toolbox
    private setDragAndDropHandlers(container: HTMLElement): void {
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
    }

    // Setup event handlers that allow the bubble to be moved around.
    private setMouseDragHandlers(
        container: HTMLElement,
        containerBounds: ClientRect | DOMRect
    ): void {
        // Precondition: Assumes the border width / etc. never changes
        const styleInfo = window.getComputedStyle(container);

        // We use mousemove effects instead of drag due to concerns that drag effects would make the entire image container appear to drag.
        // Instead, with mousemove, we can make only the specific bubble move around
        container.onmousedown = (ev: MouseEvent) => {
            // Let standard clicks on the bloom editable only be processed on the editable
            if (this.isEventForEditableOnly(ev)) {
                return;
            }

            // These coordinates need to be relative to the canvas (which is the same as relative to the image container).
            const coordinates = this.getContainerCoordinates(
                ev,
                containerBounds,
                styleInfo
            );
            if (!coordinates) {
                return;
            }

            const [targetX, targetY] = coordinates;

            const bubble = Comical.getBubbleHit(container, targetX, targetY);
            if (bubble) {
                this.draggedBubble = bubble;

                // Remember the offset between the top-left of the content box and the initial location of the mouse pointer
                const positionInfo = bubble.content.getBoundingClientRect();
                const deltaX = ev.pageX - positionInfo.left;
                const deltaY = ev.pageY - positionInfo.top;
                this.bubbleGrabOffset = { x: deltaX, y: deltaY };

                if (ev.altKey) {
                    // Resize action started. Save some information from the initial click for later.
                    this.resizeMode = this.getResizeMode(bubble.content, ev);

                    const bubbleContentJQuery = $(bubble.content);
                    this.initialClickPos = {
                        clickX: ev.pageX,
                        clickY: ev.pageY,
                        elementX: positionInfo.left,
                        elementY: positionInfo.top,
                        // Use JQuery here to have consistent calculations with the rest of the code.
                        // Jquery width(): Only the Content width. (No padding, border, scrollbar, or margin)
                        // Javascript clientWidth: Content plus Padding. (No border, scrollbar, or margin)
                        // Javascript offsetWidth: Content, Padding, Border, and scrollbar. (No margin
                        // References:
                        //   https://www.w3schools.com/jsref/prop_element_clientheight.asp
                        //   https://www.w3schools.com/jsref/prop_element_offsetheight.asp
                        width: bubbleContentJQuery.width(),
                        height: bubbleContentJQuery.height()
                    };
                } else {
                    // Even though Alt+Drag resize is not in effect, we still ensure JQuery Resizing is not in effect
                    if (!this.isResizing(container)) {
                        container.classList.add("grabbing");
                    }
                }
            }
        };

        container.onmousemove = (ev: MouseEvent) => {
            // Prevent two event handlers from triggering if the text box is currently being resized
            if (this.isResizing(container)) {
                this.draggedBubble = undefined;
                return;
            }

            // ENHANCE: Re-visit what should happen if you start a resize, then move the mouse out of the image container, then come back and continue resizing.
            //   Currently resizing ends when the mouse is moved out of the image container.
            //   That's because the Drag Move code sets this.draggedBubble to undefined.
            //   Maybe it shouldn't?

            if (this.draggedBubble) {
                // A bubble is currently in drag mode, and the mouse is being moved.
                // Move the bubble accordingly.
                if (ev.altKey) {
                    const content = $(this.draggedBubble.content);

                    const positionInfo = content[0].getBoundingClientRect();
                    const oldLeft = positionInfo.left;
                    const oldTop = positionInfo.top;
                    // Note: This uses the JQuery width() function, which returns just the width of the element without padding/border.
                    //       The ClientRect width includes the padding and border
                    //       The child functions we later call expect the width without padding/border (because they use JQuery),
                    //       so make sure to pass in the appropriate one
                    const oldWidth = content.width();
                    const oldHeight = content.height();

                    let newLeft = oldLeft;
                    let newTop = oldTop;
                    let newWidth = oldWidth;
                    let newHeight = oldHeight;

                    // Rather than using the current iteration's movementX/Y, we use the distance away from the original click.
                    // This gives behavior consistent with what JQuery resize handles do.
                    // If the user resizes it below the minimum width (which prevents the box from actually getting any smaller),
                    // they will not start immediately expanding the box when they move the mouse back, but only once they reach the minimum width threshold again.
                    const totalMovementX =
                        ev.pageX - this.initialClickPos.clickX;
                    const totalMovementY =
                        ev.pageY - this.initialClickPos.clickY;

                    // Determine the vertical component
                    if (this.resizeMode.charAt(0) == "n") {
                        // The top edge is movable, but the bottom edge is anchored.
                        newTop =
                            ev.pageY -
                            this.initialClickPos.clickY +
                            this.initialClickPos.elementY;
                        newHeight =
                            this.initialClickPos.height - totalMovementY;

                        if (newHeight < this.minTextBoxHeightPx) {
                            newHeight = this.minTextBoxHeightPx;

                            // Even though we capped newHeight, it's still possible that the height shrunk,
                            // so we may possibly still need to adjust the value of 'top'
                            newTop = oldTop + (oldHeight - newHeight);
                        }
                    } else {
                        // The bottom edge is moveable, while the top edge is anchored.
                        newHeight =
                            this.initialClickPos.height + totalMovementY;
                    }

                    // Determine the horizontal component
                    if (this.resizeMode.charAt(1) == "w") {
                        // The left edge is movable, but the right edge is anchored.
                        newLeft =
                            ev.pageX -
                            this.initialClickPos.clickX +
                            this.initialClickPos.elementX;
                        newWidth = this.initialClickPos.width - totalMovementX;

                        if (newWidth < this.minTextBoxWidthPx) {
                            newWidth = this.minTextBoxWidthPx;

                            // Even though we capped newWidth, it's still possible that the width shrunk,
                            // so we may possibly still need to adjust left
                            newLeft = oldLeft + (oldWidth - newWidth);
                        }
                    } else {
                        newWidth = this.initialClickPos.width + totalMovementX;
                        newWidth = Math.max(newWidth, this.minTextBoxWidthPx);
                    }

                    // console.log(
                    //     `Calculated: (${newLeft}, ${newTop}) with w,h= (${newWidth}, ${newHeight})`
                    // );

                    if (
                        newTop == oldTop &&
                        newLeft == oldLeft &&
                        newWidth == oldWidth &&
                        newHeight == oldHeight
                    ) {
                        // Nothing changed. Abort early to try to avoid rounding errors or minor discrepancies from accumulating
                        return;
                    }

                    content.width(newWidth);
                    content.height(newHeight);

                    this.calculateAndFixInitialLocation(
                        $(this.draggedBubble.content),
                        $(container),
                        newLeft,
                        newTop
                    );

                    // ENHANCE: Get the final value to match up perfectly with the Calculated value.
                    // If you wiggle the mouse up and down over and over and over,
                    // you can observe the text box will be moved downwards slowly but steadily.
                    // This seems to indicate that it's not rounding error, which should manifest as a random 1 pixel up or 1 pixel down.
                    // The slow erosion downward seems to indicate that calculateAndFixInitialLocation() has a small but systemic bias.
                    // const positionInfo2 = this.draggedBubble.content.getBoundingClientRect();
                    // console.log(
                    //     `Final: (${positionInfo2.left}, ${
                    //         positionInfo2.top
                    //     }) with w/h (${$(
                    //         this.draggedBubble.content
                    //     ).width()}, ${$(this.draggedBubble.content).height()})`
                    // );
                } else {
                    this.calculateAndFixInitialLocation(
                        $(this.draggedBubble.content),
                        $(container),
                        ev.pageX - this.bubbleGrabOffset.x, // These coordinates need to be relative to the document
                        ev.pageY - this.bubbleGrabOffset.y
                    );
                }
            } else {
                // Not currently dragging
                // Determine whether there is something under the mouse that could be dragged/resized,
                // and add or remove the class we use to indicate this
                const coordinates = this.getContainerCoordinates(
                    ev,
                    containerBounds,
                    styleInfo
                );
                if (!coordinates) {
                    this.cleanupMouseMoveHover(container);
                    return;
                }
                const [targetX, targetY] = coordinates;

                if (this.isEventForEditableOnly(ev)) {
                    this.cleanupMouseMoveHover(container);
                    return;
                }

                this.setHoverMouseCursor(
                    ev,
                    container,
                    targetX,
                    targetY,
                    ev.altKey
                );
            }

            // ENHANCE: If you first select the text in a text-over-picture, then Ctrl+drag it, you will both drag the bubble and drag the text.
            //   Ideally I'd like to only handle the drag the bubble event
            //   I tried to move the event handler to the preliminary Capture phase, then use stopPropagation, cancelBubble, and/or PreventDefault
            //   to stop the event from reaching the target element (the paragraph or the .bloom-editable div or whatever).
            //   Unfortunately, while it prevented the event handlers in the subsequent Bubble phase from being fired,
            //   this funny dual-drag behavior would still happen.  I don't have a solution for that yet.
        };

        container.onmouseup = (ev: MouseEvent) => {
            // ENHANCE: If you release the mouse outside of the container, it is not registered as a mouseup here.
            //          The bubble will continue to be dragged inside the container until you click and release.
            this.draggedBubble = undefined;
            this.resizeMode = "";
            container.classList.remove("grabbing");
        };

        // The container's onmousemove handler isn't capable of reliably detecting in all cases when it goes out of bounds, because
        // the mouse is no longer over the container.
        // So need a handler on the .bloom-page instead, which surrounds the image container.
        const currentPageElement = container.closest(".bloom-page");
        if (currentPageElement) {
            (currentPageElement as HTMLElement).onmousemove = (
                ev: MouseEvent
            ) => {
                if (!this.draggedBubble) {
                    return;
                }

                // Oops, the mouse cursor has left the image container
                // Current requirements are to end the drag in this case
                if (
                    ev.pageX < containerBounds.left ||
                    ev.pageX > containerBounds.right ||
                    ev.pageY < containerBounds.top ||
                    ev.pageY > containerBounds.bottom
                ) {
                    // FYI: If you use the drag handle (which uses the JQuery drag handle), it enforces the content box to stay entirely within the imageContainer.
                    // This code currently doesn't do that.
                    this.draggedBubble = undefined;
                    container.classList.remove("grabbing");
                }
            };
        }
    }

    // Returns true if any of the container's children are currently being resized
    private isResizing(container: HTMLElement) {
        // First check if we have our custom class indicator applied
        if (container.classList.contains("bloom-resizing")) {
            return true;
        }

        // Double-check using the JQuery class
        return (
            container.getElementsByClassName("ui-resizable-resizing").length > 0
        );
    }

    private isEventForEditableOnly(ev): boolean {
        if (ev.ctrlKey || ev.altKey) {
            return false;
        }

        const targetElement = ev.target as HTMLElement;
        const isInsideEditable = !!targetElement.closest(".bloom-editable");
        return isInsideEditable;
    }

    // Gets the coordinates of the specified event relative to the container.
    private getContainerCoordinates(
        event: MouseEvent,
        containerBounds: ClientRect | DOMRect,
        styleInfo: CSSStyleDeclaration
    ): number[] | undefined {
        const targetElement = event.target as HTMLElement;
        if (!(typeof targetElement.getBoundingClientRect === "function")) {
            return undefined;
        }
        const targetBounds = targetElement.getBoundingClientRect();

        const [x, y] = this.getCoordinatesRelativeTo(
            event.offsetX,
            event.offsetY,
            targetBounds,
            containerBounds,
            styleInfo
        );

        return [x, y];
    }

    // Recomputes the coordinates of element relative to the specified origin's info
    private getCoordinatesRelativeTo(
        elementX: number, // elementX: The offsetX relative to the element's top left.
        elementY: number, // elementY: The offsetY relative to the element's top left.
        elementBounds: ClientRect | DOMRect, // The Bounding Client Rectangle of the element
        originBounds: ClientRect | DOMRect, // The BoundingClientRectangle of the HTMLElement whose top-left and right will be treated as the new origin
        originStyleInfo: CSSStyleDeclaration // The computed style of the origin
    ): number[] {
        // ENHANCE: Might need to account for padding later too? Not sure.
        // ENHANCE: Do we need to adjust elementX/elementY if the element has a non-zero border width?
        const borderLeft: number = TextOverPictureManager.extractNumber(
            originStyleInfo.getPropertyValue("border-left-width")
        );
        const borderTop: number = TextOverPictureManager.extractNumber(
            originStyleInfo.getPropertyValue("border-top-width")
        );

        const relativeX = elementBounds.left - originBounds.left - borderLeft;
        const relativeY = elementBounds.top - originBounds.top - borderTop;

        return [relativeX + elementX, relativeY + elementY];
    }

    // Removes the units from a string like "10px"
    public static extractNumber(text: string | undefined | null): number {
        if (!text) {
            return 0;
        }

        let i = 0;
        for (i = 0; i < text.length; ++i) {
            const c = text.charAt(i);
            if ((c < "0" || c > "9") && c != "-" && c != "+" && c != ".") {
                break;
            }
        }

        let numberStr = "";
        if (i > 0) {
            // At this point, i points to the first non-numeric character in the string
            numberStr = text.substring(0, i);
        }

        return Number(numberStr);
    }

    public setHoverMouseCursor(
        ev: MouseEvent,
        container: HTMLElement,
        targetX: number,
        targetY: number,
        isResizeMode: boolean
    ): void {
        const hoveredBubble = Comical.getBubbleHit(container, targetX, targetY);
        if (!hoveredBubble) {
            // Cleanup the previous iteration's state
            this.cleanupMouseMoveHover(container);
            return;
        }

        // Over a bubble that could be dragged (ignoring the bloom-editable portion).
        // Make the mouse indicate that dragging/resizing is possible
        if (isResizeMode) {
            const resizeMode = this.getResizeMode(hoveredBubble.content, ev);

            this.cleanupMouseMoveHover(container); // Need to clear both grabbable and *-resizables
            container.classList.add(`${resizeMode}-resizable`);
        } else {
            container.classList.add("grabbable");
        }
    }

    public getResizeMode(element: HTMLElement, event: MouseEvent): string {
        // Convert into a coordinate system where the origin is the center of the element (rather than the top-left of the page)
        const center = this.getCenterPosition(element);
        const clickCoordinates = { x: event.pageX, y: event.pageY };
        const relativeCoordinates = {
            x: clickCoordinates.x - center.x,
            y: clickCoordinates.y - center.y
        };

        let resizeMode: string;
        if (relativeCoordinates.y! < 0) {
            if (relativeCoordinates.x! >= 0) {
                resizeMode = "ne"; // top-right
            } else {
                resizeMode = "nw"; // top-left
            }
        } else {
            if (relativeCoordinates.x! < 0) {
                resizeMode = "sw"; // bottom-left
            } else {
                resizeMode = "se"; // bottom-right
            }
        }

        return resizeMode;
    }

    // Calculates the center of an element
    public getCenterPosition(element: HTMLElement): { x: number; y: number } {
        const positionInfo = element.getBoundingClientRect();
        const centerX = positionInfo.left + positionInfo.width / 2;
        const centerY = positionInfo.top + positionInfo.height / 2;

        return { x: centerX, y: centerY };
    }

    private cleanupMouseMoveHover(element: HTMLElement): void {
        element.classList.remove("grabbable");
        this.clearResizeModeClasses(element);
    }

    private clearResizeModeClasses(element: HTMLElement): void {
        element.classList.remove("ne-resizable");
        element.classList.remove("nw-resizable");
        element.classList.remove("sw-resizable");
        element.classList.remove("se-resizable");
    }

    public turnOffHidingImageButtons() {
        Array.from(
            document.getElementsByClassName("bloom-hideImageButtons")
        ).forEach(e => e.classList.remove("bloom-hideImageButtons"));
    }

    public turnOffBubbleEditing(): void {
        if (this.isCalloutEditingOn === false) {
            return; // Already off. No work needs to be done.
        }
        this.isCalloutEditingOn = false;

        Comical.setActiveBubbleListener(undefined);
        Comical.stopEditing();

        this.turnOffHidingImageButtons();

        // Clean up event listeners that we no longer need
        Array.from(document.getElementsByClassName("bloom-editable")).forEach(
            element => {
                element.removeEventListener(
                    "focus",
                    TextOverPictureManager.onFocusSetActiveElement
                );
            }
        );
        document.removeEventListener(
            "click",
            TextOverPictureManager.onDocClickClearActiveElement
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
            // TODO: Need an official string
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
        const containerStyle = window.getComputedStyle(container[0]);
        const containerBorderLeft: number = TextOverPictureManager.extractNumber(
            containerStyle.getPropertyValue("border-left-width")
        );
        const containerBorderTop: number = TextOverPictureManager.extractNumber(
            containerStyle.getPropertyValue("border-top-width")
        );

        const xOffset =
            (mouseX - containerPosition.left - containerBorderLeft) / scale;
        const yOffset =
            (mouseY - containerPosition.top - containerBorderTop) / scale;

        // Note: This code will not clear out the rest of the style properties... they are preserved.
        //       If some or all style properties need to be removed before doing this processing, it is the caller's responsibility to do so beforehand
        //       The reason why we do this is because a bubble's onmousemove handler calls this function,
        //       and in that case we want to preserve the bubble's width/height which are set in the style
        wrapperBox.css("left", xOffset); // assumes numbers are in pixels
        wrapperBox.css("top", yOffset); // assumes numbers are in pixels

        TextOverPictureManager.calculatePercentagesAndFixTextboxPosition(
            wrapperBox
        ); // translate px to %
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    public deleteFloatingTOPBox(mouseX: number, mouseY: number) {
        const clickedElement = document.elementFromPoint(mouseX, mouseY);
        if (clickedElement) {
            const textElement = clickedElement.closest(
                ".bloom-textOverPicture"
            );
            this.deleteTOPBox(textElement);
        }
    }

    public deleteTOPBox(textElement: Element | null) {
        if (textElement && textElement.parentElement) {
            const wasComicalModified =
                textElement.parentElement.getElementsByClassName(
                    kComicalGeneratedClass
                ).length > 0;

            const parent = textElement.parentElement;
            parent.removeChild(textElement);

            if (wasComicalModified) {
                Comical.update(parent);
            }

            // Check if we're deleting the active bubble. If so, gotta clean up the state.
            if (textElement == this.getActiveElement()) {
                this.setActiveElement(undefined);
            }
        }
    }

    private makeTOPBoxesDraggableAndClickable(
        thisTOPBoxes: JQuery,
        scale: number
    ): void {
        thisTOPBoxes.each((index, element) => {
            const thisTOPBox = $(element);
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
                    thisTOPBox
                        .find(".bloom-dragHandleTOP")
                        .addClass("grabbing");
                },
                handle: ".bloom-dragHandleTOP",
                stop: (event, ui) => {
                    const target = event.target;
                    if (target) {
                        TextOverPictureManager.calculatePercentagesAndFixTextboxPosition(
                            $(target)
                        );
                    }

                    thisTOPBox
                        .find(".bloom-dragHandleTOP")
                        .removeClass("grabbing");
                }
            });

            thisTOPBox.find(".bloom-editable").click(function(e) {
                this.focus();
            });
        });
    }

    public initializeTextOverPictureEditing(): void {
        this.makeTextOverPictureBoxDraggableClickableAndResizable();
        this.turnOnBubbleEditing();
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
            handles: "all",
            // ENHANCE: Maybe we should add a containtment option here?
            //   If we don't let you drag one of these outside the image container, maybe we shouldn't let you resize one outside either
            // resize: (event, ui) => {
            //     ENHANCE: Workaround for the following bug
            //       If the text over picture element is at minimum height, and then you use the top (North) edge to resize DOWN (that is, making it smaller)
            //       the element will be DRAGGED down a little ways. (It seems to be dragged down until the top edge reaches the original bottom edge position).
            //       I managed to confirm that this is registering as a RESIZE event, not the Bloom mousemove ("drag") event or the JQuery DRAG event
            //       Oddly enough, this does not occur on the bottom edge. (FYI, the bottom edge defaults to on if you don't explicitly set "handles", but the top edge doesn't).
            // },
            stop: (event, ui) => {
                const target = event.target as Element;
                if (target) {
                    // Resizing also changes size and position to pixels. Fix it.
                    TextOverPictureManager.calculatePercentagesAndFixTextboxPosition(
                        $(target)
                    );
                    // There was a problem where resizing a box messed up its draggable containment,
                    // so now after we resize we go back through making it draggable and clickable again.
                    this.makeTOPBoxesDraggableAndClickable($(target), scale);

                    // Clear the custom class used to indicate that a resize action may have been started
                    TextOverPictureManager.clearResizingClass(target);
                }
            }
        });

        // Normally JQuery adds the class "ui-resizable-resizing" to a Resizable when it is resizing,
        // but this does not occur until the BUBBLE (normal) phase when the mouse is first MOVED (not when it is first clicked).
        // This means that any children's onmousemove functions will fire first, before the handle.
        // That means that children may incorrectly perceive no resize as happening, when there is in fact a resize going on.
        // So, we set a custom indicator on it during the mousedown event, before the mousemove starts happening.
        for (let i = 0; i < textOverPictureElems.length; ++i) {
            const textOverPicElement = textOverPictureElems[i];
            const handles = textOverPicElement.getElementsByClassName(
                "ui-resizable-handle"
            );
            for (let i = 0; i < handles.length; ++i) {
                const handle = handles[i];

                handle.addEventListener(
                    "mousedown",
                    TextOverPictureManager.addResizingClassHandler
                );

                // Even though we clear it in the JQuery Resize Stop handler, we also need one here
                // because if the mouse is depressed and then released (without moving), we do want this class applied temporarily
                // but we also need to make sure it gets cleaned up, even though no formal Resize Start/Stop events occurred.
                handle.addEventListener(
                    "mouseup",
                    TextOverPictureManager.clearResizingClassHandler
                );
            }
        }

        this.makeTOPBoxesDraggableAndClickable(textOverPictureElems, scale);
    }

    // An event handler that adds the "bloom-resizing" class to the image container.
    private static addResizingClassHandler(event: MouseEvent) {
        const handle = event.currentTarget as Element;

        const container = handle.closest(".bloom-imageContainer");
        if (container) {
            container.classList.add("bloom-resizing");
        }
    }

    // An event handler that adds the "bloom-resizing" class to the image container.
    private static clearResizingClassHandler(event: MouseEvent) {
        TextOverPictureManager.clearResizingClass(
            event.currentTarget as Element
        );
    }

    private static clearResizingClass(element: Element) {
        const container = element.closest(".bloom-imageContainer");
        if (container) {
            container.classList.remove("bloom-resizing");
        }
    }

    private static calculatePercentagesAndFixTextboxPosition(
        wrapperBox: JQuery
    ) {
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
