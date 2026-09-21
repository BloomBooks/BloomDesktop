// CDP plumbing shared by the live checks in this directory (BL-6681).
//
// Connects Playwright to the running Bloom's WebView2. The ports come from the dev launcher's
// status API (`launcherControl.mjs --status --json`), or from BLOOM_CDP_PORT / BLOOM_HTTP_PORT if
// you started Bloom some other way.
import { createRequire } from "node:module";
import { execFileSync } from "node:child_process";
import path from "node:path";
import url from "node:url";

export const repoRoot = path.resolve(
    path.dirname(url.fileURLToPath(import.meta.url)),
    "../../..",
);
const ctDir = path.join(
    repoRoot,
    "src/BloomBrowserUI/react_components/component-tester",
);
const req = createRequire(path.join(ctDir, "package.json"));
const { chromium } = req("playwright");

/**
 * The running Bloom's ports. Asked of the dev launcher (`launcherControl.mjs --status --json`, which
 * reads output/bloom-launcher.json and queries the launcher's control API) unless overridden.
 */
export function ports() {
    let fromLauncher = {};
    if (!process.env.BLOOM_CDP_PORT || !process.env.BLOOM_HTTP_PORT) {
        try {
            const out = execFileSync(
                "node",
                [
                    path.join(
                        repoRoot,
                        ".github/skills/bloom-automation/launcherControl.mjs",
                    ),
                    "--status",
                    "--json",
                ],
                { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] },
            );
            const status = JSON.parse(out).status || {};
            fromLauncher = {
                cdpPort: status.cdpPort,
                httpPort: status.httpPort,
            };
        } catch {
            // no launcher; rely on the environment
        }
    }
    return {
        cdpPort: process.env.BLOOM_CDP_PORT || fromLauncher.cdpPort,
        httpPort: process.env.BLOOM_HTTP_PORT || fromLauncher.httpPort,
    };
}

export async function connect() {
    const { cdpPort } = ports();
    if (!cdpPort)
        throw new Error(
            "no CDP port: start Bloom with launcherControl.mjs or set BLOOM_CDP_PORT",
        );
    const browser = await chromium.connectOverCDP(
        `http://localhost:${cdpPort}`,
    );
    const pages = browser.contexts().flatMap((c) => c.pages());
    return { browser, pages };
}

/** Bloom's main (workspace) page, as opposed to the toolbox content page. */
export function mainPage(pages) {
    const p = pages.find(
        (p) =>
            p.url().includes("/bloom/") && !p.url().includes("toolboxcontent"),
    );
    if (!p) throw new Error("no Bloom main page over CDP");
    return p;
}
