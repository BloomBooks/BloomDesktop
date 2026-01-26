// Helper functions extracted from CanvasElementManager.
//
// This module contains positioning and sizing helpers for canvas elements.
// It is used by the editable-page bundle and aims to stay focused on DOM/layout
// mechanics rather than selection/tool UI concerns.

import { BubbleSpec } from "comicaljs";
import { Point, PointScaling } from "../point";
import { kBloomCanvasSelector } from "../../toolbox/canvas/canvasElementConstants";
import { getCombinedBordersAndPaddings } from "./CanvasElementGeometry";

export const setCanvasElementPosition = (
    canvasElement: HTMLElement,
    unscaledRelativeLeft: number,
    unscaledRelativeTop: number,
): void => {
    if (canvasElement.classList.contains("bloom-passive-element")) {
        return;
    }

    canvasElement.style.left = unscaledRelativeLeft + "px";
    canvasElement.style.top = unscaledRelativeTop + "px";

    const currentWidth = canvasElement.style.width;
    if (!currentWidth || !currentWidth.endsWith("px")) {
        const clientWidth = canvasElement.clientWidth;
        const clientHeight = canvasElement.clientHeight;
        canvasElement.style.width = clientWidth + "px";
        canvasElement.style.height = clientHeight + "px";
        console.assert(
            clientWidth === canvasElement.clientWidth &&
                clientHeight === canvasElement.clientHeight,
            "CanvasElementManager.setCanvasElementPosition(): clientWidth/Height mismatch!",
        );
    }
};

export const getInteriorWidthHeight = (element: HTMLElement): Point => {
    const boundingRect = element.getBoundingClientRect();

    const exterior = new Point(
        boundingRect.width,
        boundingRect.height,
        PointScaling.Scaled,
        "getBoundingClientRect() result (Relative to viewport)",
    );

    const borderAndPadding = getCombinedBordersAndPaddings(element);
    return exterior.subtract(borderAndPadding);
};

export const getBloomCanvas = (element: Element): HTMLElement | null => {
    if (!element?.closest) {
        return null;
    }
    return element.closest(kBloomCanvasSelector);
};

export const inPlayMode = (someElt: Element): boolean => {
    return !!someElt
        .closest(".bloom-page")
        ?.parentElement?.classList.contains("drag-activity-play");
};

export const getChildPositionFromParentCanvasElement = (
    parentElement: HTMLElement,
    parentBubbleSpec: BubbleSpec | undefined,
): number[] => {
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

        if (deltaX > 0) {
            offsetX = -parentElement.clientWidth;
        } else {
            offsetX = parentElement.clientWidth;
        }

        if (deltaY > 0) {
            offsetY = -parentElement.clientHeight;
        } else {
            offsetY = parentElement.clientHeight;
        }
    }

    return [offsetX, offsetY];
};
