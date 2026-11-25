// The toolbar Bloom shows above a text box while some of its text is selected: bold, italic,
// underline, superscript, text color, remove formatting and hyperlink. It formats the selection
// itself (direct formatting), where the Format dialog formats the box's whole style.
//
// The toolbar is CKEditor's. Every text box has one of its own in the page, and Bloom shows only
// the one of the box that holds the selection (attachToCkEditor in bloomEditing.ts), so the
// helpers here look for the toolbar that is showing. The color button opens a panel that CKEditor
// draws in an iframe of its own (lib/ckeditor/plugins/colorbutton/plugin.js).

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { cssColorToHex } from "./cssValues";

/** The toolbar's buttons that turn one kind of formatting on or off for the selection. */
export type SelectionToolbarButton =
    | "bold"
    | "italic"
    | "underline"
    | "superscript";

/** The toolbar of whichever box holds the selection. The others are in the page, hidden. */
function shownToolbar(frame: Frame): Locator {
    return frame.locator("div.cke:not(.cke_panel):visible");
}

/**
 * Select the last word of a text box with the keys a person can use for it: Ctrl+End, then
 * Ctrl+Shift+Left. Waits until Bloom shows the selection toolbar, and returns the word selected.
 */
export async function selectLastWordIn(
    page: Page,
    box: Locator,
): Promise<string> {
    await box.click();
    await box.press("Control+End");
    await box.press("Control+Shift+ArrowLeft");
    const frame = editablePageFrame(page);
    const selected = await frame.evaluate(
        () => document.getSelection()?.toString() ?? "",
    );
    if (!selected)
        throw new Error(
            `Ctrl+End and Ctrl+Shift+Left selected nothing in a box whose text is "${await box.innerText()}".`,
        );
    await expect(
        shownToolbar(frame),
        `Bloom did not show the selection toolbar although "${selected}" is selected.`,
    ).toBeVisible({ timeout: 15000 });
    return selected;
}

/** Whether the toolbar shows this kind of formatting as on for the selection. */
async function isSelectionToolbarButtonOn(
    frame: Frame,
    button: SelectionToolbarButton,
): Promise<boolean> {
    return shownToolbar(frame)
        .locator(`.cke_button__${button}`)
        .evaluate((b) => b.classList.contains("cke_button_on"));
}

/**
 * Click one of the selection toolbar's formatting buttons (B, I, U or superscript), which turns
 * that formatting on for the selected text if it was off and off if it was on. Something must be
 * selected, so that the toolbar is showing. Returns the new state.
 */
export async function toggleSelectionFormatting(
    page: Page,
    button: SelectionToolbarButton,
): Promise<boolean> {
    const frame = editablePageFrame(page);
    const wasOn = await isSelectionToolbarButtonOn(frame, button);
    await shownToolbar(frame).locator(`.cke_button__${button}`).click();
    await expect
        .poll(() => isSelectionToolbarButtonOn(frame, button), {
            timeout: 15000,
            message: `Clicking the selection toolbar's ${button} button did not turn it ${wasOn ? "off" : "on"}.`,
        })
        .toBe(!wasOn);
    return !wasOn;
}

/**
 * Give the selected text a color with the selection toolbar's text color button: open its panel
 * and click the swatch of `color` ("#rrggbb", one of the text palette). Throws, listing the
 * swatches, if the panel has no swatch of that color.
 */
export async function chooseSelectionTextColor(
    page: Page,
    color: string,
): Promise<void> {
    const frame = editablePageFrame(page);
    await shownToolbar(frame).locator(".cke_button__textcolor").click();
    // The panel is an iframe that CKEditor adds to the page; a panel opened earlier stays in the
    // page, hidden, so take the one that is showing.
    const panel = frame
        .locator(".cke_panel:visible iframe")
        .contentFrame()
        .locator(".cke_colorblock");
    const swatches = panel.locator("a.cke_colorbox > span.cke_colorbox");
    await swatches.first().waitFor({ state: "visible", timeout: 15000 });
    const colors = (
        await swatches.evaluateAll((elements) =>
            elements.map((e) => getComputedStyle(e).backgroundColor),
        )
    ).map(cssColorToHex);
    const index = colors.indexOf(color.toLowerCase());
    if (index < 0)
        throw new Error(
            `The selection toolbar's color panel has no swatch of ${color}. Its swatches are: ${colors.join(", ")}.`,
        );
    await swatches.nth(index).click();
    await expect(
        frame.locator(".cke_panel:visible"),
        "The selection toolbar's color panel did not close after a color was clicked.",
    ).toHaveCount(0, { timeout: 15000 });
}

/** How a run of text in a text box is drawn. */
export interface ITextRunFormatting {
    bold: boolean;
    italic: boolean;
    underline: boolean;
    /** The text color, as "#rrggbb". */
    color: string;
}

/**
 * How `text` is drawn in a text box, measured from the page (computed styles), which is what the
 * reader sees. `text` must lie within one run of the box, that is, be formatted alike throughout;
 * the first run that holds it is the one measured. Throws if no run holds it.
 */
export async function getTextRunFormatting(
    box: Locator,
    text: string,
): Promise<ITextRunFormatting> {
    const raw = await box.evaluate((boxElement, wanted) => {
        const walker = document.createTreeWalker(
            boxElement,
            NodeFilter.SHOW_TEXT,
        );
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            if (!node.textContent?.includes(wanted)) continue;
            const run = node.parentElement!;
            const style = getComputedStyle(run);
            // An underline is drawn by the element that asks for it, through everything inside
            // it, and text-decoration-line is not inherited: look at each element out to the box.
            let underline = false;
            for (
                let e: Element | null = run;
                e && boxElement.contains(e);
                e = e.parentElement
            )
                if (
                    getComputedStyle(e).textDecorationLine.includes("underline")
                )
                    underline = true;
            return {
                fontWeight: style.fontWeight,
                fontStyle: style.fontStyle,
                underline,
                color: style.color,
            };
        }
        return null;
    }, text);
    if (!raw)
        throw new Error(
            `No single run of the box holds "${text}". The box holds: ${await box.innerHTML()}`,
        );
    return {
        bold: parseInt(raw.fontWeight, 10) >= 600,
        italic: raw.fontStyle === "italic",
        underline: raw.underline,
        color: cssColorToHex(raw.color),
    };
}
