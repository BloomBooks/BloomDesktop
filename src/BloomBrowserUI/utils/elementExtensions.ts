// Defines extension methods for element.
declare global {
    interface Element {
        farthest<E extends Element = Element>(selector: string): E | null;
    }
}

export {};

/**
 * Like Element.closest(), but finds the farthest element that matches the selector instead of the closest
 * (self included)
 * To use this extension method, you must import this file in any file that references it (e.g. import "../../utils/elementExtensions";)
 *
 * @returns The farthest element matching the selector (i.e. the earliest ancestor or "patriarch" that matches the selector)
 * If the element matches the selector but no ancestors do, the element will be returned (just like in closest)
 * If no self-or-ancestor elements match the selector, then returns null.
 */
Element.prototype.farthest = function<E extends Element = Element>(
    this: Element,
    selector: string
) {
    let patriarch = this.closest<E>(selector);

    while (patriarch) {
        const nextAncestor = patriarch.parentElement?.closest<E>(selector);

        if (nextAncestor) {
            patriarch = nextAncestor;
        } else {
            break;
        }
    }

    return patriarch;
};
