import { CanvasElementType } from "./canvasElementTypes";

export type CanvasElementMenuSection =
    | "url"
    | "video"
    | "image"
    | "audio"
    | "bubble"
    | "text"
    | "wholeElementCommands";

export type CanvasElementToolbarButton =
    | "spacer"
    | "setDestination"
    | "chooseVideo"
    | "recordVideo"
    | "chooseImage"
    | "pasteImage"
    | "missingMetadata"
    | "expandToFillSpace"
    | "format"
    | "duplicate"
    | "delete"
    | "linkGridChooseBooks";

export interface ICanvasElementDefinition {
    type: CanvasElementType;
    menuSections: CanvasElementMenuSection[];
    toolbarButtons: CanvasElementToolbarButton[];
}

export const canvasElementDefinitions: Record<
    CanvasElementType,
    ICanvasElementDefinition
> = {
    image: {
        type: "image",
        menuSections: ["image", "audio", "wholeElementCommands"],
        toolbarButtons: [
            "missingMetadata",
            "chooseImage",
            "pasteImage",
            "expandToFillSpace",
            "spacer",
            "duplicate",
            "delete",
        ],
    },
    video: {
        type: "video",
        menuSections: ["video", "wholeElementCommands"],
        toolbarButtons: [
            "chooseVideo",
            "recordVideo",
            "spacer",
            "duplicate",
            "delete",
        ],
    },
    sound: {
        type: "sound",
        menuSections: ["audio", "wholeElementCommands"],
        toolbarButtons: ["duplicate", "delete"],
    },
    rectangle: {
        type: "rectangle",
        menuSections: ["audio", "bubble", "text", "wholeElementCommands"],
        toolbarButtons: ["format", "spacer", "duplicate", "delete"],
    },
    speech: {
        type: "speech",
        menuSections: ["audio", "bubble", "text", "wholeElementCommands"],
        toolbarButtons: ["format", "spacer", "duplicate", "delete"],
    },
    caption: {
        type: "caption",
        menuSections: ["audio", "bubble", "text", "wholeElementCommands"],
        toolbarButtons: ["format", "spacer", "duplicate", "delete"],
    },
    "book-link-grid": {
        type: "book-link-grid",
        menuSections: ["text"],
        toolbarButtons: ["linkGridChooseBooks"],
    },
    "navigation-image-button": {
        type: "navigation-image-button",
        menuSections: ["url", "image", "wholeElementCommands"],
        toolbarButtons: [
            "setDestination",
            //"missingMetadata",
            "chooseImage",
            "pasteImage",
            "spacer",
            "duplicate",
            "delete",
        ],
    },
    "navigation-image-with-label-button": {
        type: "navigation-image-with-label-button",
        menuSections: ["url", "image", "text", "wholeElementCommands"],
        toolbarButtons: [
            "setDestination",
            //"missingMetadata",
            "chooseImage",
            "pasteImage",
            //"format",
            "spacer",
            "duplicate",
            "delete",
        ],
    },
    "navigation-label-button": {
        type: "navigation-label-button",
        menuSections: ["url", "text", "wholeElementCommands"],
        toolbarButtons: [
            "setDestination",
            //"format",
            "spacer",
            "duplicate",
            "delete",
        ],
    },
    none: {
        type: "none",
        menuSections: ["wholeElementCommands"],
        toolbarButtons: ["duplicate", "delete"],
    },
};
