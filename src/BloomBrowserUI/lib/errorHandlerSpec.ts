import { describe, expect, it } from "vitest";
import { shouldIgnoreUnhandledError } from "./errorHandler";

describe("shouldIgnoreUnhandledError", () => {
    // This is the exact message from BL-16587, produced when a CKEditor plugin script
    // fails to load because the edit page was torn down (tab/page switch) mid-request.
    const bl16587Message =
        'Uncaught [CKEDITOR.resourceManager.load] Resource name "floatpanel" was not found at "http://localhost:8092/bloom/lib/ckeditor/plugins/floatpanel/plugin.js?t=F62B".';

    it("ignores the BL-16587 CKEditor floatpanel resource-load failure", () => {
        // Sanity check: make sure the message really looks like the real one before asserting.
        expect(bl16587Message).toContain("floatpanel");
        expect(shouldIgnoreUnhandledError(bl16587Message)).toBe(true);
    });

    it("ignores the same failure for any CKEditor plugin, not just floatpanel", () => {
        const colorbuttonMessage =
            'Uncaught [CKEDITOR.resourceManager.load] Resource name "colorbutton" was not found at "http://localhost:8092/bloom/lib/ckeditor/plugins/colorbutton/plugin.js?t=F62B".';
        expect(shouldIgnoreUnhandledError(colorbuttonMessage)).toBe(true);
    });

    it("still ignores the previously-handled ResizeObserver nuisance", () => {
        expect(
            shouldIgnoreUnhandledError(
                "ResizeObserver loop completed with undelivered notifications.",
            ),
        ).toBe(true);
    });

    it("does NOT ignore an ordinary error", () => {
        expect(
            shouldIgnoreUnhandledError(
                "TypeError: Cannot read properties of undefined (reading 'foo')",
            ),
        ).toBe(false);
    });
});
