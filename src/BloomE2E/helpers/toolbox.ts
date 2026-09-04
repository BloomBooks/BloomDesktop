// The Edit tab's right-hand toolbox: showing and hiding it, and opening one of its tools.
//
// The toolbox is a drawer in the Edit tab's own document, not in the page being edited, and its
// tools live in an iframe of their own. Two quirks are absorbed here. The drawer is driven by a
// hidden check box (`#pure-toggle-right`) whose visible label is what a person clicks, because the
// pure-drawer CSS builds the whole open/shut effect on that check box's state; clicking the box
// itself does nothing. And whether the drawer starts open is remembered per book (BookInfo
// ToolboxIsOpen), so a test that needs it open has to ask rather than assume.
//
// Nothing here turns a tool on through the toolbox's own "More..." check boxes. The toolbox shows
// only the tools a book has enabled, and for the tools a test wants, Bloom enables the tool itself
// when the page asks for it: clicking the canvas of a Canvas page opens the Canvas tool (see
// canvasElements.ts, openCanvasTool), and clicking a video box opens the Sign Language tool (see
// videos.ts). A test that goes through the page drives the same route a person does.

import { expect, type Frame, type Page } from "@playwright/test";

/** The tool ids Bloom's toolbox uses, as the accordion headers carry them in data-toolid. */
export type ToolId =
    | "canvas"
    | "game"
    | "signLanguage"
    | "talkingBook"
    | "decodableReader"
    | "leveledReader"
    | "bookSettings"
    | "settings"
    | "impairmentVisualizer"
    | "music";

/** The check box the pure-drawer CSS reads to decide whether the toolbox is open. */
const TOOLBOX_CHECKBOX = "#pure-toggle-right";

/** The label a person clicks to open or shut the toolbox. */
const TOOLBOX_LABEL = 'label.pure-toggle-label[for="pure-toggle-right"]';

/** True when the toolbox drawer is open. */
export async function isToolboxShowing(page: Page): Promise<boolean> {
    return page.evaluate((selector) => {
        const box = document.querySelector(selector) as HTMLInputElement | null;
        return !!box?.checked;
    }, TOOLBOX_CHECKBOX);
}

/**
 * Open the toolbox drawer, by clicking the same label a person clicks, and wait until it is open.
 * Does nothing when it is open already, so a test can call it without knowing what the book
 * remembered. Throws when Bloom is not showing the Edit tab, where there is no toolbox at all.
 */
export async function showToolbox(page: Page): Promise<Frame> {
    const label = page.locator(TOOLBOX_LABEL);
    if (
        (await label.count()) === 0 &&
        (await page.locator(TOOLBOX_CHECKBOX).count()) === 0
    )
        throw new Error(
            "There is no toolbox toggle in this document, so Bloom is not showing the Edit tab.",
        );
    if (!(await isToolboxShowing(page))) {
        await label.click();
        await expect
            .poll(async () => isToolboxShowing(page), {
                timeout: 30000,
                message:
                    "Clicking the toolbox toggle did not open the toolbox.",
            })
            .toBe(true);
    }
    return toolboxFrame(page);
}

/** Shut the toolbox drawer, and wait until it is shut. Does nothing when it is shut already. */
export async function hideToolbox(page: Page): Promise<void> {
    if (!(await isToolboxShowing(page))) return;
    await page.locator(TOOLBOX_LABEL).click();
    await expect
        .poll(async () => isToolboxShowing(page), {
            timeout: 30000,
            message: "Clicking the toolbox toggle did not shut the toolbox.",
        })
        .toBe(false);
}

/**
 * The iframe the toolbox's tools are drawn in. The frame exists whether or not the drawer is open,
 * so a caller that wants a control in it should showToolbox first.
 */
export function toolboxFrame(page: Page): Frame {
    const frame = page.frame({ name: "toolbox" });
    if (!frame)
        throw new Error(
            "There is no 'toolbox' frame, so Bloom is not showing the Edit tab. " +
                `Frames: ${page
                    .frames()
                    .map((f) => f.name() || "(main)")
                    .join(", ")}.`,
        );
    return frame;
}

/**
 * Open one of the tools the toolbox is showing, by clicking its accordion header the way a person
 * does, and wait until the tool's own controls are showing. Opens the toolbox drawer first if it is
 * shut. Does nothing but return the frame when the tool's controls are showing already.
 *
 * The tool is found by the `data-toolid` its header carries, not by its heading text, which is
 * localized. Throws, naming the tools on offer, when the book has not got this tool; see the note
 * at the top of this file for how a tool gets turned on.
 */
export async function openTool(
    page: Page,
    tool: ToolId,
    controlsSelector: string,
): Promise<Frame> {
    const frame = await showToolbox(page);
    const controls = frame.locator(controlsSelector).first();
    if (await controls.isVisible().catch(() => false)) return frame;
    const header = frame
        .locator(`.MuiAccordionSummary-root:has([data-toolid="${tool}"])`)
        .first();
    if ((await header.count()) === 0)
        throw new Error(
            `The toolbox is not offering the "${tool}" tool. It shows: ` +
                `${(await getShownTools(page)).join(", ") || "(nothing)"}. A tool is offered ` +
                `only once the book has it on; the page it belongs to turns it on when clicked.`,
        );
    await header.click();
    await controls.waitFor({ state: "visible", timeout: 30000 });
    return frame;
}

/** Which tools the toolbox is showing, by the `data-toolid` each accordion header carries. */
export async function getShownTools(page: Page): Promise<string[]> {
    return toolboxFrame(page)
        .locator(".MuiAccordionSummary-root [data-toolid]")
        .evaluateAll((headers) =>
            headers.map((header) => header.getAttribute("data-toolid") ?? ""),
        );
}
