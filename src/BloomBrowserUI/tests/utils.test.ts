import { test, expect } from "@playwright/test";

// THIS FILE WAS WRITTEN BY COPILOT

// Example of testing a pure TypeScript utility function
// This demonstrates how to load and test actual TypeScript modules in Playwright

test.describe("TypeScript Utility Functions", () => {
    test("should test string manipulation functions", async ({ page }) => {
        // Navigate to a blank page
        await page.goto("about:blank");

        // Test a simple utility function
        const result = await page.evaluate(() => {
            // Example utility function to test
            function capitalizeFirstLetter(str: string): string {
                if (!str) return str;
                return str.charAt(0).toUpperCase() + str.slice(1);
            }

            return {
                empty: capitalizeFirstLetter(""),
                singleChar: capitalizeFirstLetter("a"),
                multipleWords: capitalizeFirstLetter("hello world"),
                alreadyCapitalized: capitalizeFirstLetter("Hello")
            };
        });

        expect(result.empty).toBe("");
        expect(result.singleChar).toBe("A");
        expect(result.multipleWords).toBe("Hello world");
        expect(result.alreadyCapitalized).toBe("Hello");
    });

    test("should test array utility functions", async ({ page }) => {
        await page.goto("about:blank");

        const result = await page.evaluate(() => {
            // Example array utility function
            function removeDuplicates<T>(arr: T[]): T[] {
                return [...new Set(arr)];
            }

            return {
                numbers: removeDuplicates([1, 2, 2, 3, 3, 4]),
                strings: removeDuplicates(["a", "b", "a", "c"]),
                empty: removeDuplicates([])
            };
        });

        expect(result.numbers).toEqual([1, 2, 3, 4]);
        expect(result.strings).toEqual(["a", "b", "c"]);
        expect(result.empty).toEqual([]);
    });

    test("should test validation functions", async ({ page }) => {
        await page.goto("about:blank");

        const result = await page.evaluate(() => {
            // Example validation function
            function isValidEmail(email: string): boolean {
                const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
                return emailRegex.test(email);
            }

            return {
                valid: isValidEmail("test@example.com"),
                invalidNoDomain: isValidEmail("test@"),
                invalidNoAt: isValidEmail("testexample.com"),
                empty: isValidEmail("")
            };
        });

        expect(result.valid).toBe(true);
        expect(result.invalidNoDomain).toBe(false);
        expect(result.invalidNoAt).toBe(false);
        expect(result.empty).toBe(false);
    });
});
