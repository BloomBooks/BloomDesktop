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
            // The harness publishes this accessor for us; see ToolboxRootTestHarness.tsx.
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            (window as any)
                .getToolboxReactAdapterForTests?.()
                ?.setActiveToolByToolId("decodableReaderTool");
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

    // Split out of the test above and disabled rather than deleted, because it asserts on
    // things the React header has never produced, and deciding what it *should* produce is a
    // product question, not a test fix:
    //  - ".subscription-badge" is emitted by the legacy jQuery toolbox
    //    (toolbox.ts buildToolboxHeader), not by the React header, so the count is 0 here.
    //  - ".toolbox-react-header-icon" does not exist anywhere in the app — no component or
    //    stylesheet has ever defined that class.
    // It also checks tool icons via computed background-image, which
    // src/BloomBrowserUI/AGENTS.md tells us not to do ("Don't check for styles in tests as a
    // way to know the status of something... have components add css classes"). So whoever
    // re-enables this should first decide whether the React header renders subscription
    // badges and icons at all, then give those elements stable classes/test ids to assert on.
    test.fixme(
        "header shows icons and subscription badges",
        async ({ page }) => {
            await routeToolboxApis(page);

            await page.route(
                "**/bloom/api/toolbox/enabledTools",
                async (route) => {
                    await route.fulfill({
                        status: 200,
                        contentType: "text/plain",
                        body: "canvas,motion,music,settings",
                    });
                },
            );

            await page.goto("/?component=ToolboxRootTestHarness");

            await expect(page.getByText("Loading component…")).toHaveCount(0, {
                timeout: 15000,
            });

            await expect(page.locator(".subscription-badge")).toHaveCount(3);

            await expect(
                page.locator(
                    ".toolbox-react-header-icon[data-toolid='canvas']",
                ),
            ).toHaveCSS(
                "background-image",
                /Canvas%20Icon\.svg|Canvas Icon\.svg/,
            );
            await expect(
                page.locator(
                    ".toolbox-react-header-icon[data-toolid='motion']",
                ),
            ).toHaveCSS("background-image", /motion\.svg/);
            await expect(
                page.locator(".toolbox-react-header-icon[data-toolid='music']"),
            ).toHaveCSS("background-image", /music-notes-white\.svg/);
        },
    );
});
