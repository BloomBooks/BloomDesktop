import { expect, test } from "../../component-tester/playwrightTest";

const getToolHeader = (page: import("@playwright/test").Page, label: string) =>
    page.locator(".MuiAccordionSummary-root", { hasText: label });

const getToolHeaderTexts = async (
    page: import("@playwright/test").Page,
): Promise<string[]> => {
    return await page
        .locator(".MuiAccordionSummary-content .MuiTypography-root")
        .allTextContents();
};

const routeToolboxApis = async (page: import("@playwright/test").Page) => {
    await page.route("**/bloom/api/toolbox/enabledTools", async (route) => {
        await route.fulfill({
            status: 200,
            contentType: "text/plain",
            body: "talkingBook,settings",
        });
    });
};

test.describe("ToolboxRoot React mode", () => {
    test("switches active React section", async ({ page }) => {
        await routeToolboxApis(page);

        await page.goto("/?component=ToolboxRootTestHarness");

        const talkingBook = getToolHeader(page, "Talking Book Tool");
        const more = getToolHeader(page, "More...");

        await expect(talkingBook).toBeVisible({ timeout: 10000 });
        await expect(more).toBeVisible();

        await talkingBook.click();
        await expect(talkingBook).toHaveAttribute("aria-expanded", "true");

        await more.click();
        await expect(more).toHaveAttribute("aria-expanded", "true");
        await expect(talkingBook).toHaveAttribute("aria-expanded", "false");
    });

    test("initial selection follows restored current tool", async ({
        page,
    }) => {
        await routeToolboxApis(page);

        await page.addInitScript(() => {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            (window as any).toolboxBundle = {
                getTheOneToolbox: () => ({
                    getCurrentTool: () => ({
                        id: () => "decodableReader",
                    }),
                }),
            };
        });

        await page.route("**/bloom/api/toolbox/enabledTools", async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/plain",
                body: "talkingBook,decodableReader,settings",
            });
        });

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        await expect(
            getToolHeader(page, "Decodable Reader Tool"),
        ).toHaveAttribute("aria-expanded", "true");
    });

    test("dynamically added decodable reader can be activated", async ({
        page,
    }) => {
        await routeToolboxApis(page);

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        // Scoped to the accordion headers on purpose: the "More..." panel lists every
        // registered tool as a checkbox, so a bare getByText would match that label and
        // report the tool as present before it has been added as a section.
        await expect(getToolHeader(page, "Decodable Reader Tool")).toHaveCount(
            0,
        );

        await page.evaluate(() => {
            window.dispatchEvent(
                new CustomEvent("toolbox-tool-added", {
                    detail: { toolId: "decodableReaderTool" },
                }),
            );
            window.toolboxReactAdapter?.setActiveToolByToolId(
                "decodableReaderTool",
            );
        });

        await expect(getToolHeader(page, "Decodable Reader Tool")).toBeVisible({
            timeout: 10000,
        });
        await expect(
            getToolHeader(page, "Decodable Reader Tool"),
        ).toHaveAttribute("aria-expanded", "true");
    });

    test("tools are alphabetical on initial render with More last", async ({
        page,
    }) => {
        await routeToolboxApis(page);

        await page.route("**/bloom/api/toolbox/enabledTools", async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/plain",
                body: "talkingBook,canvas,settings",
            });
        });

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        await expect(page.getByText("Canvas Tool").first()).toBeVisible({
            timeout: 10000,
        });

        await expect(await getToolHeaderTexts(page)).toEqual([
            "Canvas Tool",
            "Talking Book Tool",
            "More...",
        ]);
    });

    test("tools stay alphabetical after dynamic add with More last", async ({
        page,
    }) => {
        await routeToolboxApis(page);

        await page.route("**/bloom/api/toolbox/enabledTools", async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/plain",
                body: "talkingBook,canvas,settings",
            });
        });

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        await page.evaluate(() => {
            window.dispatchEvent(
                new CustomEvent("toolbox-tool-added", {
                    detail: { toolId: "decodableReaderTool" },
                }),
            );
        });

        await expect(
            page.getByText("Decodable Reader Tool").first(),
        ).toBeVisible({
            timeout: 10000,
        });

        await expect(await getToolHeaderTexts(page)).toEqual([
            "Canvas Tool",
            "Decodable Reader Tool",
            "Talking Book Tool",
            "More...",
        ]);
    });

    test("accordion headers match More panel tool names", async ({ page }) => {
        await routeToolboxApis(page);

        await page.route("**/bloom/api/toolbox/enabledTools", async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/plain",
                body: "impairmentVisualizer,signLanguage,settings",
            });
        });

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        await expect(await getToolHeaderTexts(page)).toEqual([
            "Impairment Visualizer",
            "Sign Language Tool",
            "Talking Book Tool",
            "More...",
        ]);
    });

    test("headers show tool names without chevrons", async ({ page }) => {
        await routeToolboxApis(page);

        await page.route("**/bloom/api/toolbox/enabledTools", async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/plain",
                body: "canvas,motion,music,settings",
            });
        });

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        await expect(await getToolHeaderTexts(page)).toEqual([
            "Canvas Tool",
            "Motion Tool",
            "Music Tool",
            "Talking Book Tool",
            "More...",
        ]);

        await expect(
            page.locator(".MuiAccordionSummary-expandIconWrapper"),
        ).toHaveCount(0);
    });

    // Canvas, Motion and Music are the tools that need a subscription, so each of their
    // headers carries a badge; Talking Book does not. Every enabled tool's header carries
    // an icon. Which image each icon shows is a static lookup table and is not asserted.
    test("header shows icons and subscription badges", async ({ page }) => {
        await routeToolboxApis(page);

        await page.route("**/bloom/api/toolbox/enabledTools", async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/plain",
                body: "canvas,motion,music,talkingBook,settings",
            });
        });

        await page.goto("/?component=ToolboxRootTestHarness");

        // The harness's first mount waits on the Vite dev server transforming the
        // toolbox module graph, which on a cold server takes well over the default
        // expect timeout; every test in this file uses the same wait for the same reason.
        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        // Five headers: the four tools plus the "More..." (settings) header. Each tool's
        // icon is an inline background image (an svg or png) the component sets from its own
        // table, so that attribute is the state to check; "More..." has no icon of its own.
        const icons = page.getByTestId("toolbox-header-icon");
        await expect(icons).toHaveCount(5);
        const icon = (toolId: string) =>
            icons.and(page.locator(`[data-toolid='${toolId}']`));
        for (const toolId of ["canvas", "motion", "music", "talkingBook"]) {
            await expect(icon(toolId)).toHaveCount(1);
            await expect(icon(toolId)).toHaveAttribute("style", /svg|png/);
        }
        await expect(icon("settings")).toHaveCount(1);
        await expect(icon("settings")).not.toHaveAttribute("style", /svg|png/);

        // Scoped to the headers: the "More..." panel lists the tools with their own
        // badges, which are not what this test is about.
        const headerBadges = (toolId: string) =>
            page
                .locator(".MuiAccordionSummary-root", {
                    has: icon(toolId),
                })
                .getByTestId("subscription-badge");
        for (const toolId of ["canvas", "motion", "music"]) {
            await expect(headerBadges(toolId)).toHaveCount(1);
        }
        for (const toolId of ["talkingBook", "settings"]) {
            await expect(headerBadges(toolId)).toHaveCount(0);
        }
    });
});
