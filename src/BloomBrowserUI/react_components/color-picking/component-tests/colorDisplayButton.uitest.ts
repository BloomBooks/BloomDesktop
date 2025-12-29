import { test, expect } from "../../component-tester/playwrightTest";
import { setTestComponent } from "../../component-tester/setTestComponent";

test.describe("ColorDisplayButton + ColorPickerDialog", () => {
    test("single swatch click updates hex input in dialog", async ({
        page,
    }) => {
        await page.route(
            "**/settings/getCustomPaletteColors?palette=*",
            (route) =>
                route.fulfill({
                    status: 200,
                    contentType: "application/json",
                    body: "[]",
                }),
        );

        await setTestComponent(
            page,
            "../color-picking/component-tests/colorDisplayButtonTestHarness",
            "ColorDisplayButtonTestHarness",
            {},
        );

        await page.getByTestId("color-display-button-swatch").click();

        const dialog = page.getByRole("dialog");
        await expect(dialog).toBeVisible();

        const hexInput = dialog.locator('input[type="text"]');
        await expect(hexInput).toHaveValue("#111111");

        await dialog.locator(".swatch-row .color-swatch").first().click();
        await expect(hexInput).not.toHaveValue("#111111");
    });
});
