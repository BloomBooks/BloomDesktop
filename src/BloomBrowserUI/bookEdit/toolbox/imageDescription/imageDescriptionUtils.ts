import { OverlayTool } from "../overlay/overlayTool";

// This file is intended to expose some image description functions that other parts of the
// code (in both iframes) need to use, while pulling in a minimum of dependencies.

export function showImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const bubbleManager = OverlayTool.bubbleManager();
    bubbleManager?.suspendComicEditing("forTool");
    // turn on special layout to make image descriptions visible (might already be on)
    bodyOfPageIframe.classList.add("bloom-showImageDescriptions");
}
export function hideImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const bubbleManager = OverlayTool.bubbleManager();
    // removing the class must be done first; resume won't work right while the overlays
    // are hidden
    bodyOfPageIframe.classList.remove("bloom-showImageDescriptions");
    bubbleManager?.resumeComicEditing();
}
