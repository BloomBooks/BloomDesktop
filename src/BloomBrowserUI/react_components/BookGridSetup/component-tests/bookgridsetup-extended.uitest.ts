/**
 * Extended Tests for BookGridSetup
 * These tests cover advanced functionality like Add All Books, Remove, Disabled states, etc.
 * Run with: yarn test
 */

import { expect, test } from "../../component-tester/playwrightTest";
import type { BookInfoForLinks } from "../BookLinkTypes";
import {
    setupBookGridSetupComponent,
    createTestBook,
    getAddBookButton,
    getAddAllBooksButton,
    expectTargetBookVisible,
    expectTargetBookNotVisible,
    getTargetCount,
    selectSourceBook,
    getRemoveButton,
    getSourceBookById,
    getTargetBookById,
} from "./test-helpers";

test.describe("BookGridSetup - Add All Books", () => {
    test("Can add all books at once", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        const [moonBook, countingFun, animalFriends] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Click Add All Books button
        const addAllButton = await getAddAllBooksButton(page);
        await addAllButton.click();

        // Verify all books appear in the target list
        await expectTargetBookVisible(page, moonBook.id);
        await expectTargetBookVisible(page, countingFun.id);
        await expectTargetBookVisible(page, animalFriends.id);

        // Verify the count is updated
        const count = await getTargetCount(page);
        expect(count).toBe(3);

        // Verify onLinksChanged was called with all books
        const links = await receiver.getData();
        expect(links).toHaveLength(3);
        expect(links[0].book.id).toBe("book1");
        expect(links[1].book.id).toBe("book2");
        expect(links[2].book.id).toBe("book3");
    });

    test("Add All Books is disabled when all books are already added", async ({
        page,
    }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
        ];

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Add all books
        const addAllButton = await getAddAllBooksButton(page);
        await addAllButton.click();

        // Verify button is now disabled
        await expect(addAllButton).toBeDisabled();
    });

    test("Add All Books only adds remaining books", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        const [moonBook, countingFun, animalFriends] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: testBooks[0] }], // First book already added
        });

        // Click Add All Books button
        const addAllButton = await getAddAllBooksButton(page);
        await addAllButton.click();

        // Verify all books are now in the target list
        await expectTargetBookVisible(page, moonBook.id);
        await expectTargetBookVisible(page, countingFun.id);
        await expectTargetBookVisible(page, animalFriends.id);

        // Verify the count is 3
        const count = await getTargetCount(page);
        expect(count).toBe(3);

        // Verify onLinksChanged was called with all 3 books
        const links = await receiver.getData();
        expect(links).toHaveLength(3);
    });

    test("Add All Books clears any current selection", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Pre-select a book to verify the selection is cleared after adding all books.
        await selectSourceBook(page, testBooks[1].id);

        const addAllButton = await getAddAllBooksButton(page);
        await addAllButton.click();

        const addButton = await getAddBookButton(page);
        await expect(addButton).toBeDisabled();

        const selectedCardCount = await page
            .locator('[data-testid^="source-book-"] .MuiCard-root')
            .evaluateAll((elements) => {
                return elements.filter((element) => {
                    const styles = window.getComputedStyle(
                        element as HTMLElement,
                    );
                    return (
                        styles.outlineStyle !== "none" &&
                        styles.outlineWidth !== "0px"
                    );
                }).length;
            });

        expect(selectedCardCount).toBe(0);
    });
});

test.describe("BookGridSetup - Remove Books", () => {
    test("Can remove a book from the target list", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
        ];

        const [moonBook, countingFun] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: testBooks[0] }, { book: testBooks[1] }],
        });

        // Remove the first book
        // Hover over the target book to show the remove button
        const targetBook1 = await getTargetBookById(page, moonBook.id);
        await targetBook1.hover();
        const removeButton = await getRemoveButton(page, moonBook.id);
        await removeButton.click();

        // Verify the book is no longer in the target list
        await expectTargetBookNotVisible(page, moonBook.id);

        // Verify the other book is still there
        await expectTargetBookVisible(page, countingFun.id);

        // Verify the count is updated
        const count = await getTargetCount(page);
        expect(count).toBe(1);

        // Verify onLinksChanged was called with updated list
        const links = await receiver.getData();
        expect(links).toHaveLength(1);
        expect(links[0].book.id).toBe("book2");
    });

    test("Can remove all books one by one", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
        ];

        const [moonBook, countingFun] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: testBooks[0] }, { book: testBooks[1] }],
        });

        // Remove both books
        // Hover over first target book to show the remove button
        const targetBook1 = await getTargetBookById(page, moonBook.id);
        await targetBook1.hover();
        const removeButton1 = await getRemoveButton(page, moonBook.id);
        await removeButton1.click();

        // Hover over second target book to show the remove button
        const targetBook2 = await getTargetBookById(page, countingFun.id);
        await targetBook2.hover();
        const removeButton2 = await getRemoveButton(page, countingFun.id);
        await removeButton2.click();

        // Verify both books are removed
        await expectTargetBookNotVisible(page, moonBook.id);
        await expectTargetBookNotVisible(page, countingFun.id);

        // Verify the count is 0
        const count = await getTargetCount(page);
        expect(count).toBe(0);

        // Verify onLinksChanged was called with empty list
        const links = await receiver.getData();
        expect(links).toHaveLength(0);
    });

    test("After removing a book, it can be added again", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
        ];

        const [moonBook] = testBooks;

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: testBooks[0] }],
        });

        // Remove the book
        // Hover over the target book to show the remove button
        const targetBook = await getTargetBookById(page, moonBook.id);
        await targetBook.hover();
        const removeButton = await getRemoveButton(page, moonBook.id);
        await removeButton.click();

        // Verify it's removed
        await expectTargetBookNotVisible(page, moonBook.id);

        // Select and add it again
        await selectSourceBook(page, moonBook.id);
        const addButton = await getAddBookButton(page);
        await addButton.click();

        // Verify it's back in the target list
        await expectTargetBookVisible(page, moonBook.id);
    });
});

test.describe("BookGridSetup - Disabled States", () => {
    test("Add Book button is disabled when no book is selected", async ({
        page,
    }) => {
        const testBooks = [createTestBook("book1", "The Moon Book")];

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Verify Add Book button is disabled without selection
        const addButton = await getAddBookButton(page);
        await expect(addButton).toBeDisabled();
    });

    test("Add Book button is disabled when selected book is already added", async ({
        page,
    }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
        ];

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: testBooks[0] }],
        });

        // Select the already-added book
        await selectSourceBook(page, testBooks[0].id);

        // Verify Add Book button is disabled
        const addButton = await getAddBookButton(page);
        await expect(addButton).toBeDisabled();
    });

    test("Books already in target list are shown as disabled in source list", async ({
        page,
    }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
        ];

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: testBooks[0] }],
        });

        // Get the source book element
        const sourceBook = await getSourceBookById(page, testBooks[0].id);

        // Verify it has the disabled attribute or class (this depends on how BookLinkCard implements disabled state)
        // The BookLinkCard receives a disabled prop and should render accordingly
        await expect(sourceBook).toBeVisible();
        // We can check if clicking it does nothing by verifying selection doesn't change
        await sourceBook.click();
        const addButton = await getAddBookButton(page);
        await expect(addButton).toBeDisabled(); // Should remain disabled as no valid selection
    });
});

test.describe("BookGridSetup - Multiple Books Management", () => {
    test("Can add multiple books sequentially", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        const [moonBook, countingFun, animalFriends] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Add first book
        await selectSourceBook(page, moonBook.id);
        const addButton = await getAddBookButton(page);
        await addButton.click();

        // Add second book
        await selectSourceBook(page, countingFun.id);
        await addButton.click();

        // Add third book
        await selectSourceBook(page, animalFriends.id);
        await addButton.click();

        // Verify all books are in target list
        await expectTargetBookVisible(page, moonBook.id);
        await expectTargetBookVisible(page, countingFun.id);
        await expectTargetBookVisible(page, animalFriends.id);

        // Verify the count
        const count = await getTargetCount(page);
        expect(count).toBe(3);

        // Verify final state
        const links = await receiver.getData();
        expect(links).toHaveLength(3);
    });

    test("After adding a book, the next available book is automatically selected", async ({
        page,
    }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        const [, countingFun] = testBooks;

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Select and add the first book
        await selectSourceBook(page, testBooks[0].id);
        const addButton = await getAddBookButton(page);
        await addButton.click();

        // Verify the next book (Counting Fun) is automatically selected
        // We can check this by verifying the Add Book button is enabled
        await expect(addButton).toBeEnabled();

        // Add it without manually selecting
        await addButton.click();

        // Verify it was added
        await expectTargetBookVisible(page, countingFun.id);
    });

    test("Auto-advance skips books already in the target list", async ({
        page,
    }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        const [, prelinkedBook, finalBook] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: prelinkedBook }],
        });

        await selectSourceBook(page, testBooks[0].id);
        const addButton = await getAddBookButton(page);
        await addButton.click();

        // The selection should advance past the prelinked book to the remaining option.
        await expect(addButton).toBeEnabled();

        await addButton.click();

        await expectTargetBookVisible(page, finalBook.id);
        const count = await getTargetCount(page);
        expect(count).toBe(3);

        const links = await receiver.getData();
        expect(links).toHaveLength(3);
        expect(links[2].book.id).toBe(finalBook.id);
    });
});

test.describe("BookGridSetup - Edge Cases", () => {
    test("Works with empty source books list", async ({ page }) => {
        await setupBookGridSetupComponent(page, {
            sourceBooks: [],
            links: [],
        });

        // Verify Add Book button is disabled
        const addButton = await getAddBookButton(page);
        await expect(addButton).toBeDisabled();

        // Verify Add All Books button is disabled
        const addAllButton = await getAddAllBooksButton(page);
        await expect(addAllButton).toBeDisabled();

        // Verify count is 0
        const count = await getTargetCount(page);
        expect(count).toBe(0);
    });

    test("Works with single book", async ({ page }) => {
        const testBooks = [createTestBook("book1", "The Moon Book")];

        const [moonBook] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Select and add the book
        await selectSourceBook(page, moonBook.id);
        const addButton = await getAddBookButton(page);
        await addButton.click();

        // Verify it was added
        await expectTargetBookVisible(page, moonBook.id);

        // Verify the count
        const count = await getTargetCount(page);
        expect(count).toBe(1);

        // Verify Add Book button is now disabled (no more books)
        await expect(addButton).toBeDisabled();

        // Verify onLinksChanged was called
        const links = await receiver.getData();
        expect(links).toHaveLength(1);
    });

    test("Can start with some books already in target list", async ({
        page,
    }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        const [moonBook, countingFun, animalFriends] = testBooks;

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [{ book: testBooks[0] }, { book: testBooks[2] }],
        });

        // Verify pre-added books are visible
        await expectTargetBookVisible(page, moonBook.id);
        await expectTargetBookVisible(page, animalFriends.id);

        // Verify the count
        const count = await getTargetCount(page);
        expect(count).toBe(2);

        // Verify we can still add the remaining book
        await selectSourceBook(page, countingFun.id);
        const addButton = await getAddBookButton(page);
        await addButton.click();

        await expectTargetBookVisible(page, countingFun.id);
    });

    test("Source list shows folder name, target list shows title, with tooltips", async ({
        page,
    }) => {
        // Create a book where folder name and title differ
        const testBook: BookInfoForLinks = {
            id: "book1",
            title: "The Amazing Moon Book",
            folderName: "moon-book-folder",
            thumbnail:
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
        };

        await setupBookGridSetupComponent(page, {
            sourceBooks: [testBook],
            links: [],
        });

        // Source list should show the folder name
        const sourceBook = page.getByTestId(`source-book-${testBook.id}`);
        await expect(sourceBook).toBeVisible();
        const sourceText = await sourceBook.textContent();
        expect(sourceText).toContain("moon-book-folder");

        // Hover over source book to check tooltip shows title
        await sourceBook.hover();
        const sourceTooltip = page.getByRole("tooltip");
        await expect(sourceTooltip).toBeVisible();
        await expect(sourceTooltip).toHaveText("The Amazing Moon Book");

        // Add the book to target list
        await sourceBook.click();
        const addButton = await getAddBookButton(page);
        await addButton.click();

        // Target list should show the title
        const targetBook = await getTargetBookById(page, testBook.id);
        await expect(targetBook).toBeVisible();
        const targetText = await targetBook.textContent();
        expect(targetText).toContain("The Amazing Moon Book");

        // Hover over target book to check tooltip shows folder name
        await targetBook.hover();
        const targetTooltip = page.getByRole("tooltip");
        await expect(targetTooltip).toBeVisible();
        await expect(targetTooltip).toHaveText("moon-book-folder");
    });
});
