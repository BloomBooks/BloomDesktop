// video-related functions that are used in both iframes. Aiming for minimal imports here,
// to minimize code that is pulled into both bundles.

import { getPageIframeBody } from "../../utils/shared";
import {
    kTextOverPictureSelector,
    getBubbleManager
} from "../toolbox/overlay/overlayUtils";

export const kVideoContainerClass = "bloom-videoContainer";

// Set the attribute which makes a bubble active for the sign language tool.
// Make sure nothing else has it.
// If it's in an overlay, make that overlay active. If not, make sure no overlay is active.
// notifyBubbleManager is false when calling FROM setActiveElement, and should not be otherwise.
export function selectVideoContainer(
    videoContainer: Element | undefined | null,
    notifyBubbleManager = true
) {
    const body = getPageIframeBody();
    if (body) {
        Array.from(body.getElementsByClassName("bloom-selected"))
            .filter(e => e !== videoContainer)
            .forEach(e => e.classList.remove("bloom-selected"));
    }
    videoContainer?.classList.add("bloom-selected");
    const overlay = videoContainer?.closest(
        kTextOverPictureSelector
    ) as HTMLElement;
    // If it's in an overlay, make that overlay active. If not, make sure no overlay is active.
    // We don't need the confusion of two different ideas of what's active.
    if (notifyBubbleManager) {
        getBubbleManager()?.setActiveElement(overlay);
    }
}

// When switching to the comicTool from elsewhere (notably the sign language tool), we remove
// the 'bloom-selected' class, so the container doesn't have a yellow border like it does in the
// sign language tool. We only need to do this for non-overlay video containers,
// since the yellow box is hidden in overlays, and we would like the same one (that is our active
// overlay) to be selected in the sign langauge tool if we switch back.)
export function deselectVideoContainers() {
    const videoContainers: HTMLElement[] = Array.from(
        document.getElementsByClassName(kVideoContainerClass) as any
    );
    videoContainers
        .filter(x => !x.closest(kTextOverPictureSelector))
        .forEach(container => {
            container.classList.remove("bloom-selected");
        });
}
