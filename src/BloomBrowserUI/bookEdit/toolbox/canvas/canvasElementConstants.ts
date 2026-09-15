// split-pane.js and editMode.less know about this too
export const kBackgroundImageClass = "bloom-backgroundImage";

// Used in multiple places (toolbox + page iframe); keep this dependency-light.
export const kBloomButtonClass = "bloom-canvas-button";

// Used in multiple places (toolbox + page iframe); keep this dependency-light.
export const kCanvasElementClass = "bloom-canvas-element";
export const kCanvasElementSelector = `.${kCanvasElementClass}`;

export const kHasCanvasElementClass = "bloom-has-canvas-element";
// The requestPageContent delay id held for the whole conversion of an old-style background
// image (an img directly in the bloom-canvas) into a background canvas element, cleanup
// included. Shared here because the off-screen capture in bloomEditing.ts refuses to give up
// waiting on this particular delay (see captureContentForExternalProcessing).
export const kBackgroundConversionDelayId = "switchBackgroundToCanvasElement";
// also declared in split-pane.js, which needs it but doesn't want to be a module.
export const kBloomCanvasClass = "bloom-canvas";
export const kBloomCanvasSelector = `.${kBloomCanvasClass}`;

export const kImageFitModeAttribute = "data-image-fit";
export const kImageFitModeContainValue = "contain";
export const kImageFitModeCoverValue = "cover";
