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

    test("dragging lightness slider changes color", async ({ page }) => {
        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerTestHarness",
            "ColorPickerTestHarness",
            {},
        );

        const hexInput = page.locator('input[type="text"]');
        const initialHex = await hexInput.inputValue();

        const lightnessSlider = page.getByTestId("oklch-slider-L");
        const sliderBox = await lightnessSlider.boundingBox();
        expect(sliderBox).not.toBeNull();

        // Drag from left to right on the slider
        await page.mouse.move(
            sliderBox!.x + 10,
            sliderBox!.y + sliderBox!.height / 2,
        );
        await page.mouse.down();
        await page.mouse.move(
            sliderBox!.x + sliderBox!.width - 10,
            sliderBox!.y + sliderBox!.height / 2,
        );
        await page.mouse.up();

        // Verify the color changed
        const finalHex = await hexInput.inputValue();
        expect(finalHex).not.toBe(initialHex);
    });

    test("dragging 2D picker changes color", async ({ page }) => {
        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerTestHarness",
            "ColorPickerTestHarness",
            {},
        );

        const hexInput = page.locator('input[type="text"]');
        const initialHex = await hexInput.inputValue();

        const picker = page.getByTestId("oklch-2d-picker");
        const pickerBox = await picker.boundingBox();
        expect(pickerBox).not.toBeNull();

        // Drag from center to a different location
        await page.mouse.move(
            pickerBox!.x + pickerBox!.width / 2,
            pickerBox!.y + pickerBox!.height / 2,
        );
        await page.mouse.down();
        await page.mouse.move(
            pickerBox!.x + pickerBox!.width * 0.8,
            pickerBox!.y + pickerBox!.height * 0.2,
        );
        await page.mouse.up();

        // Verify the color changed
        const finalHex = await hexInput.inputValue();
        expect(finalHex).not.toBe(initialHex);
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
