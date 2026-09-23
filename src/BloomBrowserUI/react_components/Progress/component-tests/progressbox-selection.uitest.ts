import { expect, test } from "../../component-tester/playwrightTest";
import { getProgressLog, setupProgressBox } from "./test-helpers";

const fullMessage =
    "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november";
const longTokenMessage = "0123456789".repeat(40);

test.describe("ProgressBox selection", () => {
    test("allows selecting only part of a log line", async ({ page }) => {
        await setupProgressBox(page, {
            preloadedProgressEvents: [
                {
                    id: "message",
                    clientContext: "progress-test",
                    message: fullMessage,
                    progressKind: "Progress",
                },
            ],
        });

        const log = getProgressLog(page);
        await expect(log).toContainText(fullMessage);

        const line = log.locator("p").first();
        const box = await line.boundingBox();
        if (!box) {
            throw new Error("ProgressBox log line was not rendered.");
        }

        const dragY = box.y + box.height * 0.6;
        await page.mouse.move(box.x + box.width * 0.15, dragY);
        await page.mouse.down();
        await page.mouse.move(box.x + box.width * 0.6, dragY, {
            steps: 8,
        });
        await page.mouse.up();

        const selectedText = await page.evaluate(() => {
            return window.getSelection()?.toString() ?? "";
        });
        const normalizedSelection = selectedText.replace(/\s+/g, " ").trim();

        expect(normalizedSelection.length).toBeGreaterThan(0);
        expect(normalizedSelection.length).toBeLessThan(fullMessage.length);
        expect(fullMessage.includes(normalizedSelection)).toBe(true);
    });

    test("wraps long content without horizontal scrolling", async ({
        page,
    }) => {
        await setupProgressBox(page, {
            preloadedProgressEvents: [
                {
                    id: "message",
                    clientContext: "progress-test",
                    message: longTokenMessage,
                    progressKind: "Progress",
                },
            ],
        });

        const log = getProgressLog(page);
        await expect(log).toContainText(longTokenMessage);

        await log.evaluate((element) => {
            const logElement = element as HTMLDivElement;
            logElement.style.width = "220px";
        });

        const hasHorizontalOverflow = await log.evaluate((element) => {
            const logElement = element as HTMLDivElement;
            return logElement.scrollWidth > logElement.clientWidth + 1;
        });

        expect(hasHorizontalOverflow).toBe(false);
    });
});
