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
    // The menu leaves "Edit with AI..." out until Bloom has answered whether the AI Image Editing
    // feature is on, and it asks only once the menu's page has loaded, so a menu opened soon
    // after a new book shows can lack the command for a moment.
    let command: { id: string; enabled: boolean } | undefined;
    await expect
        .poll(
            async () => {
                command = (await getCanvasElementMenuItems(page)).find(
                    (item) => item.id === kEditWithAiCommand,
                );
                return command !== undefined;
            },
            {
                timeout: 30000,
                message: `The picture's menu has no "Edit with AI..." command (${kEditWithAiCommand}).`,
            },
        )
        .toBe(true);
    expect(
        command?.enabled,
        'The picture\'s "Edit with AI..." command is not enabled.',
    ).toBe(true);
    await clickCanvasElementMenuItem(page, kEditWithAiCommand);
    await aiImageEditorFrame(page)
        .getByTestId("thumbnail-strip-bookImages")
        .waitFor({ state: "visible", timeout: 60000 });
}

/**
 * Make every later launch of the editor in this page start as it does for a user who has not
 * connected Bloom to OpenRouter, whatever key the Windows user running the test has stored.
 * Bloom hands the editor the stored key in its launch reply, and that store belongs to the Windows
 * user, not to the test's collection or settings folder, so without this the editor would start
 * connected on a developer's machine and unconnected on the nightly runner.
 */
export async function launchAiImageEditorWithoutOpenRouterKey(
    page: Page,
): Promise<void> {
    await page.route("**/api/aiImageEditor/launch", async (route) => {
        const response = await route.fetch();
        const launchData = await response.json();
        await route.fulfill({
            response,
            json: { ...launchData, apiKey: null },
        });
    });
}

/**
 * The button of the welcome the editor puts up as it opens for a user with no OpenRouter
 * connection, which closes it without connecting. The welcome has no test id, so its button is
 * found by its English label.
 */
function justLookAroundButton(page: Page): Locator {
    return aiImageEditorFrame(page).getByRole("button", {
        name: "I just want to look around",
    });
}

/**
 * Close the welcome that offers to connect the editor to OpenRouter, the way a person who only
 * wants to look around does, and wait until it has gone. The editor puts it up once its state has
 * loaded, which can be after its book pictures show.
 */
export async function declineOpenRouterWelcome(page: Page): Promise<void> {
    const button = justLookAroundButton(page);
    await expect(
        button,
        "The AI image editor did not offer to connect to OpenRouter, though it had no key.",
    ).toBeVisible({ timeout: 30000 });
    await button.click();
    await expect(button).toBeHidden({ timeout: 30000 });
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
