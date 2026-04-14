import { test, expect } from "../../../component-tester/playwrightTest";
import {
    clickWorkspaceTab,
    connectToBloomExe,
    waitForActiveWorkspaceTab,
} from "../../../component-tester/bloomExeCdp";

test.describe("CollectionTopBarControls on Bloom.exe", () => {
    test("shows the real collection top bar controls from the embedded exe", async () => {
        const connection = await connectToBloomExe();

        try {
            await clickWorkspaceTab(connection.page, "Collections");
            await waitForActiveWorkspaceTab("collection");

            await expect(
                connection.page.getByText("Settings", { exact: true }),
            ).toBeVisible();
            await expect(
                connection.page.getByText("Other Collection", { exact: true }),
            ).toBeVisible();
        } finally {
            await connection.browser.close();
        }
    });
});
