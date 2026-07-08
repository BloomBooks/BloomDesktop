// E2E-1: create/share an existing collection (verify rows + objects).
//
// The UI path for this (Settings dialog -> Team Collection tab -> "Share this collection on
// the Bloom sharing server" -> CreateCloudTeamCollectionDialog) is NOT automatable over CDP:
// every one of those is a WinForms Form hosting its OWN WebView2 control (a genuinely separate
// browser process/environment, confirmed via Bloom's own log: each shows a distinct
// `UserDataFolder=...Bloom WV2-<n>`), and ALL of them request the SAME fixed
// `--remote-debugging-port` (WebView2Browser.cs: `RemoteDebuggingPort => portForHttp + 2`,
// applied uniformly to every control). Only one such browser process can ever actually bind
// that port, so secondary dialogs are invisible to Playwright's `connectOverCDP` AND to the
// raw CDP `/json/list` endpoint (confirmed empirically: polled both for 15s with the dialog
// independently known to be open). See README.md and the progress log for the full
// investigation.
//
// Since the create-cloud-collection dialog's checkboxes are a pure client-side
// acknowledgement gate (never sent to the server -- see HandleCreateCloudTeamCollection in
// TeamCollectionApi.cs, which takes no request body), this test drives the exact same backend
// action the dialog's "Share Collection" button would (`POST
// teamCollection/createCloudTeamCollection`), then verifies the real, observable results: the
// TeamCollection capabilities flip from folder to cloud, a `tc.collections` row appears, and
// every collection file + book file lands in S3 under `tc/<collectionId>/`.
import { test, expect } from "@playwright/test";
import * as path from "node:path";
import { resetStack } from "../harness/reset";
import { createScratchCollection } from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE } from "../harness/devStack";
import {
    postApi,
    getApi,
    postCreateCloudTeamCollection,
} from "../harness/bloomApi";
import { queryDb } from "../harness/db";
import { listS3Objects } from "../harness/s3";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-1";

test.describe("E2E-1 create/share an existing collection", () => {
    let instance: LaunchedBloom | undefined;

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        if (instance) {
            await instance.kill();
            instance = undefined;
        }
    });

    test("sharing a folder collection to the cloud creates a DB row and uploads every file", async () => {
        const scratch = await createScratchCollection("e2e-1", "alice");

        instance = await launchBloom({
            collectionFilePath: scratch.collectionFilePath,
            user: ALICE,
            label: "e2e1-alice",
            logDir: LOG_DIR,
        });

        // Wait for the workspace WebView2 to finish initializing before triggering
        // createCloudTeamCollection (the same connect-before-trigger pattern E2E-2 uses, but
        // for a different reason): the endpoint's handler runs ON the UI thread and opens a
        // modal BrowserProgressDialog. If the request arrives while the workspace browser is
        // still inside EnsureBrowserReadyToNavigate's nested message pump, the dialog's OWN
        // WebView2 initialization deadlocks against it, the dialog's React page never POSTs
        // progress/ready, DoWorkWithProgressDialog's worker spins forever on
        // _readyForProgressReports, and the HTTP request never returns (diagnosed 8 Jul 2026
        // from a dotnet-stack dump of the hung process; reproducible whenever the desktop
        // session is locked, which slows WebView2 startup enough to lose the race every time).
        // A CDP-attachable, dom-loaded workspace page proves initialization is complete.
        await instance.connect();

        const capsBefore = await (
            await getApi(instance.httpPort, "teamCollection/capabilities")
        ).json();
        expect(capsBefore.supportsSharingUi).toBe(false);

        const createResponse = await postCreateCloudTeamCollection(instance);
        expect(createResponse.status).toBe(200);

        // ConnectToCloudCollection + the reopen callback + the initial upload are not
        // synchronous with the HTTP reply; poll for the capability flip rather than assuming
        // a fixed delay is enough.
        await expect
            .poll(
                async () => {
                    const caps = await (
                        await getApi(
                            instance!.httpPort,
                            "teamCollection/capabilities",
                        )
                    ).json();
                    return caps.supportsSharingUi;
                },
                {
                    timeout: 20_000,
                    message: "capabilities never flipped to a cloud collection",
                },
            )
            .toBe(true);

        const collectionRows = await queryDb<{ id: string; name: string }>(
            "select id, name from tc.collections where id = $1",
            [scratch.collectionId],
        );
        expect(collectionRows).toHaveLength(1);
        expect(collectionRows[0].name).toBe(scratch.collectionName);

        // Poll S3 too: the initial upload happens after the DB row is created.
        await expect
            .poll(
                async () =>
                    (await listS3Objects(`tc/${scratch.collectionId}/`)).length,
                {
                    timeout: 20_000,
                    message:
                        "no objects ever appeared in S3 for this collection",
                },
            )
            .toBeGreaterThan(0);

        const keys = await listS3Objects(`tc/${scratch.collectionId}/`);
        // One collection file group (customCollectionStyles.css, the .bloomCollection file,
        // ReaderTools*.json) plus the one template book's files, each under books/<bookId>/.
        expect(
            keys.some((key) =>
                key.endsWith(`${scratch.collectionName}.bloomCollection`),
            ),
        ).toBe(true);
        expect(
            keys.some((key) => key.endsWith(`${scratch.bookName}.htm`)),
        ).toBe(true);
        expect(keys.some((key) => key.endsWith("meta.json"))).toBe(true);
    });
});
