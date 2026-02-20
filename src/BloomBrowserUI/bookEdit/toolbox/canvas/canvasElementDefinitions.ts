// Declarative canvas element definitions.
//
// This file is the per-element source of truth for which controls appear on each
// surface:
// - `menuSections`: which menu section groups are shown and in what order
// - `toolbar`: which context-toolbar controls are shown and in what order
// - `toolPanel`: which right-side toolbox panel sections are shown
// - `availabilityRules`: per-control visibility/enabled policy overrides
//
// Supporting modules:
// - `canvasControlRegistry.ts` provides concrete control implementations and section maps.
// - `canvasControlAvailabilityPresets.ts` provides shared policy fragments composed here.
// - `canvasControlHelpers.ts` resolves these definitions into concrete UI rows/buttons.
//
// Design intent: keep each element definition explicit and readable so reviewers can
// understand behavior from this file without chasing constructor indirection.
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
} from "./canvasControlAvailabilityPresets";

const mergeRules = (...rules: AvailabilityRulesMap[]): AvailabilityRulesMap => {
    return rules.reduce<AvailabilityRulesMap>((result, rule) => {
        return {
            ...result,
            ...rule,
        };
    }, {});
};

export const imageCanvasElementDefinition: ICanvasElementDefinition = {
    type: "image",
    // Shared definition: image elements can appear on standard canvas pages and
    // also as game pieces created from the Game tool.
    // `gameDraggable` is intentionally listed here so game pages can surface
    // draggable commands; availability rules/context keep it hidden on non-game pages.
    menuSections: ["image", "audio", "gameDraggable", "wholeElement"],
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
    // Shared definition: video elements are used both in normal canvas editing
    // and as game pieces on game pages.
    // `gameDraggable` is game-only in practice; non-game pages resolve this
    // section to no visible rows via runtime availability.
    menuSections: ["video", "gameDraggable", "wholeElement"],
    toolbar: ["chooseVideo", "recordVideo", "spacer", "duplicate", "delete"],
    toolPanel: [],
    availabilityRules: mergeRules(
        videoAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const soundCanvasElementDefinition: ICanvasElementDefinition = {
    type: "sound",
    // Shared definition: sound elements are used in regular canvas content and
    // can also participate in game layouts.
    // `gameDraggable` is included for game contexts and intentionally resolves
    // to no rows on non-game pages.
    menuSections: ["audio", "gameDraggable", "wholeElement"],
    toolbar: ["duplicate", "delete"],
    toolPanel: [],
    availabilityRules: mergeRules(
        audioAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

export const rectangleCanvasElementDefinition: ICanvasElementDefinition = {
    type: "rectangle",
    // Shared definition: rectangle bubbles are used in standard canvas pages and
    // can also appear as fixed game pieces.
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
    // Shared definition: speech bubbles are used in normal canvas pages and
    // are also a primary game piece type.
    // `gameDraggable` is listed so game pages can expose drag-specific commands;
    // it remains hidden outside game context.
    menuSections: ["audio", "bubble", "gameDraggable", "text", "wholeElement"],
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
    // Shared definition: caption elements are used in regular canvas editing
    // and can also be used as game pieces.
    // `gameDraggable` is included for game behavior and is not shown on
    // non-game pages because availability gates it.
    menuSections: ["audio", "bubble", "gameDraggable", "text", "wholeElement"],
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
        textColor: "exclude",
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
            visible: true,
        },
        imageFillMode: {
            visible: (ctx) => ctx.hasImage,
        },
        textColor: {
            visible: (ctx) => ctx.hasText,
        },
        backgroundColor: {
            visible: true,
        },
        missingMetadata: {
            // Keep metadata editing in the menu for navigation buttons,
            // but do not show it as a toolbar icon.
            surfacePolicy: {
                toolbar: {
                    visible: false,
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
                visible: true,
            },
            imageFillMode: {
                visible: (ctx) => ctx.hasImage,
            },
            textColor: {
                visible: (ctx) => ctx.hasText,
            },
            backgroundColor: {
                visible: true,
            },
            missingMetadata: {
                // Keep metadata editing in the menu for navigation buttons,
                // but do not show it as a toolbar icon.
                surfacePolicy: {
                    toolbar: {
                        visible: false,
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
            visible: true,
        },
        backgroundColor: {
            visible: true,
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
