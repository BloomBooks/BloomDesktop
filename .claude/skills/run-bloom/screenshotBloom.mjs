// Screenshot the embedded WebView2 of a running Bloom.exe over CDP.
// Companion to the helpers in .github/skills/bloom-automation/; reuses their
// instance discovery so it targets the exact Bloom that owns a given HTTP port.
//
// Usage:
//   node .claude/skills/run-bloom/screenshotBloom.mjs (--running-bloom | --http-port <port>) [--out <path>] [--json]
import { createRequire } from "node:module";
import { mkdirSync } from "node:fs";
import path from "node:path";
import {
    fetchBloomInstanceInfo,
    findRunningStandardBloomInstance,
    getDefaultRepoRoot,
    normalizeBloomInstanceInfo,
    requireOptionValue,
    requireTcpPortOption,
    toLocalOrigin,
} from "../../../.github/skills/bloom-automation/bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        runningBloom: false,
        httpPort: undefined,
        out: undefined,
        json: false,
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

        if (arg === "--out") {
            options.out = requireOptionValue(args, index, "--out");
            index++;
            continue;
        }

        if (arg.startsWith("--out=")) {
            options.out = arg.slice("--out=".length);
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
        "Usage: node .claude/skills/run-bloom/screenshotBloom.mjs (--running-bloom | --http-port <port>) [--out <path>] [--json]",
    );
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
            `Could not load Playwright from ${componentTesterDir}. Run 'pnpm install' in src/BloomBrowserUI/react_components/component-tester if dependencies are missing. Original error: ${message}`,
        );
    }
};

const resolveInstance = async (options) => {
    if (options.httpPort) {
        const response = await fetchBloomInstanceInfo(options.httpPort);
        if (!response.reachable || !response.json) {
            throw new Error(
                `No Bloom instance reported common/instanceInfo on http://localhost:${options.httpPort}.`,
            );
        }

        return normalizeBloomInstanceInfo(response.json, options.httpPort);
    }

    if (options.runningBloom) {
        const instance = await findRunningStandardBloomInstance();
        if (!instance) {
            throw new Error(
                "No running Bloom instance was found on Bloom's standard HTTP port range.",
            );
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
    const instance = await resolveInstance(options);
    if (!instance.cdpPort) {
        throw new Error(
            "The selected Bloom instance did not report a CDP endpoint.",
        );
    }

    const outPath = path.resolve(
        getDefaultRepoRoot(),
        options.out ?? path.join("output", "screenshots", "bloom.png"),
    );
    mkdirSync(path.dirname(outPath), { recursive: true });

    const { chromium } = loadPlaywright();
    const browser = await chromium.connectOverCDP(
        toLocalOrigin(instance.cdpPort),
    );

    try {
        const page = getBloomPage(browser);
        if (!page) {
            throw new Error(
                `Could not find a Bloom WebView2 target on CDP port ${instance.cdpPort}.`,
            );
        }

        await page.waitForLoadState("domcontentloaded");
        await page.screenshot({ path: outPath });

        const result = {
            instance: {
                processId: instance.processId,
                httpPort: instance.httpPort,
                cdpPort: instance.cdpPort,
            },
            pageUrl: page.url(),
            screenshot: outPath,
        };

        if (options.json) {
            console.log(JSON.stringify(result, null, 2));
            return;
        }

        console.log(`Bloom HTTP port: ${result.instance.httpPort}`);
        console.log(`Page URL: ${result.pageUrl}`);
        console.log(`Screenshot: ${result.screenshot}`);
    } finally {
        await browser.close();
    }
};

main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
});
