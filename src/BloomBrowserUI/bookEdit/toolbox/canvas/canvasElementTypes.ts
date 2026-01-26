// The canonical set of supported canvas element types.
//
// This type is used by the declarative canvasElementDefinitions registry and by
// legacy inference/migration code.

export type CanvasElementType =
    | "image"
    | "video"
    | "sound"
    | "rectangle"
    | "speech"
    | "caption"
    | "book-link-grid"
    | "navigation-image-button"
    | "navigation-image-with-label-button"
    | "navigation-label-button"
    | "none";
