import { expect, test as base, type Frame } from "playwright/test";
import {
    openCanvasToolOnCurrentPage,
    type ICanvasTestContext,
} from "../helpers/canvasActions";

interface ICanvasFixtures {
    canvasContext: ICanvasTestContext;
    toolboxFrame: Frame;
    pageFrame: Frame;
}

export const test = base.extend<ICanvasFixtures>({
    canvasContext: async ({ page }, applyFixture) => {
        const canvasContext = await openCanvasToolOnCurrentPage(page);
        await applyFixture(canvasContext);
    },
    toolboxFrame: async ({ canvasContext }, applyFixture) => {
        await applyFixture(canvasContext.toolboxFrame);
    },
    pageFrame: async ({ canvasContext }, applyFixture) => {
        await applyFixture(canvasContext.pageFrame);
    },
});

export { expect };
