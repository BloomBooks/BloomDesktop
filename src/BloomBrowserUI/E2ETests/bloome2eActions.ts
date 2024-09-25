import { test, expect, Page } from "./connect";

export async function createNewBook(collectionTabPage: Page) {
    // find the first <button> that contains a span with the text "eBook"
    const ebookButton = await collectionTabPage.locator(
        "button:has(span:has-text('eBook'))"
    );
    await ebookButton.click();
    // find the first <button> that contains a span with the text "Make a book using this source"
    const makeBookButton = await collectionTabPage.locator(
        "button:has(span:has-text('Make a book using this source'))"
    );
    await makeBookButton.click();

    // pause for a moment while that gets created
    await collectionTabPage.waitForTimeout(1000);
}

export async function selectCollectionBook(
    collectionTabPage: Page,
    bookTitle: string
) {
    const bookButton = await collectionTabPage
        .locator(`button:has(span:has-text('${bookTitle}'))`)
        .first();
    await bookButton.click();
}

export async function clickEditButton(collectionTabPage: Page) {
    const editButton = await collectionTabPage.locator(
        "button:has(img[src='/bloom/images/EditTab.svg'])"
    );
    await editButton.click();
}

export const templatePageIds = {
    chooseAnImage: "fe7acd9d-c05c-449b-9f99-841d54856924",
    fullPageImage: ""
};
export async function addPage(
    pageList: Page,
    editPageContentsPage: Page,
    templatePageId: string
) {
    // find the first <button> that contains an <img> with src = /bloom/bookEdit/pageThumbnailList/pageControls/addPage.png
    const buttonToBringUpAddPageDialog = await pageList.locator(
        "button:has(img[src='/bloom/bookEdit/pageThumbnailList/pageControls/addPage.png'])"
    );
    await buttonToBringUpAddPageDialog.click();

    // the new page dialog comes up on in the browser containing the page we're editing
    // const orderWordsButton = await editPageContentsPage.locator(
    //     "div:has(img[src*='Words.svg'])"
    // );
    const templateButton = await editPageContentsPage.getByTestId(
        "templatePageThumbnailOverlay_" + templatePageId
    );

    await templateButton.click();
    // pause for a moment to see the result
    await editPageContentsPage.waitForTimeout(2000);
    // find the span that contains the text "Add Page"
    //aka getByRole('button', { name: 'Add Page' })
    const addPageButton = await editPageContentsPage
        .locator("span:has-text('Add Page')")
        .first();

    // click it
    await addPageButton.click();
}
