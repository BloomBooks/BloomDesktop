// The text color picker that the Format dialog's Color button opens: every control in it shows the
// color being chosen, the text on the page takes that color at once, and Cancel or Escape put the
// old color back while OK or a click outside the picker keep the new one.
// Automates the manual test "Text Color Picker" (Test Case ID 365).
//
// One A5 Basic Book with a text-only page is used throughout. The tests are serial: each starts
// from the color the one before it left on the text.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    appendToHexBox,
    clickAlphaSliderMiddle,
    clickHueSlider,
    clickPaletteSwatch,
    clickSaturationBox,
    closeColorPickerByClickingOutside,
    closeColorPickerWith,
    closeColorPickerWithEscape,
    colorPicker,
    hexToRgba,
    parseCssRgba,
    pureHue,
    readColorPicker,
    toHsv,
    typeInHexBox,
    type IRgba,
} from "../helpers/colorPicker";
import {
    isFormatDialogOpen,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
} from "../helpers/formatDialog";

test.use({
    collectionSpec: { name: "text-color-picker", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const L1 = "en";
const GROUP = ".bloom-translationGroup";

// Colors from the text palette, as "#rrggbb".
const RED = "#ff1616";
const TEAL = "#03989e";
const PURPLE = "#8c52ff";
/** A color that is in no palette, for typing into the hex box. */
const TYPED = "#1E90FF";

/** Color channels are rounded where the browser and react-color convert between color spaces. */
const kChannelTolerance = 2;
/** The pointers' positions are rounded the same way, in percent. */
const kPointerTolerancePercent = 1.5;

/** The text's color before any test changed it. Set by the first test. */
let originalColor: IRgba;

/** The color of the text on the page. */
async function textColor(page: Page): Promise<IRgba> {
    return parseCssRgba(
        await editablePageFrame(page)
            .locator(`${GROUP} .bloom-editable[lang="${L1}"]`)
            .first()
            .evaluate((e) => getComputedStyle(e).color),
    );
}

/** How far apart two colors are: the largest difference of any channel, alpha counted in 255ths. */
function colorDistance(a: IRgba, b: IRgba): number {
    return Math.max(
        Math.abs(a.r - b.r),
        Math.abs(a.g - b.g),
        Math.abs(a.b - b.b),
        Math.abs(a.a - b.a) * 255,
    );
}

const describe = (c: IRgba) => `rgba(${c.r}, ${c.g}, ${c.b}, ${c.a})`;

/** Wait until the text on the page is this color. */
async function expectTextColor(
    page: Page,
    expected: IRgba,
    why: string,
): Promise<void> {
    await expect
        .poll(async () => colorDistance(await textColor(page), expected), {
            timeout: 15000,
            message: `${why}: the text should be ${describe(expected)}, but is ${describe(await textColor(page))}.`,
        })
        .toBeLessThanOrEqual(kChannelTolerance);
}

/**
 * Wait until the picker's hex box shows `hex` (as Bloom writes it, "#RRGGBBAA"), then check that
 * every other control of the picker, and the text on the page, show the same color.
 */
async function expectEverythingShows(page: Page, hex: string): Promise<void> {
    const frame = editablePageFrame(page);
    await expect
        .poll(async () => (await readColorPicker(frame)).hex.toUpperCase(), {
            timeout: 15000,
            message: "The hex box did not show the color chosen.",
        })
        .toBe(hex.toUpperCase());
    await expectControlsMatchHexBox(page);
}

/** Check that every control of the picker, and the text on the page, show the hex box's color. */
async function expectControlsMatchHexBox(page: Page): Promise<IRgba> {
    const state = await readColorPicker(editablePageFrame(page));
    const color = hexToRgba(state.hex);
    const hsv = toHsv(color);
    expect(
        colorDistance(state.sample, color),
        `the sample (${describe(state.sample)}) should be the hex box's ${state.hex}`,
    ).toBeLessThanOrEqual(kChannelTolerance);
    expect(
        Math.abs(state.alphaPercentage - Math.round(color.a * 100)),
        `the alpha slider's percentage (${state.alphaPercentage}%) should be the hex box's alpha`,
    ).toBeLessThanOrEqual(1);
    // The saturation box and the hue slider show the hue, which a gray does not have.
    if (hsv.s > 0) {
        expect(
            colorDistance(state.saturationHue, pureHue(hsv.h)),
            `the saturation box (${describe(state.saturationHue)}) should show the hue of ${state.hex}`,
        ).toBeLessThanOrEqual(kChannelTolerance);
        expect(
            Math.abs(state.huePointer - (hsv.h * 100) / 360),
            `the hue slider's pointer (${state.huePointer}%) should be at the hue of ${state.hex}`,
        ).toBeLessThanOrEqual(kPointerTolerancePercent);
    }
    expect(
        Math.abs(state.saturationPointer.left - hsv.s * 100),
        `the saturation box's pointer (left ${state.saturationPointer.left}%) should be at the saturation of ${state.hex}`,
    ).toBeLessThanOrEqual(kPointerTolerancePercent);
    expect(
        Math.abs(state.saturationPointer.top - (100 - hsv.v * 100)),
        `the saturation box's pointer (top ${state.saturationPointer.top}%) should be at the value of ${state.hex}`,
    ).toBeLessThanOrEqual(kPointerTolerancePercent);
    await expectTextColor(page, color, `with ${state.hex} in the picker`);
    return color;
}

/** The hex box's form of a palette color, which is opaque. */
const opaque = (color: string) => `${color}FF`;

/** Open the text color picker from the Format dialog's Characters tab. */
async function openTextColorPicker(page: Page): Promise<void> {
    if (!(await isFormatDialogOpen(page))) {
        await clickInGroup(page, GROUP, L1);
        await scrollFormatGearIntoView(page);
        await openFormatDialog(page);
    }
    await showFormatDialogTab(page, "characters");
    const frame = editablePageFrame(page);
    await frame.locator("#colorSelectButton").click();
    await colorPicker(frame).waitFor({ state: "visible", timeout: 15000 });
}

test.describe("Text Color Picker", () => {
    test("builds a book with a page of text", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await typeInGroup(page, GROUP, L1, "Some text to color.");
        originalColor = await textColor(page);
        // Sanity check: the palette colors used below must all differ from the starting color.
        for (const color of [RED, TEAL, PURPLE])
            expect(
                colorDistance(originalColor, hexToRgba(color)),
            ).toBeGreaterThan(kChannelTolerance);
    });

    test("clicking palette colors changes every control and the text; Cancel puts the old color back [Test Case ID 365]", async ({
        page,
    }) => {
        await openTextColorPicker(page);
        const frame = editablePageFrame(page);
        for (const color of [RED, TEAL]) {
            await clickPaletteSwatch(frame, color);
            await expectEverythingShows(page, opaque(color));
        }
        await closeColorPickerWith(frame, "Cancel");
        await expectTextColor(page, originalColor, "after Cancel");
    });

    test("Escape closes the picker and puts the old color back [Test Case ID 365]", async ({
        page,
    }) => {
        await openTextColorPicker(page);
        const frame = editablePageFrame(page);
        await clickPaletteSwatch(frame, PURPLE);
        await expectEverythingShows(page, opaque(PURPLE));
        await closeColorPickerWithEscape(frame);
        await expectTextColor(page, originalColor, "after Escape");
    });

    test("OK keeps the new color, and so does a click outside the picker [Test Case ID 365]", async ({
        page,
    }) => {
        await openTextColorPicker(page);
        const frame = editablePageFrame(page);
        await clickPaletteSwatch(frame, RED);
        await expectEverythingShows(page, opaque(RED));
        await closeColorPickerWith(frame, "OK");
        await expectTextColor(page, hexToRgba(RED), "after OK");

        await openTextColorPicker(page);
        await clickPaletteSwatch(frame, TEAL);
        await expectEverythingShows(page, opaque(TEAL));
        await closeColorPickerByClickingOutside(frame);
        await expectTextColor(
            page,
            hexToRgba(TEAL),
            "after a click outside the picker",
        );
    });

    test("a color made with the hue slider and the saturation box shows in every control and the text, and can be canceled or kept [Test Case ID 365]", async ({
        page,
    }) => {
        const frame = editablePageFrame(page);
        const before = await textColor(page);
        // Pick a color twice: the first time Cancel it, the second time OK it.
        for (const closeWith of ["Cancel", "OK"] as const) {
            await openTextColorPicker(page);
            await clickHueSlider(frame, 66);
            await clickSaturationBox(frame, 80, 20);
            const state = await readColorPicker(frame);
            expect(
                Math.abs(state.huePointer - 66),
                "the hue slider's pointer should be where it was clicked",
            ).toBeLessThanOrEqual(kPointerTolerancePercent * 2);
            expect(
                Math.abs(state.saturationPointer.left - 80) +
                    Math.abs(state.saturationPointer.top - 20),
                "the saturation box's pointer should be where it was clicked",
            ).toBeLessThanOrEqual(kPointerTolerancePercent * 2);
            const made = await expectControlsMatchHexBox(page);
            expect(
                colorDistance(made, before),
                "the color made should differ from the text's color before",
            ).toBeGreaterThan(kChannelTolerance);
            await closeColorPickerWith(frame, closeWith);
            await expectTextColor(
                page,
                closeWith === "OK" ? made : before,
                `after ${closeWith}`,
            );
        }
    });

    test("the alpha slider changes the hex box, the sample and the text [Test Case ID 365]", async ({
        page,
    }) => {
        await openTextColorPicker(page);
        const frame = editablePageFrame(page);
        await clickPaletteSwatch(frame, RED);
        await expectEverythingShows(page, opaque(RED));
        await clickAlphaSliderMiddle(frame);
        await expect
            .poll(async () => (await readColorPicker(frame)).alphaPercentage, {
                timeout: 15000,
                message: "The alpha slider's percentage did not change.",
            })
            .not.toBe(100);
        const state = await readColorPicker(frame);
        expect(
            Math.abs(state.alphaPercentage - 50),
            "a click in the middle of the alpha slider should give about 50%",
        ).toBeLessThanOrEqual(3);
        expect(
            state.hex.slice(0, 7).toLowerCase(),
            "the alpha slider should change only the alpha of the hex code",
        ).toBe(RED);
        await expectControlsMatchHexBox(page);
        await closeColorPickerWith(frame, "OK");
    });

    test("a hex code typed in, and then '80' added to it, changes every control and the text [Test Case ID 365]", async ({
        page,
    }) => {
        await openTextColorPicker(page);
        const frame = editablePageFrame(page);
        await typeInHexBox(frame, TYPED);
        // A six-digit code stays as typed, without an alpha channel.
        await expectEverythingShows(page, TYPED);

        await appendToHexBox(frame, "80");
        await expectEverythingShows(page, `${TYPED}80`);
        expect(
            (await readColorPicker(frame)).alphaPercentage,
            "the alpha slider should be at 50%",
        ).toBe(50);
        await closeColorPickerWith(frame, "OK");
        await expectTextColor(page, hexToRgba(`${TYPED}80`), "after OK");
    });
});
