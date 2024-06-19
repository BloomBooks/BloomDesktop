// This class makes it possible to add and delete elements that float over images. These floating
// elements were originally intended for use in making comic books, but could also be useful for many
// other cases of where there is space for text or another image or a video within the bounds of
// the picture.
///<reference path="../../typings/jquery/jquery.d.ts"/>
// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="./collectionSettings.d.ts"/>

import { EditableDivUtils } from "./editableDivUtils";
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
import { getRgbaColorStringFromColorAndOpacity } from "../../utils/colorUtils";
import { SetupElements, attachToCkEditor } from "./bloomEditing";
import {
    addImageEditingButtons,
    DisableImageEditing,
    EnableImageEditing,
    EnableAllImageEditing,
    tryRemoveImageEditingButtons
} from "./bloomImages";

export interface ITextColorInfo {
    color: string;
    isDefault: boolean;
}

const kComicalGeneratedClass: string = "comical-generated";
// We could rename this class to "bloom-overPictureElement", but that would involve a migration.
// For now we're keeping this name for backwards-compatibility, even though now the element could be
// a video or even another picture.
const kTextOverPictureClass = "bloom-textOverPicture";
const kTextOverPictureSelector = `.${kTextOverPictureClass}`;
const kImageContainerClass = "bloom-imageContainer";
const kImageContainerSelector = `.${kImageContainerClass}`;
const kVideoContainerClass = "bloom-videoContainer";

const kOverlayClass = "hasOverlay";

type ResizeDirection = "ne" | "nw" | "sw" | "se";

// References to "TOP" in the code refer to the actual TextOverPicture box (what "Bubble"s were
// originally called) installed in the Bloom page. We are gradually removing these, since now there
// are multiple types of elements that can be placed over pictures, not just Text.
// "Bubble" now becomes a generic name for any element placed over a picture that communicates with
// comicaljs.
export class BubbleManager {
    // The min width/height needs to be kept in sync with the corresponding values in bubble.less
    public minTextBoxWidthPx = 30;
    public minTextBoxHeightPx = 30;

    private activeElement: HTMLElement | undefined;
    public isComicEditingOn: boolean = false;
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
        // Currently nothing to do; used to set up web socket listener
        // for right-click messages to add and delete OverPicture elements.
        // Keeping hook in case we want it one day...
    }

    public getIsComicEditingOn(): boolean {
        return this.isComicEditingOn;
    }

    // Given the box has been determined to be overflowing vertically by
    // 'overflowY' pixels, if it's inside an OverPicture element, enlarge the OverPicture element
    // by that much so it won't overflow.
    // (Caller may wish to do box.scrollTop = 0 to make sure the whole content shows now there
    // is room for it all.)
    // Returns true if successful; it will currently fail if box is not
    // inside a valid OverPicture element or if the OverPicture element can't grow this much while
    // remaining inside the image container. If it returns false, it makes no changes at all.
    public static growOverflowingBox(
        box: HTMLElement,
        overflowY: number
    ): boolean {
        const wrapperBox = box.closest(kTextOverPictureSelector) as HTMLElement;
        if (!wrapperBox) {
            return false; // we can't fix it
        }

        const container = BubbleManager.getTopLevelImageContainerElement(
            wrapperBox
        );
        if (!container) {
            return false; // paranoia; OverPicture element should always be in image container
        }

        if (overflowY > -6 && overflowY < -2) {
            return false; // near enough, avoid jitter; -4 would be no change, see below.
        }
        // The +4 is based on experiment. It may relate to a couple of 'fudge factors'
        // in OverflowChecker.getSelfOverflowAmounts, which I don't want to mess with
        // as a lot of work went into getting overflow reporting right. We seem to
        // need a bit of extra space to make sure the last line of text fits.
        // The 27 is the minimumSize that CSS imposes on OverPicture elements; it may cause
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
        BubbleManager.convertTextboxPositionToAbsolute(wrapperBox, container);
        return true;
    }

    // When the format dialog changes the amount of padding for overlays, adjust their sizes
    // and positions (keeping the text in the same place).
    // This function assumes that the postion and size of overlays are determined by the
    // top, left, width, and height properties of the .bloom-textOverPicture elements,
    // and that they are measured in pixels.
    public static adjustOverlaysForPaddingChange(
        container: HTMLElement,
        style: string,
        oldPaddingStr: string, // number+px
        newPaddingStr: string // number+px
    ) {
        const wrapperBoxes = Array.from(
            container.getElementsByClassName(kTextOverPictureClass)
        ) as HTMLElement[];
        const oldPadding = BubbleManager.pxToNumber(oldPaddingStr);
        const newPadding = BubbleManager.pxToNumber(newPaddingStr);
        const delta = newPadding - oldPadding;
        const overlayLang = GetSettings().languageForNewTextBoxes;
        wrapperBoxes.forEach(wrapperBox => {
            // The language check is a belt-and-braces thing. At the time I did this PR, we had a bug where
            // the bloom-editables in a TG did not necessarily all have the same style.
            // We could possibly enconuter books where this is still true.
            if (
                Array.from(wrapperBox.getElementsByClassName(style)).filter(
                    x => x.getAttribute("lang") === overlayLang
                ).length > 0
            ) {
                if (!wrapperBox.style.height.endsWith("px")) {
                    // Some sort of legacy situation; for a while we had all the placements as percentages.
                    // This will typically not move it, but will force it to the new system of placement
                    // by pixel. Don't want to do this if we don't have to, because there could be rounding
                    // errors that would move it very slightly.
                    this.setTextboxPosition(
                        $(wrapperBox as HTMLElement),
                        wrapperBox.offsetLeft - container.offsetLeft,
                        wrapperBox.offsetTop - container.offsetTop
                    );
                }
                const oldHeight = this.pxToNumber(wrapperBox.style.height);
                wrapperBox.style.height = oldHeight + 2 * delta + "px";
                const oldWidth = this.pxToNumber(wrapperBox.style.width);
                wrapperBox.style.width = oldWidth + 2 * delta + "px";
                const oldTop = this.pxToNumber(wrapperBox.style.top);
                wrapperBox.style.top = oldTop - delta + "px";
                const oldLeft = this.pxToNumber(wrapperBox.style.left);
                wrapperBox.style.left = oldLeft - delta + "px";

                BubbleManager.convertTextboxPositionToAbsolute(
                    wrapperBox,
                    this.getTopLevelImageContainerElement(wrapperBox)!
                );
            }
        });
    }

    // Convert string ending in pixels to a number
    private static pxToNumber(px: string): number {
        return parseInt(px.replace("px", ""));
    }

    // Now that we have the possibility of "nested" imageContainer elements,
    // we need to limit the img tags we look at to those that are immediate children.
    public static hideImageButtonsIfNotPlaceHolderOrHasOverlays(
        container: HTMLElement
    ) {
        if (
            document.getElementsByClassName(kTextOverPictureClass).length === 0
        ) {
            // If the page has no overlays at all, we really don't want to do this.
            // Even though the comical toolbox is open, comic editing doesn't get properly
            // initialized until we add at least one overlay, and without that init,
            // clicking on the picture doesn't force the controls to show; it can be
            // really confusing if the tool was left open from another page but isn't
            // relevant to this one.
            return;
        }
        const placeHolderImages = Array.from(container.childNodes).filter(
            child => {
                return (
                    child.nodeName === "IMG" &&
                    (child as HTMLElement).getAttribute("src") ===
                        "placeHolder.png"
                );
            }
        );
        const hasOverlay = container.classList.contains("hasOverlay");
        // Would this be more reliable?
        // container.getElementsByClassName("bloom-textOverPicture").length > 0;
        if (placeHolderImages.length === 0 || hasOverlay) {
            DisableImageEditing(container);
        }
    }

    public turnOnHidingImageButtons() {
        const imageContainers: HTMLElement[] = Array.from(
            this.getAllPrimaryImageContainersOnPage() as any
        );
        imageContainers.forEach(container => {
            BubbleManager.hideImageButtonsIfNotPlaceHolderOrHasOverlays(
                container
            );
        });
    }

    // When switching to the comicTool from elsewhere (notably the sign language tool), we remove
    // the 'bloom-selected' class, so the container doesn't have a yellow border like it does in the
    // sign language tool.
    public deselectVideoContainers() {
        const videoContainers: HTMLElement[] = Array.from(
            document.getElementsByClassName(kVideoContainerClass) as any
        );
        videoContainers.forEach(container => {
            container.classList.remove("bloom-selected");
        });
    }

    private getAllVisibleFocusableDivs(
        overPictureContainerElement: HTMLElement
    ): Element[] {
        // If the Over Picture element has visible bloom-editables, we want them.
        // Otherwise, look for video and image elements. At this point, an over picture element
        // can only have one of three types of content and each are mutually exclusive.
        // bloom-editable or bloom-videoContainer or bloom-imageContainer. It doesn't even really
        // matter which order we look for them.
        let focusableDivs = Array.from(
            overPictureContainerElement.getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )
        ).filter(
            el =>
                !(
                    el.parentElement!.classList.contains("box-header-off") ||
                    el.parentElement!.classList.contains(
                        "bloom-imageDescription"
                    )
                )
        );
        if (focusableDivs.length === 0) {
            focusableDivs = Array.from(
                overPictureContainerElement.getElementsByClassName(
                    kVideoContainerClass
                )
            );
        }
        if (focusableDivs.length === 0) {
            // This could be a bit tricky, since the whole canvas is in a 'bloom-imageContainer'.
            // But 'overPictureContainerElement' here is a div.bloom-textOverPicture element,
            // so if we find any imageContainers inside of that, they are picture over picture elements.
            focusableDivs = Array.from(
                overPictureContainerElement.getElementsByClassName(
                    kImageContainerClass
                )
            );
        }
        return focusableDivs;
    }

    /**
     * Attempts to finds the first visible div which can be focused. If so, focuses it.
     *
     * @returns True if an element was focused. False otherwise.
     */
    private focusFirstVisibleFocusable(activeElement: HTMLElement): boolean {
        const focusElements = this.getAllVisibleFocusableDivs(activeElement);
        if (focusElements.length > 0) {
            (focusElements[0] as HTMLElement).focus();
            return true;
        }
        return false;
    }

    private focusLastVisibleFocusable(activeElement: HTMLElement) {
        const focusElements = this.getAllVisibleFocusableDivs(activeElement);
        const numberOfElements = focusElements.length;
        if (numberOfElements > 0) {
            (focusElements[numberOfElements - 1] as HTMLElement).focus();
        }
    }

    public turnOnBubbleEditing(): void {
        if (this.isComicEditingOn === true) {
            return; // Already on. No work needs to be done
        }
        this.isComicEditingOn = true;

        Comical.setActiveBubbleListener(activeElement => {
            if (activeElement) {
                this.focusFirstVisibleFocusable(activeElement);
            }
        });

        const imageContainers: HTMLElement[] = this.getAllPrimaryImageContainersOnPage();

        imageContainers.forEach(container => {
            this.adjustOverlaysForCurrentLanguage(container);
            this.ensureBubblesIntersectParent(container);
            // image containers are already set by CSS to overflow:hidden, so they
            // SHOULD never scroll. But there's also a rule that when something is
            // focused, it has to be scrolled to. If we set focus to a bubble that's
            // sufficiently (almost entirely?) off-screen, the browser decides that
            // it MUST scroll to show it. For a reason I haven't determined, the
            // element it picks to scroll seems to be the image container. This puts
            // the display in a confusing state where the text that should be hidden
            // is visible, though the canvas has moved over and most of the bubble
            // is still hidden (BL-11646).
            // Another solution would be to find the code that is focusing the
            // bubble after page load, and give it the option {preventScroll: true}.
            // But (a) this is not supported in Gecko (added in FF68), and (b) you
            // can get a similar bad effect by moving the cursor through text that
            // is supposed to be hidden. This drastic approach prevents both.
            // We're basically saying, if this element scrolls its content for
            // any reason, undo it.
            container.addEventListener("scroll", () => {
                container.scrollLeft = 0;
                container.scrollTop = 0;
            });
        });

        // todo: select the right one...in particular, currently we just select the last one.
        // This is reasonable when just coming to the page, and when we add a new OverPicture element,
        // we make the new one the last in its parent, so with only one image container
        // the new one gets selected after we refresh. However, once we have more than one
        // image container, I don't think the new OverPicture element will get selected if it's not on
        // the first image.
        // todo: make sure comical is turned on for the right parent, in case there's more than one
        // image on the page?
        const overPictureElements: HTMLElement[] = Array.from(
            document.getElementsByClassName(kTextOverPictureClass) as any
        );
        if (overPictureElements.length > 0) {
            this.activeElement = overPictureElements[
                overPictureElements.length - 1
            ] as HTMLElement;
            // This focus call doesn't seem to work, at least in a lasting fashion.
            // See the code in bloomEditing.ts/SetupElements() that sets focus after
            // calling BloomSourceBubbles.MakeSourceBubblesIntoQtips() in a delayed loop.
            // That code usually finds that nothing is focused.
            // (gjm: I reworked the code that finds a visible element a bit,
            // it's possible the above comment is no longer accurate)
            this.focusFirstVisibleFocusable(this.activeElement);
            Comical.setUserInterfaceProperties({ tailHandleColor: "#96668F" }); // light bloom purple
            Comical.startEditing(imageContainers);
            this.migrateOldTextOverPictureElements(overPictureElements);
            Comical.activateElement(this.activeElement);
            overPictureElements.forEach(container => {
                this.addEventsToFocusableElements(container, false);
            });
            document.addEventListener(
                "click",
                BubbleManager.onDocClickClearActiveElement
            );
            // If we have sign language video over picture elements that are so far only placeholders,
            // they are not focusable by default and so won't get the blue border that elements
            // are supposed to have when selected. So we add tabindex="0" so they become focusable.
            overPictureElements.forEach(element => {
                const videoContainers = Array.from(
                    element.getElementsByClassName(kVideoContainerClass)
                );
                if (videoContainers.length === 1) {
                    const container = videoContainers[0] as HTMLElement;
                    // If there is a video childnode, it is already focusable.
                    if (container.childElementCount === 0) {
                        container.setAttribute("tabindex", "0");
                    }
                }
            });
        } else {
            // Focus something!
            // BL-8073: if Comic Tool is open, this 'turnOnBubbleEditing()' method will get run.
            // If this particular page has no comic bubbles, we can actually arrive here with the 'body'
            // as the document's activeElement. So we focus the first visible focusable element
            // we come to.
            const marginBox = document.getElementsByClassName("marginBox");
            if (marginBox.length > 0) {
                this.focusFirstVisibleFocusable(marginBox[0] as HTMLElement);
            }
        }

        // turn on various behaviors for each image
        Array.from(this.getAllPrimaryImageContainersOnPage()).forEach(
            (container: HTMLElement) => {
                container.addEventListener("click", event => {
                    // The goal here is that if the user clicks outside any comical bubble,
                    // we want none of the comical bubbles selected, so that
                    // (after moving the mouse away to get rid of hover effects)
                    // the user can see exactly what the final comic will look like.
                    // This is a difficult and horrible kludge.
                    // First problem is that this click handler is fired for a click
                    // ANYWHERE in the image...none of the bubble- or OverPicture element- related
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
                            // Usually we hide the image editing controls in overlay mode so
                            // they don't get in the way of manipulating the bubbles, but a click
                            // on the underlying image is understood to mean the user wants to work
                            // on it, so allow them to be seen again.
                            // (Note: we're not making it focused in the same way an image overlay could be)
                            EnableImageEditing(container);
                            // So far so good. We have now determined that we want to remove
                            // focus from anything in this image.
                            // (Enhance: should we check that something within this image
                            // is currently focused, so clicking on a picture won't
                            // arbitrarily move the focus if it's not in this image?)
                            // Leaving nothing at all selected is something of a last resort,
                            // so we first look for something we can focus that is outside the
                            // image.
                            let somethingElseToFocus = Array.from(
                                document.getElementsByClassName(
                                    "bloom-editable"
                                )
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
                                somethingElseToFocus.setAttribute(
                                    "tabindex",
                                    "0"
                                );
                                somethingElseToFocus.style.display = "block"; // defeat rules making it display:none and hence not focusable

                                // However, we don't actually want to see it; these rules
                                // (somewhat redundantly) make it have no size and be positioned
                                // off-screen.
                                somethingElseToFocus.style.width = "0";
                                somethingElseToFocus.style.height = "0";
                                somethingElseToFocus.style.overflow = "hidden";
                                somethingElseToFocus.style.position =
                                    "absolute";
                                somethingElseToFocus.style.left = "-1000px";
                            }
                            // In case we've already set this listener, remove it before setting it again.
                            somethingElseToFocus.removeEventListener(
                                "focusin",
                                BubbleManager.onFocusSetActiveElement
                            );
                            // And we want the usual behavior when it gets focus!
                            somethingElseToFocus.addEventListener(
                                "focusin",
                                BubbleManager.onFocusSetActiveElement
                            );
                            somethingElseToFocus.focus();
                        }
                    }
                });

                this.setDragAndDropHandlers(container);
                this.setMouseDragHandlers(container);
            }
        );

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

    adjustOverlaysForCurrentLanguage(container: HTMLElement) {
        const overlayLang = GetSettings().languageForNewTextBoxes;
        Array.from(
            container.getElementsByClassName("bloom-textOverPicture")
        ).forEach(top => {
            const editable = Array.from(
                top.getElementsByClassName("bloom-editable")
            ).find(e => e.getAttribute("lang") === overlayLang);
            if (editable) {
                const alternatesString = editable.getAttribute(
                    "data-bubble-alternate"
                );
                if (alternatesString) {
                    const alternate = JSON.parse(
                        alternatesString.replace(/`/g, '"')
                    ) as IAlternate;
                    top.setAttribute("style", alternate.style);
                    const bubbleData = top.getAttribute("data-bubble");
                    if (bubbleData) {
                        const bubbleDataObj = JSON.parse(
                            bubbleData.replace(/`/g, '"')
                        );
                        bubbleDataObj.tails = alternate.tails;
                        const newBubbleData = JSON.stringify(
                            bubbleDataObj
                        ).replace(/"/g, "`");
                        top.setAttribute("data-bubble", newBubbleData);
                    }
                }
            }

            // If we don't find a matching bloom-editable, or there is no alternate attribute
            // there, that's fine; just let the current state of the bubble serve as a
            // default for the new language.
        });
        // If we have an existing alternate SVG for this language, remove it.
        // (It will effectively be replaced by the new active comical-generated svg
        // made when we save the page.)
        const altSvg = Array.from(
            container.getElementsByClassName("comical-alternate")
        ).find(svg => svg.getAttribute("data-lang") === overlayLang);
        if (altSvg) {
            container.removeChild(altSvg);
        }

        const currentSvg = container.getElementsByClassName(
            "comical-generated"
        )[0];
        if (currentSvg) {
            const currentSvgLang = currentSvg.getAttribute("data-lang");
            if (currentSvgLang && currentSvgLang !== overlayLang) {
                // it was generated for some other language. Save it for possible use with
                // that language in Bloom Player.
                // We need to remove this class so Comical won't delete it.
                currentSvg.classList.remove("comical-generated");
                // and add this one to help bloom-player (and the code above) find it
                currentSvg.classList.add("comical-alternate");
                // Make sure nothing sees it unless it gets reactivated by bloom-player.
                // We do this instead of having a CSS rule to hide comical-alternate so
                // alternates will be hidden even in a book being shown by an old version
                // of bloom-player.
                (currentSvg as HTMLElement).style.display = "none";
            }
        }
    }

    // Save the current state of things so that we can later position everything
    // correctly for this language, even if in the meantime we change bubble
    // positions for other languages.
    saveCurrentOverlayStateAsCurrentLangAlternate(container: HTMLElement) {
        const overlayLang = GetSettings().languageForNewTextBoxes;
        Array.from(
            container.getElementsByClassName("bloom-textOverPicture")
        ).forEach(top => {
            const editable = Array.from(
                top.getElementsByClassName("bloom-editable")
            ).find(e => e.getAttribute("lang") === overlayLang);
            if (editable) {
                const bubbleData = top.getAttribute("data-bubble") ?? "";
                const bubbleDataObj = JSON.parse(bubbleData.replace(/`/g, '"'));
                const alternate = {
                    lang: overlayLang,
                    style: top.getAttribute("style") ?? "",
                    tails: bubbleDataObj.tails as object[]
                };
                editable.setAttribute(
                    "data-bubble-alternate",
                    JSON.stringify(alternate).replace(/"/g, "`")
                );
            }
        });
        // Record that the current comical-generated SVG is for this language.
        const currentSvg = container.getElementsByClassName(
            "comical-generated"
        )[0];
        currentSvg?.setAttribute("data-lang", overlayLang);
    }

    // "container" refers to a .bloom-textOverPicture div, which holds one (and only one) of the
    // 3 main types of "bubble": text, video or image.
    // This method will attach the focusin event to each of these.
    private addEventsToFocusableElements(
        container: HTMLElement,
        includeCkEditor: boolean
    ) {
        const focusables = this.getAllVisibleFocusableDivs(container);
        focusables.forEach(element => {
            // Don't use an arrow function as an event handler here.
            //These can never be identified as duplicate event listeners, so we'll end up with tons
            // of duplicates.
            element.addEventListener("focusin", ev => {
                // Restore hiding these when we focus a bubble, so they don't get in the way of working on
                // that bubble.
                this.turnOnHidingImageButtons();
                BubbleManager.onFocusSetActiveElement(ev);
            });
            if (
                includeCkEditor &&
                element.classList.contains("bloom-editable")
            ) {
                attachToCkEditor(element);
            }
        });
    }

    // This should not return any .bloom-imageContainers that have imageContainer ancestors.
    private getAllPrimaryImageContainersOnPage() {
        const unfilteredContainers = document.getElementsByClassName(
            kImageContainerClass
        );
        return Array.from(unfilteredContainers).filter(
            (el: Element) =>
                el.parentElement!.closest(kImageContainerSelector) === null
        ) as HTMLElement[];
    }

    // Use this one when adding/duplicating a bubble to avoid re-navigating the page.
    // If we are passing "undefined" as the bubble, it's because we just deleted a bubble
    // and we want Bloom to determine what to select next (it might not be a bubble at all).
    public refreshBubbleEditing(
        imageContainer: HTMLElement,
        bubble: Bubble | undefined,
        attachEventsToEditables: boolean
    ): void {
        Comical.startEditing([imageContainer]);
        if (bubble) {
            const newTextOverPictureElement = bubble.content;
            Comical.activateBubble(bubble);
            this.updateComicalForSelectedElement(newTextOverPictureElement);
            SetupElements(imageContainer);

            // Since we may have just added an element, check if the container has at least one
            // overlay element and add the 'hasOverlay' class.
            updateOverlayClass(imageContainer);

            // SetupElements (above) will do most of what we need, but when it gets to
            // 'turnOnBubbleEditing()', it's already on, so the method will get skipped.
            // The only piece left from that method that still needs doing is to set the
            // 'focusin' eventlistener.
            // And then the only thing left from a full refresh that needs to happen here is
            // to attach the new bloom-editable to ckEditor.
            // If attachEventsToEditables is false, then this is a child or duplicate bubble that
            // was already sent through here once. We don't need to add more 'focusin' listeners and
            // re-attach to the StyleEditor again.
            if (attachEventsToEditables) {
                this.addEventsToFocusableElements(
                    newTextOverPictureElement,
                    attachEventsToEditables
                );
            }
        } else {
            let focusableContainer: HTMLElement = imageContainer;
            if (
                this.getAllVisibleFocusableDivs(focusableContainer).length === 0
            ) {
                focusableContainer = document.getElementsByClassName(
                    "marginBox"
                )[0] as HTMLElement;
            }
            // We just deleted a bubble, so focus the most recently added bubble in the same
            // imageContainer.
            this.focusLastVisibleFocusable(focusableContainer);
            // When the last visible editable gets focus, onFocusSetActiveElement()
            // will call setActiveElement() to update the toolbox UI.

            // Also, since we just deleted an element, check if the original container no longer
            // has any overlay elements and remove the 'hasOverlay' class.
            updateOverlayClass(imageContainer);
        }
    }

    private migrateOldTextOverPictureElements(
        textOverPictureElems: HTMLElement[]
    ): void {
        textOverPictureElems.forEach(top => {
            if (!top.getAttribute("data-bubble")) {
                const bubbleSpec = Bubble.getDefaultBubbleSpec(top, "none");
                new Bubble(top).setBubbleSpec(bubbleSpec);
                // it would be nice to do this only once, but there MIGHT
                // be TOP elements in more than one image container...too complicated,
                // and this only happens once per TOP.
                Comical.update(
                    BubbleManager.getTopLevelImageContainerElement(top)!
                );
            }
        });
    }

    // The event handler to be called when something relevant on the page frame gets focus.
    // This will set the active textOverPicture element.
    public static onFocusSetActiveElement(event: Event) {
        // The current target is the element we attached the event listener to
        const focusedElement = event.currentTarget as Element;

        // If we focus something on the page that isn't in a bubble, we need to switch
        // to having no active bubble element. Note: we don't want to use focusout
        // on the bubble elements, because then we lose the active element while clicking
        // on controls in the toolbox (and while debugging).

        // We don't think this function ever gets called when it's not initialized, but it doesn't
        // hurt to make sure.
        initializeBubbleManager();

        const bubbleElement = focusedElement.closest(kTextOverPictureSelector);
        if (bubbleElement) {
            theOneBubbleManager.setActiveElement(bubbleElement as HTMLElement);
        } else {
            theOneBubbleManager.setActiveElement(undefined);
        }
    }

    private static onDocClickClearActiveElement(event: Event) {
        const clickedElement = event.target as Element; // most local thing clicked on
        if (!clickedElement.closest) {
            // About the only other possibility is that it's the top-level document.
            // If that's the target, we didn't click in an image container or button.
            return;
        }
        if (
            BubbleManager.getTopLevelImageContainerElement(clickedElement) ||
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
        if (this.activeElement) {
            tryRemoveImageEditingButtons(
                this.activeElement.getElementsByClassName(
                    "bloom-imageContainer"
                )[0] as Element | undefined
            );
        }
        this.activeElement = element;
        if (this.notifyBubbleChange) {
            this.notifyBubbleChange(this.getSelectedFamilySpec());
        }
        Comical.activateElement(this.activeElement);
    }

    // Set the color of the text in all of the active bubble family's TextOverPicture boxes.
    // If hexOrRgbColor is empty string, we are setting the bubble to use the style default.
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
        this.restoreFocus();
    }

    private setTextColorInternal(hexOrRgbColor: string, element: HTMLElement) {
        // BL-11621: We are in the process of moving to putting the Overlay text color on the inner
        // bloom-editables. So we clear any color on the textOverPicture div and set it on all of the
        // inner bloom-editables.
        const topBox = element.closest(
            kTextOverPictureSelector
        ) as HTMLDivElement;
        topBox.style.color = "";
        const editables = topBox.getElementsByClassName("bloom-editable");
        for (let i = 0; i < editables.length; i++) {
            const editableElement = editables[i] as HTMLElement;
            editableElement.style.color = hexOrRgbColor;
        }
    }

    public getTextColorInformation(): ITextColorInfo {
        const activeEl = theOneBubbleManager.getActiveElement();
        let textColor = "";
        let isDefaultStyleColor = false;
        if (activeEl) {
            const topBox = activeEl.closest(
                kTextOverPictureSelector
            ) as HTMLDivElement;
            // const allUserStyles = StyleEditor.GetFormattingStyleRules(
            //     topBox.ownerDocument
            // );
            const style = topBox.style;
            textColor = style && style.color ? style.color : "";
            // We are in the process of moving to putting the Overlay text color on the inner
            // bloom-editables. So if the textOverPicture div didn't have a color, check the inner
            // bloom-editables.
            if (textColor === "") {
                const editables = topBox.getElementsByClassName(
                    "bloom-editable"
                );
                if (editables.length === 0) {
                    // Image on Image case comes here.
                    isDefaultStyleColor = true;
                    textColor = "black";
                } else {
                    const firstEditable = editables[0] as HTMLElement;
                    const colorStyle = firstEditable.style.color;
                    if (colorStyle) {
                        textColor = colorStyle;
                    } else {
                        textColor = this.getDefaultStyleTextColor(
                            firstEditable
                        );
                        isDefaultStyleColor = true;
                    }
                }
            }
        }
        return { color: textColor, isDefault: isDefaultStyleColor };
    }

    // Returns the computed color of the text, which in the absence of a color style from the
    // Overlay Tool will be from the Bubble-style (set in the StyleEditor).
    // An unfortunate, but greatly simplifying, use of JQuery.
    public getDefaultStyleTextColor(firstEditable: HTMLElement): string {
        return $(firstEditable).css("color");
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
        const newBackgroundColors = colors;
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
        this.restoreFocus();
    }

    // Here we keep track of something (currently, typically, an input box in
    // the color chooser) to which focus needs to be restored after we modify
    // foreground or background color on the bubble, since those processes
    // involve focusing the bubble and this is inconvenient when typing in the
    // input boxes.
    private thingToFocusAfterSettingColor: HTMLElement;
    private restoreFocus() {
        if (this.thingToFocusAfterSettingColor) {
            this.thingToFocusAfterSettingColor.focus();
            // I don't fully understand why we need this, but without it, the input
            // doesn't end up focused. Apparently we just need to overcome whatever
            // is stealing the focus before the next cycle.
            setTimeout(() => {
                this.thingToFocusAfterSettingColor.focus();
            }, 0);
        }
    }

    public setThingToFocusAfterSettingColor(x: HTMLElement): void {
        this.thingToFocusAfterSettingColor = x;
    }

    public getBackgroundColorArray(familySpec: BubbleSpec): string[] {
        if (
            !familySpec.backgroundColors ||
            familySpec.backgroundColors.length === 0
        ) {
            return ["white"];
        }
        return familySpec.backgroundColors;
    }

    // drag-and-drop support for bubbles from comical toolbox
    private setDragAndDropHandlers(container: HTMLElement): void {
        if (isLinux()) return; // these events never fire on Linux: see BL-7958.
        // This suppresses the default behavior, which is to forbid dragging things to
        // an element, but only if the source of the drag is a bloom bubble.
        container.ondragover = ev => {
            if (
                ev.dataTransfer &&
                // don't be tempted to return to ev.dataTransfer.getData("text/x-bloombubble")
                // as we used with geckofx. In WebView2, this returns an empty string.
                // I think it is some sort of security thing, the idea is that something
                // you're just dragging over shouldn't have access to the content.
                // The presence of our custom data type at all indicates this is something
                // we want to accept dropped here.
                // (types is an array: indexOf returns -1 if the item is not found)
                ev.dataTransfer.types.indexOf("text/x-bloombubble") >= 0
            ) {
                ev.preventDefault();
            }
        };
        // Controls what happens when a bloom bubble is dropped. We get the style
        // set in ComicToolControls.ondragstart() and make a bubble with that style
        // at the drop position.
        container.ondrop = ev => {
            // test this so we don't interfere with dragging for text edit,
            // nor add bubbles when something else is dragged
            if (
                ev.dataTransfer &&
                ev.dataTransfer.getData("text/x-bloombubble")
            ) {
                ev.preventDefault();
                const style = ev.dataTransfer
                    ? ev.dataTransfer.getData("text/x-bloombubble")
                    : "speech";
                this.addOverPictureElement(ev.clientX, ev.clientY, style);
            }
        };
    }

    // Setup event handlers that allow the bubble to be moved around or resized.
    private setMouseDragHandlers(container: HTMLElement): void {
        // An earlier version of this code set onmousedown to this.onMouseDown, etc.
        // We need to use addEventListener so we can capture.
        // It's unlikely, but I can't rule it out, that a deliberate side effect
        // was to remove some other onmousedown handler. Just in case, clear the fields.
        // I don't think setting these has any effect on handlers done with addEventListener,
        // but just in case, I'm doing this first.
        container.onmousedown = null;
        container.onmousemove = null;
        container.onmouseup = null;

        // We use mousemove effects instead of drag due to concerns that drag effects would make the entire image container appear to drag.
        // Instead, with mousemove, we can make only the specific bubble move around
        // Grabbing these (particularly the move event) in the capture phase allows us to suppress
        // effects of ctrl and alt clicks on the text.
        container.addEventListener("mousedown", this.onMouseDown, {
            capture: true
        });

        container.addEventListener("mousemove", this.onMouseMove, {
            capture: true
        });

        container.addEventListener("mouseup", this.onMouseUp, {
            capture: true
        });

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

    // Move all child bubbles as necessary so they are at least partly inside their container
    // (by as much as we require when dragging them).
    private ensureBubblesIntersectParent(parentContainer: HTMLElement) {
        const overlays = Array.from(
            parentContainer.getElementsByClassName(kTextOverPictureClass)
        );
        let changed = false;
        overlays.forEach(overlay => {
            const bubbleRect = overlay.getBoundingClientRect();
            this.adjustBubbleLocation(
                overlay as HTMLElement,
                parentContainer,
                new Point(
                    bubbleRect.left,
                    bubbleRect.top,
                    PointScaling.Scaled,
                    "ensureBubblesIntersectParent"
                )
            );
            changed = this.ensureTailsInsideParent(
                parentContainer,
                overlay as HTMLElement,
                changed
            );
        });
        if (changed) {
            Comical.update(parentContainer);
        }
    }

    // Make sure the handles of the tail(s) of the overlay are within the container.
    // Return true if any tail was changed (or if changed was already true)
    private ensureTailsInsideParent(
        imageContainer: HTMLElement,
        overlay: HTMLElement,
        changed: boolean
    ) {
        const originalTailSpecs = Bubble.getBubbleSpec(overlay).tails;
        const newTails = originalTailSpecs.map(spec => {
            const tipPoint = this.adjustRelativePointToImageContainer(
                imageContainer,
                new Point(
                    spec.tipX,
                    spec.tipY,
                    PointScaling.Unscaled,
                    "ensureTailsInsideParent.tip"
                )
            );
            const midPoint = this.adjustRelativePointToImageContainer(
                imageContainer,
                new Point(
                    spec.midpointX,
                    spec.midpointY,
                    PointScaling.Unscaled,
                    "ensureTailsInsideParent.tip"
                )
            );
            changed =
                changed || // using changed ||= works but defeats prettier
                spec.tipX != tipPoint.getUnscaledX() ||
                spec.tipY != tipPoint.getUnscaledY() ||
                spec.midpointX != midPoint.getUnscaledX() ||
                spec.midpointY != midPoint.getUnscaledY();
            return {
                ...spec,
                tipX: tipPoint.getUnscaledX(),
                tipY: tipPoint.getUnscaledY(),
                midpointX: midPoint.getUnscaledX(),
                midpointY: midPoint.getUnscaledY()
            };
        });
        const bubble = new Bubble(overlay);
        bubble.mergeWithNewBubbleProps({ tails: newTails });
        return changed;
    }
    // This is pretty small, but it's the amount of the text box that has to be visible;
    // typically a bit more of the actual bubble can be seen.
    // Arguably it would be better to use a slightly larger number and make it apply to the
    // actual bubble outline, but
    // - this is much harder; we'd need ComicalJs enhancments to know exactly where the edge
    //   of the bubble is.
    // - the two dimensions would not be independent; a bubble whose top is above the bottom
    //   of the container and whose right is to the right of the contaniner's left
    //   might still be entirely invisible as its curve places it entirely beyond the bottom
    //   left corner.
    // - The constraint would actually be different depending on the type of bubble,
    //   which means a bubble might need to move as a result of changing its bubble type.
    private minBubbleVisible = 10;

    // Conceptually, move the bubble to the specified location (which may be where it is already).
    // However, first adjust the location to make sure at least a little of the bubble is visible
    // within the specified container. (This means the method may be used both to constrain moving
    // the bubble, and also, by passing its current location, to ensure it becomes visible if
    // it somehow stopped being.)
    private adjustBubbleLocation(
        bubble: HTMLElement,
        container: HTMLElement,
        location: Point
    ) {
        const bubbleRect = bubble.getBoundingClientRect();
        const parentRect = container.getBoundingClientRect();
        const left = location.getScaledX();
        const right = left + bubbleRect.width;
        const top = location.getScaledY();
        const bottom = top + bubbleRect.height;
        let x = left;
        let y = top;
        if (right < parentRect.left + this.minBubbleVisible) {
            x = parentRect.left + this.minBubbleVisible - bubbleRect.width;
        }
        if (left > parentRect.right - this.minBubbleVisible) {
            x = parentRect.right - this.minBubbleVisible;
        }
        if (bottom < parentRect.top + this.minBubbleVisible) {
            y = parentRect.top + this.minBubbleVisible - bubbleRect.height;
        }
        if (top > parentRect.bottom - this.minBubbleVisible) {
            y = parentRect.bottom - this.minBubbleVisible;
        }
        // The 0.1 here is rather arbitrary. On the one hand, I don't want to do all the work
        // of placeElementAtPosition in the rather common case that we're just checking bubble
        // positions at startup and none need to move. On the other hand, we're dealing with scaling
        // here, and it's possible that even a half pixel might get scaled so that the difference
        // is noticeable. I'm compromizing on a discrepancy that is less than a pixel at our highest
        // zoom.
        if (
            Math.abs(x - bubbleRect.left) > 0.1 ||
            Math.abs(y - bubbleRect.top) > 0.1
        ) {
            const moveTo = new Point(
                x,
                y,
                PointScaling.Scaled,
                "AdjustBubbleLocation"
            );
            this.placeElementAtPosition($(bubble), container, moveTo);
        }
    }

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseDown = (event: MouseEvent) => {
        const container = event.currentTarget as HTMLElement;
        // Let standard clicks on the bloom editable or other UI elements only be processed by that element
        if (this.isMouseEventAlreadyHandled(event)) {
            return;
        }

        // These coordinates need to be relative to the canvas (which is the same as relative to the image container).
        const coordinates = this.getPointRelativeToCanvas(event, container);

        if (!coordinates) {
            return;
        }

        // If the click is on one of our drag handles (typically for another bubble), we don't
        // want to drag this one as well using the events here.
        if (
            event.target instanceof Element &&
            event.target.closest(".bloom-dragHandle")
        ) {
            return;
        }

        const bubble = Comical.getBubbleHit(
            container,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY()
        );

        if (
            Comical.isDraggableNear(
                container,
                coordinates.getUnscaledX(),
                coordinates.getUnscaledY()
            )
        ) {
            // If we're starting to drag something, typically a tail handle, in Comical,
            // don't do any other mouse activity.
            return;
        }

        if (bubble) {
            // We clicked on a bubble, and either ctrl or alt is down, or we clicked outside the
            // editable part. If we're outside the editable but inside the bubble, we don't need any default event processing,
            // and if we're inside and ctrl or alt is down, we want to prevent the events being
            // processed by the text.
            event.preventDefault();
            event.stopPropagation();
            this.focusFirstVisibleFocusable(bubble.content);
            const positionInfo = bubble.content.getBoundingClientRect();

            if (!event.altKey) {
                // Move action started
                this.bubbleToDrag = bubble;
                this.activeContainer = container;
                // in case this is somehow left from earlier, we want a fresh start for the new move.
                this.animationFrame = 0;
                // mouse is presumably moving inside this image, it should have the buttons.
                // (It does not get them in the usual way by mouseEnter, because that event is
                // suppressed by the comicaljs canvas, which is above the image container.)
                addImageEditingButtons(
                    bubble.content.getElementsByClassName(
                        "bloom-imageContainer"
                    )[0] as HTMLElement
                );

                // Remember the offset between the top-left of the content box and the initial
                // location of the mouse pointer.
                const deltaX = event.pageX - positionInfo.left;
                const deltaY = event.pageY - positionInfo.top;
                this.bubbleDragGrabOffset = { x: deltaX, y: deltaY };

                // Even though Alt+Drag resize is not in effect, we still check using isResizing() to
                // make sure JQuery Resizing is not in effect before proceeding.
                if (!this.isJQueryResizing(container)) {
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
    };

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseMove = (event: MouseEvent) => {
        // Capture the most recent data to use when our animation frame request is satisfied.
        // or so keyboard events can reference the current mouse position.
        this.lastMoveEvent = event;

        const container = event.currentTarget as HTMLElement;
        // Prevent two event handlers from triggering if the text box is currently being resized
        if (this.isJQueryResizing(container)) {
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
    };

    // Mouse hover - No move or resize is currently active, but check if there is a bubble under the mouse that COULD be
    // and add or remove the classes we use to indicate this
    private handleMouseMoveHover(event: MouseEvent, container: HTMLElement) {
        if (this.isMouseEventAlreadyHandled(event)) {
            this.cleanupMouseMoveHover(container);
            return;
        }

        let hoveredBubble = this.getBubbleUnderMouse(event, container);

        // Now there are several options depending on various conditions. There's some
        // overlap in the conditions and it is tempting to try to combine into a single compound
        // "if" statement. But note, this first one may change hoveredBubble to null,
        // which then changes which of the following options is chosen. Be careful!
        if (hoveredBubble && hoveredBubble.content !== this.activeElement) {
            // The hovered bubble is not selected. If it's an image, the user might
            // want to drag a tail tip there, which is hard to do with a grab cursor,
            // so don't switch.
            if (this.isPictureOverPictureElement(hoveredBubble.content)) {
                hoveredBubble = null;
            }
        }

        if (!hoveredBubble) {
            // Cleanup the previous iteration's state
            this.cleanupMouseMoveHover(container);
            if (this.activeElement) {
                tryRemoveImageEditingButtons(
                    this.activeElement.getElementsByClassName(
                        "bloom-imageContainer"
                    )[0]
                );
            }
            return;
        }

        if (
            hoveredBubble &&
            hoveredBubble.content === this.activeElement &&
            this.isPictureOverPictureElement(hoveredBubble.content)
        ) {
            // Make sure the image editing buttons are present as expected.
            // (It does not get them in the usual way by mouseEnter, because that event is
            // suppressed by the comicaljs canvas, which is above the image container.)
            const imageContainers = hoveredBubble.content.getElementsByClassName(
                "bloom-imageContainer"
            );
            if (imageContainers.length) {
                const imageContainer = imageContainers[0] as HTMLElement;
                addImageEditingButtons(imageContainer);
            }
        }

        const isVideo = this.isVideoOverPictureElement(hoveredBubble.content);
        const targetElement = event.target as HTMLElement;
        // Over a bubble that could be dragged (ignoring the bloom-editable portion),
        // make the mouse indicate that dragging/resizing is possible.
        if (!event.altKey) {
            if (isVideo) {
                // Don't add "grabbable" to video over picture, because click will play the video,
                // not drag it (unless we're holding the Ctrl key down).
                if (event.ctrlKey) {
                    // In this case, we want to drag the container.
                    container.classList.add("grabbable");
                    targetElement.removeAttribute("controls");
                } else {
                    // In this case, we want to play the video, if we click on it.
                    container.classList.remove("grabbable");
                    targetElement.setAttribute("controls", "");
                }
            } else {
                container.classList.add("grabbable");
            }
        } else {
            this.applyResizingUI(hoveredBubble, container, event, isVideo);
        }
    }

    /**
     * Gets the bubble under the mouse location, or null if no bubble is
     */
    public getBubbleUnderMouse(
        event: MouseEvent,
        container: HTMLElement
    ): Bubble | null {
        const coordinates = this.getPointRelativeToCanvas(event, container);
        if (!coordinates) {
            // Give up
            return null;
        }

        return (
            Comical.getBubbleHit(
                container,
                coordinates.getUnscaledX(),
                coordinates.getUnscaledY()
            ) ?? null
        );
    }

    /**
     * Applies resizing UI, including:
     * - Make the mouse cursor reflect the direction in which the currently hovered bubble can be resized.
     * - Remove video controls
     *
     * @param hoveredBubble The bubble currently being hovered
     * @param container The current main bloom-imageContainer which contains the currently hovered bubble (if applicable)
     * @param event The most recent mouse event.
     * @param isVideo Whether ${hoveredBubble} is a video overlay.
     */
    public applyResizingUI(
        hoveredBubble: Bubble,
        container: HTMLElement,
        event: MouseEvent,
        isVideo: boolean
    ) {
        this.cleanupMouseMoveHover(container); // Need to clear both grabbable and *-resizables

        const resizeMode = this.getResizeMode(hoveredBubble.content, event);
        container.classList.add(`${resizeMode}-resizable`);

        if (isVideo && event.target instanceof Element) {
            event.target.removeAttribute("controls");
        }
    }

    /**
     * Attempts to apply resizing UI, given only the current image container.
     * Will find the currently hovered bubble (if any)
     * Does nothing if no bubble is currently being hovered
     * @param container The current main bloom-imageContainer which contains the currently hovered bubble (if any)
     */
    public tryApplyResizingUI(container: HTMLElement) {
        const mouseEvent = this.lastMoveEvent;
        const hoveredBubble = this.getBubbleUnderMouse(mouseEvent, container);
        if (!hoveredBubble) {
            return;
        }

        const isVideo = this.isVideoOverPictureElement(hoveredBubble.content);
        this.applyResizingUI(hoveredBubble, container, mouseEvent, isVideo);
    }

    private animationFrame: number;
    private lastMoveEvent: MouseEvent;
    private lastMoveContainer: HTMLElement;

    // A bubble is currently in drag mode, and the mouse is being moved.
    // Move the bubble accordingly.
    private handleMouseMoveDragBubble(
        event: MouseEvent,
        container: HTMLElement
    ) {
        // Capture the most recent data to use when our animation frame request is satisfied.
        this.lastMoveContainer = container;
        // We don't want any other effects of mouse move, like selecting text in the box,
        // to happen while we're dragging it around.
        event.preventDefault();
        event.stopPropagation();
        if (this.animationFrame) {
            // already working on an update, starting another before
            // we complete it only slows rendering.
            // The site where I got this idea suggested instead using cancelAnimationFrame at this
            // point. One possible advantage is that the very last mousemove before mouse up is
            // then certain to get processed. But it seemed to be significantly less effective
            // at getting frames fully rendered often, and the difference in where the box ends up
            // is unlikely to be significant...the user will keep dragging until satisfied.
            // Note that we're capturing the mouse position from the most recent move event.
            // The most we can lose is the movement between when we start the requestAnimationFrame
            // callback and a subsequent mouseUp before the callback returns and clears
            // this.animationFrame (which will allow the next mouse move to start a new request).
            // That may not even be possible (the system would likely do another mouse move after
            // the callback and before the mouseup, if the mouse had moved again?). But at worst,
            // we can only lose the movement in the time it takes us to move the box once...about 1/30
            // second on my system when throttled 6x.
            return;
        }
        this.animationFrame = requestAnimationFrame(() => {
            if (!this.bubbleToDrag) {
                // This case could be reached when using the JQuery drag handle.
                this.animationFrame = 0; // must clear, or move will forever be blocked.
                return;
            }

            const newPosition = new Point(
                this.lastMoveEvent.pageX - this.bubbleDragGrabOffset.x,
                this.lastMoveEvent.pageY - this.bubbleDragGrabOffset.y,
                PointScaling.Scaled,
                "Created by handleMouseMoveDragBubble()"
            );
            this.adjustBubbleLocation(
                this.bubbleToDrag.content,
                this.lastMoveContainer,
                newPosition
            );
            this.animationFrame = 0;
        });
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
        // If we're resizing, we don't want to get effects of dragging within the text.
        event.preventDefault();
        event.stopPropagation();

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
        this.placeElementAtPosition(content, container, newPoint);
    }

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseUp = (event: MouseEvent) => {
        const container = event.currentTarget as HTMLElement;
        if (this.bubbleToDrag || this.bubbleToResize) {
            // if we're doing a resize or drag, we don't want ordinary mouseup activity
            // on the text inside the bubble.
            event.preventDefault();
            event.stopPropagation();
        }

        this.bubbleToDrag = undefined;
        container.classList.remove("grabbing");
        this.turnOffResizing(container);
    };

    public turnOffResizing(container: Element) {
        this.clearResizeModeClasses(container);
        this.activeContainer = undefined;
        this.bubbleToResize = undefined;
        this.bubbleResizeMode = "";
    }

    public isResizing(container: Element) {
        return this.bubbleToResize || this.isJQueryResizing(container);
    }

    // Returns true if any of the container's children are currently being resized using JQuery
    private isJQueryResizing(container: Element) {
        // First check the class that we try to always apply when starting a JQuery resize
        // (the class is applied so that we know one is in progress even before the mouse moves.)
        if (container.classList.contains("ui-jquery-resizing-in-progress")) {
            return true;
        }

        // Double-check using the JQuery class
        return (
            container.getElementsByClassName("ui-resizable-resizing").length > 0
        );
    }

    /**
     * Returns true if a handler already exists to sufficiently process this mouse event
     * without needing our custom onMouseDown/onMouseHover/etc event handlers to process it
     */
    private isMouseEventAlreadyHandled(ev: MouseEvent): boolean {
        const targetElement = ev.target instanceof Element ? ev.target : null;
        if (!targetElement) {
            // As far as I can research, the target of a mouse event is always
            // "the most deeply nested element." Apparently some very old browsers
            // might answer a text node, but I think that stopped well before FF60.
            // Therefore ev.target should be an element, not null or undefined or
            // some other object, and it should have a classList, and calling contains
            // on that classList should not throw.
            // But: BL-11668 shows that it IS possible for classList to be undefined.
            // Some testing revealed that somehow, most likely when dragging rapidly
            // towards the edge of the document, we can get an event where target is
            // the root document, which doesn't have a classList.
            // Since we're looking for the click to be on some particular element,
            // if somehow it's not connected to an element at all, I think we can safely
            // return false.
            return false;
        }
        if (targetElement.classList.contains("bloom-dragHandle")) {
            // The drag handle is outside the bubble, so dragging it with the mouse
            // events we handle doesn't work. Returning true lets its own event handler
            // deal with things, and is a good thing even when ctrl or alt is down.
            return true;
        }
        if (targetElement.classList.contains("ui-resizable-handle")) {
            // Ignore clicks on the JQuery resize handles.
            return true;
        }
        if (targetElement.classList.contains("imageOverlayButton")) {
            // Ignore clicks on the image overlay buttons. The button's handler should process that instead.
            return true;
        }
        if (ev.ctrlKey || ev.altKey) {
            return false;
        }
        if (targetElement.closest(".bloom-videoContainer")) {
            // want playback-related behavior in video containers
            return true;
        }
        const isInsideEditable = !!targetElement.closest(".bloom-editable");
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
    private getResizeMode(
        element: HTMLElement,
        event: MouseEvent
    ): ResizeDirection {
        // Convert into a coordinate system where the origin is the center of the element (rather than the top-left of the page)
        const center = this.getCenterPosition(element);
        const clickCoordinates = { x: event.pageX, y: event.pageY };
        const relativeCoordinates = {
            x: clickCoordinates.x - center.x,
            y: clickCoordinates.y - center.y
        };

        let resizeMode: ResizeDirection;
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

    private cleanupMouseMoveHover(element: Element): void {
        element.classList.remove("grabbable");
        this.clearResizeModeClasses(element);
    }

    public clearResizeModeClasses(element: Element): void {
        element.classList.remove("ne-resizable");
        element.classList.remove("nw-resizable");
        element.classList.remove("sw-resizable");
        element.classList.remove("se-resizable");
    }

    public turnOffBubbleEditing(): void {
        if (this.isComicEditingOn === false) {
            return; // Already off. No work needs to be done.
        }
        this.isComicEditingOn = false;

        Comical.setActiveBubbleListener(undefined);
        Comical.stopEditing();
        this.getAllPrimaryImageContainersOnPage().forEach(container =>
            this.saveCurrentOverlayStateAsCurrentLangAlternate(
                container as HTMLElement
            )
        );

        EnableAllImageEditing();

        // Clean up event listeners that we no longer need
        Array.from(
            document.getElementsByClassName(kTextOverPictureClass)
        ).forEach(container => {
            const focusables = this.getAllVisibleFocusableDivs(
                container as HTMLElement
            );
            focusables.forEach(element => {
                // Don't use an arrow function as an event handler here. These can never be identified as duplicate event listeners, so we'll end up with tons of duplicates
                element.removeEventListener(
                    "focusin",
                    BubbleManager.onFocusSetActiveElement
                );
            });
        });
        document.removeEventListener(
            "click",
            BubbleManager.onDocClickClearActiveElement
        );
    }

    public cleanUp(): void {
        // We used to close a WebSocket here; saving the hook in case we need it someday.
    }

    // Gets the bubble spec of the active element. (If it is a child, the child's partial bubble spec will be returned)
    public getSelectedItemBubbleSpec(): BubbleSpec | undefined {
        if (!this.activeElement) {
            return undefined;
        }
        return Bubble.getBubbleSpec(this.activeElement);
    }

    // Get the active element's family's bubble spec. (i.e., the root/patriarch of the active element)
    public getSelectedFamilySpec(): BubbleSpec | undefined {
        const tempBubble = this.getPatriarchBubbleOfActiveElement();
        return tempBubble ? tempBubble.getBubbleSpec() : undefined;
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

        // ENHANCE: Constructing new bubble instances is dangerous. It may get out of sync with the instance that Comical knows about.
        // It would be preferable if we asked Comical to find the bubble instance corresponding to this element.
        const activeBubble = new Bubble(this.activeElement);

        return this.updateBubbleWithPropsHelper(activeBubble, newBubbleProps);
    }

    public updateSelectedFamilyBubbleSpec(newBubbleProps: BubbleSpecPattern) {
        const parentBubble = this.getPatriarchBubbleOfActiveElement();
        return this.updateBubbleWithPropsHelper(parentBubble, newBubbleProps);
    }

    private updateBubbleWithPropsHelper(
        bubble: Bubble | undefined,
        newBubbleProps: BubbleSpecPattern
    ): BubbleSpec | undefined {
        if (!this.activeElement || !bubble) {
            return undefined;
        }

        bubble.mergeWithNewBubbleProps(newBubbleProps);
        Comical.update(this.activeElement.parentElement!);

        // BL-9548: Interaction with the toolbox panel makes the bubble lose focus, which requires
        // we re-activate the current comical element.
        Comical.activateElement(this.activeElement);

        return bubble.getBubbleSpec();
    }

    // Adds a new over-picture element as a child of the specified {parentElement}
    //    (It is a child in the sense that the Comical library will recognize it as a child)
    // {offsetX}/{offsetY} is the offset in position from the parent to the child elements
    //    (i.e., offsetX = child.left - parent.left)
    //    (remember that positive values of Y are further to the bottom)
    // This is what the comic tool calls when the user clicks ADD CHILD BUBBLE.
    public addChildOverPictureElementAndRefreshPage(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number
    ): void {
        // The only reason to keep a separate method here is that the 'internal' form returns
        // the new child. We don't need it here, but we do in the duplicate bubble function.
        this.addChildInternal(parentElement, offsetX, offsetY);
    }

    // Make sure comical is up-to-date in the case where we know there is a selected/current element.
    private updateComicalForSelectedElement(element: HTMLElement) {
        if (!element) {
            return;
        }
        const imageContainer = BubbleManager.getTopLevelImageContainerElement(
            element
        );
        if (!imageContainer) {
            return; // shouldn't happen...
        }
        const comicalGenerated = imageContainer.getElementsByClassName(
            kComicalGeneratedClass
        );
        if (comicalGenerated.length > 0) {
            Comical.update(imageContainer);
        }
    }

    private addChildInternal(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number
    ): HTMLElement | undefined {
        // Make sure everything in parent is "saved".
        this.updateComicalForSelectedElement(parentElement);

        const newPoint = this.findBestLocationForNewBubble(
            parentElement,
            offsetX,
            offsetY
        );
        if (!newPoint) {
            return undefined;
        }

        const childElement = this.addOverPictureElement(
            newPoint.getScaledX(),
            newPoint.getScaledY(),
            undefined
        );
        if (!childElement) {
            return undefined;
        }

        // Make sure that the child inherits any non-default text color from the parent bubble
        // (which must be the active element).
        this.setActiveElement(parentElement);
        const parentTextColor = this.getTextColorInformation();
        if (!parentTextColor.isDefault) {
            this.setTextColorInternal(parentTextColor.color, childElement);
        }

        Comical.initializeChild(childElement, parentElement);
        // In this case, the 'addOverPictureElement()' above will already have done the new bubble's
        // refresh. We still want to refresh, but not attach to ckeditor, etc., so we pass
        // attachEventsToEditables as false.
        this.refreshBubbleEditing(
            BubbleManager.getTopLevelImageContainerElement(parentElement)!,
            new Bubble(childElement),
            false
        );
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
        const container = BubbleManager.getTopLevelImageContainerElement(
            parentElement
        );
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
        point: Point
    ): Point {
        const maxWidth = (imageContainer as HTMLElement).offsetWidth;
        const maxHeight = (imageContainer as HTMLElement).offsetHeight;
        let newX = point.getUnscaledX();
        let newY = point.getUnscaledY();

        const bufferPixels = 15;
        if (newX < 1) {
            newX = bufferPixels;
        } else if (newX > maxWidth) {
            newX = maxWidth - bufferPixels;
        }

        if (newY < 1) {
            newY = bufferPixels;
        } else if (newY > maxHeight) {
            newY = maxHeight - bufferPixels;
        }
        return new Point(
            newX,
            newY,
            PointScaling.Unscaled,
            "Scaled viewport coordinates"
        );
    }

    public addOverPictureElementWithScreenCoords(
        screenX: number,
        screenY: number,
        style: string
    ) {
        const clientX = screenX - window.screenX;
        const clientY = screenY - window.screenY;
        this.addOverPictureElement(clientX, clientY, style);
    }

    private addOverPictureElementFromOriginal(
        offsetX: number,
        offsetY: number,
        originalElement: HTMLElement,
        style?: string
    ): HTMLElement | undefined {
        const imageContainer = BubbleManager.getTopLevelImageContainerElement(
            originalElement
        );
        if (!imageContainer) {
            return undefined;
        }
        const positionInViewport = new Point(
            offsetX,
            offsetY,
            PointScaling.Scaled,
            "Scaled Viewport coordinates"
        );
        // Detect if the original is a picture over picture or video over picture element.
        if (this.isPictureOverPictureElement(originalElement)) {
            return this.addPictureOverPicture(
                positionInViewport,
                $(imageContainer)
            );
        }
        if (this.isVideoOverPictureElement(originalElement)) {
            return this.addVideoOverPicture(
                positionInViewport,
                $(imageContainer)
            );
        }
        return this.addTextOverPicture(
            positionInViewport,
            $(imageContainer),
            style
        );
    }

    private isOverPictureElementWithClass(
        overPictureElement: HTMLElement,
        className: string
    ): boolean {
        for (let i = 0; i < overPictureElement.childElementCount; i++) {
            const child = overPictureElement.children[i] as HTMLElement;
            if (child && child.classList.contains(className)) {
                return true;
            }
        }
        return false;
    }

    public isActiveElementPictureOverPicture(): boolean {
        if (!this.activeElement) {
            return false;
        }
        return this.isPictureOverPictureElement(this.activeElement);
    }

    private isPictureOverPictureElement(
        overPictureElement: HTMLElement
    ): boolean {
        return this.isOverPictureElementWithClass(
            overPictureElement,
            kImageContainerClass
        );
    }

    private isVideoOverPictureElement(
        overPictureElement: HTMLElement
    ): boolean {
        return this.isOverPictureElementWithClass(
            overPictureElement,
            kVideoContainerClass
        );
    }

    public isActiveElementVideoOverPicture(): boolean {
        if (!this.activeElement) {
            return false;
        }
        return this.isVideoOverPictureElement(this.activeElement);
    }

    // This method is called when the user "drops" an element from the comicTool onto an image.
    // It is also called by addChildInternal() and by the Linux version of dropping: "ondragend".
    public addOverPictureElement(
        mouseX: number,
        mouseY: number,
        style?: string
    ): HTMLElement | undefined {
        const imageContainer = this.getImageContainerFromMouse(mouseX, mouseY);
        if (!imageContainer || imageContainer.length === 0) {
            // Don't add an OverPicture element if we can't find the containing imageContainer.
            return undefined;
        }
        // initial mouseX, mouseY coordinates are relative to viewport
        const positionInViewport = new Point(
            mouseX,
            mouseY,
            PointScaling.Scaled,
            "Scaled Viewport coordinates"
        );
        if (style === "video") {
            return this.addVideoOverPicture(positionInViewport, imageContainer);
        }
        if (style === "image") {
            return this.addPictureOverPicture(
                positionInViewport,
                imageContainer
            );
        }
        return this.addTextOverPicture(
            positionInViewport,
            imageContainer,
            style
        );
    }

    private addTextOverPicture(
        location: Point,
        imageContainerJQuery: JQuery,
        style?: string
    ): HTMLElement {
        const defaultNewTextLanguage = GetSettings().languageForNewTextBoxes;
        // add a draggable text bubble to the html dom of the current page
        const editableDivClasses =
            "bloom-editable bloom-content1 bloom-visibility-code-on Bubble-style";
        const editableDivHtml =
            "<div class='" +
            editableDivClasses +
            "' lang='" +
            defaultNewTextLanguage +
            "'><p></p></div>";
        const transGroupDivClasses =
            "bloom-translationGroup bloom-leadingElement";
        const transGroupHtml =
            "<div class='" +
            transGroupDivClasses +
            "' data-default-languages='V'>" +
            editableDivHtml +
            "</div>";

        return this.finishAddingOverPictureElement(
            imageContainerJQuery,
            transGroupHtml,
            location,
            style,
            false
        );
    }

    private addVideoOverPicture(
        location: Point,
        imageContainerJQuery: JQuery
    ): HTMLElement {
        const standardVideoClasses =
            kVideoContainerClass +
            " bloom-noVideoSelected bloom-leadingElement";
        const videoContainerHtml =
            "<div class='" + standardVideoClasses + "' tabindex='0'></div>";
        return this.finishAddingOverPictureElement(
            imageContainerJQuery,
            videoContainerHtml,
            location,
            "none",
            true
        );
    }

    private addPictureOverPicture(
        location: Point,
        imageContainerJQuery: JQuery
    ): HTMLElement {
        const standardImageClasses =
            kImageContainerClass + " bloom-leadingElement";
        const imagePlaceHolderHtml = "<img src='placeHolder.png' alt=''></img>";
        const imageContainerHtml =
            // The tabindex here is necessary to get focus to work on an image.
            "<div tabindex='0' class='" +
            standardImageClasses +
            "'>" +
            imagePlaceHolderHtml +
            "</div>";
        return this.finishAddingOverPictureElement(
            imageContainerJQuery,
            imageContainerHtml,
            location,
            "none",
            true
        );
    }

    private finishAddingOverPictureElement(
        imageContainerJQuery: JQuery,
        internalHtml: string,
        location: Point,
        style?: string,
        setElementActive?: boolean
    ): HTMLElement {
        // add OverPicture element as last child of .bloom-imageContainer (BL-7883)
        const lastContainerChild = imageContainerJQuery.children().last();
        const wrapperHtml =
            "<div class='" +
            kTextOverPictureClass +
            "'>" +
            internalHtml +
            "</div>";
        // It's especially important that the new overlay comes AFTER the main image,
        // since that's all that keeps it on top of the image. We're deliberately not
        // using z-index so that the image-container is not a stacking context so we
        // can use z-index on the buttons inside it to put them above the comicaljs canvas.
        const wrapperJQuery = $(wrapperHtml).insertAfter(lastContainerChild);
        this.setDefaultWrapperBoxHeight(wrapperJQuery);
        const contentElement = wrapperJQuery.get(0);
        this.placeElementAtPosition(
            wrapperJQuery,
            imageContainerJQuery.get(0),
            location
        );

        // The following code would not be needed for Picture and Video bubbles if the focusin
        // handler were reliably called after being attached by refreshBubbleEditing() below.
        // However, calling the jquery.focus() method in bloomEditing.focusOnChildIfFound()
        // causes the handler to fire ONLY for Text bubbles.  This is a complete mystery to me.
        // Therefore, for Picture and Video bubbles, we set the content active and notify the
        // overlay tool. But we don't need/want the actions of setActiveElement() which overlap
        // with refreshBubbleEditing(). This code actually prevents bloomEditing.focusOnChildIfFound()
        // from being called, but that doesn't really matter since calling it does no good.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-11620.
        if (setElementActive) {
            this.activeElement = contentElement;
            if (this.notifyBubbleChange) {
                this.notifyBubbleChange(this.getSelectedFamilySpec());
            }
        }
        const bubble = new Bubble(contentElement);
        const bubbleSpec: BubbleSpec = Bubble.getDefaultBubbleSpec(
            contentElement,
            style || "speech"
        );
        bubble.setBubbleSpec(bubbleSpec);
        const imageContainer = imageContainerJQuery.get(0);
        this.refreshBubbleEditing(imageContainer, bubble, true);
        return contentElement;
    }

    // This 'wrapperBox' param has just been added to the DOM by JQuery before arriving here.
    // All of the text-based bubbles' default heights are based on the min-height of 30px set
    // in bubble.less for a .bloom-textOverPicture element. For video or picture over pictures,
    // we want something a bit taller.
    private setDefaultWrapperBoxHeight(wrapperBox: JQuery) {
        const width = parseInt(wrapperBox.css("width"), 10);
        if (wrapperBox.find(`.${kVideoContainerClass}`).length > 0) {
            // Set the default video aspect to 4:3, the same as the sign language tool generates.
            wrapperBox.css("height", (width * 3) / 4);
        }
        if (wrapperBox.find(kImageContainerSelector).length > 0) {
            // Set the default image aspect to square.
            wrapperBox.css("height", width);
        }
    }

    // mouseX and mouseY are the location in the viewport of the mouse
    private getImageContainerFromMouse(mouseX: number, mouseY: number): JQuery {
        const clickElement = document.elementFromPoint(mouseX, mouseY);
        if (!clickElement) {
            // method not specified to return null
            return $();
        }
        return $(BubbleManager.getTopLevelImageContainerElement(clickElement)!);
    }

    // This method is used both for creating new elements and in dragging/resizing.
    // positionInViewport: is the position to place the top-left corner of the wrapperBox
    private placeElementAtPosition(
        wrapperBox: JQuery,
        container: Element,
        positionInViewport: Point
    ) {
        const newPoint = BubbleManager.convertPointFromViewportToElementFrame(
            positionInViewport,
            container
        );
        const xOffset = newPoint.getUnscaledX();
        const yOffset = newPoint.getUnscaledY();

        // Note: This code will not clear out the rest of the style properties... they are preserved.
        //       If some or all style properties need to be removed before doing this processing, it is the caller's responsibility to do so beforehand
        //       The reason why we do this is because a bubble's onmousemove handler calls this function,
        //       and in that case we want to preserve the bubble's width/height which are set in the style
        wrapperBox.css("left", xOffset); // assumes numbers are in pixels
        wrapperBox.css("top", yOffset); // assumes numbers are in pixels

        BubbleManager.setTextboxPosition(wrapperBox, xOffset, yOffset);
    }

    // This used to be called from a right-click context menu, but now it only gets called
    // from the comicTool where we verify that we have an active element BEFORE calling this
    // method. That simplifies things here.
    public deleteTOPBox(textOverPicDiv: HTMLElement) {
        // Simple guard, just in case.
        if (!textOverPicDiv || !textOverPicDiv.parentElement) {
            return;
        }
        const containerElement = textOverPicDiv.parentElement;
        // Make sure comical is up-to-date.
        if (
            containerElement.getElementsByClassName(kComicalGeneratedClass)
                .length > 0
        ) {
            Comical.update(containerElement);
        }

        Comical.deleteBubbleFromFamily(textOverPicDiv, containerElement);

        // Update UI and make sure things get redrawn correctly.
        this.refreshBubbleEditing(containerElement, undefined, false);
    }

    // We verify that 'textElement' is the active element before calling this method.
    public duplicateTOPBox(textElement: HTMLElement) {
        // simple guard
        if (!textElement || !textElement.parentElement) {
            return;
        }
        const imageContainer = textElement.parentElement;
        // Make sure comical is up-to-date before we clone things.
        if (
            imageContainer.getElementsByClassName(kComicalGeneratedClass)
                .length > 0
        ) {
            Comical.update(imageContainer);
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
                return;
            }

            this.duplicateBubbleFamily(patriarchBubble, bubbleSpecToDuplicate);
        }
    }

    // Should duplicate all bubbles and their size and relative placement and color, etc.,
    // and the actual text in the bubbles.
    // The 'patriarchSourceBubble' is the head of a family of bubbles to duplicate,
    // although this one bubble may be all there is.
    // The content of 'patriarchSourceBubble' is now the active element.
    // The 'bubbleSpecToDuplicate' param is the bubbleSpec for the patriarch source bubble.
    // The function returns the patriarch textOverPicture element of the new
    // duplicated bubble family.
    // This method handles all needed refreshing of the duplicate bubbles.
    private duplicateBubbleFamily(
        patriarchSourceBubble: Bubble,
        bubbleSpecToDuplicate: BubbleSpec
    ): HTMLElement | undefined {
        const sourceElement = patriarchSourceBubble.content;
        const proposedOffset = 15;
        const newPoint = this.findBestLocationForNewBubble(
            sourceElement,
            proposedOffset + sourceElement.clientWidth, // try to not overlap too much
            proposedOffset
        );
        if (!newPoint) {
            return;
        }
        const patriarchDuplicateElement = this.addOverPictureElementFromOriginal(
            newPoint.getScaledX(),
            newPoint.getScaledY(),
            sourceElement,
            bubbleSpecToDuplicate.style
        );
        if (!patriarchDuplicateElement) {
            return;
        }
        patriarchDuplicateElement.style.color = sourceElement.style.color; // preserve text color
        patriarchDuplicateElement.innerHTML = this.safelyCloneHtmlStructure(
            sourceElement
        );
        this.setActiveElement(patriarchDuplicateElement);
        this.matchSizeOfSource(sourceElement, patriarchDuplicateElement);
        const container = BubbleManager.getTopLevelImageContainerElement(
            patriarchDuplicateElement
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
        // OK, now we're done with our manipulation of the patriarch bubble and we're about to go on
        // and deal with the child bubbles (if any). But we replaced the innerHTML after creating the
        // initial duplicate bubble and the editable divs may not have the appropriate events attached,
        // so we'll refresh again with 'attachEventsToEditables' set to 'true'.
        this.refreshBubbleEditing(
            container,
            new Bubble(patriarchDuplicateElement),
            true
        );
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
        return patriarchDuplicateElement;
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
                new Point(
                    spec.tipX + offSetFromSource.getUnscaledX(),
                    spec.tipY + offSetFromSource.getUnscaledY(),
                    PointScaling.Unscaled,
                    "getAdjustedTailSpec.tip"
                )
            );
            const midPoint = this.adjustRelativePointToImageContainer(
                imageContainer,
                new Point(
                    spec.midpointX + offSetFromSource.getUnscaledX(),
                    spec.midpointY + offSetFromSource.getUnscaledY(),
                    PointScaling.Unscaled,
                    "getAdjustedTailSpec.mid"
                )
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

    private matchSizeOfSource(
        sourceElement: HTMLElement,
        destElement: HTMLElement
    ) {
        destElement.style.width = sourceElement.clientWidth.toFixed(0) + "px";
        // text elements adjust their height automatically based on width and content...
        // picture over picture and video over picture don't.
        destElement.style.height = sourceElement.clientHeight.toFixed(0) + "px";
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
        this.matchSizeOfSource(sourceElement, newChildElement);
        // We just replaced the bloom-editables from the 'addChildInternal' with a clone of the source
        // bubble's HTML. This will undo any event handlers that might have been attached by the
        // refresh triggered by 'addChildInternal'. So we send the newly modified child through again,
        // with 'attachEventsToEditables' set to 'true'.
        this.refreshBubbleEditing(
            BubbleManager.getTopLevelImageContainerElement(parentElement)!,
            new Bubble(newChildElement),
            true
        );
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
        // Picture over picture elements need the tabindex (="0") in order to be focusable.
        // But for text-based bubbles we need to delete positive tabindex, so we don't do weird
        // things to talking book playback order when we duplicate a family of bubbles.
        this.removePositiveTabindex(element);
        this.safelyRemoveAttribute(element, "data-duration");
        this.safelyRemoveAttribute(element, "data-audiorecordingendtimes");

        // Clean children
        const childArray = Array.from(element.childNodes);
        childArray.forEach(element => {
            this.cleanClonedNode(element as Element);
        });
    }

    private removePositiveTabindex(element: Element) {
        if (!element.hasAttribute("tabindex")) {
            return;
        }
        const indexStr = element.getAttribute("tabindex");
        if (!indexStr) {
            return;
        }
        const indexValue = parseInt(indexStr, 10);
        if (indexValue > 0) {
            element.attributes.removeNamedItem("tabindex");
        }
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

    private makeOverPictureElementsDraggableAndClickable(
        thisOverPictureElements: JQuery
    ): void {
        thisOverPictureElements.each((index, element) => {
            const thisOverPictureElement = element as HTMLElement;
            const imageContainer = BubbleManager.getTopLevelImageContainerElement(
                thisOverPictureElement
            )!;
            const containerPos = imageContainer.getBoundingClientRect();
            const wrapperBoxRectangle = thisOverPictureElement.getBoundingClientRect();

            // Add the dragHandle (if needed). It is above the canvas to make it able to get mouse events.
            const dragHandles = thisOverPictureElement.getElementsByClassName(
                "bloom-dragHandle"
            );
            if (dragHandles.length == 0) {
                // Not added yet. Let's create it
                const imgElement = document.createElement("img");
                imgElement.className = "bloom-ui bloom-dragHandle";
                imgElement.src = "/bloom/bookEdit/img/dragHandle.svg";
                thisOverPictureElement.append(imgElement);
            }

            // Containment, drag and stop work when scaled (zoomed) as long as the page has been saved since the zoom
            // factor was last changed. Therefore we force reconstructing the page
            // in the EditingView.Zoom setter (in C#).
            $(thisOverPictureElement).draggable({
                // Adjust containment by scaling
                containment: [
                    // arguably we could add this.minBubbleVisible ti the first two, but in practice,
                    // since the handle is in the top left, it can't be used to drag it even that close.
                    containerPos.left,
                    containerPos.top,
                    containerPos.left +
                        containerPos.width -
                        this.minBubbleVisible,
                    containerPos.top +
                        containerPos.height -
                        this.minBubbleVisible
                ],
                revertDuration: 0,
                handle: ".bloom-dragHandle",
                drag: (event, ui) => {
                    ui.helper.children(".bloom-editable").blur();
                    this.focusFirstVisibleFocusable(thisOverPictureElement);
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
                        imageContainer
                    );
                    console.assert(
                        scroll.length() <= 0.0001,
                        `Scroll expected to be [0, 0] but was [${scroll.getScaledX()}, ${scroll.getScaledY()}].`
                    );

                    // Adjusts the positioning for scale.
                    // (Doesn't accurately adjust for the amount of scroll)
                    ui.position.left = position.getUnscaledX();
                    ui.position.top = position.getUnscaledY();

                    const handles = thisOverPictureElement.getElementsByClassName(
                        "bloom-dragHandle"
                    );
                    Array.from(handles).forEach(element => {
                        (element as HTMLElement).classList.add("grabbing");
                    });
                },
                stop: event => {
                    const target = event.target as Element;
                    if (target) {
                        BubbleManager.convertTextboxPositionToAbsolute(target);
                    }

                    const handles = thisOverPictureElement.getElementsByClassName(
                        "bloom-dragHandle"
                    );
                    Array.from(handles).forEach(element => {
                        (element as HTMLElement).classList.remove("grabbing");
                    });
                }
            });
        });
    }

    // Notes that comic editing either has not been suspended...isComicEditingOn might be true or false...
    // or that it was suspended because of a drag in progress that might affect page layout
    // (current example: mouse is down over an origami splitter), or because some longer running
    // process that affects layout is happening (current example: origami layout tool is active).
    // When in one of the latter states, it may be inferred that isComicEditingOn was true when
    // suspendComicEditing was called, that it is now false, and that resumeComicEditing should
    // turn it on again.
    private comicEditingSuspendedState: "none" | "forDrag" | "forTool" = "none";

    public suspendComicEditing(forWhat: "forDrag" | "forTool") {
        if (!this.isComicEditingOn) {
            // Note that this prevents us from getting into one of the suspended states
            // unless it was on to begin with. Therefore a subsequent resume won't turn
            // it back on unless it was to start with.
            return;
        }
        this.turnOffBubbleEditing();
        // We don't want to switch to state 'forDrag' while it is suspended by a tool.
        // But we don't need to prevent it because if it's suspended by a tool (e.g., origami layout),
        // any mouse events will find that comic editing is off and won't get this far.
        this.comicEditingSuspendedState = forWhat;
    }

    public resumeComicEditing() {
        if (this.comicEditingSuspendedState === "none") {
            // This guards against both mouse up events that are nothing to do with
            // splitters and (if this is even possible) a resume that matches a suspend
            // call when comic editing wasn't on to begin with.
            return;
        }
        if (this.comicEditingSuspendedState === "forTool") {
            // after a forTool suspense, we might have new dividers to put handlers on.
            this.setupSplitterEventHandling();
        }
        this.comicEditingSuspendedState = "none";
        this.turnOnBubbleEditing();
    }

    // mouse down in an origami slider: if comic editing is on, remember that, and turn it off.
    private dividerMouseDown = (ev: Event) => {
        // We could plausibly ignore it if already suspended for a tool.
        // But the call won't do anything anyway when we're already suspended.
        this.suspendComicEditing("forDrag");
    };

    // on ANY mouse up, if comic editing was turned off by an origami click, turn it back on.
    // (This is attached to the document because I don't want it missed if the mouseUp
    // doesn't happen inside the slider.)
    private documentMouseUp = (ev: Event) => {
        // ignore mouseup events while suspended for a tool.
        if (this.comicEditingSuspendedState === "forTool") {
            return;
        }
        this.resumeComicEditing();
    };

    public initializeOverPictureEditing(): void {
        // Cleanup old .bloom-ui elements and old drag handles etc.
        // We want to clean these up sooner rather than later so that there's less chance of accidentally blowing away
        // a UI element that we'll actually need now
        // (e.g. the ui-resizable-handles or the format gear, which both have .bloom-ui applied to them)
        this.cleanupOverPictureElements();

        this.makeOverPictureElementsDraggableClickableAndResizable();

        this.setupSplitterEventHandling();

        this.turnOnBubbleEditing();
    }

    // When dragging origami sliders, turn comical off.
    // With this, we get some weirdness during dragging: overlay text moves, but
    // the bubbles do not. But everything clears up when we turn it back on afterwards.
    // Without it, things are even weirder, and the end result may be weird, too.
    // The comical canvas does not change size as the slider moves, and things may end
    // up in strange states with bubbles cut off where the boundary used to be.
    // It's possible that we could do better by forcing the canvas to stay the same
    // size as the image container, but I'm very unsure how resizing an active canvas
    // containing objects will affect ComicalJs and the underlying PaperJs.
    // It should be pretty rare to resize an image after adding bubbles, so I think it's
    // better to go with this, which at least gives a predictable result.
    // Note: we don't ever need to remove these; they can usefully hang around until
    // we load some other page. (We don't turn off comical when we hide the tool, since
    // the bubbles are still visible and editable, and we need it's help to support
    // all the relevant behaviors and keep the bubbles in sync with the text.)
    // Because we're adding a fixed method, not a local function, adding multiple
    // times will not cause duplication.
    private setupSplitterEventHandling() {
        Array.from(
            document.getElementsByClassName("split-pane-divider")
        ).forEach(d => d.addEventListener("mousedown", this.dividerMouseDown));
        document.addEventListener("mouseup", this.documentMouseUp);
    }

    public cleanupOverPictureElements() {
        const allOverPictureElements = $("body").find(kTextOverPictureSelector);
        allOverPictureElements.each((index, element) => {
            const thisOverPictureElement = $(element);

            thisOverPictureElement.find(".bloom-ui").remove(); // Just in case somehow one is stuck in there
            thisOverPictureElement.find(".bloom-dragHandleTOP").remove(); // BL-7903 remove any left over drag handles (this was the class used in 4.7 alpha)
        });
    }

    // Make any added BubbleManager textboxes draggable, clickable, and resizable.
    // Called by bloomEditing.ts.
    public makeOverPictureElementsDraggableClickableAndResizable() {
        // get all textOverPicture elements
        const textOverPictureElems = $("body").find(kTextOverPictureSelector);
        if (textOverPictureElems.length === 0) {
            return; // if there aren't any, quit before we hurt ourselves!
        }
        const scale = EditableDivUtils.getPageScale();

        textOverPictureElems.resizable({
            handles: "all",
            // ENHANCE: Maybe we should add a containment option here?
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

                    BubbleManager.convertTextboxPositionToAbsolute(target);

                    // There was a problem where resizing a box messed up its draggable containment,
                    // so now after we resize we go back through making it draggable and clickable again.
                    this.makeOverPictureElementsDraggableAndClickable(
                        $(target)
                    );

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

                if (handle instanceof HTMLElement) {
                    // Overwrite the default zIndex of 90 provided in the inline styles of modified_libraries\jquery-ui\jquery-ui-1.10.3.custom.min.js
                    handle.style.zIndex = "1003"; // Should equal @resizeHandleEventZIndex
                }

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

        this.makeOverPictureElementsDraggableAndClickable(textOverPictureElems);
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

    // An event handler that adds the "ui-jquery-resizing-in-progress" class to the image container.
    private static addResizingClassHandler(event: MouseEvent) {
        const handle = event.currentTarget as Element;

        const container = BubbleManager.getTopLevelImageContainerElement(
            handle
        );
        if (container) {
            container.classList.add("ui-jquery-resizing-in-progress");
        }
    }

    // An event handler that removes the "ui-jquery-resizing-in-progress" class from the image container.
    private static clearResizingClassHandler(event: MouseEvent) {
        BubbleManager.clearResizingClass(event.currentTarget as Element);
    }

    private static clearResizingClass(element: Element) {
        const container = BubbleManager.getTopLevelImageContainerElement(
            element
        );
        if (container) {
            container.classList.remove("ui-jquery-resizing-in-progress");
        }
    }

    // Converts a text box's position to absolute in pixels (using CSS styling)
    // (Used to be a percentage of parent size. See comments on setTextboxPosition.)
    // textBox: The thing we want to position
    // container: Optional. The image container the text box is in. If this parameter is not defined, the function will automatically determine it.
    private static convertTextboxPositionToAbsolute(
        textBox: Element,
        container?: Element | null | undefined
    ): void {
        let unscaledRelativeLeft: number;
        let unscaledRelativeTop: number;

        if (!container) {
            container = BubbleManager.getTopLevelImageContainerElement(textBox);
        }

        if (container) {
            const positionInfo = textBox.getBoundingClientRect();
            const wrapperBoxPos = new Point(
                positionInfo.left,
                positionInfo.top,
                PointScaling.Scaled,
                "convertTextboxPositionToAbsolute()"
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
                "convertTextboxPositionToAbsolute(): container was null or undefined."
            );

            // If can't find the container for some reason, fallback to the old, deprecated calculation.
            // (This algorithm does not properly account for the border of the imageContainer when zoomed,
            //  so the results may be slightly off by perhaps up to 2 pixels)
            const scale = EditableDivUtils.getPageScale();
            const pos = $(textBox).position();
            unscaledRelativeLeft = pos.left / scale;
            unscaledRelativeTop = pos.top / scale;
        }

        this.setTextboxPosition(
            $(textBox),
            unscaledRelativeLeft,
            unscaledRelativeTop
        );
    }

    // Sets a text box's position permanently to where it is now.
    // (Not sure if this ever changes anything, except when migrating. Earlier versions of Bloom
    // stored the bubble position and size as a percentage of the image container size.
    // The reasons for that are lost in history; probably we thought that it would better
    // preserve the user's intent to keep in the same shape and position.
    // But in practice it didn't work well, especially since everything was relative to the
    // image container, and the image moves around in that as determined by content:fit etc
    // to keep its aspect ratio. The reasons to prefer an absolute position and
    // size are in BL-11667. Basically, we don't want the overlay to change its size or position
    // relative to its own tail when the image is resized, either because the page size changed
    // or because of dragging a splitter. It would usually be even better if everything kept
    // its position relative to the image itself, but that is much harder to do since the overlay
    // isn't (can't be) a child of the img.)
    private static setTextboxPosition(
        textBox: JQuery,
        unscaledRelativeLeft: number,
        unscaledRelativeTop: number
    ) {
        textBox
            .css("left", unscaledRelativeLeft + "px")
            .css("top", unscaledRelativeTop + "px")
            // FYI: The textBox width/height is rounded to the nearest whole pixel. Ideally we might like its more precise value...
            // But it's a huge performance hit to get its getBoundingClientRect()
            // It seems that getBoundingClientRect() may be internally cached under the hood,
            // since getting the bounding rect of the image container once per mousemove event or even 100x per mousemove event caused no ill effect,
            // but getting this one is quite taxing on the CPU
            .css("width", textBox.width() + "px")
            .css("height", textBox.height() + "px");
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

    // Lots of places we need to find the bloom-imageContainer that a particular element resides in.
    // Method is static because several of the callers are static.
    // BL-9976: now that we can have images on top of images, we need to ensure that we don't return
    // a "sub-imageContainer". But we do want to return null if we aren't in an imageContainer at all.
    private static getTopLevelImageContainerElement(
        element: Element
    ): HTMLElement | null {
        if (!element?.closest) {
            // It's possible for the target to be the root document object. If so, it doesn't
            // have a 'closest' function, so we'd better not try to call it.
            // It's also certainly not inside an image container, so null is a safe result.
            return null;
        }
        const firstTry = element.closest(kImageContainerSelector);
        if (!firstTry) {
            return null; // 'element' is not in an imageContainer at all
        }
        const secondTry = firstTry.parentElement!.closest(
            kImageContainerSelector
        );
        return secondTry
            ? (secondTry as HTMLElement) // element was inside of a image over image
            : (firstTry as HTMLElement); // element was just inside of a top level imageContainer
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

// For use by bloomImages.ts, so that newly opened books get this class updated for their images.
export function updateOverlayClass(imageContainer: HTMLElement) {
    if (
        imageContainer.getElementsByClassName(kTextOverPictureClass).length > 0
    ) {
        imageContainer.classList.add(kOverlayClass);
    } else {
        imageContainer.classList.remove(kOverlayClass);
    }
}

export let theOneBubbleManager: BubbleManager;

export function initializeBubbleManager() {
    if (theOneBubbleManager) return;
    theOneBubbleManager = new BubbleManager();
    theOneBubbleManager.initializeBubbleManager();
}

// This is a definition of the object we store as JSON in data-bubble-alternate.
// Tails has further structure but BubbleManager doesn't care about it.
interface IAlternate {
    style: string; // What to put in the style attr of the overlay; determines size and position
    tails: object[]; // The tails of the data-bubble; determines placing of tail.
}
