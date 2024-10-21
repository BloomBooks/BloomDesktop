// This file exposes some utility functions that are needed in both iframes. The idea is
// to make them available to import with a minimum of dependencies.

import { getEditablePageBundleExports } from "../../editViewFrame";
import { BubbleManager } from "../../js/bubbleManager";

export const kTextOverPictureClass = "bloom-textOverPicture";
export const kTextOverPictureSelector = `.${kTextOverPictureClass}`;
// Class added to primary image containers when the user has taken control of their
// size and position within the space available. Also used for primary images which
// have overlays. This suppressed the removal of height and width in bloomImages.SetupImage(),
// but (unlike the old bloom-scale-with-code) does not)
export const kcodeResizeClass = "bloom-codeResize";

// Enhance: we could reduce cross-bundle dependencies by separately defining the BubbleManager interface
// and just importing that here.
export function getBubbleManager(): BubbleManager | undefined {
    const exports = getEditablePageBundleExports();
    return exports ? exports.getTheOneBubbleManager() : undefined;
}
