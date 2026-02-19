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
import { canvasElementDefinitionsNew } from "./canvasElementNewDefinitions";
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

export const buildControlContext = (
    canvasElement: HTMLElement,
): IControlContext => {
    const page = canvasElement.closest(".bloom-page") as HTMLElement | null;

    const inferredCanvasElementType = inferCanvasElementType(canvasElement);
    const isKnownType =
        !!inferredCanvasElementType &&
        Object.prototype.hasOwnProperty.call(
            canvasElementDefinitionsNew,
            inferredCanvasElementType,
        );

    if (!inferredCanvasElementType) {
        const canvasElementId = canvasElement.getAttribute("id");
        const canvasElementClasses = canvasElement.getAttribute("class");
        console.warn(
            `inferCanvasElementType() returned undefined for a selected canvas element${canvasElementId ? ` id='${canvasElementId}'` : ""}${canvasElementClasses ? ` (class='${canvasElementClasses}')` : ""}. Falling back to 'none'.`,
        );
    } else if (!isKnownType) {
        console.warn(
            `Canvas element type '${inferredCanvasElementType}' is not registered in canvasElementDefinitionsNew. Falling back to 'none'.`,
        );
    }

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

    const isLinkGrid =
        canvasElement.getElementsByClassName("bloom-link-grid").length > 0;
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
        isLinkGrid,
        isNavigationButton: elementType.startsWith("navigation-"),
        isButton,
        isBookGrid: isLinkGrid,
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
            ? dataSound.replace(/.mp3$/, "")
            : undefined,
        canToggleDraggability,
        hasDraggableId,
        hasDraggableTarget:
            !!currentDraggableId &&
            !!page?.querySelector(`[data-target-of=\"${currentDraggableId}\"]`),
        textHasAudio: true,
    };
};
