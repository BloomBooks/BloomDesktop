// Test Case ID 49, "Report Problem While Offline": with no internet, Help > Report a Problem...
// and Submit should not just fail. Bloom should say that it could not send the report, and give
// the person a zip of it to email to issues@bloomlibrary.org instead.
//
// The test takes only Bloom offline, not the machine: it restarts Bloom with its web proxy set to
// one the test runs (helpers/refusingProxy.ts), which refuses everything, so every request Bloom's
// code makes to the internet fails as it does with no connection. Pressing Submit online would
// file a real issue on Bloom's YouTrack, so the test presses it only once the proxy has seen
// Bloom's own startup traffic, which proves that Bloom's requests go through it, and it checks
// afterwards that Bloom did try to reach YouTrack through it.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import { readZip } from "../helpers/bulkBloomPub";
import {
    closeReactDialogFromNode,
    waitForReportDialogPage,
} from "../helpers/problemDialogs";
import { startRefusingProxy } from "../helpers/refusingProxy";
import { chooseReportAProblemFromHelpMenu } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "report-problem-offline", languages: ["en"] },
});

// Where Bloom files a report (issueTrackingSystemBackend in BloomExe/Resources/
// current-service-urls.json, the list Bloom falls back on when it cannot fetch the current one).
const kYouTrackHost = "issues.bloomlibrary.org:443";
const kIssuesEmail = "issues@bloomlibrary.org";

/**
 * The zip path in the failure message, read by turning the message's template (in the UI's
 * language) into a pattern; undefined while the dialog shows anything else.
 */
function zipPathIn(message: string, template: string): string | undefined {
    const escape = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const [before, between, after] = template
        .split(/\{[01]\}/)
        .map((piece) => escape(piece.replace(/\s+/g, " ").trim()));
    const pattern = new RegExp(
        `^${before}\\s*(.+?)\\s*${between}\\s*${escape(kIssuesEmail)}\\s*${after}$`,
    );
    return pattern.exec(message)?.[1];
}

async function readDialogMessage(dialogPage: Page): Promise<string> {
    const text = await dialogPage
        .locator(".problem-dialog .content")
        .innerText()
        .catch(() => "");
    return text.replace(/\s+/g, " ").trim();
}

test("TC-49: a report submitted offline is offered as a zip to email", async ({
    bloomApp,
    problemDialogWatcher,
}) => {
    test.setTimeout(300000);
    const proxy = await startRefusingProxy();
    let zipPath: string | undefined;
    try {
        const page = await test.step("take Bloom offline", async () => {
            const shell = await bloomApp.restart(undefined, {
                proxy: proxy.url,
            });
            await expect
                .poll(() => proxy.hostsAsked().length, {
                    timeout: 60000,
                    message:
                        "Bloom never sent its startup requests through the proxy, so it may not be " +
                        "offline, and Submit could file a real report: not pressing it",
                })
                .toBeGreaterThan(0);
            return shell;
        });

        const resumeWatcher = await problemDialogWatcher.pause();
        try {
            const dialogPage =
                await test.step("open Report a Problem", async () => {
                    await chooseReportAProblemFromHelpMenu(page);
                    return waitForReportDialogPage(page);
                });

            const description = `TC-49 e2e: reporting a problem while offline ${Date.now()}`;
            const email = "tc49-e2e@example.com";
            const submittedAt = Date.now();
            await test.step("fill in the report and submit it", async () => {
                await dialogPage
                    .locator(".problem-dialog .what_were_you_doing textarea")
                    .first()
                    .fill(description);
                await dialogPage
                    .locator(".problem-dialog .email input")
                    .fill(email);
                await dialogPage
                    .getByTestId("ReportProblemDialog.SubmitButton")
                    .click();
            });

            const template = await getUiString(
                page,
                "ReportProblemDialog.CouldNotSendToServer",
                "Bloom was not able to submit your report directly to our server. Please retry or email {0} to {1}.",
            );
            await test.step("Bloom says it could not send the report, and offers a zip to email", async () => {
                let shown = "";
                await expect
                    .poll(
                        async () => {
                            shown = await readDialogMessage(dialogPage);
                            zipPath = zipPathIn(shown, template);
                            return zipPath !== undefined;
                        },
                        {
                            timeout: 120000,
                            message:
                                "the dialog should say that Bloom could not submit the report and name " +
                                `a zip to email to ${kIssuesEmail}`,
                        },
                    )
                    .toBe(true);
                expect(
                    proxy.hostsAsked(),
                    "Bloom should have tried to send the report to YouTrack, and been refused",
                ).toContain(kYouTrackHost);
                expect(
                    await dialogPage.locator(".problem-dialog a[href]").count(),
                    `no link to a filed issue should show; the dialog said "${shown}"`,
                ).toBe(0);
            });

            await test.step("the zip holds the report", async () => {
                const path = zipPath!;
                expect(Path.basename(path)).toMatch(/^Report .*\.zip$/);
                expect(fs.existsSync(path), `${path} should exist`).toBe(true);
                expect(
                    fs.statSync(path).mtimeMs,
                    "the zip should have been made by this Submit",
                ).toBeGreaterThanOrEqual(submittedAt - 2000);
                const entries = readZip(fs.readFileSync(path));
                const report = entries.find((e) => e.name === "report.txt");
                expect(
                    report,
                    `the zip should hold report.txt; it holds ${entries.map((e) => e.name).join(", ")}`,
                ).toBeDefined();
                const text = report!.content.toString("utf8");
                expect(text).toContain(description);
                expect(text).toContain(email.split("@")[0]);
            });

            await test.step("close the dialog", async () => {
                await closeReactDialogFromNode(page);
                await expect
                    .poll(
                        async () =>
                            dialogPage.isClosed() ||
                            (await dialogPage
                                .locator(".problem-dialog")
                                .count()
                                .catch(() => 0)) === 0,
                        { timeout: 30000 },
                    )
                    .toBe(true);
            });
        } finally {
            resumeWatcher();
        }
    } finally {
        if (zipPath && fs.existsSync(zipPath)) fs.rmSync(zipPath);
        // Put Bloom back online for whatever this worker runs next.
        await bloomApp.restart(undefined, { proxy: "" });
        await proxy.close();
    }
});
