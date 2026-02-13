import { Bubble, Comical, TailSpec } from "comicaljs";
import { getImageFromCanvasElement, isPlaceHolderImage } from "../bloomImages";
import {
    kBackgroundImageClass,
    kCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";
import { adjustCanvasElementAlternates } from "./CanvasElementAlternates";

export interface ICanvasElementCanvasResizeAdjustmentsHost {
    adjustBackgroundImageSize: (
        bloomCanvas: HTMLElement,
        bgCanvasElement: HTMLElement,
        useSizeOfNewImage: boolean,
    ) => void;

    pxToNumber: (source: string) => number;
}

export class CanvasElementCanvasResizeAdjustments {
    private host: ICanvasElementCanvasResizeAdjustmentsHost;

    public constructor(host: ICanvasElementCanvasResizeAdjustmentsHost) {
        this.host = host;
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
            `${bloomCanvas.clientWidth},${bloomCanvas.clientHeight}`,
        );
    }

    public adjustChildrenIfSizeChanged = (bloomCanvas: HTMLElement): void => {
        // Phase 1: detect whether the bloom-canvas size changed from the last
        // recorded baseline.
        const oldSizeData = bloomCanvas.getAttribute("data-imgsizebasedon");
        if (!oldSizeData) {
            if (
                bloomCanvas.getElementsByClassName(kCanvasElementClass).length >
                0
            ) {
                this.updateBloomCanvasSizeData(bloomCanvas);
            }
            return;
        }
        const oldSizeDataArray = oldSizeData.split(",");
        let oldWidth = parseInt(oldSizeDataArray[0]);
        let oldHeight = parseInt(oldSizeDataArray[1]);

        const newWidth = bloomCanvas.clientWidth;
        const newHeight = bloomCanvas.clientHeight;
        if (oldWidth === newWidth && oldHeight === newHeight) return;
        this.updateBloomCanvasSizeData(bloomCanvas);

        // Phase 2: collect children that participate in resize repositioning.
        const children = (
            Array.from(bloomCanvas.children) as HTMLElement[]
        ).filter(
            (c) =>
                c.style.left !== "" &&
                c.classList.contains("bloom-ui") === false &&
                c.tagName.toLowerCase() !== "canvas",
        );
        if (children.length === 0) return;

        let top = Number.MAX_VALUE;
        let bottom = -Number.MAX_VALUE;
        let left = Number.MAX_VALUE;
        let right = -Number.MAX_VALUE;
        // Phase 3: compute old bounds of relevant children and reconcile any
        // background-image offset quirks before scaling the rest.
        for (let i = 0; i < children.length; i++) {
            const child = children[i];
            const childTop = child.offsetTop;
            const childLeft = child.offsetLeft;
            if (child.classList.contains(kBackgroundImageClass)) {
                const img = getImageFromCanvasElement(child);
                if (
                    !img ||
                    isPlaceHolderImage(img.getAttribute("src")) ||
                    children.length === 1
                ) {
                    this.host.adjustBackgroundImageSize(
                        bloomCanvas,
                        child,
                        false,
                    );
                    if (children.length > 1) {
                        continue;
                    } else {
                        return;
                    }
                }
            }
            if (childTop < top) top = Math.max(childTop, 0);
            if (childLeft < left) left = Math.max(childLeft, 0);
            if (childTop + child.clientHeight > bottom)
                bottom = Math.min(childTop + child.clientHeight, oldHeight);
            if (childLeft + child.clientWidth > right)
                right = Math.min(childLeft + child.clientWidth, oldWidth);

            if (child.classList.contains(kBackgroundImageClass)) {
                if (
                    (child.clientLeft !== 0 && child.clientTop !== 0) ||
                    (Math.abs(child.clientWidth - oldWidth) > 1 &&
                        Math.abs(child.clientHeight - oldHeight) > 1)
                ) {
                    const deltaX = child.clientLeft;
                    const deltaY = child.clientTop;
                    for (let j = 0; j < children.length; j++) {
                        const c = children[j];
                        c.style.left =
                            this.host.pxToNumber(c.style.left) - deltaX + "px";
                        c.style.top =
                            this.host.pxToNumber(c.style.top) - deltaY + "px";
                    }
                    oldWidth = child.clientWidth;
                    oldHeight = child.clientHeight;
                }
                break;
            }
        }

        // Phase 4: compute the new content box in the resized canvas while
        // preserving relative padding and aggregate aspect ratio.
        const childrenHeight = bottom - top;
        const childrenWidth = right - left;
        const childrenAspectRatio = childrenWidth / childrenHeight;

        const widthPadding = oldWidth - childrenWidth;
        const heightPadding = oldHeight - childrenHeight;
        const oldLeftPaddingFraction =
            widthPadding > 1 ? left / widthPadding : 0.5;
        const oldTopPaddingFraction =
            heightPadding > 1 ? top / heightPadding : 0.5;
        const oldWidthFraction = childrenWidth / oldWidth;
        const oldHeightFraction = childrenHeight / oldHeight;
        let newChildrenWidth = oldWidthFraction * newWidth;
        let newChildrenHeight = oldHeightFraction * newHeight;
        if (newChildrenWidth / newChildrenHeight > childrenAspectRatio) {
            newChildrenHeight = newChildrenWidth / childrenAspectRatio;
            if (newChildrenHeight > newHeight) {
                newChildrenHeight = newHeight;
                newChildrenWidth = newChildrenHeight * childrenAspectRatio;
            }
        } else {
            newChildrenWidth = newChildrenHeight * childrenAspectRatio;
            if (newChildrenWidth > newWidth) {
                newChildrenWidth = newWidth;
                newChildrenHeight = newChildrenWidth / childrenAspectRatio;
            }
        }
        const scale = newChildrenWidth / childrenWidth;
        const newWidthPadding = newWidth - newChildrenWidth;
        const newHeightPadding = newHeight - newChildrenHeight;
        const newLeft = oldLeftPaddingFraction * newWidthPadding;
        const newTop = oldTopPaddingFraction * newHeightPadding;
        let needComicalUpdate = false;

        // Phase 5: reposition/resize each child and adjust image crop offsets,
        // tails, and alternates as needed.
        children.forEach((child: HTMLElement) => {
            const childTop = child.offsetTop;
            const childLeft = child.offsetLeft;
            let newChildTop = newTop + (childTop - top) * scale;
            let newChildLeft = newLeft + (childLeft - left) * scale;
            let newChildWidth = child.clientWidth;
            let newChildHeight = child.clientHeight;
            let reposition = true;
            const bubbleSpec = Bubble.getBubbleSpec(child);
            needComicalUpdate =
                needComicalUpdate ||
                (!!bubbleSpec.tails && bubbleSpec.tails.length > 0) ||
                bubbleSpec.spec !== "none";
            if (
                Array.from(child.children).some(
                    (c: HTMLElement) =>
                        c.classList.contains("bloom-imageContainer") ||
                        c.classList.contains("bloom-videoContainer"),
                )
            ) {
                newChildWidth = child.clientWidth * scale;
                newChildHeight = child.clientHeight * scale;
                const img = child.getElementsByTagName("img")[0];
                if (img && img.style.width) {
                    const imgLeft = this.host.pxToNumber(img.style.left);
                    const imgTop = this.host.pxToNumber(img.style.top);
                    const imgWidth = this.host.pxToNumber(img.style.width);
                    img.style.left = imgLeft * scale + "px";
                    img.style.top = imgTop * scale + "px";
                    img.style.width = imgWidth * scale + "px";
                }
            } else if (
                child.classList.contains(kCanvasElementClass) ||
                child.hasAttribute("data-target-of")
            ) {
                const oldCenterX = childLeft + child.clientWidth / 2;
                const oldCenterY = childTop + child.clientHeight / 2;
                const newCenterX = newLeft + (oldCenterX - left) * scale;
                const newCenterY = newTop + (oldCenterY - top) * scale;
                newChildTop = newCenterY - newChildHeight / 2;
                newChildLeft = newCenterX - newChildWidth / 2;
            } else {
                reposition = false;
            }
            if (reposition) {
                child.style.top = newChildTop + "px";
                child.style.left = newChildLeft + "px";
                child.style.width = newChildWidth + "px";
                child.style.height = newChildHeight + "px";
            }
            if (child.classList.contains(kCanvasElementClass)) {
                const tails: TailSpec[] = bubbleSpec.tails;
                tails.forEach((tail) => {
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
                            c.classList.contains("bloom-videoContainer"),
                    )
                ) {
                    adjustCanvasElementAlternates(
                        child,
                        scale,
                        left,
                        top,
                        newLeft,
                        newTop,
                    );
                }
            }
        });

        // Phase 6: redraw comical overlays once after batched updates.
        if (needComicalUpdate) {
            Comical.update(bloomCanvas);
        }
    };
}
