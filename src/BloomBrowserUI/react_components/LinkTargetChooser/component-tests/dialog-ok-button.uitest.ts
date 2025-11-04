import { test, expect } from "../../component-tester/playwrightTest";
import {
    setupLinkTargetChooser,
    dialog,
    urlEditor,
    bookList,
} from "./test-helpers";

test.describe("LinkTargetChooserDialog - OK Button Behavior", () => {
    test("OK button is disabled when dialog first opens", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const okButton = await dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(true);
    });

    test("OK button becomes enabled after typing URL", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const input = await urlEditor.getInput();
        await input.fill("https://example.com");

        const okButton = await dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(false);
    });

    test("OK button becomes enabled after selecting book", async ({ page }) => {
        await setupLinkTargetChooser(page);

        await bookList.selectBook("book1");

        const okButton = await dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(false);
    });

    test("OK button is disabled for empty URL", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const input = await urlEditor.getInput();
        await input.fill("   "); // Just whitespace

        const okButton = await dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(true);
    });

    test("OK button is disabled when there is an error", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/missing-book",
        });

        // Wait for error to process
        await page.waitForTimeout(100);

        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeDisabled();
    });

    test("Close button is always enabled", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const closeButton = await dialog.getCloseButton();
        const isDisabled = await closeButton.isDisabled();
        expect(isDisabled).toBe(false);
    });

    test("Dialog displays title", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const title = page.getByText("Choose Link Target");
        await expect(title).toBeVisible();
    });
});
