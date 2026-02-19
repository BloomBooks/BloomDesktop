import { AvailabilityRulesMap } from "./canvasControlTypes";

export const imageAvailabilityRules: AvailabilityRulesMap = {
    chooseImage: {
        visible: (ctx) => ctx.hasImage,
        enabled: (ctx) => ctx.canModifyImage,
    },
    pasteImage: {
        visible: (ctx) => ctx.hasImage,
        enabled: (ctx) => ctx.canModifyImage,
    },
    copyImage: {
        visible: (ctx) => ctx.hasImage,
        enabled: (ctx) => ctx.hasRealImage,
    },
    resetImage: {
        visible: (ctx) => ctx.hasImage,
        enabled: (ctx) => ctx.isCropped,
    },
    missingMetadata: {
        surfacePolicy: {
            toolbar: {
                visible: (ctx) => ctx.hasRealImage && ctx.missingMetadata,
            },
            menu: {
                visible: (ctx) => ctx.hasImage && ctx.canModifyImage,
                enabled: (ctx) => ctx.hasRealImage,
            },
        },
    },
    expandToFillSpace: {
        visible: (ctx) => ctx.isBackgroundImage,
        enabled: (ctx) => ctx.canExpandBackgroundImage,
    },
};

export const videoAvailabilityRules: AvailabilityRulesMap = {
    chooseVideo: {
        visible: (ctx) => ctx.hasVideo,
    },
    recordVideo: {
        visible: (ctx) => ctx.hasVideo,
    },
    playVideoEarlier: {
        visible: (ctx) => ctx.hasVideo,
        enabled: (ctx) => ctx.hasPreviousVideoContainer,
    },
    playVideoLater: {
        visible: (ctx) => ctx.hasVideo,
        enabled: (ctx) => ctx.hasNextVideoContainer,
    },
};

export const audioAvailabilityRules: AvailabilityRulesMap = {
    chooseAudio: {
        visible: (ctx) => ctx.canChooseAudioForElement,
    },
};

export const textAvailabilityRules: AvailabilityRulesMap = {
    format: {
        visible: (ctx) => ctx.hasText,
    },
    copyText: {
        visible: (ctx) => ctx.hasText,
    },
    pasteText: {
        visible: (ctx) => ctx.hasText,
    },
    autoHeight: {
        visible: (ctx) => ctx.hasText && !ctx.isButton,
    },
    fillBackground: {
        visible: (ctx) => ctx.isRectangle,
    },
};

export const bubbleAvailabilityRules: AvailabilityRulesMap = {
    addChildBubble: {
        visible: (ctx) => ctx.hasText && !ctx.isInDraggableGame,
    },
};

export const wholeElementAvailabilityRules: AvailabilityRulesMap = {
    duplicate: {
        visible: (ctx) =>
            !ctx.isLinkGrid &&
            !ctx.isBackgroundImage &&
            !ctx.isSpecialGameElement,
    },
    delete: {
        surfacePolicy: {
            toolbar: {
                visible: (ctx) => !ctx.isLinkGrid && !ctx.isSpecialGameElement,
            },
            menu: {
                visible: (ctx) => !ctx.isLinkGrid,
            },
        },
        enabled: (ctx) => {
            if (ctx.isBackgroundImage) {
                return ctx.hasRealImage;
            }
            if (ctx.isSpecialGameElement) {
                return false;
            }
            return true;
        },
    },
    toggleDraggable: {
        visible: (ctx) => ctx.canToggleDraggability,
    },
    togglePartOfRightAnswer: {
        visible: (ctx) => ctx.hasDraggableId,
    },
};
