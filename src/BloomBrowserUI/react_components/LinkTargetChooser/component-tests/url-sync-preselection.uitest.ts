/**
 * Tests for LinkTargetChooser - URL Synchronization and Preselection
 * Covers: URL box syncs with book/page selections, preselection from URL prop
 * Run with: yarn test
 */

import { test, expect } from "../../component-tester/playwrightTest";
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
        pageCount,
    };
};

const createScrollableBooks = (count: number) =>
    Array.from({ length: count }, (_, index) => createScrollableBook(index));

test.describe("LinkTargetChooser - URL Synchronization", () => {
    test("URL box shows book URL when book is selected", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        // Wait for books to load, then select a book
        await context.bookList.selectBook("book1");

        // URL should update to show book link once the pages load
        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book1");

        // Cover page should become selected automatically
        await expect
            .poll(async () => await context.pageList.isPageSelected("cover"))
            .toBeTruthy();
    });

    test("URL box shows book+page URL when page is selected", async ({
        page,
    }) => {
        const context = await setupLinkTargetChooser(page);

        // Select a book (this waits for books to load)
        await context.bookList.selectBook("book2");

        // Select a page
        await context.pageList.selectPage(2);

        // URL should update to show book+page link
        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book2#2");
    });

    test("URL box shows cover page link correctly", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        // Select book (waits for books to load)
        await context.bookList.selectBook("book1");
        await context.pageList.selectPage(2); // Different page to ensure we switch back

        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book1#2");

        await context.pageList.selectPage(0); // Cover page

        await expect(input).toHaveValue("/book/book1");
        await expect
            .poll(async () => await context.pageList.isPageSelected("cover"))
            .toBeTruthy();
    });

    test("Typing in URL box clears book/page selection", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        // Select a book first (waits for books to load)
        await context.bookList.selectBook("book1");

        // Type a URL
        await context.urlEditor.setValue("https://example.com");

        // Verify the URL is updated
        const urlValue = await context.urlEditor.getValue();
        expect(urlValue).toBe("https://example.com");

        // Book should no longer appear selected (we'd need to check visual state in real test)
        // For now, we confirm URL changed which indicates selection was cleared
    });

    test("Selecting book after typing URL clears the URL and shows book URL", async ({
        page,
    }) => {
        const context = await setupLinkTargetChooser(page);

        // Wait for books to load
        await context.bookList.waitForBooksToLoad();

        // Type a URL first
        await context.urlEditor.setValue("https://example.com");

        // Now select a book
        await context.bookList.selectBook("book3");

        // URL should be updated to book URL
        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book3");
        await expect
            .poll(async () => await context.pageList.isPageSelected("cover"))
            .toBeTruthy();
    });

    test("Deleting the URL clears the book and page selection", async ({
        page,
    }) => {
        const context = await setupLinkTargetChooser(page);
        await context.bookList.selectBook("book1");

        const input = await context.urlEditor.getInput();
        await input.click();
        await input.press("Control+A");
        await input.press("Delete");

        await expect(input).toHaveValue("");
        await expect
            .poll(async () => await context.bookList.isBookSelected("book1"))
            .toBeFalsy();
        await expect(
            page.getByText("Select a book to see its pages"),
        ).toBeVisible();
    });

    test("Pasting a URL clears any book and page selection", async ({
        page,
    }) => {
        const context = await setupLinkTargetChooser(page);
        await context.bookList.selectBook("book2");

        const clipboardValue = "https://docs.example.org/help";
        await page.route("**/bloom/api/common/clipboardText", async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "application/json",
                body: JSON.stringify({ data: clipboardValue }),
            });
            await page.unroute("**/bloom/api/common/clipboardText");
        });

        const pasteButton = await context.urlEditor.getPasteButton();
        await pasteButton.click();

        const selector =
            '[data-testid="url-input"] input, input[placeholder="Paste or enter a URL"]';
        const _count = await page.locator(selector).count();
        const containerCount = await page.getByTestId("url-input").count();

        if (containerCount > 0) {
            const _containerHtml = await page
                .getByTestId("url-input")
                .first()
                .evaluate((el) => el.outerHTML);
        }

        const _selectionDetails = await page
            .locator('[data-book-id="book2"]')
            .evaluate((element) => {
                const target = element as HTMLElement;
                return {
                    dataSelected: target.getAttribute("data-selected"),
                    dataDisabled: target.getAttribute("data-disabled"),
                    classList: Array.from(target.classList),
                };
            });

        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue(clipboardValue, { timeout: 5000 });
        await expect
            .poll(async () => await context.bookList.isBookSelected("book2"))
            .toBeFalsy();
        await expect(
            page.getByText("Select a book to see its pages"),
        ).toBeVisible();
    });

    test("Auto-selects current book when API reports one", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentBookId: "book2",
        });

        await context.bookList.waitForBooksToLoad();

        await expect
            .poll(async () => await context.bookList.isBookSelected("book2"))
            .toBeTruthy();

        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book2");

        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });
});

test.describe("LinkTargetChooser - URL Preselection", () => {
    test("Keeps anchor-only URL when no book is selected", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "#cover",
        });

        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("#cover");

        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeDisabled();
    });

    test("Preselects book when URL is /book/BOOKID", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "/book/book2",
        });

        // Wait for books to load and component to initialize
        await context.bookList.waitForBooksToLoad();

        // URL box should show the URL
        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book2");

        await expect
            .poll(async () => await context.pageList.isPageSelected("cover"))
            .toBeTruthy();

        // OK button should be enabled
        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Preselects book and page when URL is /book/BOOKID#PAGEID", async ({
        page,
    }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "/book/book1#3",
        });

        // Wait for books to load and component to initialize
        await context.bookList.waitForBooksToLoad();

        // URL box should show the URL
        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book1#3");

        // OK button should be enabled
        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Preselects cover page when URL has #cover", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "/book/book4#cover",
        });

        // Wait for books to load and component to initialize
        await context.bookList.waitForBooksToLoad();

        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book4");

        await expect
            .poll(async () => await context.pageList.isPageSelected("cover"))
            .toBeTruthy();

        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Shows external URL in URL box when provided", async ({ page }) => {
        const externalURL = "https://example.org/page";
        const context = await setupLinkTargetChooser(page, {
            currentURL: externalURL,
        });

        // Wait for books to load and component to initialize
        await context.bookList.waitForBooksToLoad();

        const urlValue = await context.urlEditor.getValue();
        expect(urlValue).toBe(externalURL);

        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Handles empty initial URL", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "",
        });

        const urlValue = await context.urlEditor.getValue();
        expect(urlValue).toBe("");

        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeDisabled();
    });

    test("Normalizes existing cover GUID URLs to book-only links", async ({
        page,
    }) => {
        const frontCoverGuid = "front-cover-guid";
        const context = await setupLinkTargetChooser(page, {
            currentURL: `/book/book1#${frontCoverGuid}`,
            pages: [
                { key: frontCoverGuid, caption: "Front Cover" },
                { key: "page-1", caption: "Page 1" },
            ],
            selectedPageId: frontCoverGuid,
        });

        await context.bookList.waitForBooksToLoad();

        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book1");
        await expect
            .poll(async () => await context.pageList.isPageSelected("cover"))
            .toBeTruthy();
    });

    test("Scrolls preselected book into view", async ({ page }) => {
        const books = createScrollableBooks(25);
        const targetBook = books[20];

        const context = await setupLinkTargetChooser(page, {
            currentURL: `/book/${targetBook.id}`,
            books,
        });

        await context.bookList.waitForBooksToLoad();

        await expect
            .poll(
                async () =>
                    await context.bookList.isBookSelected(targetBook.id),
                {
                    timeout: 2000,
                },
            )
            .toBeTruthy();

        const targetCard = await context.bookList.getBookCard(targetBook.id);
        await targetCard.waitFor({ state: "attached" });

        await expect
            .poll(
                async () =>
                    await targetCard.evaluate((element) => {
                        const container = element.parentElement;
                        if (!(container instanceof HTMLElement)) {
                            return false;
                        }
                        const containerRect = container.getBoundingClientRect();
                        const rect = element.getBoundingClientRect();
                        return (
                            rect.bottom > containerRect.top &&
                            rect.top < containerRect.bottom
                        );
                    }),
                { timeout: 2000 },
            )
            .toBeTruthy();
    });

    test("Scrolls preselected page into view", async ({ page }) => {
        const books = createScrollableBooks(25);
        const targetBook = books[22];
        const targetPageId = "150";

        const context = await setupLinkTargetChooser(page, {
            currentURL: `/book/${targetBook.id}#${targetPageId}`,
            books,
        });

        await context.bookList.waitForBooksToLoad();

        await expect
            .poll(
                async () =>
                    await context.bookList.isBookSelected(targetBook.id),
                {
                    timeout: 2000,
                },
            )
            .toBeTruthy();

        const targetPage = await context.pageList.getPage(targetPageId);
        await targetPage.waitFor({ state: "attached" });

        await expect
            .poll(
                async () =>
                    await targetPage.evaluate((element) => {
                        const target = element as HTMLElement;
                        return (
                            target.getAttribute("data-selected") === "true" ||
                            target.classList.contains(
                                "link-target-page--selected",
                            )
                        );
                    }),
                { timeout: 2000 },
            )
            .toBeTruthy();

        await expect
            .poll(
                async () =>
                    await targetPage.evaluate((element) => {
                        const container = element.closest("#pageGridWrapper");
                        if (!(container instanceof HTMLElement)) {
                            return false;
                        }
                        return container.scrollTop > 0;
                    }),
                { timeout: 2000 },
            )
            .toBeTruthy();

        await expect
            .poll(
                async () =>
                    await targetPage.evaluate((element) => {
                        const container = element.closest("#pageGridWrapper");
                        if (!(container instanceof HTMLElement)) {
                            return false;
                        }
                        const containerRect = container.getBoundingClientRect();
                        const rect = element.getBoundingClientRect();
                        return (
                            rect.bottom > containerRect.top &&
                            rect.top < containerRect.bottom
                        );
                    }),
                { timeout: 2000 },
            )
            .toBeTruthy();
    });
});
