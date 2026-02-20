import {
    findNextVideoContainer,
    findPreviousVideoContainer,
} from "../../js/bloomVideo";
import { isPlaceHolderImage, kImageContainerClass } from "../../js/bloomImages";
import { getGameType, GameType } from "../games/GameInfo";
import { kDraggableIdAttribute } from "./canvasElementDraggables";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
} from "./canvasElementConstants";
import { getCanvasElementManager } from "./canvasElementUtils";
import { inferCanvasElementType } from "./canvasElementTypeInference";
import { canvasElementDefinitions } from "./canvasElementDefinitions";
import { CanvasElementType } from "./canvasElementTypes";
import { IControlContext } from "./canvasControlTypes";

const hasRealImage = (img: HTMLImageElement | undefined): boolean => {
    if (!img) {
        return false;
    }

    if (isPlaceHolderImage(img.getAttribute("src"))) {
        return false;
    }

    if (img.classList.contains("bloom-imageLoadError")) {
        return false;
    }

    if (img.parentElement?.classList.contains("bloom-imageLoadError")) {
        return false;
    }

    return true;
};

// Builds the runtime context used to resolve which canvas controls should be
// shown/enabled for the currently selected canvas element.
export const buildControlContext = (
    canvasElement: HTMLElement,
): IControlContext => {
    const closestPage = canvasElement.closest(".bloom-page");
    const page = closestPage instanceof HTMLElement ? closestPage : null;

    const inferredCanvasElementType = inferCanvasElementType(canvasElement);
    const isKnownType =
        !!inferredCanvasElementType &&
        inferredCanvasElementType in canvasElementDefinitions;

    // Fail soft for unknown/undefined inferred types. We need this because
    // type is inferred from DOM (not persisted), and mixed-version content can
    // legitimately produce shapes this build doesn't recognize yet.
    if (!inferredCanvasElementType) {
        const canvasElementId = canvasElement.getAttribute("id");
        const canvasElementClasses = canvasElement.getAttribute("class");
        console.warn(
            `inferCanvasElementType() returned undefined for a selected canvas element${canvasElementId ? ` id='${canvasElementId}'` : ""}${canvasElementClasses ? ` (class='${canvasElementClasses}')` : ""}. Falling back to 'none'.`,
        );
    } else if (!isKnownType) {
        console.warn(
            `Canvas element type '${inferredCanvasElementType}' is not registered in canvasElementDefinitions. Falling back to 'none'.`,
        );
    }

    // "none" intentionally degrades to safest controls rather than throwing.
    const elementType: CanvasElementType = isKnownType
        ? inferredCanvasElementType
        : "none";

    const imgContainer = canvasElement.getElementsByClassName(
        kImageContainerClass,
    )[0] as HTMLElement | undefined;

    const img = imgContainer?.getElementsByTagName("img")[0];

    const videoContainer = canvasElement.getElementsByClassName(
        "bloom-videoContainer",
    )[0] as HTMLElement | undefined;

    const hasImage = !!imgContainer;
    const hasVideo = !!videoContainer;
    const hasText =
        canvasElement.getElementsByClassName("bloom-editable").length > 0;
    const isRectangle =
        canvasElement.getElementsByClassName("bloom-rectangle").length > 0;
    const rectangle = canvasElement.getElementsByClassName(
        "bloom-rectangle",
    )[0] as HTMLElement | undefined;
    const isBackgroundImage = canvasElement.classList.contains(
        kBackgroundImageClass,
    );
    const isSpecialGameElement = canvasElement.classList.contains(
        "drag-item-order-sentence",
    );
    const isButton = canvasElement.classList.contains(kBloomButtonClass);

    const dataSound = canvasElement.getAttribute("data-sound") ?? "none";
    const hasCurrentImageSound = dataSound !== "none";

    const activityType = page?.getAttribute("data-activity") ?? "";
    const isInDraggableGame = activityType.startsWith("drag-");

    const currentDraggableId = canvasElement.getAttribute(
        kDraggableIdAttribute,
    );
    const hasDraggableId = !!currentDraggableId;

    // Draggability is intentionally constrained for several element kinds and
    // game states so we don't offer controls that would create invalid or
    // unsupported game behavior.
    const canToggleDraggability =
        page !== null &&
        isInDraggableGame &&
        getGameType(activityType, page) !== GameType.DragSortSentence &&
        !canvasElement.classList.contains("drag-item-wrong") &&
        !canvasElement.classList.contains("drag-item-correct") &&
        !canvasElement.classList.contains("bloom-gif") &&
        !canvasElement.querySelector(".bloom-rectangle") &&
        !isSpecialGameElement &&
        !isBackgroundImage &&
        !canvasElement.querySelector(`[data-icon-type=\"audio\"]`);

    return {
        canvasElement,
        page,
        elementType,
        hasImage,
        hasRealImage: hasRealImage(img),
        hasVideo,
        hasPreviousVideoContainer: videoContainer
            ? !!findPreviousVideoContainer(videoContainer)
            : false,
        hasNextVideoContainer: videoContainer
            ? !!findNextVideoContainer(videoContainer)
            : false,
        hasText,
        isRectangle,
        rectangleHasBackground:
            rectangle?.classList.contains("bloom-theme-background") ?? false,
        isCropped: !!img?.style?.width,
        isNavigationButton: elementType.startsWith("navigation-"),
        isButton,
        isBackgroundImage,
        isSpecialGameElement,
        canModifyImage:
            !!imgContainer &&
            !imgContainer.classList.contains("bloom-unmodifiable-image") &&
            !!img,
        canExpandBackgroundImage:
            getCanvasElementManager()?.canExpandToFillSpace() ?? false,
        missingMetadata:
            hasImage &&
            !isPlaceHolderImage(img?.getAttribute("src")) &&
            !!img &&
            !img.getAttribute("data-copyright"),
        isInDraggableGame,
        canChooseAudioForElement: isInDraggableGame && (hasImage || hasText),
        hasCurrentImageSound,
        currentImageSoundLabel: hasCurrentImageSound
            ? dataSound.replace(/\.mp3$/, "")
            : undefined,
        canToggleDraggability,
        hasDraggableId,
        hasDraggableTarget:
            !!currentDraggableId &&
            !!page?.querySelector(`[data-target-of=\"${currentDraggableId}\"]`),
        textHasAudio: true,
    };
};
