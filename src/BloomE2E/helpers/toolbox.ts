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
// videos.ts). A test that goes through the page drives the same route a person does. The two
// exceptions are for tests whose subject is the toolbox itself: setToolTurnedOn is the journey
// route through the "More..." check boxes, and enableToolForBook is the fast setup route that
// posts the same setting they do.

import { expect, type Frame, type Page } from "@playwright/test";
import { apiPost } from "./api";
import { editBook } from "./bookMaking";

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
    | "imageDescription"
    | "motion"
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

/** The selector for one tool's accordion header, by the `data-toolid` it carries. */
function toolHeader(tool: ToolId): string {
    return `.MuiAccordionSummary-root:has([data-toolid="${tool}"])`;
}

/**
 * The tool whose section of the toolbox is open, by the `data-toolid` its header carries, e.g.
 * "talkingBook", or undefined when no section is open. getOpenToolName reads the header's
 * localized text instead.
 */
export async function getOpenTool(page: Page): Promise<ToolId | undefined> {
    const icon = toolboxFrame(page).locator(
        '.MuiAccordionSummary-root[aria-expanded="true"] [data-toolid]',
    );
    if ((await icon.count()) === 0) return undefined;
    return ((await icon.first().getAttribute("data-toolid")) ?? undefined) as
        | ToolId
        | undefined;
}

/**
 * Wait until the toolbox has finished reacting to what just happened in it: two animation frames
 * in the toolbox's own document, by which time React has committed any re-render a click caused.
 * This is for a test that has to show a click changed NOTHING, where there is no new state to
 * wait for.
 */
async function waitForToolboxToSettle(page: Page): Promise<void> {
    await toolboxFrame(page).evaluate(
        () =>
            new Promise<void>((resolve) =>
                requestAnimationFrame(() =>
                    requestAnimationFrame(() => resolve()),
                ),
            ),
    );
}

/**
 * Click the header of one of the toolbox's sections, the way a person does, whether or not that
 * section is already open, and wait for the toolbox to settle. openTool is the route for "open
 * this tool"; this is for a test whose subject is what the click itself does, such as clicking
 * the header of the section that is already open. Throws, naming the tools on offer, when the
 * toolbox is not offering this one.
 */
export async function clickToolHeader(page: Page, tool: ToolId): Promise<void> {
    const frame = await showToolbox(page);
    const header = frame.locator(toolHeader(tool)).first();
    if ((await header.count()) === 0)
        throw new Error(
            `The toolbox is not offering the "${tool}" tool. It shows: ` +
                `${(await getShownTools(page)).join(", ") || "(nothing)"}.`,
        );
    await header.click();
    await waitForToolboxToSettle(page);
}

/** The row under the toolbox's "More..." section that holds one tool's on/off check box. */
function toolCheckboxRow(tool: ToolId): string {
    return `[data-testid="toolbox-tool-checkbox-${tool}"]`;
}

/**
 * Turn a tool on or off by ticking or unticking its check box under the toolbox's "More..."
 * section, the way a person does, and wait until the toolbox has caught up: the tool's section is
 * offered (on) or gone (off). Opens the "More..." section first. Does nothing but wait when the
 * box is already in the state asked for.
 *
 * This is the UI route, for the journey test of turning tools on and off. A test that only needs
 * a tool to be there should call enableToolForBook, which is faster.
 */
export async function setToolTurnedOn(
    page: Page,
    tool: ToolId,
    on: boolean,
): Promise<void> {
    const frame = await openTool(page, "settings", toolCheckboxRow(tool));
    const box = frame.locator(
        `${toolCheckboxRow(tool)} input[type="checkbox"]`,
    );
    if ((await box.isChecked()) !== on) await box.click();
    await expect
        .poll(async () => (await getShownTools(page)).includes(tool), {
            timeout: 30000,
            message: `Turning the "${tool}" tool ${on ? "on" : "off"} under "More..." did not ${
                on ? "add" : "remove"
            } its section.`,
        })
        .toBe(on);
}

/**
 * Turn a tool on for the book being edited, by posting the same setting its check box under
 * "More..." posts, and wait until the toolbox is offering it. This is SETUP, not the path under
 * test; setToolTurnedOn is the UI route. `bookFolder` must be the book being edited.
 *
 * The toolbox asks which tools are enabled only while it is initializing, so this leaves the book
 * and comes back; without that round trip the tool stays absent until something else rebuilds the
 * toolbox. It comes back by selecting the book again rather than just switching tabs: after a book
 * is made from a template, the Collections tab can still have the template selected, and going
 * straight back to Edit then shows the template, which has no page to edit. (readerSetup.ts's
 * enableDecodableReaderTool is the same sequence, without that reselection.)
 */
export async function enableToolForBook(
    page: Page,
    bookFolder: string,
    tool: ToolId,
): Promise<void> {
    await apiPost(
        page,
        "editView/saveToolboxSetting",
        // "active", then the check box's id (the tool id plus "Check"), then 1 for on.
        `active\t${tool}Check\t1`,
        "text/plain",
    );
    // editBook waits for Bloom's editing state machine, not just for the toolbox: clicks aimed at a
    // toolbox that is still re-rendering land on nothing (see readerSetup.ts).
    await editBook(page, bookFolder);
    await expect
        .poll(async () => (await getShownTools(page)).includes(tool), {
            timeout: 30000,
            message: `The toolbox never offered the "${tool}" tool after it was enabled and the Edit tab was re-entered.`,
        })
        .toBe(true);
}

/**
 * Wait until the toolbox has a section open, and return which tool it is. The toolbox opens a
 * section a moment after the drawer opens, and again a moment after a tool is turned on (so the
 * person sees the check box tick before "More..." closes, BL-16501).
 */
export async function waitForOpenTool(page: Page): Promise<ToolId> {
    let open: ToolId | undefined;
    await expect
        .poll(
            async () => {
                open = await getOpenTool(page);
                return open;
            },
            {
                timeout: 30000,
                message: `The toolbox never opened a section. It shows: ${(
                    await getShownTools(page)
                ).join(", ")}.`,
            },
        )
        .toBeDefined();
    return open!;
}

/** Wait until this tool is the one open in the toolbox, and fail with `message` if it never is. */
export async function expectOpenTool(
    page: Page,
    tool: ToolId,
    message: string,
): Promise<void> {
    await expect
        .poll(async () => getOpenTool(page), { timeout: 30000, message })
        .toBe(tool);
}
