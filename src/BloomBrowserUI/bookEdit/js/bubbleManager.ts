// This class makes it possible to add and delete textboxes that float over images. These floating
// textboxes are intended for use in making comic books, but could also be useful in the case of
// any book that uses a picture where there is space for text within the bounds of the picture.
///<reference path="../../typings/jquery/jquery.d.ts"/>

import { EditableDivUtils } from "./editableDivUtils";
import { BloomApi } from "../../utils/bloomApi";
import {
    Bubble,
    BubbleSpec,
    BubbleSpecPattern,
    Comical,
    TailSpec
} from "comicaljs";
import { Point, PointScaling } from "./point";
import { isLinux } from "../../utils/isLinux";
import { reportError } from "../../lib/errorHandler";
import { getRgbaColorStringFromColorAndOpacity } from "../toolbox/comic/comicToolColorHelper";

const kComicalGeneratedClass: string = "comical-generated";
const kTextOverPictureClass = "bloom-textOverPicture";
const kTextOverPictureSelector = `.${kTextOverPictureClass}`;

// references to "TOP" in the code refer to the actual TextOverPicture box (what "Bubble"s were originally called) installed in the Bloom page.
export class BubbleManager {
    // The min width/height needs to be kept in sync with the corresponding values in bubble.less
    public minTextBoxWidthPx = 30;
    public minTextBoxHeightPx = 30;

    private activeElement: HTMLElement | undefined;
    private isComicEditingOn: boolean = false;
    private notifyBubbleChange:
        | ((x: BubbleSpec | undefined) => void)
        | undefined;

    // These variables are used by the bubble's onmouse* event handlers
    private bubbleToDrag: Bubble | undefined; // Use Undefined to indicate that there is no active drag in progress
    private bubbleDragGrabOffset: { x: number; y: number } = { x: 0, y: 0 };
    private activeContainer: HTMLElement | undefined;

    private bubbleToResize: Bubble | undefined; // Use Undefined to indicate that there is no active resize in progress
    private bubbleResizeMode: string;
    private bubbleResizeInitialPos: {
        clickX: number;
        clickY: number;
        elementX: number;
        elementY: number;
        width: number;
        height: number;
    };

    public initializeBubbleManager(): void {
        // currently nothing to do; used to set up web socket listener
        // for right-click messages to add and delete TOP boxes.
        // Keeping hook in case we want it one day...
    }

    public getIsComicEditingOn(): boolean {
        return this.isComicEditingOn;
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
        overflowY: number
    ): boolean {
        const wrapperBox = box.closest(kTextOverPictureSelector) as HTMLElement;
        if (!wrapperBox) {
            return false; // we can't fix it
        }

        const container = wrapperBox.closest(".bloom-imageContainer");
        if (!container) {
            return false; // paranoia; TOP box should always be in image container
        }

        if (overflowY > -6 && overflowY < -2) {
            return false; // near enough, avoid jitter; -4 would be no change, see below.
        }
        // The +4 is based on experiment. It may relate to a couple of 'fudge factors'
        // in OverflowChecker.getSelfOverflowAmounts, which I don't want to mess with
        // as a lot of work went into getting overflow reporting right. We seem to
        // need a bit of extra space to make sure the last line of text fits.
        // The 27 is the minimumSize that CSS imposes on TOP boxes; it may cause
        // Comical some problems if we try to set the actual size smaller.
        // (I think I saw background gradients behaving strangely, for example.)
        let newHeight = Math.max(wrapperBox.clientHeight + overflowY + 4, 27);
        if (overflowY < -4) {
            if (!wrapperBox.classList.contains("bloom-allowAutoShrink")) {
                return false; // currently auto-shrink is only allowed when manually changing width
            }
            // the scrollSize property that overflowY is based on is not useful when it's nowhere
            // near overflowing. So figure the new size we need another way.
            // This is not ideal, we're not going to get the exact same
            // size when the bubble is shrinking as when it's growing. But in practice it
            // seems near enough for automatic sizing. The user can take over and fine tune
            // it if desired.
            let maxContentBottom = 0;
            Array.from(box.children).forEach((x: HTMLElement) => {
                if (!(x instanceof HTMLElement)) return; // not an element
                if (window.getComputedStyle(x).position === "absolute") return; // special element like format button
                const xbottom = x.offsetTop + x.offsetHeight;
                if (xbottom > maxContentBottom) {
                    maxContentBottom = xbottom;
                }
            });
            newHeight = Math.max(maxContentBottom + box.scrollTop + 4, 27);
        }

        // If a lot of text is pasted, the container will scroll down.
        //    (This can happen even if the text doesn't necessarily go out the bottom of the image container).
        // The children of the container (e.g. img and canvas elements) will be offset above the image container.
        // This is an annoying situation, both visually for the image and in terms of computing the correct position for JQuery draggables.
        // So instead, we force the container to scroll back to the top.
        container.scrollTop = 0;

        // Check if required height exceeds available height
        if (newHeight + wrapperBox.offsetTop > container.clientHeight) {
            // ENHANCE: Would be nice if this set the height up to the max
            //          But it probably requires some changes to what the return value represents and how the caller should deal.
            //          Maybe we should return an adjusted overflowY instead of a boolean.
            return false;
        }

        wrapperBox.style.height = newHeight + "px"; // next line will change to percent
        BubbleManager.convertTextboxPositionToPercentage(wrapperBox, container);
        return true;
    }

    public static hideImageButtonsIfNotPlaceHolder(container: HTMLElement) {
        const images = Array.from(container.getElementsByTagName("img"));
        if (
            !images.some(img => img.getAttribute("src") === "placeHolder.png")
        ) {
            container.classList.add("bloom-hideImageButtons");
        }
    }

    public turnOnHidingImageButtons() {
        const imageContainers: HTMLElement[] = Array.from(
            document.getElementsByClassName("bloom-imageContainer") as any
        );
        imageContainers.forEach(e => {
            BubbleManager.hideImageButtonsIfNotPlaceHolder(e);
        });
    }

    private focusFirstVisibleEditable(activeElement: HTMLElement) {
        const focusElements = Array.from(
            activeElement.getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )
        ).filter(el => !el.parentElement!.classList.contains("box-header-off"));
        if (focusElements.length > 0) {
            (focusElements[0] as HTMLElement).focus();
        }
    }

    public turnOnBubbleEditing(): void {
        if (this.isComicEditingOn === true) {
            return; // Already on. No work needs to be done
        }
        this.isComicEditingOn = true;

        Comical.setActiveBubbleListener(activeElement => {
            if (activeElement) {
                this.focusFirstVisibleEditable(activeElement);
            }
        });

        const imageContainers: HTMLElement[] = Array.from(
            document.getElementsByClassName("bloom-imageContainer") as any
        );
        // todo: select the right one...in particular, currently we just select the last one.
        // This is reasonable when just coming to the page, and when we add a new TOP,
        // we make the new one the last in its parent, so with only one image container
        // the new one gets selected after we refresh. However, once we have more than one
        // image container, I don't think the new TOP box will get selected if it's not on
        // the first image.
        // todo: make sure comical is turned on for the right parent, in case there's more than one image on the page?
        const textOverPictureElems: HTMLElement[] = Array.from(
            document.getElementsByClassName(kTextOverPictureClass) as any
        );
        if (textOverPictureElems.length > 0) {
            this.activeElement = textOverPictureElems[
                textOverPictureElems.length - 1
            ] as HTMLElement;
            // This focus call doesn't seem to work, at least in a lasting fashion.
            // See the code in bloomEditing.ts/SetupElements() that sets focus after
            // calling BloomSourceBubbles.MakeSourceBubblesIntoQtips() in a delayed loop.
            // That code usually finds that nothing is focused. (??)
            // (gjm: I reworked the code that finds a visible element a bit, it's possible the above comment
            // is no longer accurate)
            this.focusFirstVisibleEditable(this.activeElement);
            Comical.setUserInterfaceProperties({ tailHandleColor: "#96668F" }); // light bloom purple
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
                    BubbleManager.onFocusSetActiveElement
                );
            });
            document.addEventListener(
                "click",
                BubbleManager.onDocClickClearActiveElement
            );
        } else {
            // Focus something!
            // BL-8073: if Comic Tool is open, this 'turnOnBubbleEditing()' method will get run.
            // If this particular page has no comic bubbles, we can actually arrive here with the 'body'
            // as the document's activeElement. So we focus the first visible editable we come to.
            const marginBox = document.getElementsByClassName("marginBox");
            if (marginBox.length > 0) {
                this.focusFirstVisibleEditable(marginBox[0] as HTMLElement);
            }
        }

        // turn on various behaviors for each image
        Array.from(
            document.getElementsByClassName("bloom-imageContainer")
        ).forEach((container: HTMLElement) => {
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
                                BubbleManager.onFocusSetActiveElement
                            );
                        }
                        somethingElseToFocus.focus();
                    }
                }
            });

            this.setDragAndDropHandlers(container);
            this.setMouseDragHandlers(container);
        });

        // The container's onmousemove handler isn't capable of reliably detecting in all cases
        // when it goes out of bounds, because the mouse is no longer over the container.
        // So we need a handler on the .bloom-page instead, which surrounds the image container.
        Array.from(document.getElementsByClassName("bloom-page")).forEach(
            (pageElement: HTMLElement) => {
                pageElement.addEventListener(
                    "mousemove",
                    BubbleManager.onPageMouseMove
                );
            }
        );
    }

    private migrateOldTopElems(textOverPictureElems: HTMLElement[]): void {
        textOverPictureElems.forEach(top => {
            if (!top.getAttribute("data-bubble")) {
                const bubbleSpec = Bubble.getDefaultBubbleSpec(top, "none");
                new Bubble(top).setBubbleSpec(bubbleSpec);
                // it would be nice to do this only once, but there MIGHT
                // be TOP elements in more than one image container...too complicated,
                // and this only happens once per TOP.
                Comical.update(
                    top.closest(".bloom-imageContainer") as HTMLElement
                );
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
            initializeBubbleManager();

            const bubbleElement = focusedElement.closest(
                kTextOverPictureSelector
            );
            if (bubbleElement) {
                theOneBubbleManager.setActiveElement(
                    bubbleElement as HTMLElement
                );
            } else {
                theOneBubbleManager.setActiveElement(undefined);
            }
        }
    }

    private static onDocClickClearActiveElement(event: Event) {
        const clickedElement = event.target as Element; // most local thing clicked on
        if (
            clickedElement.closest(".bloom-imageContainer") ||
            clickedElement.closest(".source-copy-button")
        ) {
            // We have other code to handle setting and clearing Comical handles
            // if the click is inside a Comical area.
            // BL-9198 We also have code (in BloomSourceBubbles) to handle clicks on source bubble
            // copy buttons.
            return;
        }
        // If we clicked in the document outside a Comical picture
        // we don't want anything Comical to be active.
        // (We don't use a blur event for this because we don't want to unset
        // the active element for clicks outside the content window, e.g., on the
        // toolbox controls, or even in a debug window. This event handler is
        // attached to the page frame document.)
        theOneBubbleManager.setActiveElement(undefined);
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

    // Set the color of the text in all of the active bubble family's TextOverPicture boxes.
    public setTextColor(hexOrRgbColor: string) {
        const activeEl = theOneBubbleManager.getActiveElement();
        if (activeEl) {
            // First, see if this bubble is in parent/child relationship with any others.
            // We need to set text color on the whole 'family' at once.
            const bubble = new Bubble(activeEl);
            const relatives = Comical.findRelatives(bubble);
            relatives.push(bubble);
            relatives.forEach(bubble =>
                this.setTextColorInternal(hexOrRgbColor, bubble.content)
            );
        }
    }

    private setTextColorInternal(hexOrRgbColor: string, element: HTMLElement) {
        const topBox = element.closest(
            kTextOverPictureSelector
        ) as HTMLDivElement;
        topBox.style.color = hexOrRgbColor;
    }

    public getTextColor(): string {
        const activeEl = theOneBubbleManager.getActiveElement();
        let textColor = "";
        if (activeEl) {
            const topBox = activeEl.closest(
                kTextOverPictureSelector
            ) as HTMLDivElement;
            const style = topBox.style;
            textColor = style && style.color ? style.color : "";
        }
        return textColor;
    }

    // This gives us the patriarch (farthest ancestor) bubble of a family of bubbles.
    // If the active element IS the parent of our family, this returns the active element's bubble.
    public getPatriarchBubbleOfActiveElement(): Bubble | undefined {
        if (!this.activeElement) {
            return undefined;
        }
        const tempBubble = new Bubble(this.activeElement);
        const ancestors = Comical.findAncestors(tempBubble);
        return ancestors.length > 0 ? ancestors[0] : tempBubble;
    }

    public getSpecsOfPatriarchBubble(): BubbleSpec | undefined {
        const tempBubble = this.getPatriarchBubbleOfActiveElement();
        return tempBubble ? tempBubble.getBubbleSpec() : undefined;
    }

    // Set the color of the background in all of the active bubble family's TextOverPicture boxes.
    public setBackgroundColor(colors: string[], opacity: number | undefined) {
        if (!this.activeElement) {
            return;
        }
        const originalActiveElement = this.activeElement;
        const parentBubble = this.getPatriarchBubbleOfActiveElement();
        if (parentBubble) {
            this.setActiveElement(parentBubble.content);
        }
        let newBackgroundColors = colors;
        if (opacity && opacity < 1) {
            newBackgroundColors[0] = getRgbaColorStringFromColorAndOpacity(
                colors[0],
                opacity
            );
        }
        this.updateSelectedItemBubbleSpec({
            backgroundColors: newBackgroundColors
        });
        // reset active element
        this.setActiveElement(originalActiveElement);
    }

    public getBackgroundColorArray(spec: BubbleSpec): string[] {
        let newSpec = spec;
        // First, check to see if this is a child bubble, if so, get the parent's specs.
        const parentBubbleSpec = this.getSpecsOfPatriarchBubble();
        if (parentBubbleSpec) {
            newSpec = parentBubbleSpec;
        }
        if (
            !newSpec.backgroundColors ||
            newSpec.backgroundColors.length === 0
        ) {
            return ["white"];
        }
        return newSpec.backgroundColors;
    }

    // drag-and-drop support for bubbles from comical toolbox
    private setDragAndDropHandlers(container: HTMLElement): void {
        if (isLinux()) return; // these events never fire on Linux: see BL-7958.
        // This suppresses the default behavior, which is to forbid dragging things to
        // an element, but only if the source of the drag is a bloom bubble.
        container.ondragover = ev => {
            if (ev.dataTransfer && ev.dataTransfer.getData("bloomBubble")) {
                ev.preventDefault();
            }
        };
        // Controls what happens when a bloom bubble is dropped. We get the style
        // set in ComicToolControls.ondragstart() and make a bubble with that style
        // at the drop position.
        container.ondrop = ev => {
            // test this so we don't interfere with dragging for text edit,
            // nor add bubbles when something else is dragged
            if (ev.dataTransfer && ev.dataTransfer.getData("bloomBubble")) {
                ev.preventDefault();
                const style = ev.dataTransfer
                    ? ev.dataTransfer.getData("bloomBubble")
                    : "speech";
                this.addFloatingTOPBox(ev.clientX, ev.clientY, style);
                BloomApi.postThatMightNavigate(
                    "common/saveChangesAndRethinkPageEvent"
                );
            }
        };
    }

    // Setup event handlers that allow the bubble to be moved around or resized.
    private setMouseDragHandlers(container: HTMLElement): void {
        // We use mousemove effects instead of drag due to concerns that drag effects would make the entire image container appear to drag.
        // Instead, with mousemove, we can make only the specific bubble move around
        container.onmousedown = (event: MouseEvent) => {
            this.onMouseDown(event, container);
        };

        container.onmousemove = (event: MouseEvent) => {
            this.onMouseMove(event, container);
        };

        container.onmouseup = (event: MouseEvent) => {
            this.onMouseUp(event, container);
        };

        container.onkeypress = (event: Event) => {
            // If the user is typing in a bubble, make sure automatic shrinking is off.
            // Automatic shrinking while typing might be useful when originally authoring a comic,
            // but it's a nuisance when translating one, as the bubble is initially empty
            // and shrinks to one line, messing up the whole layout.
            if (!event.target || !(event.target as Element).closest) return;
            const topBox = (event.target as Element).closest(
                kTextOverPictureSelector
            ) as HTMLElement;
            if (!topBox) return;
            topBox.classList.remove("bloom-allowAutoShrink");
        };
    }

    // Checks to see if the mouse has gone outside of the active container
    private static onPageMouseMove(event: MouseEvent) {
        // Ensures the singleton is ready. (Normally basically a NO-OP because it should already be initialized)
        initializeBubbleManager();

        if (
            !theOneBubbleManager.bubbleToDrag ||
            !theOneBubbleManager.activeContainer
        ) {
            return;
        }

        const container = theOneBubbleManager.activeContainer;
        const containerBounds = container.getBoundingClientRect();

        // Oops, the mouse cursor has left the image container
        // Current requirements are to end the drag in this case
        // If adjusting this, be careful to use pairs of event/object properties
        // that are consistent even if the page is scrolled and/or zoomed.
        if (
            event.clientX < containerBounds.left ||
            event.clientX > containerBounds.right ||
            event.clientY < containerBounds.top ||
            event.clientY > containerBounds.bottom
        ) {
            // FYI: If you use the drag handle (which uses JQuery), it enforces the content box to stay entirely within the imageContainer.
            // This code currently doesn't do that.
            theOneBubbleManager.bubbleToDrag = undefined;
            theOneBubbleManager.activeContainer = undefined;
            container.classList.remove("grabbing");
        }

        // Note: Resize is not stopped here. IMO I think this is more natural, and it also lines up with our current JQuery Resize code
        // (Which does not constrain the resize at all. It also lets you to come back into the container and have the resize continue.)
    }

    private onMouseDown(event: MouseEvent, container: HTMLElement) {
        // Let standard clicks on the bloom editable only be processed on the editable
        if (this.isEventForEditableOnly(event)) {
            return;
        }

        // These coordinates need to be relative to the canvas (which is the same as relative to the image container).
        const coordinates = this.getPointRelativeToCanvas(event, container);

        if (!coordinates) {
            return;
        }

        const bubble = Comical.getBubbleHit(
            container,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY()
        );

        if (bubble) {
            const positionInfo = bubble.content.getBoundingClientRect();

            if (!event.altKey) {
                // Move action started
                this.bubbleToDrag = bubble;
                this.activeContainer = container;

                // Remember the offset between the top-left of the content box and the initial location of the mouse pointer
                const deltaX = event.pageX - positionInfo.left;
                const deltaY = event.pageY - positionInfo.top;
                this.bubbleDragGrabOffset = { x: deltaX, y: deltaY };

                // Even though Alt+Drag resize is not in effect, we still check using isResizing() to make sure JQuery Resizing is not in effect before proceeding
                if (!this.isResizing(container)) {
                    container.classList.add("grabbing");
                }
            } else {
                // Resize action started. Save some information from the initial click for later.
                this.bubbleToResize = bubble;

                // Save the resize mode. Later on, based on what the initial resize mode was, we'll parse the string
                // and determine how to calculate the new boundaries of the content box.
                this.bubbleResizeMode = this.getResizeMode(
                    bubble.content,
                    event
                );
                this.cleanupMouseMoveHover(container); // Need to clear both grabbable and *-resizables
                container.classList.add(`${this.bubbleResizeMode}-resizable`);

                const bubbleContentJQuery = $(bubble.content);
                this.bubbleResizeInitialPos = {
                    clickX: event.pageX,
                    clickY: event.pageY,
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
            }
        }
    }

    private onMouseMove(event: MouseEvent, container: HTMLElement) {
        // Prevent two event handlers from triggering if the text box is currently being resized
        if (this.isResizing(container)) {
            this.bubbleToDrag = undefined;
            this.activeContainer = undefined;
            return;
        }

        if (!this.bubbleToDrag && !this.bubbleToResize) {
            this.handleMouseMoveHover(event, container);
        } else if (this.bubbleToDrag) {
            this.handleMouseMoveDragBubble(event, container);
        } else {
            this.handleMouseMoveResizeBubble(event, container);
        }
    }

    // Mouse hover - No move or resize is currently active, but check if there is a bubble under the mouse that COULD be
    // and add or remove the classes we use to indicate this
    private handleMouseMoveHover(event: MouseEvent, container: HTMLElement) {
        const coordinates = this.getPointRelativeToCanvas(event, container);
        if (!coordinates) {
            this.cleanupMouseMoveHover(container);
            return;
        }

        if (this.isEventForEditableOnly(event)) {
            this.cleanupMouseMoveHover(container);
            return;
        }

        const hoveredBubble = Comical.getBubbleHit(
            container,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY()
        );

        if (!hoveredBubble) {
            // Cleanup the previous iteration's state
            this.cleanupMouseMoveHover(container);
            return;
        }

        // Over a bubble that could be dragged (ignoring the bloom-editable portion).
        // Make the mouse indicate that dragging/resizing is possible
        if (!event.altKey) {
            container.classList.add("grabbable");
        } else {
            const resizeMode = this.getResizeMode(hoveredBubble.content, event);

            this.cleanupMouseMoveHover(container); // Need to clear both grabbable and *-resizables
            container.classList.add(`${resizeMode}-resizable`);
        }
    }

    // A bubble is currently in drag mode, and the mouse is being moved.
    // Move the bubble accordingly.
    private handleMouseMoveDragBubble(
        event: MouseEvent,
        container: HTMLElement
    ) {
        if (!this.bubbleToDrag) {
            console.assert(false, "bubbleToDrag is undefined");
            return;
        }

        const newPosition = new Point(
            event.pageX - this.bubbleDragGrabOffset.x,
            event.pageY - this.bubbleDragGrabOffset.y,
            PointScaling.Scaled,
            "Created by handleMouseMoveDragBubble()"
        );
        this.placeElementAtPosition(
            $(this.bubbleToDrag.content),
            $(container),
            newPosition
        );

        // ENHANCE: If you first select the text in a text-over-picture, then Ctrl+drag it, you will both drag the bubble and drag the text.
        //   Ideally I'd like to only handle the drag-the-bubble event, not the drag-the-text event.
        //   I tried to move the event handler to the preliminary Capture phase, then use stopPropagation, cancelBubble, and/or PreventDefault
        //   to stop the event from reaching the target element (the paragraph or the .bloom-editable div or whatever).
        //   Unfortunately, while it prevented the event handlers in the subsequent Bubble phase from being fired,
        //   this funny dual-drag behavior would still happen.  I don't have a solution for that yet.
    }

    // Resizes the current bubble
    private handleMouseMoveResizeBubble(
        event: MouseEvent,
        container: HTMLElement
    ) {
        if (!this.bubbleToResize) {
            console.assert(false, "bubbleToResize is undefined");
            return;
        }

        const content = $(this.bubbleToResize.content);

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
        const totalMovement = new Point(
            event.pageX - this.bubbleResizeInitialPos.clickX,
            event.pageY - this.bubbleResizeInitialPos.clickY,
            PointScaling.Scaled,
            "PageX - ClickX (Scaled)"
        );

        // Determine the vertical component
        if (this.bubbleResizeMode.charAt(0) == "n") {
            // The top edge is moving, but the bottom edge is anchored.
            newTop =
                this.bubbleResizeInitialPos.elementY +
                totalMovement.getScaledY();
            newHeight =
                this.bubbleResizeInitialPos.height -
                totalMovement.getUnscaledY();

            if (newHeight < this.minTextBoxHeightPx) {
                newHeight = this.minTextBoxHeightPx;

                // Even though we capped newHeight, it's still possible that the height shrunk,
                // so we may possibly still need to adjust the value of 'top'
                newTop = oldTop + (oldHeight - newHeight);
            }
        } else {
            // The bottom edge is moving, while the top edge is anchored.
            newHeight =
                this.bubbleResizeInitialPos.height +
                totalMovement.getUnscaledY();
            newHeight = Math.max(newHeight, this.minTextBoxHeightPx);
        }

        // Determine the horizontal component
        if (this.bubbleResizeMode.charAt(1) == "w") {
            // The left edge is moving, but the right edge is anchored.
            newLeft =
                this.bubbleResizeInitialPos.elementX +
                totalMovement.getScaledX();
            newWidth =
                this.bubbleResizeInitialPos.width -
                totalMovement.getUnscaledX();

            if (newWidth < this.minTextBoxWidthPx) {
                newWidth = this.minTextBoxWidthPx;

                // Even though we capped newWidth, it's still possible that the width shrunk,
                // so we may possibly still need to adjust left
                newLeft = oldLeft + (oldWidth - newWidth);
            }
        } else {
            // The right edge is moving, but the left edge is anchored.
            newWidth =
                this.bubbleResizeInitialPos.width +
                totalMovement.getUnscaledX();
            newWidth = Math.max(newWidth, this.minTextBoxWidthPx);
        }

        if (
            newTop == oldTop &&
            newLeft == oldLeft &&
            newWidth == oldWidth &&
            newHeight == oldHeight
        ) {
            // Nothing changed, can abort early.
            return;
        }

        // If the drag changed the height of the bubble, we'd better turn off
        // the behavior that will otherwise immediately force it back to
        // the standard height!
        if (newHeight !== oldHeight) {
            content.get(0).classList.remove("bloom-allowAutoShrink");
        }

        // Width/Height should use unscaled units
        content.width(newWidth);
        content.height(newHeight);

        const newPoint = new Point(
            newLeft,
            newTop,
            PointScaling.Scaled,
            "Created by handleMouseMoveResizeBubble()"
        );
        this.placeElementAtPosition(content, $(container), newPoint);
    }

    private onMouseUp(event: MouseEvent, container: HTMLElement) {
        this.bubbleToDrag = undefined;
        this.activeContainer = undefined;
        this.bubbleToResize = undefined;
        this.bubbleResizeMode = "";
        container.classList.remove("grabbing");
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
        // I'm not sure what else targetElement can be, but have seen JS errors
        // when closest is not defined.
        const isInsideEditable = !!(
            targetElement.closest && targetElement.closest(".bloom-editable")
        );
        return isInsideEditable;
    }

    // Gets the coordinates of the specified event relative to the canvas element.
    private getPointRelativeToCanvas(
        event: MouseEvent,
        container: Element
    ): Point | undefined {
        const canvas = this.getFirstCanvasForContainer(container);
        if (!canvas) {
            return undefined;
        }

        const pointRelativeToViewport = new Point(
            event.clientX,
            event.clientY,
            PointScaling.Scaled,
            "MouseEvent Client (Relative to viewport)"
        );

        return BubbleManager.convertPointFromViewportToElementFrame(
            pointRelativeToViewport,
            canvas
        );
    }

    // Returns the first canvas in the container, or returns undefined if it does not exist.
    private getFirstCanvasForContainer(
        container: Element
    ): HTMLCanvasElement | undefined {
        const collection = container.getElementsByTagName("canvas");
        if (!collection || collection.length <= 0) {
            return undefined;
        }

        return collection.item(0) as HTMLCanvasElement;
    }

    // Gets the coordinates of the specified event relative to the specified element.
    private static convertPointFromViewportToElementFrame(
        pointRelativeToViewport: Point, // The current point, relative to the top-left of the viewport
        element: Element // The element to reference for the new origin
    ): Point {
        const referenceBounds = element.getBoundingClientRect();
        const origin = new Point(
            referenceBounds.left,
            referenceBounds.top,
            PointScaling.Scaled,
            "BoundingClientRect (Relative to viewport)"
        );

        // Origin gives the location of the outside edge of the border. But we want values relative to the inside edge of the padding.
        // So we need to subtract out the border and padding
        // Exterior gives the location of the outside edge of the border. But we want values relative to the inside edge of the padding.
        // So we need to subtract out the border and padding
        const border = BubbleManager.getLeftAndTopBorderWidths(element);
        const padding = BubbleManager.getLeftAndTopPaddings(element);
        const borderAndPadding = border.add(padding);

        // Try not to be scrolled. It's not easy to figure out how to adjust the calculations
        // properly across all zoom levels if the box is scrolled.
        const scroll = BubbleManager.getScrollAmount(element);
        if (scroll.length() > 0.001) {
            const error = new Error(
                `Assert failed. container.scroll expected to be (0, 0), but it was: (${scroll.getScaledX()}, ${scroll.getScaledY()})`
            );
            // Reports a non-fatal passive if on Alpha
            reportError(error.message, error.stack || "");
        }

        const transposedPoint = pointRelativeToViewport
            .subtract(origin)
            .subtract(borderAndPadding);
        return transposedPoint;
    }

    // Gets an element's border width/height of an element
    //   The x coordinate of the point represents the left border width
    //   The y coordinate of the point represents the top border height
    private static getLeftAndTopBorderWidths(element: Element): Point {
        return new Point(
            element.clientLeft,
            element.clientTop,
            PointScaling.Unscaled,
            "Element ClientLeft/Top (Unscaled)"
        );
    }

    // Gets an element's border width/height of an element
    //   The x coordinate of the point represents the right border width
    //   The y coordinate of the point represents the bottom border height
    private static getRightAndBottomBorderWidths(
        element: Element,
        styleInfo?: CSSStyleDeclaration
    ): Point {
        // There is no such field as element.clientRight, so we have to get it from the CSS style info instead.
        if (!styleInfo) {
            styleInfo = window.getComputedStyle(element);
        }

        const borderRight: number = BubbleManager.extractNumber(
            styleInfo.getPropertyValue("border-right-width")
        );
        const borderBottom: number = BubbleManager.extractNumber(
            styleInfo.getPropertyValue("border-bottom-width")
        );

        return new Point(
            borderRight,
            borderBottom,
            PointScaling.Unscaled,
            "Element ClientRight/Bottom (Unscaled)"
        );
    }

    // Gets an element's border width/height
    //   The x coordinate of the point represents the sum of the left and right border width
    //   The y coordinate of the point represents the sum of the top and bottom border width
    private static getCombinedBorderWidths(
        element: Element,
        styleInfo?: CSSStyleDeclaration
    ): Point {
        if (!styleInfo) {
            styleInfo = window.getComputedStyle(element);
        }

        return this.getLeftAndTopBorderWidths(element).add(
            this.getRightAndBottomBorderWidths(element, styleInfo)
        );
    }

    // Given a CSSStyleDeclearation, retrieves the requested padding and converts it to a number
    private static getPadding(
        side: string,
        styleInfo: CSSStyleDeclaration
    ): number {
        const propertyKey = `padding-${side}`;
        const paddingString = styleInfo.getPropertyValue(propertyKey);
        const padding: number = this.extractNumber(paddingString);
        return padding;
    }

    // Gets the padding of an element
    //   The x coordinate of the point represents the left padding
    //   The y coordinate of the point represents the bottom padding
    private static getLeftAndTopPaddings(
        element: Element, // The element to check
        styleInfo?: CSSStyleDeclaration // Optional. If you have it handy, you can pass in the computed style of the element. Otherwise, it will be determined for you
    ): Point {
        if (!styleInfo) {
            styleInfo = window.getComputedStyle(element);
        }

        return new Point(
            this.getPadding("left", styleInfo),
            this.getPadding("top", styleInfo),
            PointScaling.Unscaled,
            "CSSStyleDeclaration padding"
        );
    }

    // Gets the padding of an element
    //   The x coordinate of the point represents the left padding
    //   The y coordinate of the point represents the bottom padding
    private static getRightAndBottomPaddings(
        element: Element, // The element to check
        styleInfo?: CSSStyleDeclaration // Optional. If you have it handy, you can pass in the computed style of the element. Otherwise, it will be determined for you
    ): Point {
        if (!styleInfo) {
            styleInfo = window.getComputedStyle(element);
        }

        return new Point(
            this.getPadding("right", styleInfo),
            this.getPadding("bottom", styleInfo),
            PointScaling.Unscaled,
            "Padding"
        );
    }

    // Gets the padding of an element
    // The x coordinate of the point represents the sum of the left and right padding
    // The y coordinate of the point represents the sum of the top and bottom padding
    private static getCombinedPaddings(
        element: Element,
        styleInfo?: CSSStyleDeclaration
    ): Point {
        if (!styleInfo) {
            styleInfo = window.getComputedStyle(element);
        }

        return this.getLeftAndTopPaddings(element, styleInfo).add(
            this.getRightAndBottomPaddings(element, styleInfo)
        );
    }

    // Gets the sum of an element's borders and paddings
    // The x coordinate of the point represents the sum of the left and right
    // The y coordinate of the point represents the sum of the top and bottom
    private static getCombinedBordersAndPaddings(element: Element): Point {
        const styleInfo = window.getComputedStyle(element);

        const borders = this.getCombinedBorderWidths(element);
        const paddings = this.getCombinedPaddings(element, styleInfo);
        return borders.add(paddings);
    }

    // Returns the amount the element has been scrolled, as a Point
    private static getScrollAmount(element: Element): Point {
        return new Point(
            element.scrollLeft,
            element.scrollTop,
            PointScaling.Unscaled, // I think that imageContainer returns an unscaled amount, but not 100% sure.
            "Element ScrollLeft/Top (Unscaled)"
        );
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

    // Returns a string representing which style of resize to use
    // This is based on where the mouse event is relative to the center of the element
    //
    // The returned string is the directional prefix to the *-resize cursor values
    // e.g., if "ne-resize" would be appropriate, this function will return the "ne" prefix
    // e.g. "ne" = Northeast, "nw" = Northwest", "sw" = Southwest, "se" = Southeast"
    private getResizeMode(element: HTMLElement, event: MouseEvent): string {
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
                resizeMode = "ne"; // NorthEast = top-right
            } else {
                resizeMode = "nw"; // NorthWest = top-left
            }
        } else {
            if (relativeCoordinates.x! < 0) {
                resizeMode = "sw"; // SouthWest = bottom-left
            } else {
                resizeMode = "se"; // SouthEast = bottom-right
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
        if (this.isComicEditingOn === false) {
            return; // Already off. No work needs to be done.
        }
        this.isComicEditingOn = false;

        Comical.setActiveBubbleListener(undefined);
        Comical.stopEditing();

        this.turnOffHidingImageButtons();

        // Clean up event listeners that we no longer need
        Array.from(document.getElementsByClassName("bloom-editable")).forEach(
            element => {
                element.removeEventListener(
                    "focus",
                    BubbleManager.onFocusSetActiveElement
                );
            }
        );
        document.removeEventListener(
            "click",
            BubbleManager.onDocClickClearActiveElement
        );
    }

    public cleanUp(): void {
        // We used to close a WebSocket here; saving the hook in case we need it someday.
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
    ): BubbleSpec | undefined {
        if (!this.activeElement) {
            return undefined;
        }

        const activeBubble = new Bubble(this.activeElement);
        activeBubble.mergeWithNewBubbleProps(newBubbleProps);
        Comical.update(this.activeElement.parentElement!);
        return activeBubble.getBubbleSpec() as BubbleSpec;
    }

    // Note: After reloading the page, you can't have any of your other code execute safely
    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    public addFloatingTOPBoxAndReloadPage(mouseX: number, mouseY: number) {
        this.addFloatingTOPBox(mouseX, mouseY);

        // I tried to do without this... it didn't work. This causes page changes to get saved and fills
        // things in for editing.
        // It causes EditingModel.RethinkPageAndReloadIt() to get run... which eventually causes
        // makeTOPBoxesDraggableClickableAndResizable to get called by bloomEditing.ts.
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
        if (!this.addChildInternal(parentElement, offsetX, offsetY)) {
            return;
        }

        // Need to reload the page to get it editable/draggable/etc,
        // and to get the Comical bubbles consistent with the new bubble specs
        BloomApi.postThatMightNavigate("common/saveChangesAndRethinkPageEvent");
    }

    private addChildInternal(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number
    ): HTMLElement | undefined {
        const newPoint = this.findBestLocationForNewBubble(
            parentElement,
            offsetX,
            offsetY
        );
        if (!newPoint) {
            //toastr.info("Failed to place a new child bubble.");
            return undefined;
        }

        const childElement = this.addFloatingTOPBox(
            newPoint.getScaledX(),
            newPoint.getScaledY()
        );
        if (!childElement) {
            //toastr.info("Failed to place a new child bubble.");
            return undefined;
        }

        // Make sure that the child inherits any non-default text color from the parent bubble
        // (which must be the active element).
        const parentTextColor = this.getTextColor();
        if (parentTextColor !== "") {
            this.setTextColorInternal(parentTextColor, childElement);
        }

        Comical.initializeChild(childElement, parentElement);
        return childElement;
    }

    // The 'new bubble' is either going to be a child of the 'parentElement', or a duplicate of it.
    private findBestLocationForNewBubble(
        parentElement: HTMLElement,
        proposedOffsetX: number,
        proposedOffsetY: number
    ): Point | undefined {
        const parentBoundingRect = parentElement.getBoundingClientRect();

        // // Ensure newX and newY is within the bounds of the container.
        const container = parentElement.closest(".bloom-imageContainer");
        if (!container) {
            //toastr.warning("Failed to create child or duplicate element.");
            return undefined;
        }
        return this.adjustRectToImageContainer(
            container,
            parentBoundingRect.left + proposedOffsetX,
            parentBoundingRect.top + proposedOffsetY,
            parentElement.clientWidth,
            parentElement.clientHeight
        );
    }

    private adjustRectToImageContainer(
        imageContainer: Element,
        x: number,
        y: number,
        width: number,
        height: number
    ): Point {
        const containerBoundingRect = imageContainer.getBoundingClientRect();
        let newX = x;
        let newY = y;

        const bufferPixels = 15;
        if (newX < containerBoundingRect.left) {
            newX = containerBoundingRect.left + bufferPixels;
        } else if (newX + width > containerBoundingRect.right) {
            // ENHANCE: parentElement.clientWidth is just an estimate of the size of the new bubble's width.
            //          It would be better if we could actually plug in the real value of the new bubble's width
            newX = containerBoundingRect.right - width;
        }

        if (newY < containerBoundingRect.top) {
            newY = containerBoundingRect.top + bufferPixels;
        } else if (newY + height > containerBoundingRect.bottom) {
            // ENHANCE: parentElement.clientHeight is just an estimate of the size of the new bubble's height.
            //          It would be better if we could actually plug in the real value of the new bubble's height
            newY = containerBoundingRect.bottom - height;
        }
        return new Point(
            newX,
            newY,
            PointScaling.Scaled,
            "Scaled viewport coordinates"
        );
    }

    // This method looks very similar to 'adjustRectToImageContainer' above, but the tailspec coordinates
    // here are already relative to the imageContainer's coordinates, which introduces some differences.
    private adjustRelativePointToImageContainer(
        imageContainer: Element,
        x: number,
        y: number
    ): Point {
        const containerBoundingRect = imageContainer.getBoundingClientRect();
        let newX = x;
        let newY = y;

        const bufferPixels = 15;
        if (newX < 1) {
            newX = bufferPixels;
        } else if (newX > containerBoundingRect.width) {
            newX = containerBoundingRect.width - bufferPixels;
        }

        if (newY < 1) {
            newY = bufferPixels;
        } else if (newY > containerBoundingRect.height) {
            newY = containerBoundingRect.height - bufferPixels;
        }
        return new Point(
            newX,
            newY,
            PointScaling.Scaled,
            "Scaled viewport coordinates"
        );
    }

    public addFloatingTOPBoxWithScreenCoords(
        screenX: number,
        screenY: number,
        style: string
    ): HTMLElement | undefined {
        const clientX = screenX - window.screenX;
        const clientY = screenY - window.screenY;
        return this.addFloatingTOPBox(clientX, clientY, style);
    }

    private addFloatingTOPBoxFromOriginal(
        offsetX: number,
        offsetY: number,
        originalElement: HTMLElement,
        style?: string
    ): HTMLElement | undefined {
        const imageContainer = originalElement.closest(".bloom-imageContainer");
        if (!imageContainer) {
            return undefined;
        }
        const positionInViewport = new Point(
            offsetX,
            offsetY,
            PointScaling.Scaled,
            "Scaled Viewport coordinates"
        );
        return this.addTOPBoxInternal(
            positionInViewport,
            $(imageContainer),
            style
        );
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
        // initial mouseX, mouseY coordinates are relative to viewport
        const positionInViewport = new Point(
            mouseX,
            mouseY,
            PointScaling.Scaled,
            "Scaled Viewport coordinates"
        );
        return this.addTOPBoxInternal(positionInViewport, container, style);
    }

    private addTOPBoxInternal(
        location: Point,
        imageContainer: JQuery,
        style?: string
    ): HTMLElement {
        // add a draggable text bubble to the html dom of the current page
        const editableDivClasses =
            "bloom-editable bloom-content1 bloom-visibility-code-on Bubble-style";
        const editableDivHtml =
            "<div class='" + editableDivClasses + "' ><p></p></div>";
        const transGroupDivClasses =
            "bloom-translationGroup bloom-leadingElement Bubble-style";
        const transGroupHtml =
            "<div class='" +
            transGroupDivClasses +
            "' data-default-languages='V'>" +
            editableDivHtml +
            "</div>";

        const wrapperHtml =
            "<div class='" +
            kTextOverPictureClass +
            "'>" +
            transGroupHtml +
            "</div>";
        // add textbox as last child of .bloom-imageContainer (BL-7883)
        const lastContainerChild = imageContainer.children().last();
        const wrapperBox = $(wrapperHtml).insertAfter(lastContainerChild);
        this.placeElementAtPosition(wrapperBox, imageContainer, location);

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

    // mouseX and mouseY are the location in the viewport of the mouse
    private getImageContainerFromMouse(mouseX: number, mouseY: number): JQuery {
        const clickElement = document.elementFromPoint(mouseX, mouseY);
        if (!clickElement) {
            // method not specified to return null
            return $();
        }
        return $(clickElement).closest(".bloom-imageContainer");
    }

    // positionInViewport is the position to place the top-left corner of the wrapperBox
    private placeElementAtPosition(
        wrapperBox: JQuery,
        container: JQuery,
        positionInViewport: Point
    ) {
        const newPoint = BubbleManager.convertPointFromViewportToElementFrame(
            positionInViewport,
            container[0]
        );
        const xOffset = newPoint.getUnscaledX();
        const yOffset = newPoint.getUnscaledY();

        // Note: This code will not clear out the rest of the style properties... they are preserved.
        //       If some or all style properties need to be removed before doing this processing, it is the caller's responsibility to do so beforehand
        //       The reason why we do this is because a bubble's onmousemove handler calls this function,
        //       and in that case we want to preserve the bubble's width/height which are set in the style
        wrapperBox.css("left", xOffset); // assumes numbers are in pixels
        wrapperBox.css("top", yOffset); // assumes numbers are in pixels

        BubbleManager.setTextboxPositionAsPercentage(
            wrapperBox,
            xOffset,
            yOffset
        ); // translate px to %
    }

    // mouseX and mouseY are the location in the viewport of the mouse when right-clicking
    // to create the context menu
    public deleteFloatingTOPBox(mouseX: number, mouseY: number) {
        const clickedElement = document.elementFromPoint(mouseX, mouseY);
        if (clickedElement) {
            const textElement = clickedElement.closest(
                kTextOverPictureSelector
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

    public duplicateTOPBox(textElement: Element) {
        if (this.getActiveElement() !== textElement) {
            // something strange going on!
            //toastr.info("Failed to duplicate bubble.");
            return;
        }
        const parent = textElement.parentElement;
        if (parent) {
            // Make sure comical is up-to-date before we clone things.
            if (
                parent.getElementsByClassName(kComicalGeneratedClass).length > 0
            ) {
                Comical.update(parent);
            }
            // Get the patriarch bubble of this comical family. Can only be undefined if no active element.
            const patriarchBubble = this.getPatriarchBubbleOfActiveElement();
            if (patriarchBubble) {
                if (textElement !== patriarchBubble.content) {
                    this.setActiveElement(patriarchBubble.content);
                }
                const bubbleSpecToDuplicate = this.getSelectedItemBubbleSpec();
                if (!bubbleSpecToDuplicate) {
                    // Oddness! Bail!
                    // reset active element to what it was
                    this.setActiveElement(textElement as HTMLElement);
                    //toastr.info("Failed to duplicate bubble.");
                    return;
                }

                this.duplicateBubbleFamily(
                    patriarchBubble,
                    bubbleSpecToDuplicate
                );
            }

            BloomApi.postThatMightNavigate(
                "common/saveChangesAndRethinkPageEvent"
            );
        }
    }

    // Should duplicate all bubbles and their size and relative placement and color, etc.,
    // and the actual text in the bubbles.
    // The 'patriarchSourceBubble' is the head of a family of bubbles to duplicate,
    // although this one bubble may be all there is.
    // The content of 'patriarchSourceBubble' is now the active element.
    // The 'bubbleSpecToDuplicate' param is the bubbleSpec for the patriarch source bubble.
    //
    // When we finish this method, we plan to call 'saveChangesAndRethinkPageEvent' in any case.
    private duplicateBubbleFamily(
        patriarchSourceBubble: Bubble,
        bubbleSpecToDuplicate: BubbleSpec
    ) {
        const sourceElement = patriarchSourceBubble.content;
        const proposedOffset = 15;
        const newPoint = this.findBestLocationForNewBubble(
            sourceElement,
            proposedOffset + sourceElement.clientWidth, // try to not overlap too much
            proposedOffset
        );
        if (!newPoint) {
            //toastr.info("Failed to duplicate bubble.");
            return;
        }
        const patriarchDuplicateElement = this.addFloatingTOPBoxFromOriginal(
            newPoint.getScaledX(),
            newPoint.getScaledY(),
            sourceElement,
            bubbleSpecToDuplicate.style
        );
        if (!patriarchDuplicateElement) {
            //toastr.info("Failed to add duplicate bubble.");
            return;
        }
        patriarchDuplicateElement.style.color = sourceElement.style.color; // preserve text color
        patriarchDuplicateElement.innerHTML = this.safelyCloneHtmlStructure(
            sourceElement
        );
        this.setActiveElement(patriarchDuplicateElement);
        this.matchWidthOfSource(sourceElement, patriarchDuplicateElement);
        const container = patriarchDuplicateElement.closest(
            ".bloom-imageContainer"
        );
        if (!container) {
            return; // highly unlikely!
        }
        const adjustedTailSpec = this.getAdjustedTailSpec(
            container,
            bubbleSpecToDuplicate.tails,
            sourceElement,
            patriarchDuplicateElement
        );
        // This is the bubbleSpec for the brand new (now active) copy of the patriarch bubble.
        // We will overwrite most of it, but keep its level and version properties. The level will be
        // different so the copied bubble(s) will be in a separate child chain from the original(s).
        // The version will probably be the same, but if it differs, we want the new one.
        // We will update this bubbleSpec with an adjusted version of the original tail and keep
        // other original properties (like backgroundColor and border style/color and order).
        const specOfCopiedElement = this.getSelectedItemBubbleSpec();
        if (!specOfCopiedElement) {
            return; // highly unlikely!
        }
        this.updateSelectedItemBubbleSpec({
            ...bubbleSpecToDuplicate,
            tails: adjustedTailSpec,
            level: specOfCopiedElement.level,
            version: specOfCopiedElement.version
        });
        const childBubbles = Comical.findRelatives(patriarchSourceBubble);
        childBubbles.forEach(childBubble => {
            const childOffsetFromPatriarch = this.getOffsetFrom(
                sourceElement,
                childBubble.content
            );
            this.duplicateOneChildBubble(
                childOffsetFromPatriarch,
                patriarchDuplicateElement,
                childBubble
            );
            // Make sure comical knows about each child as it's created, otherwise it gets the order wrong.
            Comical.convertBubbleJsonToCanvas(container as HTMLElement);
        });
    }

    private getAdjustedTailSpec(
        imageContainer: Element,
        originalTailSpecs: TailSpec[],
        sourceElement: HTMLElement,
        duplicateElement: HTMLElement
    ): TailSpec[] {
        if (originalTailSpecs.length === 0) {
            return originalTailSpecs;
        }
        const offSetFromSource = this.getOffsetFrom(
            sourceElement,
            duplicateElement
        );
        return originalTailSpecs.map(spec => {
            const tipPoint = this.adjustRelativePointToImageContainer(
                imageContainer,
                spec.tipX + offSetFromSource.getUnscaledX(),
                spec.tipY + offSetFromSource.getUnscaledY()
            );
            const midPoint = this.adjustRelativePointToImageContainer(
                imageContainer,
                spec.midpointX + offSetFromSource.getUnscaledX(),
                spec.midpointY + offSetFromSource.getUnscaledY()
            );
            return {
                ...spec,
                tipX: tipPoint.getUnscaledX(),
                tipY: tipPoint.getUnscaledY(),
                midpointX: midPoint.getUnscaledX(),
                midpointY: midPoint.getUnscaledY()
            };
        });
    }

    private matchWidthOfSource(
        sourceElement: HTMLElement,
        destElement: HTMLElement
    ) {
        destElement.style.width = sourceElement.clientWidth.toFixed(0) + "px";
    }

    private getOffsetFrom(
        sourceElement: HTMLElement,
        destElement: HTMLElement
    ): Point {
        return new Point(
            destElement.offsetLeft - sourceElement.offsetLeft,
            destElement.offsetTop - sourceElement.offsetTop,
            PointScaling.Scaled,
            "Destination scaled offset from Source"
        );
    }

    private duplicateOneChildBubble(
        offsetFromPatriarch: Point,
        parentElement: HTMLElement,
        childSourceBubble: Bubble
    ) {
        const newChildElement = this.addChildInternal(
            parentElement,
            offsetFromPatriarch.getScaledX(),
            offsetFromPatriarch.getScaledY()
        );
        if (!newChildElement) {
            return;
        }
        const sourceElement = childSourceBubble.content;
        newChildElement.innerHTML = this.safelyCloneHtmlStructure(
            sourceElement
        );
        this.matchWidthOfSource(sourceElement, newChildElement);
    }

    private safelyCloneHtmlStructure(elementToClone: HTMLElement): string {
        // eliminate .bloom-ui and ?
        const clonedElement = elementToClone.cloneNode(true) as HTMLElement;
        this.cleanClonedNode(clonedElement);
        return clonedElement.innerHTML;
    }

    private cleanClonedNode(element: Element) {
        if (this.clonedNodeNeedsDeleting(element)) {
            element.parentElement!.removeChild(element);
            return;
        }
        if (element.nodeName === "#text") {
            return;
        }

        // Cleanup this node
        this.safelyRemoveAttribute(element, "id");
        this.safelyRemoveAttribute(element, "tabindex");
        this.safelyRemoveAttribute(element, "data-duration");
        this.safelyRemoveAttribute(element, "data-audiorecordingendtimes");

        // Clean children
        const childArray = Array.from(element.childNodes);
        childArray.forEach(element => {
            this.cleanClonedNode(element as Element);
        });
    }

    private safelyRemoveAttribute(element: Element, attrName: string) {
        if (element.hasAttribute(attrName)) {
            element.attributes.removeNamedItem(attrName);
        }
    }

    private clonedNodeNeedsDeleting(element: Element): boolean {
        const htmlElement = element as HTMLElement;
        return (
            !htmlElement ||
            (htmlElement.classList &&
                htmlElement.classList.contains("bloom-ui"))
        );
    }

    private makeTOPBoxesDraggableAndClickable(
        thisTOPBoxes: JQuery,
        scale: number
    ): void {
        thisTOPBoxes.each((index, element) => {
            const thisTOPBox = $(element);
            const imageContainer = this.getImageContainer(thisTOPBox);
            const containerPos = imageContainer[0].getBoundingClientRect();
            const wrapperBoxRectangle = thisTOPBox[0].getBoundingClientRect();

            // Add the dragHandles (if needed). The visible one has a zindex below the canvas. The transparent one is above.
            // The 'mouseover' event listener below will make sure the .ui-draggable-handle class
            // on the transparent one is set to the right state depending on whether the visible handle
            // is occluded or not.
            const visibleHandles = thisTOPBox.find(".bloom-dragHandle.visible");
            if (visibleHandles.length == 0) {
                // Not added yet. Let's create it
                thisTOPBox.append(
                    "<img class='bloom-ui bloom-dragHandle visible' src='/bloom/bookEdit/img/dragHandle.svg'/>"
                );
            }

            // Save the dragHandle that's above the canvas and setup the 'mouseover' event to determine if we
            // should be able to drag with it or not.
            let transparentHandle: HTMLElement;
            const transparentHandles = thisTOPBox.find(
                ".bloom-dragHandle.transparent"
            );
            if (transparentHandles.length == 0) {
                // Not added yet. Let's create it
                thisTOPBox.append(
                    "<img class='bloom-ui bloom-dragHandle transparent' src='/bloom/bookEdit/img/dragHandle.svg'/>"
                );
                transparentHandle = thisTOPBox.find(
                    ".bloom-dragHandle.transparent"
                )[0];
            } else {
                // No need to append it again. Just use the existing one.
                transparentHandle = transparentHandles[0];
            }

            transparentHandle.addEventListener("mouseover", event => {
                this.setDraggableStateOnDragHandles(
                    imageContainer[0],
                    transparentHandle
                );
            });

            // Containment, drag and stop work when scaled (zoomed) as long as the page has been saved since the zoom
            // factor was last changed. Therefore we force reconstructing the page
            // in the EditingView.Zoom setter (in C#).
            thisTOPBox.draggable({
                // Adjust containment by scaling
                containment: [
                    containerPos.left,
                    containerPos.top,
                    containerPos.left +
                        containerPos.width -
                        wrapperBoxRectangle.width,
                    containerPos.top +
                        containerPos.height -
                        wrapperBoxRectangle.height
                ],
                // Don't allow dragging with occluded dragHandle
                cancel:
                    ".ui-draggable .bloom-dragHandle:not(.ui-draggable-handle)",
                revertDuration: 0,
                handle: ".bloom-dragHandle.transparent",
                drag: (event, ui) => {
                    ui.helper.children(".bloom-editable").blur();
                    const position = new Point(
                        ui.position.left,
                        ui.position.top,
                        PointScaling.Scaled,
                        "ui.position"
                    );

                    // Try to unscroll earlier. Dealing with scroll here is tough.
                    // Scrolled draggables give JQuery draggables a hard time.
                    // I'm having too hard of a time figuring out how to adjust it properly across all zoom factors.
                    // Neither unadjusted, container.scrollTop, nor delta between container and canvas are right.
                    const scroll = BubbleManager.getScrollAmount(
                        imageContainer[0]
                    );
                    console.assert(
                        scroll.length() <= 0.0001,
                        `Scroll expected to be [0, 0] but was [${scroll.getScaledX()}, ${scroll.getScaledY()}].`
                    );

                    // Adjusts the positioning for scale.
                    // (Doesn't accurately adjust for the amount of scroll)
                    ui.position.left = position.getUnscaledX();
                    ui.position.top = position.getUnscaledY();

                    thisTOPBox.find(".bloom-dragHandle").addClass("grabbing");
                },
                stop: event => {
                    const target = event.target as Element;
                    if (target) {
                        BubbleManager.convertTextboxPositionToPercentage(
                            target
                        );
                    }

                    thisTOPBox
                        .find(".bloom-dragHandle")
                        .removeClass("grabbing");
                    // We may have changed which handles are occluded; reset state on the current TOP box handles.
                    // Other handles will be reset whenever we mouseover them.
                    this.setDraggableStateOnDragHandles(
                        imageContainer[0],
                        transparentHandle
                    );
                }
            });

            thisTOPBox.find(".bloom-editable").click(function(e) {
                this.focus();
            });
        });
    }

    private setDraggableStateOnDragHandles(
        imageContainer: HTMLElement,
        dragHandle: HTMLElement
    ) {
        if (!imageContainer || !dragHandle) {
            return; // paranoia
        }
        // The 'dragHandle' here is actually the transparent one that sits above the canvas
        // (the one that we interact with). We test to see if the visible handle is occluded by
        // checking if the transparent one overlaps completely with any comical stuff. We need to use
        // the transparent one because that's the one that needs the propagation handlers switched and
        // the 'ui-draggable-handle' class removed/added.
        if (this.isVisibleHandleOccluded(imageContainer, dragHandle)) {
            dragHandle.classList.remove("ui-draggable-handle");
            // We really don't want this dragHandle to function, so we attach some stopPropagation
            // handlers to its events.
            this.setStopPropagationHandlers(dragHandle);
        } else {
            dragHandle.classList.add("ui-draggable-handle");
            this.unsetStopPropagationHandlers(dragHandle);
        }
    }

    private setStopPropagationHandlers(transparentHandle: HTMLElement) {
        transparentHandle.addEventListener(
            "click",
            this.removeableStopPropagationHandler
        );
        transparentHandle.addEventListener(
            "mousedown",
            this.removeableStopPropagationHandler
        );
        transparentHandle.addEventListener(
            "mouseup",
            this.removeableStopPropagationHandler
        );
        transparentHandle.addEventListener(
            "mousemove",
            this.removeableStopPropagationHandler
        );
    }

    private unsetStopPropagationHandlers(transparentHandle: HTMLElement) {
        transparentHandle.removeEventListener(
            "click",
            this.removeableStopPropagationHandler
        );
        transparentHandle.removeEventListener(
            "mousedown",
            this.removeableStopPropagationHandler
        );
        transparentHandle.removeEventListener(
            "mouseup",
            this.removeableStopPropagationHandler
        );
        transparentHandle.removeEventListener(
            "mousemove",
            this.removeableStopPropagationHandler
        );
    }

    private removeableStopPropagationHandler(e: Event) {
        e.stopPropagation();
    }

    private isVisibleHandleOccluded(
        imgContainerElement: HTMLElement,
        handle: HTMLElement
    ): boolean {
        if (!handle || !imgContainerElement) {
            return true; // paranoia
        }
        const divTOPElement = handle.parentElement;

        const left = divTOPElement!.offsetLeft + handle.offsetLeft;
        const right = left + handle.offsetWidth;
        const top = divTOPElement!.offsetTop + handle.offsetTop;
        const bottom = top + handle.offsetHeight;
        return Comical.isAreaCompletelyIntersected(
            imgContainerElement,
            left,
            right,
            top,
            bottom
        );
    }

    public initializeTextOverPictureEditing(): void {
        // Cleanup old .bloom-ui elements and old drag handles etc.
        // We want to clean these up sooner rather than later so that there's less chance of accidentally blowing away
        // a UI element that we'll actually need now
        // (e.g. the ui-resizable-handles or the format gear, which both have .bloom-ui applied to them)
        this.cleanupTOPBoxes();

        this.makeTOPBoxesDraggableClickableAndResizable();
        this.turnOnBubbleEditing();
    }

    public cleanupTOPBoxes() {
        const allTOPBoxes = $("body").find(kTextOverPictureSelector);
        allTOPBoxes.each((index, element) => {
            const thisTOPBox = $(element);

            thisTOPBox.find(".bloom-ui").remove(); // Just in case somehow one is stuck in there
            thisTOPBox.find(".bloom-dragHandleTOP").remove(); // BL-7903 remove any left over drag handles (this was the class used in 4.7 alpha)
        });
    }

    // Make any added BubbleManager textboxes draggable, clickable, and resizable.
    // Called by bloomEditing.ts.
    public makeTOPBoxesDraggableClickableAndResizable() {
        // get all textOverPicture elements
        const textOverPictureElems = $("body").find(kTextOverPictureSelector);
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
                    // Resizing also changes size and position to pixels. Change it back to percentage.

                    BubbleManager.convertTextboxPositionToPercentage(target);

                    // There was a problem where resizing a box messed up its draggable containment,
                    // so now after we resize we go back through making it draggable and clickable again.
                    this.makeTOPBoxesDraggableAndClickable($(target), scale);

                    // Clear the custom class used to indicate that a resize action may have been started
                    BubbleManager.clearResizingClass(target);
                }
            },
            resize: (event, ui) => {
                const target = event.target as Element;
                if (target) {
                    // If the user changed the height, prevent automatic shrinking.
                    // If only the width changed, this is the case where we want it.
                    // This needs to happen during the drag so that the right automatic
                    // behavior happens during it.
                    if (ui.originalSize.height !== ui.size.height) {
                        target.classList.remove("bloom-allowAutoShrink");
                    } else {
                        target.classList.add("bloom-allowAutoShrink");
                    }
                    this.adjustResizingForScale(ui, scale);
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

                // Add a class that indicates these elements are only needed for the UI and aren't needed to be saved in the book's HTML
                handle.classList.add("bloom-ui");

                handle.addEventListener(
                    "mousedown",
                    BubbleManager.addResizingClassHandler
                );

                // Even though we clear it in the JQuery Resize Stop handler, we also need one here
                // because if the mouse is depressed and then released (without moving), we do want this class applied temporarily
                // but we also need to make sure it gets cleaned up, even though no formal Resize Start/Stop events occurred.
                handle.addEventListener(
                    "mouseup",
                    BubbleManager.clearResizingClassHandler
                );
            }
        }

        this.makeTOPBoxesDraggableAndClickable(textOverPictureElems, scale);
    }

    // BL-8134: Keeps mouse movement in sync with bubble resizing when scale is not 100%.
    private adjustResizingForScale(
        ui: JQueryUI.ResizableUIParams,
        scale: number
    ) {
        const deltaWidth = ui.size.width - ui.originalSize.width;
        const deltaHeight = ui.size.height - ui.originalSize.height;
        if (deltaWidth != 0 || deltaHeight != 0) {
            const newWidth: number = ui.originalSize.width + deltaWidth / scale;
            const newHeight: number =
                ui.originalSize.height + deltaHeight / scale;
            ui.element.width(newWidth);
            ui.element.height(newHeight);
        }
        const deltaX = ui.position.left - ui.originalPosition.left;
        const deltaY = ui.position.top - ui.originalPosition.top;
        if (deltaX != 0 || deltaY != 0) {
            const newX: number = ui.originalPosition.left + deltaX / scale;
            const newY: number = ui.originalPosition.top + deltaY / scale;
            ui.element.css("left", newX); // when passing a number as the new value; JQuery assumes "px"
            ui.element.css("top", newY);
        }
    }

    // An event handler that adds the "bloom-resizing" class to the image container.
    private static addResizingClassHandler(event: MouseEvent) {
        const handle = event.currentTarget as Element;

        const container = handle.closest(".bloom-imageContainer");
        if (container) {
            container.classList.add("bloom-resizing");
        }
    }

    // An event handler that removes the "bloom-resizing" class from the image container.
    private static clearResizingClassHandler(event: MouseEvent) {
        BubbleManager.clearResizingClass(event.currentTarget as Element);
    }

    private static clearResizingClass(element: Element) {
        const container = element.closest(".bloom-imageContainer");
        if (container) {
            container.classList.remove("bloom-resizing");
        }
    }

    // Converts a text box's position into percentages (using CSS styling)
    // wrapperBoxElement: The specified text box
    // container: Optional. The image container the text box is in. If this parameter is not defined, the function will automatically determine it.
    private static convertTextboxPositionToPercentage(
        wrapperBoxElement: Element,
        container?: Element | null | undefined
    ): void {
        let unscaledRelativeLeft: number;
        let unscaledRelativeTop: number;

        if (!container) {
            container = wrapperBoxElement.closest(".bloom-imageContainer");
        }

        if (container) {
            const positionInfo = wrapperBoxElement.getBoundingClientRect();
            const wrapperBoxPos = new Point(
                positionInfo.left,
                positionInfo.top,
                PointScaling.Scaled,
                "convertTextboxPositionToPercentage()"
            );
            const reframedPoint = this.convertPointFromViewportToElementFrame(
                wrapperBoxPos,
                container
            );
            unscaledRelativeLeft = reframedPoint.getUnscaledX();
            unscaledRelativeTop = reframedPoint.getUnscaledY();
        } else {
            console.assert(
                false,
                "convertTextboxPositionToPercentage(): container was null or undefined."
            );

            // If can't find the container for some reason, fallback to the old, deprecated calculation.
            // (This algorithm does not properly account for the border of the imageContainer when zoomed,
            //  so the results may be slightly off by perhaps up to 2 pixels)
            const scale = EditableDivUtils.getPageScale();
            const pos = $(wrapperBoxElement).position();
            unscaledRelativeLeft = pos.left / scale;
            unscaledRelativeTop = pos.top / scale;
        }

        this.setTextboxPositionAsPercentage(
            $(wrapperBoxElement),
            unscaledRelativeLeft,
            unscaledRelativeTop
        );
    }

    // Sets a text box's position in percentages (using CSS styling)
    // wrapperBox: The text box in question
    // unscaledRelativeLeft/unscaledRelativeTop: The position to set the top-left corner/at. It should be in unscaled pixels, relative to the parent.
    private static setTextboxPositionAsPercentage(
        wrapperBox: JQuery,
        unscaledRelativeLeft: number,
        unscaledRelativeTop: number
    ) {
        const container = wrapperBox.closest(".bloom-imageContainer");
        const containerSize = this.getInteriorWidthHeight(container[0]);
        const width = containerSize.getUnscaledX();
        const height = containerSize.getUnscaledY();

        // the textbox is contained by the image, and it's actual positioning is now based on the imageContainer too.
        // so we will position by percentage of container size.
        wrapperBox
            .css("left", (unscaledRelativeLeft / width) * 100 + "%")
            .css("top", (unscaledRelativeTop / height) * 100 + "%")
            // FYI: The wrapperBox width/height is rounded to the nearest whole pixel. Ideally we might like its more precise value...
            // But it's a huge performance hit to get its getBoundingClientRect()
            // It seems that getBoundingClientRect() may be internally cached under the hood,
            // since getting the bounding rect of the image container once per mousemove event or even 100x per mousemove event caused no ill effect,
            // but getting this one is quite taxing on the CPU
            .css("width", (wrapperBox.width() / width) * 100 + "%")
            .css("height", (wrapperBox.height() / height) * 100 + "%");
    }

    // Determines the unrounded width/height of the content of an element (i.e, excluding its margin, border, padding)
    //
    // This differs from JQuery width/height because those functions give you values rounded to the nearest pixel.
    // This differs from getBoundingClientRect().width because that function includes the border and padding of the element in the width.
    // This function returns the interior content's width/height (unrounded), without any margin, border, or padding
    private static getInteriorWidthHeight(element: HTMLElement): Point {
        const boundingRect = element.getBoundingClientRect();

        const exterior = new Point(
            boundingRect.width,
            boundingRect.height,
            PointScaling.Scaled,
            "getBoundingClientRect() result (Relative to viewport)"
        );

        // Exterior gives the location of the outside edge of the border. But we want values relative to the inside edge of the padding.
        // So we need to subtract out the border and padding, once for each side of the box
        const borderAndPadding = this.getCombinedBordersAndPaddings(element);
        const interior = exterior.subtract(borderAndPadding);
        return interior;
    }

    // Gets the bloom-imageContainer that hosts this BubbleManager textbox.
    // The imageContainer will define the dragging boundaries for the textbox.
    private getImageContainer(wrapperBox: JQuery): JQuery {
        return wrapperBox.parent(".bloom-imageContainer").first();
    }

    // When showing a tail for a bubble style that doesn't have one by default, we get one here.
    public getDefaultTailSpec(): TailSpec | undefined {
        const activeElement = this.getActiveElement();
        if (activeElement) {
            return Bubble.makeDefaultTail(activeElement);
        }
        return undefined;
    }
}

export let theOneBubbleManager: BubbleManager;

export function initializeBubbleManager() {
    if (theOneBubbleManager) return;
    theOneBubbleManager = new BubbleManager();
    theOneBubbleManager.initializeBubbleManager();
}
