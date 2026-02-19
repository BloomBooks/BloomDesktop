// Helpers and constants related to draggable canvas elements used by Bloom games.
//
// Keep this module dependency-light so it can be used from toolbox and page bundles.

export const kDraggableIdAttribute = "data-draggable-id";

export const isDraggable = (canvasElement: Element | undefined): boolean => {
    return !!canvasElement?.getAttribute(kDraggableIdAttribute);
};

export const getAllDraggables = (page: HTMLElement | Document): Element[] => {
    return Array.from(page.querySelectorAll(`[${kDraggableIdAttribute}]`));
};
