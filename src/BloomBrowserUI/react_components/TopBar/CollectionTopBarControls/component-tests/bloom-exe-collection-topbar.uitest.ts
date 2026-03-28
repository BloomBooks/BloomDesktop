import { test, expect } from "../../../component-tester/playwrightTest";
import {
    connectToBloomExe,
    getBloomTopBarFrame,
    waitForActiveWorkspaceTab,
} from "../../../component-tester/bloomExeCdp";

test.describe("CollectionTopBarControls on Bloom.exe", () => {
    test("shows the real collection top bar controls from the embedded exe", async () => {
        const connection = await connectToBloomExe();

        try {
            const topBarFrame = await getBloomTopBarFrame(connection.page);

            await topBarFrame.getByRole("tab", { name: "Collections" }).click();
            await waitForActiveWorkspaceTab("collection");

            await expect(
                topBarFrame.getByText("Settings", { exact: true }),
            ).toBeVisible();
            await expect(
                topBarFrame.getByText("Other Collection", { exact: true }),
            ).toBeVisible();
        } finally {
            await connection.browser.close();
        }
    });
});
