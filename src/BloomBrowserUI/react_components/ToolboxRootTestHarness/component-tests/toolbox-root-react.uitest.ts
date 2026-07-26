// Page comes from the same re-export as test/expect, so that we resolve Playwright out of
// the component-tester's own node_modules (see playwrightTest.ts).
import { expect, test, type Page } from "../../component-tester/playwrightTest";

// Tests of the toolbox sidebar UI (ToolboxRoot.tsx) and the adapter that the rest of the
// toolbox drives it through (toolboxReactAdapter.ts). ToolboxRootTestHarness.tsx stands in
// for toolbox.ts: it registers a few tools and populates the toolbox through the real
// adapter. See that file for which tools it offers and which one it restores as current.

const harnessUrl = "/?component=ToolboxRootTestHarness";

// The header of one section. AccordionSummary is the clickable header, and it is what
// carries aria-expanded.
const getToolHeader = (page: Page, label: string) =>
    page.locator(".MuiAccordionSummary-root", { hasText: label });

// The labels of all the section headers, in the order the toolbox shows them.
const getToolHeaderTexts = async (page: Page): Promise<string[]> => {
    return await page
        .locator(".MuiAccordionSummary-content .MuiTypography-root")
        .allTextContents();
};

// The icon of one section's header. ToolboxRoot puts the tool's canonical id on it and
// shows the tool's iconPath() as its background image.
const getToolHeaderIcon = (page: Page, toolId: string) =>
    page.locator(`.MuiAccordionSummary-root span[data-toolid="${toolId}"]`);

// The subscription badges in the section headers. (The "More..." section has badges of its
// own beside its checkboxes, so these assertions must not look at the whole page.)
const getHeaderSubscriptionBadges = (page: Page) =>
    page.locator(
        '.MuiAccordionSummary-root img[src*="bloom-enterprise-badge.svg"]',
    );

// Goes to the harness and waits until the toolbox has finished populating itself.
const gotoHarness = async (page: Page): Promise<void> => {
    await page.goto(harnessUrl);

    await expect(page.getByText("Loading component…")).toHaveCount(0, {
        timeout: 15000,
    });
    await expect(getToolHeader(page, "More...")).toBeVisible({
        timeout: 10000,
    });
};

// Does what toolbox.ts does when the user turns a tool on: tells the toolbox to offer a
// section for it, and makes it the active one.
const addToolAndMakeItActive = async (
    page: Page,
    toolId: string,
): Promise<void> => {
    await page.evaluate((idOfToolToAdd) => {
        // The harness publishes this accessor for us; see ToolboxRootTestHarness.tsx.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const adapter = (window as any).getToolboxReactAdapterForTests?.();
        adapter?.addTool(idOfToolToAdd);
        adapter?.setActiveToolByToolId(idOfToolToAdd);
    }, toolId);
};

test.describe("ToolboxRoot", () => {
    test("clicking a section header makes it the active section", async ({
        page,
    }) => {
        await gotoHarness(page);

        const impairmentVisualizer = getToolHeader(
            page,
            "Impairment Visualizer",
        );
        const more = getToolHeader(page, "More...");

        await impairmentVisualizer.click();
        await expect(impairmentVisualizer).toHaveAttribute(
            "aria-expanded",
            "true",
        );

        await more.click();
        await expect(more).toHaveAttribute("aria-expanded", "true");
        await expect(impairmentVisualizer).toHaveAttribute(
            "aria-expanded",
            "false",
        );
    });

    test("opens the tool the book was last using, not just the first one", async ({
        page,
    }) => {
        await gotoHarness(page);

        // The harness restores Motion as the current tool, and Impairment Visualizer sorts
        // ahead of it, so this only passes if setActiveToolByToolId() was honored.
        await expect(getToolHeader(page, "Motion Tool")).toHaveAttribute(
            "aria-expanded",
            "true",
        );
        await expect(
            getToolHeader(page, "Impairment Visualizer"),
        ).toHaveAttribute("aria-expanded", "false");
    });

    test("a tool added later gets a section and can be made active", async ({
        page,
    }) => {
        await gotoHarness(page);

        await expect(getToolHeader(page, "Canvas Tool")).toHaveCount(0);

        await addToolAndMakeItActive(page, "canvas");

        const canvas = getToolHeader(page, "Canvas Tool");
        await expect(canvas).toBeVisible({ timeout: 10000 });
        await expect(canvas).toHaveAttribute("aria-expanded", "true");
    });

    test("sections are alphabetical with More last", async ({ page }) => {
        await gotoHarness(page);

        expect(await getToolHeaderTexts(page)).toEqual([
            "Impairment Visualizer",
            "Motion Tool",
            "More...",
        ]);
    });

    test("sections stay alphabetical with More last after a tool is added", async ({
        page,
    }) => {
        await gotoHarness(page);

        await addToolAndMakeItActive(page, "canvas");
        await expect(getToolHeader(page, "Canvas Tool")).toBeVisible({
            timeout: 10000,
        });

        expect(await getToolHeaderTexts(page)).toEqual([
            "Canvas Tool",
            "Impairment Visualizer",
            "Motion Tool",
            "More...",
        ]);
    });

    test("header labels are the ones derived from the tool ids", async ({
        page,
    }) => {
        await gotoHarness(page);

        // "Impairment Visualizer" (rather than "Impairment Visualizer Tool") is the one
        // exception to the "<Tool Name> Tool" convention; see toolIds.getToolLabelInfo.
        // The Settings tool's section is labelled "More...".
        expect(await getToolHeaderTexts(page)).toEqual([
            "Impairment Visualizer",
            "Motion Tool",
            "More...",
        ]);
    });

    test("headers show each tool's icon", async ({ page }) => {
        await gotoHarness(page);

        await expect(getToolHeaderIcon(page, "impairmentVisualizer")).toHaveCSS(
            "background-image",
            /blind-eye-white\.svg/,
        );
        await expect(getToolHeaderIcon(page, "motion")).toHaveCSS(
            "background-image",
            /motion\.svg/,
        );
    });

    test("headers show a subscription badge only where the tool needs one, and no expand chevrons", async ({
        page,
    }) => {
        await gotoHarness(page);

        // Motion requires a subscription; Impairment Visualizer and "More..." do not.
        await expect(getHeaderSubscriptionBadges(page)).toHaveCount(1);
        await expect(
            getToolHeader(page, "Motion Tool").locator(
                'img[src*="bloom-enterprise-badge.svg"]',
            ),
        ).toHaveCount(1);

        // Adding Canvas, which also requires a subscription, adds a second badge.
        await addToolAndMakeItActive(page, "canvas");
        await expect(getToolHeader(page, "Canvas Tool")).toBeVisible({
            timeout: 10000,
        });
        await expect(getHeaderSubscriptionBadges(page)).toHaveCount(2);

        // ToolboxRoot deliberately gives its AccordionSummaries no expandIcon.
        await expect(
            page.locator(".MuiAccordionSummary-expandIconWrapper"),
        ).toHaveCount(0);
    });
});
