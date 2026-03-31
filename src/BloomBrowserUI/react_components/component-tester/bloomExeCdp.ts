import { Browser, Page, chromium } from "./playwrightTest";

type WorkspaceTabId = "collection" | "edit" | "publish";
const configuredCdpPort = process.env.BLOOM_CDP_PORT;
const configuredHttpPort = process.env.BLOOM_HTTP_PORT;
const configuredCdpOrigin = process.env.BLOOM_CDP_ORIGIN;
const derivedCdpPort = configuredHttpPort
    ? String(Number.parseInt(configuredHttpPort, 10) + 2)
    : undefined;
const cdpEndpoints = configuredCdpOrigin
    ? [configuredCdpOrigin]
    : configuredCdpPort
      ? [
            `http://127.0.0.1:${configuredCdpPort}`,
            `http://localhost:${configuredCdpPort}`,
        ]
      : derivedCdpPort
        ? [
              `http://127.0.0.1:${derivedCdpPort}`,
              `http://localhost:${derivedCdpPort}`,
          ]
        : ["http://127.0.0.1:8091", "http://localhost:8091"];
const workspaceTabsUrl =
    process.env.BLOOM_WORKSPACE_TABS_URL ||
    `http://localhost:${configuredHttpPort || "8089"}/bloom/api/workspace/tabs`;

export const connectToBloomExe = async (): Promise<{
    browser: Browser;
    page: Page;
}> => {
    let browser: Browser | undefined;
    let lastError: unknown;

    for (const endpoint of cdpEndpoints) {
        try {
            browser = await chromium.connectOverCDP(endpoint);
            break;
        } catch (error) {
            lastError = error;
        }
    }

    if (!browser) {
        throw lastError instanceof Error
            ? lastError
            : new Error(
                  `Could not connect to Bloom WebView2 over CDP at ${cdpEndpoints.join(", ")}. Verify that Bloom is running and remote debugging is enabled.`,
              );
    }

    const pages = browser.contexts().flatMap((context) => context.pages());
    const page = pages.find(
        (candidate) =>
            candidate.url().includes("/bloom/") &&
            !candidate.url().startsWith("devtools://"),
    );

    if (!page) {
        await browser.close();
        throw new Error(
            `Could not find a Bloom WebView2 target on ${cdpEndpoints.join(", ")}. Start Bloom first and confirm remote debugging is enabled.`,
        );
    }

    await page.waitForLoadState("domcontentloaded");
    return { browser, page };
};

export const clickWorkspaceTab = async (
    page: Page,
    name: WorkspaceTabId extends infer _T
        ? "Collections" | "Edit" | "Publish"
        : never,
): Promise<void> => {
    await page.waitForSelector("#main-tabs button", {
        timeout: 10000,
    });

    await page.locator("#main-tabs button").filter({ hasText: name }).first();

    await page
        .locator("#main-tabs button")
        .filter({ hasText: name })
        .first()
        .click();
};

export const getWorkspaceTabs = async (): Promise<{
    tabStates: Record<WorkspaceTabId, string>;
}> => {
    const response = await fetch(workspaceTabsUrl);
    if (!response.ok) {
        throw new Error(
            `workspace/tabs failed: ${response.status} ${response.statusText} for ${workspaceTabsUrl}`,
        );
    }

    return response.json();
};

export const waitForActiveWorkspaceTab = async (
    tab: WorkspaceTabId,
): Promise<void> => {
    const timeoutAt = Date.now() + 10000;

    while (Date.now() < timeoutAt) {
        const tabs = await getWorkspaceTabs();
        if (tabs.tabStates[tab] === "active") {
            return;
        }

        await new Promise((resolve) => setTimeout(resolve, 250));
    }

    throw new Error(
        `Timed out waiting for workspace tab '${tab}' to become active.`,
    );
};
