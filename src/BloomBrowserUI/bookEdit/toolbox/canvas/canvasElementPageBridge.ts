// Cross-frame bridge from Toolbox code to the page-frame canvas element manager.
//
// This module intentionally imports workspaceFrames so Toolbox code can reach the
// page bundle's CanvasElementManager via bundle exports.
//
// This is intentionally a bridge module, not a general-purpose utils bucket.
// Prefer importing DOM selector constants from canvasElementConstants instead of this file.

import { getEditablePageBundleExports } from "../../js/workspaceFrames";
import type { CanvasElementManager } from "../../js/canvasElementManager/CanvasElementManager";
import {
    kBloomButtonClass,
    kBloomCanvasClass,
    kBloomCanvasSelector,
    kCanvasElementClass,
    kCanvasElementSelector,
    kHasCanvasElementClass,
    kImageFitModeAttribute,
    kImageFitModeContainValue,
    kImageFitModeCoverValue,
} from "./canvasElementConstants";

// Re-export the dependency-light DOM constants for backwards compatibility.
// Prefer importing these directly from canvasElementConstants instead of this bridge.
export {
    kBloomButtonClass,
    kBloomCanvasClass,
    kBloomCanvasSelector,
    kCanvasElementClass,
    kCanvasElementSelector,
    kHasCanvasElementClass,
    kImageFitModeAttribute,
    kImageFitModeContainValue,
    kImageFitModeCoverValue,
};

export function getCanvasElementManager(): CanvasElementManager | undefined {
    const editablePageBundleExports = getEditablePageBundleExports();
    return editablePageBundleExports
        ? editablePageBundleExports.getTheOneCanvasElementManager()
        : undefined;
}
