// Names for inline images (see inlineImages.ts) that code which cannot import inlineImages.ts
// without an import cycle also needs, such as editableDivUtils.ts.

// The class of the wrapper around each inline image.
export const kInlineImageClass = "bloom-inlineImage";

// Identifies one inline image. Its copy in every language's editable has the same value. A
// text block can hold any number of inline images, so there may be no single "wrapper in this
// editable", and sync, normalize and undo all pair up the copies by this value. It has to be
// a data attribute rather than an id, because TranslationGroupManager removes the id from an
// editable it clones for a new language (TranslationGroupManager.cs:998). None of the C# code
// that cleans up book markup touches a data attribute it does not know, and StripOutText only
// removes p/br/u/b/i elements and text nodes, so the wrapper and this attribute reach the new
// language with the value unchanged.
export const kInlineImageIdAttr = "data-bloom-inline-image-id";
