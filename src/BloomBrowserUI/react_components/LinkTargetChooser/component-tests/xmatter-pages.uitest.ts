import { test, expect } from "../../component-tester/playwrightTest";
import {
    setupLinkTargetChooser,
    bookList,
    pageList,
    urlEditor,
} from "./test-helpers";

const frontCoverId = "front-cover-guid";
const contentPageId = "content-1";
const xmatterPageId = "credits";

test.describe("LinkTargetChooser - XMatter pages", () => {
    test("Disables non-cover XMatter pages and keeps selection unchanged", async ({
        page,
    }) => {
        await setupLinkTargetChooser(page, {
            pages: [
                {
                    key: frontCoverId,
                    caption: "Front Cover",
                    isXMatter: true,
                },
                { key: contentPageId, caption: "Page 1" },
                {
                    key: xmatterPageId,
                    caption: "Credits",
                    isXMatter: true,
                },
            ],
        });

        await bookList.selectBook("book1");

        await expect
            .poll(async () => await pageList.isPageSelected("cover"))
            .toBeTruthy();

        const input = await urlEditor.getInput();
        await expect(input).toHaveValue("/book/book1");

        await pageList.selectPage(contentPageId);
        const isContentSelected = await pageList.isPageSelected(contentPageId);
        expect(isContentSelected).toBeTruthy();
        await expect(input).toHaveValue(`/book/book1#${contentPageId}`);

        await pageList.selectPage(xmatterPageId);
        const isXmatterDisabled = await pageList.isPageDisabled(xmatterPageId);
        expect(isXmatterDisabled).toBeTruthy();

        const isXmatterSelected = await pageList.isPageSelected(xmatterPageId);
        expect(isXmatterSelected).toBeFalsy();

        await expect(input).toHaveValue(`/book/book1#${contentPageId}`);
    });
});
