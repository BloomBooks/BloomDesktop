import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
} from "../helpers/canvasActions";
import { expectCanvasElementCountToIncrease } from "../helpers/canvasAssertions";

const isSharedMode = process.env.BLOOM_CANVAS_E2E_MODE !== "isolated";

let baselineCountForCleanupSmoke: number | undefined;

test.describe.serial("shared-mode cleanup", () => {
    test("K1: creating an element changes the count", async ({
        page,
        toolboxFrame,
        pageFrame,
    }) => {
        test.skip(
            !isSharedMode,
            "This regression smoke test is only relevant in shared mode.",
        );

        baselineCountForCleanupSmoke = await getCanvasElementCount(pageFrame);

        await dragPaletteItemToCanvas({
            page,
            toolboxFrame,
            pageFrame,
            paletteItem: "speech",
        });

        await expectCanvasElementCountToIncrease(
            pageFrame,
            baselineCountForCleanupSmoke,
        );
    });

    test("K2: next test starts at baseline count", async ({ pageFrame }) => {
        test.skip(
            !isSharedMode,
            "This regression smoke test is only relevant in shared mode.",
        );

        expect(baselineCountForCleanupSmoke).toBeDefined();

        const countAtStart = await getCanvasElementCount(pageFrame);
        expect(countAtStart).toBe(baselineCountForCleanupSmoke);
    });
});
