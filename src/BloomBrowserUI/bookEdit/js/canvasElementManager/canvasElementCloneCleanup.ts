// Clone-and-clean logic extracted from CanvasElementDuplication so it can be
// unit-tested without instantiating the full class.

export function cloneCanvasElementHtmlStructure(
    elementToClone: HTMLElement,
): string {
    const clone = elementToClone.cloneNode(true) as HTMLElement;
    cleanClonedNode(clone);
    return clone.innerHTML;
}

function cleanClonedNode(element: Element): void {
    if (clonedNodeNeedsDeleting(element)) {
        element.parentElement!.removeChild(element);
        return;
    }
    if (element.nodeName === "#text") {
        return;
    }

    safelyRemoveAttribute(element, "id");
    if (element.nodeName === "IMG") {
        safelyRemoveAttribute(element, "data-book");
    }
    removePositiveTabindex(element);
    safelyRemoveAttribute(element, "data-duration");
    safelyRemoveAttribute(element, "data-audiorecordingendtimes");

    const childArray = Array.from(element.childNodes);
    childArray.forEach((child) => {
        cleanClonedNode(child as Element);
    });
}

function removePositiveTabindex(element: Element): void {
    const indexStr = element.getAttribute("tabindex");
    if (!indexStr) {
        return;
    }
    const indexValue = parseInt(indexStr, 10);
    if (indexValue > 0) {
        element.attributes.removeNamedItem("tabindex");
    }
}

function safelyRemoveAttribute(element: Element, attrName: string): void {
    if (element.hasAttribute(attrName)) {
        element.attributes.removeNamedItem(attrName);
    }
}

function clonedNodeNeedsDeleting(element: Element): boolean {
    const htmlElement = element as HTMLElement;
    return (
        !htmlElement ||
        (htmlElement.classList !== undefined &&
            htmlElement.classList.contains("bloom-ui"))
    );
}
