// Cross-frame bridge utilities for canvas element code.
//
// This module intentionally imports bloomFrames so Toolbox code can reach the page-frame
// CanvasElementManager via bundle exports.
//
// Prefer importing DOM selector constants from canvasElementConstants instead of this file.

import { getEditablePageBundleExports } from "../../js/bloomFrames";
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
