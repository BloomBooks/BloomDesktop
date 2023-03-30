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
