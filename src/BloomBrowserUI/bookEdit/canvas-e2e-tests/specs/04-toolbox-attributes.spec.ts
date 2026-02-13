// Spec 04 – Toolbox attribute controls (Areas D1-D9)
//
// Covers: CanvasToolControls.tsx.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    setStyleDropdown,
    setShowTail,
    setRoundedCorners,
    clickTextColorBar,
    clickBackgroundColorBar,
    setOutlineColorDropdown,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectToolboxOptionsDisabled,
    expectToolboxOptionsEnabled,
    expectToolboxControlsVisible,
    expectToolboxShowsNoOptions,
} from "../helpers/canvasAssertions";
import {
    canvasMatrix,
    mainPaletteRows,
    navigationPaletteRows,
    getMatrixRow,
} from "../helpers/canvasMatrix";
import { canvasSelectors } from "../helpers/canvasSelectors";
import {
    expandNavigationSection,
    selectCanvasElementAtIndex,
} from "../helpers/canvasActions";

// ── Helper ──────────────────────────────────────────────────────────────

const createAndVerify = async (canvasTestContext, paletteItem: string) => {
    const maxAttempts = 3;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(canvasTestContext);
        await dragPaletteItemToCanvas({
            canvasContext: canvasTestContext,
            paletteItem: paletteItem as any,
        });

        try {
            await expectCanvasElementCountToIncrease(
                canvasTestContext,
                beforeCount,
            );
            return;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
        }
    }
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

// ── D9: Link-grid type has expected controls ────────────────────────
// book-link-grid is limited to one per page. In shared mode, one may
// already exist from an earlier test (A1-nav). We select the existing
// element via the manager rather than dragging a new one.

test("D9: book-link-grid shows expected toolbox controls", async ({
    canvasTestContext,
}) => {
    // Try to find and select an existing book-link-grid element via
    // the canvas element manager + type inference.
    const selected = await canvasTestContext.pageFrame.evaluate(
        (canvasElSelector: string) => {
            const bundle = (window as any).editablePageBundle;
            const manager = bundle?.getTheOneCanvasElementManager?.();
            if (!manager) return false;

            const elements = Array.from(
                document.querySelectorAll(canvasElSelector),
            ) as HTMLElement[];

            for (const el of elements) {
                if (el.getElementsByClassName("bloom-link-grid").length > 0) {
                    manager.setActiveElement(el);
                    return true;
                }
            }
            return false;
        },
        canvasSelectors.page.canvasElements,
    );

    if (!selected) {
        // No book-link-grid exists yet – expand nav section and create one.
        await expandNavigationSection(canvasTestContext);
        await createAndVerify(canvasTestContext, "book-link-grid");
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
