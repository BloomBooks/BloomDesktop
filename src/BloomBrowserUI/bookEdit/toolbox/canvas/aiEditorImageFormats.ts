// The image formats the AI Image Editor can actually load and edit. Kept deliberately
// short: formats the editor can't handle (e.g. svg, tif, bmp, gif) are excluded so the
// "Edit with AI..." menu item stays disabled for them and we never hand the editor an
// image it can't open. Must stay in sync with the host's list of the same name
// (BloomExe/web/controllers/AiImageEditorApi.cs, AllowedImageExtensions).
export const kAiEditableImageExtensions: ReadonlySet<string> = new Set([
    "png",
    "jpg",
    "jpeg",
    "webp",
]);

/**
 * True if the given image src points at a format the AI Image Editor can edit. The src may
 * carry a cache-busting query string or hash and any path prefix; only the final extension
 * matters. A src with no recognizable extension is treated as not editable.
 */
export function isAiEditableImageSrc(src: string | null | undefined): boolean {
    if (!src) {
        return false;
    }
    // Drop any query string / hash, then take the text after the last dot in the filename.
    const withoutQuery = src.split(/[?#]/)[0];
    const lastSlash = Math.max(
        withoutQuery.lastIndexOf("/"),
        withoutQuery.lastIndexOf("\\"),
    );
    const filename = withoutQuery.substring(lastSlash + 1);
    const lastDot = filename.lastIndexOf(".");
    if (lastDot < 0) {
        return false;
    }
    const extension = filename.substring(lastDot + 1).toLowerCase();
    return kAiEditableImageExtensions.has(extension);
}
