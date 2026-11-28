// Submitting Bloom's Report a Problem dialog with Bloom offline, so that nothing reaches Bloom's
// real YouTrack and Bloom instead offers the report as a zip to email (ProblemReportApi
// .SubmitToYouTrack, MakeEmailableReportFile). That zip holds what an online report would have sent:
// report.txt, and, when the book is included, the zipped book with its collection's files.
//
// Only Bloom goes offline, not the machine: Bloom restarts with its web proxy set to one the test
// runs (refusingProxy.ts), which refuses everything. Submit is pressed only once the proxy has
// refused Bloom's startup request to api.bloomlibrary.org. That request goes through RestSharp,
// which sends it with an HttpClient on .NET's default proxy, the same proxy as the HttpClient that
// YouTrackIssueSubmitter sends a report with; so it proves that the report will go to the proxy too.
// (The S3 client's startup requests are not proof: it sets its proxy itself.)
//
// Submit also saves the email typed into Bloom's registration, which lives in the Windows account's
// own Bloom folder rather than the test's user-settings folder (Registration.cs), whenever no email
// was registered there. So withBloomOfflineForReports puts that file back as it found it.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect } from "../fixtures/bloomTest";
import type { IBloomApp } from "../fixtures/bloomTest";
import { getUiString } from "./api";
import { type IRefusingProxy, startRefusingProxy } from "./refusingProxy";

// Where Bloom files a report (issueTrackingSystemBackend in BloomExe/Resources/
// current-service-urls.json, the list Bloom falls back on when it cannot fetch the current one).
export const kYouTrackHost = "issues.bloomlibrary.org:443";
// What Bloom asks for at startup through RestSharp (BloomLibraryBookApiClient.kHost).
const kBloomLibraryApiHost = "api.bloomlibrary.org:443";
export const kIssuesEmail = "issues@bloomlibrary.org";

// Bloom's registration (Registration.cs: ProjectContext.AppDataPath/Registration.json).
const kRegistrationFile = Path.join(
    process.env.LOCALAPPDATA ?? "",
    "SIL",
    "Bloom",
    "Registration.json",
);

/** The registration file's bytes, or undefined when there is none. */
function readRegistration(): Buffer | undefined {
    return fs.existsSync(kRegistrationFile)
        ? fs.readFileSync(kRegistrationFile)
        : undefined;
}

/** Put the registration file back as readRegistration() found it. */
function restoreRegistration(saved: Buffer | undefined): void {
    const now = readRegistration();
    if (saved === undefined) {
        if (now !== undefined) fs.rmSync(kRegistrationFile);
    } else if (now === undefined || !now.equals(saved)) {
        fs.writeFileSync(kRegistrationFile, saved);
    }
}

/** What withBloomOfflineForReports hands its body. */
export interface IOfflineBloom {
    /** Bloom's shell page after the offline restart (the `page` fixture is stale by then). */
    page: Page;
    proxy: IRefusingProxy;
    /** Have this file (a report's zip) deleted when the body ends, however it ends. */
    deleteAfterwards: (path: string) => void;
}

/**
 * Restart Bloom offline, wait until that is proven, run `body`, then put everything back: the
 * registration, any zips handed to deleteAfterwards, and Bloom online again. A clean-up's failure
 * fails a body that passed, but never hides the failure of a body that failed.
 */
export async function withBloomOfflineForReports(
    bloomApp: IBloomApp,
    body: (offline: IOfflineBloom) => Promise<void>,
): Promise<void> {
    const registration = readRegistration();
    const proxy = await startRefusingProxy();
    const toDelete: string[] = [];
    let finished = false;
    try {
        const page = await bloomApp.restart(undefined, { proxy: proxy.url });
        await expect
            .poll(() => proxy.hostsAsked(), {
                timeout: 60000,
                message:
                    `Bloom never asked the proxy for ${kBloomLibraryApiHost}, so it is not proven ` +
                    "offline, and Submit could file a real report: not going on",
            })
            .toContain(kBloomLibraryApiHost);
        await body({
            page,
            proxy,
            deleteAfterwards: (path) => toDelete.push(path),
        });
        finished = true;
    } finally {
        const cleanUps: Array<[string, () => unknown]> = [
            [
                "put back the registration",
                () => restoreRegistration(registration),
            ],
            [
                "delete the report's zip",
                () => {
                    for (const path of toDelete)
                        if (fs.existsSync(path)) fs.rmSync(path);
                },
            ],
            // Put Bloom back online for whatever this worker runs next.
            [
                "restart Bloom online",
                () => bloomApp.restart(undefined, { proxy: "" }),
            ],
            // In case the Bloom that was offline wrote the registration again as it went.
            [
                "put back the registration again",
                () => restoreRegistration(registration),
            ],
            ["close the proxy", () => proxy.close()],
        ];
        const failures: string[] = [];
        for (const [what, cleanUp] of cleanUps) {
            try {
                await cleanUp();
            } catch (error) {
                failures.push(`could not ${what}: ${String(error)}`);
            }
        }
        if (failures.length) {
            if (finished) throw new Error(failures.join("\n"));
            console.error(failures.join("\n"));
        }
    }
}

/**
 * The zip path in the failure message, read by turning the message's template (in the UI's
 * language, {0} the zip path and {1} the email address, in whatever order the translation puts
 * them) into a pattern; undefined while the dialog shows anything else.
 */
function zipPathIn(message: string, template: string): string | undefined {
    const escape = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const pattern = template
        .split(/(\{[01]\})/)
        .map((piece) => {
            if (piece === "{0}") return "\\s*(.+?)\\s*";
            if (piece === "{1}") return `\\s*${escape(kIssuesEmail)}\\s*`;
            return escape(piece.replace(/\s+/g, " ").trim()).replace(
                / /g,
                "\\s+",
            );
        })
        .join("");
    return new RegExp(`^${pattern}$`).exec(message)?.[1];
}

async function readDialogMessage(dialogPage: Page): Promise<string> {
    const text = await dialogPage
        .locator(".problem-dialog .content")
        .innerText()
        .catch(() => "");
    return text.replace(/\s+/g, " ").trim();
}

/**
 * Fill in the open Report a Problem dialog's description and email, press Submit, and wait until
 * the dialog says that Bloom could not send the report and names a zip to email instead. Checks
 * that Bloom did try YouTrack (through the proxy) and shows no link to a filed issue. Returns the
 * zip's path; the caller hands it to deleteAfterwards.
 */
export async function submitReportOffline(
    offline: IOfflineBloom,
    dialogPage: Page,
    description: string,
    email: string,
): Promise<string> {
    await dialogPage
        .locator(".problem-dialog .what_were_you_doing textarea")
        .first()
        .fill(description);
    await dialogPage.locator(".problem-dialog .email input").fill(email);
    await dialogPage.getByTestId("ReportProblemDialog.SubmitButton").click();

    const template = await getUiString(
        offline.page,
        "ReportProblemDialog.CouldNotSendToServer",
        "Bloom was not able to submit your report directly to our server. Please retry or email {0} to {1}.",
    );
    let shown = "";
    let zipPath: string | undefined;
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
    offline.deleteAfterwards(zipPath!);
    expect(
        offline.proxy.hostsAsked(),
        "Bloom should have tried to send the report to YouTrack, and been refused",
    ).toContain(kYouTrackHost);
    expect(
        await dialogPage.locator(".problem-dialog a[href]").count(),
        `no link to a filed issue should show; the dialog said "${shown}"`,
    ).toBe(0);
    return zipPath!;
}
