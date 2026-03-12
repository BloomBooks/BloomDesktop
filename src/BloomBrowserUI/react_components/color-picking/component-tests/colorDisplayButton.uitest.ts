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

    test("deferred change waits until drag completes and cancel restores", async ({
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
            {
                initialColor: "#00AA00",
                deferOnChangeUntilComplete: true,
            },
        );

        await page.getByTestId("color-display-button-swatch").click();
        await expect(page.getByRole("dialog")).toBeVisible();
        await expect(page.getByTestId("change-count")).toHaveText("0");

        const hue = page.locator(".hue-horizontal");
        const box = await hue.boundingBox();
        expect(box).not.toBeNull();

        await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
        await page.mouse.down();
        await page.mouse.move(
            box!.x + box!.width * 0.65,
            box!.y + box!.height / 2,
            {
                steps: 8,
            },
        );

        await expect(page.getByTestId("change-count")).toHaveText("0");

        await page.mouse.up();

        await expect(page.getByTestId("change-count")).toHaveText("1");
        await expect(page.getByTestId("last-changed-color")).not.toHaveText(
            "#00AA00",
        );

        await page.getByRole("button", { name: "Cancel" }).click();

        await expect(page.getByTestId("change-count")).toHaveText("2");
        await expect(page.getByTestId("last-changed-color")).toHaveText(
            "#00aa00",
        );
        await expect(page.getByTestId("close-result")).toHaveText("cancel");
    });
});
