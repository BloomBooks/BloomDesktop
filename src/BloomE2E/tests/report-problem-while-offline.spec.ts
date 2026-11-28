// Test Case ID 49, "Report Problem While Offline": with no internet, Help > Report a Problem...
// and Submit should not just fail. Bloom should say that it could not send the report, and give
// the person a zip of it to email to issues@bloomlibrary.org instead.
//
// The test takes only Bloom offline, not the machine (helpers/offlineProblemReport.ts says how,
// and why Submit then cannot file a real issue on Bloom's YouTrack).

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { readZip } from "../helpers/bulkBloomPub";
import {
    submitReportOffline,
    withBloomOfflineForReports,
} from "../helpers/offlineProblemReport";
import {
    closeReactDialogFromNode,
    waitForReportDialogPage,
} from "../helpers/problemDialogs";
import { chooseReportAProblemFromHelpMenu } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "report-problem-offline", languages: ["en"] },
});

test("TC-49: a report submitted offline is offered as a zip to email", async ({
    bloomApp,
    problemDialogWatcher,
}) => {
    test.setTimeout(300000);
    await withBloomOfflineForReports(bloomApp, async (offline) => {
        const page = offline.page;
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
            const zipPath =
                await test.step("submit the report; Bloom says it could not send it, and offers a zip to email", () =>
                    submitReportOffline(
                        offline,
                        dialogPage,
                        description,
                        email,
                    ));

            await test.step("the zip holds the report", async () => {
                expect(Path.basename(zipPath)).toMatch(/^Report .*\.zip$/);
                expect(fs.existsSync(zipPath), `${zipPath} should exist`).toBe(
                    true,
                );
                expect(
                    fs.statSync(zipPath).mtimeMs,
                    "the zip should have been made by this Submit",
                ).toBeGreaterThanOrEqual(submittedAt - 2000);
                const entries = readZip(fs.readFileSync(zipPath));
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
    });
});
