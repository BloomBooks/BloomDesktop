// Element creation helpers extracted from CanvasElementManager.
//
// This module owns the logic for creating new canvas elements (from toolbox drop,
// duplication, and child creation). Keeping it separate helps reduce the size and
// coupling of CanvasElementManager.

/// <reference path="../collectionSettings.d.ts" />

import { Bubble, BubbleSpec, Comical } from "comicaljs";
import { Point, PointScaling } from "../point";
import {
    kBackgroundImageClass,
    kCanvasElementClass,
    kBloomButtonClass,
} from "../../toolbox/canvas/canvasElementConstants";
import { CanvasElementType } from "../../toolbox/canvas/canvasElementTypes";
import { kDraggableIdAttribute } from "../../toolbox/canvas/canvasElementDraggables";
import { changeImageInfo } from "../bloomEditing";
import { addSkeletonIfEmpty } from "../linkGrid";
import { kImageContainerClass, kImageContainerSelector } from "../bloomImages";
import { getExactClientSize } from "../../../utils/elementUtils";
import { CanvasSnapProvider } from "../CanvasSnapProvider";
import { kVideoContainerClass } from "../videoUtils";
import { adjustTarget as adjustTargetFromGameTool } from "../../toolbox/games/GameTool";
import { putBubbleBefore } from "./CanvasElementBubbleLevelUtils";
import { setCanvasElementPosition } from "./CanvasElementPositioning";
import $ from "jquery";

export interface ITextColorInfo {
    color: string;
    isDefault: boolean;
}

export interface IFinishAddingCanvasElementOptions {
    comicalBubbleStyle?: string;
    setElementActive?: boolean;
    rightTopOffset?: string;
    imageInfo?: {
        imageId: string;
        src: string; // must already appropriately URL-encoded.
        copyright: string;
        creator: string;
        license: string;
    };
    size?: { width: number; height: number };
    doAfterElementCreated?: (newElement: HTMLElement) => void;
    limitToCanvasBounds?: boolean;
}

export interface ICanvasElementFactoriesHost {
    snapProvider: CanvasSnapProvider;

    getBloomCanvasFromMouse: (mouseX: number, mouseY: number) => JQuery;

    getActiveElement: () => HTMLElement | undefined;
    setActiveElementDirect: (canvasElement: HTMLElement | undefined) => void;

    doNotifyChange: () => void;
    showCorrespondingTextBox: (canvasElement: HTMLElement) => void;
    handleResizeAdjustments: () => void;

    refreshCanvasElementEditing: (
        bloomCanvas: HTMLElement,
        bubble: Bubble | undefined,
        attachEventsToEditables: boolean,
        activateCanvasElement: boolean,
    ) => void;

    setActiveElement: (canvasElement: HTMLElement | undefined) => void;
    getTextColorInformation: () => ITextColorInfo;
    setTextColorInternal: (color: string, element: HTMLElement) => void;
}

export class CanvasElementFactories {
    private host: ICanvasElementFactoriesHost;

    public constructor(host: ICanvasElementFactoriesHost) {
        this.host = host;
    }

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
        // The only reason to keep a separate method here is that the 'internal' form returns
        // the new child. We don't need it here, but we do in the duplicate canvas element function.
        this.addChildCanvasElement(parentElement, offsetX, offsetY);
    }

    // Used by duplication logic to create a child and then customize its content.
    public addChildCanvasElement(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number,
    ): HTMLElement | undefined {
        return this.addChildInternal(parentElement, offsetX, offsetY);
    }
    public addCanvasElementWithScreenCoords(
        screenX: number,
        screenY: number,
        canvasElementType: CanvasElementType,
        userDefinedStyleName?: string,
        rightTopOffset?: string,
    ): HTMLElement | undefined {
        const topWindow = window.top ?? window;
        type TopWindowWithCanvasDrop = Window & {
            __bloomCanvasLastDrop?: {
                clientX: number;
                clientY: number;
                time: number;
            };
        };
        const lastDrop = (topWindow as TopWindowWithCanvasDrop)
            .__bloomCanvasLastDrop;
        if (lastDrop && Date.now() - lastDrop.time < 1000) {
            return this.addCanvasElement(
                lastDrop.clientX,
                lastDrop.clientY,
                canvasElementType,
                userDefinedStyleName,
                rightTopOffset,
            );
        }

        // This method is typically called from the toolbox iframe's dragend handler.
        // In that case we don't have access to the page iframe's clientX/clientY, only screenX/screenY.
        // Convert to coordinates that are meaningful in THIS document (the editable page iframe):
        // - First convert to the top-level window's client coordinates
        // - Then subtract this iframe's offset within the top-level document
        // This avoids large offsets when the page iframe is not at (0,0) within the host UI.
        // Different hosts/browsers disagree on what DragEvent.screenX/screenY represent.
        // - In WebView2 they are typically true screen coordinates.
        // - In some browser automation they can behave like top-window client coordinates.
        // We try both interpretations and pick the one that maps into this iframe.
        const frameRect = (
            window.frameElement as HTMLElement | null
        )?.getBoundingClientRect?.();

        const pickTopClientCoord = (
            screenCoord: number,
            topScreenCoord: number,
            frameOffset: number,
            frameSize: number,
        ): number => {
            const dpr = topWindow.devicePixelRatio || 1;
            const candidates = [
                screenCoord - topScreenCoord,
                screenCoord,
                (screenCoord - topScreenCoord) / dpr,
                screenCoord / dpr,
            ];
            if (!frameRect) {
                // Prefer the traditional conversion, but fall back if it is clearly not usable.
                const primary = candidates[0];
                return primary < 0 || primary > frameSize * 10
                    ? candidates[1]
                    : primary;
            }

            for (const c of candidates) {
                const inFrame = c - frameOffset;
                if (inFrame >= -5 && inFrame <= frameSize + 5) {
                    return c;
                }
            }
            return candidates[0];
        };

        const topClientX = frameRect
            ? pickTopClientCoord(
                  screenX,
                  topWindow.screenX,
                  frameRect.left,
                  frameRect.width,
              )
            : pickTopClientCoord(
                  screenX,
                  topWindow.screenX,
                  0,
                  window.innerWidth,
              );

        const topClientY = frameRect
            ? pickTopClientCoord(
                  screenY,
                  topWindow.screenY,
                  frameRect.top,
                  frameRect.height,
              )
            : pickTopClientCoord(
                  screenY,
                  topWindow.screenY,
                  0,
                  window.innerHeight,
              );

        const clientX = frameRect ? topClientX - frameRect.left : topClientX;
        const clientY = frameRect ? topClientY - frameRect.top : topClientY;
        return this.addCanvasElement(
            clientX,
            clientY,
            canvasElementType,
            userDefinedStyleName,
            rightTopOffset,
        );
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
        const bloomCanvas = this.host.getBloomCanvasFromMouse(mouseX, mouseY);
        if (!bloomCanvas || bloomCanvas.length === 0) {
            // Don't add a canvas element if we can't find the containing bloom-canvas.
            return undefined;
        }

        // mouseX/mouseY are viewport coordinates (e.g. from clientX/clientY).
        // Most of our placement logic expects a point relative to the bloom-canvas itself,
        // so convert before clamping/snapping.
        const bloomCanvasRect = bloomCanvas[0].getBoundingClientRect();
        const positionInBloomCanvasViewport = new Point(
            mouseX - bloomCanvasRect.left,
            mouseY - bloomCanvasRect.top,
            PointScaling.Scaled,
            "Scaled viewport coordinates relative to bloom-canvas",
        );
        const positionInBloomCanvas = this.adjustRelativePointToBloomCanvas(
            bloomCanvas[0],
            positionInBloomCanvasViewport,
        );

        if (canvasElementType === "video") {
            return this.addVideoCanvasElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }
        if (canvasElementType === "image") {
            return this.addPictureCanvasElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }
        if (canvasElementType === "sound") {
            return this.addSoundCanvasElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }
        if (canvasElementType === "rectangle") {
            return this.addRectangleCanvasElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }
        if (canvasElementType === "book-link-grid") {
            return this.addBookLinkGridCanvasElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }
        if (canvasElementType === "navigation-image-button") {
            return this.addNavigationImageButtonElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }
        if (canvasElementType === "navigation-label-button") {
            return this.addNavigationLabelButtonElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }
        if (canvasElementType === "navigation-image-with-label-button") {
            return this.addNavigationImageWithLabelButtonElement(
                positionInBloomCanvas,
                bloomCanvas,
                rightTopOffset,
            );
        }

        return this.addCanvasElementCore(
            positionInBloomCanvas,
            bloomCanvas,
            canvasElementType,
            userDefinedStyleName,
            rightTopOffset,
        );
    }

    public setDefaultHeightFromWidth(canvasElement: HTMLElement): void {
        // All of the text-based canvas elements' default heights are based on the min-height of 30px set
        // in canvasTool.less for a .bloom-canvas-element. For other elements, we usually want something else.
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

    // =========================================================================================
    // Private helpers
    // =========================================================================================

    private addChildInternal(
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number,
    ): HTMLElement | undefined {
        this.updateComicalForSelectedElement(parentElement);

        const newPoint = this.findBestLocationForNewCanvasElement(
            parentElement,
            offsetX,
            offsetY,
        );
        if (!newPoint) {
            return undefined;
        }

        const childElement = this.addCanvasElement(
            newPoint.getScaledX(),
            newPoint.getScaledY(),
            undefined,
        );
        if (!childElement) {
            return undefined;
        }

        // Make sure that the child inherits any non-default text color from the parent canvas element
        // (which must be the active element).
        this.host.setActiveElement(parentElement);
        const parentTextColor = this.host.getTextColorInformation();
        if (!parentTextColor.isDefault) {
            this.host.setTextColorInternal(parentTextColor.color, childElement);
        }

        Comical.initializeChild(childElement, parentElement);
        // In this case, the 'addCanvasElement()' above will already have done the new canvas element's
        // refresh. We still want to refresh, but not attach to ckeditor, etc., so we pass
        // attachEventsToEditables as false.
        const bloomCanvas = $(parentElement).closest(".bloom-canvas").get(0);
        if (bloomCanvas) {
            this.host.refreshCanvasElementEditing(
                bloomCanvas,
                new Bubble(childElement),
                false,
                true,
            );
        }
        return childElement;
    }

    // Make sure comical is up-to-date in the case where we know there is a selected/current element.
    private updateComicalForSelectedElement(element: HTMLElement): void {
        if (!element) {
            return;
        }
        const bloomCanvas = $(element).closest(".bloom-canvas").get(0);
        if (!bloomCanvas) {
            return; // shouldn't happen...
        }
        const comicalGenerated =
            bloomCanvas.getElementsByClassName("comical-generated");
        if (comicalGenerated.length > 0) {
            Comical.update(bloomCanvas);
        }
    }

    // The 'new canvas element' is either going to be a child of the 'parentElement', or a duplicate of it.
    public addCanvasElementFromOriginal(
        offsetX: number,
        offsetY: number,
        originalElement: HTMLElement,
        style?: string,
    ): HTMLElement | undefined {
        const bloomCanvas = $(originalElement).closest(".bloom-canvas").get(0);
        if (!bloomCanvas) {
            return undefined;
        }
        const positionInViewport = new Point(
            offsetX,
            offsetY,
            PointScaling.Scaled,
            "Scaled Viewport coordinates",
        );
        const positionInBloomCanvas = this.host.snapProvider.getSnappedPoint(
            this.adjustRelativePointToBloomCanvas(
                bloomCanvas,
                positionInViewport,
            ),
            // There's no obvious event from which to deduce that ctrl is down, and I don't see any
            // advantage in supporting the slightly different position that the duplicate would
            // end up in if we knew that.
            undefined,
        );
        // Detect if the original is a picture over picture or video over picture element.
        if (this.isPictureCanvasElement(originalElement)) {
            return this.addPictureCanvasElement(
                positionInBloomCanvas,
                $(bloomCanvas),
            );
        }
        if (this.isVideoCanvasElement(originalElement)) {
            return this.addVideoCanvasElement(
                positionInBloomCanvas,
                $(bloomCanvas),
            );
        }
        return this.addCanvasElementCore(
            positionInBloomCanvas,
            $(bloomCanvas),
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

    public findBestLocationForNewCanvasElement(
        parentElement: HTMLElement,
        proposedOffsetX: number,
        proposedOffsetY: number,
    ): Point | undefined {
        const parentBoundingRect = parentElement.getBoundingClientRect();

        // // Ensure newX and newY is within the bounds of the container.
        const bloomCanvas = $(parentElement).closest(".bloom-canvas").get(0);
        if (!bloomCanvas) {
            return undefined;
        }
        return this.adjustRectToBloomCanvas(
            bloomCanvas,
            parentBoundingRect.left + proposedOffsetX,
            parentBoundingRect.top + proposedOffsetY,
            parentElement.clientWidth,
            parentElement.clientHeight,
        );
    }

    private adjustRectToBloomCanvas(
        bloomCanvas: Element,
        x: number,
        y: number,
        width: number,
        height: number,
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
            "Scaled viewport coordinates",
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

    private addCanvasElementCore(
        location: Point,
        bloomCanvasJQuery: JQuery,
        style?: string,
        userDefinedStyleName?: string,
        rightTopOffset?: string,
        limitToCanvasBounds: boolean = false,
    ): HTMLElement {
        const transGroupHtml = this.makeTranslationGroup(userDefinedStyleName);

        return this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            transGroupHtml,
            location,
            {
                comicalBubbleStyle: style,
                rightTopOffset,
                limitToCanvasBounds,
            },
        );
    }

    private makeTranslationGroup(
        userDefinedStyleName: string | undefined,
    ): string {
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
        return transGroupHtml;
    }

    public addVideoCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
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
            {
                comicalBubbleStyle: "none",
                setElementActive: true,
                rightTopOffset,
            },
        );
    }

    public addPictureCanvasElement(
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
            {
                comicalBubbleStyle: "none",
                setElementActive: true,
                rightTopOffset,
                imageInfo,
                size,
                doAfterElementCreated,
            },
        );
    }

    public addNavigationImageButtonElement(
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
        const imageContainerHtml = this.makeImageContainerHtml();
        const result = this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            imageContainerHtml,
            location,
            {
                comicalBubbleStyle: "none",
                setElementActive: true,
                rightTopOffset,
                imageInfo,
                size: { width: 120, height: 120 },
                doAfterElementCreated,
                limitToCanvasBounds: true,
            },
        );
        result.classList.add(kBloomButtonClass);
        return result;
    }

    public addNavigationImageWithLabelButtonElement(
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
        const imageContainerHtml = this.makeImageContainerHtml();
        const transGroupHtml = this.makeTranslationGroup("Label");
        const result = this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            imageContainerHtml + transGroupHtml,
            location,
            {
                comicalBubbleStyle: "none",
                setElementActive: true,
                rightTopOffset,
                imageInfo,
                size: { width: 120, height: 120 },
                limitToCanvasBounds: true,
            },
        );
        result.classList.add(kBloomButtonClass);
        result.classList.add("bloom-noAutoHeight");
        return result;
    }

    public addNavigationLabelButtonElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        const result = this.addCanvasElementCore(
            location,
            bloomCanvasJQuery,
            "none", // no comical bubble style
            "navigation-label-button",
            rightTopOffset,
            true,
        );
        result.classList.add(kBloomButtonClass);
        result.classList.add("bloom-noAutoHeight");
        // The methods used in the other two get to set a size; here we just do it.
        // We need to make it a bit higher than the default so it doesn't overflow
        // with the additional padding that buttons get.
        result.style.height = "50px";
        return result;
    }

    public addSoundCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
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
            fill="var(--game-draggable-bg-color, black)"
        />
        <path
            d="M23.0403 9.12744C24.8868 10.8177 25.9241 13.11 25.9241 15.5C25.9241 17.8901 24.8868 20.1823 23.0403 21.8726M19.5634 12.3092C20.4867 13.1544 21.0053 14.3005 21.0053 15.4955C21.0053 16.6906 20.4867 17.8367 19.5634 18.6818M15.0917 9.19054L10.1669 12.796H6.22705V18.2041H10.1669L15.0917 21.8095V9.19054Z"
            stroke="var(--game-draggable-color, white)"
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
            {
                comicalBubbleStyle: "none",
                setElementActive: true,
                rightTopOffset,
            },
        );
    }

    public addBookLinkGridCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        const html =
            // The tabindex here is necessary to allow it to be focused.
            "<div tabindex='0' class='bloom-link-grid'></div>";
        const canvasElement = this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            html,
            location,
            {
                comicalBubbleStyle: "none",
                setElementActive: true,
                rightTopOffset,
                size: { width: 360, height: 360 },
                limitToCanvasBounds: true,
            },
        );
        // Add skeleton to the newly created empty grid
        const linkGrid = canvasElement.querySelector(
            ".bloom-link-grid",
        ) as HTMLElement;
        if (linkGrid) {
            addSkeletonIfEmpty(linkGrid);
        }
        return canvasElement;
    }

    public addRectangleCanvasElement(
        location: Point,
        bloomCanvasJQuery: JQuery,
        rightTopOffset?: string,
    ): HTMLElement {
        const html =
            // The tabindex here is necessary to allow it to be focused.
            "<div tabindex='0' class='bloom-rectangle'></div>";
        const result = this.finishAddingCanvasElement(
            bloomCanvasJQuery,
            html,
            location,
            {
                comicalBubbleStyle: "none",
                setElementActive: true,
                rightTopOffset,
            },
        );
        // Keep z-ordering as before by moving rectangles behind other overlays.
        this.reorderRectangleCanvasElement(result, bloomCanvasJQuery.get(0));
        return result;
    }

    private makeImageContainerHtml(): string {
        const standardImageClasses =
            kImageContainerClass + " bloom-leadingElement";
        const imagePlaceHolderHtml = "<img src='placeHolder.png' alt=''></img>";
        const imageContainerHtml =
            // The tabindex here is necessary to get focus to work on an image.
            `<div tabindex='0' class = '${standardImageClasses}'> ${imagePlaceHolderHtml}</div>`;
        return imageContainerHtml;
    }

    public reorderRectangleCanvasElement(
        rectangle: HTMLElement,
        bloomCanvas: HTMLElement,
    ): void {
        const backgroundImage = bloomCanvas.getElementsByClassName(
            kBackgroundImageClass,
        )[0] as HTMLElement;
        if (backgroundImage) {
            bloomCanvas.insertBefore(rectangle, backgroundImage.nextSibling);
            // Being first in document order gives it the right z-order, but it also has to be
            // in the right sequence by ComicalJs Bubble level for the hit test to work right.
            putBubbleBefore(
                rectangle,
                (
                    Array.from(
                        bloomCanvas.getElementsByClassName(kCanvasElementClass),
                    ) as HTMLElement[]
                ).filter((x) => x !== backgroundImage),
                Bubble.getBubbleSpec(backgroundImage).level + 1,
            );
        }
    }

    // Note: This is distinct from ensureCanvasElementsIntersectParent(), which is intended to
    // keep *existing* canvas elements at least partly visible (and also keeps tails inside).
    // Here we try to keep a *newly created* element entirely within the canvas (if possible),
    // without changing its size and without moving it above/left of the canvas.
    private ensureCanvasElementInsideCanvasIfPossible(
        canvasElement: HTMLElement,
        bloomCanvas: HTMLElement,
    ): void {
        const canvasSize = getExactClientSize(bloomCanvas);
        const canvasElementSize = getExactClientSize(canvasElement);
        const currentCanvasElementLeft = this.pxToNumber(
            canvasElement.style.left,
        );
        const currentCanvasElementTop = this.pxToNumber(
            canvasElement.style.top,
        );
        const currentCanvasElementWidth = canvasElementSize.width;
        const currentCanvasElementHeight = canvasElementSize.height;

        const maxLeft = canvasSize.width - currentCanvasElementWidth;
        const maxTop = canvasSize.height - currentCanvasElementHeight;
        const clampedLeft = Math.max(
            0,
            Math.min(currentCanvasElementLeft, maxLeft),
        );
        const clampedTop = Math.max(
            0,
            Math.min(currentCanvasElementTop, maxTop),
        );
        if (
            clampedLeft !== currentCanvasElementLeft ||
            clampedTop !== currentCanvasElementTop
        ) {
            canvasElement.style.left = clampedLeft + "px";
            canvasElement.style.top = clampedTop + "px";
            this.adjustTarget(canvasElement);
        }
    }

    private pxToNumber(px: string, fallback: number = NaN): number {
        if (!px) {
            return fallback;
        }
        const trimmed = px.trim();
        if (trimmed.endsWith("px")) {
            const result = parseFloat(trimmed.substring(0, trimmed.length - 2));
            return isNaN(result) ? fallback : result;
        }
        const result = parseFloat(trimmed);
        return isNaN(result) ? fallback : result;
    }

    private adjustTarget(draggable: HTMLElement | undefined): void {
        if (!draggable) {
            // I think this is just to remove the arrow if any.
            adjustTargetFromGameTool(
                document.firstElementChild as HTMLElement,
                undefined,
            );
            return;
        }
        const targetId = draggable.getAttribute(kDraggableIdAttribute);
        const target = targetId
            ? document.querySelector(`[data-target-of="${targetId}"]`)
            : undefined;
        adjustTargetFromGameTool(draggable, target as HTMLElement);
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
    public placeElementAtPosition(
        wrapperBox: JQuery,
        container: Element,
        positionInBloomCanvas: Point,
        rightTopOffset?: string,
    ): void {
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
            const { x, y } = this.host.snapProvider.getPosition(
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

        const elt = wrapperBox.get(0) as HTMLElement;
        setCanvasElementPosition(elt, xOffset, yOffset);
        this.adjustTarget(elt);
    }

    private finishAddingCanvasElement(
        bloomCanvasJQuery: JQuery,
        internalHtml: string,
        location: Point,
        options?: IFinishAddingCanvasElementOptions,
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
            lastChildOfBloomCanvas,
        );
        const canvasElement = canvasElementJQuery.get(0);
        if (options?.imageInfo) {
            const img = canvasElement.getElementsByTagName("img")[0];
            if (img) {
                changeImageInfo(img, options.imageInfo);
            }
        }
        if (options?.size) {
            canvasElement.style.width = options.size.width + "px";
            canvasElement.style.height = options.size.height + "px";
        } else {
            this.setDefaultHeightFromWidth(canvasElement);
        }
        this.placeElementAtPosition(
            canvasElementJQuery,
            bloomCanvasJQuery.get(0),
            location,
            options?.rightTopOffset,
        );

        if (options?.limitToCanvasBounds) {
            const bloomCanvas = bloomCanvasJQuery.get(0) as HTMLElement;
            this.ensureCanvasElementInsideCanvasIfPossible(
                canvasElement,
                bloomCanvas,
            );
        }

        // The following code would not be needed for Picture and Video canvas elements if the focusin
        // handler were reliably called after being attached by refreshBubbleEditing() below.
        // However, calling the jquery.focus() method in bloomEditing.focusOnChildIfFound()
        // causes the handler to fire ONLY for Text canvas elements.  This is a complete mystery to me.
        // Therefore, for Picture and Video canvas elements, we set the content active and notify the
        // canvas element tool. But we don't need/want the actions of setActiveElement() which overlap
        // with refreshBubbleEditing(). This code actually prevents bloomEditing.focusOnChildIfFound()
        // from being called, but that doesn't really matter since calling it does no good.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-11620.
        if (options?.setElementActive) {
            this.host.setActiveElementDirect(canvasElement);
            this.host.doNotifyChange();
            this.host.showCorrespondingTextBox(canvasElement);
        }
        const bubble = new Bubble(canvasElement);
        const bubbleSpec: BubbleSpec = Bubble.getDefaultBubbleSpec(
            canvasElement,
            options?.comicalBubbleStyle || "speech",
        );
        bubble.setBubbleSpec(bubbleSpec);
        const bloomCanvas = bloomCanvasJQuery.get(0);
        if (options?.doAfterElementCreated) {
            // It's not obvious when the best time to do this is. Obviously it has to be after
            // the element is created. For the current purpose, the main thing is that it be
            // before refreshBubbleEditing() is called, since (for picture elements) that is
            // what gets the element selected and triggers a call to adjustContainerAspectRatio().
            options.doAfterElementCreated(canvasElement);
        }
        // background image in parent bloom-canvas may need to become canvas element
        // (before we refreshBubbleEditing, since we may change some canvas elements here.)
        this.host.handleResizeAdjustments();
        this.host.refreshCanvasElementEditing(bloomCanvas, bubble, true, true);
        const editable = canvasElement.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on",
        )[0] as HTMLElement;
        editable?.focus();
        return canvasElement;
    }
}
