// Helper functions extracted from CanvasElementManager.
//
// These are geometry/pixel conversion utilities used by the editable-page bundle
// when positioning and hit-testing canvas elements. They intentionally avoid taking
// a dependency on the full CanvasElementManager class to help keep that file smaller
// and reduce import coupling.

import { Point, PointScaling } from "../point";
import { reportError } from "../../../lib/errorHandler";

export const convertPointFromViewportToElementFrame = (
    pointRelativeToViewport: Point,
    element: Element,
): Point => {
    const referenceBounds = element.getBoundingClientRect();
    const origin = new Point(
        referenceBounds.left,
        referenceBounds.top,
        PointScaling.Scaled,
        "BoundingClientRect (Relative to viewport)",
    );

    const border = getLeftAndTopBorderWidths(element);
    const padding = getLeftAndTopPaddings(element);
    const borderAndPadding = border.add(padding);

    const scroll = getScrollAmount(element);
    if (scroll.length() > 0.001) {
        const error = new Error(
            `Assert failed. container.scroll expected to be (0, 0), but it was: (${scroll.getScaledX()}, ${scroll.getScaledY()})`,
        );
        reportError(error.message, error.stack || "");
    }

    return pointRelativeToViewport.subtract(origin).subtract(borderAndPadding);
};

export const getLeftAndTopBorderWidths = (element: Element): Point => {
    return new Point(
        element.clientLeft,
        element.clientTop,
        PointScaling.Unscaled,
        "Element ClientLeft/Top (Unscaled)",
    );
};

export const getRightAndBottomBorderWidths = (
    element: Element,
    styleInfo?: CSSStyleDeclaration,
): Point => {
    if (!styleInfo) {
        styleInfo = window.getComputedStyle(element);
    }

    const borderRight: number = extractNumber(
        styleInfo.getPropertyValue("border-right-width"),
    );
    const borderBottom: number = extractNumber(
        styleInfo.getPropertyValue("border-bottom-width"),
    );

    return new Point(
        borderRight,
        borderBottom,
        PointScaling.Unscaled,
        "Element ClientRight/Bottom (Unscaled)",
    );
};

export const getCombinedBorderWidths = (
    element: Element,
    styleInfo?: CSSStyleDeclaration,
): Point => {
    if (!styleInfo) {
        styleInfo = window.getComputedStyle(element);
    }

    return getLeftAndTopBorderWidths(element).add(
        getRightAndBottomBorderWidths(element, styleInfo),
    );
};

export const getPadding = (
    side: string,
    styleInfo: CSSStyleDeclaration,
): number => {
    const propertyKey = `padding-${side}`;
    const paddingString = styleInfo.getPropertyValue(propertyKey);
    return extractNumber(paddingString);
};

export const getLeftAndTopPaddings = (
    element: Element,
    styleInfo?: CSSStyleDeclaration,
): Point => {
    if (!styleInfo) {
        styleInfo = window.getComputedStyle(element);
    }

    return new Point(
        getPadding("left", styleInfo),
        getPadding("top", styleInfo),
        PointScaling.Unscaled,
        "CSSStyleDeclaration padding",
    );
};

export const getRightAndBottomPaddings = (
    element: Element,
    styleInfo?: CSSStyleDeclaration,
): Point => {
    if (!styleInfo) {
        styleInfo = window.getComputedStyle(element);
    }

    return new Point(
        getPadding("right", styleInfo),
        getPadding("bottom", styleInfo),
        PointScaling.Unscaled,
        "Padding",
    );
};

export const getCombinedPaddings = (
    element: Element,
    styleInfo?: CSSStyleDeclaration,
): Point => {
    if (!styleInfo) {
        styleInfo = window.getComputedStyle(element);
    }

    return getLeftAndTopPaddings(element, styleInfo).add(
        getRightAndBottomPaddings(element, styleInfo),
    );
};

export const getCombinedBordersAndPaddings = (element: Element): Point => {
    const styleInfo = window.getComputedStyle(element);
    const borders = getCombinedBorderWidths(element);
    const paddings = getCombinedPaddings(element, styleInfo);
    return borders.add(paddings);
};

export const getScrollAmount = (element: Element): Point => {
    return new Point(
        element.scrollLeft,
        element.scrollTop,
        PointScaling.Unscaled,
        "Element ScrollLeft/Top (Unscaled)",
    );
};

export const extractNumber = (text: string | undefined | null): number => {
    if (!text) {
        return 0;
    }

    let i = 0;
    for (i = 0; i < text.length; ++i) {
        const c = text.charAt(i);
        if ((c < "0" || c > "9") && c !== "-" && c !== "+" && c !== ".") {
            break;
        }
    }

    let numberStr = "";
    if (i > 0) {
        numberStr = text.substring(0, i);
    }

    return Number(numberStr);
};
