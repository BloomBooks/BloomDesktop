import { test, expect } from "../../component-tester/playwrightTest";
import { setTestComponent } from "../../component-tester/setTestComponent";

test.describe("ColorPicker", () => {
    test("single swatch click updates hex input", async ({ page }) => {
        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerTestHarness",
            "ColorPickerTestHarness",
            {},
        );

        const hexInput = page.locator('input[type="text"]');
        await expect(hexInput).toHaveValue("#111111");

        await page.locator(".swatch-row .color-swatch").first().click();
        await expect(hexInput).toHaveValue("#AA0000");
    });

    test("eyedropper (native) updates hex input", async ({ page }) => {
        await page.addInitScript(() => {
            (
                window as unknown as Window & {
                    EyeDropper: {
                        new (): { open: () => Promise<{ sRGBHex: string }> };
                    };
                }
            ).EyeDropper = class {
                public async open(): Promise<{ sRGBHex: string }> {
                    return { sRGBHex: "#00AA00" };
                }
            };
        });

        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerTestHarness",
            "ColorPickerTestHarness",
            {},
        );

        const hexInput = page.locator('input[type="text"]');
        await expect(hexInput).toHaveValue("#111111");

        await page.locator('button[title="Sample Color"]').click();
        await expect(hexInput).toHaveValue("#00AA00");
    });

    test("external currentColor change updates hex input", async ({ page }) => {
        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerTestHarness",
            "ColorPickerTestHarness",
            {},
        );

        const hexInput = page.locator('input[type="text"]');
        await expect(hexInput).toHaveValue("#111111");

        await page.getByTestId("simulate-external-color").click();
        await expect(hexInput).toHaveValue("#123456");
    });
});
