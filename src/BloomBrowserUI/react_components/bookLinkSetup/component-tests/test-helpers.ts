import { Page, expect } from "../../component-tester/playwrightTest";
import { setTestComponent } from "../../component-tester/setTestComponent";
import type { IBookGridSetupProps } from "./component-tester.config";
import type { BookInfoForLinks, Link } from "../BookLinkTypes";
import {
    preparePostReceiver,
    PostReceiver,
} from "../../component-tester/apiInterceptors";

/**
 * Sets up the BookGridSetup component with the given props
 * Returns a receiver that can capture the links when they change
 */
export async function setupBookGridSetupComponent(
    page: Page,
    props?: Partial<IBookGridSetupProps>,
): Promise<PostReceiver<Link[]>> {
    // Prepare to intercept the onLinksChanged POST
    const receiver = preparePostReceiver<Link[]>(
        page,
        "**/testapi/bookGridSetup/linksChanged",
    );

    const fullProps: IBookGridSetupProps = {
        sourceBooks: props?.sourceBooks || [],
        links: props?.links || [],
        onLinksChanged:
            props?.onLinksChanged || "testapi/bookGridSetup/linksChanged",
    };

    await setTestComponent<IBookGridSetupProps>(
        page,
        "../bookLinkSetup/BookGridSetup",
        "BookGridSetup",
        fullProps,
    );

    return receiver;
}

/**
 * Helper to create test book data
 */
export function createTestBook(id: string, title: string): BookInfoForLinks {
    return {
        id,
        title,
        folderName: title, // Use title as folderName so cards show labels in both source and target lists
        thumbnail:
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
    };
}

/**
 * Gets the "Add Book" button
 */
export async function getAddBookButton(page: Page) {
    return page.getByRole("button", { name: /add book/i });
}

/**
 * Gets the "Add All Books" button
 */
export async function getAddAllBooksButton(page: Page) {
    return page.getByRole("button", { name: /add all books/i });
}

/**
 * Gets a book item from the source list by its id
 */
export async function getSourceBookById(page: Page, id: string) {
    return page.getByTestId(`source-book-${id}`);
}

/**
 * Gets a book item from the target list by its title
 */
export async function getTargetBookById(page: Page, bookId: string) {
    return page.getByTestId(`target-book-${bookId}`);
}

/**
 * Clicks on a source book to select it
 */
export async function selectSourceBook(page: Page, id: string) {
    const book = await getSourceBookById(page, id);
    await book.click();
}

/**
 * Gets the remove button for a target book
 * Note: The button is hidden by CSS (display: none) until hover,
 * but Playwright can still interact with it using force: true
 */
export async function getRemoveButton(page: Page, bookId: string) {
    const targetBook = await getTargetBookById(page, bookId);
    return targetBook.getByTestId("remove-book-button");
}

/**
 * Verifies that a book appears in the source list
 */
export async function expectSourceBookVisible(page: Page, id: string) {
    const book = await getSourceBookById(page, id);
    await expect(book).toBeVisible();
}

/**
 * Verifies that a book appears in the target list
 */
export async function expectTargetBookVisible(page: Page, bookId: string) {
    const book = await getTargetBookById(page, bookId);
    await expect(book).toBeVisible();
}

/**
 * Verifies that a book does not appear in the target list
 */
export async function expectTargetBookNotVisible(page: Page, bookId: string) {
    const book = page.getByTestId(`target-book-${bookId}`);
    await expect(book).not.toBeVisible();
}

/**
 * Gets the count of books in the target list from the header
 */
export async function getTargetCount(page: Page): Promise<number> {
    const header = page.getByRole("heading", { name: /links in grid/i });
    const text = await header.textContent();
    const match = text?.match(/\((\d+)\)/);
    return match ? parseInt(match[1], 10) : 0;
}
