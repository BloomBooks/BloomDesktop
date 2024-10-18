// video-related functions that are used in both iframes. Aiming for minimal imports here,
// to minimize code that is pulled into both bundles.

import { getPageIframeBody } from "../../utils/shared";
import {
    kTextOverPictureSelector,
    getBubbleManager
} from "../toolbox/overlay/overlayUtils";

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
