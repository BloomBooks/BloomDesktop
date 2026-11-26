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

/**
 * Select the first occurrence of `text` in a text box of one paragraph with the keys a person can
 * use for it: Ctrl+Home, Right Arrow up to where the text starts, then Shift+Right Arrow across
 * it. Waits until Bloom shows the selection toolbar. Throws if the box does not hold `text`, or if
 * the keys selected something else.
 */
export async function selectTextIn(
    page: Page,
    box: Locator,
    text: string,
): Promise<void> {
    const boxText = await box.innerText();
    const start = boxText.indexOf(text);
    if (start < 0)
        throw new Error(
            `The box does not hold "${text}", so it cannot be selected. Its text is "${boxText}".`,
        );
    await box.click();
    await box.press("Control+Home");
    for (let i = 0; i < start; i++) await box.press("ArrowRight");
    for (let i = 0; i < text.length; i++) await box.press("Shift+ArrowRight");
    const frame = editablePageFrame(page);
    const selected = await frame.evaluate(
        () => document.getSelection()?.toString() ?? "",
    );
    if (selected !== text)
        throw new Error(
            `The arrow keys selected "${selected}" rather than "${text}" in a box whose text is "${boxText}".`,
        );
    await waitForCkEditorToHaveSelection(box);
    await expect(
        shownToolbar(frame),
        `Bloom did not show the selection toolbar although "${selected}" is selected.`,
    ).toBeVisible({ timeout: 15000 });
}

/**
 * Wait until CKEditor has looked at the selection in a text box as it is now, and has no further
 * look at it waiting.
 *
 * CKEditor looks at the selection at most once in 200 ms: a key or mouse event that comes sooner
 * after the last look is answered by one look when the 200 ms are over (checkSelectionChangeTimeout
 * in lib/ckeditor). At each look Bloom shows or hides the selection toolbar and closes the
 * toolbar's color panel (the selectionCheck handler in attachToCkEditor, bloomEditing.ts). So a
 * toolbar that is showing may be showing for an earlier selection, and a color panel opened within
 * 200 ms of the last key is closed again before its swatch can be clicked. No person is that
 * fast; a test is.
 *
 * This asks CKEditor for a look, as an event would, and waits for the next look it takes; then it
 * does both once more. One round is not enough: the look that comes first may be one that was on
 * its way before the asking, with the look asked for still owed when the 200 ms are over. By the
 * second asking nothing is on its way, so the look after it answers it, and CKEditor owes none.
 */
async function waitForCkEditorToHaveSelection(box: Locator): Promise<void> {
    await box.evaluate(
        (boxElement) =>
            new Promise<void>((resolve, reject) => {
                interface ICkEditor {
                    element: { $: Element };
                    once: (event: string, handler: () => void) => void;
                    selectionChange: () => void;
                }
                const editors = Object.values(
                    (
                        window as unknown as {
                            CKEDITOR: { instances: Record<string, ICkEditor> };
                        }
                    ).CKEDITOR.instances,
                );
                const editor = editors.find((e) => e.element.$ === boxElement);
                if (!editor) {
                    reject(new Error("The text box has no CKEditor."));
                    return;
                }
                // A deadline, after which the test fails; CKEditor takes its look within 200 ms.
                const deadline = setTimeout(
                    () =>
                        reject(
                            new Error(
                                "CKEditor did not look at the selection within 15 seconds.",
                            ),
                        ),
                    15000,
                );
                // CKEditor starts each look from a timer of its own. Going on from a timer
                // started in the look lets every look that was already on its way go first.
                const afterNextLook = (then: () => void) => {
                    editor.once("selectionCheck", () => setTimeout(then, 0));
                    editor.selectionChange();
                };
                afterNextLook(() =>
                    afterNextLook(() => {
                        clearTimeout(deadline);
                        resolve();
                    }),
                );
            }),
    );
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
 * Click one of the selection toolbar's formatting buttons (B, I, U or superscript) and return
 * without waiting for anything. Something must be selected, so that the toolbar is showing. For a
 * caller that checks what the click did to the text rather than the button's own state: in a
 * canvas element the click deselects the element and the toolbar goes away (BL-17205).
 */
export async function clickSelectionToolbarButton(
    page: Page,
    button: SelectionToolbarButton,
): Promise<void> {
    await shownToolbar(editablePageFrame(page))
        .locator(`.cke_button__${button}`)
        .click();
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
    await clickSelectionToolbarButton(page, button);
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
