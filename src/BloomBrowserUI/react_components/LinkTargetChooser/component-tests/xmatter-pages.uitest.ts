import { test, expect } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser } from "./test-helpers";

const frontCoverId = "front-cover-guid";
const contentPageId = "content-1";
const xmatterPageId = "credits";

test.describe("LinkTargetChooser - XMatter pages", () => {
    test("Disables non-cover XMatter pages and keeps selection unchanged", async ({
        page,
    }) => {
        const context = await setupLinkTargetChooser(page, {
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

        await context.bookList.selectBook("book1");

        await expect
            .poll(async () => await context.pageList.isPageSelected("cover"))
            .toBeTruthy();

        const input = await context.urlEditor.getInput();
        await expect(input).toHaveValue("/book/book1");

        await context.pageList.selectPage(contentPageId);
        const isContentSelected =
            await context.pageList.isPageSelected(contentPageId);
        expect(isContentSelected).toBeTruthy();
        await expect(input).toHaveValue(`/book/book1#${contentPageId}`);

        await context.pageList.selectPage(xmatterPageId);
        const isXmatterDisabled =
            await context.pageList.isPageDisabled(xmatterPageId);
        expect(isXmatterDisabled).toBeTruthy();

        const isXmatterSelected =
            await context.pageList.isPageSelected(xmatterPageId);
        expect(isXmatterSelected).toBeFalsy();

        await expect(input).toHaveValue(`/book/book1#${contentPageId}`);
    });
});
