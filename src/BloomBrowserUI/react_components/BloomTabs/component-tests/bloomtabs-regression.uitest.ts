import { expect, test } from "../../component-tester/playwrightTest";

test.describe("BloomTabs regression", () => {
    test("shows only selected panel and switches on tab click", async ({
        page,
    }) => {
        await page.goto("/?component=BloomTabsTestHarness");

        const previewContent = page.getByTestId("preview-content");
        const historyContent = page.getByTestId("history-content");

        await expect(previewContent).toBeVisible();
        await expect(historyContent).not.toBeVisible();

        await page.getByRole("tab", { name: "History" }).click();

        await expect(historyContent).toBeVisible();
        await expect(previewContent).not.toBeVisible();
    });
});
