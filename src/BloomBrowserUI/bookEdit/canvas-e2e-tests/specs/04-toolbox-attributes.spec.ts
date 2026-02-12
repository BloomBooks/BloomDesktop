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
    getMatrixRow,
} from "../helpers/canvasMatrix";

// ── Helper ──────────────────────────────────────────────────────────────

const createAndVerify = async (
    { page, toolboxFrame, pageFrame },
    paletteItem: string,
) => {
    const maxAttempts = 3;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(pageFrame);
        await dragPaletteItemToCanvas({
            page,
            toolboxFrame,
            pageFrame,
            paletteItem: paletteItem as any,
        });

        try {
            await expectCanvasElementCountToIncrease(pageFrame, beforeCount);
            return;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
        }
    }
};

// ── D-pre: Toolbox disabled when no element selected ────────────────────

test("toolbox options disabled initially", async ({ toolboxFrame }) => {
    await expectToolboxOptionsDisabled(toolboxFrame);
});

// ── D-general: Expected controls visible for each type ──────────────────

for (const row of mainPaletteRows) {
    if (row.expectedToolboxControls.length === 0) {
        // Types with no toolbox controls (image, video) show "no options"
        test(`D: "${row.paletteItem}" shows no-options section`, async ({
            page,
            toolboxFrame,
            pageFrame,
        }) => {
            await createAndVerify(
                { page, toolboxFrame, pageFrame },
                row.paletteItem,
            );
            await expectToolboxShowsNoOptions(toolboxFrame);
        });
    } else {
        test(`D: "${row.paletteItem}" enables expected toolbox controls`, async ({
            page,
            toolboxFrame,
            pageFrame,
        }) => {
            await createAndVerify(
                { page, toolboxFrame, pageFrame },
                row.paletteItem,
            );
            await expectToolboxOptionsEnabled(toolboxFrame);
            await expectToolboxControlsVisible(
                toolboxFrame,
                row.expectedToolboxControls,
            );
        });
    }
}

// ── D1: Style dropdown updates ──────────────────────────────────────────

test("D1: style dropdown can be changed to caption", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createAndVerify({ page, toolboxFrame, pageFrame }, "speech");
    await expectToolboxOptionsEnabled(toolboxFrame);

    // Change style to caption
    await setStyleDropdown(toolboxFrame, "caption");

    // Verify the dropdown now shows 'caption'
    const dropdown = toolboxFrame.locator("#canvasElement-style-dropdown");
    await expect(dropdown).toHaveValue("caption");
});

// ── D2: Show tail toggle ────────────────────────────────────────────────

test("D2: show tail checkbox can be toggled", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createAndVerify({ page, toolboxFrame, pageFrame }, "speech");
    await expectToolboxOptionsEnabled(toolboxFrame);

    // The speech bubble should have a tail by default
    const checkbox = toolboxFrame.locator(
        'label:has-text("Show Tail") input[type="checkbox"]',
    );

    const initialState = await checkbox.isChecked();
    await setShowTail(toolboxFrame, !initialState);

    const newState = await checkbox.isChecked();
    expect(newState).toBe(!initialState);
});

// ── D5: Outline color dropdown ──────────────────────────────────────────

test("D5: outline color dropdown can change to yellow", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createAndVerify({ page, toolboxFrame, pageFrame }, "speech");
    await expectToolboxOptionsEnabled(toolboxFrame);

    // First set style to speech (a bubble type that supports outline)
    await setStyleDropdown(toolboxFrame, "speech");

    await setOutlineColorDropdown(toolboxFrame, "yellow");

    const dropdown = toolboxFrame.locator(
        "#canvasElement-outlineColor-dropdown",
    );
    await expect(dropdown).toHaveValue("yellow");
});

// ── D6: Rounded corners toggle ──────────────────────────────────────────

test("D6: rounded corners can be enabled for caption style", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createAndVerify({ page, toolboxFrame, pageFrame }, "speech");
    await expectToolboxOptionsEnabled(toolboxFrame);

    // Rounded corners requires caption style
    await setStyleDropdown(toolboxFrame, "caption");

    const checkbox = toolboxFrame.locator(
        'label:has-text("Rounded Corners") input[type="checkbox"]',
    );

    // Should be enabled for caption style
    await expect(checkbox).toBeEnabled();

    await setRoundedCorners(toolboxFrame, true);
    await expect(checkbox).toBeChecked();
});
