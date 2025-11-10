import { test, expect } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser } from "./test-helpers";

test.describe("LinkTargetChooserDialog - OK Button Behavior", () => {
    test("OK button is disabled when dialog first opens", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        const okButton = await context.dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(true);
    });

    test("OK button becomes enabled after typing URL", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        const input = await context.urlEditor.getInput();
        await input.fill("https://example.com");

        const okButton = await context.dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(false);
    });

    test("OK button becomes enabled after selecting book", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        await context.bookList.selectBook("book1");

        const okButton = await context.dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(false);
    });

    test("OK button is disabled for empty URL", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        const input = await context.urlEditor.getInput();
        await input.fill("   "); // Just whitespace

        const okButton = await context.dialog.getOKButton();
        const isDisabled = await okButton.isDisabled();
        expect(isDisabled).toBe(true);
    });

    test("OK button is disabled when there is an error", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "/book/missing-book",
        });

        const okButton = await context.dialog.getOKButton();
        await expect(okButton).toBeDisabled();
    });

    test("Close button is always enabled", async ({ page }) => {
        const context = await setupLinkTargetChooser(page);

        const closeButton = await context.dialog.getCloseButton();
        const isDisabled = await closeButton.isDisabled();
        expect(isDisabled).toBe(false);
    });

    test("Dialog displays title", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const title = page.getByText("Choose Link Target");
        await expect(title).toBeVisible();
    });
});
