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
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    openContextMenuFromToolbar,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

const createElementWithRetry = async ({
    page,
    toolboxFrame,
    pageFrame,
    paletteItem,
}: {
    page: Page;
    toolboxFrame: Frame;
    pageFrame: Frame;
    paletteItem: "image" | "video" | "speech";
}): Promise<number> => {
    const maxAttempts = 3;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(pageFrame);
        await dragPaletteItemToCanvas({
            page,
            toolboxFrame,
            pageFrame,
            paletteItem,
        });

        try {
            await expectCanvasElementCountToIncrease(pageFrame, beforeCount);
            return beforeCount;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
        }
    }

    throw new Error("Could not create canvas element after bounded retries.");
};

const writePngToClipboard = async (
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

            const canvas = document.createElement("canvas");
            canvas.width = 4;
            canvas.height = 4;
            const context = canvas.getContext("2d");
            if (!context) {
                return { ok: false, error: "Could not get 2D context." };
            }

            context.fillStyle = "#ff0000";
            context.fillRect(0, 0, 4, 4);

            const pngBlob = await new Promise<Blob | null>((resolve) =>
                canvas.toBlob((blob) => resolve(blob), "image/png"),
            );
            if (!pngBlob) {
                return { ok: false, error: "Could not create PNG blob." };
            }

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

const shouldRunNativePasteInvocationTest =
    process.env.BLOOM_CANVAS_E2E_ENABLE_NATIVE_PASTE === "true";

// ── I1: Image element has an image container (paste target) ─────────────

test("I1: newly created image element has an image container", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
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
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
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
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
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
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
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
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const createdIndex = await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
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
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const createdIndex = await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
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

const pasteInvocationTest = shouldRunNativePasteInvocationTest
    ? test
    : test.skip;

pasteInvocationTest(
    "I-clipboard: can seed PNG clipboard and invoke Paste image from ellipsis menu",
    async ({ page, toolboxFrame, pageFrame }) => {
        await createElementWithRetry({
            page,
            toolboxFrame,
            pageFrame,
            paletteItem: "image",
        });

        const clipboardResult = await writePngToClipboard(page);
        expect(
            clipboardResult.ok,
            clipboardResult.error ?? "Clipboard write failed",
        ).toBe(true);

        await openContextMenuFromToolbar(pageFrame);
        await expectContextMenuItemEnabledState(pageFrame, "Paste image", true);

        await clickContextMenuItem(pageFrame, "Paste image");

        // In this harness, paste may depend on host/native clipboard integration.
        // This assertion verifies command invocation keeps canvas interaction stable.
        await expectAnyCanvasElementActive(pageFrame);
    },
);
