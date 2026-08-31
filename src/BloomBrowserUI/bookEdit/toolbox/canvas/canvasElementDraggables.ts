// Helpers and constants related to draggable canvas elements used by Bloom games.
//
// Keep this module dependency-light so it can be used from toolbox and page bundles.

export const kDraggableIdAttribute = "data-draggable-id";

// True when a canvas element currently participates in draggable game behavior.
export const isDraggable = (canvasElement: Element | undefined): boolean => {
    return !!canvasElement?.getAttribute(kDraggableIdAttribute);
};

// Returns all draggable canvas elements within the given page/document root.
export const getAllDraggables = (page: HTMLElement | Document): Element[] => {
    return Array.from(page.querySelectorAll(`[${kDraggableIdAttribute}]`));
};
