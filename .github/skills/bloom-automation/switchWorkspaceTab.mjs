import { createRequire } from "node:module";
import path from "node:path";
import {
    fetchBloomInstanceInfo,
    findRunningStandardBloomInstance,
    getDefaultRepoRoot,
    normalizeBloomInstanceInfo,
    requireOptionValue,
    requireTcpPortOption,
} from "./bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        runningBloom: false,
        httpPort: undefined,
        cdpPort: undefined,
        tab: undefined,
        json: false,
        timeoutMs: 10000,
    };

    for (let index = 0; index < args.length; index++) {
        const arg = args[index];

        if (arg === "--running-bloom") {
            options.runningBloom = true;
            continue;
        }

        if (arg === "--http-port") {
            options.httpPort = requireTcpPortOption(
                "--http-port",
                requireOptionValue(args, index, "--http-port"),
            );
            index++;
            continue;
        }

        if (arg.startsWith("--http-port=")) {
            options.httpPort = requireTcpPortOption(
                "--http-port",
                arg.slice("--http-port=".length),
            );
            continue;
        }

        if (arg === "--cdp-port") {
            options.cdpPort = requireTcpPortOption(
                "--cdp-port",
                requireOptionValue(args, index, "--cdp-port"),
            );
            index++;
            continue;
        }

        if (arg.startsWith("--cdp-port=")) {
            options.cdpPort = requireTcpPortOption(
                "--cdp-port",
                arg.slice("--cdp-port=".length),
            );
            continue;
        }

        if (arg === "--tab") {
            options.tab = args[index + 1] || options.tab;
            index++;
            continue;
        }

        if (arg.startsWith("--tab=")) {
            options.tab = arg.slice("--tab=".length);
            continue;
        }

        if (arg === "--timeout-ms") {
            options.timeoutMs = Number(args[index + 1] || options.timeoutMs);
            index++;
            continue;
        }

        if (arg === "--json") {
            options.json = true;
            continue;
        }

        if (arg === "--help") {
            printHelp();
            process.exit(0);
        }
    }

    return options;
};

const printHelp = () => {
    console.log(
        "Usage: node .github/skills/bloom-automation/switchWorkspaceTab.mjs (--running-bloom | --http-port <port>) --tab <collection|edit|publish> [--cdp-port <port>] [--json] [--timeout-ms <ms>]",
    );
};

const normalizeTab = (tab) => {
    switch ((tab || "").toLowerCase()) {
        case "collection":
        case "collections":
            return "collection";
        case "edit":
            return "edit";
        case "publish":
            return "publish";
        default:
            return undefined;
    }
};

const getTabLabel = (tab) => {
    switch (tab) {
        case "collection":
            return "Collections";
        case "edit":
            return "Edit";
        case "publish":
            return "Publish";
        default:
            throw new Error(`Unsupported tab '${tab}'.`);
    }
};

const loadPlaywright = () => {
    const repoRoot = getDefaultRepoRoot();
    const componentTesterDir = path.join(
        repoRoot,
        "src",
        "BloomBrowserUI",
        "react_components",
        "component-tester",
    );
    const requireFromComponentTester = createRequire(
        path.join(componentTesterDir, "package.json"),
    );

    try {
        return requireFromComponentTester("playwright");
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        throw new Error(
            `Could not load Playwright from ${componentTesterDir}. Run 'yarn install' in src/BloomBrowserUI/react_components/component-tester if dependencies are missing. Original error: ${message}`,
        );
    }
};

const getWorkspaceTabs = async (workspaceTabsUrl) => {
    const response = await fetch(workspaceTabsUrl);
    if (!response.ok) {
        throw new Error(
            `workspace/tabs failed: ${response.status} ${response.statusText} for ${workspaceTabsUrl}`,
        );
    }

    return response.json();
};

const waitForActiveWorkspaceTab = async (workspaceTabsUrl, tab, timeoutMs) => {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
        const tabs = await getWorkspaceTabs(workspaceTabsUrl);
        if (tabs.tabStates?.[tab] === "active") {
            return tabs;
        }

        await new Promise((resolve) => setTimeout(resolve, 250));
    }

    throw new Error(
        `Timed out waiting for workspace tab '${tab}' to become active.`,
    );
};

const resolveInstance = async (options) => {
    if (options.httpPort) {
        const response = await fetchBloomInstanceInfo(options.httpPort);
        if (!response.reachable || !response.json) {
            throw new Error(
                `No Bloom instance reported common/instanceInfo on http://localhost:${options.httpPort}.`,
            );
        }

        const instance = normalizeBloomInstanceInfo(
            response.json,
            options.httpPort,
        );
        if (options.cdpPort) {
            instance.cdpPort = options.cdpPort;
            instance.cdpOrigin = `http://localhost:${instance.cdpPort}`;
        }

        return instance;
    }

    if (options.runningBloom) {
        const instance = await findRunningStandardBloomInstance();
        if (!instance) {
            throw new Error(
                "No running Bloom instance was found on Bloom's standard HTTP port range.",
            );
        }

        if (options.cdpPort) {
            instance.cdpPort = options.cdpPort;
            instance.cdpOrigin = `http://localhost:${instance.cdpPort}`;
        }

        return instance;
    }

    throw new Error("Specify either --running-bloom or --http-port <port>.");
};

const getBloomPage = (browser) => {
    const pages = browser.contexts().flatMap((context) => context.pages());
    return pages.find(
        (candidate) =>
            candidate.url().includes("/bloom/") &&
            !candidate.url().startsWith("devtools://"),
    );
};

const main = async () => {
    const options = parseArgs();
    const tab = normalizeTab(options.tab);

    if (!tab) {
        printHelp();
        throw new Error("--tab must be one of: collection, edit, publish.");
    }

    const instance = await resolveInstance(options);
    if (!instance.cdpOrigin) {
        throw new Error(
            "The selected Bloom instance did not report a CDP endpoint.",
        );
    }

    const { chromium } = loadPlaywright();
    const browser = await chromium.connectOverCDP(instance.cdpOrigin);

    try {
        const page = getBloomPage(browser);
        if (!page) {
            throw new Error(
                `Could not find a Bloom WebView2 target on ${instance.cdpOrigin}.`,
            );
        }

        await page.waitForLoadState("domcontentloaded");

        const topBarHandle = await page.$("#topBar");
        if (!topBarHandle) {
            throw new Error("Could not find the Bloom topBar iframe.");
        }

        const topBarFrame = await topBarHandle.contentFrame();
        if (!topBarFrame) {
            throw new Error("The Bloom topBar iframe did not expose a frame.");
        }

        await topBarFrame.waitForLoadState("domcontentloaded");
        await topBarFrame.getByRole("tab", { name: getTabLabel(tab) }).click();

        const tabs = await waitForActiveWorkspaceTab(
            instance.workspaceTabsUrl,
            tab,
            options.timeoutMs,
        );
        const bodyClassName = await page
            .locator("body")
            .evaluate((element) => element.className);

        const result = {
            instance: {
                processId: instance.processId,
                httpPort: instance.httpPort,
                cdpPort: instance.cdpPort,
                cdpOrigin: instance.cdpOrigin,
                workspaceTabsUrl: instance.workspaceTabsUrl,
            },
            selectedTab: tab,
            bodyClassName,
            pageUrl: page.url(),
            tabStates: tabs.tabStates,
        };

        if (options.json) {
            console.log(JSON.stringify(result, null, 2));
            return;
        }

        console.log(`Bloom HTTP port: ${result.instance.httpPort}`);
        console.log(`Bloom CDP port: ${result.instance.cdpPort}`);
        console.log(`Selected tab: ${result.selectedTab}`);
        console.log(`Body class: ${result.bodyClassName}`);
        console.log(JSON.stringify(result.tabStates));
    } finally {
        await browser.close();
    }
};

main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
});
