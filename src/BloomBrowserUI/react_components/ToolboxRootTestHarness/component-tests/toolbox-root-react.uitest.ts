import { expect, test } from "../../component-tester/playwrightTest";

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

    await page.route(
        "**/bloom/bookEdit/toolbox/talkingBook/talkingBookToolboxTool.html",
        async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/html",
                body: "<h3 data-toolId='talkingBookTool'>Talking Book</h3><div><div>Talking tool content injected</div></div>",
            });
        },
    );

    await page.route(
        "**/bloom/bookEdit/toolbox/settings/Settings.html",
        async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/html",
                body: "<h3 data-toolId='settingsTool'>More...</h3><div><div>Settings tool content injected</div></div>",
            });
        },
    );

    await page.route(
        "**/bloom/bookEdit/toolbox/readers/decodableReader/decodableReaderToolboxTool.html",
        async (route) => {
            await route.fulfill({
                status: 200,
                contentType: "text/html",
                body: "<h3 data-toolId='decodableReaderTool'>Decodable Reader</h3><div><div>Decodable controls injected</div></div>",
            });
        },
    );
};

test.describe("ToolboxRoot React mode", () => {
    test("renders legacy HTML sections and switches active section", async ({
        page,
    }) => {
        await routeToolboxApis(page);

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        await expect(page.getByText("Talking Book").first()).toBeVisible({
            timeout: 10000,
        });
        await expect(page.getByText("More...").first()).toBeVisible({
            timeout: 10000,
        });

        await page.getByText("Talking Book").first().click();

        await expect(
            page.getByText("Talking tool content injected"),
        ).toBeVisible();

        await page.getByText("More...").first().click();

        await expect(
            page.getByText("Settings tool content injected"),
        ).toBeVisible();
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

        await expect(page.getByText("Decodable controls injected")).toBeVisible(
            {
                timeout: 10000,
            },
        );
    });

    test("dynamically added decodable reader can be activated", async ({
        page,
    }) => {
        await routeToolboxApis(page);

        await page.goto("/?component=ToolboxRootTestHarness");

        await expect(page.getByText("Loading component…")).toHaveCount(0, {
            timeout: 15000,
        });

        await expect(page.getByText("Decodable Reader")).toHaveCount(0);

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

        await expect(page.getByText("Decodable Reader").first()).toBeVisible({
            timeout: 10000,
        });
        await expect(page.getByText("Decodable controls injected")).toBeVisible(
            {
                timeout: 10000,
            },
        );
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

        await expect(page.getByText("Canvas").first()).toBeVisible({
            timeout: 10000,
        });

        await expect(await getToolHeaderTexts(page)).toEqual([
            "Canvas",
            "Talking Book",
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

        await expect(page.getByText("Decodable Reader").first()).toBeVisible({
            timeout: 10000,
        });

        await expect(await getToolHeaderTexts(page)).toEqual([
            "Canvas",
            "Decodable Reader",
            "Talking Book",
            "More...",
        ]);
    });

    test("header shows icons and subscription badges without chevrons", async ({
        page,
    }) => {
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
            "Canvas",
            "Motion",
            "Music",
            "Talking Book",
            "More...",
        ]);

        await expect(
            page.locator(".MuiAccordionSummary-expandIconWrapper"),
        ).toHaveCount(0);

        await expect(page.locator(".subscription-badge")).toHaveCount(3);

        await expect(
            page.locator(".toolbox-react-header-icon[data-toolid='canvas']"),
        ).toHaveCSS("background-image", /Canvas%20Icon\.svg|Canvas Icon\.svg/);
        await expect(
            page.locator(".toolbox-react-header-icon[data-toolid='motion']"),
        ).toHaveCSS("background-image", /motion\.svg/);
        await expect(
            page.locator(".toolbox-react-header-icon[data-toolid='music']"),
        ).toHaveCSS("background-image", /music-notes-white\.svg/);
    });
});
