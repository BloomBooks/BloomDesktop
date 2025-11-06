import { test, expect } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser, bookList } from "./test-helpers";

test.describe("LinkTargetChooser - Page Styling", () => {
    test("Injects CSS files and applies book attributes", async ({ page }) => {
        test.setTimeout(15000);
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book1",
            currentBookId: "book1",
            cssFiles: ["styles/core.css", "nested/folder/print.css"],
            bookAttributes: { "data-theme": "night" },
        });

        await bookList.waitForBooksToLoad();
        const pagesResponse = page.waitForResponse((response) => {
            return (
                response.url().includes("/bloom/api/pageList/pages") &&
                response.request().method() === "GET"
            );
        });
        await bookList.selectBook("book1");
        await pagesResponse;

        await expect
            .poll(
                async () => {
                    const result = await page.evaluate(() => {
                        const links = Array.from(
                            document.querySelectorAll<HTMLLinkElement>(
                                'link[data-page-chooser-css="true"]',
                            ),
                        );

                        // Debug info helps diagnose why the links are missing when the poll times out
                        return {
                            count: links.length,
                            hrefs: links.map((link) => link.href),
                            innerHTML: document.head?.innerHTML || "",
                        };
                    });

                    console.log("CSS link poll:", result.count, result.hrefs);

                    return result.count;
                },
                { timeout: 10000 },
            )
            .toBeGreaterThanOrEqual(2);

        const hrefPaths = await page.evaluate(() => {
            return Array.from(
                document.querySelectorAll<HTMLLinkElement>(
                    'link[data-page-chooser-css="true"]',
                ),
            ).map((link) => new URL(link.href, window.location.href).pathname);
        });

        expect(new Set(hrefPaths).size).toBe(2);
        expect(hrefPaths).toContain(
            "/bloom/api/pageList/bookFile/book1/styles/core.css",
        );
        expect(hrefPaths).toContain(
            "/bloom/api/pageList/bookFile/book1/nested/folder/print.css",
        );

        const attributeValue = await page.evaluate(() => {
            return document
                .getElementById("wrapperForBodyAttributes")
                ?.getAttribute("data-theme");
        });
        expect(attributeValue).toBe("night");
    });
});
