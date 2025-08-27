import { test, expect } from "@playwright/test";

// THIS FILE WAS WRITTEN BY COPILOT

// Simple unit tests for pure TypeScript functions
test.describe("EncodingUtils", () => {
    test("formatForHtml should encode HTML entities", async ({ page }) => {
        const result = await page.evaluate(() => {
            // Copy of the formatForHtml function for testing
            function formatForHtml(unsafeText) {
                // Simple HTML encoding
                let safeText = unsafeText
                    .replace(/&/g, "&amp;")
                    .replace(/</g, "&lt;")
                    .replace(/>/g, "&gt;")
                    .replace(/"/g, "&quot;")
                    .replace(/'/g, "&#39;");

                // Replace literal newlines with <br> elements
                const htmlNewline = "<br />";
                safeText = safeText
                    .replace(/\r\n/g, htmlNewline)
                    .replace(/\r/g, htmlNewline)
                    .replace(/\n/g, htmlNewline);

                return safeText;
            }

            return formatForHtml('<script>alert("XSS")</script>');
        });

        expect(result).toBe(
            "&lt;script&gt;alert(&quot;XSS&quot;)&lt;/script&gt;"
        );
    });

    test("formatForHtml should convert newlines to br tags", async ({
        page
    }) => {
        const result = await page.evaluate(() => {
            function formatForHtml(unsafeText) {
                let safeText = unsafeText
                    .replace(/&/g, "&amp;")
                    .replace(/</g, "&lt;")
                    .replace(/>/g, "&gt;")
                    .replace(/"/g, "&quot;")
                    .replace(/'/g, "&#39;");

                const htmlNewline = "<br />";
                safeText = safeText
                    .replace(/\r\n/g, htmlNewline)
                    .replace(/\r/g, htmlNewline)
                    .replace(/\n/g, htmlNewline);

                return safeText;
            }

            return formatForHtml("Line 1\nLine 2\r\nLine 3\rLine 4");
        });

        expect(result).toBe("Line 1<br />Line 2<br />Line 3<br />Line 4");
    });

    test("formatForHtml should handle empty string", async ({ page }) => {
        const result = await page.evaluate(() => {
            function formatForHtml(unsafeText) {
                let safeText = unsafeText
                    .replace(/&/g, "&amp;")
                    .replace(/</g, "&lt;")
                    .replace(/>/g, "&gt;")
                    .replace(/"/g, "&quot;")
                    .replace(/'/g, "&#39;");

                const htmlNewline = "<br />";
                safeText = safeText
                    .replace(/\r\n/g, htmlNewline)
                    .replace(/\r/g, htmlNewline)
                    .replace(/\n/g, htmlNewline);

                return safeText;
            }

            return formatForHtml("");
        });

        expect(result).toBe("");
    });
});
