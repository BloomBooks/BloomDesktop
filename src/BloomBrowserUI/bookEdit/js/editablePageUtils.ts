import { getToolboxBundleExports } from "./bloomFrames";
import { kMotionToolId } from "../toolbox/toolIds";

// Utility functions likely to be useful in multiple places in the editable page context.
// Similar purpose to editableDivUtils.ts, but for more modern code I don't want the
// clutter of a class with static methods.

// Should we hide various tools that clutter images?
// Currently this suppresses origami and selecting canvas elements when the motion tool is active.
// There may one day be other tools that want this behavior, or additional behavior that
// should be suppressed.
export function shouldHideToolsOverImages(): boolean {
    const toolbox = getToolboxBundleExports();
    if (!toolbox) {
        return false;
    }
    const toolboxInstance = toolbox.getTheOneToolbox();
    return (
        toolboxInstance.toolboxIsShowing() &&
        toolboxInstance.getCurrentTool()?.id() === kMotionToolId
    );
}
