import { OverlayTool } from "../overlay/overlayTool";

// This file is intended to expose some image description functions that other parts of the
// code (in both iframes) need to use, while pulling in a minimum of dependencies.

export function showImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const bubbleManager = OverlayTool.bubbleManager();
    bubbleManager?.suspendComicEditing("forTool");
    // turn on special layout to make image descriptions visible (might already be on, so check first)
    if (!bodyOfPageIframe.classList.contains("bloom-showImageDescriptions")) {
        bodyOfPageIframe.classList.add("bloom-showImageDescriptions");
        // for each bloom-imageContainer that is not a child of another bloom-imageContainer,
        // wrap the contents (except the description) with another division with class bloom-describedImage
        // See comment in editMode.less under bloom-describedImage for why we do this.
        for (const imageContainer of Array.from(
            bodyOfPageIframe.getElementsByClassName("bloom-imageContainer")
        )) {
            if (
                !imageContainer.parentElement?.closest(".bloom-imageContainer")
            ) {
                const describedImage = document.createElement("div");
                describedImage.classList.add("bloom-describedImage");
                for (const child of Array.from(imageContainer.children)) {
                    if (!child.classList.contains("bloom-imageDescription")) {
                        describedImage.appendChild(child);
                    }
                }
                imageContainer.appendChild(describedImage);
            }
        }
    }
}
export function hideImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const bubbleManager = OverlayTool.bubbleManager();
    // removing the class and wrapper should be done first; resume may not work
    // right while the extra wrapper is present.
    bodyOfPageIframe.classList.remove("bloom-showImageDescriptions");
    // unwrap the contents of each bloom-describedImage
    for (const describedImage of Array.from(
        bodyOfPageIframe.getElementsByClassName("bloom-describedImage")
    )) {
        for (const child of Array.from(describedImage.children)) {
            describedImage.parentElement!.appendChild(child);
        }
        describedImage.remove();
    }
    bubbleManager?.resumeComicEditing();
}
