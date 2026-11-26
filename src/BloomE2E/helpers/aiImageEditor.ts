// Driving the AI image editor ("Edit with AI..." on a picture), which Bloom shows in an iframe
// over the whole window. The editor is another repository's app (bloom-ai-image-tools), so what
// it offers a test is that repository's contract: its test ids where it has them, and otherwise
// the shape of its Material UI markup. See "AI-image-editor selectors are an untested cross-repo
// contract" in AUTOMATION-DEBT.md.

import {
    expect,
    type FrameLocator,
    type Locator,
    type Page,
} from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import {
    clickCanvasElementMenuItem,
    getCanvasElementMenuItems,
} from "./canvasElements";
import { realRightClick } from "./realClick";

/** The localization id, and so the test id, of the picture menu's "Edit with AI..." command. */
const kEditWithAiCommand = "EditTab.Image.EditWithAI";

/** The editor's app, inside the overlay Bloom puts over the window. */
export function aiImageEditorFrame(page: Page): FrameLocator {
    return page.frameLocator("#ai-image-editor-overlay iframe");
}

/**
 * Right-click the first picture of the page being edited and choose "Edit with AI..." from the
 * menu that comes up, the way a person does. Returns once the editor is showing the book's
 * pictures, which it does only after Bloom has handed it the book (its init message).
 */
export async function openAiImageEditorFromPicture(page: Page): Promise<void> {
    await realRightClick(
        editablePageFrame(page).locator(".bloom-imageContainer img").first(),
    );
    const command = (await getCanvasElementMenuItems(page)).find(
        (item) => item.id === kEditWithAiCommand,
    );
    if (!command)
        throw new Error(
            `The picture's menu has no "Edit with AI..." command (${kEditWithAiCommand}).`,
        );
    expect(
        command.enabled,
        'The picture\'s "Edit with AI..." command is not enabled.',
    ).toBe(true);
    await clickCanvasElementMenuItem(page, kEditWithAiCommand);
    await aiImageEditorFrame(page)
        .getByTestId("thumbnail-strip-bookImages")
        .waitFor({ state: "visible", timeout: 60000 });
}

/**
 * The dialog the editor puts up as it opens in "look-around" mode, which says that nothing can be
 * made or changed here.
 */
export function lookAroundNotice(page: Page): Locator {
    return aiImageEditorFrame(page).getByTestId("playground-notice-dialog");
}

/** Close the look-around notice with its own button, and wait until it has gone. */
export async function dismissLookAroundNotice(page: Page): Promise<void> {
    await aiImageEditorFrame(page)
        .getByTestId("playground-notice-dismiss")
        .click();
    await expect(lookAroundNotice(page)).toBeHidden({ timeout: 30000 });
}

/**
 * Open every group of tools in the editor's tool column, so that every tool's card is showing,
 * and return the ids of the tools (their data-tool-id), in the order they show.
 *
 * The groups ("Enhance", "Games" and so on) have no test id, and their names are localized, so a
 * group's button is told apart by its shape: a button whose own label is a caption. Each opens
 * its group and leaves the others as they are, so every click must add at least one card. That
 * shows each button found opened a group; a caller that must know no group was missed checks the
 * ids returned for a tool of each group it expects.
 */
export async function showAllAiImageTools(page: Page): Promise<string[]> {
    const editor = aiImageEditorFrame(page);
    const groupButtons = editor.locator(
        "button:has(> span.MuiTypography-caption)",
    );
    const groupCount = await groupButtons.count();
    expect(
        groupCount,
        "The editor's tool column shows no groups of tools to open.",
    ).toBeGreaterThan(0);
    const toolCards = editor.locator("[data-tool-id]");
    for (let i = 0; i < groupCount; i++) {
        const cardsBefore = await toolCards.count();
        await groupButtons.nth(i).click();
        await expect
            .poll(() => toolCards.count(), {
                timeout: 30000,
                message: `Group ${i + 1} of the editor's tools showed no tools when opened.`,
            })
            .toBeGreaterThan(cardsBefore);
    }
    return toolCards.evaluateAll((cards) =>
        cards.map((card) => card.getAttribute("data-tool-id") ?? ""),
    );
}

/**
 * Select one tool's card, the way a person does, and return the button that runs the tool (the
 * one labelled "Go"). A card shows that button only while it is the selected one.
 */
export async function selectAiImageTool(
    page: Page,
    toolId: string,
): Promise<Locator> {
    const card = aiImageEditorFrame(page).locator(`[data-tool-id="${toolId}"]`);
    // The card's heading is the button that selects it; it is the card's first full-size button.
    await card.locator("button:not(.MuiIconButton-root)").first().click();
    // The run button is the card's only full-width contained button; while a run is under way the
    // same button becomes an outlined "Click to Cancel".
    const runButton = card.locator(
        "button.MuiButton-fullWidth.MuiButton-contained",
    );
    await runButton.waitFor({ state: "visible", timeout: 30000 });
    return runButton;
}

/** Close the editor with its own Cancel button, and wait until Bloom has taken its overlay down. */
export async function cancelAiImageEditor(page: Page): Promise<void> {
    await aiImageEditorFrame(page).getByTestId("bloom-host-cancel").click();
    await expect(page.locator("#ai-image-editor-overlay")).toHaveCount(0, {
        timeout: 30000,
    });
}
