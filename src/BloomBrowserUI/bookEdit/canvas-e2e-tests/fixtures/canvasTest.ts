import { expect, test as base, type Frame, type Page } from "playwright/test";
import {
    getCanvasElementCount,
    openCanvasToolOnCurrentPage,
    removeCanvasElementsDownToCount,
    type ICanvasTestContext,
} from "../helpers/canvasActions";

type CanvasE2eMode = "shared" | "isolated";

interface ICanvasWorkerFixtures {
    canvasMode: CanvasE2eMode;
    sharedCanvasPage: Page | undefined;
    sharedCanvasBaselineCount: number;
}

interface ICanvasFixtures {
    canvasContext: ICanvasTestContext;
    toolboxFrame: Frame;
    pageFrame: Frame;
    _resetCanvasInSharedMode: void;
}

const getCanvasMode = (): CanvasE2eMode => {
    return process.env.BLOOM_CANVAS_E2E_MODE === "isolated"
        ? "isolated"
        : "shared";
};

export const test = base.extend<ICanvasFixtures, ICanvasWorkerFixtures>({
    canvasMode: [
        async ({}, applyFixture) => {
            await applyFixture(getCanvasMode());
        },
        {
            scope: "worker",
        },
    ],
    sharedCanvasPage: [
        async ({ browser, canvasMode }, applyFixture) => {
            if (canvasMode === "isolated") {
                await applyFixture(undefined);
                return;
            }

            const context = await browser.newContext();
            const page = await context.newPage();
            await openCanvasToolOnCurrentPage(page, {
                navigate: true,
            });

            await applyFixture(page);

            await context.close();
        },
        {
            scope: "worker",
        },
    ],
    sharedCanvasBaselineCount: [
        async ({ canvasMode, sharedCanvasPage }, applyFixture) => {
            if (canvasMode === "isolated") {
                await applyFixture(0);
                return;
            }

            const canvasContext = await openCanvasToolOnCurrentPage(
                sharedCanvasPage!,
                {
                    navigate: false,
                },
            );
            const baselineCount = await getCanvasElementCount(
                canvasContext.pageFrame,
            );
            await applyFixture(baselineCount);
        },
        {
            scope: "worker",
        },
    ],
    page: async ({ browser, canvasMode, sharedCanvasPage }, applyFixture) => {
        if (canvasMode === "shared") {
            await applyFixture(sharedCanvasPage!);
            return;
        }

        const context = await browser.newContext();
        const page = await context.newPage();
        await applyFixture(page);
        await context.close();
    },
    canvasContext: async ({ page, canvasMode }, applyFixture) => {
        const canvasContext = await openCanvasToolOnCurrentPage(page, {
            navigate: canvasMode === "isolated",
        });
        await applyFixture(canvasContext);
    },
    toolboxFrame: async ({ canvasContext }, applyFixture) => {
        await applyFixture(canvasContext.toolboxFrame);
    },
    pageFrame: async ({ canvasContext }, applyFixture) => {
        await applyFixture(canvasContext.pageFrame);
    },
    _resetCanvasInSharedMode: [
        async (
            { canvasMode, page, sharedCanvasBaselineCount },
            applyFixture,
        ) => {
            await applyFixture(undefined);

            if (canvasMode === "isolated") {
                return;
            }

            const canvasContext = await openCanvasToolOnCurrentPage(page, {
                navigate: false,
            });
            await removeCanvasElementsDownToCount(
                canvasContext.pageFrame,
                sharedCanvasBaselineCount,
            );
        },
        {
            auto: true,
        },
    ],
});

export { expect };
