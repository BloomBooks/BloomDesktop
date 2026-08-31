// Declarative canvas element control registry.
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
// - `canvasControlAvailabilityRules.ts` provides shared policy fragments composed here.
// - `canvasControlResolution.ts` resolves this registry into concrete UI rows/buttons.
//
// Design intent: keep each element type's control configuration explicit and
// readable, while leaving the concrete command/panel implementations in
// `canvasControlRegistry.ts`.
import { CanvasElementType } from "./canvasElementTypes";
import {
    ICanvasElementControlConfiguration,
    AvailabilityRulesMap,
} from "./canvasControlTypes";
import { bloomBuildsMenuForCell } from "./canvasElementTableCells";
import {
    audioAvailabilityRules,
    bubbleAvailabilityRules,
    imageAvailabilityRules,
    textAvailabilityRules,
    videoAvailabilityRules,
    wholeElementAvailabilityRules,
} from "./canvasControlAvailabilityRules";

const mergeRules = (...rules: AvailabilityRulesMap[]): AvailabilityRulesMap => {
    return rules.reduce<AvailabilityRulesMap>((result, rule) => {
        return {
            ...result,
            ...rule,
        };
    }, {});
};

export const imageCanvasElementControls: ICanvasElementControlConfiguration = {
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

export const videoCanvasElementControls: ICanvasElementControlConfiguration = {
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

export const soundCanvasElementControls: ICanvasElementControlConfiguration = {
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

export const rectangleBubbleCanvasElementControls: ICanvasElementControlConfiguration =
    {
        type: "rectangle",
        // Shared definition: rectangular bubble elements are used in standard canvas
        // pages and can also appear as fixed game pieces.
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

export const speechCanvasElementControls: ICanvasElementControlConfiguration = {
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

export const captionCanvasElementControls: ICanvasElementControlConfiguration =
    {
        type: "caption",
        // Shared definition: caption elements are used in regular canvas editing
        // and can also be used as game pieces.
        // `gameDraggable` is included for game behavior and is not shown on
        // non-game pages because availability gates it.
        menuSections: [
            "audio",
            "bubble",
            "gameDraggable",
            "text",
            "wholeElement",
        ],
        toolbar: ["format", "spacer", "duplicate", "delete"],
        toolPanel: ["bubble", "text", "outline"],
        availabilityRules: mergeRules(
            audioAvailabilityRules,
            bubbleAvailabilityRules,
            textAvailabilityRules,
            wholeElementAvailabilityRules,
        ),
    };

export const tableCanvasElementControls: ICanvasElementControlConfiguration = {
    type: "table",
    // A table carries its own in-page affordances for everything inside it:
    // pills for the table, the row and the column, edge insert buttons, and
    // right-click menus. So the canvas surfaces only what belongs to the
    // element as a whole, such as Duplicate and Delete.
    menuSections: ["wholeElement"],
    toolbar: ["duplicate", "delete"],
    toolPanel: [],
    availabilityRules: wholeElementAvailabilityRules,
};

export const calendarCanvasElementControls: ICanvasElementControlConfiguration =
    {
        type: "calendar",
        // A calendar month grid is a table, and carries the same in-page affordances a table
        // does, so the canvas adds only what belongs to the element as a whole. The Calendar
        // section is what the grid itself needs: which month it is for, and whether it shows
        // the dates of the neighboring months.
        menuSections: ["calendar", "wholeElement"],
        toolbar: ["duplicate", "delete"],
        toolPanel: [],
        availabilityRules: wholeElementAvailabilityRules,
    };

export const bookLinkGridControls: ICanvasElementControlConfiguration = {
    type: "book-link-grid",
    menuSections: ["linkGrid", "wholeElement"],
    toolbar: ["linkGridChooseBooks", "spacer", "duplicate", "delete"],
    toolPanel: ["text"],
    availabilityRules: {
        textColor: "exclude",
    },
};

export const navigationImageButtonControls: ICanvasElementControlConfiguration =
    {
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

export const navigationImageWithLabelButtonControls: ICanvasElementControlConfiguration =
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

export const navigationLabelButtonControls: ICanvasElementControlConfiguration =
    {
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

export const noneCanvasElementControls: ICanvasElementControlConfiguration = {
    type: "none",
    menuSections: ["formatTarget", "wholeElement"],
    toolbar: ["format", "spacer", "duplicate", "delete"],
    toolPanel: [],
    availabilityRules: mergeRules(
        textAvailabilityRules,
        wholeElementAvailabilityRules,
    ),
};

/**
 * The controls for a canvas element that is the content of a table cell, built from
 * the controls its own type would give it.
 *
 * The toolbar is kept as it is, because its buttons act on the content itself:
 * choosing the picture a picture cell shows, pasting one, fitting it to the cell.
 * The menu becomes the cell's. The rows an element's own type puts there act on the
 * element as a whole, and a cell's content is not free to be acted on that way:
 * duplicating or deleting it, or making it the page's background, would leave the
 * cell holding something other than the one piece of content of the type it says it
 * has.
 *
 * There are two ways to give the user the cell's menu, and which cell it is decides
 * between them.
 *
 * For most cells the menu is the bloom-table library's own Cell menu, the one a
 * right-click on the cell opens, and Bloom composes no menu at all: menuSections is
 * empty and opensTableCellMenu sends the "..." button to the library.
 *
 * A picture in a calendar month grid needs more than that menu holds: the user
 * expects the image commands as well. So there Bloom composes the menu, from the
 * image section it already has plus the items the library gives for the cell (see
 * includesTableCellMenuItems), and one menu carries both. The section brings
 * becomeBackground, which a cell's picture must not do: it is already the background
 * image of the cell's own bloom-canvas.
 */
export const cellContentControls = (
    controlsForItsOwnType: ICanvasElementControlConfiguration,
    tableCell: HTMLElement,
): ICanvasElementControlConfiguration => {
    if (bloomBuildsMenuForCell(tableCell)) {
        return {
            ...controlsForItsOwnType,
            menuSections: ["image"],
            includesTableCellMenuItems: true,
            availabilityRules: {
                ...controlsForItsOwnType.availabilityRules,
                becomeBackground: "exclude",
            },
        };
    }
    return {
        ...controlsForItsOwnType,
        menuSections: [],
        opensTableCellMenu: true,
    };
};

export const canvasElementControlRegistry: Record<
    CanvasElementType,
    ICanvasElementControlConfiguration
> = {
    image: imageCanvasElementControls,
    video: videoCanvasElementControls,
    sound: soundCanvasElementControls,
    rectangle: rectangleBubbleCanvasElementControls,
    speech: speechCanvasElementControls,
    caption: captionCanvasElementControls,
    table: tableCanvasElementControls,
    calendar: calendarCanvasElementControls,
    "book-link-grid": bookLinkGridControls,
    "navigation-image-button": navigationImageButtonControls,
    "navigation-image-with-label-button":
        navigationImageWithLabelButtonControls,
    "navigation-label-button": navigationLabelButtonControls,
    none: noneCanvasElementControls,
};
