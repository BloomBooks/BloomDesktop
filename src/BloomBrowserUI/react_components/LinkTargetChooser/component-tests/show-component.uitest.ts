/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open indefinitely
 * so you can interact with it manually.
 *
 * Run with: ./show.sh, ./show.sh page-only-url, etc.
 */
import { test } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser } from "./test-helpers";

const scrollTestColors = [
    "4CAF50",
    "2196F3",
    "FF9800",
    "E91E63",
    "9C27B0",
    "00BCD4",
    "CDDC39",
    "FF5722",
    "607D8B",
    "795548",
];

const createScrollableBook = (index: number) => {
    const color = scrollTestColors[index % scrollTestColors.length];
    const bookNumber = index + 1;
    const pageCount = bookNumber * 10;

    return {
        id: `book${bookNumber}`,
        title: `Book ${bookNumber}`,
        folderName: `${bookNumber} Book ${bookNumber}`,
        thumbnail: `data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%23${color}'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook ${bookNumber}%3C/text%3E%3C/svg%3E`,
        pageLength: pageCount + 1,
    };
};

const createScrollableBooks = (count: number) =>
    Array.from({ length: count }, (_, index) => createScrollableBook(index));

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "/book/book10#44",
            currentBookBeingEditedId: "book3",
            clipboardText: "https://example.com",
        });

        await page.waitForEvent("close");
    });

    test("page-only-url", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "#5",
            currentBookBeingEditedId: "book3",
        });

        await page.waitForEvent("close");
    });

    test("hash-only-url-with-cover", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "#cover",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    test("missing-book", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "/book/999",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    test("book-path-url-simplified-to-hash", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "/book/book2#7",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=LinkTargetChooserDialog");

        await page.waitForEvent("close");
    });

    test("preselected-page-scroll", async ({ page }) => {
        test.setTimeout(0);

        const books = createScrollableBooks(25);
        const targetBook = books[22];
        const targetPageId = "150";

        await setupLinkTargetChooser(page, {
            currentURL: `/book/${targetBook.id}#${targetPageId}`,
            books,
        });

        await page.waitForEvent("close");
    });
});
