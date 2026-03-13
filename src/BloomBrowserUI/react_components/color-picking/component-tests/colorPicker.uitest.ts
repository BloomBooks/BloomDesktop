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
        await expect(hexInput).toHaveValue("#111111FF");

        await page.locator(".swatch-row .color-swatch").first().click();
        await expect(hexInput).toHaveValue("#AA0000FF");
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
        await expect(hexInput).toHaveValue("#111111FF");

        await page.locator('button[title="Sample Color"]').click();
        await expect(hexInput).toHaveValue("#00AA00FF");
    });

    test("external currentColor change updates hex input", async ({ page }) => {
        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerTestHarness",
            "ColorPickerTestHarness",
            {},
        );

        const hexInput = page.locator('input[type="text"]');
        await expect(hexInput).toHaveValue("#111111FF");

        await page.getByTestId("simulate-external-color").click();
        await expect(hexInput).toHaveValue("#123456FF");
    });

    test("hue slider supports continuous drag updates", async ({ page }) => {
        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerTestHarness",
            "ColorPickerTestHarness",
            {},
        );

        const swatches = page.locator(".swatch-row .color-swatch");
        await swatches.nth(1).click();

        const hexInput = page.locator('input[type="text"]');
        const beforeDrag = await hexInput.inputValue();

        const hue = page.locator(".hue-horizontal");
        const box = await hue.boundingBox();
        expect(box).not.toBeNull();

        await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
        await page.mouse.down();
        await page.mouse.move(
            box!.x + box!.width * 0.35,
            box!.y + box!.height / 2,
            {
                steps: 8,
            },
        );
        const duringDrag = await hexInput.inputValue();
        await page.mouse.move(
            box!.x + box!.width * 0.7,
            box!.y + box!.height / 2,
            {
                steps: 8,
            },
        );
        await page.mouse.up();
        const afterDrag = await hexInput.inputValue();

        expect(beforeDrag).not.toEqual(duringDrag);
        expect(duringDrag).not.toEqual(afterDrag);
    });
});
