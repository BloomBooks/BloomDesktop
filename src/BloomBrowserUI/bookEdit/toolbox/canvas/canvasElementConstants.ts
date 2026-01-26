// split-pane.js and editMode.less know about this too
export const kBackgroundImageClass = "bloom-backgroundImage";

// Used in multiple places (toolbox + page iframe); keep this dependency-light.
export const kBloomButtonClass = "bloom-canvas-button";

// Used in multiple places (toolbox + page iframe); keep this dependency-light.
export const kCanvasElementClass = "bloom-canvas-element";
export const kCanvasElementSelector = `.${kCanvasElementClass}`;

export const kHasCanvasElementClass = "bloom-has-canvas-element";
// also declared in split-pane.js, which needs it but doesn't want to be a module.
export const kBloomCanvasClass = "bloom-canvas";
export const kBloomCanvasSelector = `.${kBloomCanvasClass}`;
