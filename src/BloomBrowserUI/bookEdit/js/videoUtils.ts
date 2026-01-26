// video-related functions that are used in both iframes. Aiming for minimal imports here,
// to minimize code that is pulled into both bundles.

import { getPageIframeBody } from "../../utils/shared";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";
import { getCanvasElementManager } from "../toolbox/canvas/canvasElementUtils";

export const kVideoContainerClass = "bloom-videoContainer";

// Set the attribute which makes a canvas element active for the sign language tool.
// Make sure nothing else has it.
// If it's in a canvas element, make that canvas element active. If not, make sure no canvas element is active.
// notifyCanvasElementManager is false when calling FROM setActiveElement, and should not be otherwise.
export function selectVideoContainer(
    videoContainer: Element | undefined | null,
    notifyCanvasElementManager = true,
) {
    const body = getPageIframeBody();
    if (body) {
        Array.from(body.getElementsByClassName("bloom-selected"))
            .filter((e) => e !== videoContainer)
            .forEach((e) => e.classList.remove("bloom-selected"));
    }
    videoContainer?.classList.add("bloom-selected");
    const canvasElement = videoContainer?.closest(
        kCanvasElementSelector,
    ) as HTMLElement;
    // If it's in a canvas element, make that canvas element active. If not, make sure no canvas element is active.
    // We don't need the confusion of two different ideas of what's active.
    if (notifyCanvasElementManager) {
        getCanvasElementManager()?.setActiveElement(canvasElement);
    }
}

// When switching to the comicTool from elsewhere (notably the sign language tool), we remove
// the 'bloom-selected' class, so the container doesn't have a yellow border like it does in the
// sign language tool. We only need to do this for non-canvas element video containers,
// since the yellow box is hidden in canvas elements, and we would like the same one (that is our active
// canvas element) to be selected in the sign langauge tool if we switch back.)
export function deselectVideoContainers() {
    const videoContainers: HTMLElement[] = Array.from(
        document.getElementsByClassName(kVideoContainerClass) as any,
    );
    videoContainers
        .filter((x) => !x.closest(kCanvasElementSelector))
        .forEach((container) => {
            container.classList.remove("bloom-selected");
        });
}
