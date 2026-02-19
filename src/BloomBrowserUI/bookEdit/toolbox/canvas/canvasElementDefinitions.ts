import { CanvasElementType } from "./canvasElementTypes";
import {
    ICanvasElementDefinition,
    AvailabilityRulesMap,
} from "./canvasControlTypes";
import {
    audioAvailabilityRules,
    bubbleAvailabilityRules,
    imageAvailabilityRules,
    textAvailabilityRules,
    videoAvailabilityRules,
    wholeElementAvailabilityRules,
} from "./canvasAvailabilityPresets";

const mergeRules = (...rules: AvailabilityRulesMap[]): AvailabilityRulesMap => {
    return Object.assign({}, ...rules);
};

export const imageCanvasElementDefinition: ICanvasElementDefinition = {
    type: "image",
    menuSections: ["image", "audio", "wholeElement"],
    toolbar: [
        "missingMetadata",
        "chooseImage",
        "pasteImage",
        "expandToFillSpace",
        "spacer",
        "duplicate",
        "delete",
    ],
    toolPanel: [],
    availabilityRules: mergeRules(
        imageAvailabilityRules,
        audioAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const videoCanvasElementDefinition: ICanvasElementDefinition = {
    type: "video",
    menuSections: ["video", "wholeElement"],
    toolbar: ["chooseVideo", "recordVideo", "spacer", "duplicate", "delete"],
    toolPanel: [],
    availabilityRules: mergeRules(
        videoAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const soundCanvasElementDefinition: ICanvasElementDefinition = {
    type: "sound",
    menuSections: ["audio", "wholeElement"],
    toolbar: ["duplicate", "delete"],
    toolPanel: [],
    availabilityRules: mergeRules(
        audioAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const rectangleCanvasElementDefinition: ICanvasElementDefinition = {
    type: "rectangle",
    menuSections: ["audio", "bubble", "text", "wholeElement"],
    toolbar: ["format", "spacer", "duplicate", "delete"],
    toolPanel: ["bubble", "text", "outline"],
    availabilityRules: mergeRules(
        audioAvailabilityRules,
        bubbleAvailabilityRules,
        textAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const speechCanvasElementDefinition: ICanvasElementDefinition = {
    type: "speech",
    menuSections: ["audio", "bubble", "text", "wholeElement"],
    toolbar: ["format", "spacer", "duplicate", "delete"],
    toolPanel: ["bubble", "text", "outline"],
    availabilityRules: mergeRules(
        audioAvailabilityRules,
        bubbleAvailabilityRules,
        textAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const captionCanvasElementDefinition: ICanvasElementDefinition = {
    type: "caption",
    menuSections: ["audio", "bubble", "text", "wholeElement"],
    toolbar: ["format", "spacer", "duplicate", "delete"],
    toolPanel: ["bubble", "text", "outline"],
    availabilityRules: mergeRules(
        audioAvailabilityRules,
        bubbleAvailabilityRules,
        textAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const bookLinkGridDefinition: ICanvasElementDefinition = {
    type: "book-link-grid",
    menuSections: ["linkGrid", "wholeElement"],
    toolbar: ["linkGridChooseBooks", "spacer", "duplicate", "delete"],
    toolPanel: ["text"],
    availabilityRules: {
        linkGridChooseBooks: {
            visible: (ctx) => ctx.isLinkGrid,
        },
        duplicate: {
            visible: (ctx) => ctx.isLinkGrid,
        },
        delete: {
            surfacePolicy: {
                toolbar: {
                    visible: (ctx) => ctx.isLinkGrid,
                },
                menu: {
                    visible: (ctx) => ctx.isLinkGrid,
                },
            },
            enabled: (ctx) => ctx.isLinkGrid,
        },
        textColor: "exclude",
        backgroundColor: {
            visible: (ctx) => ctx.isBookGrid,
        },
    },
};

export const navigationImageButtonDefinition: ICanvasElementDefinition = {
    type: "navigation-image-button",
    menuSections: ["url", "image", "wholeElement"],
    toolbar: [
        "setDestination",
        "chooseImage",
        "pasteImage",
        "spacer",
        "duplicate",
        "delete",
    ],
    toolPanel: ["text", "imagePanel"],
    availabilityRules: {
        ...mergeRules(
            imageAvailabilityRules,
            textAvailabilityRules,
            wholeElementAvailabilityRules,
        ),
        setDestination: {
            visible: () => true,
        },
        imageFillMode: {
            visible: (ctx) => ctx.hasImage,
        },
        textColor: {
            visible: (ctx) => ctx.hasText,
        },
        backgroundColor: {
            visible: () => true,
        },
        missingMetadata: {
            surfacePolicy: {
                toolbar: {
                    visible: () => false,
                },
                menu: {
                    visible: (ctx) => ctx.hasImage && ctx.canModifyImage,
                    enabled: (ctx) => ctx.hasRealImage,
                },
            },
        },
    },
};

export const navigationImageWithLabelButtonDefinition: ICanvasElementDefinition =
    {
        type: "navigation-image-with-label-button",
        menuSections: ["url", "image", "text", "wholeElement"],
        toolbar: [
            "setDestination",
            "chooseImage",
            "pasteImage",
            "spacer",
            "duplicate",
            "delete",
        ],
        toolPanel: ["text", "imagePanel"],
        availabilityRules: {
            ...mergeRules(
                imageAvailabilityRules,
                textAvailabilityRules,
                wholeElementAvailabilityRules,
            ),
            setDestination: {
                visible: () => true,
            },
            imageFillMode: {
                visible: (ctx) => ctx.hasImage,
            },
            textColor: {
                visible: (ctx) => ctx.hasText,
            },
            backgroundColor: {
                visible: () => true,
            },
            missingMetadata: {
                surfacePolicy: {
                    toolbar: {
                        visible: () => false,
                    },
                    menu: {
                        visible: (ctx) => ctx.hasImage && ctx.canModifyImage,
                        enabled: (ctx) => ctx.hasRealImage,
                    },
                },
            },
        },
    };

export const navigationLabelButtonDefinition: ICanvasElementDefinition = {
    type: "navigation-label-button",
    menuSections: ["url", "text", "wholeElement"],
    toolbar: ["setDestination", "spacer", "duplicate", "delete"],
    toolPanel: ["text"],
    availabilityRules: {
        ...mergeRules(textAvailabilityRules, wholeElementAvailabilityRules),
        setDestination: {
            visible: () => true,
        },
        backgroundColor: {
            visible: () => true,
        },
    },
};

export const noneCanvasElementDefinition: ICanvasElementDefinition = {
    type: "none",
    menuSections: ["wholeElement"],
    toolbar: ["duplicate", "delete"],
    toolPanel: [],
    availabilityRules: mergeRules(wholeElementAvailabilityRules),
};

export const canvasElementDefinitions: Record<
    CanvasElementType,
    ICanvasElementDefinition
> = {
    image: imageCanvasElementDefinition,
    video: videoCanvasElementDefinition,
    sound: soundCanvasElementDefinition,
    rectangle: rectangleCanvasElementDefinition,
    speech: speechCanvasElementDefinition,
    caption: captionCanvasElementDefinition,
    "book-link-grid": bookLinkGridDefinition,
    "navigation-image-button": navigationImageButtonDefinition,
    "navigation-image-with-label-button":
        navigationImageWithLabelButtonDefinition,
    "navigation-label-button": navigationLabelButtonDefinition,
    none: noneCanvasElementDefinition,
};
