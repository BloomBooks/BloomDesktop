// The bloom-exe CDP suite runs against the repo-root Playwright install (`playwright/test`),
// not the component-tester's own `@playwright/test`. Import from the same module the runner
// uses so the whole run shares one Playwright instance (mixing the two throws
// "Requiring @playwright/test a second time").
import { Browser, Page, chromium } from "playwright/test";
import * as fs from "fs";
import * as path from "path";

type WorkspaceTabId = "collection" | "edit" | "publish";
const configuredCdpPort = process.env.BLOOM_CDP_PORT;
const configuredHttpPort = process.env.BLOOM_HTTP_PORT;
const configuredCdpOrigin = process.env.BLOOM_CDP_ORIGIN;
const derivedCdpPort = configuredHttpPort
    ? String(Number.parseInt(configuredHttpPort, 10) + 2)
    : undefined;
// The CDP endpoints to try, best first. Exported so other bloom-exe tests that need
// their own connect/retry logic (e.g. reconnecting after Bloom restarts) share the
// same port derivation. IPv4 first: WebView2's CDP listens on 127.0.0.1, and Node can
// resolve localhost to ::1.
export const cdpEndpoints = configuredCdpOrigin
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

// Builds the full URL for one of Bloom's API endpoints, for calling from the test
// process (Node) rather than from inside the page. Deliberately uses localhost, not
// 127.0.0.1: Bloom's server rejects requests whose Host header is 127.0.0.1.
export const bloomApiUrl = (suffix: string): string =>
    `http://localhost:${configuredHttpPort || "8089"}/bloom/api/${suffix}`;

// Where the ./go.sh launcher advertises its control server (see scripts/watchBloomExe.mjs).
const launcherDiscoveryFile = path.resolve(
    __dirname,
    "../../../..",
    "output",
    "bloom-launcher.json",
);

// Asks the ./go.sh launcher (if one is running) which ports its Bloom is currently on.
// Bloom's ports are NOT stable across restarts: Bloom picks a free port at startup, so
// after anything that relaunches it (for example, toggling "Show translations which have
// not been approved yet") the new instance may be on different HTTP/CDP ports. Tests that
// survive a restart must therefore re-discover the ports rather than trusting the
// env-derived values above. Returns undefined when no launcher is running (e.g. Bloom was
// started some other way); callers should then fall back to those env-derived values.
export const discoverLauncherPorts = async (): Promise<
    { httpPort: number; cdpPort: number } | undefined
> => {
    try {
        const record = JSON.parse(
            await fs.promises.readFile(launcherDiscoveryFile, "utf8"),
        ) as { controlUrl?: string };
        if (!record.controlUrl) {
            return undefined;
        }
        // The discovery file survives a hard kill of the launcher, so only a live answer
        // from its control server counts.
        const response = await fetch(
            `${record.controlUrl.replace(/\/+$/, "")}/status`,
        );
        if (!response.ok) {
            return undefined;
        }
        const status = (await response.json()) as {
            httpPort?: number;
            cdpPort?: number;
        };
        if (!status.httpPort || !status.cdpPort) {
            return undefined;
        }
        return { httpPort: status.httpPort, cdpPort: status.cdpPort };
    } catch {
        // No discovery file, or nobody listening at its controlUrl: no launcher.
        return undefined;
    }
};

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
    // The WinForms shell still owns top-level tab state, so tests ask Bloom's API instead of inferring it from the DOM.
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
