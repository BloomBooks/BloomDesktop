// The triangle on a book's tile in the Collections tab: the tile of the selected book, and only
// that one, shows a triangle at its bottom right corner, and a click on it opens the same menu as a
// right-click on the tile.
//
// Automates the manual test "Thumbnail Triangle Menu" (Test Case ID 386).

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    chooseFromSelectedBookMenuByTestId,
    clickBookTile,
    getBooksShowingMenuTriangle,
    getSelectedBookId,
    listEditableBooks,
    readSelectedBookMenu,
} from "../helpers/collection";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "book-menu-triangle", languages: ["en"] },
});

test("the selected book's triangle opens the same menu as a right-click [Test Case ID 386]", async ({
    page,
    step,
}) => {
    let firstBook = "";
    let secondBook = "";
    await step("Make two books and show the Collections tab", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await makeBookFromTemplate(page, "Basic Book");
        await switchTab(page, "collection");
        const books = await listEditableBooks(page);
        expect(
            books,
            "Sanity check: the collection should hold the two books just made",
        ).toHaveLength(2);
        // Bloom leaves the book made last selected.
        const selected = await getSelectedBookId(page);
        expect(
            books.map((b) => b.id),
            "Sanity check: one of the two books should be the selected one",
        ).toContain(selected);
        secondBook = selected!;
        firstBook = books.find((b) => b.id !== selected)!.id;
    });

    await step("See the triangle on the selected book only", async () => {
        await expect
            .poll(() => getBooksShowingMenuTriangle(page), {
                message: "only the selected book's tile should show a triangle",
            })
            .toEqual([secondBook]);
    });

    await step("Select the other book by clicking its thumbnail", async () => {
        await clickBookTile(page, firstBook);
        await expect
            .poll(() => getBooksShowingMenuTriangle(page), {
                message:
                    "the triangle should move to the tile of the book just selected",
            })
            .toEqual([firstBook]);
    });

    await step(
        "Compare the triangle's menu with the right-click menu",
        async () => {
            const rightClickMenu = await readSelectedBookMenu(
                page,
                "right-click",
            );
            expect(
                rightClickMenu.entries.map((e) => e.testId),
                "Sanity check: the right-click menu should offer its commands",
            ).toContain("CollectionTab.BookMenu.DuplicateBook");
            expect(
                rightClickMenu.more.length,
                "Sanity check: the right-click menu's More submenu should offer commands",
            ).toBeGreaterThan(0);

            const triangleMenu = await readSelectedBookMenu(page, "triangle");
            expect(
                triangleMenu,
                "the triangle should open the same menu as a right-click",
            ).toEqual(rightClickMenu);
        },
    );

    await step("Duplicate the book from the triangle's menu", async () => {
        await chooseFromSelectedBookMenuByTestId(
            page,
            "CollectionTab.BookMenu.DuplicateBook",
            "triangle",
        );
        await expect
            .poll(async () => (await listEditableBooks(page)).length, {
                timeout: 60000,
                message:
                    "Duplicate Book, chosen from the triangle's menu, should add a book",
            })
            .toBe(3);
        const duplicate = (await listEditableBooks(page)).find(
            (b) => b.id !== firstBook && b.id !== secondBook,
        );
        // Bloom selects the duplicate, so the triangle follows it there.
        await expect
            .poll(() => getBooksShowingMenuTriangle(page), {
                timeout: 30000,
                message:
                    "the triangle should move to the duplicate, which Bloom selects",
            })
            .toEqual([duplicate!.id]);
    });
});
