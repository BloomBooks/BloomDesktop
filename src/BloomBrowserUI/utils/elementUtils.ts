import { EditableDivUtils } from "../bookEdit/js/editableDivUtils";

/**
 * Like element.closest(), but instead finds the farthest element (starting from ${startElement}) that matches the selector
 * (self included)
 *
 * @returns The farthest element matching the selector (i.e. the earliest ancestor or "patriarch" that matches the selector)
 * If ${startElement} matches the selector but no ancestors do, the element will be returned (just like in closest)
 * If no self-or-ancestor elements match the selector, then returns null.
 */
export function farthest<E extends Element = Element>(
    startElement: Element,
    selector: string
): E | null {
    let patriarch = startElement.closest<E>(selector);

    while (patriarch) {
        const nextAncestor = patriarch.parentElement?.closest<E>(selector);

        if (nextAncestor) {
            patriarch = nextAncestor;
        } else {
            break;
        }
    }

    return patriarch;
}

export function getBorderThickness(element) {
    const styles = window.getComputedStyle(element);
    const borderTopWidth = parseFloat(
        styles.getPropertyValue("border-top-width")
    );
    const borderBottomWidth = parseFloat(
        styles.getPropertyValue("border-bottom-width")
    );
    const borderLeftWidth = parseFloat(
        styles.getPropertyValue("border-left-width")
    );
    const borderRightWidth = parseFloat(
        styles.getPropertyValue("border-right-width")
    );
    return {
        top: borderTopWidth,
        bottom: borderBottomWidth,
        left: borderLeftWidth,
        right: borderRightWidth
    };
}

// .clientHeight and .clientWidth return integers, so if rounding errors are a problem we can
// use this to get the exact fractional values. Like clientHeight and clientWidth, this include
// the padding, but not the border, margin. However, unlike clientHeight and clientWidth,
// this size will include the scrollbar since we use getBoundingClientRect().
// This may also behave differently from .clientHeight and .clientWidth when used on the root element
export function getExactClientSize(
    element: HTMLElement
): { width: number; height: number } {
    const scalingFactor = EditableDivUtils.getPageScale();
    const boundingRect = element.getBoundingClientRect();
    const borderThicknesses = getBorderThickness(element);
    const exactClientHeight =
        boundingRect.height / scalingFactor -
        (borderThicknesses.top + borderThicknesses.bottom);
    const exactClientWidth =
        boundingRect.width / scalingFactor -
        (borderThicknesses.left + borderThicknesses.right);
    return {
        width: exactClientWidth,
        height: exactClientHeight
    };
}
