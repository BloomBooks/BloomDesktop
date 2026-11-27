// A new book made from the Digital Comic Book or the Paper Comic Book template opens in the Edit
// tab with the toolbox open and the Canvas tool expanded, ready to put bubbles on its pages. Each
// template's meta.json asks for that (currentTool "canvasTool", toolboxIsOpen true), and a new
// book starts as a copy of its template's folder, meta.json included (BookStarter).
//
// Automates "Comic Book Opens Canvas Tool" (Test Case ID 338).
//
// Whether the toolbox is open is remembered per book, so the test shuts it in the first comic book
// before making the second: the second book then opens its toolbox and Canvas tool only because its
// own template asks for that, not because the book before it left them open.

import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    expectExpandedTool,
    hideToolbox,
    isToolboxShowing,
    toolboxFrame,
} from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "comic-book-opens-canvas-tool",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The Canvas tool's own controls, in the toolbox frame. */
const kCanvasToolControls = "#canvasToolControls";

/**
 * Check that the book Bloom just opened in the Edit tab shows the toolbox, with the Canvas tool
 * expanded and its palette showing.
 */
async function expectCanvasToolOpened(
    page: Page,
    template: string,
): Promise<void> {
    await expect
        .poll(() => isToolboxShowing(page), {
            timeout: 30000,
            message: `A new ${template} did not open with the toolbox showing.`,
        })
        .toBe(true);
    await expectExpandedTool(
        page,
        "canvas",
        `A new ${template} did not open with the Canvas tool expanded.`,
    );
    const toolbox = toolboxFrame(page);
    await expect(
        toolbox.locator(kCanvasToolControls).first(),
        `The Canvas tool of a new ${template} is expanded but its controls are not showing.`,
    ).toBeVisible({ timeout: 30000 });
    // The palette is what a person uses the tool for; below Pro a subscription notice would sit
    // in its place.
    await expect(
        toolbox
            .locator(`${kCanvasToolControls} [data-testid^=palette-]:visible`)
            .first(),
        `The Canvas tool of a new ${template} shows no palette items.`,
    ).toBeVisible({ timeout: 20000 });
}

test("A new comic book opens the Canvas tool [Test Case ID 338]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    await step(
        "Make a book from the Digital Comic Book template: the Canvas tool is opened",
        async () => {
            await makeBookFromTemplate(page, "Digital Comic Book");
            await expectCanvasToolOpened(page, "Digital Comic Book");
        },
    );

    await step(
        "Shut the toolbox, so the next book cannot inherit an open toolbox",
        async () => {
            await hideToolbox(page);
            expect(
                await isToolboxShowing(page),
                "test setup: the toolbox should be shut",
            ).toBe(false);
        },
    );

    await step(
        "Make a book from the Paper Comic Book template: the Canvas tool is opened",
        async () => {
            await makeBookFromTemplate(page, "Paper Comic Book");
            await expectCanvasToolOpened(page, "Paper Comic Book");
        },
    );
});
