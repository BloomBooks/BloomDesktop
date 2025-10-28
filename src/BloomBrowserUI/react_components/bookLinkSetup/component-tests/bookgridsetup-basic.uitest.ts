/**
 * Tests for BookGridSetup - Initial Setup Tests
 * These tests verify that the component can be rendered and basic interaction works
 * Run with: yarn test
 */

import { expect, test } from "../../component-tester/playwrightTest";
import {
    setupBookGridSetupComponent,
    createTestBook,
    getAddBookButton,
    expectSourceBookVisible,
    expectTargetBookVisible,
    getTargetCount,
} from "./test-helpers";

test.describe("BookGridSetup - Initial Setup", () => {
    test("Component renders with source books", async ({ page }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
        ];

        await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Verify all source books are visible
        await expectSourceBookVisible(page, "book1");
        await expectSourceBookVisible(page, "book2");
        await expectSourceBookVisible(page, "book3");
    });

    test("Can select a book and add it to the target list", async ({
        page,
    }) => {
        const testBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
        ];

        const [moonBook] = testBooks;

        const receiver = await setupBookGridSetupComponent(page, {
            sourceBooks: testBooks,
            links: [],
        });

        // Select the first book
        const firstBook = page.getByTestId(`source-book-${moonBook.id}`);
        await firstBook.click();

        // Click Add Book button
        const addButton = await getAddBookButton(page);
        await addButton.click();

        // Verify the book appears in the target list
        await expectTargetBookVisible(page, moonBook.id);

        // Verify the count is updated
        const count = await getTargetCount(page);
        expect(count).toBe(1);

        // Verify onLinksChanged was called with the correct data
        const links = await receiver.getData();
        expect(links).toHaveLength(1);
        expect(links[0].book.id).toBe("book1");
        expect(links[0].book.title).toBe("The Moon Book");
    });
});
