import type { Frame, Page } from "playwright/test";

const currentPageUrl = "http://localhost:8089/bloom/CURRENTPAGE";

const waitForFrame = async (
    page: Page,
    predicate: (frame: Frame) => boolean,
    label: string,
): Promise<Frame> => {
    const timeoutMs = 15000;
    const pollMs = 100;
    const endTime = Date.now() + timeoutMs;

    while (Date.now() < endTime) {
        const frame = page.frames().find((candidate) => predicate(candidate));
        if (frame) {
            return frame;
        }
        await page.waitForTimeout(pollMs);
    }

    throw new Error(`Timed out waiting for ${label} frame.`);
};

export const gotoCurrentPage = async (page: Page): Promise<void> => {
    await page.goto(currentPageUrl, { waitUntil: "domcontentloaded" });
};

export const getToolboxFrame = async (page: Page): Promise<Frame> => {
    return waitForFrame(
        page,
        (frame) =>
            frame.name() === "toolbox" || /toolboxcontent/i.test(frame.url()),
        "toolbox",
    );
};

export const getPageFrame = async (page: Page): Promise<Frame> => {
    return waitForFrame(
        page,
        (frame) => {
            if (frame === page.mainFrame()) {
                return false;
            }
            if (frame.name() === "page") {
                return true;
            }
            const url = frame.url();
            if (!url || /toolboxcontent/i.test(url)) {
                return false;
            }
            return /page-memsim/i.test(url);
        },
        "editable page",
    );
};

export const openCanvasToolTab = async (toolboxFrame: Frame): Promise<void> => {
    const controls = toolboxFrame.locator("#canvasToolControls").first();
    if (await controls.isVisible().catch(() => false)) {
        return;
    }

    const canvasToolHeader = toolboxFrame
        .locator(
            'h3[data-toolid="canvasTool"], h3[data-toolid="canvas"], h3[data-toolid*="canvas"], h3:has-text("Canvas")',
        )
        .first();

    await canvasToolHeader.click();
    await controls.waitFor({
        state: "visible",
        timeout: 10000,
    });
};

export const waitForCanvasReady = async (pageFrame: Frame): Promise<void> => {
    await pageFrame.locator(".bloom-canvas").first().waitFor({
        state: "visible",
        timeout: 10000,
    });
};
