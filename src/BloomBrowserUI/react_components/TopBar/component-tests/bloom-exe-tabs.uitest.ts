import { test, expect } from "../../component-tester/playwrightTest";
import {
    connectToBloomExe,
    getBloomTopBarFrame,
    waitForActiveWorkspaceTab,
} from "../../component-tester/bloomExeCdp";

test.describe("Bloom exe CDP top bar", () => {
    test("switches embedded workspace tabs through the real top bar iframe", async () => {
        const connection = await connectToBloomExe();

        try {
            const topBarFrame = await getBloomTopBarFrame(connection.page);

            await topBarFrame.getByRole("tab", { name: "Collections" }).click();
            await waitForActiveWorkspaceTab("collection");
            await expect(connection.page.locator("body")).toHaveClass(
                /collection-mode/,
            );

            await topBarFrame.getByRole("tab", { name: "Publish" }).click();
            await waitForActiveWorkspaceTab("publish");
            await expect(connection.page.locator("body")).toHaveClass(
                /publish-mode/,
            );

            await topBarFrame.getByRole("tab", { name: "Edit" }).click();
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
            const topBarFrame = await getBloomTopBarFrame(connection.page);
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

            await topBarFrame.getByRole("tab", { name: "Publish" }).click();
            await waitForActiveWorkspaceTab("publish");

            await expect
                .poll(() =>
                    requestUrls.some((url) =>
                        url.includes("/bloom/api/workspace/selectTab"),
                    ),
                )
                .toBe(true);

            await topBarFrame.getByRole("tab", { name: "Edit" }).click();
            await waitForActiveWorkspaceTab("edit");
        } finally {
            await connection.browser.close();
        }
    });
});
