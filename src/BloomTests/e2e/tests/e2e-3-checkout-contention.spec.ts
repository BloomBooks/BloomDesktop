// E2E-3: checkout contention (exactly one winner; loser sees holder).
//
// CloudTeamCollection.TryLockInRepo (src/BloomExe/TeamCollection/Cloud/CloudTeamCollection.cs)
// dispatches to a single conditional-UPDATE RPC (`checkout_book`, CONTRACTS.md: "race-free") --
// the server, not either client, decides who wins a simultaneous attempt. This test proves that
// server-side race-freedom for real: Alice and Bob both select the same book, then both POST
// `teamCollection/attemptLockOfCurrentBook` via `Promise.all` (no synchronization between them
// beyond firing both requests at once), rather than one after the other.
//
// Book selection uses `selectBookByName` (direct `external/select-book` API), not a CDP click, for
// BOB specifically: a fresh `connectOverCdp` against his instance (joined via pullDown + kill +
// relaunch, see twoInstanceSetup.ts) reliably found only a stuck `about:blank` CDP target even
// though his HTTP API was already responding -- see harness/selectBook.ts's header comment for
// the full investigation, a new finding on top of README.md's existing CDP-reachability notes.
// Alice still uses her real, already-open, held-since-launch CDP page for the click (proven
// reliable by E2E-2), so this test also cross-checks that both selection paths agree.
// Checkout/lock itself goes through the direct API for the same reason E2E-2 does (CDP clicks on
// the checkout button have no observed effect).
import { test, expect } from "@playwright/test";
import { resetStack } from "../harness/reset";
import { setUpAliceAndBobOnSharedCollection } from "../harness/twoInstanceSetup";
import { LaunchedBloom } from "../harness/launch";
import { postApi } from "../harness/bloomApi";
import { bookStatus, pollNowViaReceiveUpdates } from "../harness/bookStatus";
import { selectBookByName } from "../harness/selectBook";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-3";

test.describe("E2E-3 checkout contention", () => {
    let alice: LaunchedBloom | undefined;
    let bob: LaunchedBloom | undefined;

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        await Promise.all(
            [alice, bob]
                .filter((i): i is LaunchedBloom => !!i)
                .map((i) => i.kill()),
        );
        alice = undefined;
        bob = undefined;
    });

    test("simultaneous checkout attempts have exactly one winner, and the loser sees the winner as holder", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-3",
            LOG_DIR,
        );
        alice = shared.alice;
        bob = shared.bob;
        const { aliceScratch, alicePage } = shared;

        // Both instances select the same book -- Alice via her already-attached CDP page (real
        // click, proven reliable in E2E-2), Bob via the direct external/select-book API (see the
        // header comment above for why a fresh CDP connect to his instance isn't reliable here).
        await alicePage
            .getByText(aliceScratch.bookName, { exact: true })
            .first()
            .click();
        await selectBookByName(
            bob.httpPort,
            shared.bobCollectionFilePath.replace(
                /[^\\]+\.bloomCollection$/,
                "",
            ),
            aliceScratch.bookName,
        );

        // The race: fire both lock attempts at once, with no ordering between them beyond
        // Promise.all's simultaneous dispatch. TryLockInRepo's conditional UPDATE on the server
        // is what actually decides the winner -- if the server logic were not race-free, this
        // could non-deterministically report BOTH as successful.
        const [aliceResult, bobResult] = await Promise.all([
            postApi(
                alice.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            ),
            postApi(
                bob.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            ),
        ]);
        expect(aliceResult.status).toBe(200);
        expect(bobResult.status).toBe(200);
        const [aliceWon, bobWon] = await Promise.all([
            aliceResult.json(),
            bobResult.json(),
        ]);

        expect(
            aliceWon !== bobWon,
            `expected exactly one winner, got alice=${aliceWon} bob=${bobWon}`,
        ).toBe(true);

        const winner = aliceWon ? alice : bob;
        const winnerLabel = aliceWon ? "alice" : "bob";
        const loser = aliceWon ? bob : alice;

        // The loser must see the winner holding the book once it refreshes (forced immediately
        // via pollNowViaReceiveUpdates rather than waiting out the 60s poll timer).
        await pollNowViaReceiveUpdates(loser.httpPort);
        await expect
            .poll(
                async () =>
                    (await bookStatus(loser.httpPort, aliceScratch.bookName))
                        .who,
                {
                    timeout: 90_000, // past the organic 60s poll, in case the forced poll raced the commit
                    message: "loser never saw the winner as the lock holder",
                },
            )
            .toBeTruthy();

        const winnerStatus = await bookStatus(
            winner.httpPort,
            aliceScratch.bookName,
        );
        expect(
            winnerStatus.who,
            `winner (${winnerLabel}) does not see itself as the lock holder`,
        ).toBeTruthy();

        // The loser's very first `who` reading above can legitimately be the raw auth user id
        // rather than the resolved email: `attemptLockOfCurrentBook`'s own failed-checkout RPC
        // response write-throughs `locked_by` (raw id) synchronously (CloudRepoCache.
        // RecordCheckoutResult), while the friendlier `locked_by_email` only arrives via the
        // NEXT get_changes poll (HandleReceiveUpdates replies to the HTTP request BEFORE calling
        // PollNow() -- see its own comment -- so `pollNowViaReceiveUpdates` resolving does not
        // guarantee that poll's delta has actually been applied to the cache yet). Poll again
        // (re-issuing receiveUpdates each iteration) until the two sides' identity strings agree.
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(loser.httpPort);
                    return (
                        await bookStatus(loser.httpPort, aliceScratch.bookName)
                    ).who;
                },
                {
                    timeout: 20_000,
                    message: `loser's view of the holder's identity (raw id vs resolved email) never converged with the winner's own ('${winnerStatus.who}')`,
                },
            )
            .toBe(winnerStatus.who);

        // The loser's own attempt must not have actually taken the lock -- re-confirm by trying
        // to lock again from the loser: this call should still fail while the winner holds it.
        const loserRetry = await (
            await postApi(
                loser.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(loserRetry).toBe(false);
    });
});
