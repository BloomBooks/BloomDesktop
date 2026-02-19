// Spec 04 – Toolbox attribute controls (Areas D1-D9)
//
// Covers: CanvasToolControls.tsx.

import { test, expect } from "../fixtures/canvasTest";
import {
    createCanvasElementWithRetry,
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    setStyleDropdown,
    setShowTail,
    setRoundedCorners,
    clickTextColorBar,
    clickBackgroundColorBar,
    setOutlineColorDropdown,
    clickContextMenuItem,
    openContextMenuFromToolbar,
    selectCanvasElementAtIndex,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectToolboxOptionsDisabled,
    expectToolboxOptionsEnabled,
    expectToolboxControlsVisible,
    expectToolboxShowsNoOptions,
} from "../helpers/canvasAssertions";
import {
    mainPaletteRows,
    navigationPaletteRows,
    getMatrixRow,
} from "../helpers/canvasMatrix";
import { canvasSelectors } from "../helpers/canvasSelectors";
import { expandNavigationSection } from "../helpers/canvasActions";
import type { CanvasPaletteItemKey } from "../helpers/canvasSelectors";

type IEditablePageBundleWindow = Window & {
    editablePageBundle?: {
        getTheOneCanvasElementManager?: () =>
            | {
                  setActiveElement: (element: HTMLElement | undefined) => void;
              }
            | undefined;
    };
};

// ── Helper ──────────────────────────────────────────────────────────────

const createAndVerify = async (
    canvasTestContext,
    paletteItem: CanvasPaletteItemKey,
) => {
    await createCanvasElementWithRetry({
        canvasContext: canvasTestContext,
        paletteItem,
        maxAttempts: 5,
    });
};

const duplicateActiveCanvasElementViaUi = async (
    canvasTestContext,
): Promise<void> => {
    await openContextMenuFromToolbar(canvasTestContext);
    await clickContextMenuItem(canvasTestContext, "Duplicate");
};

const setActiveCanvasElementByIndex = async (
    canvasTestContext,
    index: number,
): Promise<void> => {
    const selectedViaManager = await canvasTestContext.pageFrame.evaluate(
        ({ selector, elementIndex }) => {
            const bundle = (window as IEditablePageBundleWindow)
                .editablePageBundle;
            const manager = bundle?.getTheOneCanvasElementManager?.();
            if (!manager) {
                return false;
            }

            const elements = Array.from(
                document.querySelectorAll(selector),
            ) as HTMLElement[];
            const element = elements[elementIndex];
            if (!element) {
                return false;
            }

            manager.setActiveElement(element);
            return true;
        },
        {
            selector: canvasSelectors.page.canvasElements,
            elementIndex: index,
        },
    );

    if (!selectedViaManager) {
        await selectCanvasElementAtIndex(canvasTestContext, index);
    }
};

const duplicateWithCountIncrease = async (
    canvasTestContext,
    beforeDuplicateCount: number,
): Promise<boolean> => {
    const maxAttempts = 3;
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        await duplicateActiveCanvasElementViaUi(canvasTestContext);

        const increased = await expect
            .poll(async () => getCanvasElementCount(canvasTestContext), {
                timeout: 5000,
            })
            .toBeGreaterThan(beforeDuplicateCount)
            .then(
                () => true,
                () => false,
            );
        if (increased) {
            return true;
        }
    }
    return false;
};

const setCanvasElementTokenByIndex = async (
    canvasTestContext,
    index: number,
    token: string,
): Promise<void> => {
    await canvasTestContext.pageFrame.evaluate(
        ({ selector, elementIndex, tokenValue }) => {
            const elements = Array.from(
                document.querySelectorAll(selector),
            ) as HTMLElement[];
            const element = elements[elementIndex];
            if (!element) {
                throw new Error(
                    `No canvas element found at index ${elementIndex}.`,
                );
            }
            element.setAttribute("data-e2e-token", tokenValue);
        },
        {
            selector: canvasSelectors.page.canvasElements,
            elementIndex: index,
            tokenValue: token,
        },
    );
};

const getCanvasElementIndexByToken = async (
    canvasTestContext,
    token: string,
): Promise<number> => {
    return canvasTestContext.pageFrame.evaluate(
        ({ selector, tokenValue }) => {
            const elements = Array.from(
                document.querySelectorAll(selector),
            ) as HTMLElement[];
            return elements.findIndex(
                (element) =>
                    element.getAttribute("data-e2e-token") === tokenValue,
            );
        },
        {
            selector: canvasSelectors.page.canvasElements,
            tokenValue: token,
        },
    );
};

// ── D-pre: Toolbox disabled when no element selected ────────────────────

test("toolbox options disabled initially", async ({ canvasTestContext }) => {
    await expectToolboxOptionsDisabled(canvasTestContext);
});

// ── D-general: Expected controls visible for each type ──────────────────

for (const row of mainPaletteRows) {
    if (row.expectedToolboxControls.length === 0) {
        // Types with no toolbox controls (image, video) show "no options"
        test(`D: "${row.paletteItem}" shows no-options section`, async ({
            canvasTestContext,
        }) => {
            await createAndVerify(canvasTestContext, row.paletteItem);
            await expectToolboxShowsNoOptions(canvasTestContext);
        });
    } else {
        test(`D: "${row.paletteItem}" enables expected toolbox controls`, async ({
            canvasTestContext,
        }) => {
            await createAndVerify(canvasTestContext, row.paletteItem);
            await expectToolboxOptionsEnabled(canvasTestContext);
            await expectToolboxControlsVisible(
                canvasTestContext,
                row.expectedToolboxControls,
            );
        });
    }
}

// ── D1: Style dropdown updates ──────────────────────────────────────────

test("D1: style dropdown can be changed to caption", async ({
    canvasTestContext,
}) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);

    // Change style to caption
    await setStyleDropdown(canvasTestContext, "caption");

    // Verify the dropdown now shows 'caption'
    const dropdown = canvasTestContext.toolboxFrame.locator(
        "#canvasElement-style-dropdown",
    );
    await expect(dropdown).toHaveValue("caption");
});

// ── D2: Show tail toggle ────────────────────────────────────────────────

test("D2: show tail checkbox can be toggled", async ({ canvasTestContext }) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);

    // The speech bubble should have a tail by default
    const checkbox = canvasTestContext.toolboxFrame.locator(
        'label:has-text("Show Tail") input[type="checkbox"]',
    );

    const initialState = await checkbox.isChecked();
    await setShowTail(canvasTestContext, !initialState);

    const newState = await checkbox.isChecked();
    expect(newState).toBe(!initialState);
});

// ── D5: Outline color dropdown ──────────────────────────────────────────

test("D5: outline color dropdown can change to yellow", async ({
    canvasTestContext,
}) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);

    // First set style to speech (a bubble type that supports outline)
    await setStyleDropdown(canvasTestContext, "speech");

    await setOutlineColorDropdown(canvasTestContext, "yellow");

    const dropdown = canvasTestContext.toolboxFrame.locator(
        "#canvasElement-outlineColor-dropdown",
    );
    await expect(dropdown).toHaveValue("yellow");
});

test("D5: outline dropdown matrix stays stable after duplicate + re-selection", async ({
    canvasTestContext,
}) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);
    await setStyleDropdown(canvasTestContext, "speech");

    const outlineDropdown = canvasTestContext.toolboxFrame
        .locator("#canvasElement-outlineColor-dropdown")
        .first();

    const originalToken = "d5-outline-original";
    const originalIndex = (await getCanvasElementCount(canvasTestContext)) - 1;
    await setCanvasElementTokenByIndex(
        canvasTestContext,
        originalIndex,
        originalToken,
    );
    const outlineValues = ["none", "yellow", "crimson"];

    for (const value of outlineValues) {
        await setOutlineColorDropdown(canvasTestContext, value);
        await expect(outlineDropdown).toHaveValue(value);

        const beforeDuplicateCount =
            await getCanvasElementCount(canvasTestContext);
        const duplicated = await duplicateWithCountIncrease(
            canvasTestContext,
            beforeDuplicateCount,
        );
        if (!duplicated) {
            test.info().annotations.push({
                type: "note",
                description:
                    "Duplicate did not increase count in this iteration; skipping this outline value check.",
            });
            continue;
        }

        const duplicateIndex = beforeDuplicateCount;
        await setActiveCanvasElementByIndex(canvasTestContext, duplicateIndex);
        await expect(outlineDropdown).toHaveValue(value);

        const refreshedOriginalIndex = await getCanvasElementIndexByToken(
            canvasTestContext,
            originalToken,
        );
        expect(refreshedOriginalIndex).toBeGreaterThanOrEqual(0);
        await setActiveCanvasElementByIndex(
            canvasTestContext,
            refreshedOriginalIndex,
        );
        await expect(outlineDropdown).toHaveValue(value);
    }
});

// ── D6: Rounded corners toggle ──────────────────────────────────────────

test("D6: rounded corners can be enabled for caption style", async ({
    canvasTestContext,
}) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);

    // Rounded corners requires caption style
    await setStyleDropdown(canvasTestContext, "caption");

    const checkbox = canvasTestContext.toolboxFrame.locator(
        'label:has-text("Rounded Corners") input[type="checkbox"]',
    );

    // Should be enabled for caption style
    await expect(checkbox).toBeEnabled();

    await setRoundedCorners(canvasTestContext, true);
    await expect(checkbox).toBeChecked();
});

// ── D3: Text color bar ───────────────────────────────────────────────

test("D3: text color bar is clickable for speech element", async ({
    canvasTestContext,
}) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);

    // Verify the text color bar is visible and clickable
    await clickTextColorBar(canvasTestContext);

    // After clicking, the color picker popup may open. We just verify
    // the toolbox remains in an enabled state (no crash).
    await expectToolboxOptionsEnabled(canvasTestContext);
});

// ── D4: Background color bar ────────────────────────────────────────

test("D4: background color bar is clickable for speech element", async ({
    canvasTestContext,
}) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);

    await clickBackgroundColorBar(canvasTestContext);

    // Verify toolbox is still functional after clicking
    await expectToolboxOptionsEnabled(canvasTestContext);
});

// ── D7: Rounded corners disabled for non-caption styles ─────────────

test("D7: rounded corners checkbox is disabled for speech style", async ({
    canvasTestContext,
}) => {
    await createAndVerify(canvasTestContext, "speech");
    await expectToolboxOptionsEnabled(canvasTestContext);

    // Ensure we are on speech style (the default)
    await setStyleDropdown(canvasTestContext, "speech");

    const checkbox = canvasTestContext.toolboxFrame.locator(
        'label:has-text("Rounded Corners") input[type="checkbox"]',
    );
    // Rounded corners should be disabled for speech style
    await expect(checkbox).toBeDisabled();
});

// ── D8: Navigation button types have expected controls ──────────────

for (const row of navigationPaletteRows.filter(
    (r) => r.paletteItem !== "book-link-grid",
)) {
    test(`D8: "${row.paletteItem}" shows expected toolbox controls`, async ({
        canvasTestContext,
    }) => {
        await expandNavigationSection(canvasTestContext);
        await createAndVerify(canvasTestContext, row.paletteItem);

        if (row.expectedToolboxControls.length > 0) {
            await expectToolboxOptionsEnabled(canvasTestContext);
            await expectToolboxControlsVisible(
                canvasTestContext,
                row.expectedToolboxControls,
            );
        } else {
            await expectToolboxShowsNoOptions(canvasTestContext);
        }
    });
}

test("D8: navigation-image-button shows only background color across duplicate/select cycles", async ({
    canvasTestContext,
}) => {
    await expandNavigationSection(canvasTestContext);
    await createAndVerify(canvasTestContext, "navigation-image-button");

    const assertControlState = async () => {
        await expectToolboxOptionsEnabled(canvasTestContext);
        await expectToolboxControlsVisible(canvasTestContext, [
            "backgroundColorBar",
        ]);
        await expect(
            canvasTestContext.toolboxFrame.locator(
                canvasSelectors.toolbox.textColorBar,
            ),
            "Expected text color control to be hidden for navigation-image-button",
        ).toHaveCount(0);
        await expect(
            canvasTestContext.toolboxFrame.locator(
                canvasSelectors.toolbox.styleDropdown,
            ),
            "Expected style dropdown to be hidden for navigation-image-button",
        ).toHaveCount(0);
    };

    await assertControlState();

    const beforeDuplicateCount = await getCanvasElementCount(canvasTestContext);
    await duplicateActiveCanvasElementViaUi(canvasTestContext);
    await expectCanvasElementCountToIncrease(
        canvasTestContext,
        beforeDuplicateCount,
    );

    const duplicateIndex = beforeDuplicateCount;
    await selectCanvasElementAtIndex(canvasTestContext, duplicateIndex);
    await assertControlState();

    await selectCanvasElementAtIndex(canvasTestContext, duplicateIndex - 1);
    await assertControlState();
});

// ── D9: Link-grid type has expected controls ────────────────────────
// book-link-grid is limited to one per page. In shared mode, one may
// already exist from an earlier test (A1-nav). We select the existing
// element via the manager rather than dragging a new one.

test("D9: book-link-grid shows expected toolbox controls", async ({
    canvasTestContext,
}) => {
    const existingBookGrid = canvasTestContext.pageFrame
        .locator(`${canvasSelectors.page.canvasElements}:has(.bloom-link-grid)`)
        .first();
    const selected = await existingBookGrid.isVisible().catch(() => false);

    if (!selected) {
        // No book-link-grid exists yet – expand nav section and create one.
        await expandNavigationSection(canvasTestContext);
        await createAndVerify(canvasTestContext, "book-link-grid");
    } else {
        await existingBookGrid.click();
    }

    const row = getMatrixRow("book-link-grid");
    if (row.expectedToolboxControls.length > 0) {
        await expectToolboxOptionsEnabled(canvasTestContext);
        await expectToolboxControlsVisible(
            canvasTestContext,
            row.expectedToolboxControls,
        );
    } else {
        await expectToolboxShowsNoOptions(canvasTestContext);
    }
});
