// Bloom's color picker dialog (react_components/color-picking/colorPickerDialog.tsx), as the
// Format dialog's Color button opens it in the Edit tab's page frame.
//
// The dialog shows one color in several controls at once: the saturation box (whose background is
// the pure hue of the color, with a pointer at its saturation and value), the hue slider, the
// alpha slider with its percentage, the hex box, and the sample swatch beside the hex box. Under
// them is the palette, a row of swatches. readColorPicker() reads every control, so that a test
// can check that they all show the same color.

import { expect, type Frame, type Locator } from "@playwright/test";
import { cssColorToHex } from "./cssValues";

/** A color as red, green and blue (0-255) and alpha (0-1). */
export interface IRgba {
    r: number;
    g: number;
    b: number;
    a: number;
}

/** What every control of the color picker shows. */
export interface IColorPickerState {
    /** The hex box, as Bloom shows it, e.g. "#FF1616FF" (the last two digits are the alpha). */
    hex: string;
    /** The sample swatch beside the hex box. */
    sample: IRgba;
    /** The saturation box's background, which is the pure hue of the color. */
    saturationHue: IRgba;
    /** Where the saturation box's pointer is, in percent of the box's width and height. */
    saturationPointer: { left: number; top: number };
    /** Where the hue slider's pointer is, in percent of the slider's width. */
    huePointer: number;
    /** Where the alpha slider's pointer is, in percent of the slider's width. */
    alphaPointer: number;
    /** The percentage beside the alpha slider, e.g. 100. */
    alphaPercentage: number;
}

/** The color picker dialog. */
export function colorPicker(frame: Frame): Locator {
    return frame.locator('[role="dialog"]');
}

/** A CSS color as the browser reports it ("rgb(1, 2, 3)" or "rgba(1, 2, 3, 0.5)"). */
export function parseCssRgba(cssColor: string): IRgba {
    const m =
        /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/.exec(
            cssColor.trim(),
        );
    if (!m)
        throw new Error(
            `"${cssColor}" is not a color this helper understands.`,
        );
    return {
        r: Number(m[1]),
        g: Number(m[2]),
        b: Number(m[3]),
        a: m[4] === undefined ? 1 : Number(m[4]),
    };
}

/** A hex color, "#rrggbb" or "#rrggbbaa", as IRgba. */
export function hexToRgba(hex: string): IRgba {
    const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})?$/i.exec(
        hex,
    );
    if (!m) throw new Error(`"${hex}" is not a hex color.`);
    return {
        r: parseInt(m[1], 16),
        g: parseInt(m[2], 16),
        b: parseInt(m[3], 16),
        a: m[4] === undefined ? 1 : parseInt(m[4], 16) / 255,
    };
}

/** Read every control of the open color picker. */
export async function readColorPicker(
    frame: Frame,
): Promise<IColorPickerState> {
    const state = await colorPicker(frame).evaluate((dialog) => {
        const one = (selector: string): HTMLElement => {
            const element = dialog.querySelector<HTMLElement>(selector);
            if (!element)
                throw new Error(`The color picker has no "${selector}".`);
            return element;
        };
        // The sample is the one swatch that is not in the palette.
        const sample = [
            ...dialog.querySelectorAll<HTMLElement>(".color-swatch"),
        ].find((swatch) => !swatch.closest(".swatch-section"));
        if (!sample) throw new Error("The color picker has no sample swatch.");
        const saturation = one(".saturation-white");
        const saturationPointer = one(".saturation-white > div:last-child");
        // react-color's Alpha draws a checkerboard, a gradient, and last a container whose only
        // child is the pointer, which it places with a "left" in percent.
        const alphaPointer = one(
            ".alpha-slider-track > div > div:last-child > div",
        ).style.left;
        if (!alphaPointer.endsWith("%"))
            throw new Error(
                `The alpha slider's pointer has left "${alphaPointer}", not a percentage.`,
            );
        return {
            hex: (one('input[type="text"]') as HTMLInputElement).value,
            sample: getComputedStyle(sample.lastElementChild as HTMLElement)
                .backgroundColor,
            saturationHue: getComputedStyle(saturation.parentElement!)
                .backgroundColor,
            saturationPointer: {
                left: parseFloat(saturationPointer.style.left),
                top: parseFloat(saturationPointer.style.top),
            },
            huePointer: parseFloat(
                one('.hue-horizontal > div[style*="left"]').style.left,
            ),
            alphaPointer: parseFloat(alphaPointer),
            alphaPercentage: parseFloat(
                one(".alpha-slider-percentage").textContent ?? "",
            ),
        };
    });
    return {
        ...state,
        sample: parseCssRgba(state.sample),
        saturationHue: parseCssRgba(state.saturationHue),
    };
}

/**
 * Click the palette swatch of this color ("#rrggbb"). Throws, listing the swatches, if the palette
 * has none of that color.
 */
export async function clickPaletteSwatch(
    frame: Frame,
    color: string,
): Promise<void> {
    const swatches = colorPicker(frame).locator(
        ".swatch-section .color-swatch > div:last-child",
    );
    await swatches.first().waitFor({ state: "visible", timeout: 15000 });
    const colors = (
        await swatches.evaluateAll((elements) =>
            elements.map((e) => getComputedStyle(e).backgroundColor),
        )
    ).map(cssColorToHex);
    const index = colors.indexOf(color.toLowerCase());
    if (index < 0)
        throw new Error(
            `The color picker has no swatch of ${color}. Its swatches are: ${colors.join(", ")}.`,
        );
    await swatches.nth(index).click();
}

/** Whether the open color picker shows its alpha slider, which it leaves out where transparency is not offered. */
export async function hasAlphaSlider(frame: Frame): Promise<boolean> {
    return (
        (await colorPicker(frame).locator(".alpha-slider-track").count()) > 0
    );
}

/**
 * How opaque each swatch of the open color picker's palette is, from 0 (the transparent swatch) to
 * 1, in palette order. A gradient swatch counts as 1: the palette's gradients are all opaque.
 */
export async function getPaletteSwatchOpacities(
    frame: Frame,
): Promise<number[]> {
    const swatches = colorPicker(frame).locator(
        ".swatch-section .color-swatch > div:last-child",
    );
    await swatches.first().waitFor({ state: "visible", timeout: 15000 });
    const backgrounds = await swatches.evaluateAll((elements) =>
        elements.map((e) => {
            const style = getComputedStyle(e);
            return {
                color: style.backgroundColor,
                image: style.backgroundImage,
            };
        }),
    );
    return backgrounds.map((b) =>
        b.image !== "none" ? 1 : parseCssRgba(b.color).a,
    );
}

/** The open color picker's hex box. */
export function hexBox(frame: Frame): Locator {
    return colorPicker(frame).locator('input[type="text"]');
}

/** Click a point of a control, given in percent of its width and height. */
async function clickAtPercent(
    control: Locator,
    leftPercent: number,
    topPercent: number,
): Promise<void> {
    const box = await control.boundingBox();
    if (!box) throw new Error("The control is not on screen.");
    await control.click({
        position: {
            x: (box.width * leftPercent) / 100,
            y: (box.height * topPercent) / 100,
        },
    });
}

/** Click the hue slider at this percent of its width. */
export async function clickHueSlider(
    frame: Frame,
    leftPercent: number,
): Promise<void> {
    await clickAtPercent(
        colorPicker(frame).locator(".hue-horizontal"),
        leftPercent,
        50,
    );
}

/** Click the saturation box at this point, in percent of its width and height. */
export async function clickSaturationBox(
    frame: Frame,
    leftPercent: number,
    topPercent: number,
): Promise<void> {
    await clickAtPercent(
        colorPicker(frame).locator(".saturation-white"),
        leftPercent,
        topPercent,
    );
}

/** Click the middle of the alpha slider. */
export async function clickAlphaSliderMiddle(frame: Frame): Promise<void> {
    await clickAtPercent(
        colorPicker(frame).locator(".alpha-slider-track"),
        50,
        50,
    );
}

/** Replace what the hex box holds by typing `text`, as a person would. */
export async function typeInHexBox(frame: Frame, text: string): Promise<void> {
    const input = colorPicker(frame).locator('input[type="text"]');
    await input.click();
    await input.press("Control+A");
    await input.pressSequentially(text);
}

/** Type more characters at the end of what the hex box holds. */
export async function appendToHexBox(
    frame: Frame,
    text: string,
): Promise<void> {
    const input = colorPicker(frame).locator('input[type="text"]');
    await input.click();
    await input.press("End");
    await input.pressSequentially(text);
}

/**
 * Close the picker with its OK or Cancel button, and wait for it to go. The buttons are found by
 * their test ids, so this works whatever language Bloom is showing.
 */
export async function closeColorPickerWith(
    frame: Frame,
    button: "OK" | "Cancel",
): Promise<void> {
    const dialog = colorPicker(frame);
    await dialog
        .getByTestId(button === "OK" ? "dialog-ok" : "dialog-cancel")
        .click();
    await expect(
        dialog,
        `The color picker did not close after ${button}.`,
    ).toBeHidden({ timeout: 15000 });
}

/** Close the picker by clicking outside it, near the corner of the page frame, and wait for it to go. */
export async function closeColorPickerByClickingOutside(
    frame: Frame,
): Promise<void> {
    await frame
        .locator(".MuiDialog-container")
        .click({ position: { x: 5, y: 5 } });
    await expect(
        colorPicker(frame),
        "The color picker did not close when clicked outside.",
    ).toBeHidden({ timeout: 15000 });
}

/** Close the picker with the Escape key, and wait for it to go. */
export async function closeColorPickerWithEscape(frame: Frame): Promise<void> {
    await colorPicker(frame).press("Escape");
    await expect(
        colorPicker(frame),
        "The color picker did not close on Escape.",
    ).toBeHidden({ timeout: 15000 });
}

/**
 * The hue, saturation and value of a color, as react-color works them out: hue in degrees,
 * saturation and value from 0 to 1.
 */
export function toHsv(color: IRgba): { h: number; s: number; v: number } {
    const r = color.r / 255;
    const g = color.g / 255;
    const b = color.b / 255;
    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);
    const d = max - min;
    let h = 0;
    if (d !== 0) {
        if (max === r) h = ((g - b) / d) % 6;
        else if (max === g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
    }
    return { h, s: max === 0 ? 0 : d / max, v: max };
}

/** The fully saturated, full-value color of this hue, which the saturation box shows behind it. */
export function pureHue(h: number): IRgba {
    const f = (n: number) => {
        const k = (n + h / 60) % 6;
        return Math.round(255 * (1 - Math.max(0, Math.min(k, 4 - k, 1))));
    };
    return { r: f(5), g: f(3), b: f(1), a: 1 };
}
