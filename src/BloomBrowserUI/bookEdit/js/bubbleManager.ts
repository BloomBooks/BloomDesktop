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
import { adjustTarget } from "../toolbox/dragActivity/dragActivityTool";
import BloomSourceBubbles from "../sourceBubbles/BloomSourceBubbles";
import BloomHintBubbles from "./BloomHintBubbles";
import { renderOverlayContextControls } from "./OverlayContextControls";
import { data } from "jquery";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { MeasureText } from "../../utils/measureText";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

export interface ITextColorInfo {
    color: string;
    isDefault: boolean;
}

const kComicalGeneratedClass: string = "comical-generated";
// We could rename this class to "bloom-overPictureElement", but that would involve a migration.
// For now we're keeping this name for backwards-compatibility, even though now the element could be
// a video or even another picture.
export const kTextOverPictureClass = "bloom-textOverPicture";
export const kTextOverPictureSelector = `.${kTextOverPictureClass}`;
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
    private thingsToNotifyOfBubbleChange: {
        // identifies the source that requested the notification; allows us to remove the
        // right one when no longer needed, and prevent multiple notifiers to the same client.
        id: string;
        handler: (x: BubbleSpec | undefined) => void;
    }[] = [];

    // These variables are used by the bubble's onmouse* event handlers
    private bubbleToDrag: Bubble | undefined; // Use Undefined to indicate that there is no active drag in progress
    private bubbleDragGrabOffset: { x: number; y: number } = { x: 0, y: 0 };
    private activeContainer: HTMLElement | undefined;

    public initializeBubbleManager(): void {
        // Currently nothing to do; used to set up web socket listener
        // for right-click messages to add and delete OverPicture elements.
        // Keeping hook in case we want it one day...
    }

    public getIsComicEditingOn(): boolean {
        return this.isComicEditingOn;
    }

    // Given the box has been determined to be overflowing vertically by
    // 'overflowY' pixels, if it's inside an OverPicture element that does not have the class
    // bloom-noAutoSize, adjust the size of the OverPicture element
    // to fit it.
    // (Caller may wish to do box.scrollTop = 0 to make sure the whole content shows now there
    // is room for it all.)
    // Returns true if successful; it will currently fail if box is not
    // inside a valid OverPicture element or if the OverPicture element can't grow this much while
    // remaining inside the image container. If it returns false, it makes no changes at all.
    public growOverflowingBox(box: HTMLElement, overflowY: number): boolean {
        const wrapperBox = box.closest(kTextOverPictureSelector) as HTMLElement;
        if (
            !wrapperBox ||
            wrapperBox.classList.contains("bloom-noAutoHeight")
        ) {
            return false; // we can't fix it
        }

        const container = BubbleManager.getTopLevelImageContainerElement(
            wrapperBox
        );
        if (!container) {
            return false; // paranoia; OverPicture element should always be in image container
        }

        // The +4 is based on experiment. It may relate to a couple of 'fudge factors'
        // in OverflowChecker.getSelfOverflowAmounts, which I don't want to mess with
        // as a lot of work went into getting overflow reporting right. We seem to
        // need a bit of extra space to make sure the last line of text fits.
        // The 27 is the minimumSize that CSS imposes on OverPicture elements; it may cause
        // Comical some problems if we try to set the actual size smaller.
        // (I think I saw background gradients behaving strangely, for example.)
        let newHeight = Math.max(box.clientHeight + overflowY + 4, 27);
        if (
            newHeight < wrapperBox.clientHeight &&
            newHeight > wrapperBox.clientHeight - 4
        ) {
            return false; // near enough, avoid jitter making it a tiny bit smaller.
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
        this.adjustTarget(wrapperBox);
        this.alignControlFrameWithActiveElement();
        return true;
    }

    public updateAutoHeight(): void {
        if (
            this.activeElement &&
            !this.activeElement.classList.contains("bloom-noAutoHeight")
        ) {
            const editable = this.activeElement.getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )[0] as HTMLElement;

            this.adjustBubbleHeightToContentOrMarkOverflow(editable);
        }
        this.alignControlFrameWithActiveElement();
    }
    public adjustBubbleHeightToContentOrMarkOverflow(
        editable: HTMLElement
    ): void {
        if (!this.activeElement) return;
        const overflowAmounts = OverflowChecker.getSelfOverflowAmounts(
            editable
        );
        let overflowY =
            overflowAmounts[1] +
            editable.offsetHeight -
            this.activeElement.offsetHeight;

        // This mimics the relevant part of OverflowChecker.MarkOverflowInternal
        if (
            theOneBubbleManager.growOverflowingBox(
                this.activeElement,
                overflowY
            )
        ) {
            overflowY = 0;
        }
        editable.classList.toggle("overflow", overflowY > 0);
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
    public static pxToNumber(px: string): number {
        if (!px) return 0;
        return parseInt(px.replace("px", ""));
    }

    // We usually don't show the image editing buttons on an overlay page.
    // (If the user clicks on the background, we show them.)
    // An earlier version did not hide them if there isn't a background image (just a placeholder).
    // But it's increasingly common to deliberately leave the background blank.
    public static hideImageButtonsIfHasOverlays(container: HTMLElement) {
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
        DisableImageEditing(container);
    }

    public turnOnHidingImageButtons() {
        const imageContainers: HTMLElement[] = Array.from(
            this.getAllPrimaryImageContainersOnPage() as any
        );
        imageContainers.forEach(container => {
            BubbleManager.hideImageButtonsIfHasOverlays(container);
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

    // A visible, editable div is generally focusable, but sometimes (e.g. in Bloom games),
    // we may disable it by turning off pointer events. So we filter those ones out.
    private getAllVisibleFocusableDivs(
        overPictureContainerElement: HTMLElement
    ): Element[] {
        return this.getAllVisibileEditableDivs(
            overPictureContainerElement
        ).filter(
            focusElement =>
                window.getComputedStyle(focusElement).pointerEvents !== "none"
        );
    }

    private getAllVisibileEditableDivs(
        overPictureContainerElement: HTMLElement
    ): Element[] {
        // If the Over Picture element has visible bloom-editables, we want them.
        // Otherwise, look for video and image elements. At this point, an over picture element
        // can only have one of three types of content and each are mutually exclusive.
        // bloom-editable or bloom-videoContainer or bloom-imageContainer. It doesn't even really
        // matter which order we look for them.
        const editables = Array.from(
            overPictureContainerElement.getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )
        );
        let focusableDivs = editables
            // At least in Bloom games, some elements with visibility code on are nevertheless hidden
            .filter(e => !EditableDivUtils.isInHiddenLanguageBlock(e));
        focusableDivs = focusableDivs.filter(
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
            ).filter(x => !EditableDivUtils.isInHiddenLanguageBlock(x));
        }
        if (focusableDivs.length === 0) {
            // This could be a bit tricky, since the whole canvas is in a 'bloom-imageContainer'.
            // But 'overPictureContainerElement' here is a div.bloom-textOverPicture element,
            // so if we find any imageContainers inside of that, they are picture over picture elements.
            focusableDivs = Array.from(
                overPictureContainerElement.getElementsByClassName(
                    kImageContainerClass
                )
            ).filter(x => !EditableDivUtils.isInHiddenLanguageBlock(x));
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
            var focusElement = focusElements[0] as HTMLElement;
            focusElement.focus();
            return true;
        }
        return false;
    }

    public turnOnBubbleEditing(): void {
        if (this.isComicEditingOn === true) {
            return; // Already on. No work needs to be done
        }
        this.isComicEditingOn = true;

        Comical.setActiveBubbleListener(activeElement => {
            // No longer want to focus a bubble when activated.
            // if (activeElement) {
            //     this.focusFirstVisibleFocusable(activeElement);
            // }
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
        const overPictureElements = Array.from(
            document.getElementsByClassName(kTextOverPictureClass)
        ).filter(
            x => !EditableDivUtils.isInHiddenLanguageBlock(x)
        ) as HTMLElement[];
        if (overPictureElements.length > 0) {
            // If we have no activeElement, or it's not in the list...deleted or belongs to
            // another page, perhaps...pick an arbitrary one.
            if (
                !this.activeElement ||
                overPictureElements.indexOf(this.activeElement) === -1
            ) {
                this.activeElement = overPictureElements[
                    overPictureElements.length - 1
                ] as HTMLElement;
            }
            // This focus call doesn't seem to work, at least in a lasting fashion.
            // See the code in bloomEditing.ts/SetupElements() that sets focus after
            // calling BloomSourceBubbles.MakeSourceBubblesIntoQtips() in a delayed loop.
            // That code usually finds that nothing is focused.
            // (gjm: I reworked the code that finds a visible element a bit,
            // it's possible the above comment is no longer accurate)
            //this.focusFirstVisibleFocusable(this.activeElement);
            Comical.setUserInterfaceProperties({ tailHandleColor: kBloomBlue });
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
    // declare this strange way so it has the right 'this' when added as event listener.
    private bubbleLosingFocus = event => {
        if (BubbleManager.ignoreFocusChanges) return;
        // removing focus from a text bubble means the next click on it could drag it.
        // However, it's possible the active bubble already moved; don't clear theBubbleWeAreTextEditing if so
        if (event.currentTarget === this.theBubbleWeAreTextEditing) {
            this.theBubbleWeAreTextEditing = undefined;
            this.removeFocusClass();
        }
    };

    // This is not a great place to make this available to the world.
    // But GetSettings only works in the page Iframe, and the bubble manager
    // is one componenent from there that the Game code already works with
    // and that already uses the injected GetSettings(). I don't have a better idea,
    // short of refactoring so that we get settings from an API call rather than
    // by injection. But that may involve making a lot of stuff async.
    public getSettings(): ICollectionSettings {
        return GetSettings();
    }

    // This is invoked when the toolbox adds a bubble that wants source and/or hint bubbles.
    public addSourceAndHintBubbles(translationGroup: HTMLElement) {
        const bubble = BloomSourceBubbles.ProduceSourceBubbles(
            translationGroup
        );
        const divsThatHaveSourceBubbles: HTMLElement[] = [];
        const bubbleDivs: any[] = [];
        if (bubble.length !== 0) {
            divsThatHaveSourceBubbles.push(translationGroup);
            bubbleDivs.push(bubble);
        }
        BloomHintBubbles.addHintBubbles(
            translationGroup.parentElement!,
            divsThatHaveSourceBubbles,
            bubbleDivs
        );
        if (divsThatHaveSourceBubbles.length > 0) {
            BloomSourceBubbles.MakeSourceBubblesIntoQtips(
                divsThatHaveSourceBubbles[0],
                bubbleDivs[0]
            );
            BloomSourceBubbles.setupSizeChangedHandling(
                divsThatHaveSourceBubbles
            );
        }
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
        // Arguably, we only need to do this to ones that can be focused. But the sort of disabling
        // that causes editables not to be focusable comes and goes, so rather than have to keep
        // calling this, we'll just set them all up with focus handlers and CkEditor.
        const editables = this.getAllVisibileEditableDivs(container);
        editables.forEach(element => {
            // Don't use an arrow function as an event handler here.
            //These can never be identified as duplicate event listeners, so we'll end up with tons
            // of duplicates.
            element.addEventListener("focusin", this.handleFocusInEvent);
            if (
                includeCkEditor &&
                element.classList.contains("bloom-editable")
            ) {
                attachToCkEditor(element);
            }
        });
        Array.from(
            document.getElementsByClassName(kTextOverPictureClass)
        ).forEach((element: HTMLElement) => {
            element.addEventListener("focusout", this.bubbleLosingFocus);
        });
    }

    private handleFocusInEvent(ev: Event) {
        BubbleManager.onFocusSetActiveElement(ev);
    }

    // This should not return any .bloom-imageContainers that have imageContainer ancestors.
    public getAllPrimaryImageContainersOnPage() {
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
        attachEventsToEditables: boolean,
        activateBubble: boolean
    ): void {
        Comical.startEditing([imageContainer]);
        // necessary if we added the very first bubble, and Comical was not previously initialized
        Comical.setUserInterfaceProperties({ tailHandleColor: kBloomBlue });
        if (bubble) {
            const newTextOverPictureElement = bubble.content;
            if (activateBubble) {
                Comical.activateBubble(bubble);
            }
            this.updateComicalForSelectedElement(newTextOverPictureElement);

            // SetupElements (below) will do most of what we need, but when it gets to
            // 'turnOnBubbleEditing()', it's already on, so the method will get skipped.
            // The only piece left from that method that still needs doing is to set the
            // 'focusin' eventlistener.
            // And then the only thing left from a full refresh that needs to happen here is
            // to attach the new bloom-editable to ckEditor.
            // If attachEventsToEditables is false, then this is a child or duplicate bubble that
            // was already sent through here once. We don't need to add more 'focusin' listeners and
            // re-attach to the StyleEditor again.
            // This must be done before we call SetupElements, which will attempt to focus the new
            // bubble, and expects the focus event handler to get called.
            if (attachEventsToEditables) {
                this.addEventsToFocusableElements(
                    newTextOverPictureElement,
                    attachEventsToEditables
                );
            }
            SetupElements(
                imageContainer,
                activateBubble ? bubble.content : undefined
            );

            // Since we may have just added an element, check if the container has at least one
            // overlay element and add the 'hasOverlay' class.
            updateOverlayClass(imageContainer);
        } else {
            // deleted a bubble. Don't try to focus anything.
            this.removeControlFrame(); // but don't leave this behind.

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
        if (BubbleManager.ignoreFocusChanges) return;
        if (BubbleManager.inPlayMode(event.currentTarget as Element)) {
            return;
        }
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
            // When a bubble is first clicked, we try hard not to let it get focus.
            // Another click will focus it. Unfortunately, various other things do as well,
            // such as activating Bloom (which seems to focus the thing that most recently had
            // a text selection, possibly because of CkEditor), and Undo. If something
            // has focused the bubble, it will typically have a selection visible, and so it
            // looks as if it's in edit mode. I think it's best to just make it so.)
            theOneBubbleManager.theBubbleWeAreTextEditing =
                theOneBubbleManager.activeElement;
            theOneBubbleManager.theBubbleWeAreTextEditing?.classList.add(
                "bloom-focusedTOP"
            );
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
        if (clickedElement.classList.contains("MuiBackdrop-root")) {
            return; // we clicked outside a popup menu to close it. Don't mess with focus.
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
        if (
            clickedElement.closest("#overlay-control-frame") ||
            clickedElement.closest("#overlay-context-controls") ||
            clickedElement.closest(".MuiMenu-list") ||
            clickedElement.closest(".above-page-control-container") ||
            clickedElement.closest(".MuiDialog-container")
        ) {
            // clicking things in here (such as menu item in the pull-down, or a prompt dialog) should not
            // clear the active element
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

    // In drag-word-chooser-slider game, there are image TOP boxes with data-img-txt attributes
    // linking them to corresponding text boxes with data-txt-img attributes. Only one
    // of these text boxes is shown at a time, controlled by giving it the class
    // bloom-activeTextBox. If the argument passed is one of the image boxes,
    // this method will show the corresponding text box, by adding bloom-activeTextBox
    // to the appropriate one and removing it from all others.
    // There are also 'wrong' pictures that don't have a corresponding text box.
    // If one of these is selected, it gets the class bloom-activePicture.
    private showCorrespondingTextBox(element: HTMLElement | undefined) {
        //Slider: if (!element) {
        //     return;
        // }
        // const linkId = element.getAttribute("data-img-txt");
        // if (!linkId) {
        //     return; // arguent is not a picture with a link to a text box
        // }
        // const textBox = element.ownerDocument.querySelector(
        //     "[data-txt-img='" + linkId + "']"
        // );
        // const allTextBoxes = Array.from(
        //     element.ownerDocument.getElementsByClassName("bloom-wordChoice")
        // );
        // allTextBoxes.forEach(box => {
        //     if (box !== textBox) {
        //         box.classList.remove("bloom-activeTextBox");
        //     }
        // });
        // Array.from(
        //     element.ownerDocument.getElementsByClassName("bloom-activePicture")
        // ).forEach(box => {
        //     box.classList.remove("bloom-activePicture");
        // });
        // // Note that if this is a 'wrong' picture, there may be no corresponding text box.
        // // (In that case we still want to hide the other picture-specific ones.)
        // if (textBox) {
        //     textBox.classList.add("bloom-activeTextBox");
        // } else {
        //     element.classList.add("bloom-activePicture");
        // }
    }

    public removeFocusClass() {
        Array.from(document.getElementsByClassName("bloom-focusedTOP")).forEach(
            element => {
                element.classList.remove("bloom-focusedTOP");
            }
        );
    }

    // Some controls, such as MUI menus, temporarily steal focus. We don't want the usual
    // loss-of-focus behavior, so this allows suppressing it.
    public static ignoreFocusChanges: boolean;

    public setActiveElement(element: HTMLElement | undefined) {
        if (this.activeElement !== element && this.activeElement) {
            tryRemoveImageEditingButtons(
                this.activeElement.getElementsByClassName(
                    "bloom-imageContainer"
                )[0] as Element | undefined
            );
            this.activeElement.removeAttribute("data-bloom-active");
        }
        if (this.activeElement !== element) {
            this.theBubbleWeAreTextEditing = undefined; // even if focus doesn't move.
            // For some reason this doesnt' trigger as a result of changing the selection.
            // But we definitely don't want to show the CkEditor toolbar until there is some
            // new range selection, so just set up the usual class to hide it.
            document.body.classList.add("hideAllCKEditors");
            window.getSelection()?.removeAllRanges();
            this.removeFocusClass();
        }
        // Some of this could probably be avoided if this.activeElement is not changing.
        // But there are cases in page initialization where this.activeElement
        // gets set without calling this method, then it gets called again.
        // It's safest if we just do it all every time.
        this.activeElement = element;
        this.activeElement?.setAttribute("data-bloom-active", "true");
        this.doNotifyChange();
        Comical.activateElement(this.activeElement);
        this.adjustTarget(this.activeElement);
        this.showCorrespondingTextBox(this.activeElement);
        this.setupControlFrame();
        if (this.activeElement) {
            // Restore hiding these when we activate a bubble, so they don't get in the way of working on
            // that bubble.
            theOneBubbleManager.turnOnHidingImageButtons();
        }
    }

    // clientX/Y of the mouseDown event in one of the resize handles.
    // Comparing with the position during mouseMove tells us how much to resize.
    private startResizeDragX: number;
    private startResizeDragY: number;
    // the original size and postion (at mouseDown) during a resize or crop
    private oldWidth: number;
    private oldHeight: number;
    private oldLeft: number;
    private oldTop: number;
    // The original size and position of the main img inside an overlay being resized or cropped
    private oldImageWidth: number;
    private oldImageLeft: number;
    private oldImageTop: number;
    // during a resize drag, keeps track of which corner we're dragging
    private resizeDragCorner: "ne" | "nw" | "se" | "sw" | undefined;

    // Keeps track of whether the mouse was moved during a mouse event in the main content of a
    // bubble. If so, we interpret it as a drag, moving the bubble. If not, we interpret it as a click.
    private gotAMoveWhileMouseDown: boolean = false;

    // Remove the overlay control frame if it exists (when no overlay is active)
    // Also remove the menu if it's still open.  See BL-13852.
    removeControlFrame() {
        // this.activeElement is still set and works for hiding the menu.
        const eltWithControlOnIt = this.activeElement;
        const controlFrame = document.getElementById("overlay-control-frame");
        if (controlFrame) {
            if (eltWithControlOnIt) {
                // we're going to remove the container of the overlay context controls,
                // but it seems best to let React clean up after itself.
                // For example, there may be a context menu popup to remove, too.
                renderOverlayContextControls(eltWithControlOnIt, false);
            }
            // Reschedule so that the rerender can finish before removing the control frame.
            setTimeout(() => {
                controlFrame.remove();
                document.getElementById("overlay-context-controls")?.remove();
            }, 0);
        }
    }

    // Set up the control frame for the active overlay. This includes creating it if it
    // doesn't exist, and positioning it correctly.
    setupControlFrame() {
        const eltToPutControlsOn = this.activeElement;
        let controlFrame = document.getElementById("overlay-control-frame");
        if (!eltToPutControlsOn) {
            this.removeControlFrame();
            return;
        }
        // if the overlay is not the right shape for a contained image, fix it now.
        this.matchContainerToImage(eltToPutControlsOn);

        if (!controlFrame) {
            controlFrame = eltToPutControlsOn.ownerDocument.createElement(
                "div"
            );
            controlFrame.setAttribute("id", "overlay-control-frame");
            controlFrame.classList.add("bloom-ui"); // makes sure it gets cleaned up.
            eltToPutControlsOn.parentElement?.appendChild(controlFrame);
            const corners = ["ne", "nw", "se", "sw"];
            corners.forEach(corner => {
                const control = eltToPutControlsOn.ownerDocument.createElement(
                    "div"
                );
                control.classList.add("bloom-ui-overlay-resize-handle");
                control.classList.add(
                    "bloom-ui-overlay-resize-handle-" + corner
                );
                controlFrame?.appendChild(control);
                control.addEventListener("mousedown", event => {
                    this.startResizeDrag(
                        event,
                        corner as "ne" | "nw" | "se" | "sw"
                    );
                });
            });
            // "sides means not just left and right, but all four sides of the control frame"
            const sides = ["n", "s", "e", "w"];
            sides.forEach(side => {
                const sideControl = eltToPutControlsOn.ownerDocument.createElement(
                    "div"
                );
                sideControl.classList.add("bloom-ui-overlay-side-handle");
                sideControl.classList.add(
                    "bloom-ui-overlay-side-handle-" + side
                );
                controlFrame?.appendChild(sideControl);
                sideControl.addEventListener("mousedown", event => {
                    if (event.buttons !== 1 || !this.activeElement) {
                        return;
                    }
                    this.startSideControlDrag(event, side);
                });
            });
            const sideHandle = eltToPutControlsOn.ownerDocument.createElement(
                "div"
            );
            sideHandle.classList.add("bloom-ui-overlay-move-crop-handle");
            controlFrame?.appendChild(sideHandle);
            sideHandle.addEventListener("mousedown", event => {
                if (event.buttons !== 1 || !this.activeElement) {
                    return;
                }
                this.startMoveCrop(event);
            });
            const toolboxRoot = eltToPutControlsOn.ownerDocument.createElement(
                "div"
            );
            toolboxRoot.setAttribute("id", "overlay-context-controls");
            // We don't have to worry about removing this before saving because it is above the level
            // of the bloom-page.
            document.body.appendChild(toolboxRoot);
        }
        const hasImage =
            eltToPutControlsOn?.getElementsByClassName("bloom-imageContainer")
                ?.length > 0;
        if (hasImage) {
            controlFrame.classList.add("has-image");
        } else {
            controlFrame.classList.remove("has-image");
        }
        const hasText =
            eltToPutControlsOn?.getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            ).length > 0;
        if (hasText) {
            controlFrame.classList.add("has-text");
        } else {
            controlFrame.classList.remove("has-text");
        }
        // to reduce flicker we don't show this when switching to a different bubble until we determine
        // that it is wanted.
        controlFrame.classList.remove("bloom-ui-overlay-show-move-crop-handle");
        this.alignControlFrameWithActiveElement();
        renderOverlayContextControls(eltToPutControlsOn, false);
    }

    private startMoveCropX: number;
    private startMoveCropY: number;
    private startMoveCropControlX: number;
    private startMoveCropControlY: number;
    startMoveCrop = (event: MouseEvent) => {
        event.preventDefault();
        event.stopPropagation();
        if (!this.activeElement) return;
        this.currentDragControl = event.currentTarget as HTMLElement;
        this.currentDragControl.classList.add("active");
        this.startMoveCropX = event.clientX;
        this.startMoveCropY = event.clientY;
        const imgC = this.activeElement.getElementsByClassName(
            "bloom-imageContainer"
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (!img) return;
        this.oldImageTop = BubbleManager.pxToNumber(img.style.top);
        this.oldImageLeft = BubbleManager.pxToNumber(img.style.left);
        this.lastCropControl = undefined;
        this.startMoveCropControlX = this.currentDragControl.offsetLeft;
        this.startMoveCropControlY = this.currentDragControl.offsetTop;

        document.addEventListener("mousemove", this.continueMoveCrop, {
            capture: true
        });
        // capture:true makes sure we can't miss it.
        document.addEventListener("mouseup", this.endMoveCrop, {
            capture: true
        });
    };
    private endMoveCrop = (event: MouseEvent) => {
        document.removeEventListener("mousemove", this.continueMoveCrop, {
            capture: true
        });
        document.removeEventListener("mouseup", this.endMoveCrop, {
            capture: true
        });
        this.currentDragControl?.classList.remove("active");
        this.currentDragControl!.style.left = "";
        this.currentDragControl!.style.top = "";
    };

    private continueMoveCrop = (event: MouseEvent) => {
        if (event.buttons !== 1 || !this.activeElement) {
            return;
        }
        const deltaX = event.clientX - this.startMoveCropX;
        const deltaY = event.clientY - this.startMoveCropY;
        const imgC = this.activeElement.getElementsByClassName(
            "bloom-imageContainer"
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (!img) return;
        const imgStyle = img.style;
        // left can't be greater than zero; that would leave empty space on the left.
        // also can't be so small as to make the right of the image (img.clientWidth + newLeft) less than
        // the right of the overlay (this.activeElement.clientLeft + this.activElement.clientWidth)
        const newLeft = Math.max(
            Math.min(this.oldImageLeft + deltaX, 0),
            this.activeElement.clientLeft +
                this.activeElement.clientWidth -
                img.clientWidth
        );
        const newTop = Math.max(
            Math.min(this.oldImageTop + deltaY, 0),
            this.activeElement.clientTop +
                this.activeElement.clientHeight -
                img.clientHeight
        );
        imgStyle.left = newLeft + "px";
        imgStyle.top = newTop + "px";
        this.currentDragControl!.style.left =
            this.startMoveCropControlX + newLeft - this.oldImageLeft + "px";
        this.currentDragControl!.style.top =
            this.startMoveCropControlY + newTop - this.oldImageTop + "px";
    };

    private startResizeDrag(
        event: MouseEvent,
        corner: "ne" | "nw" | "se" | "sw"
    ) {
        event.preventDefault();
        event.stopPropagation();
        if (!this.activeElement) return;
        this.currentDragControl = event.currentTarget as HTMLElement;
        this.currentDragControl.classList.add("active-control");
        this.startResizeDragX = event.clientX;
        this.startResizeDragY = event.clientY;
        this.resizeDragCorner = corner;
        const style = this.activeElement.style;
        this.oldWidth = BubbleManager.pxToNumber(style.width);
        this.oldHeight = BubbleManager.pxToNumber(style.height);
        this.oldTop = BubbleManager.pxToNumber(style.top);
        this.oldLeft = BubbleManager.pxToNumber(style.left);
        const imgC = this.activeElement.getElementsByClassName(
            "bloom-imageContainer"
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (img && img.style.width) {
            this.oldImageWidth = BubbleManager.pxToNumber(img.style.width);
            this.oldImageTop = BubbleManager.pxToNumber(img.style.top);
            this.oldImageLeft = BubbleManager.pxToNumber(img.style.left);
        }
        document.addEventListener("mousemove", this.continueResizeDrag, {
            capture: true
        });
        // capture:true makes sure we can't miss it.
        document.addEventListener("mouseup", this.endResizeDrag, {
            capture: true
        });
    }
    private endResizeDrag = (_event: MouseEvent) => {
        document.removeEventListener("mousemove", this.continueResizeDrag, {
            capture: true
        });
        document.removeEventListener("mouseup", this.endResizeDrag, {
            capture: true
        });
        this.currentDragControl?.classList.remove("active-control");
    };

    private minWidth = 30; // @MinTextBoxWidth in bubble.less
    private minHeight = 30; // @MinTextBoxHeight in bubble.less

    // handles mouse move while dragging a resize handle.
    private continueResizeDrag = (event: MouseEvent) => {
        if (event.buttons !== 1 || !this.activeElement) {
            this.resizeDragCorner = undefined; // drag is over
            return;
        }
        // we're handling this event, we don't want (e.g.) Comical to do so as well.
        event.stopPropagation();
        event.preventDefault();
        // We seem to get an initial no-op mouse move right after the mouse down.
        // It would be harmless to go through all the steps for it, but it's quite annoying when
        // try to debug an actual move.
        if (event.movementX === 0 && event.movementY === 0) return;
        this.lastCropControl = undefined; // resize resets the basis for cropping

        if (!this.resizeDragCorner) return; // make lint happy
        const deltaX = event.clientX - this.startResizeDragX;
        const deltaY = event.clientY - this.startResizeDragY;
        const style = this.activeElement.style;
        const imgC = this.activeElement.getElementsByClassName(
            "bloom-imageContainer"
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        // The slope of a line from nw to se (since y is positive down, this is a positive slope).
        // If we're moving one of the other points we will negate it to get the slope of the line
        // from ne to sw
        let slope = img ? this.oldHeight / this.oldWidth : 0;

        // Default is all unchanged...we will adjust the appropriate ones depending on how far
        // the mouse moved and which corner is being dragged.
        let newWidth = this.oldWidth;
        let newHeight = this.oldHeight;
        let newTop = this.oldTop;
        let newLeft = this.oldLeft;
        switch (this.resizeDragCorner) {
            case "ne":
                newWidth = Math.max(this.oldWidth + deltaX, this.minWidth);
                newHeight = Math.max(this.oldHeight - deltaY, this.minHeight);
                // Use the difference here rather than deltaY so the minWidth is respected.
                newTop = this.oldTop + (this.oldHeight - newHeight);
                break;
            case "nw":
                newWidth = Math.max(this.oldWidth - deltaX, this.minWidth);
                newHeight = Math.max(this.oldHeight - deltaY, this.minHeight);
                newTop = this.oldTop + (this.oldHeight - newHeight);
                newLeft = this.oldLeft + (this.oldWidth - newWidth);
                break;
            case "se":
                newWidth = Math.max(this.oldWidth + deltaX, this.minWidth);
                newHeight = Math.max(this.oldHeight + deltaY, this.minHeight);
                break;
            case "sw":
                newWidth = Math.max(this.oldWidth - deltaX, this.minWidth);
                newHeight = Math.max(this.oldHeight + deltaY, this.minHeight);
                newLeft = this.oldLeft + (this.oldWidth - newWidth);
                break;
        }
        if (slope) {
            // We want to keep the aspect ratio of the image. So the possible places to move
            // the moving corner must be on a line through the opposite corner
            // (which isn't moving) with a slope that would make it pass through the
            // original position of the point that is moving.
            // If the point where the mouse is is not on that line, we pick the closest
            // point that is.
            // Note that we want to keep the aspect ratio of the overlay, not the original image.
            // The aspect ratio is not changed by resizing (thanks to this code here), but it
            // can be changed by cropping, and subsequent resizing should keep the same part
            // of the image visible, and therefore keep the aspect ratio produced by the cropping.
            // A first step is to set adjustX/Y to the new position that the moving corner would
            // have without any constraints, and originX/Y to the original position of the opposite
            // corner.
            let adjustX = newLeft;
            let adjustY = newTop;
            let originX = this.oldLeft;
            let originY = this.oldTop;
            switch (this.resizeDragCorner) {
                case "ne":
                    adjustX = newLeft + newWidth;
                    originY = this.oldTop + this.oldHeight; // SW
                    slope = -slope;
                    break;
                case "sw":
                    adjustY = newTop + newHeight;
                    originX = this.oldLeft + this.oldWidth; // NE
                    slope = -slope;
                    break;
                case "se":
                    adjustX = newLeft + newWidth;
                    adjustY = newTop + newHeight;
                    // origin is already NW
                    break;
                case "nw":
                    originX = this.oldLeft + this.oldWidth; // SE
                    originY = this.oldTop + this.oldHeight; // SE
                    break;
            }
            // move adjustX, adjustY to the closest point on a line through originX, originY with the given slope
            // point must be on line y = slope(x - originX) + originY
            // and on the line at right angles to it through newX/newY y = (x - adjustX)/-slope + adjustY
            // convert to standard equation a1 * x + b1 * y + c1 = 0, a2 * x + b2 * y + c2 = 0
            // b1 and b2 are 1 and can be dropped.
            const a1 = -slope;
            const c1 = slope * originX - originY;
            const a2 = 1 / slope;
            const c2 = -adjustX / slope - adjustY;
            adjustX = (c2 - c1) / (a1 - a2);
            adjustY = (c1 * a2 - c2 * a1) / (a1 - a2);
            switch (this.resizeDragCorner) {
                case "ne":
                    newWidth = adjustX - this.oldLeft;
                    newHeight = this.oldTop + this.oldHeight - adjustY;
                    break;
                case "sw":
                    newHeight = adjustY - this.oldTop;
                    newWidth = this.oldLeft + this.oldWidth - adjustX;
                    break;
                case "se":
                    newWidth = adjustX - this.oldLeft;
                    newHeight = adjustY - this.oldTop;
                    break;
                case "nw":
                    newWidth = this.oldLeft + this.oldWidth - adjustX;
                    newHeight = this.oldTop + this.oldHeight - adjustY;
                    break;
            }
            if (newWidth < this.minWidth) {
                newWidth = this.minWidth;
                newHeight = newWidth * slope;
            }
            if (newHeight < this.minHeight) {
                newHeight = this.minHeight;
                newWidth = newHeight / slope;
            }
            switch (this.resizeDragCorner) {
                case "ne":
                    newTop = adjustY;
                    break;
                case "sw":
                    newLeft = adjustX;
                    break;
                case "se":
                    break;
                case "nw":
                    newLeft = adjustX;
                    newTop = adjustY;

                    break;
            }
        }
        style.width = newWidth + "px";
        style.height = newHeight + "px";
        style.top = newTop + "px";
        style.left = newLeft + "px";
        // Now, if the image is not cropped, it will resize automatically (width: 100% from
        // stylesheet, height unset so automatically scales with width). If it is cropped,
        // we need to resize it so that it stays the same amount cropped visually.
        if (img?.style.width) {
            const scale = newWidth / this.oldWidth;
            img.style.width = this.oldImageWidth * scale + "px";
            // to keep the same part of it showing, we need to scale left and top the same way.
            img.style.left = this.oldImageLeft * scale + "px";
            img.style.top = this.oldImageTop * scale + "px";
        }
        // Finally, adjust various things that are affected by the new size.
        this.alignControlFrameWithActiveElement();
        this.adjustTarget(this.activeElement);
    };
    private startSideDragX: number;
    private startSideDragY: number;

    // The most recent crop control that was dragged. We use this to decide whether to
    // reset the initial values.
    // Multiple drags of the same crop control can use the same initial values
    // to help figure the effect of dragging past the edge of the image.
    // This (and the other initial values) are set when the first drag on a particular
    // crop control starts since various events which reset it to undefined.
    // (This is modeled on Canva, but that is not an arbitrary choice. For example, if we
    // did not reset cropping when the overlay was moved, we would need to adjust
    // initialCropBubbleTop/Left in a non-obvious way).
    private lastCropControl: HTMLElement | undefined;
    private initialCropImageWidth: number;
    private initialCropImageHeight: number;
    private initialCropImageLeft: number;
    private initialCropImageTop: number;
    private initialCropBubbleWidth: number;
    private initialCropBubbleHeight: number;
    private initialCropBubbleTop: number;
    private initialCropBubbleLeft: number;

    private currentDragSide: string | undefined;
    // For both resize and crop
    private currentDragControl: HTMLElement | undefined;

    private startSideControlDrag(event: MouseEvent, side: string) {
        const img = this.activeElement?.getElementsByTagName("img")[0];
        const textBox = this.activeElement?.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on"
        )[0];
        if ((!img && !textBox) || !this.activeElement) {
            return;
        }
        this.startSideDragX = event.clientX;
        this.startSideDragY = event.clientY;
        this.currentDragControl = event.currentTarget as HTMLElement;
        this.currentDragControl.classList.add("active-control");
        this.currentDragSide = side;
        const style = this.activeElement.style;
        this.oldWidth = BubbleManager.pxToNumber(style.width);
        this.oldHeight = BubbleManager.pxToNumber(style.height);
        this.oldTop = BubbleManager.pxToNumber(style.top);
        this.oldLeft = BubbleManager.pxToNumber(style.left);
        if (img) {
            this.oldImageLeft = BubbleManager.pxToNumber(img.style.left);
            this.oldImageTop = BubbleManager.pxToNumber(img.style.top);

            if (this.lastCropControl !== event.currentTarget) {
                this.initialCropImageWidth = img.offsetWidth;
                this.initialCropImageHeight = img.offsetHeight;
                this.initialCropImageLeft = BubbleManager.pxToNumber(
                    img.style.left
                );
                this.initialCropImageTop = BubbleManager.pxToNumber(
                    img.style.top
                );
                this.initialCropBubbleWidth = this.activeElement.offsetWidth;
                this.initialCropBubbleHeight = this.activeElement.offsetHeight;
                this.initialCropBubbleTop = BubbleManager.pxToNumber(
                    this.activeElement.style.top
                );
                this.initialCropBubbleLeft = BubbleManager.pxToNumber(
                    this.activeElement.style.left
                );
                this.lastCropControl = event.currentTarget as HTMLElement;
            }
            if (!img.style.width) {
                // From here on it should stay this width unless we decide otherwise.
                img.style.width = `${this.initialCropImageWidth}px`;
                // tempting to add bloom-scale-with-code, which would prevent old versions of Bloom
                // from wiping out the width and height style settings we use for cropping.
                // However, it also triggers stuff in SetImageDisplaySizeIfCalledFor that is specific
                // to Kyrgyzstan and messes up cropping horribly, so that won't work.
            }
        }
        // move/up listeners are on the document so we can continue the drag even if it moves
        // outside the control clicked. I think something similar can be achieved
        // with mouse capture, but less portably.
        document.addEventListener("mousemove", this.continueSideDrag, {
            capture: true
        });
        // putting this in capture phase to make sure we can't miss it. Had some trouble with
        // mouseup not firing, possibly because something does stopPropagation.
        document.addEventListener("mouseup", this.stopSideDrag, {
            capture: true
        });
    }
    private stopSideDrag = () => {
        document.removeEventListener("mousemove", this.continueSideDrag, {
            capture: true
        });
        document.removeEventListener("mouseup", this.stopSideDrag, {
            capture: true
        });
        this.currentDragControl?.classList.remove("active-control");
    };
    private continueTextBoxResize(event: MouseEvent, editable: HTMLElement) {
        if (!this.activeElement) return; // should never happen, but makes lint happy
        let deltaX = event.clientX - this.startSideDragX;
        let deltaY = event.clientY - this.startSideDragY;
        let newBubbleWidth = this.oldWidth; // default
        let newBubbleHeight = this.oldHeight; // default
        console.assert(
            this.currentDragSide === "e" ||
                this.currentDragSide === "w" ||
                this.currentDragSide === "s"
        );
        switch (this.currentDragSide) {
            case "e":
                newBubbleWidth = Math.max(
                    this.oldWidth + deltaX,
                    this.minWidth
                );
                deltaX = newBubbleWidth - this.oldWidth;
                this.activeElement.style.width = `${newBubbleWidth}px`;
                break;
            case "w":
                newBubbleWidth = Math.max(
                    this.oldWidth - deltaX,
                    this.minWidth
                );
                deltaX = this.oldWidth - newBubbleWidth;
                this.activeElement.style.width = `${newBubbleWidth}px`;
                this.activeElement.style.left = `${this.oldLeft + deltaX}px`;
                break;
            case "s":
                newBubbleHeight = Math.max(
                    this.oldHeight + deltaY,
                    this.minHeight
                );
                deltaY = newBubbleHeight - this.oldHeight;
                this.activeElement.style.height = `${newBubbleHeight}px`;
        }
        // This won't adjust the height of the editable, but it will mark overflow appropriately.
        // See BL-13902.
        theOneBubbleManager.adjustBubbleHeightToContentOrMarkOverflow(editable);
        this.alignControlFrameWithActiveElement();
    }

    private continueSideDrag = (event: MouseEvent) => {
        if (event.buttons !== 1 || !this.activeElement) {
            return;
        }
        const textBox = this.activeElement.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on"
        )[0];
        if (textBox) {
            this.continueTextBoxResize(event, textBox as HTMLElement);
            return;
        }
        const img = this.activeElement?.getElementsByTagName("img")[0];
        if (!img) {
            // These handles shouldn't even be visible in this case, so this is for paranoia/lint.
            return;
        }
        // These may be adjusted to the deltas that would not violate min sizes
        let deltaX = event.clientX - this.startSideDragX;
        let deltaY = event.clientY - this.startSideDragY;
        if (event.movementX === 0 && event.movementY === 0) return;

        let newBubbleWidth = this.oldWidth; // default
        let newBubbleHeight = this.oldHeight;
        switch (this.currentDragSide) {
            case "n":
                newBubbleHeight = Math.max(
                    this.oldHeight - deltaY,
                    this.minHeight
                );
                // Everything subsequent behaves as if it only moved as far as permitted.
                deltaY = this.oldHeight - newBubbleHeight;
                this.activeElement.style.height = `${newBubbleHeight}px`;
                // Moves down by the amount the bubble shrank (or up by the amount it grew),
                // so the bottom stays in the same place
                this.activeElement.style.top = `${this.oldTop + deltaY}px`;
                // For a first attempt, we move it the oppposite of how the bubble actually
                // changd size. That might leave a gap at the top, but we'll adjust for that later.
                img.style.top = `${this.oldImageTop - deltaY}px`;
                break;
            case "s":
                newBubbleHeight = Math.max(
                    this.oldHeight + deltaY,
                    this.minHeight
                );
                deltaY = newBubbleHeight - this.oldHeight;
                this.activeElement.style.height = `${newBubbleHeight}px`;
                break;
            case "e":
                newBubbleWidth = Math.max(
                    this.oldWidth + deltaX,
                    this.minWidth
                );
                deltaX = newBubbleWidth - this.oldWidth;
                this.activeElement.style.width = `${newBubbleWidth}px`;
                break;
            case "w":
                newBubbleWidth = Math.max(
                    this.oldWidth - deltaX,
                    this.minWidth
                );
                deltaX = this.oldWidth - newBubbleWidth;
                this.activeElement.style.width = `${newBubbleWidth}px`;
                this.activeElement.style.left = `${this.oldLeft + deltaX}px`;
                img.style.left = `${this.oldImageLeft - deltaX}px`;
                break;
        }
        let newImageWidth: number;
        let newImageHeight: number;
        // How much of the image should stay cropped on the left if we're adjusting the right, etc.
        // Some of these are not needed on some sides, but it's easier to calculate them all,
        // and makes lint happy if we don't declare variables inside the switch.
        const leftFraction =
            -this.initialCropImageLeft / this.initialCropImageWidth;
        // Fraction of the total image width that is left of the center of the bubble.
        // This stays constant as we crop on the top and bottom.
        const centerFractionX =
            leftFraction +
            this.initialCropBubbleWidth / this.initialCropImageWidth / 2;
        const rightFraction =
            (this.initialCropImageWidth +
                this.initialCropImageLeft -
                this.initialCropBubbleWidth) /
            this.initialCropImageWidth;
        const bottomFraction =
            (this.initialCropImageHeight +
                this.initialCropImageTop -
                this.initialCropBubbleHeight) /
            this.initialCropImageHeight;
        const topFraction =
            -this.initialCropImageTop / this.initialCropImageHeight;
        // fraction of the total image height that is above the center of the bubble.
        // This stays constant as we crop on the left and right.
        const centerFractionY =
            topFraction +
            this.initialCropBubbleHeight / this.initialCropImageHeight / 2;
        // Deliberately dividing by the WIDTH here; all our calculations are
        // based on the adjusted width of the image.
        const topAsFractionOfWidth =
            -this.initialCropImageTop / this.initialCropImageWidth;
        // Specifically, the aspect ratio for computing the height of the (full) image
        // from its width.
        const aspectRatio = img.naturalHeight / img.naturalWidth;
        switch (this.currentDragSide) {
            case "e":
                if (
                    // the bubble has stretched beyond the right side of the image
                    newBubbleWidth >
                    this.initialCropImageLeft + this.initialCropImageWidth
                ) {
                    // grow the image. We want its right edge to end up at newBubbleWidth,
                    // after being stretched enough to leave the same fraction as before
                    // cropped on the left.
                    newImageWidth = newBubbleWidth / (1 - leftFraction);
                    img.style.width = `${newImageWidth}px`;
                    // fiddle with the left to keep the same part cropped
                    img.style.left = `${-leftFraction * newImageWidth}px`;
                    // and the top to split the extra height between top and bottom
                    newImageHeight = newImageWidth * aspectRatio;
                    const newTopFraction =
                        centerFractionY -
                        this.initialCropBubbleHeight / newImageHeight / 2;
                    img.style.top = `${-newTopFraction * newImageHeight}px`;
                } else {
                    // no need to stretch. Restore the image to its original position and size.
                    img.style.width = `${this.initialCropImageWidth}px`;
                    img.style.top = `${this.initialCropImageTop}px`;
                }
                break;
            case "w":
                if (
                    // the bubble has stretched beyond the original left side of the image
                    // this.oldLeft + deltaX is where the left of the bubble is now
                    // this.initialCropImageLeft + this.initialBubbleImageLeft is where
                    // the left of the image was when we started.
                    this.oldLeft + deltaX <
                    this.initialCropImageLeft + this.initialCropBubbleLeft
                ) {
                    // grow the image. We want its left edge to end up at zero,
                    // after being stretched enough to leave the same fraction as before
                    // cropped on the right.
                    newImageWidth = newBubbleWidth / (1 - rightFraction);
                    img.style.width = `${newImageWidth}px`;
                    // no cropping on the left
                    img.style.left = `0`;
                    // and the top to split the extra height between top and bottom
                    newImageHeight = newImageWidth * aspectRatio;
                    const newTopFraction =
                        centerFractionY -
                        this.initialCropBubbleHeight / newImageHeight / 2;
                    img.style.top = `${-newTopFraction * newImageHeight}px`;
                } else {
                    img.style.width = `${this.initialCropImageWidth}px`;
                    img.style.top = `${this.initialCropImageTop}px`;
                }
                break;
            case "s":
                if (
                    // the bubble has stretched beyond the bottom side of the image
                    newBubbleHeight >
                    this.initialCropImageTop + this.initialCropImageHeight
                ) {
                    // grow the image. We want its bottom edge to end up at newBubbleHeight,
                    // after being stretched enough to leave the same fraction as before
                    // cropped on the top.
                    newImageHeight = newBubbleHeight / (1 - topFraction);
                    newImageWidth = newImageHeight / aspectRatio;
                    img.style.width = `${newImageWidth}px`;
                    // fiddle with the top to keep the same part cropped
                    img.style.top = `${-topAsFractionOfWidth *
                        newImageWidth}px`;
                    // and the left to split the extra width between top and bottom
                    // centerFractionX = leftFraction + this.initialCropBubbleWidth / this.initialCropImageWidth / 2;
                    // centerFractionX = newleftFraction + this.initialCropBubbleWidth / newImageWidth / 2;
                    const newleftFraction =
                        centerFractionX -
                        this.initialCropBubbleWidth / newImageWidth / 2;
                    img.style.left = `${-newleftFraction * newImageWidth}px`;
                } else {
                    img.style.width = `${this.initialCropImageWidth}px`;
                    img.style.left = `${this.initialCropImageLeft}px`;
                }
                break;
            case "n":
                if (
                    // the bubble has stretched beyond the original top side of the image
                    // this.oldTop + deltaY is where the top of the bubble is now
                    // this.initialCropImageTop + this.initialBubbleImageTop is where
                    // the top of the image was when we started.
                    this.oldTop + deltaY <
                    this.initialCropImageTop + this.initialCropBubbleTop
                ) {
                    // grow the image. We want its top edge to end up at zero,
                    // after being stretched enough to leave the same fraction as before
                    // cropped on the bottom.
                    newImageHeight = newBubbleHeight / (1 - bottomFraction);
                    newImageWidth = newImageHeight / aspectRatio;
                    img.style.width = `${newImageWidth}px`;
                    // no cropping on the top
                    img.style.top = `0`;
                    // and the left to split the extra width between top and bottom
                    const newleftFraction =
                        centerFractionX -
                        this.initialCropBubbleWidth / newImageWidth / 2;
                    img.style.left = `${-newleftFraction * newImageWidth}px`;
                } else {
                    img.style.width = `${this.initialCropImageWidth}px`;
                    img.style.left = `${this.initialCropImageLeft}px`;
                }
                break;
        }
        // adjust other things that are affected by the new size.
        this.alignControlFrameWithActiveElement();
        this.adjustTarget(this.activeElement);
    };

    // If this overlay contains an image, and it has not already been adjusted so that the overlay
    // dimensions have the same aspect ratio as the image, make it so, reducing either height or
    // width as necessary.
    private matchContainerToImage(overlay: HTMLElement) {
        const container = overlay.getElementsByClassName(
            "bloom-imageContainer"
        )[0];
        // Don't go straight from overlay to img. A text box, for example, contains an img
        // that is the cog wheel for the format dialog. We don't want to match the overlay
        // aspect ratio to that.
        const img = container?.getElementsByTagName("img")[0];
        if (!img) return;
        if (img.style.width) {
            // we've already done cropping on this image, so we should not force the
            // container back to the original image shape.
            return;
        }
        const containerWidth = overlay.clientWidth;
        const containerHeight = overlay.clientHeight;
        const imgWidth = img.naturalWidth;
        const imgHeight = img.naturalHeight;
        const imgRatio = imgWidth / imgHeight;
        const containerRatio = containerWidth / containerHeight;
        let newHeight = containerHeight;
        let newWidth = containerWidth;
        if (imgRatio > containerRatio) {
            // remove white bars at top and bottom by reducing container height
            newHeight = containerWidth / imgRatio;
            if (newHeight < this.minHeight) {
                newHeight = this.minHeight;
                newWidth = newHeight * imgRatio;
            }
        } else {
            // remove white bars at left and right by reducing container width
            newWidth = containerHeight * imgRatio;
            if (newWidth < this.minWidth) {
                newWidth = this.minWidth;
                newHeight = newWidth / imgRatio;
            }
        }
        const oldHeight = BubbleManager.pxToNumber(overlay.style.height);
        overlay.style.height = `${newHeight}px`;
        // and move container down so image does not move
        const oldTop = BubbleManager.pxToNumber(overlay.style.top);
        overlay.style.top = `${oldTop + (oldHeight - newHeight) / 2}px`;
        const oldWidth = BubbleManager.pxToNumber(overlay.style.width);
        overlay.style.width = `${newWidth}px`;
        // and move container right so image does not move
        const oldLeft = BubbleManager.pxToNumber(overlay.style.left);
        overlay.style.left = `${oldLeft + (oldWidth - newWidth) / 2}px`;
    }

    // When the image is changed in a bubble (e.g., choose or paste image),
    // we remove cropping, adjust the aspect ratio, and move the control frame.
    updateBubbleForChangedImage(imgOrImageContainer: HTMLElement) {
        const overlay = imgOrImageContainer.closest(
            kTextOverPictureSelector
        ) as HTMLElement;
        if (!overlay) return;
        const img =
            imgOrImageContainer.tagName === "IMG"
                ? imgOrImageContainer
                : imgOrImageContainer.getElementsByTagName("img")[0];
        if (!img) return;
        // remove any cropping
        img.style.width = "";
        img.style.height = "";
        img.style.left = "";
        img.style.top = "";
        // Get the aspect ratio right
        this.matchContainerToImage(overlay);
        // and align the controls with the new image size
        this.alignControlFrameWithActiveElement();
    }

    private async getHandleTitlesAsync(
        controlFrame: HTMLElement,
        className: string,
        l10nId: string,
        force: boolean = false
    ) {
        const handles = Array.from(
            controlFrame.getElementsByClassName(className)
        ) as HTMLElement[];
        // We could cache these somewhere, especially the crop/change shape pair, but I think
        // it would be premature optimization. We only have four title, and
        // only the crop/change shape one has to be retrieved each time the frame moves.
        if (!handles[0]?.title || force) {
            const title = await theOneLocalizationManager.asyncGetText(
                "EditTab.Toolbox.ComicTool.Handle." + l10nId,
                "",
                ""
            );
            handles.forEach(handle => {
                handle.title = title;
            });
        }
    }

    // Align the control frame with the active overlay.
    private alignControlFrameWithActiveElement = () => {
        const controlFrame = document.getElementById("overlay-control-frame");
        let controlsAbove = false;
        if (controlFrame && this.activeElement) {
            controlFrame.classList.toggle(
                "bloom-noAutoHeight",
                this.activeElement.classList.contains("bloom-noAutoHeight")
            );
            const hasText = controlFrame.classList.contains("has-text");
            // We don't need to await these, they are just async so the handle titles can be updated
            // once the localization manager retrieves them.
            this.getHandleTitlesAsync(
                controlFrame,
                "bloom-ui-overlay-resize-handle",
                "Resize"
            );
            this.getHandleTitlesAsync(
                controlFrame,
                "bloom-ui-overlay-side-handle",
                hasText ? "ChangeShape" : "Crop",
                true
            );
            this.getHandleTitlesAsync(
                controlFrame,
                "bloom-ui-overlay-move-crop-handle",
                "Shift"
            );
            // Text boxes get a little extra padding, making the control frame bigger than
            // the overlay itself. The extra needed corresponds roughly to the (.less) @sideHandleRadius,
            // but one pixel less seems to be enough to prevent the side handles actually overlapping text,
            // though maybe I've just been lucky and this should really be 4.
            // Seems like it should be easy to do this in the .less file, but the control frame is not
            // a child of the overlay (for z-order reasons), so it's not easy for CSS to move it left
            // when the style is already absolutely controlling style.left. It's easier to just tweak
            // it here.
            const extraPadding = hasText ? 3 : 0;
            controlFrame.style.width =
                this.activeElement.clientWidth + 2 * extraPadding + "px";
            controlFrame.style.height = this.activeElement.style.height;
            controlFrame.style.left =
                BubbleManager.pxToNumber(this.activeElement.style.left) -
                extraPadding +
                "px";
            controlFrame.style.top = this.activeElement.style.top;
            const tails = Bubble.getBubbleSpec(this.activeElement).tails;
            if (tails.length > 0) {
                const tipY = tails[0].tipY;
                controlsAbove =
                    tipY >
                    this.activeElement.clientHeight +
                        this.activeElement.offsetTop;
            }
        }
        this.adjustMoveCropHandleVisibility();
        this.adjustContextControlPosition(controlFrame, controlsAbove);
    };

    adjustContextControlPosition(
        controlFrame: HTMLElement | null,
        controlsAbove: boolean
    ) {
        const contextControl = document.getElementById(
            "overlay-context-controls"
        );
        if (!contextControl) return;
        if (!controlFrame) {
            contextControl.remove();
            return;
        }
        const scalingContainer = document.getElementById(
            "page-scaling-container"
        );
        // The context controls look as if they're on the page, so they should have the same scaling.
        // But they aren't actually in the scaling container, so we have to give them their
        // own scaling transform.
        contextControl.style.transform =
            scalingContainer?.style.transform ?? "";
        const controlFrameRect = controlFrame.getBoundingClientRect();
        const contextControlRect = contextControl.getBoundingClientRect();
        const scale = Point.getScalingFactor();

        // This just needs to be wider than the context controls ever are. The get centered in a box this wide.
        const contextControlsWidth = 300;
        // Subtracting half the width of the context control frame and adding half the width of the control Frame
        // centers it. The width of the context controls is scaled by its own transform (which we set
        // to match the one that applies to the control frame) so we need to scale the left offset the same.)
        // The width of the control frame rect is already scaled by the transform.
        const left =
            controlFrameRect.left +
            window.scrollX +
            controlFrameRect.width / 2 -
            (contextControlsWidth / 2) * scale;
        let top = controlFrameRect.top + window.scrollY;
        if (controlsAbove) {
            // Bottom 11 px above the top of the control frame.
            top -= contextControlRect.height + 11;
        } else {
            // Top 11 px below the bottom of the control frame
            top += controlFrameRect.height + 11;
        }
        contextControl.style.left = left + "px";
        contextControl.style.top = top + "px";
        // This is constant, so it could be in the CSS. But then it could not share a constant
        // with the computation of left above, so it would be harder to keep things consistent.
        contextControl.style.width = contextControlsWidth + "px";
    }

    public doNotifyChange() {
        const spec = this.getSelectedFamilySpec();
        this.thingsToNotifyOfBubbleChange.forEach(f => f.handler(spec));
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
                ev.dataTransfer.getData("text/x-bloombubble") &&
                !ev.dataTransfer.getData("text/x-bloomdraggable") // items that create a draggable use another approach
            ) {
                ev.preventDefault();
                const style = ev.dataTransfer
                    ? ev.dataTransfer.getData("text/x-bloombubble")
                    : "speech";
                // If this got used, we'd want it to have a rightTopOffset value. But I think all our things that can
                // be dragged are now using overlayItem, and its dragStart sets text/x-bloomdraggable, so this
                // code does't get used.
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
    public ensureBubblesIntersectParent(parentContainer: HTMLElement) {
        const overlays = Array.from(
            parentContainer.getElementsByClassName(kTextOverPictureClass)
        );
        let changed = false;
        overlays.forEach(overlay => {
            const bubbleRect = overlay.getBoundingClientRect();
            // If the bubble is not visible, its width will be 0. Don't try to adjust it.
            if (bubbleRect.width === 0) return;
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
        this.alignControlFrameWithActiveElement();
    }

    // Move the text insertion point to the specified location.
    // This is what a click at that location would typically do, but we are intercepting
    // those events to turn the click into a drag of the bubble if there is mouse movement.
    // This uses the browser's caretPositionFromPoint or caretRangeFromPoint, which are not
    // supported by all browsers, but at least one of them works in WebView2, which is all we need.
    private moveInsertionPointAndFocusTo = (x, y): Range | undefined => {
        const doc = document as any;
        const rangeOrCaret = doc.caretPositionFromPoint
            ? doc.caretPositionFromPoint(x, y)
            : doc.caretRangeFromPoint
            ? doc.caretRangeFromPoint(x, y)
            : null;
        let range = rangeOrCaret;
        if (!range) {
            return undefined;
        }
        // We really seem to need to handle both possibilities. I had it working with just the
        // code for range, then restarted Bloom and started getting CaretPositions. Maybe a new
        // version of WebView2 got auto-installed? Anyway, now it should handle both.
        if (!range.endContainer) {
            // probably a CaretPositon. We need a range to use with addRange.
            range = document.createRange();
            range.setStart(rangeOrCaret.offsetNode, rangeOrCaret.offset);
            range.setEnd(rangeOrCaret.offsetNode, rangeOrCaret.offset);
        }

        if (range && range.collapse && range?.endContainer?.parentElement) {
            range.collapse(false); // probably not needed?
            range.endContainer.parentElement.focus();
            const setSelection = () => {
                const selection = window.getSelection();
                selection?.removeAllRanges();
                selection?.addRange(range);
            };
            // I have _no_ idea why it is necessary to do this twice, but if we don't, the selection
            // ends up at a more-or-less random position (often something that was recently selected).
            setSelection();
            setSelection();
        }
        return range as Range;
    };

    private activeElementAtMouseDown: HTMLElement | undefined;
    // Keeps track of whether we think the mouse is down (that is, we've handled a mouseDown but not
    // yet a mouseUp)). Does not get set if our mouseDown handler finds that isMouseEventAlreadyHandled
    // returns true.
    private mouseIsDown = false;

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseDown = (event: MouseEvent) => {
        this.activeElementAtMouseDown = this.activeElement;
        const container = event.currentTarget as HTMLElement;
        // Let standard clicks on the bloom editable or other UI elements only be processed by that element
        if (this.isMouseEventAlreadyHandled(event)) {
            return;
        }
        this.gotAMoveWhileMouseDown = false;
        this.mouseIsDown = true;

        // These coordinates need to be relative to the canvas (which is the same as relative to the image container).
        const coordinates = this.getPointRelativeToCanvas(event, container);

        if (!coordinates) {
            return;
        }

        const bubble = Comical.getBubbleHit(
            container,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY(),
            true // only consider bubbles with pointer events allowed.
        );
        if (bubble && event.button === 2) {
            // Right mouse button
            if (bubble.content !== this.activeElement) {
                this.setActiveElement(bubble.content);
            }
            // Aimed at preventing the browser context menu from appearing, but did not succeed.
            // But I don't think we want any other right-click behavior than the menu, so we may
            // as well suppress it.
            event.preventDefault();
            event.stopPropagation();
            // re-render the toolbox with its menu open at the desired location
            renderOverlayContextControls(bubble.content, true, {
                left: event.clientX,
                top: event.clientY
            });
            return;
        }

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

        const startDraggingBubble = (bubble: Bubble) => {
            // Note: at this point we do NOT want to focus it. Only if we decide in mouse up that we want to text-edit it.
            this.setActiveElement(bubble.content);
            const positionInfo = bubble.content.getBoundingClientRect();

            // Possible move action started
            this.bubbleToDrag = bubble;
            this.activeContainer = container;
            // in case this is somehow left from earlier, we want a fresh start for the new move.
            this.animationFrame = 0;

            // Remember the offset between the top-left of the content box and the initial
            // location of the mouse pointer.
            const deltaX = event.pageX - positionInfo.left;
            const deltaY = event.pageY - positionInfo.top;
            this.bubbleDragGrabOffset = { x: deltaX, y: deltaY };
        };

        if (bubble) {
            if (
                window.getComputedStyle(bubble.content).pointerEvents === "none"
            ) {
                // We're doing some fairly tricky stuff to handle an event on a parent element but
                // use it to manipulate a child. If the child is not supposed to be responding to
                // pointer events, we should not be manipulating it here either.
                return;
            }
            if (event.altKey) {
                event.preventDefault();
                event.stopPropagation();
                // using this trick for a bubble that is part of a family doesn't work well.
                // We can only drag one bubble at once, so where should we put the other duplicate?
                // Maybe we can come up with an answer, but for now, I'm just going to ignore the alt key.
                if (Comical.findRelatives(bubble).length === 0) {
                    // duplicate the bubble and drag that.
                    // currently duplicateTOPBox actually dupliates the current active element,
                    // not the one it is passed. So make sure the one we clicked is active, though it won't be for long.
                    this.setActiveElement(bubble.content);
                    const newBubble = this.duplicateTOPBox(
                        bubble.content,
                        true
                    );
                    if (!newBubble) return;
                    startDraggingBubble(new Bubble(newBubble));
                    return;
                }
            }
            // We clicked on a bubble that's not disabled. If we clicked inside the bubble we are
            // text editing, and neither ctrl nor alt is down, we handle it normally. Otherwise, we
            // need to suppress. If we're outside the editable but inside the bubble, we don't need any default event processing,
            // and if we're inside and ctrl or alt is down, we want to prevent the events being
            // processed by the text. And if we're inside a bubble not yet recognized as the one we're
            // editing, we want to suppress the event because, unless it turns out to be a simple click
            // with no movement, we're going to treat it as dragging the bubble.
            const clickOnBubbleWeAreEditing =
                this.theBubbleWeAreTextEditing ===
                    (event.target as HTMLElement)?.closest(
                        kTextOverPictureSelector
                    ) && this.theBubbleWeAreTextEditing;
            if (event.altKey || event.ctrlKey || !clickOnBubbleWeAreEditing) {
                event.preventDefault();
                event.stopPropagation();
            }
            startDraggingBubble(bubble);
        }
    };

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseMove = (event: MouseEvent) => {
        if (BubbleManager.inPlayMode(event.currentTarget as HTMLElement)) {
            return; // no edit mode functionality is relevant
        }
        if (event.buttons === 0 && this.mouseIsDown) {
            // we missed the mouse up...maybe because we're debugging? In any case, we don't want to go
            // on doing drag-type things; best to simulate the mouse up we missed.
            this.onMouseUp(event);
            return;
        }
        // Capture the most recent data to use when our animation frame request is satisfied.
        // or so keyboard events can reference the current mouse position.
        this.lastMoveEvent = event;
        if (event.buttons === 1 && (event.movementX || event.movementY)) {
            this.gotAMoveWhileMouseDown = true;
            const controlFrame = document.getElementById(
                "overlay-control-frame"
            );
            controlFrame?.classList?.add("moving");
            this.activeElement?.classList?.add("moving");
            document
                .getElementById("overlay-context-controls")
                ?.classList?.add("moving");
        }

        const container = event.currentTarget as HTMLElement;

        if (!this.bubbleToDrag) {
            this.handleMouseMoveHover(event, container);
        } else if (this.bubbleToDrag) {
            this.handleMouseMoveDragBubble(event, container);
        }
    };

    // Mouse hover - No move or resize is currently active, but check if there is a bubble under the mouse that COULD be
    // and add or remove the classes we use to indicate this
    private handleMouseMoveHover(event: MouseEvent, container: HTMLElement) {
        if (this.isMouseEventAlreadyHandled(event)) {
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
            if (this.activeElement) {
                tryRemoveImageEditingButtons(
                    this.activeElement.getElementsByClassName(
                        "bloom-imageContainer"
                    )[0]
                );
            }
            return;
        }

        const isVideo = this.isVideoOverPictureElement(hoveredBubble.content);
        const targetElement = event.target as HTMLElement;
        // Over a bubble that could be dragged (ignoring the bloom-editable portion),
        // make the mouse indicate that dragging/resizing is possible (except for
        // text boxes, which can also be clicked for text editing, so it would be confusing
        // and make it hard to click in the exact place wanted).
        if (!event.altKey) {
            // event.altKey test is probably redundant due to BL-13899.
            if (isVideo) {
                if (event.ctrlKey) {
                    // In this case, we want to drag the container.
                    targetElement.removeAttribute("controls");
                } else {
                    // In this case, we want to play the video, if we click on it.
                    targetElement.setAttribute("controls", "");
                }
            }
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

    private animationFrame: number;
    private lastMoveEvent: MouseEvent;
    private lastMoveContainer: HTMLElement;

    // A bubble is currently in drag mode, and the mouse is being moved.
    // Move the bubble accordingly.
    private handleMouseMoveDragBubble(
        event: MouseEvent,
        container: HTMLElement
    ) {
        if (event.buttons === 0) {
            // we missed the mouse up...maybe because we're debugging? In any case, we need to
            // get out of that mode.
            this.onMouseUp(event);
            return;
        }
        if (this.activeElement) {
            const r = this.activeElement.getBoundingClientRect();
            const activeContainer = this.activeElement.parentElement?.closest(
                kImageContainerSelector
            );
            if (activeContainer) {
                const canvas = this.getFirstCanvasForContainer(activeContainer);
                if (canvas)
                    canvas.classList.toggle(
                        "moving",
                        event.clientX > r.left &&
                            event.clientX < r.right &&
                            event.clientY > r.top &&
                            event.clientY < r.bottom
                    );
            }
        }
        // Capture the most recent data to use when our animation frame request is satisfied.
        this.lastMoveContainer = container;
        this.lastMoveContainer.style.cursor = "move";
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
            this.lastCropControl = undefined; // move resets the basis for cropping
            this.animationFrame = 0;
        });
    }

    // The center handle, used to move the picture under the bubble, does nothing
    // unless the bubble has actually been cropped. Unless we figure out something
    // sensible to do in this case, it's better not to show it, lest the user be
    // confused by a control that does nothing.
    private adjustMoveCropHandleVisibility() {
        const controlFrame = document.getElementById("overlay-control-frame");
        if (!controlFrame || !this.activeElement) return;
        const imgC = this.activeElement.getElementsByClassName(
            kImageContainerClass
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        let wantMoveCropHandle = false;
        if (img) {
            const imgRect = img.getBoundingClientRect();
            const controlRect = controlFrame.getBoundingClientRect();
            // We don't ever allow it to be smaller, nor to be offset without being larger, so this is enough to test.
            // Rounding errors can throw things off slightly, especially when zoomed, so we give a one-pixel margin.
            // Not much point moving the picture if we're only one pixel cropped, anyway.
            wantMoveCropHandle =
                imgRect.width > controlRect.width + 1 ||
                imgRect.height > controlRect.height + 1;
        }
        controlFrame.classList.toggle(
            "bloom-ui-overlay-show-move-crop-handle",
            wantMoveCropHandle
        );
    }

    private stopMoving() {
        if (this.lastMoveContainer) this.lastMoveContainer.style.cursor = "";
        const controlFrame = document.getElementById("overlay-control-frame");
        // We want to get rid of it at least from the control frame and the active bubble,
        // but may as well make sure it doesn't get left anywhere.
        Array.from(document.getElementsByClassName("moving")).forEach(
            element => {
                element.classList.remove("moving");
            }
        );
        this.adjustMoveCropHandleVisibility();
        this.alignControlFrameWithActiveElement();
    }

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseUp = (event: MouseEvent) => {
        this.mouseIsDown = false;
        const container = event.currentTarget as HTMLElement;
        if (BubbleManager.inPlayMode(container)) {
            return;
        }
        this.stopMoving();

        if (this.bubbleToDrag) {
            // if we're doing a resize or drag, we don't want ordinary mouseup activity
            // on the text inside the bubble.
            event.preventDefault();
            event.stopPropagation();
        }

        this.bubbleToDrag = undefined;
        container.classList.remove("grabbing");
        this.turnOffResizing(container);
        const editable = (event.target as HTMLElement)?.closest(
            ".bloom-editable"
        );
        if (
            editable &&
            editable.closest(kTextOverPictureSelector) ===
                this.theBubbleWeAreTextEditing
        ) {
            // We're text editing in this overlay, let the mouse do its normal things.
            // In particular, we don't want to do moveInsertionPointAndFocusTo here,
            // because it will force the selection back to an IP when we might want a range
            // (e.g., after a double-click).
            // (But note, if we started out with the overlay not active, a double click
            // is properly interpreted as one click to select the overlay, one to put it
            // into edit mode...that is NOT a regular double-click that selects a word.
            // At least, that seems to be what Canva does.)
            return;
        }
        // a click without movement on an overlay that is already the active one puts it in edit mode.
        if (
            !this.gotAMoveWhileMouseDown &&
            editable &&
            this.activeElementAtMouseDown === this.activeElement
        ) {
            // Going into edit mode on this bubble.
            this.theBubbleWeAreTextEditing = (event.target as HTMLElement)?.closest(
                kTextOverPictureSelector
            ) as HTMLElement;
            this.theBubbleWeAreTextEditing?.classList.add("bloom-focusedTOP");
            // We want to position the IP as if the user clicked where they did.
            // Since we already suppressed the mouseDown event, it's not enough to just
            // NOT suppress the mouseUp event. We need to actually move the IP to the
            // appropriate spot and give the bubble focus.
            this.moveInsertionPointAndFocusTo(event.clientX, event.clientY);
        } else if (!this.isMouseEventAlreadyHandled(event)) {
            // prevent the click giving it focus (or any other default behavior). This mouse up
            // is part of dragging a bubble or resizing it or some similar special behavior that
            // we are handling.
            event.preventDefault();
            event.stopPropagation();
        }
    };

    public turnOffResizing(container: Element) {
        this.activeContainer = undefined;
    }

    // If we get a click (without movement) on a text bubble, we treat subsequent mouse events on
    // that bubble as text editing events, rather than drag events, as long as it keeps focus.
    // This is the bubble, if any, that is currently in that state.
    public theBubbleWeAreTextEditing: HTMLElement | undefined;
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
        if (BubbleManager.inPlayMode(targetElement)) {
            // Game in play mode...no edit mode functionality is relevant
            return true;
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
        if (targetElement.closest("#overlay-control-frame")) {
            // New drag controls
            return true;
        }
        if (targetElement.closest("[data-target-of")) {
            // Bloom game targets want to handle their own dragging.
            return true;
        }
        if (ev.ctrlKey || ev.altKey) {
            return false;
        }
        const editable = targetElement.closest(".bloom-editable");
        if (
            editable &&
            this.theBubbleWeAreTextEditing &&
            this.theBubbleWeAreTextEditing.contains(editable) &&
            ev.button !== 2
        ) {
            // an editable is allowed to handle its own events only if it's parent bubble has
            // been established as active for text editing and it's not a right-click.
            // Otherwise, we handle it as a move (or context menu request, or...).
            return true;
        }
        if (targetElement.closest(".MuiDialog-container")) {
            // Dialog boxes (e.g., letter game prompt) get to handle their own events.
            return true;
        }
        return false;
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

    public turnOffBubbleEditing(): void {
        if (this.isComicEditingOn === false) {
            return; // Already off. No work needs to be done.
        }
        this.isComicEditingOn = false;
        this.removeControlFrame();
        this.removeFocusClass();

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
            const editables = this.getAllVisibileEditableDivs(
                container as HTMLElement
            );
            editables.forEach(element => {
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
        id: string,
        notifier: (bubble: BubbleSpec | undefined) => void
    ): void {
        this.detachBubbleChangeNotification(id);
        this.thingsToNotifyOfBubbleChange.push({ id, handler: notifier });
    }

    public detachBubbleChangeNotification(id: string): void {
        const index = this.thingsToNotifyOfBubbleChange.findIndex(
            x => x.id === id
        );
        if (index >= 0) {
            this.thingsToNotifyOfBubbleChange.splice(index, 1);
        }
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

    // Adjust the ordering of bubbles so that draggables are at the end.
    // We want the things that can be moved around to be on top of the ones that can't.
    // We don't use z-index because that makes stacking contexts and interferes with
    // the way we keep bubble children on top of the canvas.
    // Bubble levels should be consistent with the order of the elements in the DOM,
    // since the former controls which one is treated as being clicked when there is overlap,
    // while the latter determines which is on top.
    public adjustBubbleOrdering = () => {
        const parents = this.getAllPrimaryImageContainersOnPage();
        parents.forEach(imageContainer => {
            const bubbles = Array.from(
                imageContainer.getElementsByClassName("bloom-textOverPicture")
            );
            let maxLevel = Math.max(
                ...bubbles.map(
                    b => Bubble.getBubbleSpec(b as HTMLElement).level ?? 0
                )
            );
            const draggables = bubbles.filter(b =>
                b.getAttribute("data-bubble-id")
            );
            if (
                draggables.length === 0 ||
                bubbles.indexOf(draggables[0]) ===
                    bubbles.length - draggables.length
            ) {
                return; // already all at end (or none to move)
            }
            // Move them to the end, keeping them in order.
            draggables.forEach(draggable => {
                draggable.parentElement?.appendChild(draggable);
                const bubble = new Bubble(draggable as HTMLElement);
                // This would need to get fancier if draggbles came in groups with the same level.
                // As it is, we just want their levels to be in the same order as their DOM order
                // (relative to each other and the other bubbles) so getBubbleHit() will return
                // the one that appears on top when they are stacked.
                bubble.getBubbleSpec().level = maxLevel + 1;
                bubble.persistBubbleSpec();
                maxLevel++;
            });
            const parentContainer = draggables[0].closest(
                ".bloom-imageContainer"
            );
            Comical.update(parentContainer as HTMLElement);
        });
    };

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
            false,
            true
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
        style: string,
        userDefinedStyleName?: string,
        rightTopOffset?: string
    ): HTMLElement | undefined {
        const clientX = screenX - window.screenX;
        const clientY = screenY - window.screenY;
        return this.addOverPictureElement(
            clientX,
            clientY,
            style,
            userDefinedStyleName,
            rightTopOffset
        );
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
        style?: string,
        userDefinedStyleName?: string,
        rightTopOffset?: string
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
            return this.addVideoOverPicture(
                positionInViewport,
                imageContainer,
                rightTopOffset
            );
        }
        if (style === "image") {
            return this.addPictureOverPicture(
                positionInViewport,
                imageContainer,
                rightTopOffset
            );
        }
        return this.addTextOverPicture(
            positionInViewport,
            imageContainer,
            style,
            userDefinedStyleName,
            rightTopOffset
        );
    }

    private addTextOverPicture(
        location: Point,
        imageContainerJQuery: JQuery,
        style?: string,
        userDefinedStyleName?: string,
        rightTopOffset?: string
    ): HTMLElement {
        const defaultNewTextLanguage = GetSettings().languageForNewTextBoxes;
        const userDefinedStyle = userDefinedStyleName ?? "Bubble";
        // add a draggable text bubble to the html dom of the current page
        const editableDivClasses = `bloom-editable bloom-content1 bloom-visibility-code-on ${userDefinedStyle}-style`;
        const editableDivHtml =
            "<div class='" +
            editableDivClasses +
            "' lang='" +
            defaultNewTextLanguage +
            "'><p></p></div>";

        const transGroupDivClasses = `bloom-translationGroup bloom-leadingElement ${userDefinedStyle}-style`;
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
            false,
            rightTopOffset
        );
    }

    private addVideoOverPicture(
        location: Point,
        imageContainerJQuery: JQuery,
        rightTopOffset?: string
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
            true,
            rightTopOffset
        );
    }

    private addPictureOverPicture(
        location: Point,
        imageContainerJQuery: JQuery,
        rightTopOffset?: string
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
            true,
            rightTopOffset
        );
    }

    private finishAddingOverPictureElement(
        imageContainerJQuery: JQuery,
        internalHtml: string,
        location: Point,
        style?: string,
        setElementActive?: boolean,
        rightTopOffset?: string
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
            location,
            rightTopOffset
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
            this.doNotifyChange();
            this.showCorrespondingTextBox(contentElement);
        }
        const bubble = new Bubble(contentElement);
        const bubbleSpec: BubbleSpec = Bubble.getDefaultBubbleSpec(
            contentElement,
            style || "speech"
        );
        bubble.setBubbleSpec(bubbleSpec);
        const imageContainer = imageContainerJQuery.get(0);
        this.refreshBubbleEditing(imageContainer, bubble, true, true);
        const editable = contentElement.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on"
        )[0] as HTMLElement;
        editable?.focus();
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
    // The desired element might be covered by a .MuiModal-backdrop, so we may
    // need to check multiple elements at that location.
    private getImageContainerFromMouse(mouseX: number, mouseY: number): JQuery {
        const elements = document.elementsFromPoint(mouseX, mouseY);
        for (let i = 0; i < elements.length; i++) {
            const trial = BubbleManager.getTopLevelImageContainerElement(
                elements[i]
            );
            if (trial) {
                return $(trial);
            }
        }
        return $();
    }

    // This method is used both for creating new elements and in dragging/resizing.
    // positionInViewport and rightTopOffset determine where to place the element.
    // If rightTopOffset is falsy, we put the element's top left at positionInViewPort.
    // If rightTopOffset is truthy, it is a string like "10,-20" which are values to
    // add to positionInViewport (which in this case is the mouse position where
    // something was dropped) to get the top right of the visual object that was dropped.
    // Then we position the new element so its top right is at that same point.
    // Note: I wish we could just make this adjustment in the dragEnd event handler
    // which receives both the point and the rightTopOffset data, but it does not
    // have acess to the element being created to get its width. We could push it up
    // one level into finishAddingOverPictureElement, but it's simpler here where we're
    // already extracting and adjusting the offsets from positionInViewport
    private placeElementAtPosition(
        wrapperBox: JQuery,
        container: Element,
        positionInViewport: Point,
        rightTopOffset?: string
    ) {
        const newPoint = BubbleManager.convertPointFromViewportToElementFrame(
            positionInViewport,
            container
        );
        let xOffset = newPoint.getUnscaledX();
        let yOffset = newPoint.getUnscaledY();
        let right = 0;
        let top = 0;
        if (rightTopOffset) {
            const parts = rightTopOffset.split(",");
            right = parseInt(parts[0]);
            top = parseInt(parts[1]);
            // Why -10? I have no idea. But visually most box types seem to come out
            // in the position we want with that correction.
            xOffset = xOffset + right - wrapperBox.width() - 10;
            yOffset = yOffset + top;
        }

        // Note: This code will not clear out the rest of the style properties... they are preserved.
        //       If some or all style properties need to be removed before doing this processing, it is the caller's responsibility to do so beforehand
        //       The reason why we do this is because a bubble's onmousemove handler calls this function,
        //       and in that case we want to preserve the bubble's width/height which are set in the style
        wrapperBox.css("left", xOffset); // assumes numbers are in pixels
        wrapperBox.css("top", yOffset); // assumes numbers are in pixels

        BubbleManager.setTextboxPosition(wrapperBox, xOffset, yOffset);

        this.adjustTarget(wrapperBox.get(0));
    }

    private adjustTarget(draggable: HTMLElement | undefined) {
        if (!draggable) {
            // I think this is just to remove the arrow if any.
            adjustTarget(document.firstElementChild as HTMLElement, undefined);
            return;
        }
        const targetId = draggable.getAttribute("data-bubble-id");
        const target = targetId
            ? document.querySelector(`[data-target-of="${targetId}"]`)
            : undefined;
        adjustTarget(draggable, target as HTMLElement);
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
        this.refreshBubbleEditing(containerElement, undefined, false, false);
        // We no longer have an active element, but the old active element may be
        // needed by the removeControlFrame method called by refreshBubbleEditing
        // to remove a popup menu.
        this.setActiveElement(undefined);
        // By this point it's really gone, so this will clean up if it had a target.
        this.removeDetachedTargets();
    }

    // We verify that 'textElement' is the active element before calling this method.
    public duplicateTOPBox(
        textElement: HTMLElement,
        sameLocation?: boolean
    ): HTMLElement | undefined {
        // simple guard
        if (!textElement || !textElement.parentElement) {
            return undefined;
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

            const result = this.duplicateBubbleFamily(
                patriarchBubble,
                bubbleSpecToDuplicate,
                sameLocation
            );

            // The JQuery resizable event handler needs to be removed after the duplicate bubble
            // family is created, and then the over picture editing needs to be initialized again.
            // See BL-13617.
            this.removeJQueryResizableWidget();
            this.initializeOverPictureEditing();
            return result;
        }
        return undefined;
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
        bubbleSpecToDuplicate: BubbleSpec,
        sameLocation: boolean = false
    ): HTMLElement | undefined {
        const sourceElement = patriarchSourceBubble.content;
        const proposedOffset = 15;
        const newPoint = this.findBestLocationForNewBubble(
            sourceElement,
            sameLocation ? 0 : proposedOffset + sourceElement.clientWidth, // try to not overlap too much
            sameLocation ? 0 : proposedOffset
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
        // Preserve the Auto Height setting.  See BL-13931.
        if (sourceElement.classList.contains("bloom-noAutoHeight"))
            patriarchDuplicateElement.classList.add("bloom-noAutoHeight");

        // copy any data-sound
        const sourceDataSound = sourceElement.getAttribute("data-sound");
        if (sourceDataSound) {
            patriarchDuplicateElement.setAttribute(
                "data-sound",
                sourceDataSound
            );
        }

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
            true,
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
        // Preserve the Auto Height setting.  See BL-13931.
        if (sourceElement.classList.contains("bloom-noAutoHeight"))
            newChildElement.classList.add("bloom-noAutoHeight");

        this.matchSizeOfSource(sourceElement, newChildElement);
        // We just replaced the bloom-editables from the 'addChildInternal' with a clone of the source
        // bubble's HTML. This will undo any event handlers that might have been attached by the
        // refresh triggered by 'addChildInternal'. So we send the newly modified child through again,
        // with 'attachEventsToEditables' set to 'true'.
        this.refreshBubbleEditing(
            BubbleManager.getTopLevelImageContainerElement(parentElement)!,
            new Bubble(newChildElement),
            true,
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

    // Notes that comic editing either has not been suspended...isComicEditingOn might be true or false...
    // or that it was suspended because of a drag in progress that might affect page layout
    // (current example: mouse is down over an origami splitter), or because some longer running
    // process that affects layout is happening (current example: origami layout tool is active),
    // or because we're testing a bloom game.
    // When in one of the latter states, it may be inferred that isComicEditingOn was true when
    // suspendComicEditing was called, that it is now false, and that resumeComicEditing should
    // turn it on again.
    private comicEditingSuspendedState:
        | "none"
        | "forDrag"
        | "forTool"
        | "forGamePlayMode" = "none";

    public suspendComicEditing(
        forWhat: "forDrag" | "forTool" | "forGamePlayMode"
    ) {
        if (!this.isComicEditingOn) {
            // Note that this prevents us from getting into one of the suspended states
            // unless it was on to begin with. Therefore a subsequent resume won't turn
            // it back on unless it was to start with.
            return;
        }
        this.turnOffBubbleEditing();

        if (forWhat === "forGamePlayMode") {
            const allOverPictureElements = Array.from(
                document.getElementsByClassName(kTextOverPictureClass)
            );
            // We don't want the user to be able to edit the text in the bubbles while playing a game.
            // This doesn't need to be in the game prepareActivity because we remove contenteditable
            // from all elements when publishing a book.
            allOverPictureElements.forEach(element => {
                const editables = Array.from(
                    element.getElementsByClassName("bloom-editable")
                );
                editables.forEach(editable => {
                    editable.removeAttribute("contenteditable");
                });
            });
        }
        // We don't want to switch to state 'forDrag' while it is suspended by a tool.
        // But we don't need to prevent it because if it's suspended by a tool (e.g., origami layout),
        // any mouse events will find that comic editing is off and won't get this far.
        this.comicEditingSuspendedState = forWhat;
    }

    public checkActiveElementIsVisible() {
        if (!this.activeElement) {
            return;
        }
        if (window.getComputedStyle(this.activeElement).display === "none") {
            this.setActiveElement(undefined);
        }
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
        if (this.comicEditingSuspendedState === "forGamePlayMode") {
            const allOverPictureElements = Array.from(
                document.getElementsByClassName(kTextOverPictureClass)
            );
            allOverPictureElements.forEach(element => {
                const editables = Array.from(
                    element.getElementsByClassName("bloom-editable")
                );
                editables.forEach(editable => {
                    editable.setAttribute("contenteditable", "true");
                });
            });
            this.setupControlFrame();
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

    public removeDetachedTargets() {
        const detachedTargets = Array.from(
            document.querySelectorAll("[data-target-of]")
        );
        const bubbles = Array.from(
            document.querySelectorAll("[data-bubble-id]")
        );
        bubbles.forEach(bubble => {
            const bubbleId = bubble.getAttribute("data-bubble-id");
            if (bubbleId) {
                const index = detachedTargets.findIndex(
                    (target: Element) =>
                        target.getAttribute("data-target-of") === bubbleId
                );
                if (index > -1) {
                    detachedTargets.splice(index, 1); // not detached if bubble points to it
                }
            }
        });
        detachedTargets.forEach(target => {
            target.remove();
        });
    }

    // on ANY mouse up, if comic editing was turned off by an origami click, turn it back on.
    // (This is attached to the document because I don't want it missed if the mouseUp
    // doesn't happen inside the origami slider.)
    // We don't want it turned back on for a tool or in game play mode, because we'll
    // still be in that state after the mouseup.
    private documentMouseUp = (ev: Event) => {
        if (
            this.comicEditingSuspendedState === "forTool" ||
            this.comicEditingSuspendedState === "forGamePlayMode"
        ) {
            return;
        }
        this.resumeComicEditing();
    };

    public initializeOverPictureEditing(): void {
        // This gets called in bloomEditable's SetupElements method. This is how it gets set up on page
        // load, so that bubble editing works even when the Overlay tool is not active. So it definitely
        // needs to be called there when we're calling SetupElements during page load. It's possible
        // that's the only time it needs to be called from there, but I'm not sure so I'm leaving it
        // called always. However, there's at least one situation where we call SetupElements but do
        // NOT want comic editing turned on: when we're creating an image description translation group
        // in the process of switching to the image description tool. Comic editing is deliberately
        // suspended while that tool is active. For now I'm going with a more-or-less minimal change:
        // if comic editing is not only already initialized, but suspended, we won't turn it on again
        // here.
        if (this.comicEditingSuspendedState !== "none") {
            return;
        }
        // Cleanup old .bloom-ui elements and old drag handles etc.
        // We want to clean these up sooner rather than later so that there's less chance of accidentally blowing away
        // a UI element that we'll actually need now
        // (e.g. the ui-resizable-handles or the format gear, which both have .bloom-ui applied to them)
        this.cleanupOverPictureElements();

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

    private removeJQueryResizableWidget() {
        try {
            const allOverPictureElements = $("body").find(
                kTextOverPictureSelector
            );
            // Removes the resizable functionality completely. This will return the element back to its pre-init state.
            allOverPictureElements.resizable("destroy");
        } catch (e) {
            //console.log(`Error removing resizable widget: ${e}`);
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

        //Slider: if (textBox.get(0).getAttribute("data-txt-img")) {
        //     // Only one of these is ever visible; move them together.
        //     Array.from(
        //         textBox.get(0).ownerDocument.querySelectorAll("[data-txt-img]")
        //     ).forEach((tbox: HTMLElement) => {
        //         tbox.style.left = unscaledRelativeLeft + "px";
        //         tbox.style.top = unscaledRelativeTop + "px";
        //     });
        // }
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
        const secondTry = firstTry.parentElement?.closest(
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

    private static inPlayMode(someElt: Element) {
        return someElt
            .closest(".bloom-page")
            ?.parentElement?.classList.contains("drag-activity-play");
    }

    public deleteBubble(): void {
        // "this" might be a menu item that was clicked.  Calling explicitly again fixes that.  See BL-13928.
        if (this !== theOneBubbleManager) {
            theOneBubbleManager.deleteBubble();
            return;
        }
        const active = this.getActiveElement();
        if (active) {
            this.deleteTOPBox(active);
        }
    }

    public duplicateBubble(): HTMLElement | undefined {
        // "this" might be a menu item that was clicked.  Calling explicitly again fixes that.  See BL-13928.
        if (this !== theOneBubbleManager) {
            return theOneBubbleManager.duplicateBubble();
        }
        const active = this.getActiveElement();
        if (active) {
            return this.duplicateTOPBox(active);
        }
        return undefined;
    }

    public addChildBubble(): void {
        // "this" might be a menu item that was clicked.  Calling explicitly again fixes that.  See BL-13928.
        if (this !== theOneBubbleManager) {
            theOneBubbleManager.addChildBubble();
            return;
        }
        const parentElement = this.getActiveElement();
        if (!parentElement) {
            // No parent to attach to
            toastr.info("No element is currently active.");
            return;
        }

        // Enhance: Is there a cleaner way to keep activeBubbleSpec up to date?
        // Comical would need to call the notifier a lot more often like when the tail moves.

        // Retrieve the latest bubbleSpec
        const bubbleSpec = this.getSelectedItemBubbleSpec();
        const [
            offsetX,
            offsetY
        ] = BubbleManager.GetChildPositionFromParentBubble(
            parentElement,
            bubbleSpec
        );
        this.addChildOverPictureElementAndRefreshPage(
            parentElement,
            offsetX,
            offsetY
        );
    }

    // Returns a 2-tuple containing the desired x and y offsets of the child bubble from the parent bubble
    //   (i.e., offsetX = child.left - parent.left)
    public static GetChildPositionFromParentBubble(
        parentElement: HTMLElement,
        parentBubbleSpec: BubbleSpec | undefined
    ): number[] {
        let offsetX = parentElement.clientWidth;
        let offsetY = parentElement.clientHeight;

        if (
            parentBubbleSpec &&
            parentBubbleSpec.tails &&
            parentBubbleSpec.tails.length > 0
        ) {
            const tail = parentBubbleSpec.tails[0];

            const bubbleCenterX =
                parentElement.offsetLeft + parentElement.clientWidth / 2.0;
            const bubbleCenterY =
                parentElement.offsetTop + parentElement.clientHeight / 2.0;

            const deltaX = tail.tipX - bubbleCenterX;
            const deltaY = tail.tipY - bubbleCenterY;

            // Place the new child in the opposite quandrant of the tail
            if (deltaX > 0) {
                // ENHANCE: SHould be the child's width
                offsetX = -parentElement.clientWidth;
            } else {
                offsetX = parentElement.clientWidth;
            }

            if (deltaY > 0) {
                // ENHANCE: SHould be the child's height
                offsetY = -parentElement.clientHeight;
            } else {
                offsetY = parentElement.clientHeight;
            }
        }

        return [offsetX, offsetY];
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

// Note: do NOT use this directly in toolbox code; it will import its own copy of
// bubbleManager and not use the proper one from the page iframe. Instead, use
// the OverlayTool.bubbleManager().
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

// This is just for debugging. It produces a string that describes the bubble, generally
// well enough to identify it in console.log.
export function bubbleDescription(e: Element | null | undefined): string {
    const elt = e as HTMLElement;
    if (!elt) {
        return "no bubble";
    }
    const result = "bubble at (" + elt.style.left + ", " + elt.style.top + ") ";
    const imageContainer = elt.getElementsByClassName(
        "bloom-imageContainer"
    )[0];
    if (imageContainer) {
        const img = imageContainer.getElementsByTagName("img")[0];
        if (img) {
            return result + "with image : " + img.getAttribute("src");
        }
    }
    // Enhance: look for videoContainer similarly
    else {
        return result + "with text " + elt.innerText;
    }
    return result;
}
