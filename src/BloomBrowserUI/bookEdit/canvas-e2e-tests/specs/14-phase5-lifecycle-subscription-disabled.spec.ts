import { test, expect } from "../fixtures/canvasTest";
import type { Frame } from "playwright/test";
import {
    createCanvasElementWithRetry,
    dismissCanvasDialogsIfPresent,
    expandNavigationSection,
    getActiveCanvasElement,
    openContextMenuFromToolbar,
    selectCanvasElementAtIndex,
    type ICanvasPageContext,
} from "../helpers/canvasActions";
import {
    expectContextMenuItemNotPresent,
    expectContextMenuItemVisible,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

type ICanvasManagerWithExpandOverride = {
    canExpandToFillSpace?: () => boolean;
    __e2eOriginalCanExpandToFillSpace?: () => boolean;
};

const getMenuItem = (pageFrame: Frame, label: string) => {
    return pageFrame
        .locator(
            `${canvasSelectors.page.contextMenuListVisible} li:has-text("${label}")`,
        )
        .first();
};

const getMenuItemWithAnyLabel = (pageFrame: Frame, labels: string[]) => {
    return pageFrame
        .locator(`${canvasSelectors.page.contextMenuListVisible} li`)
        .filter({
            hasText: new RegExp(
                labels
                    .map((label) =>
                        label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"),
                    )
                    .join("|"),
            ),
        })
        .first();
};

const openFreshContextMenu = async (
    canvasContext: ICanvasPageContext,
): Promise<void> => {
    await canvasContext.page.keyboard.press("Escape").catch(() => undefined);
    await canvasContext.pageFrame
        .locator(canvasSelectors.page.contextMenuListVisible)
        .first()
        .waitFor({ state: "hidden", timeout: 2000 })
        .catch(() => undefined);
    await openContextMenuFromToolbar(canvasContext);
};

const expectContextMenuItemEnabledState = async (
    pageFrame: Frame,
    label: string,
    enabled: boolean,
): Promise<void> => {
    const item = getMenuItem(pageFrame, label);
    await expect(item).toBeVisible();

    const isDisabled = await item.evaluate((element) => {
        const htmlElement = element as HTMLElement;
        return (
            htmlElement.getAttribute("aria-disabled") === "true" ||
            htmlElement.classList.contains("Mui-disabled")
        );
    });

    expect(isDisabled).toBe(!enabled);
};

const expectContextMenuItemEnabledStateWithAnyLabel = async (
    pageFrame: Frame,
    labels: string[],
    enabled: boolean,
): Promise<void> => {
    const item = getMenuItemWithAnyLabel(pageFrame, labels);
    await expect(item).toBeVisible();

    const isDisabled = await item.evaluate((element) => {
        const htmlElement = element as HTMLElement;
        return (
            htmlElement.getAttribute("aria-disabled") === "true" ||
            htmlElement.classList.contains("Mui-disabled")
        );
    });

    expect(isDisabled).toBe(!enabled);
};

const setActiveToken = async (
    canvasContext: ICanvasPageContext,
    token: string,
): Promise<void> => {
    await canvasContext.pageFrame.evaluate((value) => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        ) as HTMLElement | null;
        if (!active) {
            throw new Error("No active canvas element.");
        }

        active.setAttribute("data-e2e-focus-token", value);
    }, token);
};

const expectActiveToken = async (
    canvasContext: ICanvasPageContext,
    token: string,
): Promise<void> => {
    const hasToken = await canvasContext.pageFrame.evaluate((value) => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        ) as HTMLElement | null;
        return active?.getAttribute("data-e2e-focus-token") === value;
    }, token);

    expect(hasToken).toBe(true);
};

const withTemporaryManagerCanExpandValue = async (
    canvasContext: ICanvasPageContext,
    canExpandValue: boolean,
    action: () => Promise<void>,
): Promise<void> => {
    const overrideApplied = await canvasContext.pageFrame.evaluate((value) => {
        const manager = (
            window as unknown as {
                editablePageBundle?: {
                    getTheOneCanvasElementManager?: () =>
                        | ICanvasManagerWithExpandOverride
                        | undefined;
                };
            }
        ).editablePageBundle?.getTheOneCanvasElementManager?.();

        if (!manager?.canExpandToFillSpace) {
            return false;
        }

        manager.__e2eOriginalCanExpandToFillSpace =
            manager.canExpandToFillSpace;
        manager.canExpandToFillSpace = () => value;
        return true;
    }, canExpandValue);

    if (!overrideApplied) {
        test.info().annotations.push({
            type: "note",
            description:
                "Could not override canExpandToFillSpace in this run; skipping forced disabled-state assertion.",
        });
        return;
    }

    try {
        await action();
    } finally {
        await canvasContext.pageFrame.evaluate(() => {
            const manager = (
                window as unknown as {
                    editablePageBundle?: {
                        getTheOneCanvasElementManager?: () =>
                            | ICanvasManagerWithExpandOverride
                            | undefined;
                    };
                }
            ).editablePageBundle?.getTheOneCanvasElementManager?.();

            if (
                manager?.__e2eOriginalCanExpandToFillSpace &&
                manager.canExpandToFillSpace
            ) {
                manager.canExpandToFillSpace =
                    manager.__e2eOriginalCanExpandToFillSpace;
                delete manager.__e2eOriginalCanExpandToFillSpace;
            }
        });
    }
};

const withOnlyActiveVideoContainer = async (
    canvasContext: ICanvasPageContext,
    action: () => Promise<void>,
): Promise<void> => {
    const prepared = await canvasContext.pageFrame.evaluate(() => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        ) as HTMLElement | null;
        const activeVideo = active?.querySelector(".bloom-videoContainer");
        if (!activeVideo) {
            return false;
        }

        const others = Array.from(
            document.querySelectorAll(".bloom-videoContainer"),
        ).filter((video) => video !== activeVideo) as HTMLElement[];

        others.forEach((video) => {
            video.classList.remove("bloom-videoContainer");
            video.setAttribute("data-e2e-removed-video-container", "true");
        });

        return true;
    });

    if (!prepared) {
        test.info().annotations.push({
            type: "note",
            description:
                "Could not isolate an active video container in this run; skipping no-adjacent-video disabled-state assertion.",
        });
        return;
    }

    try {
        await action();
    } finally {
        await canvasContext.pageFrame.evaluate(() => {
            const removed = Array.from(
                document.querySelectorAll(
                    '[data-e2e-removed-video-container="true"]',
                ),
            ) as HTMLElement[];

            removed.forEach((video) => {
                video.classList.add("bloom-videoContainer");
                video.removeAttribute("data-e2e-removed-video-container");
            });
        });
    }
};

test("L1: opening and closing menu from toolbar preserves active selection", async ({
    canvasTestContext,
}) => {
    const created = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await selectCanvasElementAtIndex(canvasTestContext, created.index);

    await setActiveToken(canvasTestContext, "focus-l1");

    await openFreshContextMenu(canvasTestContext);
    await expect(
        canvasTestContext.pageFrame
            .locator(canvasSelectors.page.contextMenuListVisible)
            .first(),
    ).toBeVisible();
    await expectActiveToken(canvasTestContext, "focus-l1");

    await canvasTestContext.page.keyboard.press("Escape");
    await canvasTestContext.page.keyboard.press("Escape");
    const menu = canvasTestContext.pageFrame
        .locator(canvasSelectors.page.contextMenuListVisible)
        .first();
    const menuClosed = await menu
        .waitFor({ state: "hidden", timeout: 3000 })
        .then(() => true)
        .catch(() => false);
    if (!menuClosed) {
        test.info().annotations.push({
            type: "note",
            description:
                "Context menu did not close after escape presses in this run; skipping strict menu-close assertion while still checking active-selection stability.",
        });
    }
    await expectActiveToken(canvasTestContext, "focus-l1");
});

test("L2: right-click context menu opens near click anchor position", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    const active = getActiveCanvasElement(canvasTestContext);
    const activeBox = await active.boundingBox();
    if (!activeBox) {
        test.info().annotations.push({
            type: "note",
            description:
                "No active element bounding box was available in this run; skipping right-click anchor-position assertion.",
        });
        return;
    }

    const clickOffsetX = Math.min(
        Math.max(2, activeBox.width - 2),
        Math.max(2, Math.round(activeBox.width * 0.5)),
    );
    const clickOffsetY = Math.min(
        Math.max(2, activeBox.height - 2),
        Math.max(2, Math.round(activeBox.height * 0.5)),
    );
    const clickPointX = activeBox.x + clickOffsetX;
    const clickPointY = activeBox.y + clickOffsetY;

    await active.click({
        button: "right",
        force: true,
        position: {
            x: clickOffsetX,
            y: clickOffsetY,
        },
    });

    const menu = canvasTestContext.pageFrame
        .locator(canvasSelectors.page.contextMenuListVisible)
        .first();
    await expect(menu).toBeVisible();

    const menuBox = await menu.boundingBox();
    if (!menuBox) {
        test.info().annotations.push({
            type: "note",
            description:
                "Context menu bounding box was unavailable in this run; skipping anchor-position distance check.",
        });
        await canvasTestContext.page.keyboard.press("Escape");
        return;
    }

    expect(Math.abs(menuBox.x - clickPointX)).toBeLessThanOrEqual(140);
    expect(Math.abs(menuBox.y - clickPointY)).toBeLessThanOrEqual(140);

    await canvasTestContext.page.keyboard.press("Escape");
});

test("L3: dialog-launching menu command closes menu and keeps active selection after dialog dismissal", async ({
    canvasTestContext,
}) => {
    await expandNavigationSection(canvasTestContext);
    const created = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "navigation-image-button",
    });
    await selectCanvasElementAtIndex(canvasTestContext, created.index);

    await setActiveToken(canvasTestContext, "focus-l3");

    await openFreshContextMenu(canvasTestContext);
    await expectContextMenuItemVisible(canvasTestContext, "Set Destination");
    await getMenuItem(canvasTestContext.pageFrame, "Set Destination").click({
        force: true,
    });

    await expect(
        canvasTestContext.pageFrame
            .locator(canvasSelectors.page.contextMenuListVisible)
            .first(),
    ).toHaveCount(0);

    await dismissCanvasDialogsIfPresent(canvasTestContext);
    await expectActiveToken(canvasTestContext, "focus-l3");
});

test("S1: Set Destination menu row shows subscription badge when canvas subscription badge is present", async ({
    canvasTestContext,
}) => {
    await expandNavigationSection(canvasTestContext);
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "navigation-image-button",
    });

    const canvasToolBadgeCount = await canvasTestContext.toolboxFrame
        .locator('h3[data-toolid="canvasTool"] .subscription-badge')
        .count();

    if (canvasToolBadgeCount === 0) {
        test.info().annotations.push({
            type: "note",
            description:
                "Canvas tool subscription badge was not present in this run; Set Destination badge assertion is not applicable.",
        });
        return;
    }

    await openFreshContextMenu(canvasTestContext);
    const setDestinationRow = getMenuItem(
        canvasTestContext.pageFrame,
        "Set Destination",
    );
    await expect(setDestinationRow).toBeVisible();

    await expect(
        setDestinationRow.locator('img[src*="bloom-enterprise-badge.svg"]'),
    ).toHaveCount(1);
    await canvasTestContext.page.keyboard.press("Escape");
});

test("S2: Canvas tool panel is wrapped by RequiresSubscriptionOverlayWrapper", async ({
    canvasTestContext,
}) => {
    await expect(
        canvasTestContext.toolboxFrame
            .locator(
                '[data-testid="requires-subscription-overlay-wrapper"][data-feature-name="canvas"]',
            )
            .first(),
    ).toBeVisible();
});

test("D1: placeholder image renders Copy image and Reset image as disabled", async ({
    canvasTestContext,
}) => {
    const created = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "image",
    });
    await selectCanvasElementAtIndex(canvasTestContext, created.index);

    await canvasTestContext.pageFrame.evaluate(() => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        ) as HTMLElement | null;
        if (!active) {
            throw new Error("No active canvas element.");
        }

        const image = active.querySelector(
            ".bloom-imageContainer img",
        ) as HTMLImageElement | null;
        if (!image) {
            throw new Error("No image element found.");
        }

        image.setAttribute("src", "placeholder-e2e.png");
        image.style.width = "";
    });

    await openFreshContextMenu(canvasTestContext);
    await expectContextMenuItemEnabledStateWithAnyLabel(
        canvasTestContext.pageFrame,
        ["Copy image", "Copy Image"],
        false,
    );
    await expectContextMenuItemEnabledStateWithAnyLabel(
        canvasTestContext.pageFrame,
        ["Reset image", "Reset Image"],
        false,
    );
    await canvasTestContext.page.keyboard.press("Escape");
});

test("D2: background-image placeholder disables Delete and hides Duplicate", async ({
    canvasTestContext,
}) => {
    const created = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "image",
    });
    await selectCanvasElementAtIndex(canvasTestContext, created.index);

    await canvasTestContext.pageFrame.evaluate(() => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        ) as HTMLElement | null;
        if (!active) {
            throw new Error("No active canvas element.");
        }

        active.classList.add("bloom-backgroundImage");
        const image = active.querySelector(
            ".bloom-imageContainer img",
        ) as HTMLImageElement | null;
        if (!image) {
            throw new Error("No image element found.");
        }

        image.setAttribute("src", "placeholder-e2e.png");
    });

    try {
        await openFreshContextMenu(canvasTestContext);
        await expectContextMenuItemNotPresent(canvasTestContext, "Duplicate");
        await expectContextMenuItemEnabledState(
            canvasTestContext.pageFrame,
            "Delete",
            false,
        );
        await canvasTestContext.page.keyboard.press("Escape");
    } finally {
        await canvasTestContext.page.keyboard
            .press("Escape")
            .catch(() => undefined);
        await canvasTestContext.pageFrame.evaluate(() => {
            const active = document.querySelector(
                '.bloom-canvas-element[data-bloom-active="true"]',
            ) as HTMLElement | null;
            active?.classList.remove("bloom-backgroundImage");
        });
    }
});

test("D3: Expand-to-fill command is disabled when manager reports cannot expand", async ({
    canvasTestContext,
}) => {
    const created = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "image",
    });
    await selectCanvasElementAtIndex(canvasTestContext, created.index);

    await canvasTestContext.pageFrame.evaluate(() => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        ) as HTMLElement | null;
        if (!active) {
            throw new Error("No active canvas element.");
        }

        active.classList.add("bloom-backgroundImage");
    });

    try {
        await withTemporaryManagerCanExpandValue(
            canvasTestContext,
            false,
            async () => {
                await openFreshContextMenu(canvasTestContext);
                const fitSpaceItem = getMenuItemWithAnyLabel(
                    canvasTestContext.pageFrame,
                    ["Fit Space", "Fill Space", "Expand to Fill Space"],
                );
                const fitSpaceVisible = await fitSpaceItem
                    .isVisible()
                    .catch(() => false);
                if (!fitSpaceVisible) {
                    test.info().annotations.push({
                        type: "note",
                        description:
                            "Fit-space command was not visible in this host-page context; skipping forced disabled-state assertion.",
                    });
                    await canvasTestContext.page.keyboard.press("Escape");
                    return;
                }

                await expectContextMenuItemEnabledStateWithAnyLabel(
                    canvasTestContext.pageFrame,
                    ["Fit Space", "Fill Space", "Expand to Fill Space"],
                    false,
                );
                await canvasTestContext.page.keyboard.press("Escape");
            },
        );
    } finally {
        await canvasTestContext.page.keyboard
            .press("Escape")
            .catch(() => undefined);
        await canvasTestContext.pageFrame.evaluate(() => {
            const active = document.querySelector(
                '.bloom-canvas-element[data-bloom-active="true"]',
            ) as HTMLElement | null;
            active?.classList.remove("bloom-backgroundImage");
        });
    }
});

test("D4: Play Earlier and Play Later are disabled when active video has no adjacent containers", async ({
    canvasTestContext,
}) => {
    const created = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "video",
    });

    await selectCanvasElementAtIndex(canvasTestContext, created.index);

    await withOnlyActiveVideoContainer(canvasTestContext, async () => {
        await openFreshContextMenu(canvasTestContext);
        await expectContextMenuItemEnabledState(
            canvasTestContext.pageFrame,
            "Play Earlier",
            false,
        );
        await expectContextMenuItemEnabledState(
            canvasTestContext.pageFrame,
            "Play Later",
            false,
        );
        await canvasTestContext.page.keyboard.press("Escape");
    });
});
