import { OverlayTool } from "../overlay/overlayTool";

// This file is intended to expose some image description functions that other parts of the
// code (in both iframes) need to use, while pulling in a minimum of dependencies.

export function showImageDescriptions(page: HTMLElement, show: boolean) {
    const bubbleManager = OverlayTool.bubbleManager();
    if (show) {
        bubbleManager?.suspendComicEditing("forTool");
        // turn on special layout to make image descriptions visible (might already be on)
        page.classList.add("bloom-showImageDescriptions");
    } else {
        // removing the class must be done first; resume won't work right while the overlays
        // are hidden
        page.classList.remove("bloom-showImageDescriptions");
        bubbleManager?.resumeComicEditing();
    }
}
