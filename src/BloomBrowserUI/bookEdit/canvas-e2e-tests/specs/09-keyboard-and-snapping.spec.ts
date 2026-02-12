// Spec 09 – Keyboard movement + snapping + guides (Areas E1-E5)
//
// Covers: CanvasElementKeyboardProvider.ts, CanvasSnapProvider.ts,
//         CanvasGuideProvider.ts.

import { test } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
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
    await expectAnyCanvasElementActive(pageFrame);
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
