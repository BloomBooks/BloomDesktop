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

    getHandleTitlesAsync: (
        controlFrame: HTMLElement,
        className: string,
        l10nId: string,
        force?: boolean,
        attribute?: string,
    ) => Promise<void>;

    startMoving: () => void;
    stopMoving: () => void;
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
    };

    private continueMoveCrop = (event: MouseEvent) => {
        const activeElement = this.host.getActiveElement();
        if (event.buttons !== 1 || !activeElement) {
            return;
        }
        const deltaX = event.clientX - this.startMoveCropX;
        const deltaY = event.clientY - this.startMoveCropY;
        const imgC =
            activeElement.getElementsByClassName(kImageContainerClass)[0];
        const img = imgC?.getElementsByTagName("img")[0];
        if (!img) return;
        event.preventDefault();
        event.stopPropagation();
        const imgStyle = img.style;
        const newLeft = Math.max(
            Math.min(this.oldImageLeft + deltaX, 0),
            activeElement.clientLeft +
                activeElement.clientWidth -
                img.clientWidth,
        );
        const newTop = Math.max(
            Math.min(this.oldImageTop + deltaY, 0),
            activeElement.clientTop +
                activeElement.clientHeight -
                img.clientHeight,
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
        document.removeEventListener("mousemove", this.continueResizeDrag, {
            capture: true,
        });
        document.removeEventListener("mouseup", this.endResizeDrag, {
            capture: true,
        });
        this.currentDragControl?.classList.remove("active-control");
        this.guideProvider.endDrag();
        this.snapProvider.endDrag();
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
        const deltaX = event.clientX - this.startResizeDragX;
        const deltaY = event.clientY - this.startResizeDragY;
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
        let deltaX = event.clientX - this.startSideDragX;
        let deltaY = event.clientY - this.startSideDragY;
        let newCanvasElementWidth = this.oldWidth;
        let newCanvasElementHeight = this.oldHeight;
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
                activeElement.style.width = `${newCanvasElementWidth}px`;
                activeElement.style.left = `${this.oldLeft + deltaX}px`;
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
        let deltaX = event.clientX - this.startSideDragX;
        let deltaY = event.clientY - this.startSideDragY;
        if (event.movementX === 0 && event.movementY === 0) return;

        let newCanvasElementWidth = this.oldWidth;
        let newCanvasElementHeight = this.oldHeight;
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

        switch (this.currentDragSide) {
            case "n":
                deltaY = this.adjustDeltaForSnap(
                    shouldSnapForBackground === "y",
                    deltaY,
                    backgroundSnapDelta,
                    "n",
                );
                if (this.oldImageTop - deltaY > 0) {
                    deltaY = this.oldImageTop;
                }
                newCanvasElementHeight = Math.max(
                    this.oldHeight - deltaY,
                    minHeight,
                );
                deltaY = this.oldHeight - newCanvasElementHeight;
                activeElement.style.height = `${newCanvasElementHeight}px`;
                activeElement.style.top = `${this.oldTop + deltaY}px`;
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
                    this.initialCropImageTop + this.initialCropImageHeight <
                    this.oldHeight + deltaY
                ) {
                    deltaY =
                        this.initialCropImageTop +
                        this.initialCropImageHeight -
                        this.oldHeight;
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
                    this.initialCropImageLeft + this.initialCropImageWidth <
                    this.oldWidth + deltaX
                ) {
                    deltaX =
                        this.initialCropImageLeft +
                        this.initialCropImageWidth -
                        this.oldWidth;
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
                if (this.oldImageLeft > deltaX) {
                    deltaX = this.oldImageLeft;
                }
                newCanvasElementWidth = Math.max(
                    this.oldWidth - deltaX,
                    minWidth,
                );
                deltaX = this.oldWidth - newCanvasElementWidth;
                activeElement.style.width = `${newCanvasElementWidth}px`;
                activeElement.style.left = `${this.oldLeft + deltaX}px`;
                img.style.left = `${this.oldImageLeft - deltaX}px`;
                break;
        }
        this.host.adjustStuffRelatedToImage(activeElement, img);
        this.updateCurrentlyCropped(activeElement);
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
            void this.host.getHandleTitlesAsync(
                controlFrame,
                "bloom-ui-canvas-element-side-handle-" + side,
                "Fill",
                true,
                "data-title",
            );
            return backgroundSnapDelta;
        }
        void this.host.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-side-handle-" + side,
            "Crop",
            true,
            "data-title",
        );
        return delta;
    }

    public adjustMoveCropHandleVisibility(removeCropAttrsIfNotNeeded = false) {
        const controlFrame = document.getElementById(
            "canvas-element-control-frame",
        );
        const activeElement = this.host.getActiveElement();
        if (!controlFrame || !activeElement) return;
        const imgC =
            activeElement.getElementsByClassName(kImageContainerClass)[0];
        const img = imgC?.getElementsByTagName("img")[0];
        let wantMoveCropHandle = false;
        if (img) {
            const imgRect = img.getBoundingClientRect();
            const controlRect = controlFrame.getBoundingClientRect();
            wantMoveCropHandle =
                imgRect.width > controlRect.width + 1 ||
                imgRect.height > controlRect.height + 1;
            if (!wantMoveCropHandle && removeCropAttrsIfNotNeeded) {
                img.style.width = "";
                img.style.top = "";
                img.style.left = "";
            }
        }
        controlFrame.classList.toggle(
            "bloom-ui-canvas-element-show-move-crop-handle",
            wantMoveCropHandle,
        );
        this.updateCurrentlyCropped(activeElement);
    }

    private updateCurrentlyCropped(activeElement: HTMLElement) {
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
        const imgRect = img.getBoundingClientRect();
        const canvasElementRect = activeElement.getBoundingClientRect();
        const slop = 1;
        const cropped = {
            n: imgRect.top + slop < canvasElementRect.top,
            e: imgRect.right > canvasElementRect.right + slop,
            s: imgRect.bottom > canvasElementRect.bottom + slop,
            w: imgRect.left + slop < canvasElementRect.left,
        };
        sideHandles.forEach((handle) => {
            const longClass = Array.from(handle.classList).find((c) =>
                c.startsWith("bloom-ui-canvas-element-side-handle-"),
            );
            if (!longClass) return;
            const side = longClass.substring(
                "bloom-ui-canvas-element-side-handle-".length,
            );
            if (cropped[side]) {
                handle.classList.add("bloom-currently-cropped");
            } else {
                handle.classList.remove("bloom-currently-cropped");
            }
        });
    }
}
