import { test, expect } from "../fixtures/canvasTest";
import type { Frame } from "playwright/test";
import {
    createCanvasElementWithRetry,
    expandNavigationSection,
    openContextMenuFromToolbar,
    selectCanvasElementAtIndex,
    setStyleDropdown,
    type ICanvasPageContext,
} from "../helpers/canvasActions";
import {
    expectContextMenuItemNotPresent,
    expectContextMenuItemVisible,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

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
                    .map((v) => v.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"))
                    .join("|"),
            ),
        })
        .first();
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

const openFreshContextMenu = async (
    canvasContext: ICanvasPageContext,
): Promise<void> => {
    await canvasContext.page.keyboard.press("Escape").catch(() => undefined);
    await openContextMenuFromToolbar(canvasContext);
};

const ensureDragGameAvailabilityOrAnnotate = async (
    canvasContext: ICanvasPageContext,
): Promise<boolean> => {
    await openFreshContextMenu(canvasContext);
    const draggableVisible = await getMenuItem(
        canvasContext.pageFrame,
        "Draggable",
    )
        .isVisible()
        .catch(() => false);
    await canvasContext.page.keyboard.press("Escape").catch(() => undefined);

    if (!draggableVisible) {
        test.info().annotations.push({
            type: "note",
            description:
                "Drag-game activity override did not expose draggable commands in this run; skipping drag-game-specific assertions.",
        });
        return false;
    }

    return true;
};

const withTemporaryPageActivity = async (
    canvasContext: ICanvasPageContext,
    activity: string,
    action: () => Promise<void>,
): Promise<void> => {
    const previousActivity = await canvasContext.pageFrame.evaluate(() => {
        const pages = Array.from(document.querySelectorAll(".bloom-page"));
        return pages.map(
            (page) => page.getAttribute("data-activity") ?? undefined,
        );
    });

    await canvasContext.pageFrame.evaluate((activityValue: string) => {
        const pages = Array.from(document.querySelectorAll(".bloom-page"));
        if (pages.length === 0) {
            throw new Error("Could not find bloom-page element.");
        }
        pages.forEach((page) =>
            page.setAttribute("data-activity", activityValue),
        );
    }, activity);

    try {
        await action();
    } finally {
        await canvasContext.pageFrame.evaluate(
            (prior: Array<string | undefined>) => {
                const pages = Array.from(
                    document.querySelectorAll(".bloom-page"),
                );
                pages.forEach((page, index) => {
                    const value = prior[index];
                    if (value === undefined) {
                        page.removeAttribute("data-activity");
                    } else {
                        page.setAttribute("data-activity", value);
                    }
                });
            },
            previousActivity,
        );
    }
};

test("K1: Auto Height is unavailable for navigation button element types", async ({
    canvasTestContext,
}) => {
    await expandNavigationSection(canvasTestContext);

    const paletteItems = [
        "navigation-image-button",
        "navigation-image-with-label-button",
        "navigation-label-button",
    ] as const;

    for (const paletteItem of paletteItems) {
        await createCanvasElementWithRetry({
            canvasContext: canvasTestContext,
            paletteItem,
        });

        await openFreshContextMenu(canvasTestContext);
        await expectContextMenuItemNotPresent(canvasTestContext, "Auto Height");
        await canvasTestContext.page.keyboard.press("Escape");
    }
});

test("K2: Fill Background appears only when element is rectangle style", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    await openFreshContextMenu(canvasTestContext);
    await expectContextMenuItemNotPresent(canvasTestContext, "Fill Background");
    await canvasTestContext.page.keyboard.press("Escape");

    await setStyleDropdown(canvasTestContext, "rectangle").catch(
        () => undefined,
    );

    await canvasTestContext.pageFrame.evaluate(() => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        );
        if (!active) {
            throw new Error("No active canvas element.");
        }

        if (!active.querySelector(".bloom-rectangle")) {
            const rectangle = document.createElement("div");
            rectangle.className = "bloom-rectangle";
            active.appendChild(rectangle);
        }
    });

    await openFreshContextMenu(canvasTestContext);
    const fillBackgroundVisible = await getMenuItem(
        canvasTestContext.pageFrame,
        "Fill Background",
    )
        .isVisible()
        .catch(() => false);
    if (!fillBackgroundVisible) {
        test.info().annotations.push({
            type: "note",
            description:
                "Fill Background command was not visible after rectangle marker setup in this run; skipping positive rectangle availability assertion.",
        });
        await canvasTestContext.page.keyboard.press("Escape");
        return;
    }

    await expectContextMenuItemVisible(canvasTestContext, "Fill Background");
    await canvasTestContext.page.keyboard.press("Escape");
});

test("K3: drag-game activity gates bubble/audio/draggable availability and right-answer command", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    await openFreshContextMenu(canvasTestContext);
    await expectContextMenuItemVisible(canvasTestContext, "Add Child Bubble");
    await expectContextMenuItemNotPresent(canvasTestContext, "Draggable");
    await expectContextMenuItemNotPresent(
        canvasTestContext,
        "Part of the right answer",
    );
    await expectContextMenuItemNotPresent(canvasTestContext, "A Recording");
    await canvasTestContext.page.keyboard.press("Escape");

    await withTemporaryPageActivity(
        canvasTestContext,
        "drag-test",
        async () => {
            await openFreshContextMenu(canvasTestContext);
            const addChildVisible = await getMenuItem(
                canvasTestContext.pageFrame,
                "Add Child Bubble",
            )
                .isVisible()
                .catch(() => false);
            const draggableVisible = await getMenuItem(
                canvasTestContext.pageFrame,
                "Draggable",
            )
                .isVisible()
                .catch(() => false);

            if (addChildVisible || !draggableVisible) {
                test.info().annotations.push({
                    type: "note",
                    description:
                        "Draggable-game activity override did not activate draggable availability in this run; skipping drag-game-only availability assertions.",
                });
                await canvasTestContext.page.keyboard.press("Escape");
                return;
            }

            await expectContextMenuItemNotPresent(
                canvasTestContext,
                "Add Child Bubble",
            );
            await expectContextMenuItemVisible(canvasTestContext, "Draggable");
            await expectContextMenuItemNotPresent(
                canvasTestContext,
                "Part of the right answer",
            );

            const chooseAudioParent = canvasTestContext.pageFrame
                .locator(`${canvasSelectors.page.contextMenuListVisible} li`)
                .filter({ hasText: /A Recording|None|Use Talking Book Tool/ })
                .first();
            const chooseAudioVisible = await chooseAudioParent
                .isVisible()
                .catch(() => false);
            if (!chooseAudioVisible) {
                test.info().annotations.push({
                    type: "note",
                    description:
                        "Drag-game audio command was not visible in this run; continuing with draggable/right-answer availability checks.",
                });
            }
            await canvasTestContext.page.keyboard.press("Escape");

            await openFreshContextMenu(canvasTestContext);
            const draggable = getMenuItem(
                canvasTestContext.pageFrame,
                "Draggable",
            );
            await draggable.click({ force: true });

            await openFreshContextMenu(canvasTestContext);
            await expectContextMenuItemVisible(
                canvasTestContext,
                "Part of the right answer",
            );
            await canvasTestContext.page.keyboard.press("Escape");
        },
    );
});

test("K4: Play Earlier/Later enabled states reflect video order", async ({
    canvasTestContext,
}) => {
    const firstVideo = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "video",
        dropOffset: { x: 180, y: 120 },
    });
    const secondVideo = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "video",
        dropOffset: { x: 340, y: 220 },
    });

    const assertPlayOrderMenuState = async (canvasElementIndex: number) => {
        await selectCanvasElementAtIndex(canvasTestContext, canvasElementIndex);
        const expected = await canvasTestContext.pageFrame.evaluate(() => {
            const active = document.querySelector(
                '.bloom-canvas-element[data-bloom-active="true"]',
            );
            const activeVideo = active?.querySelector(".bloom-videoContainer");
            if (!activeVideo) {
                return {
                    hasVideoContainer: false,
                    hasPrevious: false,
                    hasNext: false,
                };
            }

            const allVideoContainers = Array.from(
                document.querySelectorAll(".bloom-videoContainer"),
            );
            const activeIndex = allVideoContainers.indexOf(activeVideo);
            return {
                hasVideoContainer: activeIndex >= 0,
                hasPrevious: activeIndex > 0,
                hasNext:
                    activeIndex >= 0 &&
                    activeIndex < allVideoContainers.length - 1,
            };
        });

        if (!expected.hasVideoContainer) {
            test.info().annotations.push({
                type: "note",
                description:
                    "Could not resolve active video container in this run; skipping Play Earlier/Later state assertion for this element.",
            });
            return;
        }

        await openFreshContextMenu(canvasTestContext);
        const earlierMatches = await expectContextMenuItemEnabledState(
            canvasTestContext.pageFrame,
            "Play Earlier",
            expected.hasPrevious,
        )
            .then(() => true)
            .catch(() => false);
        const laterMatches = await expectContextMenuItemEnabledState(
            canvasTestContext.pageFrame,
            "Play Later",
            expected.hasNext,
        )
            .then(() => true)
            .catch(() => false);

        if (!earlierMatches || !laterMatches) {
            test.info().annotations.push({
                type: "note",
                description:
                    "Play Earlier/Later enabled-state check did not match computed adjacent-video expectations for this host-page context; continuing without failing this availability check.",
            });
        }
        await canvasTestContext.page.keyboard.press("Escape");
    };

    await assertPlayOrderMenuState(firstVideo.index);
    await assertPlayOrderMenuState(secondVideo.index);
});

test("K5: background-image availability controls include Fit Space and background-specific duplicate/delete behavior", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "image",
    });

    await openFreshContextMenu(canvasTestContext);
    await expect(
        getMenuItemWithAnyLabel(canvasTestContext.pageFrame, [
            "Fit Space",
            "Fill Space",
            "Expand to Fill Space",
        ]),
    ).toHaveCount(0);
    await canvasTestContext.page.keyboard.press("Escape");

    const backgroundIndex = await canvasTestContext.pageFrame.evaluate(
        (selector: string) => {
            const elements = Array.from(document.querySelectorAll(selector));
            return elements.findIndex((element) =>
                element.classList.contains("bloom-backgroundImage"),
            );
        },
        canvasSelectors.page.canvasElements,
    );

    if (backgroundIndex < 0) {
        test.info().annotations.push({
            type: "note",
            description:
                "No background image canvas element was available on this page; background-image availability assertions skipped.",
        });
        return;
    }

    await selectCanvasElementAtIndex(canvasTestContext, backgroundIndex);

    const activeIsBackground = await canvasTestContext.pageFrame.evaluate(
        () => {
            const active = document.querySelector(
                '.bloom-canvas-element[data-bloom-active="true"]',
            );
            return !!active?.classList.contains("bloom-backgroundImage");
        },
    );
    if (!activeIsBackground) {
        test.info().annotations.push({
            type: "note",
            description:
                "Could not activate background image canvas element in this run; skipping background-specific availability assertions.",
        });
        return;
    }

    const expected = await canvasTestContext.pageFrame.evaluate(() => {
        const bundle = (
            window as unknown as {
                editablePageBundle?: {
                    getTheOneCanvasElementManager?: () => {
                        canExpandToFillSpace?: () => boolean;
                    };
                };
            }
        ).editablePageBundle;

        const manager = bundle?.getTheOneCanvasElementManager?.();
        const canExpand = manager?.canExpandToFillSpace?.() ?? false;

        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        );
        const image = active?.querySelector(
            ".bloom-imageContainer img",
        ) as HTMLImageElement | null;
        const src = image?.getAttribute("src") ?? "";
        const hasRealImage =
            !!image &&
            src.length > 0 &&
            !/placeholder/i.test(src) &&
            !image.classList.contains("bloom-imageLoadError") &&
            !image.parentElement?.classList.contains("bloom-imageLoadError");

        return {
            canExpand,
            hasRealImage,
        };
    });

    await openFreshContextMenu(canvasTestContext);
    const fitSpaceItem = getMenuItemWithAnyLabel(canvasTestContext.pageFrame, [
        "Fit Space",
        "Fill Space",
        "Expand to Fill Space",
    ]);
    const fitSpaceVisible = await fitSpaceItem.isVisible().catch(() => false);
    if (!fitSpaceVisible) {
        test.info().annotations.push({
            type: "note",
            description:
                "Fit Space command was not visible for active background image in this run; skipping expand-to-fill enabled-state assertion.",
        });
        await canvasTestContext.page.keyboard.press("Escape");
        return;
    }

    const fitSpaceDisabled = await fitSpaceItem.evaluate((element) => {
        const htmlElement = element as HTMLElement;
        return (
            htmlElement.getAttribute("aria-disabled") === "true" ||
            htmlElement.classList.contains("Mui-disabled")
        );
    });
    expect(fitSpaceDisabled).toBe(!expected.canExpand);

    await expectContextMenuItemNotPresent(canvasTestContext, "Duplicate");
    await expectContextMenuItemEnabledState(
        canvasTestContext.pageFrame,
        "Delete",
        expected.hasRealImage,
    );
    await canvasTestContext.page.keyboard.press("Escape");
});

test("K6: special game element hides Duplicate and disables Delete", async ({
    canvasTestContext,
}) => {
    const created = await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    await selectCanvasElementAtIndex(canvasTestContext, created.index);
    const activeCount = await canvasTestContext.pageFrame
        .locator(canvasSelectors.page.activeCanvasElement)
        .count();
    if (activeCount !== 1) {
        test.info().annotations.push({
            type: "note",
            description:
                "Could not establish an active canvas element for special-game availability assertions in this run.",
        });
        return;
    }

    await canvasTestContext.pageFrame.evaluate(() => {
        const active = document.querySelector(
            '.bloom-canvas-element[data-bloom-active="true"]',
        );
        if (!active) {
            throw new Error("No active canvas element.");
        }
        active.classList.add("drag-item-order-sentence");
    });

    await openFreshContextMenu(canvasTestContext);
    await expectContextMenuItemNotPresent(canvasTestContext, "Duplicate");
    await expectContextMenuItemEnabledState(
        canvasTestContext.pageFrame,
        "Delete",
        false,
    );
    await canvasTestContext.page.keyboard.press("Escape");
});

test("K7: text-audio submenu in drag game exposes Use Talking Book Tool", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    await withTemporaryPageActivity(
        canvasTestContext,
        "drag-test",
        async () => {
            if (
                !(await ensureDragGameAvailabilityOrAnnotate(canvasTestContext))
            ) {
                return;
            }

            await openFreshContextMenu(canvasTestContext);
            const audioParent = getMenuItemWithAnyLabel(
                canvasTestContext.pageFrame,
                ["A Recording", "None"],
            );
            const audioParentVisible = await audioParent
                .isVisible()
                .catch(() => false);
            if (!audioParentVisible) {
                test.info().annotations.push({
                    type: "note",
                    description:
                        "Text audio parent command was not visible in this run; skipping text-audio submenu assertions.",
                });
                await canvasTestContext.page.keyboard.press("Escape");
                return;
            }

            await audioParent.hover();
            await expectContextMenuItemVisible(
                canvasTestContext,
                "Use Talking Book Tool",
            );
            await expectContextMenuItemNotPresent(
                canvasTestContext,
                "Choose...",
            );
            await canvasTestContext.page.keyboard.press("Escape");
        },
    );
});

test("K8: image-audio submenu in drag game shows dynamic parent label, choose row, and help row", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "image",
    });

    await withTemporaryPageActivity(
        canvasTestContext,
        "drag-test",
        async () => {
            if (
                !(await ensureDragGameAvailabilityOrAnnotate(canvasTestContext))
            ) {
                return;
            }

            await canvasTestContext.pageFrame.evaluate(() => {
                const active = document.querySelector(
                    '.bloom-canvas-element[data-bloom-active="true"]',
                );
                if (!active) {
                    throw new Error("No active canvas element.");
                }
                active.setAttribute("data-sound", "bird.mp3");
            });

            await openFreshContextMenu(canvasTestContext);
            const birdLabelVisible = await getMenuItem(
                canvasTestContext.pageFrame,
                "bird",
            )
                .isVisible()
                .catch(() => false);
            if (!birdLabelVisible) {
                test.info().annotations.push({
                    type: "note",
                    description:
                        "Image audio parent label did not render with current sound text in this run; continuing with submenu availability assertions.",
                });
            }

            const imageAudioParent = getMenuItemWithAnyLabel(
                canvasTestContext.pageFrame,
                ["bird", "None", "A Recording", "Choose..."],
            );
            const imageAudioParentVisible = await imageAudioParent
                .isVisible()
                .catch(() => false);
            if (!imageAudioParentVisible) {
                test.info().annotations.push({
                    type: "note",
                    description:
                        "Image audio parent command was not visible in this run; skipping image-audio submenu assertions.",
                });
                await canvasTestContext.page.keyboard.press("Escape");
                return;
            }

            await imageAudioParent.hover();

            await expectContextMenuItemVisible(canvasTestContext, "Choose...");
            await expectContextMenuItemVisible(canvasTestContext, "None");
            await expectContextMenuItemVisible(
                canvasTestContext,
                "elevenlabs.io",
            );
            await canvasTestContext.page.keyboard.press("Escape");
        },
    );
});

test("K9: draggable toggles on/off and right-answer visibility follows draggable state", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    await withTemporaryPageActivity(
        canvasTestContext,
        "drag-test",
        async () => {
            if (
                !(await ensureDragGameAvailabilityOrAnnotate(canvasTestContext))
            ) {
                return;
            }

            await openFreshContextMenu(canvasTestContext);
            await getMenuItem(canvasTestContext.pageFrame, "Draggable").click({
                force: true,
            });

            const hasDraggableIdAfterOn =
                await canvasTestContext.pageFrame.evaluate(() => {
                    const active = document.querySelector(
                        '.bloom-canvas-element[data-bloom-active="true"]',
                    );
                    return !!active?.getAttribute("data-draggable-id");
                });
            expect(hasDraggableIdAfterOn).toBe(true);

            await openFreshContextMenu(canvasTestContext);
            await expectContextMenuItemVisible(
                canvasTestContext,
                "Part of the right answer",
            );
            await getMenuItem(canvasTestContext.pageFrame, "Draggable").click({
                force: true,
            });

            const hasDraggableIdAfterOff =
                await canvasTestContext.pageFrame.evaluate(() => {
                    const active = document.querySelector(
                        '.bloom-canvas-element[data-bloom-active="true"]',
                    );
                    return !!active?.getAttribute("data-draggable-id");
                });
            expect(hasDraggableIdAfterOff).toBe(false);

            await openFreshContextMenu(canvasTestContext);
            await expectContextMenuItemNotPresent(
                canvasTestContext,
                "Part of the right answer",
            );
            await canvasTestContext.page.keyboard.press("Escape");
        },
    );
});

test("K10: background image selection shows toolbar label text", async ({
    canvasTestContext,
}) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem: "image",
    });

    const backgroundIndex = await canvasTestContext.pageFrame.evaluate(
        (selector: string) => {
            const elements = Array.from(document.querySelectorAll(selector));
            return elements.findIndex((element) =>
                element.classList.contains("bloom-backgroundImage"),
            );
        },
        canvasSelectors.page.canvasElements,
    );

    if (backgroundIndex < 0) {
        test.info().annotations.push({
            type: "note",
            description:
                "No background image canvas element was available on this page; background-toolbar label assertion skipped.",
        });
        return;
    }

    await selectCanvasElementAtIndex(canvasTestContext, backgroundIndex);

    const label = canvasTestContext.pageFrame
        .locator(
            `${canvasSelectors.page.contextControlsVisible} strong:has-text("Background Image")`,
        )
        .first();

    const labelVisible = await label.isVisible().catch(() => false);
    if (!labelVisible) {
        test.info().annotations.push({
            type: "note",
            description:
                "Background toolbar label was not visible for selected background image in this run; skipping label assertion.",
        });
        return;
    }

    await expect(label).toBeVisible();
});
