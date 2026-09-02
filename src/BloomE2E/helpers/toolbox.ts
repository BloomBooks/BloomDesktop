// The toolbox: the panel of tools (Talking Book, Decodable Reader, ...) at the right of the Edit
// tab, and the handle that shows and hides it.
//
// The show/hide handle is a checkbox in the Edit tab's own document; the tools live in a separate
// "toolbox" frame, as an accordion with one section per enabled tool. Bloom remembers per book
// whether the toolbox was showing, so a test that shows it should hide it again when it is done.

import { expect, type Frame, type Page } from "@playwright/test";

/** The checkbox that decides whether the toolbox is showing. */
const TOGGLE = "#pure-toggle-right";
/** The visible handle a person clicks. (A second, full-screen overlay label targets the same box.) */
const TOGGLE_HANDLE = `label.pure-toggle-label[for="pure-toggle-right"]`;

/** The frame holding the tools. Throws if the Edit tab is not showing. */
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

/** True while the toolbox is showing beside the page. */
export async function isToolboxShowing(page: Page): Promise<boolean> {
    return page.locator(TOGGLE).isChecked();
}

/**
 * Show the toolbox by clicking its handle, the way a person does, if it is not showing already.
 * The tool Bloom last had open in this book (Talking Book, for a new book) opens with it.
 */
export async function showToolbox(page: Page): Promise<void> {
    if (await isToolboxShowing(page)) return;
    await page.locator(TOGGLE_HANDLE).click();
    await expect(
        page.locator(TOGGLE),
        "Clicking the toolbox handle did not show the toolbox.",
    ).toBeChecked({ timeout: 30000 });
}

/** Hide the toolbox by clicking its handle, if it is showing. */
export async function hideToolbox(page: Page): Promise<void> {
    if (!(await isToolboxShowing(page))) return;
    await page.locator(TOGGLE_HANDLE).click();
    await expect(
        page.locator(TOGGLE),
        "Clicking the toolbox handle did not hide the toolbox.",
    ).not.toBeChecked({ timeout: 30000 });
}

/**
 * The name of the tool whose section of the toolbox is open, as its header shows it, e.g.
 * "Talking Book Tool", or undefined when no tool is open.
 */
export async function getOpenToolName(page: Page): Promise<string | undefined> {
    const open = toolboxFrame(page).locator(
        '.MuiAccordionSummary-root[aria-expanded="true"]',
    );
    if ((await open.count()) === 0) return undefined;
    return (await open.first().innerText()).trim();
}
