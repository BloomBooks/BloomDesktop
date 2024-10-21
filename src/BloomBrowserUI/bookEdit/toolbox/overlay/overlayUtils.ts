// This file exposes some utility functions that are needed in both iframes. The idea is
// to make them available to import with a minimum of dependencies.

import { getEditablePageBundleExports } from "../../editViewFrame";
import { BubbleManager } from "../../js/bubbleManager";

export const kTextOverPictureClass = "bloom-textOverPicture";
export const kTextOverPictureSelector = `.${kTextOverPictureClass}`;

// Enhance: we could reduce cross-bundle dependencies by separately defining the BubbleManager interface
// and just importing that here.
export function getBubbleManager(): BubbleManager | undefined {
    const exports = getEditablePageBundleExports();
    return exports ? exports.getTheOneBubbleManager() : undefined;
}
