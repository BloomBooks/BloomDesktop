import {
    getImageFromCanvasElement,
    kImageContainerClass,
} from "../bloomImages";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
    kBloomCanvasSelector,
    kCanvasElementSelector,
} from "../../toolbox/canvas/canvasElementConstants";
import { renderCanvasElementContextControls } from "./CanvasElementContextControls";
import { CanvasGuideProvider } from "./CanvasGuideProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";
import { getHandleTitlesAsync } from "./CanvasElementSelectionUi";
import {
    getCanvasElementRotation,
    rotateVector,
    setCanvasElementRotation,
    unrotateVector,
} from "./canvasElementRotation";
import { pushUndoForCanvasElementRotation } from "../ImageUndoManager";
import { getImageContentTransform } from "../imageContentTransform";

export interface ICanvasElementHandleDragInteractionsHost {
    getActiveElement: () => HTMLElement | undefined;

    getMinWidth: () => number;
    getMinHeight: () => number;

    adjustTarget: (canvasElement: HTMLElement) => void;
    alignControlFrameWithActiveElement: () => void;
    adjustBackgroundImageSize: (
        bloomCanvas: HTMLElement,
        bgCanvasElement: HTMLElement,
        useSizeOfNewImage: boolean,
    ) => void;

    adjustCanvasElementHeightToContentOrMarkOverflow: (
        editable: HTMLElement,
    ) => void;

    adjustStuffRelatedToImage: (
        activeElement: HTMLElement,
        img: HTMLImageElement | undefined,
    ) => void;

    startMoving: () => void;
    stopMoving: () => void;
}

// Where a picture lands inside the canvas element that holds it, in the element's own
// coordinates.
//
// The picture's own box may be turned inside the element, by the Rotate Right command on a page
// background. A CSS transform turns the box about its own centre and leaves the layout alone, so
// what the element shows is a rectangle the size of the box with its two dimensions swapped for
// an odd number of quarter turns, centred where the layout put the box's centre. Every crop
// measurement has to use this rectangle. The box itself, which the picture's `left`, `top` and
// `width` describe, is the right answer only for a picture that is not turned.
export function getShownContentRectangle(
    boxLeft: number,
    boxTop: number,
    boxWidth: number,
    boxHeight: number,
    quarterTurns: number,
): { left: number; top: number; width: number; height: number } {
    const isQuarterTurn = quarterTurns % 2 === 1;
    const width = isQuarterTurn ? boxHeight : boxWidth;
    const height = isQuarterTurn ? boxWidth : boxHeight;
    return {
        left: boxLeft + boxWidth / 2 - width / 2,
        top: boxTop + boxHeight / 2 - height / 2,
        width,
        height,
    };
}

// Which sides of a picture are cropped, so that the marks go on the right handles.
export function getCroppedSides(
    elementWidth: number,
    elementHeight: number,
    boxLeft: number,
    boxTop: number,
    boxWidth: number,
    boxHeight: number,
    quarterTurns: number,
    // Client values are whole pixels, and rounding easily produces a spurious difference of one.
    slop = 1,
): { n: boolean; e: boolean; s: boolean; w: boolean } {
    const shown = getShownContentRectangle(
        boxLeft,
        boxTop,
        boxWidth,
        boxHeight,
        quarterTurns,
    );
    return {
        n: shown.top < -slop,
        e: shown.left + shown.width > elementWidth + slop,
        s: shown.top + shown.height > elementHeight + slop,
        w: shown.left < -slop,
    };
}

// Where to put a picture's box so that the picture keeps covering its canvas element while the
// author drags it about inside the crop. The wanted position is what the pointer asks for; the
// result is that position pulled back far enough that no blank band appears at an edge.
//
// The box is what we write, but the element shows the turned rectangle, so the limits belong to
// that rectangle. On a picture whose two dimensions differ, and which is turned, the two frames
// disagree by half the difference, which is enough to hold the picture still through a whole
// drag.
export function clampCropPosition(
    elementWidth: number,
    elementHeight: number,
    wantedBoxLeft: number,
    wantedBoxTop: number,
    boxWidth: number,
    boxHeight: number,
    quarterTurns: number,
): { left: number; top: number } {
    const shown = getShownContentRectangle(
        wantedBoxLeft,
        wantedBoxTop,
        boxWidth,
        boxHeight,
        quarterTurns,
    );
    const clampedShownLeft = Math.max(
        Math.min(shown.left, 0),
        elementWidth - shown.width,
    );
    const clampedShownTop = Math.max(
        Math.min(shown.top, 0),
        elementHeight - shown.height,
    );
    // The turn moves the box and the rectangle it shows by the same amount, so one difference
    // carries the clamp back into the box's own frame.
    return {
        left: wantedBoxLeft + clampedShownLeft - shown.left,
        top: wantedBoxTop + clampedShownTop - shown.top,
    };
}

export class CanvasElementHandleDragInteractions {
    private host: ICanvasElementHandleDragInteractionsHost;
    private snapProvider: CanvasSnapProvider;
    private guideProvider: CanvasGuideProvider;

    // clientX/Y of the mouseDown event in one of the resize handles.
    private startResizeDragX: number;
    private startResizeDragY: number;
    // the original size and position (at mouseDown) during a resize or crop
    private oldWidth: number;
    private oldHeight: number;
    private oldLeft: number;
    private oldTop: number;
    // The original size and position of the main img inside a canvas element being resized or cropped
    private oldImageWidth: number;
    private oldImageHeight: number;
    private oldImageLeft: number;
    private oldImageTop: number;
    // during a resize drag, keeps track of which corner we're dragging
    private resizeDragCorner: "ne" | "nw" | "se" | "sw" | undefined;

    private startMoveCropX: number;
    private startMoveCropY: number;
    private startMoveCropControlX: number;
    private startMoveCropControlY: number;

    private startSideDragX: number;
    private startSideDragY: number;

    private lastCropControl: HTMLElement | undefined;
    private initialCropImageWidth: number;
    private initialCropImageHeight: number;
    private initialCropImageLeft: number;
    private initialCropImageTop: number;
    private initialCropCanvasElementWidth: number;
    private initialCropCanvasElementHeight: number;
    private initialCropCanvasElementTop: number;
    private initialCropCanvasElementLeft: number;
    private cropSnapDisabled: boolean = false;

    private currentDragSide: string | undefined;
    private currentDragControl: HTMLElement | undefined;

    // The centre of the element being rotated, in viewport coordinates, and the two angles
    // we measure the drag against.
    private rotateCenterX: number;
    private rotateCenterY: number;
    private startRotatePointerAngle: number;
    private startRotation: number;

    public constructor(
        host: ICanvasElementHandleDragInteractionsHost,
        snapProvider: CanvasSnapProvider,
        guideProvider: CanvasGuideProvider,
    ) {
        this.host = host;
        this.snapProvider = snapProvider;
        this.guideProvider = guideProvider;
    }

    public resetCropBasis(): void {
        this.lastCropControl = undefined;
    }

    // Convert a movement of the mouse, which is measured on the screen, into the element's
    // own un-rotated coordinates. All the resize and crop arithmetic below works in those
    // coordinates, so without this a rotated element grows along the wrong axis.
    private getUnrotatedDelta(
        activeElement: HTMLElement,
        screenDeltaX: number,
        screenDeltaY: number,
    ): { x: number; y: number } {
        return unrotateVector(
            screenDeltaX,
            screenDeltaY,
            getCanvasElementRotation(activeElement),
        );
    }

    // A rotated element turns about its own centre, so changing the size of the box moves
    // what the user sees at both ends of it: the edge or corner opposite the one being
    // dragged drifts away. This shifts the element to put that opposite edge back where it
    // was on screen.
    //
    // Writing it as a correction applied after the ordinary arithmetic keeps the rotation out
    // of that arithmetic.
    //
    // The caller passes the position the ordinary arithmetic wants, rather than letting this
    // method read the position out of the element. That matters because the correction writes
    // both left and top, while a drag of one edge recomputes only one of them from the
    // mouse-down values: the other still holds the correction of the previous mouse move.
    // Reading it back therefore fed each correction into the next one, and the element crept
    // across the page for as long as the drag went on (BL-16741).
    private compensateForRotationDuringResize(
        activeElement: HTMLElement,
        newLeft: number,
        newTop: number,
    ): void {
        const rotation = getCanvasElementRotation(activeElement);
        if (rotation === 0) {
            return;
        }
        // How far the centre of the box has moved, in un-rotated coordinates.
        const centerShiftX =
            newLeft +
            activeElement.clientWidth / 2 -
            (this.oldLeft + this.oldWidth / 2);
        const centerShiftY =
            newTop +
            activeElement.clientHeight / 2 -
            (this.oldTop + this.oldHeight / 2);
        // On screen that shift appears turned by the element's angle. The difference between
        // where it appears and where the arithmetic put it is what we must undo.
        const onScreen = rotateVector(centerShiftX, centerShiftY, rotation);
        activeElement.style.left = `${newLeft + onScreen.x - centerShiftX}px`;
        activeElement.style.top = `${newTop + onScreen.y - centerShiftY}px`;
    }

    // Start dragging the rotate handle (the "lollipop" above the top of the control frame).
    // We turn the element by however far the pointer travels around its centre, so the point
    // the user grabbed stays under the pointer.
    public startRotateDrag = (event: MouseEvent) => {
        event.preventDefault();
        event.stopPropagation();
        const activeElement = this.host.getActiveElement();
        if (!activeElement) return;
        this.currentDragControl = event.currentTarget as HTMLElement;
        this.currentDragControl.classList.add("active-control");
        // Turning about the centre leaves the centre of the bounding rectangle where it was,
        // so this is the centre of rotation whether or not the element is already turned.
        const bounds = activeElement.getBoundingClientRect();
        this.rotateCenterX = bounds.left + bounds.width / 2;
        this.rotateCenterY = bounds.top + bounds.height / 2;
        this.startRotation = getCanvasElementRotation(activeElement);
        this.startRotatePointerAngle = this.getPointerAngle(event);
        document.addEventListener("mousemove", this.continueRotateDrag, {
            capture: true,
        });
        document.addEventListener("mouseup", this.endRotateDrag, {
            capture: true,
        });
        this.host.startMoving();
    };

    // The angle from the centre of the element to the pointer, in degrees.
    private getPointerAngle(event: MouseEvent): number {
        return (
            (Math.atan2(
                event.clientY - this.rotateCenterY,
                event.clientX - this.rotateCenterX,
            ) *
                180) /
            Math.PI
        );
    }

    private continueRotateDrag = (event: MouseEvent) => {
        const activeElement = this.host.getActiveElement();
        if (event.buttons !== 1 || !activeElement) {
            this.endRotateDrag();
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        const pointerTravel =
            this.getPointerAngle(event) - this.startRotatePointerAngle;
        // Snapping puts the common angles within easy reach; holding CTRL turns it off.
        const newRotation = this.snapProvider.getSnappedRotation(
            this.startRotation + pointerTravel,
            event,
        );
        setCanvasElementRotation(activeElement, newRotation);
        this.host.alignControlFrameWithActiveElement();
    };

    private endRotateDrag = () => {
        document.removeEventListener("mousemove", this.continueRotateDrag, {
            capture: true,
        });
        document.removeEventListener("mouseup", this.endRotateDrag, {
            capture: true,
        });
        this.currentDragControl?.classList.remove("active-control");
        this.host.stopMoving();
        const activeElement = this.host.getActiveElement();
        if (activeElement) {
            // One record for the whole drag, and none at all if the element came back to the
            // angle it started at, so that Undo always has something to put back.
            if (
                getCanvasElementRotation(activeElement) !== this.startRotation
            ) {
                pushUndoForCanvasElementRotation(
                    activeElement,
                    this.startRotation,
                );
            }
            this.host.adjustTarget(activeElement);
        }
        this.host.alignControlFrameWithActiveElement();
    };

    public startMoveCrop = (event: MouseEvent) => {
        event.preventDefault();
        event.stopPropagation();
        const activeElement = this.host.getActiveElement();
        if (!activeElement) return;
        this.currentDragControl = event.currentTarget as HTMLElement;
        this.currentDragControl.classList.add("active");
        this.startMoveCropX = event.clientX;
        this.startMoveCropY = event.clientY;
        const imgC =
            activeElement.getElementsByClassName(kImageContainerClass)[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (!img) return;
        this.oldImageTop = img.offsetTop;
        this.oldImageLeft = img.offsetLeft;
        this.lastCropControl = undefined;
        this.startMoveCropControlX = this.currentDragControl.offsetLeft;
        this.startMoveCropControlY = this.currentDragControl.offsetTop;

        document.addEventListener("mousemove", this.continueMoveCrop, {
            capture: true,
        });
        document.addEventListener("mouseup", this.endMoveCrop, {
            capture: true,
        });
        this.host.startMoving();
    };

    private endMoveCrop = (_event: MouseEvent) => {
        const activeElement = this.host.getActiveElement();
        document.removeEventListener("mousemove", this.continueMoveCrop, {
            capture: true,
        });
        document.removeEventListener("mouseup", this.endMoveCrop, {
            capture: true,
        });
        this.currentDragControl?.classList.remove("active");
        this.currentDragControl!.style.left = "";
        this.currentDragControl!.style.top = "";
        this.host.stopMoving();
        if (activeElement?.classList.contains(kBackgroundImageClass)) {
            // currently we only need to do this because the command to expand to fill
            // the container might have become enabled.
            renderCanvasElementContextControls(activeElement, false);
        }
    };

    private continueMoveCrop = (event: MouseEvent) => {
        const activeElement = this.host.getActiveElement();
        if (event.buttons !== 1 || !activeElement) {
            return;
        }
        const { x: deltaX, y: deltaY } = this.getUnrotatedDelta(
            activeElement,
            event.clientX - this.startMoveCropX,
            event.clientY - this.startMoveCropY,
        );
        const imgC =
            activeElement.getElementsByClassName(kImageContainerClass)[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (!img) return;
        event.preventDefault();
        event.stopPropagation();
        const imgStyle = img.style;
        const { left: newLeft, top: newTop } = clampCropPosition(
            activeElement.clientWidth,
            activeElement.clientHeight,
            this.oldImageLeft + deltaX,
            this.oldImageTop + deltaY,
            img.clientWidth,
            img.clientHeight,
            getImageContentTransform(img).quarterTurns,
        );
        imgStyle.left = newLeft + "px";
        imgStyle.top = newTop + "px";
        this.currentDragControl!.style.left =
            this.startMoveCropControlX + newLeft - this.oldImageLeft + "px";
        this.currentDragControl!.style.top =
            this.startMoveCropControlY + newTop - this.oldImageTop + "px";

        this.host.adjustStuffRelatedToImage(activeElement, img);
    };

    public startResizeDrag = (
        event: MouseEvent,
        corner: "ne" | "nw" | "se" | "sw",
    ) => {
        event.preventDefault();
        event.stopPropagation();
        const activeElement = this.host.getActiveElement();
        if (!activeElement) return;
        this.currentDragControl = event.currentTarget as HTMLElement;
        this.currentDragControl.classList.add("active-control");
        this.startResizeDragX = event.clientX;
        this.startResizeDragY = event.clientY;
        this.resizeDragCorner = corner;
        this.oldWidth = activeElement.clientWidth;
        this.oldHeight = activeElement.clientHeight;
        this.oldTop = activeElement.offsetTop;
        this.oldLeft = activeElement.offsetLeft;
        const imgOrVideo = this.getImageOrVideo(activeElement);
        if (imgOrVideo && imgOrVideo.style.width) {
            this.oldImageWidth = imgOrVideo.clientWidth;
            this.oldImageTop = imgOrVideo.offsetTop;
            this.oldImageLeft = imgOrVideo.offsetLeft;
        }
        this.guideProvider.startDrag(
            "resize",
            Array.from(
                document.querySelectorAll(kCanvasElementSelector),
            ) as HTMLElement[],
        );
        document.addEventListener("mousemove", this.continueResizeDrag, {
            capture: true,
        });
        document.addEventListener("mouseup", this.endResizeDrag, {
            capture: true,
        });
    };

    private endResizeDrag = (_event: MouseEvent) => {
        const activeElement = this.host.getActiveElement();
        document.removeEventListener("mousemove", this.continueResizeDrag, {
            capture: true,
        });
        document.removeEventListener("mouseup", this.endResizeDrag, {
            capture: true,
        });
        this.currentDragControl?.classList.remove("active-control");
        this.guideProvider.endDrag();
        this.snapProvider.endDrag();
        // If this is a button, notify the overflow checker to recheck overflow.
        if (activeElement?.classList.contains(kBloomButtonClass)) {
            activeElement.dispatchEvent(
                new Event("buttonCanvasElementResized", {
                    bubbles: true,
                    cancelable: false,
                }),
            );
        }
    };

    private getImageOrVideo(
        activeElement: HTMLElement,
    ): HTMLElement | undefined {
        const imgC =
            activeElement.getElementsByClassName(kImageContainerClass)[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (img) return img;
        const videoC = activeElement.getElementsByClassName(
            "bloom-videoContainer",
        )[0];
        const video = videoC?.getElementsByTagName("video")[0];
        return video;
    }

    private continueResizeDrag = (event: MouseEvent) => {
        // Resize flow:
        // 1) compute dragged corner target from current mouse delta,
        // 2) snap that target in canvas coordinates,
        // 3) clamp to min width/height and adjust anchored edges,
        // 4) preserve media aspect ratio where required,
        // 5) scale crop offsets (if present) and refresh guide/state UI.
        const activeElement = this.host.getActiveElement();
        if (event.buttons !== 1 || !activeElement) {
            this.resizeDragCorner = undefined;
            return;
        }
        event.stopPropagation();
        event.preventDefault();
        if (event.movementX === 0 && event.movementY === 0) return;
        this.lastCropControl = undefined;

        if (!this.resizeDragCorner) return;
        const { x: deltaX, y: deltaY } = this.getUnrotatedDelta(
            activeElement,
            event.clientX - this.startResizeDragX,
            event.clientY - this.startResizeDragY,
        );
        const style = activeElement.style;
        const imgOrVideo = this.getImageOrVideo(activeElement);
        let slope = imgOrVideo ? this.oldHeight / this.oldWidth : 0;
        if (!slope && activeElement.querySelector(".bloom-svg")) slope = 1;

        let newWidth = this.oldWidth;
        let newHeight = this.oldHeight;
        let newTop = this.oldTop;
        let newLeft = this.oldLeft;

        let targetX, targetY;
        switch (this.resizeDragCorner) {
            case "ne":
                targetX = this.oldLeft + this.oldWidth + deltaX;
                targetY = this.oldTop + deltaY;
                break;
            case "nw":
                targetX = this.oldLeft + deltaX;
                targetY = this.oldTop + deltaY;
                break;
            case "se":
                targetX = this.oldLeft + this.oldWidth + deltaX;
                targetY = this.oldTop + this.oldHeight + deltaY;
                break;
            case "sw":
                targetX = this.oldLeft + deltaX;
                targetY = this.oldTop + this.oldHeight + deltaY;
                break;
            default:
                console.error("Invalid resize corner:", this.resizeDragCorner);
                return;
        }

        let { x: snappedX, y: snappedY } = this.snapProvider.getPosition(
            event,
            targetX - this.oldLeft,
            targetY - this.oldTop,
        );
        snappedX += this.oldLeft;
        snappedY += this.oldTop;

        let potentialWidth, potentialHeight;

        if (this.resizeDragCorner.includes("n")) {
            newTop = snappedY;
            potentialHeight = this.oldTop + this.oldHeight - newTop;
        } else {
            potentialHeight = snappedY - this.oldTop;
        }

        if (this.resizeDragCorner.includes("w")) {
            newLeft = snappedX;
            potentialWidth = this.oldLeft + this.oldWidth - newLeft;
        } else {
            potentialWidth = snappedX - this.oldLeft;
        }

        const minWidth = this.host.getMinWidth();
        const minHeight = this.host.getMinHeight();
        newWidth = Math.max(potentialWidth, minWidth);
        newHeight = Math.max(potentialHeight, minHeight);

        if (
            newWidth !== potentialWidth &&
            this.resizeDragCorner.includes("w")
        ) {
            newLeft = this.oldLeft + this.oldWidth - newWidth;
        }
        if (
            newHeight !== potentialHeight &&
            this.resizeDragCorner.includes("n")
        ) {
            newTop = this.oldTop + this.oldHeight - newHeight;
        }

        if (slope && !activeElement.classList.contains(kBloomButtonClass)) {
            let adjustX = newLeft;
            let adjustY = newTop;
            let originX = this.oldLeft;
            let originY = this.oldTop;
            switch (this.resizeDragCorner) {
                case "ne":
                    adjustX = newLeft + newWidth;
                    originY = this.oldTop + this.oldHeight;
                    slope = -slope;
                    break;
                case "sw":
                    adjustY = newTop + newHeight;
                    originX = this.oldLeft + this.oldWidth;
                    slope = -slope;
                    break;
                case "se":
                    adjustX = newLeft + newWidth;
                    adjustY = newTop + newHeight;
                    break;
                case "nw":
                    originX = this.oldLeft + this.oldWidth;
                    originY = this.oldTop + this.oldHeight;
                    break;
            }
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
            if (newWidth < minWidth) {
                newWidth = minWidth;
                newHeight = newWidth * slope;
            }
            if (newHeight < minHeight) {
                newHeight = minHeight;
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
        this.compensateForRotationDuringResize(activeElement, newLeft, newTop);
        if (imgOrVideo?.style.width) {
            const scale = newWidth / this.oldWidth;
            imgOrVideo.style.width = this.oldImageWidth * scale + "px";
            imgOrVideo.style.left = this.oldImageLeft * scale + "px";
            imgOrVideo.style.top = this.oldImageTop * scale + "px";
        }
        this.host.adjustStuffRelatedToImage(
            activeElement,
            imgOrVideo?.tagName === "IMG"
                ? (imgOrVideo as HTMLImageElement)
                : undefined,
        );

        this.guideProvider.duringDrag(activeElement);
    };

    public startSideControlDrag = (event: MouseEvent, side: string) => {
        const activeElement = this.host.getActiveElement();
        const img = activeElement?.getElementsByTagName("img")[0];
        const textBox = activeElement?.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on",
        )[0];
        if ((!img && !textBox) || !activeElement) {
            return;
        }
        this.startSideDragX = event.clientX;
        this.startSideDragY = event.clientY;
        this.currentDragControl = event.currentTarget as HTMLElement;
        this.currentDragControl.classList.add("active-control");
        this.currentDragSide = side;
        this.oldWidth = activeElement.clientWidth;
        this.oldHeight = activeElement.clientHeight;
        this.oldTop = activeElement.offsetTop;
        this.oldLeft = activeElement.offsetLeft;
        if (img) {
            this.oldImageLeft = img.offsetLeft;
            this.oldImageTop = img.offsetTop;
            // A side drag never writes the picture's own size, so these hold for the whole drag.
            this.oldImageWidth = img.offsetWidth;
            this.oldImageHeight = img.offsetHeight;

            if (this.lastCropControl !== event.currentTarget) {
                this.initialCropImageWidth = img.offsetWidth;
                this.initialCropImageHeight = img.offsetHeight;
                this.initialCropImageLeft = img.offsetLeft;
                this.initialCropImageTop = img.offsetTop;
                this.initialCropCanvasElementWidth = activeElement.offsetWidth;
                this.initialCropCanvasElementHeight =
                    activeElement.offsetHeight;
                this.initialCropCanvasElementTop = activeElement.offsetTop;
                this.initialCropCanvasElementLeft = activeElement.offsetLeft;
                this.lastCropControl = event.currentTarget as HTMLElement;
            }
            this.cropSnapDisabled = true;
            if (!img.style.width) {
                img.style.width = `${this.initialCropImageWidth}px`;
            }
        }
        this.guideProvider.startDrag(
            "resize",
            Array.from(
                document.querySelectorAll(kCanvasElementSelector),
            ) as HTMLElement[],
        );
        document.addEventListener("mousemove", this.continueSideDrag, {
            capture: true,
        });
        document.addEventListener("mouseup", this.stopSideDrag, {
            capture: true,
        });
        this.host.startMoving();
    };

    private stopSideDrag = () => {
        const activeElement = this.host.getActiveElement();
        this.guideProvider.endDrag();
        this.snapProvider.endDrag();
        document.removeEventListener("mousemove", this.continueSideDrag, {
            capture: true,
        });
        document.removeEventListener("mouseup", this.stopSideDrag, {
            capture: true,
        });
        this.currentDragControl?.classList.remove("active-control");
        if (activeElement?.classList.contains(kBackgroundImageClass)) {
            this.host.adjustBackgroundImageSize(
                activeElement.closest(kBloomCanvasSelector)!,
                activeElement,
                false,
            );
            this.lastCropControl = undefined;
        }
        this.host.stopMoving();
        renderCanvasElementContextControls(activeElement as HTMLElement, false);
    };

    private continueTextBoxResize(event: MouseEvent, editable: HTMLElement) {
        const activeElement = this.host.getActiveElement();
        if (!activeElement) return;
        const unrotatedDelta = this.getUnrotatedDelta(
            activeElement,
            event.clientX - this.startSideDragX,
            event.clientY - this.startSideDragY,
        );
        let deltaX = unrotatedDelta.x;
        let deltaY = unrotatedDelta.y;
        let newCanvasElementWidth = this.oldWidth;
        let newCanvasElementHeight = this.oldHeight;
        // Where this drag puts the box. Only the w handle moves it, but the rotation
        // correction needs both numbers; see compensateForRotationDuringResize.
        let newLeft = this.oldLeft;
        console.assert(
            this.currentDragSide === "e" ||
                this.currentDragSide === "w" ||
                this.currentDragSide === "s",
        );
        const minWidth = this.host.getMinWidth();
        const minHeight = this.host.getMinHeight();
        switch (this.currentDragSide) {
            case "e":
                newCanvasElementWidth = Math.max(
                    this.snapProvider.getSnappedX(
                        this.oldWidth + deltaX,
                        event,
                    ),
                    minWidth,
                );
                deltaX = newCanvasElementWidth - this.oldWidth;
                activeElement.style.width = `${newCanvasElementWidth}px`;
                break;
            case "w":
                newCanvasElementWidth = Math.max(
                    this.snapProvider.getSnappedX(
                        this.oldWidth - deltaX,
                        event,
                    ),
                    minWidth,
                );
                deltaX = this.oldWidth - newCanvasElementWidth;
                newLeft = this.oldLeft + deltaX;
                activeElement.style.width = `${newCanvasElementWidth}px`;
                activeElement.style.left = `${newLeft}px`;
                break;
            case "s":
                newCanvasElementHeight = Math.max(
                    this.snapProvider.getSnappedY(
                        this.oldHeight + deltaY,
                        event,
                    ),
                    minHeight,
                );
                deltaY = newCanvasElementHeight - this.oldHeight;
                activeElement.style.height = `${newCanvasElementHeight}px`;
        }
        this.host.adjustCanvasElementHeightToContentOrMarkOverflow(editable);
        this.compensateForRotationDuringResize(
            activeElement,
            newLeft,
            this.oldTop,
        );
        this.host.adjustTarget(activeElement);
        this.host.alignControlFrameWithActiveElement();
        this.guideProvider.duringDrag(activeElement);
    }

    private continueSideDrag = (event: MouseEvent) => {
        // Side-drag flow handles two cases:
        // - text-box resize (n/e/s/w handles adjust canvas element bounds),
        // - image crop resize (maintains crop offsets, with optional background
        //   fill snapping when Ctrl is not pressed).
        const activeElement = this.host.getActiveElement();
        if (event.buttons !== 1 || !activeElement) {
            return;
        }
        const textBox = activeElement.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on",
        )[0];
        if (textBox) {
            event.preventDefault();
            event.stopPropagation();
            this.continueTextBoxResize(event, textBox as HTMLElement);
            return;
        }
        const img = activeElement.getElementsByTagName("img")[0];
        if (!img) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        const unrotatedDelta = this.getUnrotatedDelta(
            activeElement,
            event.clientX - this.startSideDragX,
            event.clientY - this.startSideDragY,
        );
        let deltaX = unrotatedDelta.x;
        let deltaY = unrotatedDelta.y;
        if (event.movementX === 0 && event.movementY === 0) return;

        let newCanvasElementWidth = this.oldWidth;
        let newCanvasElementHeight = this.oldHeight;
        // Where this drag puts the box. Only the n and w handles move it, but the rotation
        // correction needs both numbers; see compensateForRotationDuringResize.
        let newLeft = this.oldLeft;
        let newTop = this.oldTop;
        let shouldSnapForBackground = "";
        let backgroundSnapDelta = 0;
        if (
            activeElement.classList.contains(kBackgroundImageClass) &&
            !event.ctrlKey
        ) {
            const bloomCanvas = activeElement.closest(
                kBloomCanvasSelector,
            ) as HTMLElement;
            const containerAspectRatio =
                bloomCanvas.clientWidth / bloomCanvas.clientHeight;
            const canvasElementAspectRatio = this.oldWidth / this.oldHeight;
            switch (this.currentDragSide) {
                case "n":
                    if (containerAspectRatio > canvasElementAspectRatio) {
                        backgroundSnapDelta =
                            this.oldHeight -
                            this.oldWidth / containerAspectRatio;
                        shouldSnapForBackground = "y";
                    }
                    break;
                case "w":
                    if (containerAspectRatio < canvasElementAspectRatio) {
                        backgroundSnapDelta =
                            this.oldWidth -
                            this.oldHeight * containerAspectRatio;
                        shouldSnapForBackground = "x";
                    }
                    break;
                case "s":
                    if (containerAspectRatio > canvasElementAspectRatio) {
                        backgroundSnapDelta =
                            this.oldWidth / containerAspectRatio -
                            this.oldHeight;
                        shouldSnapForBackground = "y";
                    }
                    break;
                case "e":
                    if (containerAspectRatio < canvasElementAspectRatio) {
                        backgroundSnapDelta =
                            this.oldHeight * containerAspectRatio -
                            this.oldWidth;
                        shouldSnapForBackground = "x";
                    }
                    break;
            }
        }

        const minWidth = this.host.getMinWidth();
        const minHeight = this.host.getMinHeight();

        // How far each edge can travel is a property of the rectangle the element shows, not of
        // the picture's own box, and the two differ on a turned picture. The n and w handles move
        // the picture as well as the edge, so they measure from where the picture was when this
        // drag began; the s and e handles leave the picture alone, so they measure from where it
        // was when this handle was first taken hold of.
        const quarterTurns = getImageContentTransform(img).quarterTurns;
        const shownNow = getShownContentRectangle(
            this.oldImageLeft,
            this.oldImageTop,
            this.oldImageWidth,
            this.oldImageHeight,
            quarterTurns,
        );
        const shownAtFirst = getShownContentRectangle(
            this.initialCropImageLeft,
            this.initialCropImageTop,
            this.initialCropImageWidth,
            this.initialCropImageHeight,
            quarterTurns,
        );

        switch (this.currentDragSide) {
            case "n":
                deltaY = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "y",
                    deltaY,
                    backgroundSnapDelta,
                    "n",
                );
                if (shownNow.top - deltaY > 0) {
                    deltaY = shownNow.top;
                }
                newCanvasElementHeight = Math.max(
                    this.oldHeight - deltaY,
                    minHeight,
                );
                deltaY = this.oldHeight - newCanvasElementHeight;
                newTop = this.oldTop + deltaY;
                activeElement.style.height = `${newCanvasElementHeight}px`;
                activeElement.style.top = `${newTop}px`;
                img.style.top = `${this.oldImageTop - deltaY}px`;
                break;
            case "s":
                deltaY = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "y",
                    deltaY,
                    backgroundSnapDelta,
                    "s",
                );
                if (
                    shownAtFirst.top + shownAtFirst.height <
                    this.oldHeight + deltaY
                ) {
                    deltaY =
                        shownAtFirst.top + shownAtFirst.height - this.oldHeight;
                }
                newCanvasElementHeight = Math.max(
                    this.oldHeight + deltaY,
                    minHeight,
                );
                deltaY = newCanvasElementHeight - this.oldHeight;
                activeElement.style.height = `${newCanvasElementHeight}px`;
                break;
            case "e":
                deltaX = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "x",
                    deltaX,
                    backgroundSnapDelta,
                    "e",
                );
                if (
                    shownAtFirst.left + shownAtFirst.width <
                    this.oldWidth + deltaX
                ) {
                    deltaX =
                        shownAtFirst.left + shownAtFirst.width - this.oldWidth;
                }
                newCanvasElementWidth = Math.max(
                    this.oldWidth + deltaX,
                    minWidth,
                );
                deltaX = newCanvasElementWidth - this.oldWidth;
                activeElement.style.width = `${newCanvasElementWidth}px`;
                break;
            case "w":
                deltaX = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "x",
                    deltaX,
                    backgroundSnapDelta,
                    "w",
                );
                if (shownNow.left > deltaX) {
                    deltaX = shownNow.left;
                }
                newCanvasElementWidth = Math.max(
                    this.oldWidth - deltaX,
                    minWidth,
                );
                deltaX = this.oldWidth - newCanvasElementWidth;
                newLeft = this.oldLeft + deltaX;
                activeElement.style.width = `${newCanvasElementWidth}px`;
                activeElement.style.left = `${newLeft}px`;
                img.style.left = `${this.oldImageLeft - deltaX}px`;
                break;
        }
        this.compensateForRotationDuringResize(activeElement, newLeft, newTop);
        this.host.adjustStuffRelatedToImage(activeElement, img);
        CanvasElementHandleDragInteractions.updateCurrentlyCropped(
            activeElement,
        );
    };

    private adjustDeltaForSnap(
        shouldSnap: boolean,
        delta: number,
        backgroundSnapDelta: number,
        side: string,
    ): number {
        // When the crop edge is near the exact "fill" position, snap and update
        // handle title to "Fill". Otherwise keep free crop movement and label "Crop".
        if (!shouldSnap) return delta;
        const snapDelta = 30;
        const controlFrame = document.getElementById(
            "canvas-element-control-frame",
        ) as HTMLElement;
        if (Math.abs(backgroundSnapDelta - delta) < snapDelta) {
            void getHandleTitlesAsync(
                controlFrame,
                "bloom-ui-canvas-element-side-handle-" + side,
                "Fill",
                true,
                "data-title",
            );
            return backgroundSnapDelta;
        }
        void getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-side-handle-" + side,
            "Crop",
            true,
            "data-title",
        );
        return delta;
    }

    public static updateCurrentlyCropped(activeElement: HTMLElement): void {
        const sideHandles = Array.from(
            document.getElementsByClassName(
                "bloom-ui-canvas-element-side-handle",
            ),
        );
        if (sideHandles.length === 0) return;
        const img = getImageFromCanvasElement(activeElement);
        if (!img) {
            sideHandles.forEach((handle) => {
                handle.classList.remove("bloom-currently-cropped");
            });
            return;
        }
        // Compare the laid-out positions and sizes rather than the on-screen rectangles.
        // getBoundingClientRect reports the upright box around a turned element, which is
        // larger than the element itself, so on a turned element it puts the mark on sides
        // that are not cropped. The image and the element are inside the same rotation, so
        // their own offsets, widths and heights compare directly.
        let imgLeft = 0;
        let imgTop = 0;
        for (
            let e: HTMLElement | null = img;
            e && e !== activeElement;
            e = e.offsetParent as HTMLElement | null
        ) {
            imgLeft += e.offsetLeft;
            imgTop += e.offsetTop;
        }
        const cropped = getCroppedSides(
            activeElement.clientWidth,
            activeElement.clientHeight,
            imgLeft,
            imgTop,
            img.offsetWidth,
            img.offsetHeight,
            getImageContentTransform(img).quarterTurns,
        );
        sideHandles.forEach((handle) => {
            const longClass = Array.from(handle.classList).find((c) =>
                c.startsWith("bloom-ui-canvas-element-side-handle-"),
            );
            if (!longClass) return;
            const side = longClass.substring(
                "bloom-ui-canvas-element-side-handle-".length,
            ) as keyof typeof cropped;
            if (cropped[side]) {
                handle.classList.add("bloom-currently-cropped");
            } else {
                handle.classList.remove("bloom-currently-cropped");
            }
        });
    }
}
