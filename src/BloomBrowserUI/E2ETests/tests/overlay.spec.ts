import { test, expect } from "../connect";
import { addPage, createNewBook, templatePageIds } from "../bloome2eActions";

test("connection test", async ({ collectionPage }) => {
    await expect(collectionPage.locator("div#reactRoot")).toBeVisible();
});
test("test making a game", async ({
    collectionPage,
    editPageListPage,
    editPageContentsPage
}) => {
    await createNewBook(collectionPage);
    await addPage(
        editPageListPage,
        editPageContentsPage,
        templatePageIds.chooseAnImage
    );
});
