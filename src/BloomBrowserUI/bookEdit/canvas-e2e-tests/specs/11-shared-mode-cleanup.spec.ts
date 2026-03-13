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
        canvasTestContext,
    }) => {
        test.skip(
            !isSharedMode,
            "This regression smoke test is only relevant in shared mode.",
        );

        baselineCountForCleanupSmoke =
            await getCanvasElementCount(canvasTestContext);

        await dragPaletteItemToCanvas({
            canvasContext: canvasTestContext,
            paletteItem: "speech",
        });

        await expectCanvasElementCountToIncrease(
            canvasTestContext,
            baselineCountForCleanupSmoke,
        );
    });

    test("K2: next test starts at baseline count", async ({
        canvasTestContext,
    }) => {
        test.skip(
            !isSharedMode,
            "This regression smoke test is only relevant in shared mode.",
        );

        expect(baselineCountForCleanupSmoke).toBeDefined();

        const countAtStart = await getCanvasElementCount(canvasTestContext);
        expect(countAtStart).toBe(baselineCountForCleanupSmoke);
    });
});
