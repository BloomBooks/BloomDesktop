import { test, expect } from "playwright/test";
import {
    clickWorkspaceTab,
    connectToBloomExe,
    waitForActiveWorkspaceTab,
} from "../../component-tester/bloomExeCdp";

test.describe("Bloom exe CDP top bar", () => {
    test("switches embedded workspace tabs through the real top bar", async () => {
        const connection = await connectToBloomExe();

        try {
            await clickWorkspaceTab(connection.page, "collection");
            await waitForActiveWorkspaceTab("collection");
            await expect(connection.page.locator("body")).toHaveClass(
                /collection-mode/,
            );

            await clickWorkspaceTab(connection.page, "publish");
            await waitForActiveWorkspaceTab("publish");
            await expect(connection.page.locator("body")).toHaveClass(
                /publish-mode/,
            );

            await clickWorkspaceTab(connection.page, "edit");
            await waitForActiveWorkspaceTab("edit");
            await expect(connection.page.locator("body")).toHaveClass(
                /edit-mode/,
            );
        } finally {
            await connection.browser.close();
        }
    });

    test("can observe console output and network traffic while attached to Bloom.exe", async () => {
        const connection = await connectToBloomExe();

        try {
            const consoleMessages: string[] = [];
            const requestUrls: string[] = [];

            connection.page.on("console", (message) => {
                consoleMessages.push(message.text());
            });
            connection.page.on("request", (request) => {
                requestUrls.push(request.url());
            });

            await connection.page.evaluate(() => {
                console.log("bloom-exe-cdp-console-smoke");
            });

            await expect
                .poll(() =>
                    consoleMessages.includes("bloom-exe-cdp-console-smoke"),
                )
                .toBe(true);

            await clickWorkspaceTab(connection.page, "publish");
            await waitForActiveWorkspaceTab("publish");

            await expect
                .poll(() =>
                    requestUrls.some((url) =>
                        url.includes("/bloom/api/workspace/selectTab"),
                    ),
                )
                .toBe(true);

            await clickWorkspaceTab(connection.page, "edit");
            await waitForActiveWorkspaceTab("edit");
        } finally {
            await connection.browser.close();
        }
    });
});
