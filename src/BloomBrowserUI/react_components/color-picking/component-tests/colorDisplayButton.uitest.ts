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

    test("transparent selection keeps transparency background visible", async ({
        page,
    }) => {
        await setTestComponent(
            page,
            "../color-picking/component-tests/colorDisplayButtonTestHarness",
            "ColorDisplayButtonTestHarness",
            {
                initialColor: "transparent",
                transparency: true,
            },
        );

        const transparencyBackground = page.getByTestId(
            "color-display-button-transparency-background",
        );
        await expect(transparencyBackground).toBeVisible({ timeout: 5000 });

        const backgroundImage = await transparencyBackground.evaluate(
            (element) => getComputedStyle(element).backgroundImage,
        );
        expect(backgroundImage).not.toBe("none");

        await expect(page.getByTestId("color-display-button-swatch")).toHaveCSS(
            "background-color",
            "rgba(0, 0, 0, 0)",
        );
    });

    // With deferOnChangeUntilComplete, onChange follows react-color's onChangeComplete, which
    // is a 100ms debounce of the picker's changes, not a mouse-up event. So a drag that pauses
    // sends a change while the button is still held, and how many changes a drag sends depends
    // on how fast the machine delivers the mouse moves. This test therefore asserts only what
    // does not depend on timing: the changes settle on the dragged color, and Cancel then sends
    // the original color back exactly once. The drag pauses halfway on purpose, so the test
    // always exercises the mid-drag change a slow machine produces.
    test("deferred change settles on the dragged color and cancel restores", async ({
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

        const y = box!.y + box!.height / 2;
        await page.mouse.move(box!.x + 5, y);
        await page.mouse.down();
        await page.mouse.move(box!.x + box!.width * 0.35, y, { steps: 4 });
        // A user pausing mid-drag, for longer than the 100ms debounce. This timed wait is an
        // approved exception to the no-timed-waits rule in .claude/skills/component-test: it is
        // not waiting for the app to finish anything. The pause is itself the input under test,
        // so there is no condition to wait on instead.
        await page.waitForTimeout(250);
        await page.mouse.move(box!.x + box!.width * 0.65, y, { steps: 4 });
        await page.mouse.up();

        // Once the last change carries the color the dialog shows, the debounce has
        // delivered its final call and nothing more is pending.
        const hexInput = page.getByRole("dialog").locator('input[type="text"]');
        await expect
            .poll(async () => {
                const shown = (await hexInput.inputValue()).toLowerCase();
                const last = await page
                    .getByTestId("last-changed-color")
                    .textContent();
                return shown !== "#00aa00" && last === shown;
            })
            .toBe(true);
        const changesAfterDrag = Number(
            await page.getByTestId("change-count").textContent(),
        );
        expect(changesAfterDrag).toBeGreaterThan(0);

        await page.getByRole("button", { name: "Cancel" }).click();

        await expect(page.getByTestId("change-count")).toHaveText(
            String(changesAfterDrag + 1),
        );
        await expect(page.getByTestId("last-changed-color")).toHaveText(
            "#00aa00",
        );
        await expect(page.getByTestId("close-result")).toHaveText("cancel");
    });
});
