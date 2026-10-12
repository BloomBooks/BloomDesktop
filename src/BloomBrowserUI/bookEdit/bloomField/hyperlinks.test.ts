import { afterEach, describe, expect, test, vi } from "vitest";

import { tryProcessHyperlink } from "./hyperlinks";

// The prefix check in tryProcessHyperlink must not depend on the browser's locale. It used to call
// toLocaleLowerCase() with no argument, which follows the WebView2 (i.e. Windows display) locale;
// in Turkish, "I" lower-cases to the dotless "ı", so "MAILTO:" became "maılto:" and a perfectly
// good link was rejected. See BL-16754.
describe("tryProcessHyperlink", () => {
    const bookId = "abc123";
    const originalToLocaleLowerCase = String.prototype.toLocaleLowerCase;

    afterEach(() => {
        vi.restoreAllMocks();
    });

    test("accepts allowed prefixes regardless of case", () => {
        expect(tryProcessHyperlink("HTTPS://example.com", bookId)).toBe(
            "HTTPS://example.com",
        );
        expect(tryProcessHyperlink("MAILTO:someone@example.com", bookId)).toBe(
            "MAILTO:someone@example.com",
        );
        expect(tryProcessHyperlink("ftp://example.com", bookId)).toBe("");
    });

    test("still accepts MAILTO: when the ambient locale lower-cases like Turkish", () => {
        // Make the argument-less toLocaleLowerCase behave as it does on a Turkish machine.
        vi.spyOn(String.prototype, "toLocaleLowerCase").mockImplementation(
            function (this: string) {
                return originalToLocaleLowerCase.call(this, "tr-TR");
            },
        );
        // Sanity check: the simulated locale really does break naive lower-casing.
        expect("MAILTO:".toLocaleLowerCase()).toBe("ma\u0131lto:");

        expect(tryProcessHyperlink("MAILTO:someone@example.com", bookId)).toBe(
            "MAILTO:someone@example.com",
        );
    });

    test("simplifies links into the current book to the page id part", () => {
        expect(tryProcessHyperlink(`/book/${bookId}#page1`, bookId)).toBe(
            "#page1",
        );
    });
});
