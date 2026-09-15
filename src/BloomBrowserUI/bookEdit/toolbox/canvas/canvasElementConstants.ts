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

export const kImageFitModeAttribute = "data-image-fit";
export const kImageFitModeContainValue = "contain";
export const kImageFitModeCoverValue = "cover";

// The class Bloom puts on an image slot, and the selector for it. Lives here rather than in
// bloomImages.ts so that modules which must not drag that file (and everything it imports)
// into their bundle can still name a slot; bloomImages.ts re-exports both. C# knows the same
// name as HtmlDom.kImageContainerClass.
export const kImageContainerClass = "bloom-imageContainer";
export const kImageContainerSelector = `.${kImageContainerClass}`;
