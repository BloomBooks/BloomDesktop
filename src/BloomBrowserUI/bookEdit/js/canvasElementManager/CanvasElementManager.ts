// This class makes it possible to add and delete elements that float over images. These floating
// elements were originally intended for use in making comic books, but could also be useful for many
// other cases of where there is space for text or another image or a video within the bounds of
// the picture.
///<reference path="../../../typings/jquery/jquery.d.ts"/>
// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="../collectionSettings.d.ts"/>

import { EditableDivUtils } from "../editableDivUtils";
import {
    Bubble,
    BubbleSpec,
    BubbleSpecPattern,
    Comical,
    TailSpec,
} from "comicaljs";
import { Point, PointScaling } from "../point";
import { isLinux } from "../../../utils/isLinux";
import { getRgbaColorStringFromColorAndOpacity } from "../../../utils/colorUtils";
import {
    IImageInfo,
    SetupElements,
    attachToCkEditor,
    notifyToolOfChangedImage,
} from "../bloomEditing";
import {
    EnableAllImageEditing,
    getImageFromCanvasElement,
    kImageContainerClass,
    SetupMetadataButton,
    UpdateImageTooltipVisibility,
    HandleImageError,
    isPlaceHolderImage,
} from "../bloomImages";
import BloomSourceBubbles from "../../sourceBubbles/BloomSourceBubbles";
import BloomHintBubbles from "../BloomHintBubbles";
import { renderCanvasElementContextControls } from "./CanvasElementContextControls";
import { kBloomBlue } from "../../../bloomMaterialUITheme";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
    kBloomCanvasClass,
    kBloomCanvasSelector,
    kCanvasElementClass,
    kCanvasElementSelector,
} from "../../toolbox/canvas/canvasElementConstants";
import { updateCanvasElementClass } from "../../toolbox/canvas/canvasElementDomUtils";
import OverflowChecker from "../../OverflowChecker/OverflowChecker";
import { kVideoContainerClass, selectVideoContainer } from "../videoUtils";
import { needsToBeKeptSameSize } from "../../toolbox/games/gameUtilities";
import { CanvasElementType } from "../../toolbox/canvas/canvasElementTypes";
import { CanvasGuideProvider } from "./CanvasGuideProvider";
import { CanvasElementKeyboardProvider } from "./CanvasElementKeyboardProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";
import PlaceholderProvider from "../PlaceholderProvider";
import { copyContentToTarget } from "bloom-player";
import $ from "jquery";
import { kCanvasToolId } from "../../toolbox/toolIds";
import { showCanvasTool } from "./CanvasElementManagerPublicFunctions";
import {
    convertPointFromViewportToElementFrame as convertPointFromViewportToElementFrameFromGeometry,
    getCombinedBorderWidths as getCombinedBorderWidthsFromGeometry,
    getCombinedBordersAndPaddings as getCombinedBordersAndPaddingsFromGeometry,
    getCombinedPaddings as getCombinedPaddingsFromGeometry,
    getLeftAndTopBorderWidths as getLeftAndTopBorderWidthsFromGeometry,
    getLeftAndTopPaddings as getLeftAndTopPaddingsFromGeometry,
    getPadding as getPaddingFromGeometry,
    getRightAndBottomBorderWidths as getRightAndBottomBorderWidthsFromGeometry,
    getRightAndBottomPaddings as getRightAndBottomPaddingsFromGeometry,
    getScrollAmount as getScrollAmountFromGeometry,
    extractNumber as extractNumberFromGeometry,
} from "./CanvasElementGeometry";
import {
    adjustCanvasElementsForCurrentLanguage as adjustCanvasElementsForCurrentLanguageFromAlternates,
    adjustCanvasElementAlternates as adjustCanvasElementAlternatesFromAlternates,
    adjustCenterOfTextBox as adjustCenterOfTextBoxFromAlternates,
    getLabeledNumberInPx as getLabeledNumberInPxFromAlternates,
    saveCurrentCanvasElementStateAsCurrentLangAlternate as saveCurrentCanvasElementStateAsCurrentLangAlternateFromAlternates,
    saveStateOfCanvasElementAsCurrentLangAlternate,
} from "./CanvasElementAlternates";
import {
    getBloomCanvas as getBloomCanvasFromPositioning,
    getChildPositionFromParentCanvasElement as getChildPositionFromParentCanvasElementFromPositioning,
    getInteriorWidthHeight as getInteriorWidthHeightFromPositioning,
    inPlayMode as inPlayModeFromPositioning,
    setCanvasElementPosition as setCanvasElementPositionFromPositioning,
} from "./CanvasElementPositioning";
import type { ITextColorInfo } from "./CanvasElementSharedTypes";
export type { ITextColorInfo } from "./CanvasElementSharedTypes";
import { CanvasElementFactories } from "./CanvasElementFactories";
import { CanvasElementClipboard } from "./CanvasElementClipboard";
import { CanvasElementDuplication } from "./CanvasElementDuplication";
import { CanvasElementSelectionUi } from "./CanvasElementSelectionUi";
import { CanvasElementPointerInteractions } from "./CanvasElementPointerInteractions";
import { CanvasElementHandleDragInteractions } from "./CanvasElementHandleDragInteractions";
import { CanvasElementDraggableIntegration } from "./CanvasElementDraggableIntegration";
import { CanvasElementEditingSuspension } from "./CanvasElementEditingSuspension";
import { CanvasElementCanvasResizeAdjustments } from "./CanvasElementCanvasResizeAdjustments";
import { CanvasElementBackgroundImageManager } from "./CanvasElementBackgroundImageManager";

const kComicalGeneratedClass: string = "comical-generated";

const kTransformPropName = "bloom-zoomTransformForInitialFocus";
export { kBackgroundImageClass } from "../../toolbox/canvas/canvasElementConstants";

type ResizeDirection = "ne" | "nw" | "sw" | "se";
export {
    getAllDraggables,
    isDraggable,
    kDraggableIdAttribute,
} from "../../toolbox/canvas/canvasElementDraggables";

// Canvas elements are the movable items that can be placed over images (or empty image containers).
// Some of them are associated with ComicalJs bubbles. Earlier in Bloom's history, they were variously
// called TextOverPicture boxes, TOPs, Overlays, OverPictures, and Bubbles. We have attempted to clean up all such
// names, but it is difficult, as "top" is a common CSS property, many other things are called overlays,
// and "bubble" is used in reference to ComicalJs, Source Bubbles, Hint Bubbles, and other qtips.
// Some may have been missed. (It's even conceivable that some references to the other things were
// accidentally renamed to "canvas element".)
export class CanvasElementManager {
    // The min width/height needs to be kept in sync with the corresponding values in canvasTool.less
    public minTextBoxWidthPx = 30;
    public minTextBoxHeightPx = 30;

    private activeElement: HTMLElement | undefined;
    public isCanvasElementEditingOn: boolean = false;
    private thingsToNotifyOfCanvasElementChange: {
        // identifies the source that requested the notification; allows us to remove the
        // right one when no longer needed, and prevent multiple notifiers to the same client.
        id: string;
        handler: (x: Bubble | undefined) => void;
    }[] = [];

    private guideProvider: CanvasGuideProvider;
    private keyboardProvider: CanvasElementKeyboardProvider;
    private snapProvider: CanvasSnapProvider;
    private factories: CanvasElementFactories;
    private clipboard: CanvasElementClipboard;
    private duplication: CanvasElementDuplication;
    private selectionUi: CanvasElementSelectionUi;
    private pointerInteractions: CanvasElementPointerInteractions;
    private handleDragInteractions: CanvasElementHandleDragInteractions;
    private draggableIntegration: CanvasElementDraggableIntegration;
    private editingSuspension: CanvasElementEditingSuspension;
    private canvasResizeAdjustments: CanvasElementCanvasResizeAdjustments;
    private backgroundImageManager: CanvasElementBackgroundImageManager;

    // Used by stopMoving() to clear cursor style after a drag.
    private lastMoveContainer: HTMLElement;

    public constructor() {
        this.snapProvider = new CanvasSnapProvider();
        this.guideProvider = new CanvasGuideProvider();
        this.draggableIntegration = new CanvasElementDraggableIntegration({
            getAllBloomCanvasesOnPage:
                this.getAllBloomCanvasesOnPage.bind(this),
        });
        this.editingSuspension = new CanvasElementEditingSuspension({
            getIsCanvasElementEditingOn: () => this.isCanvasElementEditingOn,
            getAllBloomCanvasesOnPage:
                this.getAllBloomCanvasesOnPage.bind(this),
            adjustBackgroundImageSize:
                this.adjustBackgroundImageSize.bind(this),
            adjustChildrenIfSizeChanged:
                this.AdjustChildrenIfSizeChanged.bind(this),
            turnOffCanvasElementEditing:
                this.turnOffCanvasElementEditing.bind(this),
            turnOnCanvasElementEditing:
                this.turnOnCanvasElementEditing.bind(this),
            setupControlFrame: this.setupControlFrame.bind(this),
        });
        this.canvasResizeAdjustments = new CanvasElementCanvasResizeAdjustments(
            {
                adjustBackgroundImageSize:
                    this.adjustBackgroundImageSize.bind(this),
                pxToNumber: CanvasElementManager.pxToNumber,
            },
        );
        this.backgroundImageManager = new CanvasElementBackgroundImageManager({
            getAllBloomCanvasesOnPage:
                this.getAllBloomCanvasesOnPage.bind(this),
            adjustChildrenIfSizeChanged:
                this.AdjustChildrenIfSizeChanged.bind(this),
            getActiveElement: () => this.activeElement,
            alignControlFrameWithActiveElement:
                this.alignControlFrameWithActiveElement,
            pxToNumber: CanvasElementManager.pxToNumber,
        });
        this.factories = new CanvasElementFactories({
            snapProvider: this.snapProvider,
            getBloomCanvasFromMouse: this.getBloomCanvasFromMouse.bind(this),
            getActiveElement: () => this.activeElement,
            setActiveElementDirect: (canvasElement) => {
                this.activeElement = canvasElement;
            },
            doNotifyChange: this.doNotifyChange.bind(this),
            showCorrespondingTextBox: this.showCorrespondingTextBox.bind(this),
            handleResizeAdjustments: this.handleResizeAdjustments.bind(this),
            refreshCanvasElementEditing:
                this.refreshCanvasElementEditing.bind(this),
            setActiveElement: this.setActiveElement.bind(this),
            getTextColorInformation: this.getTextColorInformation.bind(this),
            setTextColorInternal: this.setTextColorInternal.bind(this),
        });
        this.clipboard = new CanvasElementClipboard({
            snapProvider: this.snapProvider,
            minWidth: this.minWidth,
            minHeight: this.minHeight,
            getActiveOrFirstBloomCanvasOnPage:
                this.getActiveOrFirstBloomCanvasOnPage.bind(this),
            getActiveElement: () => this.activeElement,
            adjustBackgroundImageSize:
                this.adjustBackgroundImageSize.bind(this),
            adjustContainerAspectRatio:
                this.adjustContainerAspectRatio.bind(this),
            addPictureCanvasElement:
                this.factories.addPictureCanvasElement.bind(this.factories),
            setDoAfterNewImageAdjusted: (callback) => {
                this.doAfterNewImageAdjusted = callback;
            },
        });
        this.duplication = new CanvasElementDuplication({
            getPatriarchBubbleOfActiveElement:
                this.getPatriarchBubbleOfActiveElement.bind(this),
            setActiveElement: this.setActiveElement.bind(this),
            getSelectedItemBubbleSpec:
                this.getSelectedItemBubbleSpec.bind(this),
            updateSelectedItemBubbleSpec:
                this.updateSelectedItemBubbleSpec.bind(this),
            refreshCanvasElementEditing:
                this.refreshCanvasElementEditing.bind(this),
            removeJQueryResizableWidget:
                this.removeJQueryResizableWidget.bind(this),
            initializeCanvasElementEditing:
                this.initializeCanvasElementEditing.bind(this),
            addCanvasElementFromOriginal:
                this.factories.addCanvasElementFromOriginal.bind(
                    this.factories,
                ),
            findBestLocationForNewCanvasElement:
                this.findBestLocationForNewCanvasElement.bind(this),
            reorderRectangleCanvasElement:
                this.reorderRectangleCanvasElement.bind(this),
            addChildInternal: this.addChildInternal.bind(this),
            adjustRelativePointToBloomCanvas:
                this.adjustRelativePointToBloomCanvas.bind(this),
        });

        this.handleDragInteractions = new CanvasElementHandleDragInteractions(
            {
                getActiveElement: () => this.activeElement,
                getMinWidth: () => this.minWidth,
                getMinHeight: () => this.minHeight,
                adjustTarget: this.adjustTarget.bind(this),
                alignControlFrameWithActiveElement:
                    this.alignControlFrameWithActiveElement,
                adjustBackgroundImageSize:
                    this.adjustBackgroundImageSize.bind(this),
                adjustCanvasElementHeightToContentOrMarkOverflow:
                    this.adjustCanvasElementHeightToContentOrMarkOverflow.bind(
                        this,
                    ),
                adjustStuffRelatedToImage:
                    this.adjustStuffRelatedToImage.bind(this),
                getHandleTitlesAsync: this.getHandleTitlesAsync.bind(this),
                startMoving: this.startMoving.bind(this),
                stopMoving: this.stopMoving.bind(this),
            },
            this.snapProvider,
            this.guideProvider,
        );

        this.selectionUi = new CanvasElementSelectionUi({
            getActiveElement: () => this.activeElement,
            setActiveElement: this.setActiveElement.bind(this),
            adjustContainerAspectRatio:
                this.adjustContainerAspectRatio.bind(this),
            startResizeDrag: this.handleDragInteractions.startResizeDrag,
            startSideControlDrag:
                this.handleDragInteractions.startSideControlDrag,
            startMoveCrop: this.handleDragInteractions.startMoveCrop,
            adjustMoveCropHandleVisibility: (removeCropAttrsIfNotNeeded) =>
                this.handleDragInteractions.adjustMoveCropHandleVisibility(
                    removeCropAttrsIfNotNeeded,
                ),
        });

        this.pointerInteractions = new CanvasElementPointerInteractions(
            {
                getActiveElement: () => this.activeElement,
                setActiveElement: this.setActiveElement.bind(this),
                getCanvasElementWeAreTextEditing: () =>
                    this.theCanvasElementWeAreTextEditing,
                setCanvasElementWeAreTextEditing: (element) => {
                    this.theCanvasElementWeAreTextEditing = element;
                },
                isPictureCanvasElement: this.isPictureCanvasElement.bind(this),
                duplicateCanvasElementBox:
                    this.duplicateCanvasElementBox.bind(this),
                adjustCanvasElementLocation:
                    this.adjustCanvasElementLocation.bind(this),
                startMoving: this.startMoving.bind(this),
                stopMoving: this.stopMoving.bind(this),
                setLastMoveContainer: (container) => {
                    this.lastMoveContainer = container;
                },
                resetCropBasis: () => {
                    this.handleDragInteractions.resetCropBasis();
                },
            },
            this.snapProvider,
            this.guideProvider,
        );

        this.keyboardProvider = new CanvasElementKeyboardProvider(
            {
                deleteCurrentCanvasElement:
                    this.deleteCurrentCanvasElement.bind(this),
                moveActiveCanvasElement:
                    this.moveActiveCanvasElement.bind(this),
                getActiveCanvasElement: this.getActiveElement.bind(this),
            },
            this.snapProvider,
        );
        Comical.setSelectorForBubblesWhichTailMidpointMayOverlap(
            ".bloom-backgroundImage",
        );
        const page = document.getElementsByClassName("bloom-page")[0];
        page?.addEventListener("splitterDoubleClick", () => {
            this.adjustAfterOrigamiDoubleClick();
        });
    }

    public moveActiveCanvasElement(
        dx: number,
        dy: number,
        _event: KeyboardEvent,
    ): void {
        if (!this.activeElement) return;

        //Should i use this instead?

        //this.placeElementAtPosition(jQuery(this.activeElement), dx, dy, event);
        // // Get current position and calculate new position
        const currentLeft = CanvasElementManager.pxToNumber(
            this.activeElement.style.left,
        );
        const currentTop = CanvasElementManager.pxToNumber(
            this.activeElement.style.top,
        );

        // Start a snap drag operation
        //this.snapProvider.startDrag();

        // Calculate the target position (current position + delta)
        const targetX = currentLeft + dx;
        const targetY = currentTop + dy;

        // TODO give the snap provider the final say
        // Get the snapped position using the CanvasSnapProvider
        // const { x: snappedX, y: snappedY } = this.snapProvider.getPosition(
        //     event,
        //     targetX,
        //     targetY
        // );
        // Note that adjustCanvasElementLocationRelativeToParent will constrain the
        // movement to keep the element at least slightly visible. So we don't need
        // to take care here that it doesn't move off the screen. However,
        // currently adjustCanvasElementLocationRelativeToParent will not make sure
        // it is on the grid. We may want to change that, or add a check here to
        // make sure it ends up both visible AND on the grid.

        const snappedX = targetX; // Placeholder for snapped X position
        const snappedY = targetY; // Placeholder for snapped Y position

        // Apply movement with snapped coordinates
        const where = new Point(
            snappedX,
            snappedY,
            PointScaling.Unscaled,
            "moveActiveCanvasElement",
        );
        this.adjustCanvasElementLocation(
            this.activeElement,
            this.activeElement.parentElement!,
            where,
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
        growAsMuchAsPossible?: boolean,
    ): number {
        if (editable instanceof HTMLTextAreaElement) {
            // Calendars still use textareas, but we don't do anything with them here.
            return overflowY;
        }

        console.assert(
            editable.classList.contains("bloom-editable"),
            "editable is not a bloom-editable",
        );

        const canvasElement = editable.closest(
            kCanvasElementSelector,
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
            this.getMaxVisibleSiblingHeight(editable) ?? 0,
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

        // Some weird things happen to when the bloom-editable is empty and line-height is small
        // (e.g., less than 1.3 for Andika). In this case, a paragraph whose height is unconstrained
        // will not be high enough to show the font descenders, resulting in a scrollHeight larger than
        // the clientHeight. When the text has no actual descenders, we compute a large overflowY and
        // which corrects for the excessive scrollHeight to give us a good height for the canvas element.
        // However, if the text is empty, we don't get the extra scrollHeight, but still compute a large
        // excess descent, and can easily make the canvas element so small that our overflow checker
        // reports that a child is overflowing. This fudge makes sure that we at least don't make it
        // small enough to cause that problem. There may be a better fix (currently in at least one case
        // we're making an empty box a pixel shorter than one with some content), but I think this might
        // be good enough for 6.2.
        if (newHeight < canvasElement.clientHeight && !editable.textContent) {
            newHeight = Math.max(newHeight, editable.clientHeight);
        }

        // If a lot of text is pasted, the bloom-canvas will scroll down.
        // (This can happen even if the text doesn't necessarily go out the bottom of the bloom-canvas).
        // The children of the bloom-canvas (e.g. img and canvas elements) will be offset above the bloom-canvas.
        // This is an annoying situation, both visually for the image and in terms of computing the correct position for JQuery draggables.
        // So instead, we force the container to scroll back to the top.
        bloomCanvas.scrollTop = 0;

        if (growAsMuchAsPossible === undefined) {
            growAsMuchAsPossible =
                !canvasElement.classList.contains("bloom-noAutoHeight");
        }
        // Check if required height exceeds available height
        if (newHeight + canvasElement.offsetTop > bloomCanvas.clientHeight) {
            if (growAsMuchAsPossible) {
                // If we are allowed to grow as much as possible, we can set the height to the max available height.
                newHeight = bloomCanvas.clientHeight - canvasElement.offsetTop;
                overflowY =
                    overflowY - (newHeight - canvasElement.clientHeight);
            } else {
                return overflowY;
            }
        } else {
            overflowY = 0; // We won't overflow anymore, so return 0 from this method.
        }

        canvasElement.style.height = newHeight + "px";
        // The next method call will change from % positioning to px if needed.  Bloom originally
        // used % values to position canvas elements before we realized that was a bad idea.
        CanvasElementManager.convertCanvasElementPositionToAbsolute(
            canvasElement,
            bloomCanvas,
        );
        this.adjustTarget(canvasElement);
        this.alignControlFrameWithActiveElement();
        return overflowY;
    }

    private getMaxVisibleSiblingHeight(
        editable: HTMLElement,
    ): number | undefined {
        // Get any siblings of our editable that are also visible. (Typically siblings are the
        // other bloom-editables in the same bloom-translationGroup, and are all display:none.)
        const visibleSiblings = Array.from(
            editable.parentElement!.children,
        ).filter((child) => {
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
                    (child) => child.clientTop + child.clientHeight,
                ),
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
                "bloom-editable bloom-visibility-code-on",
            )[0] as HTMLElement;

            this.adjustCanvasElementHeightToContentOrMarkOverflow(editable);
        }
        this.alignControlFrameWithActiveElement();
    }

    public adjustCanvasElementHeightToContentOrMarkOverflow(
        editable: HTMLElement,
    ): void {
        if (!this.activeElement) return;
        OverflowChecker.AdjustSizeOrMarkOverflow(editable);
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
        newPaddingStr: string, // number+px
    ) {
        const wrapperBoxes = Array.from(
            container.getElementsByClassName(kCanvasElementClass),
        ) as HTMLElement[];
        const oldPadding = CanvasElementManager.pxToNumber(oldPaddingStr);
        const newPadding = CanvasElementManager.pxToNumber(newPaddingStr);
        const delta = newPadding - oldPadding;
        const canvasElementLang = GetSettings().languageForNewTextBoxes;
        wrapperBoxes.forEach((wrapperBox) => {
            // The language check is a belt-and-braces thing. At the time I did this PR, we had a bug where
            // the bloom-editables in a TG did not necessarily all have the same style.
            // We could possibly enconuter books where this is still true.
            if (
                Array.from(wrapperBox.getElementsByClassName(style)).filter(
                    (x) => x.getAttribute("lang") === canvasElementLang,
                ).length > 0
            ) {
                if (!wrapperBox.style.height.endsWith("px")) {
                    // Some sort of legacy situation; for a while we had all the placements as percentages.
                    // This will typically not move it, but will force it to the new system of placement
                    // by pixel. Don't want to do this if we don't have to, because there could be rounding
                    // errors that would move it very slightly.
                    this.setCanvasElementPosition(
                        wrapperBox,
                        wrapperBox.offsetLeft - container.offsetLeft,
                        wrapperBox.offsetTop - container.offsetTop,
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
    public static pxToNumber(px: string, fallback: number = NaN): number {
        if (!px) return 0;
        if (px.endsWith("px")) {
            return parseFloat(px.replace("px", ""));
        }
        return fallback;
    }

    // A visible, editable div is generally focusable, but sometimes (e.g. in Bloom games),
    // we may disable it by turning off pointer events. So we filter those ones out.
    private getAllVisibleFocusableDivs(bloomCanvas: HTMLElement): Element[] {
        return this.getAllVisibileEditableDivs(bloomCanvas).filter(
            (focusElement) =>
                window.getComputedStyle(focusElement).pointerEvents !== "none",
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
                "bloom-editable bloom-visibility-code-on",
            ),
        );
        let focusableDivs = editables
            // At least in Bloom games, some elements with visibility code on are nevertheless hidden
            .filter((e) => !EditableDivUtils.isInHiddenLanguageBlock(e));
        focusableDivs = focusableDivs.filter(
            (el) =>
                !(
                    el.parentElement!.classList.contains("box-header-off") ||
                    el.parentElement!.classList.contains(
                        "bloom-imageDescription",
                    )
                ),
        );
        if (focusableDivs.length === 0) {
            focusableDivs = Array.from(
                bloomCanvas.getElementsByClassName(kVideoContainerClass),
            ).filter((x) => !EditableDivUtils.isInHiddenLanguageBlock(x));
        }
        if (focusableDivs.length === 0) {
            focusableDivs = Array.from(
                bloomCanvas.getElementsByClassName(kImageContainerClass),
            ).filter((x) => !EditableDivUtils.isInHiddenLanguageBlock(x));
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
            const focusElement = focusElements[0] as HTMLElement;
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

        bloomCanvases.forEach((bloomCanvas) => {
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
            if (bloomCanvas.getAttribute("data-tool-id") === kCanvasToolId) {
                SetupClickToShowCanvasTool(bloomCanvas);
            }
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
            document.getElementsByClassName(kCanvasElementClass),
        ).filter(
            (x) => !EditableDivUtils.isInHiddenLanguageBlock(x),
        ) as HTMLElement[];
        if (canvasElements.length > 0) {
            // If we have an activeElement and it's not in the list, clear it. (Left over from another page? Deleted?)
            // An earlier version of this code would pick one and set the variable, but not properly select it
            // with SetActiveElement. Don't know why. Definitely harmful when talking book tool wants to set an
            // initial selection but doesn't because it thinks a canvas element is active.
            if (
                this.activeElement &&
                canvasElements.indexOf(this.activeElement) === -1
            ) {
                this.activeElement = undefined;
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
            canvasElements.forEach((container) => {
                this.addEventsToFocusableElements(container, false);
            });
            document.addEventListener(
                "click",
                CanvasElementManager.onDocClickClearActiveElement,
            );
            // If we have sign language video over picture elements that are so far only placeholders,
            // they are not focusable by default and so won't get the blue border that elements
            // are supposed to have when selected. So we add tabindex="0" so they become focusable.
            canvasElements.forEach((element) => {
                const videoContainers = Array.from(
                    element.getElementsByClassName(kVideoContainerClass),
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
                bloomCanvas.addEventListener("click", (event) => {
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
                            "comical-editing",
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
                this.pointerInteractions.setMouseDragHandlers(bloomCanvas);
            },
        );
    }
    removeFocus() {
        if (document.activeElement) {
            (document.activeElement as HTMLElement)?.blur();
        }
    }
    // declare this strange way so it has the right 'this' when added as event listener.
    private canvasElementLosingFocus = (event) => {
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
        const bubble =
            BloomSourceBubbles.ProduceSourceBubbles(translationGroup);
        const divsThatHaveSourceBubbles: HTMLElement[] = [];
        const bubbleDivs: Element[] = [];
        const bubbleJqs: JQuery[] = [];
        if (bubble.length !== 0) {
            divsThatHaveSourceBubbles.push(translationGroup);
            bubbleDivs.push(bubble.get(0));
            bubbleJqs.push(bubble);
        }
        BloomHintBubbles.addHintBubbles(
            translationGroup.parentElement!,
            divsThatHaveSourceBubbles,
            bubbleDivs,
        );

        // at the moment (6.2) we aren't using this for any draggable things, but we could.
        PlaceholderProvider.addPlaceholders(translationGroup.parentElement!);

        if (divsThatHaveSourceBubbles.length > 0) {
            BloomSourceBubbles.MakeSourceBubblesIntoQtips(
                divsThatHaveSourceBubbles[0],
                bubbleJqs[0],
            );
            BloomSourceBubbles.setupSizeChangedHandling(
                divsThatHaveSourceBubbles,
            );
        }
    }

    // if there is a bloom-editable in the canvas element that has a data-bubble-alternate,
    // use it to set the data-bubble of the canvas element. (data-bubble is used by Comical-js,
    // which is continuing to use the term bubble, so I think it's appropriate to still use that
    // name here.)
    adjustCanvasElementsForCurrentLanguage(container: HTMLElement) {
        adjustCanvasElementsForCurrentLanguageFromAlternates(container);
    }

    public static saveStateOfCanvasElementAsCurrentLangAlternate(
        canvasElement: HTMLElement,
        canvasElementLangIn?: string,
    ) {
        saveStateOfCanvasElementAsCurrentLangAlternate(
            canvasElement,
            canvasElementLangIn,
        );
    }

    // Save the current state of things so that we can later position everything
    // correctly for this language, even if in the meantime we change canvas element
    // positions for other languages.
    saveCurrentCanvasElementStateAsCurrentLangAlternate(
        container: HTMLElement,
    ) {
        saveCurrentCanvasElementStateAsCurrentLangAlternateFromAlternates(
            container,
        );
    }

    // "container" refers to a .bloom-canvas-element div, which holds one (and only one) of the
    // 3 main types of canvas element: text, video or image.
    // This method will attach the focusin event to each of these.
    private addEventsToFocusableElements(
        container: HTMLElement,
        includeCkEditor: boolean,
    ) {
        // Arguably, we only need to do this to ones that can be focused. But the sort of disabling
        // that causes editables not to be focusable comes and goes, so rather than have to keep
        // calling this, we'll just set them all up with focus handlers and CkEditor.
        const editables = this.getAllVisibileEditableDivs(container);
        editables.forEach((element) => {
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
            document.getElementsByClassName(kCanvasElementClass),
        ).forEach((element: HTMLElement) => {
            element.addEventListener("focusout", this.canvasElementLosingFocus);
        });
    }

    private handleFocusInEvent(ev: FocusEvent) {
        CanvasElementManager.onFocusSetActiveElement(ev);
    }

    public getAllBloomCanvasesOnPage() {
        return Array.from(
            document.getElementsByClassName(kBloomCanvasClass),
        ) as Array<HTMLElement>;
    }

    // Use this one when adding/duplicating a canvas element to avoid re-navigating the page.
    // If we are passing "undefined" as the canvas element, it's because we just deleted a canvas element
    // and we want Bloom to determine what to select next (it might not be a canvas element at all).
    public refreshCanvasElementEditing(
        bloomCanvas: HTMLElement,
        bubble: Bubble | undefined,
        attachEventsToEditables: boolean,
        activateCanvasElement: boolean,
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
                    attachEventsToEditables,
                );
            }
            SetupElements(
                bloomCanvas,
                activateCanvasElement ? bubble.content : "none",
            );

            // Since we may have just added an element, check if the container has at least one
            // canvas element and add the 'bloom-has-canvas-element' class.
            updateCanvasElementClass(bloomCanvas);
            // There may not really be a changed image, but this is not very costly and covers various cases
            // where we do need it, such as duplicating a picture overlay.
            notifyToolOfChangedImage();
        } else {
            // deleted a canvas element. Don't try to focus anything.
            this.removeControlFrame(); // but don't leave this behind.

            // Also, since we just deleted an element, check if the original container no longer
            // has any canvas elements and remove the 'bloom-has-canvas-element' class.
            updateCanvasElementClass(bloomCanvas);
        }
    }

    private migrateOldCanvasElements(canvasElements: HTMLElement[]): void {
        canvasElements.forEach((top) => {
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
            "page-scaling-container",
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
            "page-scaling-container",
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
                canvasElement as HTMLElement,
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
                "bloom-focusedCanvasElement",
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
    private showCorrespondingTextBox(_element: HTMLElement | undefined) {
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
            document.getElementsByClassName("bloom-focusedCanvasElement"),
        ).forEach((element) => {
            element.classList.remove("bloom-focusedCanvasElement");
        });
    }

    // Some controls, such as MUI menus, temporarily steal focus. We don't want the usual
    // loss-of-focus behavior, so this allows suppressing it.
    public static ignoreFocusChanges: boolean;
    // If the menu command brings up a dialog, we still don't want the active bubble to
    // change. This flag allows us to ignore the next focus change.  See BL-14123.
    public static skipNextFocusChange: boolean;

    public setIgnoreFocusChanges(
        ignore: boolean,
        skipNextFocusChange?: boolean,
    ) {
        CanvasElementManager.ignoreFocusChanges = ignore;
        if (skipNextFocusChange) {
            CanvasElementManager.skipNextFocusChange = true;
        }
    }

    public setActiveElementToClosest(element: HTMLElement) {
        this.setActiveElement(
            (element.closest(kCanvasElementSelector) as HTMLElement) ??
                undefined,
        );
    }

    public setActiveElement(element: HTMLElement | undefined) {
        // Seems it should be sufficient to remove this from the old active element if any.
        // But there's at least one case where code that adds a new canvas element sets it as
        // this.activeElement before calling this method. It's safest to make sure this
        // attribute is not set on any other element.
        document.querySelectorAll("[data-bloom-active]").forEach((e) => {
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
                    "bloom-videoContainer",
                )[0] as HTMLElement,
                false,
            );
            // if the active element isn't a text one, we don't want anything to have focus.
            // One reason is that the thing that has focus may display a source bubble that
            // hides what we're trying to work on.
            // (If we one day try to make Bloom fully accessible, we may have to instead allow
            // non-text elements to have focus so that keyboard commands can be applied to them.)
            if (
                this.activeElement.getElementsByClassName(
                    "bloom-visibility-code-on",
                ).length === 0
            ) {
                this.removeFocus();
            }
        }
        UpdateImageTooltipVisibility(
            this.activeElement?.closest(kBloomCanvasSelector),
        );
    }

    // Remove the canvas element control frame if it exists (when no canvas element is active)
    // Also remove the menu if it's still open.  See BL-13852.
    public removeControlFrame(): void {
        this.selectionUi.removeControlFrame();
    }

    // Set up the control frame for the active canvas element. This includes creating it if it
    // doesn't exist, and positioning it correctly.
    public setupControlFrame(): void {
        this.selectionUi.setupControlFrame();
    }

    private minWidth = 30; // @MinTextBoxWidth in canvasTool.less
    private minHeight = 30; // @MinTextBoxHeight in canvasTool.less

    private getImageOrVideo(): HTMLElement | undefined {
        // It will have one or the other or neither, but not both, so it doesn't much matter
        // which we search for first. But images are probably more common.
        const imgC =
            this.activeElement?.getElementsByClassName(kImageContainerClass)[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (img) return img;
        const videoC = this.activeElement?.getElementsByClassName(
            "bloom-videoContainer",
        )[0];
        const video = videoC?.getElementsByTagName("video")[0];
        return video;
    }

    private adjustStuffRelatedToImage(
        activeElement: HTMLElement,
        img: HTMLImageElement | undefined,
    ) {
        this.alignControlFrameWithActiveElement();
        this.adjustTarget(this.activeElement);
        notifyToolOfChangedImage(img);
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
            kBloomCanvasSelector,
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
            kBloomCanvasSelector,
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
    public adjustContainerAspectRatio(
        canvasElement: HTMLElement,
        useSizeOfNewImage = false,
        // Sometimes we think we need to wait for onload, but the data arrives before we set up
        // the watcher. We make a timeout so we will go ahead and adjust if we have dimensions
        // and don't get an onload in a reasonable time. If we DO get the onload before we
        // timeout, we use this handle to clear it.
        // This is set when we arrange an onload callback and receive it
        timeoutHandler: number = 0,
    ): void {
        if (timeoutHandler) {
            clearTimeout(timeoutHandler);
        }
        if (canvasElement.classList.contains(kBackgroundImageClass)) {
            this.adjustBackgroundImageSize(
                canvasElement.closest(kBloomCanvasSelector)!,
                canvasElement,
                useSizeOfNewImage,
            );
            return;
        }
        if (canvasElement.classList.contains(kBloomButtonClass)) {
            // Let image buttons keep their manually set size (BL-15738)
            // Enhance: refactor the whole method so we don't have to remember to call alignControlFrameWithActiveElement
            // separately on every return path
            this.alignControlFrameWithActiveElement();
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
                isPlaceHolderImage(imgOrVideo.getAttribute("src")) ||
                (imgOrVideo.naturalHeight === 0 && // not loaded successfully (yet)
                    !useSizeOfNewImage && // not waiting for new dimensions
                    imgOrVideo.classList.contains("bloom-imageLoadError")) // error occurred while trying to load
            ) {
                // Image is in an error state or is just a placeholder; we probably won't ever get useful dimensions. Just leave
                // the canvas element the shape it is.
                this.alignControlFrameWithActiveElement();
                return;
            }
            if (imgHeight === 0 || useSizeOfNewImage) {
                // image not ready yet, try again later.
                const handle = setTimeout(
                    () =>
                        this.adjustContainerAspectRatio(
                            canvasElement,
                            false, // if we've got dimensions just use them
                            0,
                        ), // if we get this call we don't have a timeout to cancel
                    // I think this is long enough that we won't be seeing obsolete data (from a previous src).
                    // OTOH it's not hopelessly long for the user to wait when we don't get an onload.
                    // If by any chance this happens when the image really isn't loaded enough to
                    // have naturalHeight/Width, the zero checks above will force another iteration.
                    100,
                    // somehow Typescript is confused and thinks this is a NodeJS version of setTimeout.
                ) as unknown as number;
                imgOrVideo.addEventListener(
                    "load",
                    () =>
                        this.adjustContainerAspectRatio(
                            canvasElement,
                            false, // it's loaded, we don't want to wait again
                            handle,
                        ), // if we get this call we can cancel the timeout above.
                    { once: true },
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
                    { once: true },
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
        if (Math.abs(oldHeight - newHeight) <= 0.1) {
            // don't let small rounding errors accumulate
            newHeight = oldHeight;
        } else {
            canvasElement.style.height = `${newHeight}px`;
        }
        // and move container down so image does not move
        const oldTop = canvasElement.offsetTop;
        let newTop = oldTop + (oldHeight - newHeight) / 2;

        const oldWidth = canvasElement.clientWidth;
        if (Math.abs(oldWidth - newWidth) <= 0.1) {
            newWidth = oldWidth;
        } else {
            canvasElement.style.width = `${newWidth}px`;
        }
        // and move container right so image does not move
        const oldLeft = canvasElement.offsetLeft;
        let newLeft = oldLeft + (oldWidth - newWidth) / 2;

        // except, if it was "on the grid" before, such as a newly added placeholder,
        // or we just changed the image, we want to keep it on the grid.
        const adjustedOld = this.snapProvider.getPosition(
            undefined,
            oldLeft,
            oldTop,
        );
        if (adjustedOld.x === oldLeft && adjustedOld.y === oldTop) {
            // it was on the grid, so we want to keep it there.
            const adjustedNew = this.snapProvider.getPosition(
                undefined,
                newLeft,
                newTop,
            );
            newLeft = adjustedNew.x;
            newTop = adjustedNew.y;
        }

        canvasElement.style.left = `${newLeft}px`;
        canvasElement.style.top = `${newTop}px`;
        this.alignControlFrameWithActiveElement();
        if (this.doAfterNewImageAdjusted) {
            this.doAfterNewImageAdjusted();
            this.doAfterNewImageAdjusted = undefined;
        }
        copyContentToTarget(canvasElement);
    }

    // When the image is changed in a canvas element (e.g., choose or paste image),
    // we remove cropping, adjust the aspect ratio, and move the control frame.
    updateCanvasElementForChangedImage(imgOrImageContainer: HTMLElement) {
        const canvasElement = imgOrImageContainer.closest(
            kCanvasElementSelector,
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
                true,
            );
            SetupMetadataButton(canvasElement);
        } else {
            this.adjustContainerAspectRatio(canvasElement, true);
        }
    }

    private doAfterNewImageAdjusted: (() => void) | undefined = undefined;

    private async getHandleTitlesAsync(
        controlFrame: HTMLElement,
        className: string,
        l10nId: string,
        force: boolean = false,
        attribute: string = "title",
    ) {
        return this.selectionUi.getHandleTitlesAsync(
            controlFrame,
            className,
            l10nId,
            force,
            attribute,
        );
    }

    // Align the control frame with the active canvas element.
    private alignControlFrameWithActiveElement = () => {
        this.selectionUi.alignControlFrameWithActiveElement();
    };

    adjustContextControlPosition(
        controlFrame: HTMLElement | null,
        controlsAbove: boolean,
    ) {
        this.selectionUi.adjustContextControlPosition(
            controlFrame,
            controlsAbove,
        );
    }

    public doNotifyChange() {
        const bubble = this.getPatriarchBubbleOfActiveElement();
        this.thingsToNotifyOfCanvasElementChange.forEach((f) =>
            f.handler(bubble),
        );
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
            relatives.forEach((bubble) =>
                this.setTextColorInternal(hexOrRgbColor, bubble.content),
            );
        }
        this.restoreFocus();
    }

    private setTextColorInternal(hexOrRgbColor: string, element: HTMLElement) {
        // BL-11621: We are in the process of moving to putting the canvas element text color on the inner
        // bloom-editables. So we clear any color on the canvas element div and set it on all of the
        // inner bloom-editables.
        const topBox = element.closest(
            kCanvasElementSelector,
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
                kCanvasElementSelector,
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
                const editables =
                    topBox.getElementsByClassName("bloom-editable");
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
                        textColor =
                            this.getDefaultStyleTextColor(firstEditable);
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
                opacity,
            );
        }
        if (this.activeElement.classList.contains(kBloomButtonClass)) {
            // Possibly we should do this in more cases, but I don't want to mess with
            // existing element types. When we're really making a bubble shape, we
            // need to let Comical.js handle the background color, so it is the right
            // shape to match the bubble. For text without a bubble shape, it would
            // probably be simpler to just set it like we do here, but it
            // doesn't matter much. For text buttons, we definitely want to do it using
            // the style, so the background color obeys the border radius of the button
            // and the shadow appears in the right place...makes everything simpler.
            if (newBackgroundColors.length === 1) {
                this.activeElement.style.background = "";
                this.activeElement.style.backgroundColor =
                    newBackgroundColors[0];
            } else {
                this.activeElement.style.backgroundColor = "";
                this.activeElement.style.background = `linear-gradient(${newBackgroundColors.join(", ")})`;
            }
            return;
        }
        this.updateSelectedItemBubbleSpec({
            backgroundColors: newBackgroundColors,
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
    private restoreFocus(): void {
        this.selectionUi.restoreFocus();
    }

    public setThingToFocusAfterSettingColor(x: HTMLElement): void {
        this.selectionUi.setThingToFocusAfterSettingColor(x);
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
        container.ondragover = (ev) => {
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
        container.ondrop = (ev) => {
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
                    style as CanvasElementType,
                );
            }
        };
    }

    // Setup event handlers that allow the canvas element to be moved around or resized.
    private setMouseDragHandlers(bloomCanvas: HTMLElement): void {
        this.pointerInteractions.setMouseDragHandlers(bloomCanvas);
    }

    // Move all child canvas elements as necessary so they are at least partly inside their container
    // (by as much as we require when dragging them).
    public ensureCanvasElementsIntersectParent(parentContainer: HTMLElement) {
        const canvasElements = Array.from(
            parentContainer.getElementsByClassName(kCanvasElementClass),
        ) as HTMLElement[];
        let changed = false;
        canvasElements.forEach((canvasElement) => {
            // If the canvas element is not visible, its width will be 0. Don't try to adjust it.
            if (canvasElement.clientWidth === 0) return;
            // If we're in image description mode, the algorithm won't work right,
            // and it probably isn't necessary.
            if (canvasElement.closest(".bloom-describedImage")) return;

            // Careful. For older books, left and top might be percentages.
            const canvasElementRect = canvasElement.getBoundingClientRect();
            const parentRect = parentContainer.getBoundingClientRect();

            this.adjustCanvasElementLocation(
                canvasElement,
                parentContainer,
                new Point(
                    canvasElementRect.left - parentRect.left,
                    canvasElementRect.top - parentRect.top,
                    PointScaling.Scaled,
                    "ensureCanvasElementsIntersectParent",
                ),
            );
            changed = this.ensureTailsInsideParent(
                parentContainer,
                canvasElement,
                changed,
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
        changed: boolean,
    ) {
        const originalTailSpecs = Bubble.getBubbleSpec(canvasElement).tails;
        const newTails = originalTailSpecs.map((spec) => {
            const tipPoint = this.adjustRelativePointToBloomCanvas(
                bloomCanvas,
                new Point(
                    spec.tipX,
                    spec.tipY,
                    PointScaling.Unscaled,
                    "ensureTailsInsideParent.tip",
                ),
            );
            const midPoint = this.adjustRelativePointToBloomCanvas(
                bloomCanvas,
                new Point(
                    spec.midpointX,
                    spec.midpointY,
                    PointScaling.Unscaled,
                    "ensureTailsInsideParent.tip",
                ),
            );
            changed =
                changed || // using changed ||= works but defeats prettier
                spec.tipX !== tipPoint.getUnscaledX() ||
                spec.tipY !== tipPoint.getUnscaledY() ||
                spec.midpointX !== midPoint.getUnscaledX() ||
                spec.midpointY !== midPoint.getUnscaledY();
            return {
                ...spec,
                tipX: tipPoint.getUnscaledX(),
                tipY: tipPoint.getUnscaledY(),
                midpointX: midPoint.getUnscaledX(),
                midpointY: midPoint.getUnscaledY(),
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
        positionInBloomCanvas: Point,
    ) {
        const parentWidth = container.clientWidth;
        const parentHeight = container.clientHeight;
        const left = positionInBloomCanvas.getUnscaledX();
        const right = left + canvasElement.clientWidth;
        const top = positionInBloomCanvas.getUnscaledY();
        const bottom = top + canvasElement.clientHeight;
        let x = left;
        let y = top;
        if (right < this.minCanvasElementVisible) {
            x = this.minCanvasElementVisible - canvasElement.clientWidth;
        }
        if (left > parentWidth - this.minCanvasElementVisible) {
            x = parentWidth - this.minCanvasElementVisible;
        }
        if (bottom < this.minCanvasElementVisible) {
            y = this.minCanvasElementVisible - canvasElement.clientHeight;
        }
        if (top > parentHeight - this.minCanvasElementVisible) {
            y = parentHeight - this.minCanvasElementVisible;
        }
        // The 0.1 here is rather arbitrary. On the one hand, I don't want to do all the work
        // of placeElementAtPosition in the rather common case that we're just checking canvas element
        // positions at startup and none need to move. On the other hand, we're dealing with scaling
        // here, and it's possible that even a half pixel might get scaled so that the difference
        // is noticeable. I'm compromizing on a discrepancy that is less than a pixel at our highest
        // zoom.
        if (
            Math.abs(x - canvasElement.offsetLeft) > 0.1 ||
            Math.abs(y - canvasElement.offsetTop) > 0.1
        ) {
            const moveTo = new Point(
                x,
                y,
                PointScaling.Unscaled,
                "AdjustCanvasElementLocation",
            );
            this.placeElementAtPosition($(canvasElement), container, moveTo);
        }
        this.alignControlFrameWithActiveElement();
    }

    // Add the classes that let various controls know that a move, resize, or drag is in progress.
    private startMoving() {
        const controlFrame = document.getElementById(
            "canvas-element-control-frame",
        );
        controlFrame?.classList?.add("moving");
        this.activeElement?.classList?.add("moving");
        document
            .getElementById("canvas-element-context-controls")
            ?.classList?.add("moving");
    }

    private stopMoving() {
        if (this.lastMoveContainer) this.lastMoveContainer.style.cursor = "";
        // We want to get rid of it at least from the control frame and the active canvas element,
        // but may as well make sure it doesn't get left anywhere.
        Array.from(document.getElementsByClassName("moving")).forEach(
            (element) => {
                element.classList.remove("moving");
            },
        );
        this.handleDragInteractions.adjustMoveCropHandleVisibility();
        this.alignControlFrameWithActiveElement();
    }

    // If we get a click (without movement) on a text canvas element, we treat subsequent mouse events on
    // that canvas element as text editing events, rather than drag events, as long as it keeps focus.
    // This is the canvas element, if any, that is currently in that state.
    public theCanvasElementWeAreTextEditing: HTMLElement | undefined;

    // Gets the coordinates of the specified event relative to the specified element.
    private static convertPointFromViewportToElementFrame(
        pointRelativeToViewport: Point, // The current point, relative to the top-left of the viewport
        element: Element, // The element to reference for the new origin
    ): Point {
        return convertPointFromViewportToElementFrameFromGeometry(
            pointRelativeToViewport,
            element,
        );
    }

    // Gets an element's border width/height of an element
    //   The x coordinate of the point represents the left border width
    //   The y coordinate of the point represents the top border height
    private static getLeftAndTopBorderWidths(element: Element): Point {
        return getLeftAndTopBorderWidthsFromGeometry(element);
    }

    // Gets an element's border width/height of an element
    //   The x coordinate of the point represents the right border width
    //   The y coordinate of the point represents the bottom border height
    private static getRightAndBottomBorderWidths(
        element: Element,
        styleInfo?: CSSStyleDeclaration,
    ): Point {
        return getRightAndBottomBorderWidthsFromGeometry(element, styleInfo);
    }

    // Gets an element's border width/height
    //   The x coordinate of the point represents the sum of the left and right border width
    //   The y coordinate of the point represents the sum of the top and bottom border width
    private static getCombinedBorderWidths(
        element: Element,
        styleInfo?: CSSStyleDeclaration,
    ): Point {
        return getCombinedBorderWidthsFromGeometry(element, styleInfo);
    }

    // Given a CSSStyleDeclearation, retrieves the requested padding and converts it to a number
    private static getPadding(
        side: string,
        styleInfo: CSSStyleDeclaration,
    ): number {
        return getPaddingFromGeometry(side, styleInfo);
    }

    // Gets the padding of an element
    //   The x coordinate of the point represents the left padding
    //   The y coordinate of the point represents the bottom padding
    private static getLeftAndTopPaddings(
        element: Element, // The element to check
        styleInfo?: CSSStyleDeclaration, // Optional. If you have it handy, you can pass in the computed style of the element. Otherwise, it will be determined for you
    ): Point {
        return getLeftAndTopPaddingsFromGeometry(element, styleInfo);
    }

    // Gets the padding of an element
    //   The x coordinate of the point represents the left padding
    //   The y coordinate of the point represents the bottom padding
    private static getRightAndBottomPaddings(
        element: Element, // The element to check
        styleInfo?: CSSStyleDeclaration, // Optional. If you have it handy, you can pass in the computed style of the element. Otherwise, it will be determined for you
    ): Point {
        return getRightAndBottomPaddingsFromGeometry(element, styleInfo);
    }

    // Gets the padding of an element
    // The x coordinate of the point represents the sum of the left and right padding
    // The y coordinate of the point represents the sum of the top and bottom padding
    private static getCombinedPaddings(
        element: Element,
        styleInfo?: CSSStyleDeclaration,
    ): Point {
        return getCombinedPaddingsFromGeometry(element, styleInfo);
    }

    // Gets the sum of an element's borders and paddings
    // The x coordinate of the point represents the sum of the left and right
    // The y coordinate of the point represents the sum of the top and bottom
    private static getCombinedBordersAndPaddings(element: Element): Point {
        return getCombinedBordersAndPaddingsFromGeometry(element);
    }

    // Returns the amount the element has been scrolled, as a Point
    private static getScrollAmount(element: Element): Point {
        return getScrollAmountFromGeometry(element);
    }

    // Removes the units from a string like "10px"
    public static extractNumber(text: string | undefined | null): number {
        return extractNumberFromGeometry(text);
    }

    // Returns a string representing which style of resize to use
    // This is based on where the mouse event is relative to the center of the element
    //
    // The returned string is the directional prefix to the *-resize cursor values
    // e.g., if "ne-resize" would be appropriate, this function will return the "ne" prefix
    // e.g. "ne" = Northeast, "nw" = Northwest", "sw" = Southwest, "se" = Southeast"
    private getResizeMode(
        element: HTMLElement,
        event: MouseEvent,
    ): ResizeDirection {
        // Convert into a coordinate system where the origin is the center of the element (rather than the top-left of the page)
        const center = this.getCenterPosition(element);
        const clickCoordinates = { x: event.pageX, y: event.pageY };
        const relativeCoordinates = {
            x: clickCoordinates.x - center.x,
            y: clickCoordinates.y - center.y,
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
        this.getAllBloomCanvasesOnPage().forEach((bloomCanvas) =>
            this.saveCurrentCanvasElementStateAsCurrentLangAlternate(
                bloomCanvas as HTMLElement,
            ),
        );

        EnableAllImageEditing();

        // Clean up event listeners that we no longer need
        Array.from(
            document.getElementsByClassName(kCanvasElementClass),
        ).forEach((container) => {
            const editables = this.getAllVisibileEditableDivs(
                container as HTMLElement,
            );
            editables.forEach((element) => {
                // Don't use an arrow function as an event handler here. These can never be identified as duplicate event listeners, so we'll end up with tons of duplicates
                element.removeEventListener(
                    "focusin",
                    CanvasElementManager.onFocusSetActiveElement,
                );
            });
        });
        document.removeEventListener(
            "click",
            CanvasElementManager.onDocClickClearActiveElement,
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
        notifier: (bubble: Bubble | undefined) => void,
    ): void {
        this.detachCanvasElementChangeNotification(id);
        this.thingsToNotifyOfCanvasElementChange.push({
            id,
            handler: notifier,
        });
    }

    public detachCanvasElementChangeNotification(id: string): void {
        const index = this.thingsToNotifyOfCanvasElementChange.findIndex(
            (x) => x.id === id,
        );
        if (index >= 0) {
            this.thingsToNotifyOfCanvasElementChange.splice(index, 1);
        }
    }

    public updateSelectedItemBubbleSpec(
        newBubbleProps: BubbleSpecPattern,
    ): BubbleSpec | undefined {
        if (!this.activeElement) {
            return undefined;
        }

        // ENHANCE: Constructing new canvas element instances is dangerous. It may get out of sync with the instance that Comical knows about.
        // It would be preferable if we asked Comical to find the canvas element instance corresponding to this element.
        const activeBubble = new Bubble(this.activeElement);

        return this.updateBubbleWithPropsHelper(activeBubble, newBubbleProps);
    }

    public updateSelectedFamilyBubbleSpec(
        newBubbleProps: BubbleSpecPattern,
    ): Bubble {
        const parentBubble = this.getPatriarchBubbleOfActiveElement();
        this.updateBubbleWithPropsHelper(parentBubble, newBubbleProps);
        return parentBubble!;
    }

    private updateBubbleWithPropsHelper(
        bubble: Bubble | undefined,
        newBubbleProps: BubbleSpecPattern,
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
        this.draggableIntegration.adjustCanvasElementOrdering();
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
        offsetY: number,
    ): void {
        this.factories.addChildCanvasElementAndRefreshPage(
            parentElement,
            offsetX,
            offsetY,
        );
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
            kComicalGeneratedClass,
        );
        if (comicalGenerated.length > 0) {
            Comical.update(bloomCanvas);
        }
    }

    private addChildInternal(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number,
    ): HTMLElement | undefined {
        return this.factories.addChildCanvasElement(
            parentElement,
            offsetX,
            offsetY,
        );
    }

    // The 'new canvas element' is either going to be a child of the 'parentElement', or a duplicate of it.
    private findBestLocationForNewCanvasElement(
        parentElement: HTMLElement,
        proposedOffsetX: number,
        proposedOffsetY: number,
    ): Point | undefined {
        return this.factories.findBestLocationForNewCanvasElement(
            parentElement,
            proposedOffsetX,
            proposedOffsetY,
        );
    }

    // This method looks very similar to 'adjustRectToImageContainer' above, but the tailspec coordinates
    // here are already relative to the bloom-canvas's coordinates, which introduces some differences.
    private adjustRelativePointToBloomCanvas(
        bloomCanvas: Element,
        point: Point,
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
            "Scaled viewport coordinates",
        );
    }

    public addCanvasElementWithScreenCoords(
        screenX: number,
        screenY: number,
        canvasElementType: CanvasElementType,
        userDefinedStyleName?: string,
        rightTopOffset?: string,
    ): HTMLElement | undefined {
        return this.factories.addCanvasElementWithScreenCoords(
            screenX,
            screenY,
            canvasElementType,
            userDefinedStyleName,
            rightTopOffset,
        );
    }

    private addCanvasElementFromOriginal(
        offsetX: number,
        offsetY: number,
        originalElement: HTMLElement,
        style?: string,
    ): HTMLElement | undefined {
        return this.factories.addCanvasElementFromOriginal(
            offsetX,
            offsetY,
            originalElement,
            style,
        );
    }

    private isCanvasElementWithClass(
        canvasElement: HTMLElement,
        className: string,
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
            kImageContainerClass,
        );
    }

    private isVideoCanvasElement(canvasElement: HTMLElement): boolean {
        return this.isCanvasElementWithClass(
            canvasElement,
            kVideoContainerClass,
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
        rightTopOffset?: string,
    ): HTMLElement | undefined {
        return this.factories.addCanvasElement(
            mouseX,
            mouseY,
            canvasElementType,
            userDefinedStyleName,
            rightTopOffset,
        );
    }

    private addVideoCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        return this.factories.addVideoCanvasElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
        );
    }

    public getActiveOrFirstBloomCanvasOnPage(): HTMLElement | null {
        // If there is an active element, use its bloom canvas.
        // Otherwise, return the first bloom canvas on the page.
        if (this.activeElement) {
            const bloomCanvas = CanvasElementManager.getBloomCanvas(
                this.activeElement,
            );
            if (bloomCanvas) {
                return bloomCanvas;
            }
        }
        const bloomCanvases = this.getAllBloomCanvasesOnPage();
        return bloomCanvases.length > 0 ? bloomCanvases[0] : null;
    }

    // This is called when the user pastes an image from the clipboard.
    // If there is an active canvas element that is an image, and it is empty (placeholder),
    // set its image to the pasted image.
    // Otherwise, if there is a bloom canvas on the page, it will pick the one that has the active element
    // or the first one if none has an active element.
    // (If there is no canvas, it returns false.)
    // If the canvas is empty (including the background), set the background to the image.
    // Else if canvas is allowed by the subscription tier, add the image as a canvas/game item.
    // Make it up to 1/3 width and 1/3 height of the canvas, roughly centered on the canvas.
    // Is it a draggable item? Yes, if we are in the "Start" mode of a game.
    // In that case, we put it a bit higher and further left, so there is room for the target.
    // Otherwise it's just a normal canvas overlay item (restricted to the appropriate state,
    // if we're in the Correct or Wrong state of a game).
    public pasteImageFromClipboard(): boolean {
        return this.clipboard.pasteImageFromClipboard();
    }
    public finishPasteImageFromClipboard(imageInfo: IImageInfo): void {
        this.clipboard.finishPasteImageFromClipboard(imageInfo);
    }

    private addPictureCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
        imageInfo?: {
            imageId: string;
            src: string; // must already appropriately URL-encoded.
            copyright: string;
            creator: string;
            license: string;
        },
        size?: { width: number; height: number },
        doAfterElementCreated?: (newElement: HTMLElement) => void,
    ): HTMLElement {
        return this.factories.addPictureCanvasElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
            imageInfo,
            size,
            doAfterElementCreated,
        );
    }
    private addNavigationImageButtonElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
        imageInfo?: {
            imageId: string;
            src: string; // must already appropriately URL-encoded.
            copyright: string;
            creator: string;
            license: string;
        },
        doAfterElementCreated?: (newElement: HTMLElement) => void,
    ): HTMLElement {
        return this.factories.addNavigationImageButtonElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
            imageInfo,
            doAfterElementCreated,
        );
    }

    private addNavigationImageWithLabelButtonElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
        imageInfo?: {
            imageId: string;
            src: string; // must already appropriately URL-encoded.
            copyright: string;
            creator: string;
            license: string;
        },
    ): HTMLElement {
        return this.factories.addNavigationImageWithLabelButtonElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
            imageInfo,
        );
    }

    private addNavigationLabelButtonElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        return this.factories.addNavigationLabelButtonElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
        );
    }

    private addSoundCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        return this.factories.addSoundCanvasElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
        );
    }

    private addBookLinkGridCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        return this.factories.addBookLinkGridCanvasElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
        );
    }

    private addRectangleCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        return this.factories.addRectangleCanvasElement(
            location,
            bloomCanvasJQuery,
            rightTopOffset,
        );
    }

    // Put the rectangle in the right place in the DOM so it is behind the other canvas elements
    // but in front of the background image.  Also adjust the ComicalJS bubble level so it is in
    // front of the the background image.
    private reorderRectangleCanvasElement(
        rectangle: HTMLElement,
        bloomCanvas: HTMLElement,
    ): void {
        this.factories.reorderRectangleCanvasElement(rectangle, bloomCanvas);
    }
    public setDefaultHeightFromWidth(canvasElement: HTMLElement) {
        this.factories.setDefaultHeightFromWidth(canvasElement);
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
    // positionInBloomCanvas and rightTopOffset determine where to place the element.
    // If rightTopOffset is falsy, we put the element's top left at positionInBloomCanvas.
    // If rightTopOffset is truthy, it is a string like "10,-20" which are values to
    // add to positionInBloomCanvas (which in this case is the mouse position where
    // something was dropped, relative to canvas) to get the top right of the visual object that was dropped.
    // Then we position the new element so its top right is at that same point.
    // Note: I wish we could just make this adjustment in the dragEnd event handler
    // which receives both the point and the rightTopOffset data, but it does not
    // have access to the element being created to get its width. We could push it up
    // one level into finishAddingCanvasElement, but it's simpler here where we're
    // already extracting and adjusting the offsets from positionInViewport
    private placeElementAtPosition(
        wrapperBox: JQuery,
        container: Element,
        positionInBloomCanvas: Point,
        rightTopOffset?: string,
    ) {
        let xOffset = positionInBloomCanvas.getUnscaledX();
        let yOffset = positionInBloomCanvas.getUnscaledY();
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
            // This is a bit of a kludge, but we want the position snapped here in exactly the cases
            // (dragging from the toolbox) where snapping has not already been handled...and can't easily
            // be handled at a higher level because we want the snap to take effect AFTER we adjust for
            // rightTopOffset, that is, the final position should be snapped.
            // It's conceivable that somewhere in the call stack there's an event we could use to see
            // whether the ctrl key is down, but initial placement of new elements is so inexact that
            // I don't see any point in allowing it to be unsnapped.
            const { x, y } = this.snapProvider.getPosition(
                undefined,
                xOffset,
                yOffset,
            );
            xOffset = x;
            yOffset = y;
        }

        // Note: This code will not clear out the rest of the style properties... they are preserved.
        //       If some or all style properties need to be removed before doing this processing, it is the caller's responsibility to do so beforehand
        //       The reason why we do this is because a canvas element's onmousemove handler calls this function,
        //       and in that case we want to preserve the canvas element's width/height which are set in the style
        wrapperBox.css("left", xOffset); // assumes numbers are in pixels
        wrapperBox.css("top", yOffset); // assumes numbers are in pixels

        CanvasElementManager.setCanvasElementPosition(
            wrapperBox.get(0) as HTMLElement,
            xOffset,
            yOffset,
        );

        this.adjustTarget(wrapperBox.get(0));
    }

    private adjustTarget(draggable: HTMLElement | undefined) {
        this.draggableIntegration.adjustTarget(draggable);
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
                notifyToolOfChangedImage(img);
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
            false,
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
        sameLocation?: boolean,
    ): HTMLElement | undefined {
        return this.duplication.duplicateCanvasElementBox(
            textElement,
            sameLocation,
        );
    }

    public startDraggingSplitter() {
        this.editingSuspension.startDraggingSplitter();
    }

    public endDraggingSplitter() {
        this.editingSuspension.endDraggingSplitter();
    }

    public suspendComicEditing(
        forWhat: "forDrag" | "forTool" | "forGamePlayMode" | "forJqueryResize",
    ) {
        this.editingSuspension.suspendComicEditing(forWhat);
    }

    public checkActiveElementIsVisible() {
        this.selectionUi.checkActiveElementIsVisible();
    }

    public resumeComicEditing() {
        this.editingSuspension.resumeComicEditing();
    }

    public adjustAfterOrigamiDoubleClick() {
        // make sure we're not still in a dragging-the-splitter state
        theOneCanvasElementManager.resumeComicEditing();
        // this is automatic for changes that happen while we're dragging,
        // but dragging gets stopped by mouse up, so we need to do it here.
        theOneCanvasElementManager.handleResizeAdjustments();
    }

    public removeDetachedTargets() {
        this.draggableIntegration.removeDetachedTargets();
    }

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
        if (this.editingSuspension.isSuspended()) {
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
        this.editingSuspension.setupSplitterEventHandling();
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
                        !x.classList.contains("bloom-videoControlContainer"),
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
        } catch {
            //console.log("Error removing resizable widget");
        }
    }

    // Converts a canvas element's position to absolute in pixels (using CSS styling)
    // (Used to be a percentage of parent size. See comments on setTextboxPosition.)
    // canvasElement: The thing we want to position
    // bloomCanvas: Optional. The bloom-canvas the canvas element is in. If this parameter is not defined, the function will automatically determine it.
    private static convertCanvasElementPositionToAbsolute(
        canvasElement: HTMLElement,
        bloomCanvas?: Element | null | undefined,
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
                "convertTextboxPositionToAbsolute()",
            );
            const reframedPoint = this.convertPointFromViewportToElementFrame(
                wrapperBoxPos,
                bloomCanvas,
            );
            unscaledRelativeLeft = reframedPoint.getUnscaledX();
            unscaledRelativeTop = reframedPoint.getUnscaledY();
        } else {
            console.assert(
                false,
                "convertTextboxPositionToAbsolute(): container was null or undefined.",
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
            canvasElement,
            unscaledRelativeLeft,
            unscaledRelativeTop,
        );
    }

    // Sets a canvas element's position to what is passed in.
    // (This code also tries to update the canvas element's size if it's not already
    // set as "px". Earlier versions of Bloom
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
        canvasElement: HTMLElement,
        unscaledRelativeLeft: number,
        unscaledRelativeTop: number,
    ) {
        setCanvasElementPositionFromPositioning(
            canvasElement,
            unscaledRelativeLeft,
            unscaledRelativeTop,
        );
    }

    // Determines the unrounded width/height of the content of an element (i.e, excluding its margin, border, padding)
    //
    // This differs from JQuery width/height because those functions give you values rounded to the nearest pixel.
    // This differs from getBoundingClientRect().width because that function includes the border and padding of the element in the width.
    // This function returns the interior content's width/height (unrounded), without any margin, border, or padding
    private static getInteriorWidthHeight(element: HTMLElement): Point {
        return getInteriorWidthHeightFromPositioning(element);
    }

    // Lots of places we need to find the bloom-canvas that a particular element resides in.
    // Method is static because several of the callers are static.
    // Return null if element isn't in a bloom-canvas at all.
    private static getBloomCanvas(element: Element): HTMLElement | null {
        return getBloomCanvasFromPositioning(element);
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
        return inPlayModeFromPositioning(someElt);
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
        const [offsetX, offsetY] =
            CanvasElementManager.GetChildPositionFromParentCanvasElement(
                parentElement,
                bubbleSpec,
            );
        this.addChildCanvasElementAndRefreshPage(
            parentElement,
            offsetX,
            offsetY,
        );
    }

    // Returns a 2-tuple containing the desired x and y offsets of the child canvas element from the parent canvas element
    //   (i.e., offsetX = child.left - parent.left)
    public static GetChildPositionFromParentCanvasElement(
        parentElement: HTMLElement,
        parentBubbleSpec: BubbleSpec | undefined,
    ): number[] {
        return getChildPositionFromParentCanvasElementFromPositioning(
            parentElement,
            parentBubbleSpec,
        );
    }

    private revertBackgroundCanvasElements() {
        this.backgroundImageManager.revertBackgroundCanvasElements();
    }

    private handleResizeAdjustments() {
        this.backgroundImageManager.handleResizeAdjustments();
    }

    private adjustBackgroundImageSize(
        bloomCanvas: HTMLElement,
        bgCanvasElement: HTMLElement,
        useSizeOfNewImage: boolean,
    ) {
        this.backgroundImageManager.adjustBackgroundImageSize(
            bloomCanvas,
            bgCanvasElement,
            useSizeOfNewImage,
        );
    }

    public AdjustChildrenIfSizeChanged(bloomCanvas: HTMLElement): void {
        this.canvasResizeAdjustments.adjustChildrenIfSizeChanged(bloomCanvas);
    }

    public static adjustCanvasElementAlternates(
        canvasElement: HTMLElement,
        scale: number,
        oldLeft: number,
        oldTop: number,
        newLeft: number,
        newTop: number,
    ) {
        adjustCanvasElementAlternatesFromAlternates(
            canvasElement,
            scale,
            oldLeft,
            oldTop,
            newLeft,
            newTop,
        );
    }

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
        oldRange: number,
    ): string {
        return adjustCenterOfTextBoxFromAlternates(
            label,
            style,
            scale,
            oldC,
            newC,
            oldRange,
        );
    }

    // Typical source is something like "left: 224px; top: 79.6px; width: 66px; height: 30px;"
    // We want to pass "top" and get 79.6.
    public static getLabeledNumberInPx(label: string, source: string): number {
        return getLabeledNumberInPxFromAlternates(label, source);
    }
}

// Note: do NOT use this directly in toolbox code; it will import its own copy of
// CanvasElementManager and not use the proper one from the page iframe. Instead, use
// the CanvasElementUtils.getCanvasElementManager().
export let theOneCanvasElementManager: CanvasElementManager;

export function initializeCanvasElementManager() {
    if (theOneCanvasElementManager) return;
    theOneCanvasElementManager = new CanvasElementManager();
}

export {
    canvasElementDescription,
    showCanvasTool,
} from "./CanvasElementManagerPublicFunctions";

function SetupClickToShowCanvasTool(canvas: Element) {
    // if the user clicks on a canvas element, bring up the canvas tool
    $(canvas).click((_ev) => {
        // don't interfere with editing or recording of an image description of this canvas
        if (canvas.getElementsByClassName("bloom-describedImage").length > 0) {
            return;
        }

        showCanvasTool();
    });
}

// showCanvasTool moved to CanvasElementManagerPublicFunctions.ts
