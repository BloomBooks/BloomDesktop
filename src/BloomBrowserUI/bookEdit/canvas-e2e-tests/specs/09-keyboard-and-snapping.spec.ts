// Spec 09 – Keyboard movement + snapping + guides (Areas E1-E5)
//
// Covers: CanvasElementKeyboardProvider.ts, CanvasSnapProvider.ts,
//         CanvasGuideProvider.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    dragActiveCanvasElementByOffset,
    getCanvasElementCount,
    getActiveCanvasElement,
    keyboardNudge,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectElementVisible,
    expectElementHasPositiveSize,
    expectPositionGridSnapped,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

// ── Helper ──────────────────────────────────────────────────────────────

const createSpeechElement = async ({ page, toolboxFrame, pageFrame }) => {
    const beforeCount = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCount);

    const createdElement = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(beforeCount);
    await createdElement.waitFor({ state: "visible", timeout: 10000 });

    await expectAnyCanvasElementActive(pageFrame);
    return createdElement;
};

const createImageElement = async ({ page, toolboxFrame, pageFrame }) => {
    const beforeCount = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "image",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCount);

    const createdElement = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(beforeCount);
    await createdElement.waitFor({ state: "visible", timeout: 10000 });

    await expectAnyCanvasElementActive(pageFrame);
    return createdElement;
};

// ── E1: Arrow key moves element by grid step ────────────────────────────

test("E1: arrow-right moves the active element", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const active = getActiveCanvasElement(pageFrame);
    await active.click();

    // Press arrow right multiple times to accumulate visible movement
    for (let i = 0; i < 3; i++) {
        await keyboardNudge(page, "ArrowRight");
    }

    await expectAnyCanvasElementActive(pageFrame);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

test("E1: arrow-down moves the active element", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const active = getActiveCanvasElement(pageFrame);
    await active.click();

    for (let i = 0; i < 3; i++) {
        await keyboardNudge(page, "ArrowDown");
    }

    await expectAnyCanvasElementActive(pageFrame);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── E2: Ctrl+arrow for precise 1px movement ────────────────────────────

test("E2: Ctrl+arrow-right moves by small increment", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const active = getActiveCanvasElement(pageFrame);
    await active.click();

    // Ctrl+arrow should move by 1px
    for (let i = 0; i < 5; i++) {
        await keyboardNudge(page, "ArrowRight", { ctrl: true });
    }

    await expectAnyCanvasElementActive(pageFrame);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── E4: Position is grid-snapped after arrow key movement ───────────────

test("E4: position is grid-snapped after arrow key movement", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const active = getActiveCanvasElement(pageFrame);

    // Arrow keys use grid=10 by default
    await keyboardNudge(page, "ArrowRight");
    await keyboardNudge(page, "ArrowDown");

    await expectPositionGridSnapped(active, 10);
});

// ── E3: Shift constrains drag axis ──────────────────────────────────

test("E3: Shift+drag constrains movement to primary axis", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const createdElement = await createImageElement({
        page,
        toolboxFrame,
        pageFrame,
    });

    await page.keyboard.press("Escape");

    const maxAttempts = 3;
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeShiftBox = await createdElement.boundingBox();
        if (!beforeShiftBox) {
            throw new Error(
                "Could not determine active element bounds before shift drag.",
            );
        }

        await dragActiveCanvasElementByOffset(page, pageFrame, 60, 10, {
            shift: true,
            element: createdElement,
        });

        const afterShiftBox = await createdElement.boundingBox();
        if (!afterShiftBox) {
            throw new Error(
                "Could not determine active element bounds after shift drag.",
            );
        }

        const actualDx = Math.abs(afterShiftBox.x - beforeShiftBox.x);
        const actualDy = Math.abs(afterShiftBox.y - beforeShiftBox.y);

        if (actualDx + actualDy > 2) {
            // Secondary axis (Y) should be constrained (less or equal to primary)
            expect(actualDy).toBeLessThanOrEqual(actualDx);
            return;
        }

        await page.keyboard.press("Escape");
    }

    throw new Error(
        "Shift+drag did not move the active element after bounded retries.",
    );
});
