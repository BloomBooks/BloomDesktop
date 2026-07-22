import { describe, expect, test } from "vitest";

import { isAiEditableImageSrc } from "./aiEditorImageFormats";

// Unit tests for isAiEditableImageSrc (see aiEditorImageFormats.ts), the front-end gate that
// keeps "Edit with AI..." disabled for formats the editor can't open. It works off the src's
// final extension, ignoring any path prefix and cache-busting query string / hash.
describe("isAiEditableImageSrc", () => {
    test("true for the editable raster formats, case-insensitively", () => {
        expect(isAiEditableImageSrc("photo.png")).toBe(true);
        expect(isAiEditableImageSrc("photo.jpg")).toBe(true);
        expect(isAiEditableImageSrc("photo.jpeg")).toBe(true);
        expect(isAiEditableImageSrc("photo.webp")).toBe(true);
        expect(isAiEditableImageSrc("PHOTO.PNG")).toBe(true);
    });

    test("false for formats the editor cannot edit", () => {
        expect(isAiEditableImageSrc("drawing.svg")).toBe(false);
        expect(isAiEditableImageSrc("scan.tif")).toBe(false);
        expect(isAiEditableImageSrc("scan.tiff")).toBe(false);
        expect(isAiEditableImageSrc("old.bmp")).toBe(false);
        expect(isAiEditableImageSrc("anim.gif")).toBe(false);
    });

    test("ignores path prefixes and a cache-busting query string or hash", () => {
        expect(
            isAiEditableImageSrc(
                "/bloom/C$3A/books/My Book/photo.png?optional=true&nocache=123",
            ),
        ).toBe(true);
        expect(isAiEditableImageSrc("images/drawing.svg?v=2")).toBe(false);
        expect(isAiEditableImageSrc("photo.png#frag")).toBe(true);
        // A query string containing a fake extension must not fool the extension check.
        expect(isAiEditableImageSrc("drawing.svg?fallback=photo.png")).toBe(
            false,
        );
    });

    test("false for empty, missing, or extension-less src", () => {
        expect(isAiEditableImageSrc(undefined)).toBe(false);
        expect(isAiEditableImageSrc(null)).toBe(false);
        expect(isAiEditableImageSrc("")).toBe(false);
        expect(isAiEditableImageSrc("noextension")).toBe(false);
    });
});
