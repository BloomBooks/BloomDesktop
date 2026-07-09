// Detect and dismiss Bloom's "Bloom had a problem" report/notify dialog over CDP.
//
// This dialog (src/BloomBrowserUI/problemDialog/*.tsx, both the ReportDialog and
// NotifyDialog variants) is hosted in its OWN WinForms window with its own
// WebView2, so it shows up as a separate CDP page target, not inside the main
// shell document or the edit-view `page` iframe. Both variants render a MUI
// <Dialog className="problem-dialog">, which is how we detect it.
//
// Dismissal uses the SAME action as the dialog's own Close/Quit/Cancel button:
// POST /bloom/api/common/closeReactDialog (see ReportDialog.getEndingButton ->
// post("common/closeReactDialog") and CommonApi.cs). We deliberately never click
// Submit: submitting sends a problem report (with a screenshot and the book) to
// Bloom's servers, which an automated run must not do. We only POST after
// confirming a .problem-dialog is actually present, so a legitimate modal is
// never closed out from under the user.
//
// Usage:
//   node .github/skills/bloom-automation/dismissProblemDialog.mjs --http-port <port> [--wait] [--timeout-ms <ms>] [--json]
//
// Exit code is 0 whether or not a dialog was present (absence is not an error);
// the JSON/text output reports what happened. Use --wait to poll until a dialog
// appears (or the timeout elapses) before dismissing.
import { createRequire } from "node:module";
import path from "node:path";
import {
    fetchBloomInstanceInfo,
    getDefaultRepoRoot,
    normalizeBloomInstanceInfo,
    requireOptionValue,
    requireTcpPortOption,
    toBloomApiBaseUrl,
    toLocalOrigin,
} from "./bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        httpPort: undefined,
        wait: false,
        timeoutMs: 15000,
        json: false,
    };

    for (let index = 0; index < args.length; index++) {
        const arg = args[index];

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

        if (arg === "--wait") {
            options.wait = true;
            continue;
        }

        if (arg === "--timeout-ms") {
            options.timeoutMs = Number(
                requireOptionValue(args, index, "--timeout-ms"),
            );
            index++;
            continue;
        }

        if (arg === "--json") {
            options.json = true;
            continue;
        }

        if (arg === "--help") {
            console.log(
                "Usage: node .github/skills/bloom-automation/dismissProblemDialog.mjs --http-port <port> [--wait] [--timeout-ms <ms>] [--json]",
            );
            process.exit(0);
        }
    }

    if (!options.httpPort) {
        throw new Error("Specify --http-port <port>.");
    }

    return options;
};

const loadPlaywright = () => {
    const componentTesterDir = path.join(
        getDefaultRepoRoot(),
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
            `Could not load Playwright from ${componentTesterDir}. Run 'yarn install' there if dependencies are missing. Original error: ${message}`,
        );
    }
};

const resolveInstance = async (httpPort) => {
    const response = await fetchBloomInstanceInfo(httpPort);
    if (!response.reachable || !response.json) {
        throw new Error(
            `No Bloom instance reported common/instanceInfo on http://localhost:${httpPort}.`,
        );
    }

    return normalizeBloomInstanceInfo(response.json, httpPort);
};

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// Look through every CDP page for one whose DOM contains a .problem-dialog root.
// The dialog is its own WinForms-hosted WebView, so it is a separate page target
// (in dev it is even served from the Vite port, not the Bloom http port), which
// is why we scan ALL pages rather than filtering by URL. Returns {page, heading}
// for the first match, or undefined if none is showing.
const findProblemDialog = async (browser) => {
    const pages = browser.contexts().flatMap((context) => context.pages());
    for (const page of pages) {
        if (page.url().startsWith("devtools://")) {
            continue;
        }

        const heading = await page
            .evaluate(() => {
                const dialog = document.querySelector(".problem-dialog");
                if (!dialog) {
                    return null;
                }
                const title = dialog.querySelector(".dialog-title");
                const body = dialog.querySelector(".report-heading");
                return [title?.textContent, body?.textContent]
                    .filter(Boolean)
                    .join(" — ")
                    .replace(/\s+/g, " ")
                    .trim();
            })
            .catch(() => null);

        if (heading !== null) {
            return { page, heading };
        }
    }

    return undefined;
};

// Gather the underlying problem BEFORE closing, so we never silently discard what
// went wrong. The dialog body only shows the generic "What were you doing?" form;
// the actual exception + stack live behind the "Learn More" link (the privacy
// screen), where Bloom shows exactly what it would send. We click Learn More with
// Playwright (React's onClick does not fire from an in-page element.click()),
// scrape the details, and read the concise problem name. Returns
// {problem, detail} (either may be null if it could not be read).
const gatherProblemDetail = async (page) => {
    const readDialogText = () =>
        page
            .evaluate(() => {
                const d = document.querySelector(".problem-dialog");
                return d ? d.innerText.replace(/[ \t]+\n/g, "\n").trim() : null;
            })
            .catch(() => null);

    // The concise problem name (e.g. "Cannot Find File") from the report heading.
    const problem = await page
        .evaluate(() => {
            const h = document.querySelector(".problem-dialog .report-heading");
            return h ? h.textContent.replace(/\s+/g, " ").trim() : null;
        })
        .catch(() => null);

    let detail = null;
    try {
        const learnMore = page.getByText(/learn more/i).first();
        if (await learnMore.count()) {
            await learnMore.click({ timeout: 3000 });
            await page.waitForTimeout(600);
            const full = await readDialogText();
            // Trim to the useful part: from "Exception Details" onward if present.
            if (full) {
                const idx = full.search(/exception details/i);
                detail = (idx >= 0 ? full.slice(idx) : full).slice(0, 1500);
            }
            const back = page.getByText(/^back$/i).first();
            if (await back.count()) {
                await back.click({ timeout: 3000 }).catch(() => {});
            }
        }
    } catch {
        // Leave detail null; the caller still reports the concise problem name.
    }

    return { problem, detail };
};

const main = async () => {
    const options = parseArgs();
    const instance = await resolveInstance(options.httpPort);
    if (!instance.cdpPort) {
        throw new Error(
            "The selected Bloom instance did not report a CDP endpoint.",
        );
    }

    const { chromium } = loadPlaywright();
    const browser = await chromium.connectOverCDP(
        toLocalOrigin(instance.cdpPort),
    );

    try {
        const deadline = Date.now() + options.timeoutMs;
        let found = await findProblemDialog(browser);
        while (!found && options.wait && Date.now() < deadline) {
            await delay(250);
            found = await findProblemDialog(browser);
        }

        // Drain a backlog: Bloom queues non-fatal reports and shows them one at a
        // time, so closing one can reveal the next. For each, we GATHER the
        // underlying problem before closing (so nothing is silently discarded),
        // then close it with the same action as the dialog's own Close button.
        // The cap stops us looping forever if a report re-fires faster than we can
        // close it (that means a real recurring error in the code under test).
        const maxToDrain = 25;
        const gathered = [];
        while (found && gathered.length < maxToDrain) {
            const { problem, detail } = await gatherProblemDetail(found.page);
            gathered.push({ heading: found.heading, problem, detail });

            // Same action as the dialog's Close/Quit/Cancel button (never Submit,
            // which would send a report to Bloom's servers).
            const response = await fetch(
                `${toBloomApiBaseUrl(options.httpPort)}/common/closeReactDialog`,
                { method: "POST" },
            );
            if (!response.ok) {
                throw new Error(
                    `closeReactDialog failed: ${response.status} ${response.statusText}`,
                );
            }

            // Wait for THIS dialog to go away, then look for the next queued one.
            const closeDeadline = Date.now() + 5000;
            let next = found;
            while (Date.now() < closeDeadline) {
                await delay(200);
                next = await findProblemDialog(browser);
                if (!next || next.heading !== found.heading) {
                    break;
                }
            }
            if (next && next.heading === found.heading) {
                // Same dialog still up after 5s: closing isn't taking, or it is
                // re-firing immediately. Stop and report rather than spin.
                found = next;
                break;
            }
            found = next;
        }

        const stillPresent = Boolean(await findProblemDialog(browser));
        const result = {
            instance: {
                processId: instance.processId,
                httpPort: instance.httpPort,
                cdpPort: instance.cdpPort,
            },
            present: gathered.length > 0,
            dismissedCount: stillPresent
                ? gathered.length - 1
                : gathered.length,
            stillPresent,
            problems: gathered,
        };

        if (options.json) {
            console.log(JSON.stringify(result, null, 2));
            return;
        }

        if (gathered.length === 0) {
            console.log("No problem dialog was showing.");
        } else {
            console.log(
                `Handled ${gathered.length} problem dialog(s)${stillPresent ? " (one still present — likely re-firing)" : ""}:`,
            );
            for (const p of gathered) {
                console.log(`\n• ${p.problem || p.heading}`);
                if (p.detail) {
                    console.log(
                        p.detail
                            .split("\n")
                            .map((l) => `    ${l}`)
                            .join("\n"),
                    );
                }
            }
        }
    } finally {
        await browser.close();
    }
};

main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
});
