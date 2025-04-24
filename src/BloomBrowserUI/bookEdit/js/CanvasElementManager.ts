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
    EnableAllImageEditing,
    getImageFromCanvasElement,
    kImageContainerSelector,
    getImageFromContainer,
    kImageContainerClass,
    getBackgroundImageFromBloomCanvas,
    SetupMetadataButton,
    UpdateImageTooltipVisibility,
    HandleImageError,
    kBloomCanvasSelector,
    kBloomCanvasClass
} from "./bloomImages";
import { adjustTarget } from "../toolbox/games/GameTool";
import BloomSourceBubbles from "../sourceBubbles/BloomSourceBubbles";
import BloomHintBubbles from "./BloomHintBubbles";
import { renderCanvasElementContextControls } from "./CanvasElementContextControls";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    kCanvasElementClass,
    kCanvasElementSelector,
    kHasCanvasElementClass
} from "../toolbox/overlay/canvasElementUtils";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { handlePlayClick } from "./bloomVideo";
import { kVideoContainerClass, selectVideoContainer } from "./videoUtils";
import { needsToBeKeptSameSize } from "../toolbox/games/gameUtilities";
import { CanvasElementType } from "../toolbox/overlay/CanvasElementItem";
import { getTarget } from "bloom-player";

export interface ITextColorInfo {
    color: string;
    isDefault: boolean;
}

const kComicalGeneratedClass: string = "comical-generated";
// In the process of moving these two definitions to overlayUtils.ts, but duplicating here for now.
const kTransformPropName = "bloom-zoomTransformForInitialFocus";
export const kBackgroundImageClass = "bloom-backgroundImage"; // split-pane.js and editMode.less know about this too

type ResizeDirection = "ne" | "nw" | "sw" | "se";

// Canvas elements are the movable items that can be placed over images (or empty image containers).
// Some of them are associated with ComicalJs bubbles. Earlier in Bloom's history, they were variously
// called TextOverPicture boxes, TOPs, Overlays, OverPictures, and Bubbles. We have attempted to clean up all such
// names, but it is difficult, as "top" is a common CSS property, many other things are called overlays,
// and "bubble" is used in reference to ComicalJs, Source Bubbles, Hint Bubbles, and other qtips.
// Some may have been missed. (It's even conceivable that some references to the other things were
// accidentally renamed to "canvas element".)
export class CanvasElementManager {
    // The min width/height needs to be kept in sync with the corresponding values in overlayTool.less
    public minTextBoxWidthPx = 30;
    public minTextBoxHeightPx = 30;

    private activeElement: HTMLElement | undefined;
    public isCanvasElementEditingOn: boolean = false;
    private thingsToNotifyOfCanvasElementChange: {
        // identifies the source that requested the notification; allows us to remove the
        // right one when no longer needed, and prevent multiple notifiers to the same client.
        id: string;
        handler: (x: BubbleSpec | undefined) => void;
    }[] = [];

    // These variables are used by the canvas element's onmouse* event handlers
    private bubbleToDrag: Bubble | undefined; // Use Undefined to indicate that there is no active drag in progress
    private bubbleDragGrabOffset: { x: number; y: number } = {
        x: 0,
        y: 0
    };

    public initializeCanvasElementManager(): void {
        Comical.setSelectorForBubblesWhichTailMidpointMayOverlap(
            ".bloom-backgroundImage"
        );
    }

    public getIsCanvasElementEditingOn(): boolean {
        return this.isCanvasElementEditingOn;
    }

    // Given the editable has been determined to be overflowing vertically by
    // 'overflowY' pixels, if it's inside a canvas element that does not have the class
    // bloom-noAutoSize (or one of several other disclaimers you'll find in the code below),
    // adjust the size of the canvas element to fit it.
    // (We also call editable.scrollTop = 0 to make sure the whole content shows now there
    // is room for it all.)
    // Returns 0 if totally successful, with the editable adjusted to the desired height; if nothing can be
    // done, it will return the input overflowY value.
    // If doNotShrink is true and overflowY is negative, it will not shrink the editable and will return the
    // original overflowY value.
    // If growAsMuchAsPossible is false, and there is not enough room to grow the editable, it will return the
    // original overflowY value without changing the box.  If growAsMuchAsPossible is true, it will grow
    // the editable as much as possible and return the amount of positive overflow that remains.  See BL-14632.
    public adjustSizeOfContainingCanvasElementToMatchContent(
        editable: HTMLElement,
        overflowY: number,
        doNotShrink?: boolean,
        growAsMuchAsPossible?: boolean
    ): number {
        if (editable instanceof HTMLTextAreaElement) {
            // Calendars still use textareas, but we don't do anything with them here.
            return overflowY;
        }

        console.assert(
            editable.classList.contains("bloom-editable"),
            "editable is not a bloom-editable"
        );

        const canvasElement = editable.closest(
            kCanvasElementSelector
        ) as HTMLElement;
        if (
            !canvasElement ||
            canvasElement.classList.contains("bloom-noAutoHeight")
        ) {
            return overflowY; // we can't fix it
        }
        if (doNotShrink && overflowY < 0) {
            return overflowY; // we don't want to change the box's size
        }

        const bloomCanvas = CanvasElementManager.getBloomCanvas(canvasElement);
        if (!bloomCanvas) {
            return overflowY; // paranoia; canvas element should always be in bloom-canvas
        }

        // The +4 is based on experiment. It may relate to a couple of 'fudge factors'
        // in OverflowChecker.getSelfOverflowAmounts, which I don't want to mess with
        // as a lot of work went into getting overflow reporting right. We seem to
        // need a bit of extra space to make sure the last line of text fits.
        // The 27 is the minimumSize that CSS imposes on canvas elements; it may cause
        // Comical some problems if we try to set the actual size smaller.
        // (I think I saw background gradients behaving strangely, for example.)
        let newHeight = Math.max(editable.clientHeight + overflowY + 4, 27);

        newHeight = Math.max(
            newHeight,
            this.getMaxVisibleSiblingHeight(editable) ?? 0
        );

        if (
            newHeight < canvasElement.clientHeight &&
            newHeight > canvasElement.clientHeight - 4
        ) {
            return overflowY; // near enough, avoid jitter making it a tiny bit smaller.
        }
        if (
            newHeight < canvasElement.clientHeight &&
            needsToBeKeptSameSize(canvasElement)
        ) {
            // Shrinking might cause other boxes in the group to overflow.
            // for now we just don't do it.
            return overflowY;
        }

        // If a lot of text is pasted, the bloom-canvas will scroll down.
        // (This can happen even if the text doesn't necessarily go out the bottom of the bloom-canvas).
        // The children of the bloom-canvas (e.g. img and canvas elements) will be offset above the bloom-canvas.
        // This is an annoying situation, both visually for the image and in terms of computing the correct position for JQuery draggables.
        // So instead, we force the container to scroll back to the top.
        bloomCanvas.scrollTop = 0;

        // Check if required height exceeds available height
        if (newHeight + canvasElement.offsetTop > bloomCanvas.clientHeight) {
            if (growAsMuchAsPossible) {
                // If we are allowed to grow as much as possible, we can set the height to the max available height.
                newHeight = bloomCanvas.clientHeight - canvasElement.offsetTop;
            } else {
                return overflowY;
            }
        }

        canvasElement.style.height = newHeight + "px";
        // The next method call will change from % positioning to px if needed.  Bloom originally
        // used % values to position canvas elements before we realized that was a bad idea.
        CanvasElementManager.convertCanvasElementPositionToAbsolute(
            canvasElement,
            bloomCanvas
        );
        this.adjustTarget(canvasElement);
        this.alignControlFrameWithActiveElement();
        return 0; // success; we fixed it
    }

    private getMaxVisibleSiblingHeight(
        editable: HTMLElement
    ): number | undefined {
        // Get any siblings of our editable that are also visible. (Typically siblings are the
        // other bloom-editables in the same bloom-translationGroup, and are all display:none.)
        const visibleSiblings = Array.from(
            editable.parentElement!.children
        ).filter(child => {
            if (child === editable) return false; // skip the element itself
            const computedStyle = window.getComputedStyle(child);
            return (
                computedStyle.display !== "none" &&
                computedStyle.visibility !== "hidden"
            );
        });
        if (visibleSiblings.length > 0) {
            // This is very rare. As of March 2025, the only known case is in Games, where we sometimes
            // make the English of a prompt visible until the desired language is typed. When it happens,
            // we'll make sure the canvas element is at least high enough to show the tallest sibling, but without
            // using the precision we do for just one child.
            // More care might be needed if the parent might show a format cog or language label (even as :after)...
            // anything bottom-aligned will interfere with shrinking. Currently we don't do anything like that
            // in canvas elements.
            return Math.max(
                ...visibleSiblings.map(
                    child => child.clientTop + child.clientHeight
                )
            );
        }
        return undefined;
    }

    public updateAutoHeight(): void {
        if (
            this.activeElement &&
            !this.activeElement.classList.contains("bloom-noAutoHeight")
        ) {
            const editable = this.activeElement.getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )[0] as HTMLElement;

            this.adjustCanvasElementHeightToContentOrMarkOverflow(editable);
        }
        this.alignControlFrameWithActiveElement();
    }

    public adjustCanvasElementHeightToContentOrMarkOverflow(
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
        overflowY = theOneCanvasElementManager.adjustSizeOfContainingCanvasElementToMatchContent(
            editable,
            overflowY
        );
        editable.classList.toggle("overflow", overflowY > 0);
    }

    // When the format dialog changes the amount of padding for canvas elements, adjust their sizes
    // and positions (keeping the text in the same place).
    // This function assumes that the position and size of canvas elements are determined by the
    // top, left, width, and height properties of the canvas elements,
    // and that they are measured in pixels.
    public static adjustCanvasElementsForPaddingChange(
        container: HTMLElement,
        style: string,
        oldPaddingStr: string, // number+px
        newPaddingStr: string // number+px
    ) {
        const wrapperBoxes = Array.from(
            container.getElementsByClassName(kCanvasElementClass)
        ) as HTMLElement[];
        const oldPadding = CanvasElementManager.pxToNumber(oldPaddingStr);
        const newPadding = CanvasElementManager.pxToNumber(newPaddingStr);
        const delta = newPadding - oldPadding;
        const canvasElementLang = GetSettings().languageForNewTextBoxes;
        wrapperBoxes.forEach(wrapperBox => {
            // The language check is a belt-and-braces thing. At the time I did this PR, we had a bug where
            // the bloom-editables in a TG did not necessarily all have the same style.
            // We could possibly enconuter books where this is still true.
            if (
                Array.from(wrapperBox.getElementsByClassName(style)).filter(
                    x => x.getAttribute("lang") === canvasElementLang
                ).length > 0
            ) {
                if (!wrapperBox.style.height.endsWith("px")) {
                    // Some sort of legacy situation; for a while we had all the placements as percentages.
                    // This will typically not move it, but will force it to the new system of placement
                    // by pixel. Don't want to do this if we don't have to, because there could be rounding
                    // errors that would move it very slightly.
                    this.setCanvasElementPosition(
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
            }
        });
    }

    // Convert string ending in pixels to a number
    public static pxToNumber(px: string): number {
        if (!px) return 0;
        return parseFloat(px.replace("px", ""));
    }

    // A visible, editable div is generally focusable, but sometimes (e.g. in Bloom games),
    // we may disable it by turning off pointer events. So we filter those ones out.
    private getAllVisibleFocusableDivs(bloomCanvas: HTMLElement): Element[] {
        return this.getAllVisibileEditableDivs(bloomCanvas).filter(
            focusElement =>
                window.getComputedStyle(focusElement).pointerEvents !== "none"
        );
    }

    private getAllVisibileEditableDivs(bloomCanvas: HTMLElement): Element[] {
        // If the Over Picture element has visible bloom-editables, we want them.
        // Otherwise, look for video and image elements. At this point, an over picture element
        // can only have one of three types of content and each are mutually exclusive.
        // bloom-editable or bloom-videoContainer or bloom-imageContainer. It doesn't even really
        // matter which order we look for them.
        const editables = Array.from(
            bloomCanvas.getElementsByClassName(
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
                bloomCanvas.getElementsByClassName(kVideoContainerClass)
            ).filter(x => !EditableDivUtils.isInHiddenLanguageBlock(x));
        }
        if (focusableDivs.length === 0) {
            focusableDivs = Array.from(
                bloomCanvas.getElementsByClassName(kImageContainerClass)
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

    public turnOnCanvasElementEditing(): void {
        if (this.isCanvasElementEditingOn === true) {
            return; // Already on. No work needs to be done
        }
        this.isCanvasElementEditingOn = true;
        this.handleResizeAdjustments();

        const bloomCanvases: HTMLElement[] = this.getAllBloomCanvasesOnPage();

        bloomCanvases.forEach(bloomCanvas => {
            this.adjustCanvasElementsForCurrentLanguage(bloomCanvas);
            this.ensureCanvasElementsIntersectParent(bloomCanvas);
            // image containers are already set by CSS to overflow:hidden, so they
            // SHOULD never scroll. But there's also a rule that when something is
            // focused, it has to be scrolled to. If we set focus to a canvas element that's
            // sufficiently (almost entirely?) off-screen, the browser decides that
            // it MUST scroll to show it. For a reason I haven't determined, the
            // element it picks to scroll seems to be the bloom-canvas. This puts
            // the display in a confusing state where the text that should be hidden
            // is visible, though the canvas has moved over and most of the canvas element
            // is still hidden (BL-11646).
            // Another solution would be to find the code that is focusing the
            // canvas element after page load, and give it the option {preventScroll: true}.
            // But (a) this is not supported in Gecko (added in FF68), and (b) you
            // can get a similar bad effect by moving the cursor through text that
            // is supposed to be hidden. This drastic approach prevents both.
            // We're basically saying, if this element scrolls its content for
            // any reason, undo it.
            bloomCanvas.addEventListener("scroll", () => {
                bloomCanvas.scrollLeft = 0;
                bloomCanvas.scrollTop = 0;
            });
        });

        // todo: select the right one...in particular, currently we just select the last one.
        // This is reasonable when just coming to the page, and when we add a new canvas element,
        // we make the new one the last in its parent, so with only one bloom-canvas
        // the new one gets selected after we refresh. However, once we have more than one
        // bloom-canvas, I don't think the new canvas element will get selected if it's not on
        // the first bloom-canvas.
        // todo: make sure comical is turned on for the right parent, in case there's more than one
        // bloom-canvas on the page?
        const canvasElements = Array.from(
            document.getElementsByClassName(kCanvasElementClass)
        ).filter(
            x => !EditableDivUtils.isInHiddenLanguageBlock(x)
        ) as HTMLElement[];
        if (canvasElements.length > 0) {
            // If we have no activeElement, or it's not in the list...deleted or belongs to
            // another page, perhaps...pick an arbitrary one.
            if (
                !this.activeElement ||
                canvasElements.indexOf(this.activeElement) === -1
            ) {
                this.activeElement = canvasElements[
                    canvasElements.length - 1
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
            Comical.startEditing(bloomCanvases);
            this.migrateOldCanvasElements(canvasElements);
            Comical.activateElement(this.activeElement);
            canvasElements.forEach(container => {
                this.addEventsToFocusableElements(container, false);
            });
            document.addEventListener(
                "click",
                CanvasElementManager.onDocClickClearActiveElement
            );
            // If we have sign language video over picture elements that are so far only placeholders,
            // they are not focusable by default and so won't get the blue border that elements
            // are supposed to have when selected. So we add tabindex="0" so they become focusable.
            canvasElements.forEach(element => {
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
            // BL-8073: if Comic Tool is open, this 'turnOnCanvasElementEditing()' method will get run.
            // If this particular page has no canvas elements, we can actually arrive here with the 'body'
            // as the document's activeElement. So we focus the first visible focusable element
            // we come to.
            const marginBox = document.getElementsByClassName("marginBox");
            if (marginBox.length > 0) {
                this.focusFirstVisibleFocusable(marginBox[0] as HTMLElement);
            }
        }

        // turn on various behaviors for each image
        Array.from(this.getAllBloomCanvasesOnPage()).forEach(
            (bloomCanvas: HTMLElement) => {
                bloomCanvas.addEventListener("click", event => {
                    // The goal here is that if the user clicks outside any comical canvas element,
                    // we want none of the canvas elements selected, so that
                    // (after moving the mouse away to get rid of hover effects)
                    // the user can see exactly what the final comic will look like.
                    // This is a difficult and horrible kludge.
                    // First problem is that this click handler is fired for a click
                    // ANYWHERE in the image...none of the canvas element-related
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
                        // some part of a canvas element rather than away from it.
                        // We now use a Comical function to determine whether we clicked
                        // on a Comical object.
                        const x = event.offsetX;
                        const y = event.offsetY;
                        if (!Comical.somethingHit(bloomCanvas, x, y)) {
                            // If we click on the background of the bloom-canvas, we
                            // don't want anything to have focus. This prevents any source
                            // bubbles interfering with seeing the full content of the
                            // bloom-canvas. BL-14295.
                            this.removeFocus();
                        }
                    }
                });

                this.setDragAndDropHandlers(bloomCanvas);
                this.setMouseDragHandlers(bloomCanvas);
            }
        );
    }
    removeFocus() {
        if (document.activeElement) {
            (document.activeElement as HTMLElement)?.blur();
        }
    }
    // declare this strange way so it has the right 'this' when added as event listener.
    private canvasElementLosingFocus = event => {
        if (CanvasElementManager.ignoreFocusChanges) return;
        // removing focus from a text canvas element means the next click on it could drag it.
        // However, it's possible the active canvas element already moved; don't clear theCanvasElementWeAreTextEditing if so
        if (event.currentTarget === this.theCanvasElementWeAreTextEditing) {
            this.theCanvasElementWeAreTextEditing = undefined;
            this.removeFocusClass();
        }
    };

    // This is not a great place to make this available to the world.
    // But GetSettings only works in the page Iframe, and the canvas element manager
    // is one componenent from there that the Game code already works with
    // and that already uses the injected GetSettings(). I don't have a better idea,
    // short of refactoring so that we get settings from an API call rather than
    // by injection. But that may involve making a lot of stuff async.
    public getSettings(): ICollectionSettings {
        return GetSettings();
    }

    // This is invoked when the toolbox adds a canvas element that wants source and/or hint bubbles.
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

    // if there is a bloom-editable in the canvas element that has a data-bubble-alternate,
    // use it to set the data-bubble of the canvas element. (data-bubble is used by Comical-js,
    // which is continuing to use the term bubble, so I think it's appropriate to still use that
    // name here.)
    adjustCanvasElementsForCurrentLanguage(container: HTMLElement) {
        const canvasElementLang = GetSettings().languageForNewTextBoxes;
        Array.from(
            container.getElementsByClassName(kCanvasElementClass)
        ).forEach(canvasElement => {
            const editable = Array.from(
                canvasElement.getElementsByClassName("bloom-editable")
            ).find(e => e.getAttribute("lang") === canvasElementLang);
            if (editable) {
                const alternatesString = editable.getAttribute(
                    "data-bubble-alternate"
                );
                if (alternatesString) {
                    const alternate = JSON.parse(
                        alternatesString.replace(/`/g, '"')
                    ) as IAlternate;
                    canvasElement.setAttribute("style", alternate.style);
                    const bubbleData = canvasElement.getAttribute(
                        "data-bubble"
                    );
                    if (bubbleData) {
                        const bubbleDataObj = JSON.parse(
                            bubbleData.replace(/`/g, '"')
                        );
                        bubbleDataObj.tails = alternate.tails;
                        const newBubbleData = JSON.stringify(
                            bubbleDataObj
                        ).replace(/"/g, "`");
                        canvasElement.setAttribute(
                            "data-bubble",
                            newBubbleData
                        );
                    }
                }
            }

            // If we don't find a matching bloom-editable, or there is no alternate attribute
            // there, that's fine; just let the current state of the data-bubble serve as a
            // default for the new language.
        });
        // If we have an existing alternate SVG for this language, remove it.
        // (It will effectively be replaced by the new active comical-generated svg
        // made when we save the page.)
        const altSvg = Array.from(
            container.getElementsByClassName("comical-alternate")
        ).find(svg => svg.getAttribute("data-lang") === canvasElementLang);
        if (altSvg) {
            container.removeChild(altSvg);
        }

        const currentSvg = container.getElementsByClassName(
            "comical-generated"
        )[0];
        if (currentSvg) {
            const currentSvgLang = currentSvg.getAttribute("data-lang");
            if (currentSvgLang && currentSvgLang !== canvasElementLang) {
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

    public static saveStateOfCanvasElementAsCurrentLangAlternate(
        canvasElement: HTMLElement,
        canvasElementLangIn?: string
    ) {
        const canvasElementLang =
            canvasElementLangIn ?? GetSettings().languageForNewTextBoxes;

        const editable = Array.from(
            canvasElement.getElementsByClassName("bloom-editable")
        ).find(e => e.getAttribute("lang") === canvasElementLang);
        if (editable) {
            const bubbleData = canvasElement.getAttribute("data-bubble") ?? "";
            const bubbleDataObj = JSON.parse(bubbleData.replace(/`/g, '"'));
            const alternate = {
                lang: canvasElementLang,
                style: canvasElement.getAttribute("style") ?? "",
                tails: bubbleDataObj.tails as object[]
            };
            editable.setAttribute(
                "data-bubble-alternate",
                JSON.stringify(alternate).replace(/"/g, "`")
            );
        }
    }

    // Save the current state of things so that we can later position everything
    // correctly for this language, even if in the meantime we change canvas element
    // positions for other languages.
    saveCurrentCanvasElementStateAsCurrentLangAlternate(
        container: HTMLElement
    ) {
        const canvasElementLang = GetSettings().languageForNewTextBoxes;
        Array.from(
            container.getElementsByClassName(kCanvasElementClass)
        ).forEach((top: HTMLElement) =>
            CanvasElementManager.saveStateOfCanvasElementAsCurrentLangAlternate(
                top,
                canvasElementLang
            )
        );
        // Record that the current comical-generated SVG is for this language.
        const currentSvg = container.getElementsByClassName(
            "comical-generated"
        )[0];
        currentSvg?.setAttribute("data-lang", canvasElementLang);
    }

    // "container" refers to a .bloom-canvas-element div, which holds one (and only one) of the
    // 3 main types of canvas element: text, video or image.
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
            document.getElementsByClassName(kCanvasElementClass)
        ).forEach((element: HTMLElement) => {
            element.addEventListener("focusout", this.canvasElementLosingFocus);
        });
    }

    private handleFocusInEvent(ev: FocusEvent) {
        CanvasElementManager.onFocusSetActiveElement(ev);
    }

    public getAllBloomCanvasesOnPage() {
        return Array.from(
            document.getElementsByClassName(kBloomCanvasClass)
        ) as Array<HTMLElement>;
    }

    // Use this one when adding/duplicating a canvas element to avoid re-navigating the page.
    // If we are passing "undefined" as the canvas element, it's because we just deleted a canvas element
    // and we want Bloom to determine what to select next (it might not be a canvas element at all).
    public refreshCanvasElementEditing(
        bloomCanvas: HTMLElement,
        bubble: Bubble | undefined,
        attachEventsToEditables: boolean,
        activateCanvasElement: boolean
    ): void {
        Comical.startEditing([bloomCanvas]);
        // necessary if we added the very first canvas element, and Comical was not previously initialized
        Comical.setUserInterfaceProperties({ tailHandleColor: kBloomBlue });
        if (bubble) {
            const newCanvasElement = bubble.content;
            if (activateCanvasElement) {
                Comical.activateBubble(bubble);
            }
            this.updateComicalForSelectedElement(newCanvasElement);

            // SetupElements (below) will do most of what we need, but when it gets to
            // 'turnOnCanvasElementEditing()', it's already on, so the method will get skipped.
            // The only piece left from that method that still needs doing is to set the
            // 'focusin' eventlistener.
            // And then the only thing left from a full refresh that needs to happen here is
            // to attach the new bloom-editable to ckEditor.
            // If attachEventsToEditables is false, then this is a child or duplicate canvas element that
            // was already sent through here once. We don't need to add more 'focusin' listeners and
            // re-attach to the StyleEditor again.
            // This must be done before we call SetupElements, which will attempt to focus the new
            // canvas element, and expects the focus event handler to get called.
            if (attachEventsToEditables) {
                this.addEventsToFocusableElements(
                    newCanvasElement,
                    attachEventsToEditables
                );
            }
            SetupElements(
                bloomCanvas,
                activateCanvasElement ? bubble.content : "none"
            );

            // Since we may have just added an element, check if the container has at least one
            // canvas element and add the 'bloom-has-canvas-element' class.
            updateCanvasElementClass(bloomCanvas);
        } else {
            // deleted a canvas element. Don't try to focus anything.
            this.removeControlFrame(); // but don't leave this behind.

            // Also, since we just deleted an element, check if the original container no longer
            // has any canvas elements and remove the 'bloom-has-canvas-element' class.
            updateCanvasElementClass(bloomCanvas);
        }
    }

    private migrateOldCanvasElements(canvasElements: HTMLElement[]): void {
        canvasElements.forEach(top => {
            if (!top.getAttribute("data-bubble")) {
                const bubbleSpec = Bubble.getDefaultBubbleSpec(top, "none");
                new Bubble(top).setBubbleSpec(bubbleSpec);
                // it would be nice to do this only once, but there MIGHT
                // be canvas elements in more than one bloom canvas...too complicated,
                // and this only happens once per canvas element.
                Comical.update(CanvasElementManager.getBloomCanvas(top)!);
            }
        });
    }

    // If we haven't already, note (in a variable of the top window) the initial zoom level.
    // This is used by a hack in onFocusSetActiveElement.
    public static recordInitialZoom(container: HTMLElement) {
        const zoomTransform = container.ownerDocument.getElementById(
            "page-scaling-container"
        )?.style.transform;
        const topWindowZoomTransfrom = window.top?.[kTransformPropName];
        if (window.top && zoomTransform && !topWindowZoomTransfrom) {
            window.top[kTransformPropName] = zoomTransform;
        }
    }

    // The event handler to be called when something relevant on the page frame gets focus.
    // This will set the active canvas element.
    public static onFocusSetActiveElement(event: FocusEvent) {
        if (CanvasElementManager.ignoreFocusChanges) return;
        // The following is the only fix I've found after a lot of experimentation
        // to prevent the active canvas element changing when we choose a menu command that
        // brings up a dialog, at least a C# dialog.
        if (CanvasElementManager.skipNextFocusChange) {
            CanvasElementManager.skipNextFocusChange = false;
            return;
        }
        if (CanvasElementManager.inPlayMode(event.currentTarget as Element)) {
            return;
        }

        // The current target is the element we attached the event listener to
        const focusedElement = event.currentTarget as Element;

        // This is a hack to prevent the active canvas element changing when we change zoom level.
        // For some reason I can't track down, the first focusable thing on the page is
        // given focus during the reload after a zoom change. I think somehow the
        // browser itself is trying to focus something, and it's not the thing we want.
        // We have mechanisms to focus what we do want, so we use this trick to ignore
        // focus events immediately after a zoom change.
        const zoomTransform = focusedElement.ownerDocument.getElementById(
            "page-scaling-container"
        )?.style.transform;
        const topWindowZoomTransfrom = window.top?.[kTransformPropName];
        if (window.top && zoomTransform !== topWindowZoomTransfrom) {
            // We eventually want to reset the saved zoom level to the new one, so
            // that this method can do its job...mainly allowing the user to tab between canvas elements.
            // We don't do it immediately because experience indicates that there may be more than
            // one focus event to suppress as we load the page. On my fast dev machine a 50ms
            // delay is enough to catch them all, so I'm going with ten times that. It's not
            // a catastrophe if we miss a tab key very soon after a zoom change, nor if the delay
            // is not enough for a very slow machine and so the active canvas element moves when it shouldn't.
            setTimeout(() => {
                if (window.top) {
                    window.top[kTransformPropName] = zoomTransform;
                }
            }, 500);
            return;
        }

        // If we focus something on the page that isn't in a canvas element, we need to switch
        // to having no active canvas element  Note: we don't want to use focusout
        // on the canvas elements, because then we lose the active element while clicking
        // on controls in the toolbox (and while debugging).

        // We don't think this function ever gets called when it's not initialized, but it doesn't
        // hurt to make sure.
        initializeCanvasElementManager();

        const canvasElement = focusedElement.closest(kCanvasElementSelector);
        if (canvasElement) {
            theOneCanvasElementManager.setActiveElement(
                canvasElement as HTMLElement
            );
            // When a canvas element is first clicked, we try hard not to let it get focus.
            // Another click will focus it. Unfortunately, various other things do as well,
            // such as activating Bloom (which seems to focus the thing that most recently had
            // a text selection, possibly because of CkEditor), and Undo. If something
            // has focused the canvas element, it will typically have a selection visible, and so it
            // looks as if it's in edit mode. I think it's best to just make it so.)
            theOneCanvasElementManager.theCanvasElementWeAreTextEditing =
                theOneCanvasElementManager.activeElement;
            theOneCanvasElementManager.theCanvasElementWeAreTextEditing?.classList.add(
                "bloom-focusedCanvasElement"
            );
        } else {
            theOneCanvasElementManager.setActiveElement(undefined);
        }
    }

    private static onDocClickClearActiveElement(event: Event) {
        const clickedElement = event.target as Element; // most local thing clicked on
        if (!clickedElement.closest) {
            // About the only other possibility is that it's the top-level document.
            // If that's the target, we didn't click in a bloom-canvas or button.
            return;
        }
        if (clickedElement.classList.contains("MuiBackdrop-root")) {
            return; // we clicked outside a popup menu to close it. Don't mess with focus.
        }
        if (
            CanvasElementManager.getBloomCanvas(clickedElement) ||
            clickedElement.closest(".source-copy-button")
        ) {
            // We have other code to handle setting and clearing Comical handles
            // if the click is inside a Comical area.
            // BL-9198 We also have code (in BloomSourceBubbles) to handle clicks on source bubble
            // copy buttons.
            return;
        }
        if (
            clickedElement.closest("#canvas-element-control-frame") ||
            clickedElement.closest("#canvas-element-context-controls") ||
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
        theOneCanvasElementManager.setActiveElement(undefined);
    }

    public getActiveElement() {
        return this.activeElement;
    }

    // In drag-word-chooser-slider game, there are image canvas element boxes with data-img-txt attributes
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
        Array.from(
            document.getElementsByClassName("bloom-focusedCanvasElement")
        ).forEach(element => {
            element.classList.remove("bloom-focusedCanvasElement");
        });
    }

    // Some controls, such as MUI menus, temporarily steal focus. We don't want the usual
    // loss-of-focus behavior, so this allows suppressing it.
    public static ignoreFocusChanges: boolean;
    // If the menu command brings up a dialog, we still don't want the active bubble to
    // change. This flag allows us to ignore the next focus change.  See BL-14123.
    public static skipNextFocusChange: boolean;

    public setActiveElement(element: HTMLElement | undefined) {
        // Seems it should be sufficient to remove this from the old active element if any.
        // But there's at least one case where code that adds a new canvas element sets it as
        // this.activeElement before calling this method. It's safest to make sure this
        // attribute is not set on any other element.
        document.querySelectorAll("[data-bloom-active]").forEach(e => {
            if (e !== element) {
                e.removeAttribute("data-bloom-active");
            }
        });
        if (this.activeElement !== element) {
            this.theCanvasElementWeAreTextEditing = undefined; // even if focus doesn't move.
            // For some reason this doesnt' trigger as a result of changing the selection.
            // But we definitely don't want to show the CkEditor toolbar until there is some
            // new range selection, so just set up the usual class to hide it.
            document.body.classList.add("hideAllCKEditors");
            const focusNode = window.getSelection()?.focusNode;
            if (
                focusNode &&
                this.activeElement &&
                this.activeElement.contains(focusNode as Node)
            ) {
                // clear any text selection that is part of the previously selected canvas element.
                // (but, we don't want to remove a selection we may just have made by
                // clicking in a text block that is not a canvas element)
                window.getSelection()?.removeAllRanges();
            }
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
            // We should call this if there is an active element, even if it is not a video,
            // because it will turn off the 'active video' class that might be on some
            // non-canvas element video.
            // But if there is no active element we should not, because we might be wanting to
            // record a non-canvas element video and wanting to show that one as active.
            // Indeed, we might have been called from the code that makes that so.
            selectVideoContainer(
                this.activeElement.getElementsByClassName(
                    "bloom-videoContainer"
                )[0] as HTMLElement,
                false
            );
            // if the active element isn't a text one, we don't want anything to have focus.
            // One reason is that the thing that has focus may display a source bubble that
            // hides what we're trying to work on.
            // (If we one day try to make Bloom fully accessible, we may have to instead allow
            // non-text elements to have focus so that keyboard commands can be applied to them.)
            if (
                this.activeElement.getElementsByClassName(
                    "bloom-visibility-code-on"
                ).length === 0
            ) {
                this.removeFocus();
            }
        }
        UpdateImageTooltipVisibility(
            this.activeElement?.closest(kBloomCanvasSelector)
        );
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
    // The original size and position of the main img inside a canvas element being resized or cropped
    private oldImageWidth: number;
    private oldImageLeft: number;
    private oldImageTop: number;
    // during a resize drag, keeps track of which corner we're dragging
    private resizeDragCorner: "ne" | "nw" | "se" | "sw" | undefined;

    // Keeps track of whether the mouse was moved during a mouse event in the main content of a
    // canvas element. If so, we interpret it as a drag, moving the canvas element. If not, we interpret it as a click.
    private gotAMoveWhileMouseDown: boolean = false;

    // Remove the canvas element control frame if it exists (when no canvas element is active)
    // Also remove the menu if it's still open.  See BL-13852.
    removeControlFrame() {
        // this.activeElement is still set and works for hiding the menu.
        const eltWithControlOnIt = this.activeElement;
        const controlFrame = document.getElementById(
            "canvas-element-control-frame"
        );
        if (controlFrame) {
            if (eltWithControlOnIt) {
                // we're going to remove the container of the canvas element context controls,
                // but it seems best to let React clean up after itself.
                // For example, there may be a context menu popup to remove, too.
                renderCanvasElementContextControls(eltWithControlOnIt, false);
            }
            // Reschedule so that the rerender can finish before removing the control frame.
            setTimeout(() => {
                controlFrame.remove();
                document
                    .getElementById("canvas-element-context-controls")
                    ?.remove();
            }, 0);
        }
    }

    // Set up the control frame for the active canvas element. This includes creating it if it
    // doesn't exist, and positioning it correctly.
    setupControlFrame() {
        // If the active element isn't visible, it isn't really active.  See BL-14439.
        this.checkActiveElementIsVisible();
        const eltToPutControlsOn = this.activeElement;
        let controlFrame = document.getElementById(
            "canvas-element-control-frame"
        );
        if (!eltToPutControlsOn) {
            this.removeControlFrame();
            return;
        }

        if (!controlFrame) {
            controlFrame = eltToPutControlsOn.ownerDocument.createElement(
                "div"
            );
            controlFrame.setAttribute("id", "canvas-element-control-frame");
            controlFrame.classList.add("bloom-ui"); // makes sure it gets cleaned up.
            eltToPutControlsOn.parentElement?.appendChild(controlFrame);
            const corners = ["ne", "nw", "se", "sw"];
            corners.forEach(corner => {
                const control = eltToPutControlsOn.ownerDocument.createElement(
                    "div"
                );
                control.classList.add("bloom-ui-canvas-element-resize-handle");
                control.classList.add(
                    "bloom-ui-canvas-element-resize-handle-" + corner
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
                sideControl.classList.add(
                    "bloom-ui-canvas-element-side-handle"
                );
                sideControl.classList.add(
                    "bloom-ui-canvas-element-side-handle-" + side
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
            sideHandle.classList.add(
                "bloom-ui-canvas-element-move-crop-handle"
            );
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
            toolboxRoot.setAttribute("id", "canvas-element-context-controls");
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
        const hasSvg =
            eltToPutControlsOn?.getElementsByClassName("bloom-svg")?.length > 0;
        if (hasSvg) {
            controlFrame.classList.add("has-svg");
        } else {
            controlFrame.classList.remove("has-svg");
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
        // to reduce flicker we don't show this when switching to a different canvas element until we determine
        // that it is wanted.
        controlFrame.classList.remove(
            "bloom-ui-canvas-element-show-move-crop-handle"
        );
        // If the canvas element is not the right shape for a contained image, fix it now.
        // This also aligns the canvas element controls with the image (possibly after waiting
        // for the image dimensions)
        this.adjustContainerAspectRatio(eltToPutControlsOn);
        renderCanvasElementContextControls(eltToPutControlsOn, false);
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
            kImageContainerClass
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (!img) return;
        this.oldImageTop = img.offsetTop;
        this.oldImageLeft = img.offsetLeft;
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
        this.startMoving();
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
        this.stopMoving();
    };

    private continueMoveCrop = (event: MouseEvent) => {
        if (event.buttons !== 1 || !this.activeElement) {
            return;
        }
        const deltaX = event.clientX - this.startMoveCropX;
        const deltaY = event.clientY - this.startMoveCropY;
        const imgC = this.activeElement.getElementsByClassName(
            kImageContainerClass
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (!img) return;
        event.preventDefault();
        event.stopPropagation();
        const imgStyle = img.style;
        // left can't be greater than zero; that would leave empty space on the left.
        // also can't be so small as to make the right of the image (img.clientWidth + newLeft) less than
        // the right of the canvas element (this.activeElement.clientLeft + this.activElement.clientWidth)
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
        this.oldWidth = this.activeElement.clientWidth;
        this.oldHeight = this.activeElement.clientHeight;
        this.oldTop = this.activeElement.offsetTop;
        this.oldLeft = this.activeElement.offsetLeft;
        const imgOrVideo = this.getImageOrVideo();
        if (imgOrVideo && imgOrVideo.style.width) {
            this.oldImageWidth = imgOrVideo.clientWidth;
            this.oldImageTop = imgOrVideo.offsetTop;
            this.oldImageLeft = imgOrVideo.offsetLeft;
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

    private minWidth = 30; // @MinTextBoxWidth in overlayTool.less
    private minHeight = 30; // @MinTextBoxHeight in overlayTool.less

    private getImageOrVideo(): HTMLElement | undefined {
        // It will have one or the other or neither, but not both, so it doesn't much matter
        // which we search for first. But images are probably more common.
        const imgC = this.activeElement?.getElementsByClassName(
            kImageContainerClass
        )[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (img) return img;
        const videoC = this.activeElement?.getElementsByClassName(
            "bloom-videoContainer"
        )[0];
        const video = videoC?.getElementsByTagName("video")[0];
        return video;
    }

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
        const imgOrVideo = this.getImageOrVideo();
        // The slope of a line from nw to se (since y is positive down, this is a positive slope).
        // If we're moving one of the other points we will negate it to get the slope of the line
        // from ne to sw
        let slope = imgOrVideo ? this.oldHeight / this.oldWidth : 0;
        if (!slope && this.activeElement.querySelector(".bloom-svg")) slope = 1;

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
            // Note that we want to keep the aspect ratio of the canvas element, not the original image.
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
        if (imgOrVideo?.style.width) {
            const scale = newWidth / this.oldWidth;
            imgOrVideo.style.width = this.oldImageWidth * scale + "px";
            // to keep the same part of it showing, we need to scale left and top the same way.
            imgOrVideo.style.left = this.oldImageLeft * scale + "px";
            imgOrVideo.style.top = this.oldImageTop * scale + "px";
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
    // did not reset cropping when the canvas element was moved, we would need to adjust
    // initialCropCanvasElementTop/Left in a non-obvious way).
    private lastCropControl: HTMLElement | undefined;
    private initialCropImageWidth: number;
    private initialCropImageHeight: number;
    private initialCropImageLeft: number;
    private initialCropImageTop: number;
    private initialCropCanvasElementWidth: number;
    private initialCropCanvasElementHeight: number;
    private initialCropCanvasElementTop: number;
    private initialCropCanvasElementLeft: number;
    // If we're dragging a crop control, we generally want to snap when the edege
    // of the (underlying, uncropped) image is close to the corresponding edge
    // of the canvas element in which it is cropped...that is, no cropping on that edge,
    // nor have we (this cycle) expanded the image by dragging the crop handle outward.
    // However, if the drag started in the crop position we disable cropping so small
    // adjustments can be made. If the pointer moves more than the snap distance,
    // we resume cropping. (Cropping can also be disabled by holding down the ctrl key).
    // This variable is true when we are in that state where cropping is disabled
    // because we've made only a small movement from an uncropped state. It is
    // independent of the ctrl key state (though irrelevant if it is down).
    private cropSnapDisabled: boolean = false;

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
        this.oldWidth = this.activeElement.clientWidth;
        this.oldHeight = this.activeElement.clientHeight;
        this.oldTop = this.activeElement.offsetTop;
        this.oldLeft = this.activeElement.offsetLeft;
        if (img) {
            this.oldImageLeft = img.offsetLeft;
            this.oldImageTop = img.offsetTop;

            if (this.lastCropControl !== event.currentTarget) {
                this.initialCropImageWidth = img.offsetWidth;
                this.initialCropImageHeight = img.offsetHeight;
                this.initialCropImageLeft = img.offsetLeft;
                this.initialCropImageTop = img.offsetTop;
                this.initialCropCanvasElementWidth = this.activeElement.offsetWidth;
                this.initialCropCanvasElementHeight = this.activeElement.offsetHeight;
                this.initialCropCanvasElementTop = this.activeElement.offsetTop;
                this.initialCropCanvasElementLeft = this.activeElement.offsetLeft;
                this.lastCropControl = event.currentTarget as HTMLElement;
            }
            // Determine whether the drag is starting in the "no cropping" position
            // and we therefore want to disable snapping until we move a bit.
            // switch (side) {
            //     case "n":
            //         this.cropSnapDisabled = this.oldImageTop === 0;
            //         break;
            //     case "w":
            //         this.cropSnapDisabled = this.oldImageLeft === 0;
            //         break;
            //     case "s":
            //         // initialCropImageTop + initialCropImageHeight is where the bottom of the image is.
            //         // this.oldHeight is where the bottom of the canvas element is. We're in this state if
            //         // they are equal. There can be fractions of pixels involved, so we allow up to
            //         // a pixel and still consider it uncropped.
            //         this.cropSnapDisabled =
            //             Math.abs(
            //                 this.initialCropImageTop +
            //                     this.initialCropImageHeight -
            //                     this.oldHeight
            //             ) < 1;
            //         break;
            //     case "e":
            //         // Similarly figure whether the right edge is uncropped.
            //         this.cropSnapDisabled =
            //             Math.abs(
            //                 this.initialCropImageLeft +
            //                     this.initialCropImageWidth -
            //                     this.oldWidth
            //             ) < 1;
            //         break;
            // }
            // For now we're disabling move beyond zero cropping, so we don't need snap-to-zero.
            this.cropSnapDisabled = true;
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
        this.startMoving();
    }
    private stopSideDrag = () => {
        document.removeEventListener("mousemove", this.continueSideDrag, {
            capture: true
        });
        document.removeEventListener("mouseup", this.stopSideDrag, {
            capture: true
        });
        this.currentDragControl?.classList.remove("active-control");
        if (this.activeElement?.classList.contains(kBackgroundImageClass)) {
            this.adjustBackgroundImageSize(
                this.activeElement.closest(kBloomCanvasSelector)!,
                this.activeElement,
                false
            );
            // an additional move makes continuing the last crop invalid.
            this.lastCropControl = undefined;
        }
        // Now the crop is over, if it is actually no longer cropped at all, we can
        // remove the cropping-specfic style info on the image.
        // Doing so helps us more accurately determine whether a book has cropped images,
        // which means it is not allowed to open in earlier versions of Bloom.
        //this.adjustMoveCropHandleVisibility(true); // called by stopMoving()
        this.stopMoving();
        // We may have changed the state of the fill space button, but the React code
        // doesn't know this unless we force a render.
        renderCanvasElementContextControls(
            this.activeElement as HTMLElement,
            false
        );
    };
    private continueTextBoxResize(event: MouseEvent, editable: HTMLElement) {
        if (!this.activeElement) return; // should never happen, but makes lint happy
        let deltaX = event.clientX - this.startSideDragX;
        let deltaY = event.clientY - this.startSideDragY;
        let newCanvasElementWidth = this.oldWidth; // default
        let newCanvasElementHeight = this.oldHeight; // default
        console.assert(
            this.currentDragSide === "e" ||
                this.currentDragSide === "w" ||
                this.currentDragSide === "s"
        );
        switch (this.currentDragSide) {
            case "e":
                newCanvasElementWidth = Math.max(
                    this.oldWidth + deltaX,
                    this.minWidth
                );
                deltaX = newCanvasElementWidth - this.oldWidth;
                this.activeElement.style.width = `${newCanvasElementWidth}px`;
                break;
            case "w":
                newCanvasElementWidth = Math.max(
                    this.oldWidth - deltaX,
                    this.minWidth
                );
                deltaX = this.oldWidth - newCanvasElementWidth;
                this.activeElement.style.width = `${newCanvasElementWidth}px`;
                this.activeElement.style.left = `${this.oldLeft + deltaX}px`;
                break;
            case "s":
                newCanvasElementHeight = Math.max(
                    this.oldHeight + deltaY,
                    this.minHeight
                );
                deltaY = newCanvasElementHeight - this.oldHeight;
                this.activeElement.style.height = `${newCanvasElementHeight}px`;
        }
        // This won't adjust the height of the editable, but it will mark overflow appropriately.
        // See BL-13902.
        theOneCanvasElementManager.adjustCanvasElementHeightToContentOrMarkOverflow(
            editable
        );
        adjustTarget(this.activeElement, getTarget(this.activeElement));
        this.alignControlFrameWithActiveElement();
    }

    // Determine which of the side handles, if any, should have the class "bloom-currently-cropped"
    private updateCurrentlyCropped() {
        const sideHandles = Array.from(
            document.getElementsByClassName(
                "bloom-ui-canvas-element-side-handle"
            )
        );
        if (sideHandles.length === 0 || !this.activeElement) return;
        const img = getImageFromCanvasElement(this.activeElement);
        if (!img) {
            // only images do cropping. Remove them all.
            sideHandles.forEach(handle => {
                handle.classList.remove("bloom-currently-cropped");
            });
            return;
        }
        const imgRect = img.getBoundingClientRect();
        const canvasElementRect = this.activeElement.getBoundingClientRect();
        const slop = 1; // allow for rounding errors
        const cropped = {
            n: imgRect.top + slop < canvasElementRect.top,
            e: imgRect.right > canvasElementRect.right + slop,
            s: imgRect.bottom > canvasElementRect.bottom + slop,
            w: imgRect.left + slop < canvasElementRect.left
        };
        sideHandles.forEach(handle => {
            //const side = handle.classList[1].split("-")[4];
            const longClass = Array.from(handle.classList).find(c =>
                c.startsWith("bloom-ui-canvas-element-side-handle-")
            );
            if (!longClass) return;
            const side = longClass.substring(
                "bloom-ui-canvas-element-side-handle-".length
            );
            if (cropped[side]) {
                handle.classList.add("bloom-currently-cropped");
            } else {
                handle.classList.remove("bloom-currently-cropped");
            }
        });
    }

    private continueSideDrag = (event: MouseEvent) => {
        if (event.buttons !== 1 || !this.activeElement) {
            return;
        }
        const textBox = this.activeElement.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on"
        )[0];
        if (textBox) {
            event.preventDefault();
            event.stopPropagation();
            this.continueTextBoxResize(event, textBox as HTMLElement);
            return;
        }
        const img = this.activeElement?.getElementsByTagName("img")[0];
        if (!img) {
            // These handles shouldn't even be visible in this case, so this is for paranoia/lint.
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        // These may be adjusted to the deltas that would not violate min sizes
        let deltaX = event.clientX - this.startSideDragX;
        let deltaY = event.clientY - this.startSideDragY;
        if (event.movementX === 0 && event.movementY === 0) return;

        let newCanvasElementWidth = this.oldWidth; // default
        let newCanvasElementHeight = this.oldHeight;
        // ctrl key suppresses snapping, and we also suppress it if we started
        // snapped and haven't moved far. This is to allow very small adjustments.
        const snapping = !event.ctrlKey && !this.cropSnapDisabled;
        const snapDelta = 30;
        let shouldSnapForBackground = "";
        let backgroundSnapDelta = 0;
        if (
            this.activeElement.classList.contains(kBackgroundImageClass) &&
            !event.ctrlKey
        ) {
            const bloomCanvas = this.activeElement.closest(
                kBloomCanvasSelector
            ) as HTMLElement;
            const containerAspectRatio =
                bloomCanvas.clientWidth / bloomCanvas.clientHeight;
            const canvasElementAspectRatio = this.oldWidth / this.oldHeight;
            switch (this.currentDragSide) {
                case "n":
                    if (containerAspectRatio > canvasElementAspectRatio) {
                        // The canvas element has extra space left and right. Removing just enough at the top
                        // will make the canvas element the same shape as the container. We want to snap to that.
                        // That is, how much smaller would our height have to be to make the aspect ratios
                        // match?
                        backgroundSnapDelta =
                            this.oldHeight -
                            this.oldWidth / containerAspectRatio;
                        shouldSnapForBackground = "y";
                    }
                    break;
                case "w":
                    if (containerAspectRatio < canvasElementAspectRatio) {
                        // The canvas element has extra space top and bottom. Removing just enough at the left
                        // will make the canvas element the same shape as the container. We want to snap to that.
                        backgroundSnapDelta =
                            this.oldWidth -
                            this.oldHeight * containerAspectRatio;
                        shouldSnapForBackground = "x";
                    }
                    break;
                case "s":
                    if (containerAspectRatio > canvasElementAspectRatio) {
                        // The canvas element has extra space left and right. Removing just enough at the bottom
                        // will make the canvas element the same shape as the container. We want to snap to that.
                        backgroundSnapDelta =
                            this.oldWidth / containerAspectRatio -
                            this.oldHeight;
                        shouldSnapForBackground = "y";
                    }
                    break;
                case "e":
                    if (containerAspectRatio < canvasElementAspectRatio) {
                        // The canvas element has extra space top and bottom. Removing just enough at the right
                        // will make the canvas element the same shape as the container. We want to snap to that.
                        backgroundSnapDelta =
                            this.oldHeight * containerAspectRatio -
                            this.oldWidth;
                        shouldSnapForBackground = "x";
                    }
                    break;
            }
        }

        // This block of code supports snapping to the "zero crop" position (useful if we re-enable
        // zooming the image by dragging the crop handles outward).
        // Each case begins by figuring out whether, if we are snapping, we should snap.
        // Next it figures out whether we've moved far enough to end the "start at zero"
        // non-snapping. Then it figures out a first approximation of how the canvas element and image
        // position and size should change, without considering the possibility that
        // dragging outward would leave white space. A later step adjusts for that.
        // switch (this.currentDragSide) {
        //     case "n":
        //         if (
        //             snapping &&
        //             Math.abs(this.oldImageTop - deltaY) < snapDelta
        //         ) {
        //             deltaY = this.oldImageTop;
        //         }
        //         if (Math.abs(this.oldImageTop - deltaY) > snapDelta) {
        //             // The distance moved is substantial, time to re-enable snapping
        //             // for future moves (without ctrl-key).
        //             this.cropSnapDisabled = false;
        //         }
        //         newCanvasElementHeight = Math.max(
        //             this.oldHeight - deltaY,
        //             this.minHeight
        //         );
        //         // Everything subsequent behaves as if it only moved as far as permitted.
        //         deltaY = this.oldHeight - newCanvasElementHeight;
        //         this.activeElement.style.height = `${newCanvasElementHeight}px`;
        //         // Moves down by the amount the canvas element shrank (or up by the amount it grew),
        //         // so the bottom stays in the same place
        //         this.activeElement.style.top = `${this.oldTop + deltaY}px`;
        //         // For a first attempt, we move it the oppposite of how the canvas element actually
        //         // changd size. That might leave a gap at the top, but we'll adjust for that later.
        //         img.style.top = `${this.oldImageTop - deltaY}px`;
        //         break;
        //     case "s":
        //         // These variables would make the next line more comprehensible, but they only apply
        //         // to this case and lint does not like declaring variables inside a switch.
        //         // Essentially we're trying to determine how far apart the bottom of the image and the bottom of the canvas element are.
        //         // const heightThatMathchesBottomOfImage = this.initialCropImageTop + this.initialCropImageHeight;
        //         // const newHeight = this.oldHeight + deltaY;
        //         if (
        //             snapping &&
        //             Math.abs(
        //                 this.initialCropImageTop +
        //                     this.initialCropImageHeight -
        //                     this.oldHeight -
        //                     deltaY
        //             ) < snapDelta
        //         ) {
        //             deltaY =
        //                 this.initialCropImageTop +
        //                 this.initialCropImageHeight -
        //                 this.oldHeight;
        //         }
        //         if (
        //             Math.abs(
        //                 this.initialCropImageTop +
        //                     this.initialCropImageHeight -
        //                     this.oldHeight -
        //                     deltaY
        //             ) > snapDelta
        //         ) {
        //             // The distance moved is substantial, time to re-enable snapping
        //             // for future moves (without ctrl-key).
        //             this.cropSnapDisabled = false;
        //         }
        //         newCanvasElementHeight = Math.max(
        //             this.oldHeight + deltaY,
        //             this.minHeight
        //         );
        //         deltaY = newCanvasElementHeight - this.oldHeight;
        //         this.activeElement.style.height = `${newCanvasElementHeight}px`;
        //         break;
        //     case "e":
        //         // const widthThatMathchesRightOfImage = this.initialCropImageLeft + this.initialCropImageWidth;
        //         // const newWidth = this.oldWidth + deltaX;
        //         if (
        //             snapping &&
        //             Math.abs(
        //                 this.initialCropImageLeft +
        //                     this.initialCropImageWidth -
        //                     this.oldWidth -
        //                     deltaX
        //             ) < snapDelta
        //         ) {
        //             deltaX =
        //                 this.initialCropImageLeft +
        //                 this.initialCropImageWidth -
        //                 this.oldWidth;
        //         }
        //         if (
        //             Math.abs(
        //                 this.initialCropImageLeft +
        //                     this.initialCropImageWidth -
        //                     this.oldWidth -
        //                     deltaX
        //             ) > snapDelta
        //         ) {
        //             // The distance moved is substantial, time to re-enable snapping
        //             // for future moves (without ctrl-key).
        //             this.cropSnapDisabled = false;
        //         }
        //         newCanvasElementWidth = Math.max(
        //             this.oldWidth + deltaX,
        //             this.minWidth
        //         );
        //         deltaX = newCanvasElementWidth - this.oldWidth;
        //         this.activeElement.style.width = `${newCanvasElementWidth}px`;
        //         break;
        //     case "w":
        //         if (
        //             snapping &&
        //             Math.abs(this.oldImageLeft - deltaX) < snapDelta
        //         ) {
        //             deltaX = this.oldImageLeft;
        //         }
        //         if (Math.abs(this.oldImageLeft - deltaX) > snapDelta) {
        //             // The distance moved is substantial, time to re-enable snapping
        //             // for future moves (without ctrl-key).
        //             this.cropSnapDisabled = false;
        //         }
        //         newCanvasElementWidth = Math.max(
        //             this.oldWidth - deltaX,
        //             this.minWidth
        //         );
        //         deltaX = this.oldWidth - newCanvasElementWidth;
        //         this.activeElement.style.width = `${newCanvasElementWidth}px`;
        //         this.activeElement.style.left = `${this.oldLeft + deltaX}px`;
        //         img.style.left = `${this.oldImageLeft - deltaX}px`;
        //         break;
        // }
        // This code, which is an alternative to the block commented out above, just won't let you move
        // beyond zero cropping.
        switch (this.currentDragSide) {
            case "n":
                deltaY = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "y",
                    deltaY,
                    backgroundSnapDelta,
                    "n"
                );
                // correct if we moved the top too far up, which would leave a gap at the top
                if (this.oldImageTop - deltaY > 0) {
                    deltaY = this.oldImageTop;
                }
                // correct if we moved too far down, violating the minimum image height constraint.
                newCanvasElementHeight = Math.max(
                    this.oldHeight - deltaY,
                    this.minHeight
                );
                // Everything subsequent behaves as if it only moved as far as permitted.
                deltaY = this.oldHeight - newCanvasElementHeight;
                this.activeElement.style.height = `${newCanvasElementHeight}px`;
                // Moves down by the amount the canvas element shrank (or up by the amount it grew),
                // so the bottom stays in the same place
                this.activeElement.style.top = `${this.oldTop + deltaY}px`;
                // We move it the oppposite of how the canvas element actually
                // changd size.
                img.style.top = `${this.oldImageTop - deltaY}px`;
                break;
            case "s":
                deltaY = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "y",
                    deltaY,
                    backgroundSnapDelta,
                    "s"
                );
                // correct if we moved too far down, which would leave a gap at the bottom
                // These variables would make the next line more comprehensible, but they only apply
                // to this case and lint does not like declaring variables inside a switch.
                // Essentially we're trying to determine whether we moved the bottom of the canvas element beyond the bottom of the image.
                // const heightThatMathchesBottomOfImage = this.initialCropImageTop + this.initialCropImageHeight;
                // const newHeight = this.oldHeight + deltaY;
                if (
                    this.initialCropImageTop + this.initialCropImageHeight <
                    this.oldHeight + deltaY
                ) {
                    deltaY =
                        this.initialCropImageTop +
                        this.initialCropImageHeight -
                        this.oldHeight;
                }
                // correct if we moved too far up, violating the minimum image height constraint.
                newCanvasElementHeight = Math.max(
                    this.oldHeight + deltaY,
                    this.minHeight
                );
                deltaY = newCanvasElementHeight - this.oldHeight;
                this.activeElement.style.height = `${newCanvasElementHeight}px`;
                break;
            case "e":
                deltaX = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "x",
                    deltaX,
                    backgroundSnapDelta,
                    "e"
                );
                // correct if we moved too far right, which would leave a gap at the right
                if (
                    this.initialCropImageLeft + this.initialCropImageWidth <
                    this.oldWidth + deltaX
                ) {
                    deltaX =
                        this.initialCropImageLeft +
                        this.initialCropImageWidth -
                        this.oldWidth;
                }
                // correct if we moved too far left, violating the minimum image width constraint.
                newCanvasElementWidth = Math.max(
                    this.oldWidth + deltaX,
                    this.minWidth
                );
                deltaX = newCanvasElementWidth - this.oldWidth;
                this.activeElement.style.width = `${newCanvasElementWidth}px`;
                break;
            case "w":
                deltaX = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "x",
                    deltaX,
                    backgroundSnapDelta,
                    "w"
                );
                // correct if we moved too far left, which would leave a gap at the left
                if (this.oldImageLeft > deltaX) {
                    deltaX = this.oldImageLeft;
                }
                // correct if we moved too far right, violating the minimum image width constraint.
                newCanvasElementWidth = Math.max(
                    this.oldWidth - deltaX,
                    this.minWidth
                );
                deltaX = this.oldWidth - newCanvasElementWidth;
                this.activeElement.style.width = `${newCanvasElementWidth}px`;
                this.activeElement.style.left = `${this.oldLeft + deltaX}px`;
                img.style.left = `${this.oldImageLeft - deltaX}px`;
                break;
        }
        // This block is the adjustment if we allow the image to be zoomed by dragging the crop handles outward.
        // To make that work, we also need to remove the code above that prevents moving beyond zero cropping.
        // (and probably restore the code that snaps to zero cropping).
        // let newImageWidth: number;
        // let newImageHeight: number;
        // // How much of the image should stay cropped on the left if we're adjusting the right, etc.
        // // Some of these are not needed on some sides, but it's easier to calculate them all,
        // // and makes lint happy if we don't declare variables inside the switch.
        // const leftFraction =
        //     -this.initialCropImageLeft / this.initialCropImageWidth;
        // // Fraction of the total image width that is left of the center of the canvas element.
        // // This stays constant as we crop on the top and bottom.
        // const centerFractionX =
        //     leftFraction +
        //     this.initialCropCanvasElementWidth / this.initialCropImageWidth / 2;
        // const rightFraction =
        //     (this.initialCropImageWidth +
        //         this.initialCropImageLeft -
        //         this.initialCropCanvasElementWidth) /
        //     this.initialCropImageWidth;
        // const bottomFraction =
        //     (this.initialCropImageHeight +
        //         this.initialCropImageTop -
        //         this.initialCropCanvasElementHeight) /
        //     this.initialCropImageHeight;
        // const topFraction =
        //     -this.initialCropImageTop / this.initialCropImageHeight;
        // // fraction of the total image height that is above the center of the canvas element.
        // // This stays constant as we crop on the left and right.
        // const centerFractionY =
        //     topFraction +
        //     this.initialCropCanvasElementHeight / this.initialCropImageHeight / 2;
        // // Deliberately dividing by the WIDTH here; all our calculations are
        // // based on the adjusted width of the image.
        // const topAsFractionOfWidth =
        //     -this.initialCropImageTop / this.initialCropImageWidth;
        // // Specifically, the aspect ratio for computing the height of the (full) image
        // // from its width.
        // const aspectRatio = img.naturalHeight / img.naturalWidth;
        // switch (this.currentDragSide) {
        //     case "e":
        //         if (
        //             // the canvas element has stretched beyond the right side of the image
        //             newCanvasElementWidth >
        //             this.initialCropImageLeft + this.initialCropImageWidth
        //         ) {
        //             // grow the image. We want its right edge to end up at newCanvasElementWidth,
        //             // after being stretched enough to leave the same fraction as before
        //             // cropped on the left.
        //             newImageWidth = newCanvasElementWidth / (1 - leftFraction);
        //             img.style.width = `${newImageWidth}px`;
        //             // fiddle with the left to keep the same part cropped
        //             img.style.left = `${-leftFraction * newImageWidth}px`;
        //             // and the top to split the extra height between top and bottom
        //             newImageHeight = newImageWidth * aspectRatio;
        //             const newTopFraction =
        //                 centerFractionY -
        //                 this.initialCropCanvasElementHeight / newImageHeight / 2;
        //             img.style.top = `${-newTopFraction * newImageHeight}px`;
        //         } else {
        //             // no need to stretch. Restore the image to its original position and size.
        //             img.style.width = `${this.initialCropImageWidth}px`;
        //             img.style.top = `${this.initialCropImageTop}px`;
        //         }
        //         break;
        //     case "w":
        //         if (
        //             // the canvas element has stretched beyond the original left side of the image
        //             // this.oldLeft + deltaX is where the left of the canvas element is now
        //             // this.initialCropImageLeft + this.initialCanvasElementImageLeft is where
        //             // the left of the image was when we started.
        //             this.oldLeft + deltaX <
        //             this.initialCropImageLeft + this.initialCropCanvasElementLeft
        //         ) {
        //             // grow the image. We want its left edge to end up at zero,
        //             // after being stretched enough to leave the same fraction as before
        //             // cropped on the right.
        //             newImageWidth = newCanvasElementWidth / (1 - rightFraction);
        //             img.style.width = `${newImageWidth}px`;
        //             // no cropping on the left
        //             img.style.left = `0`;
        //             // and the top to split the extra height between top and bottom
        //             newImageHeight = newImageWidth * aspectRatio;
        //             const newTopFraction =
        //                 centerFractionY -
        //                 this.initialCropCanvasElementHeight / newImageHeight / 2;
        //             img.style.top = `${-newTopFraction * newImageHeight}px`;
        //         } else {
        //             img.style.width = `${this.initialCropImageWidth}px`;
        //             img.style.top = `${this.initialCropImageTop}px`;
        //         }
        //         break;
        //     case "s":
        //         if (
        //             // the canvas element has stretched beyond the bottom side of the image
        //             newCanvasElementHeight >
        //             this.initialCropImageTop + this.initialCropImageHeight
        //         ) {
        //             // grow the image. We want its bottom edge to end up at newCanvasElementHeight,
        //             // after being stretched enough to leave the same fraction as before
        //             // cropped on the top.
        //             newImageHeight = newCanvasElementHeight / (1 - topFraction);
        //             newImageWidth = newImageHeight / aspectRatio;
        //             img.style.width = `${newImageWidth}px`;
        //             // fiddle with the top to keep the same part cropped
        //             img.style.top = `${-topAsFractionOfWidth *
        //                 newImageWidth}px`;
        //             // and the left to split the extra width between top and bottom
        //             // centerFractionX = leftFraction + this.initialCropCanvasElementWidth / this.initialCropImageWidth / 2;
        //             // centerFractionX = newleftFraction + this.initialCropCanvasElementWidth / newImageWidth / 2;
        //             const newleftFraction =
        //                 centerFractionX -
        //                 this.initialCropCanvasElementWidth / newImageWidth / 2;
        //             img.style.left = `${-newleftFraction * newImageWidth}px`;
        //         } else {
        //             img.style.width = `${this.initialCropImageWidth}px`;
        //             img.style.left = `${this.initialCropImageLeft}px`;
        //         }
        //         break;
        //     case "n":
        //         if (
        //             // the canvas element has stretched beyond the original top side of the image
        //             // this.oldTop + deltaY is where the top of the canvas element is now
        //             // this.initialCropImageTop + this.initialCanvasElementImageTop is where
        //             // the top of the image was when we started.
        //             this.oldTop + deltaY <
        //             this.initialCropImageTop + this.initialCropCanvasElementTop
        //         ) {
        //             // grow the image. We want its top edge to end up at zero,
        //             // after being stretched enough to leave the same fraction as before
        //             // cropped on the bottom.
        //             newImageHeight = newCanvasElementHeight / (1 - bottomFraction);
        //             newImageWidth = newImageHeight / aspectRatio;
        //             img.style.width = `${newImageWidth}px`;
        //             // no cropping on the top
        //             img.style.top = `0`;
        //             // and the left to split the extra width between top and bottom
        //             const newleftFraction =
        //                 centerFractionX -
        //                 this.initialCropCanvasElementWidth / newImageWidth / 2;
        //             img.style.left = `${-newleftFraction * newImageWidth}px`;
        //         } else {
        //             img.style.width = `${this.initialCropImageWidth}px`;
        //             img.style.left = `${this.initialCropImageLeft}px`;
        //         }
        //         break;
        // }
        // adjust other things that are affected by the new size.
        this.alignControlFrameWithActiveElement();
        this.adjustTarget(this.activeElement);
        this.updateCurrentlyCropped();
    };

    private adjustDeltaForSnap(
        shouldSnap: boolean,
        delta: number,
        backgroundSnapDelta: number,
        side: string
    ): number {
        if (!shouldSnap) return delta;
        const snapDelta = 30;
        const controlFrame = document.getElementById(
            "canvas-element-control-frame"
        ) as HTMLElement;
        if (Math.abs(backgroundSnapDelta - delta) < snapDelta) {
            this.getHandleTitlesAsync(
                controlFrame,
                "bloom-ui-canvas-element-side-handle-" + side,
                "Fill",
                true,
                "data-title"
            );
            return backgroundSnapDelta;
        }
        // not snapping
        this.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-side-handle-" + side,
            "Crop",
            true,
            "data-title"
        );
        return delta;
    }

    public resetCropping(adjustContainer = true) {
        if (!this.activeElement) return;
        const img = getImageFromCanvasElement(this.activeElement);
        if (!img) return;
        img.style.width = "";
        img.style.top = "";
        img.style.left = "";
        if (adjustContainer) {
            // Enhance: possibly we want to align by making it bigger rather than smaller?
            this.adjustContainerAspectRatio(this.activeElement);
        }
    }

    // If the background canvas element doesn't fill the container, we can expand the image to make it so.
    public canExpandToFillSpace(): boolean {
        if (
            !this.activeElement ||
            !this.activeElement.classList.contains(kBackgroundImageClass)
        )
            return false;
        const bloomCanvas = this.activeElement.closest(
            kBloomCanvasSelector
        ) as HTMLElement;
        if (!bloomCanvas) return false;
        return (
            bloomCanvas.clientWidth !== this.activeElement.clientWidth ||
            bloomCanvas.clientHeight !== this.activeElement.clientHeight
        );
    }

    public expandImageToFillSpace() {
        if (
            !this.activeElement ||
            !this.activeElement.classList.contains(kBackgroundImageClass)
        )
            return;
        const img = getImageFromCanvasElement(this.activeElement);
        if (!img) return;
        const bloomCanvas = this.activeElement.closest(
            kBloomCanvasSelector
        ) as HTMLElement;
        if (!bloomCanvas) return;
        // Remove any existing cropping
        this.resetCropping(false);
        const imgAspectRatio = img.naturalWidth / img.naturalHeight;
        const containerAspectRatio =
            bloomCanvas.clientWidth / bloomCanvas.clientHeight;
        this.activeElement.style.width = `${bloomCanvas.clientWidth}px`;
        this.activeElement.style.height = `${bloomCanvas.clientHeight}px`;
        if (imgAspectRatio < containerAspectRatio) {
            // When the image fills the width of the container, it will be too tall,
            // and will need cropping top and bottom.
            const imgHeightForFullWidth =
                bloomCanvas.clientWidth / imgAspectRatio;
            const delta = imgHeightForFullWidth - bloomCanvas.clientHeight;
            if (delta >= 1) {
                // let's not switch into cropped mode if it's this close already
                img.style.width = `${bloomCanvas.clientWidth}px`;
                img.style.top = `${-delta / 2}px`;
            }
        } else {
            // When the image fills the height of the container, it will be too wide,
            // and will need cropping left and right.
            const imgWidthForFullHeight =
                bloomCanvas.clientHeight * imgAspectRatio;
            const delta = imgWidthForFullHeight - bloomCanvas.clientWidth;
            if (delta >= 1) {
                img.style.width = `${imgWidthForFullHeight}px`;
                img.style.left = `${-delta / 2}px`;
            }
        }
        // I think this is redundant, but it may (now or one day) do something that needs doing
        // when the background image changes size.
        this.adjustBackgroundImageSize(bloomCanvas, this.activeElement, false);
        // We will have changed the state of the fill space button, but the React code
        // doesn't know this unless we force a render.
        renderCanvasElementContextControls(this.activeElement, false);
    }

    // If this canvas element contains an image, and it has not already been adjusted so that the canvas element
    // dimensions have the same aspect ratio as the image, make it so, reducing either height or
    // width as necessary, or possibly increasing one if the usual adjustment would make it too small.
    // After making the adjustment if necessary (which might be delayed if the image dimensions
    // are not available), align the control frame with the active element.
    private adjustContainerAspectRatio(
        canvasElement: HTMLElement,
        useSizeOfNewImage = false,
        // Sometimes we think we need to wait for onload, but the data arrives before we set up
        // the watcher. We make a timeout so we will go ahead and adjust if we have dimensions
        // and don't get an onload in a reasonable time. If we DO get the onload before we
        // timeout, we use this handle to clear it.
        // This is set when we arrange an onload callback and receive it
        timeoutHandler: number = 0
    ): void {
        if (timeoutHandler) {
            clearTimeout(timeoutHandler);
        }
        if (canvasElement.classList.contains(kBackgroundImageClass)) {
            this.adjustBackgroundImageSize(
                canvasElement.closest(kBloomCanvasSelector)!,
                canvasElement,
                useSizeOfNewImage
            );
            return;
        }
        const imgOrVideo = this.getImageOrVideo();
        if (!imgOrVideo || imgOrVideo.style.width) {
            // We don't have an image, or we've already done cropping on it, so we should not force the
            // container back to the original image shape.
            this.alignControlFrameWithActiveElement();
            return;
        }
        const containerWidth = canvasElement.clientWidth;
        const containerHeight = canvasElement.clientHeight;
        let imgWidth = 1;
        let imgHeight = 1;
        if (imgOrVideo instanceof HTMLImageElement) {
            imgWidth = imgOrVideo.naturalWidth;
            imgHeight = imgOrVideo.naturalHeight;
            if (
                imgOrVideo.naturalHeight === 0 && // not loaded successfully (yet)
                !useSizeOfNewImage && // not waiting for new dimensions
                imgOrVideo.classList.contains("bloom-imageLoadError") // error occurred while trying to load
            ) {
                // Image is in an error state; we probably won't ever get useful dimensions. Just leave
                // the canvas element the shape it is.
                return;
            }
            if (imgHeight === 0 || useSizeOfNewImage) {
                // image not ready yet, try again later.
                const handle = (setTimeout(
                    () =>
                        this.adjustContainerAspectRatio(
                            canvasElement,
                            false, // if we've got dimensions just use them
                            0
                        ), // if we get this call we don't have a timeout to cancel
                    // I think this is long enough that we won't be seeing obsolete data (from a previous src).
                    // OTOH it's not hopelessly long for the user to wait when we don't get an onload.
                    // If by any chance this happens when the image really isn't loaded enough to
                    // have naturalHeight/Width, the zero checks above will force another iteration.
                    100
                    // somehow Typescript is confused and thinks this is a NodeJS version of setTimeout.
                ) as unknown) as number;
                imgOrVideo.addEventListener(
                    "load",
                    () =>
                        this.adjustContainerAspectRatio(
                            canvasElement,
                            false, // it's loaded, we don't want to wait again
                            handle
                        ), // if we get this call we can cancel the timeout above.
                    { once: true }
                );
                return; // control frame will be aligned when the image is loaded
            }
        } else {
            const video = imgOrVideo as HTMLVideoElement;
            imgWidth = video.videoWidth;
            imgHeight = video.videoHeight;
            if (imgWidth === 0 || imgHeight === 0) {
                // video not ready yet, try again later.
                // I'm not sure this has ever been tested; the dimensions seem to be
                // always available by the time this routine is called.
                video.addEventListener(
                    "loadedmetadata",
                    () => this.adjustContainerAspectRatio(canvasElement),
                    { once: true }
                );
                return;
            }
        }
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
        const oldHeight = canvasElement.clientHeight;
        if (Math.abs(oldHeight - newHeight) > 0.1) {
            // don't let small rounding errors accumulate
            canvasElement.style.height = `${newHeight}px`;
        }
        // and move container down so image does not move
        const oldTop = canvasElement.offsetTop;
        canvasElement.style.top = `${oldTop + (oldHeight - newHeight) / 2}px`;
        const oldWidth = canvasElement.clientWidth;
        canvasElement.style.width = `${newWidth}px`;
        // and move container right so image does not move
        const oldLeft = canvasElement.offsetLeft;
        if (Math.abs(oldWidth - newWidth) > 0.1) {
            canvasElement.style.left = `${oldLeft +
                (oldWidth - newWidth) / 2}px`;
        }
        this.alignControlFrameWithActiveElement();
    }

    // When the image is changed in a canvas element (e.g., choose or paste image),
    // we remove cropping, adjust the aspect ratio, and move the control frame.
    updateCanvasElementForChangedImage(imgOrImageContainer: HTMLElement) {
        const canvasElement = imgOrImageContainer.closest(
            kCanvasElementSelector
        ) as HTMLElement;
        if (!canvasElement) return;
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
        // Get the aspect ratio right (aligns control frame)
        if (canvasElement.classList.contains(kBackgroundImageClass)) {
            this.adjustBackgroundImageSize(
                canvasElement.closest(kBloomCanvasSelector)!,
                canvasElement,
                true
            );
        } else {
            this.adjustContainerAspectRatio(canvasElement, true);
        }
    }

    private async getHandleTitlesAsync(
        controlFrame: HTMLElement,
        className: string,
        l10nId: string,
        force: boolean = false,
        attribute: string = "title"
    ) {
        const handles = Array.from(
            controlFrame.getElementsByClassName(className)
        ) as HTMLElement[];
        // We could cache these somewhere, especially the crop/change shape pair, but I think
        // it would be premature optimization. We only have four title, and
        // only the crop/change shape one has to be retrieved each time the frame moves.
        if (!handles[0]?.getAttribute(attribute) || force) {
            const title = await theOneLocalizationManager.asyncGetText(
                "EditTab.Toolbox.ComicTool.Handle." + l10nId,
                "",
                ""
            );
            handles.forEach(handle => {
                handle.setAttribute(attribute, title);
            });
        }
    }

    // Align the control frame with the active canvas element.
    private alignControlFrameWithActiveElement = () => {
        const controlFrame = document.getElementById(
            "canvas-element-control-frame"
        );
        let controlsAbove = false;
        if (!controlFrame || !this.activeElement) return;

        if (controlFrame.parentElement !== this.activeElement.parentElement) {
            this.activeElement.parentElement?.appendChild(controlFrame);
        }
        controlFrame.classList.toggle(
            "bloom-noAutoHeight",
            this.activeElement.classList.contains("bloom-noAutoHeight")
        );
        // We want some special CSS rules for control frames on background images (e.g., no resize handles).
        // But we give the class a different name so the control frame won't accidentally be affected
        // by any CSS intended for the background image itself. That is, if the active element (the actual canvas
        // element) has kBackgroundImageClass, which triggers its own CSS rules, we want the control frame
        // to have this different class to trigger control frame background-specific CSS rules.
        controlFrame.classList.toggle(
            kBackgroundImageClass + "-control-frame",
            this.activeElement.classList.contains(kBackgroundImageClass)
        );
        const hasText = controlFrame.classList.contains("has-text");
        // We don't need to await these, they are just async so the handle titles can be updated
        // once the localization manager retrieves them.
        this.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-resize-handle",
            "Resize"
        );
        this.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-side-handle",
            hasText ? "ChangeShape" : "Crop",
            // We don't need to change it while we're moving the frame, only if we're switching
            // between text and image. And there's another state we want
            // when cropping a background image and snapped.
            !controlFrame.classList.contains("moving"),
            "data-title"
        );
        this.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-move-crop-handle",
            "Shift"
        );
        // Text boxes get a little extra padding, making the control frame bigger than
        // the canvas element itself. The extra needed corresponds roughly to the (.less) @sideHandleRadius,
        // but one pixel less seems to be enough to prevent the side handles actually overlapping text,
        // though maybe I've just been lucky and this should really be 4.
        // Seems like it should be easy to do this in the .less file, but the control frame is not
        // a child of the canvas element (for z-order reasons), so it's not easy for CSS to move it left
        // when the style is already absolutely controlling style.left. It's easier to just tweak
        // it here.
        const extraPadding = hasText ? 3 : 0;
        // using pxToNumber here because the position and size of the canvas element are often fractional.
        // OTOH, clientWidth etc are whole numbers. If we allow that rounding in to affect where to
        // place the control frame, we can end up with a 1 pixel gap between the canvas element and
        // the control frame, which looks bad.
        controlFrame.style.width =
            CanvasElementManager.pxToNumber(this.activeElement.style.width) +
            2 * extraPadding +
            "px";
        controlFrame.style.height = this.activeElement.style.height;
        controlFrame.style.left =
            CanvasElementManager.pxToNumber(this.activeElement.style.left) -
            extraPadding +
            "px";
        controlFrame.style.top = this.activeElement.style.top;
        const tails = Bubble.getBubbleSpec(this.activeElement).tails;
        if (tails.length > 0) {
            const tipY = tails[0].tipY;
            controlsAbove =
                tipY >
                this.activeElement.clientHeight + this.activeElement.offsetTop;
        }
        this.adjustMoveCropHandleVisibility();
        this.adjustContextControlPosition(controlFrame, controlsAbove);
    };

    adjustContextControlPosition(
        controlFrame: HTMLElement | null,
        controlsAbove: boolean
    ) {
        const contextControl = document.getElementById(
            "canvas-element-context-controls"
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

        // This just needs to be wider than the context controls ever are. They get centered in a box this wide.
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
        contextControl.style.visibility = "visible";
        if (controlsAbove) {
            // Bottom 11 px above the top of the control frame.
            if (contextControlRect.height > 0) {
                top -= contextControlRect.height + 11;
            } else {
                // We get a zero height when it is initially hidden. Place it in about the right
                // place so we can measure it and try again once it is (invisibly) rendered.
                top -= 30 + 11;
                contextControl.style.visibility = "hidden";
                setTimeout(() => {
                    this.adjustContextControlPosition(
                        controlFrame,
                        controlsAbove
                    );
                }, 0);
            }
        } else {
            // Top 11 px below the bottom of the control frame
            top += controlFrameRect.height + 11;
            // exception: if the control frame extends beyond the bottom of the image-container,
            // we want to use the image-container's bottom as our reference point.
            // This can happen with a background image set to bloom-imageObjectFitCover.
            const bloomCanvasRect = this.activeElement!.closest(
                kBloomCanvasSelector
            )!.getBoundingClientRect();
            if (controlFrameRect.bottom > bloomCanvasRect.bottom) {
                top = bloomCanvasRect.bottom + 11;
            }
        }
        contextControl.style.left = left + "px";
        contextControl.style.top = top + "px";
        // This is constant, so it could be in the CSS. But then it could not share a constant
        // with the computation of left above, so it would be harder to keep things consistent.
        contextControl.style.width = contextControlsWidth + "px";
    }

    public doNotifyChange() {
        const spec = this.getSelectedFamilySpec();
        this.thingsToNotifyOfCanvasElementChange.forEach(f => f.handler(spec));
    }

    // Set the color of the text in all of the active canvas element family's canvas elements.
    // If hexOrRgbColor is empty string, we are setting the canvas element to use the style default.
    public setTextColor(hexOrRgbColor: string) {
        const activeEl = theOneCanvasElementManager.getActiveElement();
        if (activeEl) {
            // First, see if this canvas element is in parent/child relationship with any others.
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
        // BL-11621: We are in the process of moving to putting the canvas element text color on the inner
        // bloom-editables. So we clear any color on the canvas element div and set it on all of the
        // inner bloom-editables.
        const topBox = element.closest(
            kCanvasElementSelector
        ) as HTMLDivElement;
        topBox.style.color = "";
        const editables = topBox.getElementsByClassName("bloom-editable");
        for (let i = 0; i < editables.length; i++) {
            const editableElement = editables[i] as HTMLElement;
            editableElement.style.color = hexOrRgbColor;
        }
    }

    public getTextColorInformation(): ITextColorInfo {
        const activeEl = theOneCanvasElementManager.getActiveElement();
        let textColor = "";
        let isDefaultStyleColor = false;
        if (activeEl) {
            const topBox = activeEl.closest(
                kCanvasElementSelector
            ) as HTMLDivElement;
            // const allUserStyles = StyleEditor.GetFormattingStyleRules(
            //     topBox.ownerDocument
            // );
            const style = topBox.style;
            textColor = style && style.color ? style.color : "";
            // We are in the process of moving to putting the Canvas element text color on the inner
            // bloom-editables. So if the canvas element div didn't have a color, check the inner
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
    // Canvas element Tool will be from the Bubble-style (set in the StyleEditor).
    // An unfortunate, but greatly simplifying, use of JQuery.
    public getDefaultStyleTextColor(firstEditable: HTMLElement): string {
        return $(firstEditable).css("color");
    }

    // This gives us the patriarch (farthest ancestor) canvas element of a family of canvas elements.
    // If the active element IS the parent of our family, this returns the active element's bubble.
    public getPatriarchBubbleOfActiveElement(): Bubble | undefined {
        if (!this.activeElement) {
            return undefined;
        }
        const tempBubble = new Bubble(this.activeElement);
        const ancestors = Comical.findAncestors(tempBubble);
        return ancestors.length > 0 ? ancestors[0] : tempBubble;
    }

    // Set the color of the background in all of the active canvas element family's canvas elements.
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
    // foreground or background color on the canvas element, since those processes
    // involve focusing the canvas element and this is inconvenient when typing in the
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

    // drag-and-drop support for canvas elements from comical toolbox
    private setDragAndDropHandlers(container: HTMLElement): void {
        if (isLinux()) return; // these events never fire on Linux: see BL-7958.
        // This suppresses the default behavior, which is to forbid dragging things to
        // an element, but only if the source of the drag is a bloom canvas element.
        container.ondragover = ev => {
            if (
                ev.dataTransfer &&
                // don't be tempted to return to ev.dataTransfer.getData("text/x-bloom-canvas-element")
                // as we used with geckofx. In WebView2, this returns an empty string.
                // I think it is some sort of security thing, the idea is that something
                // you're just dragging over shouldn't have access to the content.
                // The presence of our custom data type at all indicates this is something
                // we want to accept dropped here.
                // (types is an array: indexOf returns -1 if the item is not found)
                ev.dataTransfer.types.indexOf("text/x-bloom-canvas-element") >=
                    0
            ) {
                ev.preventDefault();
            }
        };
        // Controls what happens when a bloom canvas element is dropped. We get the style
        // set in ComicToolControls.ondragstart() and make a canvas element with that style
        // at the drop position.
        container.ondrop = ev => {
            // test this so we don't interfere with dragging for text edit,
            // nor add canvas elements when something else is dragged
            if (
                ev.dataTransfer &&
                ev.dataTransfer.getData("text/x-bloom-canvas-element") &&
                !ev.dataTransfer.getData("text/x-bloomdraggable") // items that create a draggable use another approach
            ) {
                ev.preventDefault();
                const style = ev.dataTransfer
                    ? ev.dataTransfer.getData("text/x-bloom-canvas-element")
                    : "speech";
                // If this got used, we'd want it to have a rightTopOffset value. But I think all our things that can
                // be dragged are now using CanvasElementItem, and its dragStart sets text/x-bloomdraggable, so this
                // code doesn't get used.
                this.addCanvasElement(
                    ev.clientX,
                    ev.clientY,
                    style as CanvasElementType
                );
            }
        };
    }

    // Setup event handlers that allow the canvas element to be moved around or resized.
    private setMouseDragHandlers(bloomCanvas: HTMLElement): void {
        // An earlier version of this code set onmousedown to this.onMouseDown, etc.
        // We need to use addEventListener so we can capture.
        // It's unlikely, but I can't rule it out, that a deliberate side effect
        // was to remove some other onmousedown handler. Just in case, clear the fields.
        // I don't think setting these has any effect on handlers done with addEventListener,
        // but just in case, I'm doing this first.
        bloomCanvas.onmousedown = null;
        bloomCanvas.onmousemove = null;
        bloomCanvas.onmouseup = null;

        // We use mousemove effects instead of drag due to concerns that drag effects would make the entire bloom-canvas appear to drag.
        // Instead, with mousemove, we can make only the specific canvas element move around
        // Grabbing these (particularly the move event) in the capture phase allows us to suppress
        // effects of ctrl and alt clicks on the text.
        bloomCanvas.addEventListener("mousedown", this.onMouseDown, {
            capture: true
        });

        // I would prefer to add this to document in onMouseDown, but not yet satisfied that all
        // the things it does while hovering are no longer needed.
        bloomCanvas.addEventListener("mousemove", this.onMouseMove, {
            capture: true
        });

        // mouse up handler is added to document in onMouseDown

        bloomCanvas.onkeypress = (event: Event) => {
            // If the user is typing in a canvas element, make sure automatic shrinking is off.
            // Automatic shrinking while typing might be useful when originally authoring a comic,
            // but it's a nuisance when translating one, as the canvas element is initially empty
            // and shrinks to one line, messing up the whole layout.
            if (!event.target || !(event.target as Element).closest) return;
            const topBox = (event.target as Element).closest(
                kCanvasElementSelector
            ) as HTMLElement;
            if (!topBox) return;
            topBox.classList.remove("bloom-allowAutoShrink");
        };
    }

    // Move all child canvas elements as necessary so they are at least partly inside their container
    // (by as much as we require when dragging them).
    public ensureCanvasElementsIntersectParent(parentContainer: HTMLElement) {
        const canvasElements = Array.from(
            parentContainer.getElementsByClassName(kCanvasElementClass)
        );
        let changed = false;
        canvasElements.forEach(canvasElement => {
            const canvasElementRect = canvasElement.getBoundingClientRect();
            // If the canvas element is not visible, its width will be 0. Don't try to adjust it.
            if (canvasElementRect.width === 0) return;
            this.adjustCanvasElementLocation(
                canvasElement as HTMLElement,
                parentContainer,
                new Point(
                    canvasElementRect.left,
                    canvasElementRect.top,
                    PointScaling.Scaled,
                    "ensureCanvasElementsIntersectParent"
                )
            );
            changed = this.ensureTailsInsideParent(
                parentContainer,
                canvasElement as HTMLElement,
                changed
            );
        });
        if (changed) {
            Comical.update(parentContainer);
        }
    }

    // Make sure the handles of the tail(s) of the canvas element are within the container.
    // Return true if any tail was changed (or if changed was already true)
    private ensureTailsInsideParent(
        bloomCanvas: HTMLElement,
        canvasElement: HTMLElement,
        changed: boolean
    ) {
        const originalTailSpecs = Bubble.getBubbleSpec(canvasElement).tails;
        const newTails = originalTailSpecs.map(spec => {
            const tipPoint = this.adjustRelativePointToBloomCanvas(
                bloomCanvas,
                new Point(
                    spec.tipX,
                    spec.tipY,
                    PointScaling.Unscaled,
                    "ensureTailsInsideParent.tip"
                )
            );
            const midPoint = this.adjustRelativePointToBloomCanvas(
                bloomCanvas,
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
        const bubble = new Bubble(canvasElement);
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
    //   which means a canvas element might need to move as a result of changing its bubble type.
    private minCanvasElementVisible = 10;

    // Conceptually, move the canvas element to the specified location (which may be where it is already).
    // However, first adjust the location to make sure at least a little of the canvas element is visible
    // within the specified container. (This means the method may be used both to constrain moving
    // the canvas element, and also, by passing its current location, to ensure it becomes visible if
    // it somehow stopped being.)
    private adjustCanvasElementLocation(
        canvasElement: HTMLElement,
        container: HTMLElement,
        location: Point
    ) {
        const canvasElementRect = canvasElement.getBoundingClientRect();
        const parentRect = container.getBoundingClientRect();
        const left = location.getScaledX();
        const right = left + canvasElementRect.width;
        const top = location.getScaledY();
        const bottom = top + canvasElementRect.height;
        let x = left;
        let y = top;
        if (right < parentRect.left + this.minCanvasElementVisible) {
            x =
                parentRect.left +
                this.minCanvasElementVisible -
                canvasElementRect.width;
        }
        if (left > parentRect.right - this.minCanvasElementVisible) {
            x = parentRect.right - this.minCanvasElementVisible;
        }
        if (bottom < parentRect.top + this.minCanvasElementVisible) {
            y =
                parentRect.top +
                this.minCanvasElementVisible -
                canvasElementRect.height;
        }
        if (top > parentRect.bottom - this.minCanvasElementVisible) {
            y = parentRect.bottom - this.minCanvasElementVisible;
        }
        // The 0.1 here is rather arbitrary. On the one hand, I don't want to do all the work
        // of placeElementAtPosition in the rather common case that we're just checking canvas element
        // positions at startup and none need to move. On the other hand, we're dealing with scaling
        // here, and it's possible that even a half pixel might get scaled so that the difference
        // is noticeable. I'm compromizing on a discrepancy that is less than a pixel at our highest
        // zoom.
        if (
            Math.abs(x - canvasElementRect.left) > 0.1 ||
            Math.abs(y - canvasElementRect.top) > 0.1
        ) {
            const moveTo = new Point(
                x,
                y,
                PointScaling.Scaled,
                "AdjustCanvasElementLocation"
            );
            this.placeElementAtPosition($(canvasElement), container, moveTo);
        }
        this.alignControlFrameWithActiveElement();
    }

    // Move the text insertion point to the specified location.
    // This is what a click at that location would typically do, but we are intercepting
    // those events to turn the click into a drag of the canvas element if there is mouse movement.
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
    private clientXAtMouseDown: number;
    private clientYAtMouseDown: number;
    private mouseDownContainer: HTMLElement;

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseDown = (event: MouseEvent) => {
        this.activeElementAtMouseDown = this.activeElement;
        const bloomCanvas = event.currentTarget as HTMLElement;
        // Let standard clicks on the bloom editable or other UI elements only be processed by that element
        if (this.isMouseEventAlreadyHandled(event)) {
            return;
        }
        this.gotAMoveWhileMouseDown = false;
        this.mouseIsDown = true;
        this.clientXAtMouseDown = event.clientX;
        this.clientYAtMouseDown = event.clientY;
        this.mouseDownContainer = bloomCanvas;
        // Adding this to document rather than the container makes it much less likely that we'll miss
        // the mouse up. Also, we only add it at all if the mouse down happened on an appropriate target.
        // Mouse up also wants to be limited to appropriate targets, but when dragging (especially
        // a jquery resize of a motion rectangle) it's easy for the mouse up to be outside the
        // thing originally clicked on. Addding it here means that the test for whether it's a click
        // this set of functions should handle is not needed in onMouseUp; only if we decide here that it's
        // ours to handle will the mouse up handler even be added.
        // (I'd like to do the same with mouse move but we still have some hover effects.)
        document.addEventListener("mouseup", this.onMouseUp, {
            capture: true
        });

        // These coordinates need to be relative to the canvas (which is the same as relative to the bloomCanvas).
        const coordinates = this.getPointRelativeToCanvas(event, bloomCanvas);

        if (!coordinates) {
            return;
        }

        const bubble = Comical.getBubbleHit(
            bloomCanvas,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY(),
            true // only consider canvas elements with pointer events allowed.
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
            renderCanvasElementContextControls(bubble.content, true, {
                left: event.clientX,
                top: event.clientY
            });
            return;
        }

        if (
            Comical.isDraggableNear(
                bloomCanvas,
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
                // using this trick for a canvas element that is part of a family doesn't work well.
                // We can only drag one canvas element at once, so where should we put the other duplicate?
                // Maybe we can come up with an answer, but for now, I'm just going to ignore the alt key.
                if (Comical.findRelatives(bubble).length === 0) {
                    // duplicate the canvas element and drag that.
                    // currently duplicateCanvasElementBox actually dupliates the current active element,
                    // not the one it is passed. So make sure the one we clicked is active, though it won't be for long.
                    this.setActiveElement(bubble.content);
                    const newCanvasElement = this.duplicateCanvasElementBox(
                        bubble.content,
                        true
                    );
                    if (!newCanvasElement) return;
                    startDraggingBubble(new Bubble(newCanvasElement));
                    return;
                }
            }
            // We clicked on a canvas element that's not disabled. If we clicked inside the canvas element we are
            // text editing, and neither ctrl nor alt is down, we handle it normally. Otherwise, we
            // need to suppress. If we're outside the editable but inside the canvas element, we don't need any default event processing,
            // and if we're inside and ctrl or alt is down, we want to prevent the events being
            // processed by the text. And if we're inside a canvas element not yet recognized as the one we're
            // editing, we want to suppress the event because, unless it turns out to be a simple click
            // with no movement, we're going to treat it as dragging the canvas element.
            const clickOnCanvasElementWeAreEditing =
                this.theCanvasElementWeAreTextEditing ===
                    (event.target as HTMLElement)?.closest(
                        kCanvasElementSelector
                    ) && this.theCanvasElementWeAreTextEditing;
            if (
                event.altKey ||
                event.ctrlKey ||
                !clickOnCanvasElementWeAreEditing
            ) {
                event.preventDefault();
                event.stopPropagation();
            }
            if (bubble.content.classList.contains(kBackgroundImageClass)) {
                this.setActiveElement(bubble.content); // usually done by startDraggingBubble, but we're not going to drag it.
                return; // these can't be dragged, they are locked to a computed position like content-fit.
            }
            startDraggingBubble(bubble);
        }
    };

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    private onMouseMove = (event: MouseEvent) => {
        if (
            CanvasElementManager.inPlayMode(event.currentTarget as HTMLElement)
        ) {
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
        const deltaX = event.clientX - this.clientXAtMouseDown;
        const deltaY = event.clientY - this.clientYAtMouseDown;
        if (
            event.buttons === 1 &&
            Math.sqrt(deltaX * deltaX + deltaY * deltaY) > 3
        ) {
            this.gotAMoveWhileMouseDown = true;
            this.startMoving();
        }
        if (!this.gotAMoveWhileMouseDown) {
            return; // don't actually move until the distance is enough to be sure it's not a click.
        }

        const container = event.currentTarget as HTMLElement;

        if (!this.bubbleToDrag) {
            this.handleMouseMoveHover(event, container);
        } else if (this.bubbleToDrag) {
            this.handleMouseMoveDragCanvasElement(event, container);
        }
    };

    // Add the classes that let various controls know that a move, resize, or drag is in progress.
    private startMoving() {
        const controlFrame = document.getElementById(
            "canvas-element-control-frame"
        );
        controlFrame?.classList?.add("moving");
        this.activeElement?.classList?.add("moving");
        document
            .getElementById("canvas-element-context-controls")
            ?.classList?.add("moving");
    }

    // Mouse hover - No move or resize is currently active, but check if there is a canvas element under the mouse that COULD be
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
            // The hovered canvas element is not selected. If it's an image, the user might
            // want to drag a tail tip there, which is hard to do with a grab cursor,
            // so don't switch.
            if (this.isPictureCanvasElement(hoveredBubble.content)) {
                hoveredBubble = null;
            }
        }
    }

    /**
     * Gets the canvas element under the mouse location, or null if no canvas element is
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

    // A canvas element is currently in drag mode, and the mouse is being moved.
    // Move the canvas element accordingly.
    private handleMouseMoveDragCanvasElement(
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
            const bloomCanvas = this.activeElement.parentElement?.closest(
                kBloomCanvasSelector
            );
            if (bloomCanvas) {
                const canvas = this.getFirstCanvasForContainer(bloomCanvas);
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
                "Created by handleMouseMoveDragCanvasElement()"
            );
            this.adjustCanvasElementLocation(
                this.bubbleToDrag.content,
                this.lastMoveContainer,
                newPosition
            );
            this.lastCropControl = undefined; // move resets the basis for cropping
            this.animationFrame = 0;
        });
    }

    // The center handle, used to move the picture under the canvas element, does nothing
    // unless the canvas element has actually been cropped. Unless we figure out something
    // sensible to do in this case, it's better not to show it, lest the user be
    // confused by a control that does nothing.
    private adjustMoveCropHandleVisibility(removeCropAttrsIfNotNeeded = false) {
        const controlFrame = document.getElementById(
            "canvas-element-control-frame"
        );
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
            if (!wantMoveCropHandle && removeCropAttrsIfNotNeeded) {
                // remove the width, top, left styles that indicate cropping
                img.style.width = "";
                img.style.top = "";
                img.style.left = "";
            }
        }
        controlFrame.classList.toggle(
            "bloom-ui-canvas-element-show-move-crop-handle",
            wantMoveCropHandle
        );
        this.updateCurrentlyCropped();
    }

    private stopMoving() {
        if (this.lastMoveContainer) this.lastMoveContainer.style.cursor = "";
        // We want to get rid of it at least from the control frame and the active canvas element,
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
        document.removeEventListener("mouseup", this.onMouseUp, {
            capture: true
        });
        if (CanvasElementManager.inPlayMode(this.mouseDownContainer)) {
            return;
        }
        this.stopMoving();
        if (
            !this.gotAMoveWhileMouseDown &&
            (event.target as HTMLElement).closest(".bloom-videoPlayIcon")
        ) {
            handlePlayClick(event, true);
            return;
        }

        if (this.bubbleToDrag) {
            // if we're doing a resize or drag, we don't want ordinary mouseup activity
            // on the text inside the canvas element.
            event.preventDefault();
            event.stopPropagation();
        }

        this.bubbleToDrag = undefined;
        this.mouseDownContainer.classList.remove("grabbing");
        this.turnOffResizing(this.mouseDownContainer);
        const editable = (event.target as HTMLElement)?.closest(
            ".bloom-editable"
        );
        if (
            editable &&
            editable.closest(kCanvasElementSelector) ===
                this.theCanvasElementWeAreTextEditing
        ) {
            // We're text editing in this canvas element, let the mouse do its normal things.
            // In particular, we don't want to do moveInsertionPointAndFocusTo here,
            // because it will force the selection back to an IP when we might want a range
            // (e.g., after a double-click).
            // (But note, if we started out with the canvas element not active, a double click
            // is properly interpreted as one click to select the canvas element, one to put it
            // into edit mode...that is NOT a regular double-click that selects a word.
            // At least, that seems to be what Canva does.)
            return;
        }
        // a click without movement on a canvas element that is already the active one puts it in edit mode.
        if (
            !this.gotAMoveWhileMouseDown &&
            editable &&
            this.activeElementAtMouseDown === this.activeElement
        ) {
            // Going into edit mode on this canvas element.
            this.theCanvasElementWeAreTextEditing = (event.target as HTMLElement)?.closest(
                kCanvasElementSelector
            ) as HTMLElement;
            this.theCanvasElementWeAreTextEditing?.classList.add(
                "bloom-focusedCanvasElement"
            );
            // We want to position the IP as if the user clicked where they did.
            // Since we already suppressed the mouseDown event, it's not enough to just
            // NOT suppress the mouseUp event. We need to actually move the IP to the
            // appropriate spot and give the canvas element focus.
            this.moveInsertionPointAndFocusTo(event.clientX, event.clientY);
        } else {
            // prevent the click giving it focus (or any other default behavior). This mouse up
            // is part of dragging a canvas element or resizing it or some similar special behavior that
            // we are handling.
            event.preventDefault();
            event.stopPropagation();
        }
    };

    public turnOffResizing(container: Element) {}

    // If we get a click (without movement) on a text canvas element, we treat subsequent mouse events on
    // that canvas element as text editing events, rather than drag events, as long as it keeps focus.
    // This is the canvas element, if any, that is currently in that state.
    public theCanvasElementWeAreTextEditing: HTMLElement | undefined;
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
        if (CanvasElementManager.inPlayMode(targetElement)) {
            // Game in play mode...no edit mode functionality is relevant
            return true;
        }
        if (targetElement.classList.contains("changeImageButton")) {
            // The change image button should handle the mouse event itself.  See BL-14614.
            return true;
        }
        if (targetElement.classList.contains("bloom-dragHandle")) {
            // The drag handle is outside the canvas element, so dragging it with the mouse
            // events we handle doesn't work. Returning true lets its own event handler
            // deal with things, and is a good thing even when ctrl or alt is down.
            return true;
        }
        if (
            targetElement.closest("#animationEnd") ||
            targetElement.closest("#animationStart")
        ) {
            // These are used by the motion tool rectangles. Don't want canvas element code
            // interfering.
            return true;
        }
        if (targetElement.classList.contains("ui-resizable-handle")) {
            // Ignore clicks on the JQuery resize handles.
            return true;
        }
        if (targetElement.closest("#canvas-element-control-frame")) {
            // New drag controls
            return true;
        }
        if (targetElement.closest("[data-target-of")) {
            // Bloom game targets want to handle their own dragging.
            return true;
        }
        if (
            targetElement.closest(".bloom-videoReplayIcon") ||
            targetElement.closest(".bloom-videoPauseIcon")
        ) {
            // The play button has special code in onMouseUp to handle a click on it.
            // It does NOT have its own click handler (in canvas elements), because we want to allow the canvas element
            // to be dragged normally if a mouseDown on it is followed by sufficient mouse
            // movement to be considered a drag.
            // But I decided not to do that for the other two buttons, which only appear
            // when the video is playing after a click on the play button. They have normal
            // click handlers, and we don't want our mouse down/move/up handlers to respond
            // when they are clicked.
            return true;
        }
        if (ev.ctrlKey || ev.altKey) {
            return false;
        }
        const editable = targetElement.closest(".bloom-editable");
        if (
            editable &&
            this.theCanvasElementWeAreTextEditing &&
            this.theCanvasElementWeAreTextEditing.contains(editable) &&
            ev.button !== 2
        ) {
            // an editable is allowed to handle its own events only if it's parent canvas element has
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

        return CanvasElementManager.convertPointFromViewportToElementFrame(
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
        const border = CanvasElementManager.getLeftAndTopBorderWidths(element);
        const padding = CanvasElementManager.getLeftAndTopPaddings(element);
        const borderAndPadding = border.add(padding);

        // Try not to be scrolled. It's not easy to figure out how to adjust the calculations
        // properly across all zoom levels if the box is scrolled.
        const scroll = CanvasElementManager.getScrollAmount(element);
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

        const borderRight: number = CanvasElementManager.extractNumber(
            styleInfo.getPropertyValue("border-right-width")
        );
        const borderBottom: number = CanvasElementManager.extractNumber(
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
            PointScaling.Unscaled,
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

    public turnOffCanvasElementEditing(): void {
        if (this.isCanvasElementEditingOn === false) {
            return; // Already off. No work needs to be done.
        }
        this.isCanvasElementEditingOn = false;
        this.removeControlFrame();
        this.removeFocusClass();

        Comical.setActiveBubbleListener(undefined);
        Comical.stopEditing();
        this.getAllBloomCanvasesOnPage().forEach(bloomCanvas =>
            this.saveCurrentCanvasElementStateAsCurrentLangAlternate(
                bloomCanvas as HTMLElement
            )
        );

        EnableAllImageEditing();

        // Clean up event listeners that we no longer need
        Array.from(
            document.getElementsByClassName(kCanvasElementClass)
        ).forEach(container => {
            const editables = this.getAllVisibileEditableDivs(
                container as HTMLElement
            );
            editables.forEach(element => {
                // Don't use an arrow function as an event handler here. These can never be identified as duplicate event listeners, so we'll end up with tons of duplicates
                element.removeEventListener(
                    "focusin",
                    CanvasElementManager.onFocusSetActiveElement
                );
            });
        });
        document.removeEventListener(
            "click",
            CanvasElementManager.onDocClickClearActiveElement
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

    public requestCanvasElementChangeNotification(
        id: string,
        notifier: (bubble: BubbleSpec | undefined) => void
    ): void {
        this.detachCanvasElementChangeNotification(id);
        this.thingsToNotifyOfCanvasElementChange.push({
            id,
            handler: notifier
        });
    }

    public detachCanvasElementChangeNotification(id: string): void {
        const index = this.thingsToNotifyOfCanvasElementChange.findIndex(
            x => x.id === id
        );
        if (index >= 0) {
            this.thingsToNotifyOfCanvasElementChange.splice(index, 1);
        }
    }

    public updateSelectedItemBubbleSpec(
        newBubbleProps: BubbleSpecPattern
    ): BubbleSpec | undefined {
        if (!this.activeElement) {
            return undefined;
        }

        // ENHANCE: Constructing new canvas element instances is dangerous. It may get out of sync with the instance that Comical knows about.
        // It would be preferable if we asked Comical to find the canvas element instance corresponding to this element.
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

        // BL-9548: Interaction with the toolbox panel makes the canvas element lose focus, which requires
        // we re-activate the current comical element.
        Comical.activateElement(this.activeElement);

        return bubble.getBubbleSpec();
    }

    // Adjust the ordering of canvas elements so that draggables are at the end.
    // We want the things that can be moved around to be on top of the ones that can't.
    // We don't use z-index because that makes stacking contexts and interferes with
    // the way we keep canvas element children on top of the canvas.
    // Bubble levels should be consistent with the order of the elements in the DOM,
    // since the former controls which one is treated as being clicked when there is overlap,
    // while the latter determines which is on top.
    public adjustCanvasElementOrdering = () => {
        const bloomCanvases = this.getAllBloomCanvasesOnPage();
        bloomCanvases.forEach(bloomCanvas => {
            const canvasElements = Array.from(
                bloomCanvas.getElementsByClassName(kCanvasElementClass)
            );
            let maxLevel = Math.max(
                ...canvasElements.map(
                    b => Bubble.getBubbleSpec(b as HTMLElement).level ?? 0
                )
            );
            const draggables = canvasElements.filter(b =>
                b.getAttribute("data-draggable-id")
            );
            if (
                draggables.length === 0 ||
                canvasElements.indexOf(draggables[0]) ===
                    canvasElements.length - draggables.length
            ) {
                return; // already all at end (or none to move)
            }
            // Move them to the end, keeping them in order.
            draggables.forEach(draggable => {
                draggable.parentElement?.appendChild(draggable);
                const bubble = new Bubble(draggable as HTMLElement);
                // This would need to get fancier if draggables came in groups with the same level.
                // As it is, we just want their levels to be in the same order as their DOM order
                // (relative to each other and the other canvas elements) so getBubbleHit() will return
                // the one that appears on top when they are stacked.
                bubble.getBubbleSpec().level = maxLevel + 1;
                bubble.persistBubbleSpec();
                maxLevel++;
            });
            Comical.update(bloomCanvas);
        });
    };

    // Adds a new canvas element as a child of the specified {parentElement}
    //    (It is a child in the sense that the Comical library will recognize it as a child)
    // {offsetX}/{offsetY} is the offset in position from the parent to the child elements
    //    (i.e., offsetX = child.left - parent.left)
    //    (remember that positive values of Y are further to the bottom)
    // This is what the comic tool calls when the user clicks ADD CHILD BUBBLE.
    public addChildCanvasElementAndRefreshPage(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number
    ): void {
        // The only reason to keep a separate method here is that the 'internal' form returns
        // the new child. We don't need it here, but we do in the duplicate canvas element function.
        this.addChildInternal(parentElement, offsetX, offsetY);
    }

    // Make sure comical is up-to-date in the case where we know there is a selected/current element.
    private updateComicalForSelectedElement(element: HTMLElement) {
        if (!element) {
            return;
        }
        const bloomCanvas = CanvasElementManager.getBloomCanvas(element);
        if (!bloomCanvas) {
            return; // shouldn't happen...
        }
        const comicalGenerated = bloomCanvas.getElementsByClassName(
            kComicalGeneratedClass
        );
        if (comicalGenerated.length > 0) {
            Comical.update(bloomCanvas);
        }
    }

    private addChildInternal(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number
    ): HTMLElement | undefined {
        // Make sure everything in parent is "saved".
        this.updateComicalForSelectedElement(parentElement);

        const newPoint = this.findBestLocationForNewCanvasElement(
            parentElement,
            offsetX,
            offsetY
        );
        if (!newPoint) {
            return undefined;
        }

        const childElement = this.addCanvasElement(
            newPoint.getScaledX(),
            newPoint.getScaledY(),
            undefined
        );
        if (!childElement) {
            return undefined;
        }

        // Make sure that the child inherits any non-default text color from the parent canvas element
        // (which must be the active element).
        this.setActiveElement(parentElement);
        const parentTextColor = this.getTextColorInformation();
        if (!parentTextColor.isDefault) {
            this.setTextColorInternal(parentTextColor.color, childElement);
        }

        Comical.initializeChild(childElement, parentElement);
        // In this case, the 'addCanvasElement()' above will already have done the new canvas element's
        // refresh. We still want to refresh, but not attach to ckeditor, etc., so we pass
        // attachEventsToEditables as false.
        this.refreshCanvasElementEditing(
            CanvasElementManager.getBloomCanvas(parentElement)!,
            new Bubble(childElement),
            false,
            true
        );
        return childElement;
    }

    // The 'new canvas element' is either going to be a child of the 'parentElement', or a duplicate of it.
    private findBestLocationForNewCanvasElement(
        parentElement: HTMLElement,
        proposedOffsetX: number,
        proposedOffsetY: number
    ): Point | undefined {
        const parentBoundingRect = parentElement.getBoundingClientRect();

        // // Ensure newX and newY is within the bounds of the container.
        const bloomCanvas = CanvasElementManager.getBloomCanvas(parentElement);
        if (!bloomCanvas) {
            //toastr.warning("Failed to create child or duplicate element.");
            return undefined;
        }
        return this.adjustRectToBloomCanvas(
            bloomCanvas,
            parentBoundingRect.left + proposedOffsetX,
            parentBoundingRect.top + proposedOffsetY,
            parentElement.clientWidth,
            parentElement.clientHeight
        );
    }

    private adjustRectToBloomCanvas(
        bloomCanvas: Element,
        x: number,
        y: number,
        width: number,
        height: number
    ): Point {
        const containerBoundingRect = bloomCanvas.getBoundingClientRect();
        let newX = x;
        let newY = y;

        const bufferPixels = 15;
        if (newX < containerBoundingRect.left) {
            newX = containerBoundingRect.left + bufferPixels;
        } else if (newX + width > containerBoundingRect.right) {
            // ENHANCE: parentElement.clientWidth is just an estimate of the size of the new canvas element's width.
            //          It would be better if we could actually plug in the real value of the new canvas element's width
            newX = containerBoundingRect.right - width;
        }

        if (newY < containerBoundingRect.top) {
            newY = containerBoundingRect.top + bufferPixels;
        } else if (newY + height > containerBoundingRect.bottom) {
            // ENHANCE: parentElement.clientHeight is just an estimate of the size of the new canvas element's height.
            //          It would be better if we could actually plug in the real value of the new canvas element's height
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
    // here are already relative to the bloom-canvas's coordinates, which introduces some differences.
    private adjustRelativePointToBloomCanvas(
        bloomCanvas: Element,
        point: Point
    ): Point {
        const maxWidth = (bloomCanvas as HTMLElement).offsetWidth;
        const maxHeight = (bloomCanvas as HTMLElement).offsetHeight;
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

    public addCanvasElementWithScreenCoords(
        screenX: number,
        screenY: number,
        canvasElementType: CanvasElementType,
        userDefinedStyleName?: string,
        rightTopOffset?: string
    ): HTMLElement | undefined {
        const clientX = screenX - window.screenX;
        const clientY = screenY - window.screenY;
        return this.addCanvasElement(
            clientX,
            clientY,
            canvasElementType,
            userDefinedStyleName,
            rightTopOffset
        );
    }

    private addCanvasElementFromOriginal(
        offsetX: number,
        offsetY: number,
        originalElement: HTMLElement,
        style?: string
    ): HTMLElement | undefined {
        const bloomCanvas = CanvasElementManager.getBloomCanvas(
            originalElement
        );
        if (!bloomCanvas) {
            return undefined;
        }
        const positionInViewport = new Point(
            offsetX,
            offsetY,
            PointScaling.Scaled,
            "Scaled Viewport coordinates"
        );
        // Detect if the original is a picture over picture or video over picture element.
        if (this.isPictureCanvasElement(originalElement)) {
            return this.addPictureCanvasElement(
                positionInViewport,
                $(bloomCanvas)
            );
        }
        if (this.isVideoCanvasElement(originalElement)) {
            return this.addVideoCanvasElement(
                positionInViewport,
                $(bloomCanvas)
            );
        }
        return this.addCanvasElementCore(
            positionInViewport,
            $(bloomCanvas),
            style
        );
    }

    private isCanvasElementWithClass(
        canvasElement: HTMLElement,
        className: string
    ): boolean {
        for (let i = 0; i < canvasElement.childElementCount; i++) {
            const child = canvasElement.children[i] as HTMLElement;
            if (child && child.classList.contains(className)) {
                return true;
            }
        }
        return false;
    }

    public isActiveElementPictureCanvasElement(): boolean {
        if (!this.activeElement) {
            return false;
        }
        return this.isPictureCanvasElement(this.activeElement);
    }

    private isPictureCanvasElement(canvasElement: HTMLElement): boolean {
        return this.isCanvasElementWithClass(
            canvasElement,
            kImageContainerClass
        );
    }

    private isVideoCanvasElement(canvasElement: HTMLElement): boolean {
        return this.isCanvasElementWithClass(
            canvasElement,
            kVideoContainerClass
        );
    }

    public isActiveElementVideoCanvasElement(): boolean {
        if (!this.activeElement) {
            return false;
        }
        return this.isVideoCanvasElement(this.activeElement);
    }

    // This method is called when the user "drops" a canvas element from a tool onto an image.
    // It is also called by addChildInternal() and by the Linux version of dropping: "ondragend".
    public addCanvasElement(
        mouseX: number,
        mouseY: number,
        canvasElementType?: CanvasElementType,
        userDefinedStyleName?: string,
        rightTopOffset?: string
    ): HTMLElement | undefined {
        const bloomCanvas = this.getBloomCanvasFromMouse(mouseX, mouseY);
        if (!bloomCanvas || bloomCanvas.length === 0) {
            // Don't add a canvas element if we can't find the containing bloom-canvas.
            return undefined;
        }
        // initial mouseX, mouseY coordinates are relative to viewport
        const positionInViewport = new Point(
            mouseX,
            mouseY,
            PointScaling.Scaled,
            "Scaled Viewport coordinates"
        );
        if (canvasElementType === "video") {
            return this.addVideoCanvasElement(
                positionInViewport,
                bloomCanvas,
                rightTopOffset
            );
        }
        if (canvasElementType === "image") {
            return this.addPictureCanvasElement(
                positionInViewport,
                bloomCanvas,
                rightTopOffset
            );
        }
        if (canvasElementType === "sound") {
            return this.addSoundCanvasElement(
                positionInViewport,
                bloomCanvas,
                rightTopOffset
            );
        }
        if (canvasElementType === "rectangle") {
            return this.addRectangleCanvasElement(
                positionInViewport,
                bloomCanvas,
                rightTopOffset
            );
        }
        return this.addCanvasElementCore(
            positionInViewport,
            bloomCanvas,
            canvasElementType,
            userDefinedStyleName,
            rightTopOffset
        );
    }

    private addCanvasElementCore(
        location: Point,
        bloomCanvasJQuery: JQuery,
        style?: string,
        userDefinedStyleName?: string,
        rightTopOffset?: string
    ): HTMLElement {
        const defaultNewTextLanguage = GetSettings().languageForNewTextBoxes;
        const userDefinedStyle = userDefinedStyleName ?? "Bubble";
        // add a draggable text canvas element to the html dom of the current page
        const editableDivClasses = `bloom-editable bloom-content1 bloom-visibility-code-on ${userDefinedStyle}-style`;
        const editableDivHtml =
            "<div class='" +
            editableDivClasses +
            "' lang='" +
            defaultNewTextLanguage +
            "'><p></p></div>";

        const transGroupDivClasses = `bloom-translationGroup bloom-leadingElement`;
        const transGroupHtml =
            "<div class='" +
            transGroupDivClasses +
            "' data-default-languages='V'>" +
            editableDivHtml +
            "</div>";

        return this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            transGroupHtml,
            location,
            style,
            false,
            rightTopOffset
        );
    }

    private addVideoCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string
    ): HTMLElement {
        const standardVideoClasses =
            kVideoContainerClass +
            " bloom-noVideoSelected bloom-leadingElement";
        const videoContainerHtml =
            "<div class='" + standardVideoClasses + "' tabindex='0'></div>";
        return this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            videoContainerHtml,
            location,
            "none",
            true,
            rightTopOffset
        );
    }

    private addPictureCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
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
        return this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            imageContainerHtml,
            location,
            "none",
            true,
            rightTopOffset
        );
    }

    private addSoundCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string
    ): HTMLElement {
        const standardImageClasses =
            kImageContainerClass + " bloom-leadingElement";
        // This svg is basically the same as the one in AudioIcon.tsx.
        // Likely, changes to one should be mirrored in the other.
        //
        // The data-icon-type is so we can, in the future, find these and migrate/update them.
        const html = `<div tabindex='0' class='bloom-unmodifiable-image bloom-svg ${standardImageClasses}' data-icon-type='audio'>
    <svg
        viewBox="0 0 31 31"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
    >
        <rect
            width="31"
            height="31"
            rx="1.81232"
            fill="var(--draggable-background-color, black)"
        />
        <path
            d="M23.0403 9.12744C24.8868 10.8177 25.9241 13.11 25.9241 15.5C25.9241 17.8901 24.8868 20.1823 23.0403 21.8726M19.5634 12.3092C20.4867 13.1544 21.0053 14.3005 21.0053 15.4955C21.0053 16.6906 20.4867 17.8367 19.5634 18.6818M15.0917 9.19054L10.1669 12.796H6.22705V18.2041H10.1669L15.0917 21.8095V9.19054Z"
            stroke="var(--draggable-color, white)"
            strokeWidth="1.15865"
            strokeLinecap="round"
            strokeLinejoin="round"
        />
    </svg>
</div>`;
        return this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            html,
            location,
            "none",
            true,
            rightTopOffset
        );
    }

    private addRectangleCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string
    ): HTMLElement {
        const html =
            // The tabindex here is necessary to allow it to be focused.
            "<div tabindex='0' class='bloom-rectangle'></div>";
        const result = this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            html,
            location,
            "none",
            true,
            rightTopOffset
        );
        // reorder it after the element with class kBackgroundImageClass. This puts it in front of
        // the background but but behind the other canvas elements it is meant to frame.
        this.reorderRectangleCanvasElement(result, bloomCanvasJQuery.get(0));
        return result;
    }

    // Put the rectangle in the right place in the DOM so it is behind the other canvas elements
    // but in front of the background image.  Also adjust the ComicalJS bubble level so it is in
    // front of the the background image.
    private reorderRectangleCanvasElement(
        rectangle: HTMLElement,
        bloomCanvas: HTMLElement
    ): void {
        const backgroundImage = bloomCanvas.getElementsByClassName(
            kBackgroundImageClass
        )[0] as HTMLElement;
        if (backgroundImage) {
            bloomCanvas.insertBefore(rectangle, backgroundImage.nextSibling);
            // Being first in document order gives it the right z-order, but it also has to be
            // in the right sequence by ComicalJs Bubble level for the hit test to work right.
            CanvasElementManager.putBubbleBefore(
                rectangle,
                (Array.from(
                    bloomCanvas.getElementsByClassName(kCanvasElementClass)
                ) as HTMLElement[]).filter(x => x !== backgroundImage),
                Bubble.getBubbleSpec(backgroundImage).level + 1
            );
        }
    }

    private finishAddingCanvasElement(
        bloomCanvasJQuery: JQuery,
        internalHtml: string,
        location: Point,
        comicalBubbleStyle?: string,
        setElementActive?: boolean,
        rightTopOffset?: string
    ): HTMLElement {
        // add canvas element as last child of .bloom-canvas (BL-7883)
        const lastChildOfBloomCanvas = bloomCanvasJQuery.children().last();
        const canvasElementHtml =
            "<div class='" +
            kCanvasElementClass +
            "'>" +
            internalHtml +
            "</div>";
        // It's especially important that the new canvas element comes AFTER the main image,
        // since that's all that keeps it on top of the image. We're deliberately not
        // using z-index so that the bloom-canvas is not a stacking context so we
        // can use z-index on the buttons inside it to put them above the comicaljs canvas.
        const canvasElementJQuery = $(canvasElementHtml).insertAfter(
            lastChildOfBloomCanvas
        );
        const canvasElement = canvasElementJQuery.get(0);
        this.setDefaultHeightFromWidth(canvasElement);
        this.placeElementAtPosition(
            canvasElementJQuery,
            bloomCanvasJQuery.get(0),
            location,
            rightTopOffset
        );

        // The following code would not be needed for Picture and Video canvas elements if the focusin
        // handler were reliably called after being attached by refreshBubbleEditing() below.
        // However, calling the jquery.focus() method in bloomEditing.focusOnChildIfFound()
        // causes the handler to fire ONLY for Text canvas elements.  This is a complete mystery to me.
        // Therefore, for Picture and Video canvas elements, we set the content active and notify the
        // canvas element tool. But we don't need/want the actions of setActiveElement() which overlap
        // with refreshBubbleEditing(). This code actually prevents bloomEditing.focusOnChildIfFound()
        // from being called, but that doesn't really matter since calling it does no good.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-11620.
        if (setElementActive) {
            this.activeElement = canvasElement;
            this.doNotifyChange();
            this.showCorrespondingTextBox(canvasElement);
        }
        const bubble = new Bubble(canvasElement);
        const bubbleSpec: BubbleSpec = Bubble.getDefaultBubbleSpec(
            canvasElement,
            comicalBubbleStyle || "speech"
        );
        bubble.setBubbleSpec(bubbleSpec);
        const bloomCanvas = bloomCanvasJQuery.get(0);
        // background image in parent bloom-canvas may need to become canvas element
        // (before we refreshBubbleEditing, since we may change some canvas elements here.)
        this.handleResizeAdjustments();
        this.refreshCanvasElementEditing(bloomCanvas, bubble, true, true);
        const editable = canvasElement.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on"
        )[0] as HTMLElement;
        editable?.focus();
        return canvasElement;
    }

    // All of the text-based canvas elements' default heights are based on the min-height of 30px set
    // in overlayTool.less for a .bloom-canvas-element. For other elements, we usually want something else.
    public setDefaultHeightFromWidth(canvasElement: HTMLElement) {
        const width = parseInt(getComputedStyle(canvasElement).width, 10);

        if (
            canvasElement.querySelector(`.${kVideoContainerClass}`) !== null ||
            canvasElement.querySelector(`.bloom-rectangle`) !== null
        ) {
            // Set the default video aspect to 4:3, the same as the sign language tool generates.
            canvasElement.style.height = `${(width * 3) / 4}px`;
        } else if (
            canvasElement.querySelector(kImageContainerSelector) !== null
        ) {
            // Set the default image aspect to square.
            canvasElement.style.height = `${width}px`;
        }
    }

    // mouseX and mouseY are the location in the viewport of the mouse
    // The desired element might be covered by a .MuiModal-backdrop, so we may
    // need to check multiple elements at that location.
    private getBloomCanvasFromMouse(mouseX: number, mouseY: number): JQuery {
        const elements = document.elementsFromPoint(mouseX, mouseY);
        for (let i = 0; i < elements.length; i++) {
            const trial = CanvasElementManager.getBloomCanvas(elements[i]);
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
    // have access to the element being created to get its width. We could push it up
    // one level into finishAddingCanvasElement, but it's simpler here where we're
    // already extracting and adjusting the offsets from positionInViewport
    private placeElementAtPosition(
        wrapperBox: JQuery,
        container: Element,
        positionInViewport: Point,
        rightTopOffset?: string
    ) {
        const newPoint = CanvasElementManager.convertPointFromViewportToElementFrame(
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
            // The wrapperBox width seems to always be 140 at this point, but gets
            // changed before the dropped item displays.  Images (including videos and
            // GIFs) are positioned correctly if we assume their actual width is about 60
            // instead, so we need to adjust the xOffset by 80 pixels.  Text boxes are
            // positioned correctly if we assume their actual width is about 150 instead,
            // so we adjust their xOFfset by -10.  This is a bit of a hack, but it works.
            // I don't know how to get the actual width that will show up in the browser.
            // (The displayed widths for fixed images, videos, and GIFs are really not 60,
            // but they are positioned correctly if we treat them that way here.)
            // See BL-14594.
            let fudgeFactor = 80;
            if (wrapperBox.find(".bloom-translationGroup").length > 0) {
                fudgeFactor = -10;
            }
            xOffset = xOffset + right - wrapperBox.width() + fudgeFactor;
            yOffset = yOffset + top;
        }

        // Note: This code will not clear out the rest of the style properties... they are preserved.
        //       If some or all style properties need to be removed before doing this processing, it is the caller's responsibility to do so beforehand
        //       The reason why we do this is because a canvas element's onmousemove handler calls this function,
        //       and in that case we want to preserve the canvas element's width/height which are set in the style
        wrapperBox.css("left", xOffset); // assumes numbers are in pixels
        wrapperBox.css("top", yOffset); // assumes numbers are in pixels

        CanvasElementManager.setCanvasElementPosition(
            wrapperBox,
            xOffset,
            yOffset
        );

        this.adjustTarget(wrapperBox.get(0));
    }

    private adjustTarget(draggable: HTMLElement | undefined) {
        if (!draggable) {
            // I think this is just to remove the arrow if any.
            adjustTarget(document.firstElementChild as HTMLElement, undefined);
            return;
        }
        const targetId = draggable.getAttribute("data-draggable-id");
        const target = targetId
            ? document.querySelector(`[data-target-of="${targetId}"]`)
            : undefined;
        adjustTarget(draggable, target as HTMLElement);
    }

    // This used to be called from a right-click context menu, but now it only gets called
    // from the comicTool where we verify that we have an active element BEFORE calling this
    // method. That simplifies things here.
    public deleteCanvasElement(textOverPicDiv: HTMLElement) {
        // Simple guard, just in case.
        if (!textOverPicDiv || !textOverPicDiv.parentElement) {
            return;
        }
        if (textOverPicDiv.classList.contains(kBackgroundImageClass)) {
            // just revert it to a placeholder
            const img = getImageFromCanvasElement(textOverPicDiv);
            if (img) {
                img.classList.remove("bloom-imageLoadError");
                img.onerror = HandleImageError;
                img.src = "placeHolder.png";
                this.updateCanvasElementForChangedImage(img);
            }
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
        this.refreshCanvasElementEditing(
            containerElement,
            undefined,
            false,
            false
        );
        // We no longer have an active element, but the old active element may be
        // needed by the removeControlFrame method called by refreshCanvasElementEditing
        // to remove a popup menu.
        this.setActiveElement(undefined);
        // By this point it's really gone, so this will clean up if it had a target.
        this.removeDetachedTargets();
    }

    // We verify that 'textElement' is the active element before calling this method.
    public duplicateCanvasElementBox(
        textElement: HTMLElement,
        sameLocation?: boolean
    ): HTMLElement | undefined {
        // simple guard
        if (!textElement || !textElement.parentElement) {
            return undefined;
        }
        const bloomCanvas = textElement.parentElement;
        // Make sure comical is up-to-date before we clone things.
        if (
            bloomCanvas.getElementsByClassName(kComicalGeneratedClass).length >
            0
        ) {
            Comical.update(bloomCanvas);
        }
        // Get the patriarch canvas element of this comical family. Can only be undefined if no active element.
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

            const result = this.duplicateCanvasElementFamily(
                patriarchBubble,
                bubbleSpecToDuplicate,
                sameLocation
            );
            if (result) {
                const isRectangle =
                    result.getElementsByClassName("bloom-rectangle").length > 0;
                if (isRectangle) {
                    // adjust the new rectangle's z-order and comical level to match the original.
                    this.reorderRectangleCanvasElement(result, bloomCanvas);
                }
            }
            // The JQuery resizable event handler needs to be removed after the duplicate canvas element
            // family is created, and then the over picture editing needs to be initialized again.
            // See BL-13617.
            this.removeJQueryResizableWidget();
            this.initializeCanvasElementEditing();
            return result;
        }
        return undefined;
    }

    // Should duplicate all canvas elements and their size and relative placement and color, etc.,
    // and the actual text in the canvas elements.
    // The 'patriarchSourceBubble' is the head of a family of canvas elements to duplicate,
    // although this one canvas element may be all there is.
    // The content of 'patriarchSourceBubble' is now the active element.
    // The 'bubbleSpecToDuplicate' param is the bubbleSpec for the patriarch source canvas element.
    // The function returns the patriarch canvas element of the new
    // duplicated canvas element family.
    // This method handles all needed refreshing of the duplicate canvas elements.
    private duplicateCanvasElementFamily(
        patriarchSourceBubble: Bubble,
        bubbleSpecToDuplicate: BubbleSpec,
        sameLocation: boolean = false
    ): HTMLElement | undefined {
        const sourceElement = patriarchSourceBubble.content;
        const proposedOffset = 15;
        const newPoint = this.findBestLocationForNewCanvasElement(
            sourceElement,
            sameLocation ? 0 : proposedOffset + sourceElement.clientWidth, // try to not overlap too much
            sameLocation ? 0 : proposedOffset
        );
        if (!newPoint) {
            return;
        }
        const patriarchDuplicateElement = this.addCanvasElementFromOriginal(
            newPoint.getScaledX(),
            newPoint.getScaledY(),
            sourceElement,
            bubbleSpecToDuplicate.style
        );
        if (!patriarchDuplicateElement) {
            return;
        }
        patriarchDuplicateElement.classList.remove(kBackgroundImageClass);
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
        const container = CanvasElementManager.getBloomCanvas(
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
        // This is the bubbleSpec for the brand new (now active) copy of the patriarch canvas element.
        // We will overwrite most of it, but keep its level and version properties. The level will be
        // different so the copied canvas element(s) will be in a separate child chain from the original(s).
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
        // OK, now we're done with our manipulation of the patriarch canvas element and we're about to go on
        // and deal with the child canvas elements (if any). But we replaced the innerHTML after creating the
        // initial duplicate canvas element and the editable divs may not have the appropriate events attached,
        // so we'll refresh again with 'attachEventsToEditables' set to 'true'.
        this.refreshCanvasElementEditing(
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
            this.duplicateOneChildCanvasElement(
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
        bloomCanvas: Element,
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
            const tipPoint = this.adjustRelativePointToBloomCanvas(
                bloomCanvas,
                new Point(
                    spec.tipX + offSetFromSource.getUnscaledX(),
                    spec.tipY + offSetFromSource.getUnscaledY(),
                    PointScaling.Unscaled,
                    "getAdjustedTailSpec.tip"
                )
            );
            const midPoint = this.adjustRelativePointToBloomCanvas(
                bloomCanvas,
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

    private duplicateOneChildCanvasElement(
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
        // canvas element's HTML. This will undo any event handlers that might have been attached by the
        // refresh triggered by 'addChildInternal'. So we send the newly modified child through again,
        // with 'attachEventsToEditables' set to 'true'.
        this.refreshCanvasElementEditing(
            CanvasElementManager.getBloomCanvas(parentElement)!,
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
        // But for text-based canvas elements we need to delete positive tabindex, so we don't do weird
        // things to talking book playback order when we duplicate a family of canvas elements.
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
        | "forJqueryResize"
        | "forGamePlayMode" = "none";

    private splitterResizeObservers: ResizeObserver[] = [];
    public startDraggingSplitter() {
        this.getAllBloomCanvasesOnPage().forEach(bloomCanvas => {
            const backgroundCanvasElement = bloomCanvas.getElementsByClassName(
                kBackgroundImageClass
            )[0] as HTMLElement;
            if (backgroundCanvasElement) {
                // These two attributes are what the resize observer will mess with to make
                // the background resize as the splitter moves. We will restore them in
                // endDraggingSplitter so the code that adjusts all the canvas elements has the
                // correct starting size.
                backgroundCanvasElement.setAttribute(
                    "data-oldStyle",
                    backgroundCanvasElement.getAttribute("style") ?? ""
                );
                const img = getImageFromCanvasElement(backgroundCanvasElement);
                img?.setAttribute(
                    "data-oldStyle",
                    img.getAttribute("style") ?? ""
                );
                const resizeObserver = new ResizeObserver(() => {
                    this.adjustBackgroundImageSize(
                        bloomCanvas,
                        backgroundCanvasElement,
                        false
                    );
                });
                resizeObserver.observe(bloomCanvas);
                this.splitterResizeObservers.push(resizeObserver);
            }
        });
    }

    public endDraggingSplitter() {
        this.getAllBloomCanvasesOnPage().forEach(bloomCanvas => {
            const backgroundCanvasElement = bloomCanvas.getElementsByClassName(
                kBackgroundImageClass
            )[0] as HTMLElement;
            // We need to remove the results of the continuous adjustments so that we can make the change again,
            // but this time adjust all the other canvas elements with it.
            if (backgroundCanvasElement) {
                backgroundCanvasElement.setAttribute(
                    "style",
                    backgroundCanvasElement.getAttribute("data-oldStyle") ?? ""
                );
                backgroundCanvasElement.removeAttribute("data-oldStyle");
                const img = getImageFromCanvasElement(backgroundCanvasElement);
                img?.setAttribute(
                    "style",
                    img.getAttribute("data-oldStyle") ?? ""
                );
                img?.removeAttribute("data-oldStyle");
            }
            while (this.splitterResizeObservers.length) {
                this.splitterResizeObservers.pop()?.disconnect();
            }
        });
    }

    public suspendComicEditing(
        forWhat: "forDrag" | "forTool" | "forGamePlayMode" | "forJqueryResize"
    ) {
        if (!this.isCanvasElementEditingOn) {
            // Note that this prevents us from getting into one of the suspended states
            // unless it was on to begin with. Therefore a subsequent resume won't turn
            // it back on unless it was to start with.
            return;
        }
        this.turnOffCanvasElementEditing();
        if (forWhat === "forDrag" || forWhat === "forJqueryResize") {
            this.startDraggingSplitter();
        }

        if (forWhat === "forGamePlayMode") {
            const allCanvasElements = Array.from(
                document.getElementsByClassName(kCanvasElementClass)
            );
            // We don't want the user to be able to edit the text in the canvas elements while playing a game.
            // This doesn't need to be in the game prepareActivity because we remove contenteditable
            // from all elements when publishing a book.
            allCanvasElements.forEach(element => {
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
        if (
            this.comicEditingSuspendedState === "forDrag" ||
            this.comicEditingSuspendedState === "forJqueryResize"
        ) {
            this.endDraggingSplitter();
        }
        if (this.comicEditingSuspendedState === "forTool") {
            // after a forTool suspense, we might have new dividers to put handlers on.
            this.setupSplitterEventHandling();
        }
        if (this.comicEditingSuspendedState === "forGamePlayMode") {
            const allCanvasElements = Array.from(
                document.getElementsByClassName(kCanvasElementClass)
            );
            allCanvasElements.forEach(element => {
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
        this.turnOnCanvasElementEditing();
    }

    private draggingSplitter = false;

    // mouse down in an origami slider: if comic editing is on, remember that, and turn it off.
    private dividerMouseDown = (ev: Event) => {
        if (this.comicEditingSuspendedState === "forTool") {
            // We're in change layout mode. We want to get the usual behavior of any
            // existing images while dragging the splitter, but we don't need to turn
            // off comic editing since it already is.
            this.draggingSplitter = true;
            this.startDraggingSplitter();
        } else {
            // Unless we're suspended for some other reason, this will call startDraggingSplitter
            // after turning stuff off.
            this.suspendComicEditing("forDrag");
        }
    };

    public removeDetachedTargets() {
        const detachedTargets = Array.from(
            document.querySelectorAll("[data-target-of]")
        );
        const canvasElements = Array.from(
            document.querySelectorAll("[data-draggable-id]")
        );
        canvasElements.forEach(canvasElement => {
            const draggableId = canvasElement.getAttribute("data-draggable-id");
            if (draggableId) {
                const index = detachedTargets.findIndex(
                    (target: Element) =>
                        target.getAttribute("data-target-of") === draggableId
                );
                if (index > -1) {
                    detachedTargets.splice(index, 1); // not detached if draggable points to it
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
        if (this.comicEditingSuspendedState === "forDrag") {
            // The mousedown was in an origami slider.
            // Clean up and don't let the mouse up affect anything else.
            // (Note: we're not stopping IMMEDATE propagation, so another mouseup handler
            // on the document can remove the origami-drag class.)
            ev.preventDefault();
            ev.stopPropagation();
            setTimeout(() => {
                // in timeout to be sure that another mouseup handler will have removed
                // the origami-drag class from the body, so we can get the right
                // resize behavior when turning back on.
                this.resumeComicEditing();
            }, 0);
        } else if (this.draggingSplitter) {
            // dragging the splitter while in origami mode. We need to clean up
            // in the way resume normally does
            this.draggingSplitter = false;
            this.endDraggingSplitter();
            for (const bloomCanvas of this.getAllBloomCanvasesOnPage()) {
                this.AdjustChildrenIfSizeChanged(bloomCanvas);
            }
        }
    };

    public initializeCanvasElementEditing(): void {
        // This gets called in bloomEditable's SetupElements method. This is how it gets set up on page
        // load, so that canvas element editing works even when the Canvas element tool is not active. So it definitely
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
        this.cleanupCanvasElements();

        this.setupSplitterEventHandling();

        this.turnOnCanvasElementEditing();
    }

    // When dragging origami sliders, turn comical off.
    // With this, we get some weirdness during dragging: canvas element text moves, but
    // the canvas elements do not. But everything clears up when we turn it back on afterwards.
    // Without it, things are even weirder, and the end result may be weird, too.
    // The comical canvas does not change size as the slider moves, and things may end
    // up in strange states with canvas elements cut off where the boundary used to be.
    // It's possible that we could do better by forcing the canvas to stay the same
    // size as the bloom-canvas, but I'm very unsure how resizing an active canvas
    // containing objects will affect ComicalJs and the underlying PaperJs.
    // It should be pretty rare to resize an image after adding canvas elements, so I think it's
    // better to go with this, which at least gives a predictable result.
    // Note: we don't ever need to remove these; they can usefully hang around until
    // we load some other page. (We don't turn off comical when we hide the tool, since
    // the canvas elements are still visible and editable, and we need it's help to support
    // all the relevant behaviors and keep the canvas elements in sync with the text.)
    // Because we're adding a fixed method, not a local function, adding multiple
    // times will not cause duplication.
    public setupSplitterEventHandling() {
        Array.from(
            document.getElementsByClassName("split-pane-divider")
        ).forEach(d => d.addEventListener("mousedown", this.dividerMouseDown));
        document.addEventListener("mouseup", this.documentMouseUp, {
            capture: true
        });
    }

    public cleanupCanvasElements() {
        const allCanvasElements = $("body").find(kCanvasElementSelector);
        allCanvasElements.each((index, element) => {
            const thisCanvasElement = $(element);

            // Not sure about keeping this. Apparently at one point there could be some left-over controls.
            // But we clean out everything bloom-ui when we save a page, so they couldn't persist long.
            // And now I've added these video controls, which get added before we call this, so it was
            // destroying stuff we want. For now I'm just filtering out the new controls and NOT removing them.
            thisCanvasElement
                .find(".bloom-ui")
                .filter(
                    (_, x) =>
                        !x.classList.contains("bloom-videoControlContainer")
                )
                .remove();
            thisCanvasElement.find(".bloom-dragHandleTOP").remove(); // BL-7903 remove any left over drag handles (this was the class used in 4.7 alpha)
        });
    }

    private removeJQueryResizableWidget() {
        try {
            const allCanvasElements = $("body").find(kCanvasElementSelector);
            // Removes the resizable functionality completely. This will return the element back to its pre-init state.
            allCanvasElements.resizable("destroy");
        } catch (e) {
            //console.log(`Error removing resizable widget: ${e}`);
        }
    }

    // Converts a canvas element's position to absolute in pixels (using CSS styling)
    // (Used to be a percentage of parent size. See comments on setTextboxPosition.)
    // canvasElement: The thing we want to position
    // bloomCanvas: Optional. The bloom-canvas the canvas element is in. If this parameter is not defined, the function will automatically determine it.
    private static convertCanvasElementPositionToAbsolute(
        canvasElement: HTMLElement,
        bloomCanvas?: Element | null | undefined
    ): void {
        let unscaledRelativeLeft: number;
        let unscaledRelativeTop: number;

        const left = canvasElement.style.left;
        const top = canvasElement.style.top;
        if (left.endsWith("px") && top.endsWith("px")) {
            // We're already in absolute pixel position.
            return;
        }

        // Note: if the convasElement is scaled by a transform applied to an ancestor
        // element, then the following calculations will be woefully off.  See BL-14312.
        // We think all such cases will be caught by the check above for already being
        // in absolute pixel position.  But this is still something worth considering
        // if canvas elements show up in strange positions.  (Showing image descriptions
        // was the original case where we discovered this problem, and led to realizing
        // that most calls to this method are not really needed.)

        if (!bloomCanvas) {
            bloomCanvas = CanvasElementManager.getBloomCanvas(canvasElement);
        }

        if (bloomCanvas) {
            const positionInfo = canvasElement.getBoundingClientRect();
            const wrapperBoxPos = new Point(
                positionInfo.left,
                positionInfo.top,
                PointScaling.Scaled,
                "convertTextboxPositionToAbsolute()"
            );
            const reframedPoint = this.convertPointFromViewportToElementFrame(
                wrapperBoxPos,
                bloomCanvas
            );
            unscaledRelativeLeft = reframedPoint.getUnscaledX();
            unscaledRelativeTop = reframedPoint.getUnscaledY();
        } else {
            console.assert(
                false,
                "convertTextboxPositionToAbsolute(): container was null or undefined."
            );

            // If can't find the container for some reason, fallback to the old, deprecated calculation.
            // (This algorithm does not properly account for the border of the bloom-canvas when zoomed,
            //  so the results may be slightly off by perhaps up to 2 pixels)
            const scale = EditableDivUtils.getPageScale();
            const pos = $(canvasElement).position();
            unscaledRelativeLeft = pos.left / scale;
            unscaledRelativeTop = pos.top / scale;
        }
        this.setCanvasElementPosition(
            $(canvasElement),
            unscaledRelativeLeft,
            unscaledRelativeTop
        );
    }

    // Sets a text box's position permanently to where it is now.
    // (Not sure if this ever changes anything, except when migrating. Earlier versions of Bloom
    // stored the canvas element position and size as a percentage of the bloom-canvas size.
    // The reasons for that are lost in history; probably we thought that it would better
    // preserve the user's intent to keep in the same shape and position.
    // But in practice it didn't work well, especially since everything was relative to the
    // bloom-canvas, and the image moves around in that as determined by content:fit etc
    // to keep its aspect ratio. The reasons to prefer an absolute position and
    // size are in BL-11667. Basically, we don't want the canvas element to change its size or position
    // relative to its own tail when the image is resized, either because the page size changed
    // or because of dragging a splitter. It would usually be even better if everything kept
    // its position relative to the image itself, but that is much harder to do since the canvas element
    // isn't (can't be) a child of the img.)
    private static setCanvasElementPosition(
        canvasElement: JQuery,
        unscaledRelativeLeft: number,
        unscaledRelativeTop: number
    ) {
        canvasElement
            .css("left", unscaledRelativeLeft + "px")
            .css("top", unscaledRelativeTop + "px")
            // FYI: The textBox width/height is rounded to the nearest whole pixel. Ideally we might like its more precise value...
            // But it's a huge performance hit to get its getBoundingClientRect()
            // It seems that getBoundingClientRect() may be internally cached under the hood,
            // since getting the bounding rect of the bloom-canvas once per mousemove event or even 100x per mousemove event caused no ill effect,
            // but getting this one is quite taxing on the CPU
            .css("width", canvasElement.width() + "px")
            .css("height", canvasElement.height() + "px");

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

    // Lots of places we need to find the bloom-canvas that a particular element resides in.
    // Method is static because several of the callers are static.
    // Return null if element isn't in a bloom-canvas at all.
    private static getBloomCanvas(element: Element): HTMLElement | null {
        if (!element?.closest) {
            // It's possible for the target to be the root document object. If so, it doesn't
            // have a 'closest' function, so we'd better not try to call it.
            // It's also certainly not inside a bloom-canvas, so null is a safe result.
            return null;
        }
        return element.closest(kBloomCanvasSelector);
    }

    // When showing a tail for a canvas element style that doesn't have one by default, we get one here.
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

    public deleteCurrentCanvasElement(): void {
        // "this" might be a menu item that was clicked.  Calling explicitly again fixes that.  See BL-13928.
        if (this !== theOneCanvasElementManager) {
            theOneCanvasElementManager.deleteCurrentCanvasElement();
            return;
        }
        const active = this.getActiveElement();
        if (active) {
            this.deleteCanvasElement(active);
        }
    }

    public duplicateCanvasElement(): HTMLElement | undefined {
        // "this" might be a menu item that was clicked.  Calling explicitly again fixes that.  See BL-13928.
        if (this !== theOneCanvasElementManager) {
            return theOneCanvasElementManager.duplicateCanvasElement();
        }
        const active = this.getActiveElement();
        if (active) {
            return this.duplicateCanvasElementBox(active);
        }
        return undefined;
    }

    public addChildCanvasElement(): void {
        // "this" might be a menu item that was clicked.  Calling explicitly again fixes that.  See BL-13928.
        if (this !== theOneCanvasElementManager) {
            theOneCanvasElementManager.addChildCanvasElement();
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
        ] = CanvasElementManager.GetChildPositionFromParentCanvasElement(
            parentElement,
            bubbleSpec
        );
        this.addChildCanvasElementAndRefreshPage(
            parentElement,
            offsetX,
            offsetY
        );
    }

    // Returns a 2-tuple containing the desired x and y offsets of the child canvas element from the parent canvas element
    //   (i.e., offsetX = child.left - parent.left)
    public static GetChildPositionFromParentCanvasElement(
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

            const canvasElementCenterX =
                parentElement.offsetLeft + parentElement.clientWidth / 2.0;
            const canvasElementCenterY =
                parentElement.offsetTop + parentElement.clientHeight / 2.0;

            const deltaX = tail.tipX - canvasElementCenterX;
            const deltaY = tail.tipY - canvasElementCenterY;

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

    // 6.2 is the release that should properly handle background canvas elements.
    // Reverting them is a temporary hack to prevent problems in 6.1 and 6.0.
    // So this is not currently called in 6.2 like it is in 6.1 and 6.0.
    // But I'm leaving the code for now, because last I heard, we want to use this (or some variation of it)
    // at publish time to set the image containers back to the original, more simple state.
    private revertBackgroundCanvasElements() {
        for (const bgo of Array.from(
            document.getElementsByClassName(kBackgroundImageClass)
        )) {
            const bgImage = getImageFromCanvasElement(bgo as HTMLElement);
            const mainImage = getImageFromContainer(
                bgo.parentElement as HTMLElement
            );
            if (bgImage && mainImage) {
                // Note that we must use get/setAttribute here rather than e.g. mainImage.src (a property
                // of HTMLImageElement) because the src property is a full URL, and we want to preserve
                // what is actually stored in the src attribute, the path relative to the book file.
                mainImage.setAttribute(
                    "src",
                    bgImage.getAttribute("src") || ""
                );
                bgo.remove();
            }
        }
    }

    private handleResizeAdjustments() {
        const bloomCanvases = this.getAllBloomCanvasesOnPage();
        bloomCanvases.forEach(bloomCanvas => {
            this.switchBackgroundToCanvasElementIfNeeded(bloomCanvas);
            this.AdjustChildrenIfSizeChanged(bloomCanvas);
        });
    }

    // If a bloom-canvas has a non-placeholder background image, we switch the
    // background image to an image canvas element. This allows it to be manipuluated more easily.
    // More importantly, it prevents the difficult-to-account-for movement of the
    // background image when the container is resized. Once it is a canvas element,
    // we can apply our algorithm to adjust all the canvas elements together when the container
    // is resized. A further benefit is that it is somewhat backwards compatible:
    // older code will not mess with canvas element positioning like it would tend to
    // if we put position and size attributes on the background image directly.
    private switchBackgroundToCanvasElementIfNeeded(bloomCanvas: HTMLElement) {
        const bgCanvasElement = bloomCanvas.getElementsByClassName(
            kBackgroundImageClass
        )[0] as HTMLElement;
        if (bgCanvasElement) {
            // I think this is redundant, but it got added by mistake at one point,
            // and will hide the placeholder if it's there, so make sure it's not.
            bgCanvasElement.classList.remove(kHasCanvasElementClass);
            return; // already have one.
        }
        this.switchBackgroundToCanvasElement(bloomCanvas);
    }

    private switchBackgroundToCanvasElement(bloomCanvas: HTMLElement) {
        const oldBgImage = getImageFromContainer(bloomCanvas);
        let bgCanvasElement = bloomCanvas.getElementsByClassName(
            kBackgroundImageClass
        )[0] as HTMLElement;
        if (!bgCanvasElement) {
            // various legacy behavior, such as hiding the old-style background placeholder.
            bloomCanvas.classList.add(kHasCanvasElementClass);
            bgCanvasElement = document.createElement("div");
            bgCanvasElement.classList.add(kCanvasElementClass);
            bgCanvasElement.classList.add(kBackgroundImageClass);

            // Make a new image-container to hold just the background image, inside the new canvas element.
            // We don't want a deep clone...that will copy all the canvas elements, too.
            // I'm not sure how much good it does to clone rather than making a new one, now the classes are
            // not the same.
            const newImgContainer = bloomCanvas.cloneNode(false) as HTMLElement;
            newImgContainer.classList.add(kImageContainerClass);
            newImgContainer.classList.remove(kBloomCanvasClass);
            newImgContainer.classList.remove(kHasCanvasElementClass);
            bgCanvasElement.appendChild(newImgContainer);
            let newImg: HTMLElement;
            if (oldBgImage) {
                // If we have an image, we want to clone it and put it in the new image-container.
                // (Could just move it, but that complicates the code for inserting the canvas element.)
                newImg = oldBgImage.cloneNode(false) as HTMLElement;
            } else {
                // Otherwise, we'll make a placeholder image. Src may get set below.
                newImg = document.createElement("img");
                newImg.setAttribute("src", "placeHolder.png");
            }
            newImg.classList.remove("bloom-imageLoadError");
            newImgContainer.appendChild(newImg);

            // Set level so Comical will consider the new canvas element to be under the existing ones.
            const canvasElementElements = Array.from(
                bloomCanvas.getElementsByClassName(kCanvasElementClass)
            ) as HTMLElement[];
            CanvasElementManager.putBubbleBefore(
                bgCanvasElement,
                canvasElementElements,
                1
            );
            bgCanvasElement.style.visibility = "none"; // hide it until we adjust its shape and position
            // consistent with level, we want it in front of the (new, placeholder) background image
            // and behind the other canvas elements.
            if (oldBgImage) {
                bloomCanvas.insertBefore(
                    bgCanvasElement,
                    oldBgImage.nextSibling
                );
            } else {
                const canvas = bloomCanvas.getElementsByTagName(
                    "canvas"
                )[0] as HTMLElement;
                bloomCanvas.insertBefore(bgCanvasElement, canvas.nextSibling);
            }
        }
        const bgImage = getBackgroundImageFromBloomCanvas(
            bloomCanvas
        ) as HTMLElement; // must exist by now
        // Whether it's a new bgImage or not, copy its src from the old-style img
        bgImage.classList.remove("bloom-imageLoadError");
        bgImage.onerror = HandleImageError;
        bgImage.setAttribute(
            "src",
            oldBgImage?.getAttribute("src") ?? "placeHolder.png"
        );
        this.adjustBackgroundImageSize(bloomCanvas, bgCanvasElement, true);
        bgCanvasElement.style.visibility = ""; // now we can show it, if it was new and hidden
        SetupMetadataButton(bloomCanvas);
        if (oldBgImage) {
            oldBgImage.remove();
        }
    }

    // Adjust the levels of all the bubbles of all the listed canvas elements so that
    // the one passed can be given the required level and all the others (keeping their
    // current order) will be perceived by ComicalJs as having a higher level
    private static putBubbleBefore(
        canvasElement: HTMLElement,
        canvasElementElements: HTMLElement[],
        requiredLevel: number
    ) {
        let minLevel = Math.min(
            ...canvasElementElements.map(
                b => Bubble.getBubbleSpec(b as HTMLElement).level ?? 0
            )
        );
        if (minLevel <= requiredLevel) {
            // bump all the others up so we can insert one at level 1 below them all
            // We don't want to use zero as a level...some Comical code complains that
            // the canvas element doesn't have a level at all. And I'm nervous about using
            // negative numbers...something that wants a level one higher might get zero.
            canvasElementElements.forEach(b => {
                const bubble = new Bubble(b as HTMLElement);
                const spec = bubble.getBubbleSpec();
                // the one previously at minLevel will now be at requiredLevel+1, others higher in same sequence.
                spec.level += requiredLevel - minLevel + 1;
                bubble.persistBubbleSpec();
            });
            minLevel = 2;
        }
        const bubble = new Bubble(canvasElement as HTMLElement);
        bubble.getBubbleSpec().level = requiredLevel;
        bubble.persistBubbleSpec();
        Comical.update(canvasElement.parentElement as HTMLElement);
    }

    private adjustBackgroundImageSize(
        bloomCanvas: HTMLElement,
        bgCanvasElement: HTMLElement,
        useSizeOfNewImage: boolean
    ) {
        return this.adjustBackgroundImageSizeToFit(
            bloomCanvas,
            bgCanvasElement,
            useSizeOfNewImage,
            0
        );
    }

    // Given a bg canvas element, which is a canvas element having the bloom-backgroundImage
    // class, and the height and width of the parent bloom-canvas, this method attempts to
    // make the bgCanvasElement the right size and position to fill as much as possible of the parent,
    // rather like object-fit:contain. It is used in two main scenarios: the user may have
    // selected a different image, which means we must adjust to suit a different image aspect
    // ratio. Or, the size of the container may have changed, e.g., using origami. We must also
    // account for the possibility that the image has been cropped, in which case, we want to
    // keep the cropped aspect ratio. (Cropping attributes will already have been removed if it
    // is a new image.)
    // Things are complicated because it's possible the image has not loaded yet, so we can't
    // get its natural dimensions to figure an aspect ratio. In this case, the method arranges
    // to be called again after the image loads or a timeout.
    // A further complication is that the image may fail to load, so we never get natural
    // dimensions. In this case, we expand the bgCanvasElement to the full size of the container so
    // all the space is available to display the error icon and message.
    private adjustBackgroundImageSizeToFit(
        bloomCanvas: HTMLElement,
        // The canvas element div that contains the background image.
        // (Since this is the background that we overlay things on, it is itself a
        // canvas element only in the sense that it has the same HTML structure in order to
        // allow many commands and functions to work on it as if it were an ordinary canvas element.)
        bgCanvasElement: HTMLElement,
        // if this is set true, we've updated the src of the background image and want to
        // ignore any cropping (assumes the img doesn't have any
        // cropping-related style settings) and just adjust the canvas element to fit the image.
        // We'll always have to wait for it to load in this case, otherwise, we may get
        // the dimensions of a previous image.
        useSizeOfNewImage: boolean,
        // Sometimes we think we need to wait for onload, but the data arrives before we set up
        // the watcher. We make a timeout so we will go ahead and adjust if we have dimensions
        // and don't get an onload in a reasonable time. If we DO get the onload before we
        // timeout, we use this handle to clear it.
        // This is set when we arrange an onload callback and receive it
        timeoutHandler: number
    ) {
        if (timeoutHandler) {
            clearTimeout(timeoutHandler);
        }
        const bloomCanvasWidth = bloomCanvas.clientWidth;
        const bloomCanvasHeight = bloomCanvas.clientHeight;
        let imgAspectRatio =
            bgCanvasElement.clientWidth / bgCanvasElement.clientHeight;
        const img = getImageFromCanvasElement(bgCanvasElement);
        let failedImage = false;
        // We don't ever expect there not to be an img. If it happens, we'll just go
        // ahead and adjust based on the current shape of the canvas element (as set above).
        if (img) {
            // The image may not have loaded yet or may have failed to load.  If either of these
            // cases is true, then the naturalHeight and naturalWidth will be zero.  If the image
            // failed to load, a special class is added to the image to indicate this fact (if all
            // goes well).  However, we may know that this is called in response to a new image, in
            // which case the class may not have been added yet.
            // We conclude that the image has truly failed if 1) we don't have natural dimensions set
            // to something other than zero, 2) we are not waiting for new dimensions, and 3) the
            // image has the special class indicating that it failed to load.  (The class is supposed
            // to be removed when we change the src attribute, which leads to a new load attempt.)
            failedImage =
                img.naturalHeight === 0 && // not loaded successfully (yet)
                !useSizeOfNewImage && // not waiting for new dimensions
                img.classList.contains("bloom-imageLoadError"); // error occurred while trying to load
            if (failedImage) {
                // If the image failed to load, just use the container aspect ratio to fill up
                // the container with the error message (alt attribute string).
                imgAspectRatio = bloomCanvasWidth / bloomCanvasHeight;
            } else if (
                img.naturalHeight === 0 ||
                img.naturalWidth === 0 ||
                useSizeOfNewImage
            ) {
                // if we don't have a height and width, or we know the image src changed
                // and have not yet waited for new dimensions, go ahead and wait.
                // We set up this timeout
                const handle = (setTimeout(
                    () =>
                        this.adjustBackgroundImageSizeToFit(
                            bloomCanvas,
                            bgCanvasElement,
                            // after the timeout we don't consider that we MUST wait if we have dimensions
                            false,
                            0 // when we get this call, we're responding to the timeout, so don't need to cancel.
                        ),
                    // I think this is long enough that we won't be seeing obsolete data (from a previous src).
                    // OTOH it's not hopelessly long for the user to wait when we don't get an onload.
                    // If by any chance this happens when the image really isn't loaded enough to
                    // have naturalHeight/Width, the zero checks above will force another iteration.
                    100
                    // somehow Typescript is confused and thinks this is a NodeJS version of setTimeout.
                ) as unknown) as number;
                // preferably we update when we are loaded.
                img.addEventListener(
                    "load",
                    () =>
                        this.adjustBackgroundImageSizeToFit(
                            bloomCanvas,
                            bgCanvasElement,
                            false, // when this call happens we have the new dimensions.
                            handle // if this callback happens we can cancel the timeout.
                        ),
                    { once: true }
                );
                return; // try again once we have valid image data
            } else if (img.style.width) {
                // there is established cropping. Use the cropped size to determine the
                // aspect ratio.
                imgAspectRatio =
                    CanvasElementManager.pxToNumber(
                        bgCanvasElement.style.width
                    ) /
                    CanvasElementManager.pxToNumber(
                        bgCanvasElement.style.height
                    );
            } else {
                // not cropped, so we can use the natural dimensions
                imgAspectRatio = img.naturalWidth / img.naturalHeight;
            }
        }

        const oldWidth = bgCanvasElement.clientWidth;
        const containerAspectRatio = bloomCanvasWidth / bloomCanvasHeight;
        const fitCoverMode = img?.classList.contains(
            "bloom-imageObjectFit-cover"
        );
        let matchWidthOfContainer = imgAspectRatio > containerAspectRatio;
        if (fitCoverMode) {
            matchWidthOfContainer = !matchWidthOfContainer;
        }

        if (matchWidthOfContainer) {
            // size of image is width-limited
            bgCanvasElement.style.width = bloomCanvasWidth + "px";
            bgCanvasElement.style.left = "0px";
            const imgHeight = bloomCanvasWidth / imgAspectRatio;
            bgCanvasElement.style.top =
                (bloomCanvasHeight - imgHeight) / 2 + "px";
            bgCanvasElement.style.height = imgHeight + "px";
        } else {
            const imgWidth = bloomCanvasHeight * imgAspectRatio;
            bgCanvasElement.style.width = imgWidth + "px";
            bgCanvasElement.style.top = "0px";
            bgCanvasElement.style.left =
                (bloomCanvasWidth - imgWidth) / 2 + "px";
            bgCanvasElement.style.height = bloomCanvasHeight + "px";
        }
        if (!useSizeOfNewImage && img?.style.width) {
            // need to adjust image settings to preserve cropping
            const scale = bgCanvasElement.clientWidth / oldWidth;
            img.style.width =
                CanvasElementManager.pxToNumber(img.style.width) * scale + "px";
            img.style.left =
                CanvasElementManager.pxToNumber(img.style.left) * scale + "px";
            img.style.top =
                CanvasElementManager.pxToNumber(img.style.top) * scale + "px";
        }
        // Ensure that the missing image message is displayed without being cropped.
        // See BL-14241.
        if (failedImage && img && img.style && img.style.width.length > 0) {
            const imgLeft = CanvasElementManager.pxToNumber(img.style.left);
            const imgTop = CanvasElementManager.pxToNumber(img.style.top);
            if (imgLeft < 0 || imgTop < 0) {
                // The failed image was cropped. Remove the cropping to facilitate displaying the error state.
                img.setAttribute(
                    "data-style",
                    `left:${img.style.left}; width:${img.style.width}; top:${img.style.top};`
                );
                const imgWidth = CanvasElementManager.pxToNumber(
                    img.style.width
                );
                console.warn(
                    `Missing image: resetting left from ${imgLeft} to 0, top from ${imgTop} to 0, and width from ${imgWidth} to ${imgWidth +
                        imgLeft}`
                );
                img.style.left = "0px";
                img.style.top = "0px";
                img.style.width = imgWidth + imgLeft + "px";
            }
        }
        this.alignControlFrameWithActiveElement();
    }

    // Store away the current size of the bloom-canvas. At any later time if we notice that
    // this does not match the current size, we adjust everything according to how the size has changed.
    private updateBloomCanvasSizeData(bloomCanvas: HTMLElement) {
        bloomCanvas.setAttribute(
            // originally data-imgSizeBasedOn, but that is technically invalid
            // since data-* attributes must be lowercase. JS converts it to
            // data-imgsizebasedon as we write, so that's what's in files.
            // I'd prefer it to be data-img-size-based-on, but that would require data-migration.
            "data-imgsizebasedon",
            `${bloomCanvas.clientWidth},${bloomCanvas.clientHeight}`
        );
    }

    public AdjustChildrenIfSizeChanged(bloomCanvas: HTMLElement): void {
        const oldSizeData = bloomCanvas.getAttribute("data-imgsizebasedon");
        if (!oldSizeData) {
            // Can't make a useful adjustment now, with no previous size to work from.
            // But if this is an image with canvas elements, we'll want to remember the size for next time.
            if (
                bloomCanvas.getElementsByClassName(kCanvasElementClass).length >
                0
            ) {
                this.updateBloomCanvasSizeData(bloomCanvas);
            }
            return; // not using this system for sizing
        }
        // Get the width it was the last time the user was working on it
        const oldSizeDataArray = oldSizeData.split(",");
        let oldWidth = parseInt(oldSizeDataArray[0]);
        let oldHeight = parseInt(oldSizeDataArray[1]);

        const newWidth = bloomCanvas.clientWidth;
        const newHeight = bloomCanvas.clientHeight;
        if (oldWidth === newWidth && oldHeight === newHeight) return; // allow small discrepancy?
        // Leave out of this calculation the canvas and any image descriptions or controls.
        const children = (Array.from(
            bloomCanvas.children
        ) as HTMLElement[]).filter(
            c =>
                c.style.left !== "" &&
                c.classList.contains("bloom-ui") === false
        );
        if (children.length === 0) return;

        // Figure out the rectangle that contains all the canvas elements. We'll adjust the size and position
        // of this rectangle to fit the new container. (But if there's a background image, we'll instead
        // adjust to keep it in the content-fit position.)
        // Review: should we consider any data-bubble-alternate values on other language bloom-editables?
        // In most cases it won't make much difference since the alternate is in nearly the same place.
        // If an alternate is in a very different place, leaving it out here could mean it gets clipped
        // in the new layout. OTOH, if we include it, the results for this language could be quite
        // puzzling, and there might be no way to get things to stay where they are wanted without adjusting
        // the alternate language version.
        let top = Number.MAX_VALUE;
        let bottom = -Number.MAX_VALUE;
        let left = Number.MAX_VALUE;
        let right = -Number.MAX_VALUE;
        for (let i = 0; i < children.length; i++) {
            const child = children[i];
            const childTop = child.offsetTop;
            const childLeft = child.offsetLeft;
            if (child.classList.contains(kBackgroundImageClass)) {
                const img = getImageFromCanvasElement(child);
                if (!img || img.getAttribute("src") === "placeHolder.png") {
                    // No image, or placeholder. Not visible (unless it's the only thing there).
                    // Don't include this in the calculations.
                    // But in case it gets selected, or is the only one, adjust it independently
                    this.adjustBackgroundImageSize(bloomCanvas, child, false);
                    if (children.length > 1) {
                        // we'll process the others ignoring the invisible BG image
                        continue;
                    } else {
                        // there are no others, and with zero iterations of this loop
                        // something bad might happen.
                        return;
                    }
                }
            }
            // Clip the rectangle to the old container. If the author previously placed
            // something so that it was partly clipped, we don't need to 'correct' that.
            // (We're not trying to ensure that it stays clipped by the same amount,
            // just that we don't scale things down more than otherwise necessary to make
            // more of it visible.)
            if (childTop < top) top = Math.max(childTop, 0);
            if (childLeft < left) left = Math.max(childLeft, 0);
            if (childTop + child.clientHeight > bottom)
                bottom = Math.min(childTop + child.clientHeight, oldHeight);
            if (childLeft + child.clientWidth > right)
                right = Math.min(childLeft + child.clientWidth, oldWidth);

            // If found, it should be the first one; we'll make it the whole rectangle we try
            // to fit to the new container size.
            if (child.classList.contains(kBackgroundImageClass)) {
                if (
                    (child.clientLeft !== 0 && child.clientTop !== 0) ||
                    (Math.abs(child.clientWidth - oldWidth) > 1 &&
                        Math.abs(child.clientHeight - oldHeight) > 1)
                ) {
                    // The background image was not properly adjusted to fit the old container size.
                    // We'll pretend the old container size properly matched the old BG image so everything else adjusts properly.
                    // Move all the canvas elements so the BG image is in the top left.
                    const deltaX = child.clientLeft;
                    const deltaY = child.clientTop;
                    for (let j = 0; j < children.length; j++) {
                        const c = children[j];
                        c.style.left =
                            CanvasElementManager.pxToNumber(c.style.left) -
                            deltaX +
                            "px";
                        c.style.top =
                            CanvasElementManager.pxToNumber(c.style.top) -
                            deltaY +
                            "px";
                    }
                    // and pretend the old container size matched the old BG image size.
                    oldWidth = child.clientWidth;
                    oldHeight = child.clientHeight;
                }
                break;
            }
        }
        const childrenHeight = bottom - top;
        const childrenWidth = right - left;
        const childrenAspectRatio = childrenWidth / childrenHeight;
        // The goal is to figure out the new size and position of the rectangle
        // defined by top, left, childrenWidth, childrenHeight, which are relative
        // to oldWidth and oldHeight, in view of the newWidth and newHeight.
        // Ideally the new height, width, top, and left would be the same percentages
        // as before of the new container height and width. But we need to preserve
        // aspect ratio. If the ideal adjustment breaks this, we will
        // - increase the dimension that is too small for the aspect ratio until the aspect ratio is correct or it fills the container.
        // - if that didn't make things right, decrease the other dimension.
        // Conveniently this algorithm also achieves the goal of keeping any background image
        // emultating content-fit (assuming it was before).
        // What fraction of the old padding was on the left?
        const widthPadding = oldWidth - childrenWidth;
        const heightPadding = oldHeight - childrenHeight;
        // if there was significant padding before, we'll try to keep the same ratio.
        // if not, and we now need padding in that direction, we'll center things.
        const oldLeftPaddingFraction =
            widthPadding > 1 ? left / widthPadding : 0.5;
        const oldTopPaddingFraction =
            heightPadding > 1 ? top / heightPadding : 0.5;
        const oldWidthFraction = childrenWidth / oldWidth;
        const oldHeightFraction = childrenHeight / oldHeight;
        let newChildrenWidth = oldWidthFraction * newWidth;
        let newChildrenHeight = oldHeightFraction * newHeight;
        if (newChildrenWidth / newChildrenHeight > childrenAspectRatio) {
            // the initial calculation will distort things as if squeezed vertically.
            // try increasing height
            newChildrenHeight = newChildrenWidth / childrenAspectRatio;
            if (newChildrenHeight > newHeight) {
                // can't grow enough vertically, instead, reduce width
                newChildrenHeight = newHeight;
                newChildrenWidth = newChildrenHeight * childrenAspectRatio;
            }
        } else {
            // the initial calculation will distort things as if squeezed horizontally.
            // try increasing width
            newChildrenWidth = newChildrenHeight * childrenAspectRatio;
            if (newChildrenWidth > newWidth) {
                // can't grow enough horizontally, instead, reduce height
                newChildrenWidth = newWidth;
                newChildrenHeight = newChildrenWidth / childrenAspectRatio;
            }
        }
        // after the adjustments above, this is how we will scale things in both directions.
        const scale = newChildrenWidth / childrenWidth;
        // The new topLeft is calculated to distribute any whitespace in the same proportions as before.
        const newWidthPadding = newWidth - newChildrenWidth;
        const newHeightPadding = newHeight - newChildrenHeight;
        const newLeft = oldLeftPaddingFraction * newWidthPadding;
        const newTop = oldTopPaddingFraction * newHeightPadding;
        // OK, so the rectangle that represents the union of all the children (or the background image) is going to
        // be scaled by 'scale' and moved to (newLeft, newTop).
        // Now we need to adjust the position and possibly size of each child.
        children.forEach((child: HTMLElement) => {
            const childTop = child.offsetTop;
            const childLeft = child.offsetLeft;
            // a first approximation
            let newChildTop = newTop + (childTop - top) * scale;
            let newChildLeft = newLeft + (childLeft - left) * scale;
            let newChildWidth = child.clientWidth;
            let newChildHeight = child.clientHeight;
            let reposition = true;
            if (
                Array.from(child.children).some(
                    (c: HTMLElement) =>
                        c.classList.contains("bloom-imageContainer") ||
                        c.classList.contains("bloom-videoContainer")
                )
            ) {
                // an image or video canvas element: the position is OK, we want to scale the size.
                newChildWidth = child.clientWidth * scale;
                newChildHeight = child.clientHeight * scale;
                const img = child.getElementsByTagName("img")[0];
                if (img && img.style.width) {
                    // The image has been cropped. We want to keep the crop looking the same,
                    // which means we need to scale its width, left, and top.
                    const imgLeft = CanvasElementManager.pxToNumber(
                        img.style.left
                    );
                    const imgTop = CanvasElementManager.pxToNumber(
                        img.style.top
                    );
                    const imgWidth = CanvasElementManager.pxToNumber(
                        img.style.width
                    );
                    img.style.left = imgLeft * scale + "px";
                    img.style.top = imgTop * scale + "px";
                    img.style.width = imgWidth * scale + "px";
                }
            } else if (
                child.classList.contains(kCanvasElementClass) ||
                child.hasAttribute("data-target-of")
            ) {
                // text canvas element (or target): we want to leave the size alone and preserve the position of the center.
                const oldCenterX = childLeft + child.clientWidth / 2;
                const oldCenterY = childTop + child.clientHeight / 2;
                const newCenterX = newLeft + (oldCenterX - left) * scale;
                const newCenterY = newTop + (oldCenterY - top) * scale;
                newChildTop = newCenterY - newChildHeight / 2;
                newChildLeft = newCenterX - newChildWidth / 2;
            } else {
                // image description? UI artifact? leave it alone
                reposition = false;
            }
            if (reposition) {
                child.style.top = newChildTop + "px";
                child.style.left = newChildLeft + "px";
                child.style.width = newChildWidth + "px";
                child.style.height = newChildHeight + "px";
            }
            if (child.classList.contains(kCanvasElementClass)) {
                const tails: TailSpec[] = Bubble.getBubbleSpec(child).tails;
                tails.forEach(tail => {
                    tail.tipX = newLeft + (tail.tipX - left) * scale;
                    tail.tipY = newTop + (tail.tipY - top) * scale;
                    tail.midpointX = newLeft + (tail.midpointX - left) * scale;
                    tail.midpointY = newTop + (tail.midpointY - top) * scale;
                });
                const bubble = new Bubble(child);
                bubble.mergeWithNewBubbleProps({ tails: tails });
                if (
                    !Array.from(child.children).some(
                        (c: HTMLElement) =>
                            c.classList.contains("bloom-imageContainer") ||
                            c.classList.contains("bloom-videoContainer")
                    )
                ) {
                    // This must be done after we adjust the canvas element, since its new settings are
                    // written into the alternate for the current language.
                    // Review: adjusting the data-bubble-alternate means that the canvas elements in
                    // other languages will look right if we go in and edit them. However,
                    // to make things look right automatically in publications, we'd need to
                    // switch each alternative to be the live one, fire up Comical, and adjust the SVG.
                    // I think this would cause flicker, and certainly delay. If we decide we want
                    // to make that fully automatic, I think it might be better to do it
                    // as a publishing step when we know what languages will be published.
                    CanvasElementManager.adjustCanvasElementAlternates(
                        child,
                        scale,
                        left,
                        top,
                        newLeft,
                        newTop
                    );
                }
            }
        });
        this.updateBloomCanvasSizeData(bloomCanvas);
    }

    public static adjustCanvasElementAlternates(
        canvasElement: HTMLElement,
        scale: number,
        oldLeft: number,
        oldTop: number,
        newLeft: number,
        newTop: number
    ) {
        const canvasElementLang = GetSettings().languageForNewTextBoxes;
        Array.from(
            canvasElement.getElementsByClassName("bloom-editable")
        ).forEach(editable => {
            const lang = editable.getAttribute("lang");
            if (lang === canvasElementLang) {
                // We want to update this lang's alternate to the current data we already figured out.
                const alternate = {
                    style: canvasElement.getAttribute("style"),
                    tails: Bubble.getBubbleSpec(canvasElement).tails
                };
                editable.setAttribute(
                    "data-bubble-alternate",
                    JSON.stringify(alternate).replace(/"/g, "`")
                );
            } else {
                const alternatesString = editable.getAttribute(
                    "data-bubble-alternate"
                );
                if (alternatesString) {
                    const alternate = JSON.parse(
                        alternatesString.replace(/`/g, '"')
                    ) as IAlternate;
                    const style = alternate.style;
                    const width = CanvasElementManager.getLabeledNumberInPx(
                        "width",
                        style
                    );
                    const height = CanvasElementManager.getLabeledNumberInPx(
                        "height",
                        style
                    );
                    let newStyle = CanvasElementManager.adjustCenterOfTextBox(
                        "left",
                        style,
                        scale,
                        oldLeft,
                        newLeft,
                        width
                    );
                    newStyle = CanvasElementManager.adjustCenterOfTextBox(
                        "top",
                        newStyle,
                        scale,
                        oldTop,
                        newTop,
                        height
                    );

                    const tails = alternate.tails;
                    tails.forEach(
                        (tail: {
                            tipX: number;
                            tipY: number;
                            midpointX: number;
                            midpointY: number;
                        }) => {
                            tail.tipX = newLeft + (tail.tipX - oldLeft) * scale;
                            tail.tipY = newTop + (tail.tipY - oldTop) * scale;
                            tail.midpointX =
                                newLeft + (tail.midpointX - oldLeft) * scale;
                            tail.midpointY =
                                newTop + (tail.midpointY - oldTop) * scale;
                        }
                    );
                    alternate.style = newStyle;
                    alternate.tails = tails;
                    editable.setAttribute(
                        "data-bubble-alternate",
                        JSON.stringify(alternate).replace(/"/g, "`")
                    );
                }
            }
        });
    }

    private static numberPxRegex = ": ?(-?\\d+.?\\d*)px";

    // Find in 'style' the label followed by a number (e.g., left).
    // Let oldRange be the size of the object in that direction, e.g., width.
    // We want to move the center of the object on the basis that the container that
    // the labeled value is relative to is being scaled by 'scale',
    // and moved from oldC to newC, and put the new value back in the style, and yield that new style
    // as the result.
    public static adjustCenterOfTextBox(
        label: string,
        style: string,
        scale: number,
        oldC: number,
        newC: number,
        oldRange: number
    ): string {
        const old = CanvasElementManager.getLabeledNumberInPx(label, style);
        const center = old + oldRange / 2;
        const newCenter = newC + (center - oldC) * scale;
        const newVal = newCenter - oldRange / 2;
        return style.replace(
            new RegExp(label + this.numberPxRegex),
            label + ": " + newVal + "px"
        );
    }

    // Typical source is something like "left: 224px; top: 79.6px; width: 66px; height: 30px;"
    // We want to pass "top" and get 79.6.
    public static getLabeledNumberInPx(label: string, source: string): number {
        const match = source.match(
            new RegExp(label + CanvasElementManager.numberPxRegex)
        );
        if (match) {
            return parseFloat(match[1]);
        }
        return 9;
    }
}

// For use by bloomImages.ts, so that newly opened books get this class updated for their images.
export function updateCanvasElementClass(bloomCanvas: HTMLElement) {
    if (bloomCanvas.getElementsByClassName(kCanvasElementClass).length > 0) {
        bloomCanvas.classList.add(kHasCanvasElementClass);
    } else {
        bloomCanvas.classList.remove(kHasCanvasElementClass);
    }
}

// Note: do NOT use this directly in toolbox code; it will import its own copy of
// CanvasElementManager and not use the proper one from the page iframe. Instead, use
// the CanvasElementUtils.getCanvasElementManager().
export let theOneCanvasElementManager: CanvasElementManager;

export function initializeCanvasElementManager() {
    if (theOneCanvasElementManager) return;
    theOneCanvasElementManager = new CanvasElementManager();
    theOneCanvasElementManager.initializeCanvasElementManager();
}

// This is a definition of the object we store as JSON in data-bubble-alternate.
// Tails has further structure but CanvasElementManager doesn't care about it.
interface IAlternate {
    style: string; // What to put in the style attr of the canvas element; determines size and position
    tails: object[]; // The tails of the data-bubble; determines placing of tail.
}

// This is just for debugging. It produces a string that describes the canvas element, generally
// well enough to identify it in console.log.
export function canvasElementDescription(
    e: Element | null | undefined
): string {
    const elt = e as HTMLElement;
    if (!elt) {
        return "no canvas element";
    }
    const result =
        "canvas element at (" + elt.style.left + ", " + elt.style.top + ") ";
    const imageContainer = elt.getElementsByClassName(kImageContainerClass)[0];
    if (imageContainer) {
        const img = imageContainer.getElementsByTagName("img")[0];
        if (img) {
            return result + "with image : " + img.getAttribute("src");
        }
    }
    const videoSrc = elt.getElementsByTagName("source")[0];
    if (videoSrc) {
        return result + "with video " + videoSrc.getAttribute("src");
    }
    // Enhance: look for videoContainer similarly
    else {
        return result + "with text " + elt.innerText;
    }
    return result;
}
