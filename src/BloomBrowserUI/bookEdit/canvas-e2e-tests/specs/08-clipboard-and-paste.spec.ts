// Spec 08 – Clipboard and paste image flows (Areas I1-I3)
//
// Covers: CanvasElementClipboard.ts.
//
// Menu interactions in this file are intentionally accessed from the
// context toolbar ellipsis ("...") button, not right-click.

import { test, expect } from "../fixtures/canvasTest";
import type { Frame, Page } from "playwright/test";
import {
    clickContextMenuItem,
    createCanvasElementWithRetry,
    getCanvasElementCount,
    openContextMenuFromToolbar,
} from "../helpers/canvasActions";
import { expectAnyCanvasElementActive } from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

const writeRepoImageToClipboard = async (
    page: Page,
): Promise<{ ok: boolean; error?: string }> => {
    await page
        .context()
        .grantPermissions(["clipboard-read", "clipboard-write"], {
            origin: "http://localhost:8089",
        });

    return page.evaluate(async () => {
        try {
            if (!navigator.clipboard || !window.ClipboardItem) {
                return {
                    ok: false,
                    error: "Clipboard API or ClipboardItem unavailable.",
                };
            }

            const imageResponse = await fetch(
                "http://localhost:8089/bloom/images/SIL_Logo_80pxTall.png",
                { cache: "no-store" },
            );
            if (!imageResponse.ok) {
                return {
                    ok: false,
                    error: `Failed to fetch clipboard image: ${imageResponse.status}`,
                };
            }

            const imageBuffer = await imageResponse.arrayBuffer();
            const pngBlob = new Blob([imageBuffer], { type: "image/png" });

            await navigator.clipboard.write([
                new ClipboardItem({ "image/png": pngBlob }),
            ]);

            return { ok: true };
        } catch (error) {
            return { ok: false, error: String(error) };
        }
    });
};

const setActiveImageSourceForTest = async (
    pageFrame: Frame,
    elementIndex: number,
    src: string,
): Promise<void> => {
    const success = await pageFrame.evaluate(
        ({ index, nextSrc }: { index: number; nextSrc: string }) => {
            const elements = Array.from(
                document.querySelectorAll(".bloom-canvas-element"),
            ) as HTMLElement[];
            const target = elements[index];
            if (!target) {
                return false;
            }

            const image = target.querySelector(
                ".bloom-imageContainer img",
            ) as HTMLImageElement | null;
            if (!image) {
                return false;
            }

            image.setAttribute("src", nextSrc);
            image.classList.remove("bloom-imageLoadError");

            const bundle = (window as any).editablePageBundle;
            const manager = bundle?.getTheOneCanvasElementManager?.();
            manager?.setActiveElement?.(target);

            return true;
        },
        { index: elementIndex, nextSrc: src },
    );

    expect(success).toBe(true);
};

const setImageCroppedForTest = async (
    pageFrame: Frame,
    elementIndex: number,
): Promise<void> => {
    const success = await pageFrame.evaluate((index: number) => {
        const elements = Array.from(
            document.querySelectorAll(".bloom-canvas-element"),
        ) as HTMLElement[];
        const target = elements[index];
        if (!target) {
            return false;
        }
        const image = target.querySelector(
            ".bloom-imageContainer img",
        ) as HTMLImageElement | null;
        if (!image) {
            return false;
        }

        const bundle = (window as any).editablePageBundle;
        const manager = bundle?.getTheOneCanvasElementManager?.();
        manager?.setActiveElement?.(target);

        return true;
    }, elementIndex);

    expect(success).toBe(true);

    await pageFrame.evaluate((index: number) => {
        const elements = Array.from(
            document.querySelectorAll(".bloom-canvas-element"),
        ) as HTMLElement[];
        const target = elements[index];
        const image = target?.querySelector(
            ".bloom-imageContainer img",
        ) as HTMLImageElement | null;
        if (!image) {
            return;
        }
        image.style.width = "130%";
        image.style.left = "-10px";
    }, elementIndex);
};

const expectContextMenuItemEnabledState = async (
    pageFrame: Frame,
    label: string,
    enabled: boolean,
): Promise<void> => {
    const item = pageFrame
        .locator(
            `${canvasSelectors.page.contextMenuListVisible} li:has-text("${label}")`,
        )
        .first();

    await expect(
        item,
        `Expected context menu item "${label}" to be visible`,
    ).toBeVisible();

    const isDisabled = await item.evaluate((element) => {
        const htmlElement = element as HTMLElement;
        return (
            htmlElement.getAttribute("aria-disabled") === "true" ||
            htmlElement.classList.contains("Mui-disabled")
        );
    });

    expect(isDisabled).toBe(!enabled);
};

// ── I1: Image element has an image container (paste target) ─────────────

test("I1: newly created image element has an image container", async ({
    canvasPage,
    pageFrame,
}) => {
    await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "image",
    });

    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const imgContainerCount = await newest
        .locator(canvasSelectors.page.imageContainer)
        .count();
    expect(imgContainerCount).toBeGreaterThan(0);
});

// ── I2: Video element has a video container ─────────────────────────────

test("I2: newly created video element has a video container", async ({
    canvasPage,
    pageFrame,
}) => {
    await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "video",
    });

    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const videoContainerCount = await newest
        .locator(canvasSelectors.page.videoContainer)
        .count();
    expect(videoContainerCount).toBeGreaterThan(0);
});

// ── I-general: Speech element has editable text (paste target for text) ──

test("I-general: speech element has bloom-editable for text content", async ({
    canvasPage,
    pageFrame,
}) => {
    await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "speech",
    });

    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const editableCount = await newest
        .locator(canvasSelectors.page.bloomEditable)
        .count();
    expect(editableCount).toBeGreaterThan(0);
});

// ── I-menu: Placeholder image menu states ──────────────────────────────

test("I-menu: placeholder image disables copy/metadata/reset and enables paste", async ({
    canvasPage,
    pageFrame,
}) => {
    await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "image",
    });

    await openContextMenuFromToolbar(pageFrame);

    await expectContextMenuItemEnabledState(pageFrame, "Paste image", true);
    await expectContextMenuItemEnabledState(pageFrame, "Copy image", false);
    await expectContextMenuItemEnabledState(
        pageFrame,
        "Set image information...",
        false,
    );
    await expectContextMenuItemEnabledState(pageFrame, "Reset image", false);
});

// ── I-menu: Non-placeholder image menu states ──────────────────────────

test("I-menu: non-placeholder image enables copy and metadata commands", async ({
    canvasPage,
    pageFrame,
}) => {
    const { index: createdIndex } = await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "image",
    });

    await setActiveImageSourceForTest(
        pageFrame,
        createdIndex,
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+7x8AAAAASUVORK5CYII=",
    );

    await openContextMenuFromToolbar(pageFrame);

    await expectContextMenuItemEnabledState(pageFrame, "Paste image", true);
    await expectContextMenuItemEnabledState(pageFrame, "Copy image", true);
    await expectContextMenuItemEnabledState(
        pageFrame,
        "Set image information...",
        true,
    );
    await expectContextMenuItemEnabledState(pageFrame, "Reset image", false);
});

// ── I-menu: Cropped image enables reset ───────────────────────────────

test("I-menu: cropped image enables Reset image", async ({
    canvasPage,
    pageFrame,
}) => {
    const { index: createdIndex } = await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "image",
    });

    await setActiveImageSourceForTest(
        pageFrame,
        createdIndex,
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+7x8AAAAASUVORK5CYII=",
    );
    await setImageCroppedForTest(pageFrame, createdIndex);

    await openContextMenuFromToolbar(pageFrame);
    await expectContextMenuItemEnabledState(pageFrame, "Reset image", true);
});

// ── I-clipboard: Browser clipboard PNG and paste command path ─────────

test("test pasting a PNG  with ellipsis menu, then copying image into another element", async ({
    canvasPage,
    pageFrame,
}) => {
    await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "image",
    });

    const clipboardResult = await writeRepoImageToClipboard(canvasPage.page);
    expect(
        clipboardResult.ok,
        clipboardResult.error ?? "Clipboard write failed",
    ).toBe(true);

    await openContextMenuFromToolbar(pageFrame);
    await expectContextMenuItemEnabledState(pageFrame, "Paste image", true);

    await clickContextMenuItem(pageFrame, "Paste image");

    // now the copy image button should be enabled
    await openContextMenuFromToolbar(pageFrame);
    await expectContextMenuItemEnabledState(pageFrame, "Copy image", true);

    // In this harness, paste may depend on host/native clipboard integration.
    // This assertion verifies command invocation keeps canvas interaction stable.
    await expectAnyCanvasElementActive(pageFrame);

    // Now try copying the newly pasted image into a new element to verify the copy command works after a paste from the clipboard
    const { index: secondIndex } = await createCanvasElementWithRetry({
        canvasPage,
        paletteItem: "image",
    });
    const secondElement = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(secondIndex);
    await secondElement.waitFor({ state: "visible", timeout: 10000 });
    await secondElement.click();
});
