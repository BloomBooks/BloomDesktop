// Small bridge helpers for page-bundle code that needs to interact with the
// toolbox bundle without importing the full CanvasElementManager class.
// This replaces the old public-functions module with a name that makes the
// page-to-toolbox bridge direction explicit.

import { kCanvasToolId } from "../../toolbox/toolIds";
import {
    doWhenEditTabBundleLoaded,
    getToolboxBundleExports,
} from "../bloomFrames";
import { kImageContainerClass } from "../bloomImages";

// This is just for debugging. It produces a string that describes the canvas element, generally
// well enough to identify it in console.log.
export function canvasElementDescription(
    e: Element | null | undefined,
): string {
    const elt = e as HTMLElement;
    if (!elt) {
        return "no canvas element";
    }
    const result =
        "canvas element at (" + elt.style.left + ", " + elt.style.top + ") ";
    const imageContainer = elt.getElementsByClassName(kImageContainerClass)[0];
    if (imageContainer) {
        const img = (imageContainer as HTMLElement).getElementsByTagName(
            "img",
        )[0];
        if (img) {
            return result + "with image : " + img.getAttribute("src");
        }
    }
    const videoSrc = elt.getElementsByTagName("source")[0];
    if (videoSrc) {
        return result + "with video " + videoSrc.getAttribute("src");
    }
    // Enhance: look for videoContainer similarly
    return result + "with text " + elt.innerText;
}

export function showCanvasTool() {
    const handleToolbox = (toolbox: {
        toolboxIsShowing: () => boolean;
        activateToolFromId: (toolId: string) => void;
        ensureToolEnabled?: (toolId: string) => void;
    }) => {
        if (toolbox.toolboxIsShowing()) {
            if (typeof toolbox.ensureToolEnabled === "function") {
                toolbox.ensureToolEnabled(kCanvasToolId);
            }
            return;
        }
        toolbox.activateToolFromId(kCanvasToolId);
    };

    const toolbox = getToolboxBundleExports()?.getTheOneToolbox();
    if (toolbox) {
        handleToolbox(toolbox);
        return;
    }

    doWhenEditTabBundleLoaded((rootFrameExports) => {
        rootFrameExports.doWhenToolboxLoaded((toolboxFrameExports) => {
            const loadedToolbox = toolboxFrameExports.getTheOneToolbox();
            if (!loadedToolbox) {
                return;
            }
            handleToolbox(loadedToolbox);
        });
    });
}
