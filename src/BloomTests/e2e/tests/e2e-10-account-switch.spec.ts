// E2E-10: account-switch behavior (dogfood batch 1, item 9).
//
// STATUS: authored but NOT RUN. This task's hard rules forbid launching Bloom or running
// anything under src/BloomTests/e2e (the desktop/E2E lane belongs to the orchestrator). This is
// a non-run artifact for the orchestrator's next E2E pass, written against the harness patterns
// established by e2e-5 (approved accounts) and e2e-2 (two-instance collaboration loop).
//
// This REPLACES the task-09 scenario of the same number: that older scenario ("in-session
// account switch/sign-out while a book is checked out, blocked with preserve-&-release
// choices") was recorded as BLOCKED in tasks/09-e2e.md because no such feature existed. John's 9
// Jul decision (Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md item 9) replaces
// that shape entirely with an OPEN-TIME scenario: a local collection folder was used under one
// account; Bloom is re-launched against that SAME LOCAL FOLDER signed in as a DIFFERENT account
// (simulating a shared computer). No "no such feature exists" gap remains -- there is now a
// real accept/refuse decision to drive an E2E against. No spec file previously existed for
// "e2e-10"; this one is new.
//
// The harness represents "the same local collection reopened under a different account" as
// launchBloom() called twice, SEQUENTIALLY (never concurrently -- only one process may hold the
// collection folder), against the SAME collectionFilePath, with a different `user` each time --
// exactly like e2e-5's already-established pattern of relaunching a pulled-down collection under
// a different DevUser. "Same machine" falls out for free: every launch in this harness runs on
// the same physical/dev-stack machine, so TeamCollectionManager.CurrentMachine is identical
// across the two launches without any special handling.
//
// Two scenarios, matching DOGFOOD-BATCH-1.md item 9 exactly:
//   1. Bloom relaunched signed in as a NON-member (never approved) of the collection ->
//      REFUSE to open. Verified via the instance's own log file (native WinForms MessageBox
//      content is not CDP-reachable at all -- see RISK note below -- so the log line
//      Program.HandleErrorOpeningProjectWindow now writes for this exact case
//      ("*** Refused to open collection ...") is the robust, already-available signal).
//   2. Bloom relaunched signed in as an APPROVED member (but not the account that checked out
//      the book) -> CONNECTED. The book Alice checked out here still shows `who: alice`, but Bob
//      can check it in directly (no explicit re-checkout first) and the check-in succeeds and is
//      attributed to Bob both in the server event log and in the book's post-checkin lock state.
//
// RISKS / things the orchestrator should double-check on the first real run:
//   - The refusal MessageBox is a native Win32 dialog (SIL.Reporting.ErrorReport.NotifyUserOfProblem),
//     not a WebView2/React one -- CDP cannot see it at all (worse than finding #2's React-dialog
//     case). The log-file assertion below is the recommended verification; if a future task wants
//     to also assert the dialog was VISIBLE on screen, that needs a native Win32 approach (e.g.
//     enumerating top-level windows by title), not CDP.
//   - After the refusal, Program.cs falls through to ChooseACollection() (the collection chooser),
//     so the process still reports BLOOM_AUTOMATION_READY (WriteAutomationStartupInfo fires
//     whenever ANY BloomServer starts listening, including the chooser's) -- launchBloom() should
//     NOT hang, but `connect()` in that case attaches to the CHOOSER's page, not a project. This
//     spec does not call connect() for the refusal instance at all, only checks the log + that no
//     project-specific state exists.
//   - The takeover's "on B's first EDIT" requirement has no literal keystroke-level hook in the
//     product (see the item 9 implementation report); the atomic lock handover happens at
//     check-in time instead (the earliest point it has any observable effect). This spec checks
//     in without an intervening edit, which is explicitly one of the two cases John's spec
//     names ("If B checks the book in (even without editing first), history records the checkin
//     by B") -- it does not separately exercise an edit-then-checkin path.
import { test, expect } from "@playwright/test";
import { resetStack } from "../harness/reset";
import { createScratchCollection } from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE, BOB, ADMIN } from "../harness/devStack";
import {
    postApi,
    getApi,
    postCreateCloudTeamCollection,
} from "../harness/bloomApi";
import { bookStatus } from "../harness/bookStatus";
import { selectBookByName } from "../harness/selectBook";
import { queryDb } from "../harness/db";
import * as fs from "node:fs/promises";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-10";

test.describe("E2E-10 account-switch behavior", () => {
    const instances: LaunchedBloom[] = [];

    const track = (instance: LaunchedBloom): LaunchedBloom => {
        instances.push(instance);
        return instance;
    };

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        await Promise.all(
            instances.map((i) => i.kill().catch(() => undefined)),
        );
        instances.length = 0;
    });

    test("non-member reopening the collection is refused; a member takes over an on-this-machine checkout", async () => {
        // --- Setup: Alice creates/shares, approves Bob, checks a book out ---
        const aliceScratch = await createScratchCollection("e2e-10", "alice");
        const alice = track(
            await launchBloom({
                collectionFilePath: aliceScratch.collectionFilePath,
                user: ALICE,
                label: "e2e-10-alice",
                logDir: LOG_DIR,
            }),
        );
        await alice.connect(); // connect-before-trigger (harness finding #4)
        const createResponse = await postCreateCloudTeamCollection(alice);
        expect(createResponse.status).toBe(200);
        await expect
            .poll(
                async () =>
                    (
                        await (
                            await getApi(
                                alice.httpPort,
                                "teamCollection/capabilities",
                            )
                        ).json()
                    ).supportsSharingUi,
                { timeout: 20_000 },
            )
            .toBe(true);

        // Approve Bob as a member (but NOT Admin -- Admin stays unapproved for scenario 1).
        const approveResponse = await postApi(
            alice.httpPort,
            "sharing/addApproval",
            JSON.stringify({
                collectionId: aliceScratch.collectionId,
                email: BOB.email,
                role: "member",
            }),
        );
        expect(approveResponse.status).toBe(200);

        const bookName = aliceScratch.bookName;
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        const checkoutResult = await (
            await postApi(
                alice.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(checkoutResult, "Alice's checkout must succeed").toBe(true);

        const aliceStatusBefore = await bookStatus(alice.httpPort, bookName);
        expect(aliceStatusBefore.who).toBe(ALICE.email);
        const sharedMachine = aliceStatusBefore.where; // "this machine", per this harness's own identity.

        // Alice's "computer" is now switched off, WITHOUT un-linking or wiping her local
        // collection folder -- the next launches reopen this exact folder.
        await alice.kill();

        // --- Scenario 1: reopen the SAME folder signed in as a NON-member (Admin) ---
        const adminReopen = track(
            await launchBloom({
                collectionFilePath: aliceScratch.collectionFilePath,
                user: ADMIN,
                label: "e2e-10-admin-refused",
                logDir: LOG_DIR,
            }),
        );
        // Do NOT call adminReopen.connect() -- per the RISK note above, a refused open falls
        // through to the collection chooser, not the project; there is nothing project-specific
        // to attach to. Give Bloom a moment to reach and log the refusal, then inspect the log.
        await expect
            .poll(
                async () => {
                    const log = await fs
                        .readFile(adminReopen.logPath, "utf8")
                        .catch(() => "");
                    return log.includes("Refused to open collection");
                },
                { timeout: 30_000 },
            )
            .toBe(true);
        const adminLog = await fs.readFile(adminReopen.logPath, "utf8");
        expect(adminLog).toContain(ADMIN.email); // names the current (refused) logon
        expect(adminLog.toLowerCase()).toContain("not a member");
        await adminReopen.kill();

        // The refusal must not have mutated server state: the book is still locked to Alice.
        const stillAlice = await queryDb<{ locked_by: string | null }>(
            "select m.email as locked_by from tc.books b join tc.members m on m.collection_id = b.collection_id and m.user_id = b.locked_by where b.collection_id = $1 and b.name = $2",
            [aliceScratch.collectionId, bookName],
        );
        expect(stillAlice).toHaveLength(1);
        expect(stillAlice[0].locked_by).toBe(ALICE.email);

        // --- Scenario 2: reopen the SAME folder signed in as an APPROVED member (Bob) ---
        const bobReopen = track(
            await launchBloom({
                collectionFilePath: aliceScratch.collectionFilePath,
                user: BOB,
                label: "e2e-10-bob-takeover",
                logDir: LOG_DIR,
            }),
        );
        await bobReopen.connect();
        await expect
            .poll(
                async () =>
                    (
                        await (
                            await getApi(
                                bobReopen.httpPort,
                                "teamCollection/capabilities",
                            )
                        ).json()
                    ).supportsSharingUi,
                { timeout: 20_000 },
            )
            .toBe(true);

        // CONNECTED, not disconnected: the book still shows checked out to Alice, on this
        // machine, but Bob is the current signed-in user.
        const bobStatus = await bookStatus(bobReopen.httpPort, bookName);
        expect(bobStatus.who).toBe(ALICE.email);
        expect(bobStatus.where).toBe(sharedMachine);
        expect(bobStatus.currentUser).toBe(BOB.email);

        // Bob checks the book in WITHOUT an explicit re-checkout first (John's spec: "If B
        // checks the book in (even without editing first), history records the checkin by B").
        await selectBookByName(
            bobReopen.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        const checkinResponse = await postApi(
            bobReopen.httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        expect(
            checkinResponse.status,
            "Bob's check-in must succeed even though Alice's account originally held the lock",
        ).toBe(200);

        // Server-side: the lock released, and the checkin event is attributed to BOB, not Alice.
        const afterCheckin = await queryDb<{ locked_by: string | null }>(
            "select locked_by from tc.books where collection_id = $1 and name = $2",
            [aliceScratch.collectionId, bookName],
        );
        expect(afterCheckin).toHaveLength(1);
        expect(afterCheckin[0].locked_by).toBeNull();

        const lastCheckinEvent = await queryDb<{ by_email: string }>(
            "select by_email from tc.events where collection_id = $1 and book_id = (select id from tc.books where collection_id = $1 and name = $2) order by id desc limit 1",
            [aliceScratch.collectionId, bookName],
        );
        expect(lastCheckinEvent).toHaveLength(1);
        expect(lastCheckinEvent[0].by_email).toBe(BOB.email);
    });
});
