import {
    expect,
    test as base,
    type Frame,
    type Page,
    type TestInfo,
} from "playwright/test";
import {
    getCanvasElementCount,
    openCanvasToolOnCurrentPage,
    removeCanvasElementsDownToCount,
    type ICanvasPageContext,
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
    canvasPage: ICanvasPageContext;
    toolboxFrame: Frame;
    pageFrame: Frame;
    _showTestNameBanner: void;
    _resetCanvasInSharedMode: void;
}

const getCanvasMode = (): CanvasE2eMode => {
    return process.env.BLOOM_CANVAS_E2E_MODE === "isolated"
        ? "isolated"
        : "shared";
};

const testNameBannerId = "__canvas-e2e-test-name-banner";

const getDisplayTestName = (testInfo: TestInfo): string => {
    const fileName = testInfo.file.split(/[\\/]/).pop();
    if (!fileName) {
        return testInfo.title;
    }

    return `${fileName} › ${testInfo.title}`;
};

const shouldShowTestNameBanner = (testInfo: TestInfo): boolean => {
    if (process.env.BLOOM_CANVAS_E2E_SHOW_TEST_NAME === "true") {
        return true;
    }

    return testInfo.project.use.headless === false;
};

const setTestNameBanner = async (
    target: Page | Frame,
    testName: string,
): Promise<void> => {
    await target.evaluate(
        ({ bannerId, bannerText }) => {
            let banner = document.getElementById(bannerId);
            if (!banner) {
                banner = document.createElement("div");
                banner.id = bannerId;
                banner.setAttribute(
                    "data-testid",
                    "canvas-e2e-test-name-banner",
                );
                document.body.appendChild(banner);
            }

            banner.textContent = bannerText;
            Object.assign(banner.style, {
                position: "fixed",
                top: "8px",
                left: "8px",
                right: "8px",
                zIndex: "2147483647",
                padding: "8px 12px",
                borderRadius: "6px",
                background: "#202124",
                color: "#ffffff",
                fontFamily: "sans-serif",
                fontSize: "18px",
                fontWeight: "700",
                textAlign: "center",
                pointerEvents: "none",
                opacity: "0.92",
            });
        },
        {
            bannerId: testNameBannerId,
            bannerText: testName,
        },
    );
};

export const test = base.extend<ICanvasFixtures, ICanvasWorkerFixtures>({
    canvasMode: [
        async ({ browserName: _browserName }, applyFixture) => {
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
    canvasPage: async ({ page, canvasContext }, applyFixture) => {
        await applyFixture({
            page,
            toolboxFrame: canvasContext.toolboxFrame,
            pageFrame: canvasContext.pageFrame,
        });
    },
    toolboxFrame: async ({ canvasContext }, applyFixture) => {
        await applyFixture(canvasContext.toolboxFrame);
    },
    pageFrame: async ({ canvasContext }, applyFixture) => {
        await applyFixture(canvasContext.pageFrame);
    },
    _showTestNameBanner: [
        async ({ page }, applyFixture, testInfo) => {
            if (!shouldShowTestNameBanner(testInfo)) {
                await applyFixture(undefined);
                return;
            }

            const testName = getDisplayTestName(testInfo);
            await setTestNameBanner(page, testName);
            await applyFixture(undefined);
        },
        {
            auto: true,
        },
    ],
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
