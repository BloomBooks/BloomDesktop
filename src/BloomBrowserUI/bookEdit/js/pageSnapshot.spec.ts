import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
    notePageContentMayHaveChanged,
    setSnapshotsSuspended,
    startWatchingPageForSnapshots,
    stopWatchingPageForSnapshots,
    quietMsForTests,
    retryMsForTests,
    getPageLoadId,
} from "./pageSnapshot";

const posted: Array<{ url: string; body: string }> = [];

// Lets a test hold a POST open, to check that a second one never starts alongside it.
let postHook: (() => Promise<unknown>) | undefined;

// What C# answers. A real post resolves to the axios response, and this endpoint answers with a
// boolean, so `{ data: true }` is an ordinary success. `{ data: false }` is a refusal: the snapshot
// was for a page load it is not showing. And `undefined` -- no response at all -- is what a FAILED
// post looks like, because postStringQuietly goes through wrapAxios, which turns a rejected request into a
// resolved promise carrying nothing.
let postReply: unknown = { data: true };

const reported: string[] = [];
vi.mock("../../lib/errorHandler", () => ({
    reportError: (message: string) => reported.push(message),
}));

vi.mock("../../utils/bloomApi", () => ({
    postStringQuietly: (url: string, body: string) => {
        posted.push({ url, body });
        return postHook ? postHook() : Promise.resolve(postReply);
    },
}));

// The page as the gather would report it. Tests change this to simulate the user editing.
let contentToReport = "";
const gather = () => Promise.resolve(contentToReport);

function setUpPage(pageId = "page-1") {
    document.body.innerHTML = `<div class="bloom-page" id="${pageId}"><p>hello</p></div>`;
}

function changeThePage(text: string) {
    document.querySelector(".bloom-page p")!.textContent = text;
}

// startWatching... reads the page once to learn what "unchanged" looks like after loading has
// finished. Nothing is posted until that has resolved.
async function letTheBaselineSettle() {
    await vi.runAllTicks();
    await Promise.resolve();
}

// Walks the slower timer used to offer content again when C# did not take it.
async function letTheRetryHappen() {
    vi.advanceTimersByTime(retryMsForTests);
    await vi.runAllTicks();
    await Promise.resolve(); // the gather's await
    await Promise.resolve(); // the post's await
}

// A MutationObserver delivers its callback in a microtask, and the module then waits kQuietMs.
// This walks both forward.
async function letTheSnapshotHappen() {
    await Promise.resolve(); // let the observer fire
    vi.advanceTimersByTime(quietMsForTests);
    await vi.runAllTicks();
    await Promise.resolve(); // the gather's await
    await Promise.resolve(); // the post's await
}

describe("pageSnapshot", () => {
    beforeEach(() => {
        vi.useFakeTimers();
        posted.length = 0;
        reported.length = 0;
        contentToReport = "";
        postHook = undefined;
        postReply = { data: true };
        setUpPage();
    });

    afterEach(() => {
        setSnapshotsSuspended(undefined);
        stopWatchingPageForSnapshots();
        vi.useRealTimers();
        document.body.innerHTML = "";
    });

    it("posts nothing for a page the user never changes", async () => {
        contentToReport = "the untouched page";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        vi.advanceTimersByTime(quietMsForTests * 5);
        await vi.runAllTicks();

        expect(
            posted.length,
            "a page nobody edited must produce no snapshot, so that C# can tell 'nothing to save' from 'not asked yet'",
        ).toBe(0);
    });

    it("does not treat the page finishing loading as an edit", async () => {
        // Loading is not over when we start watching: image sizing and canvas-element layout
        // complete afterwards and mutate the page. The observer cannot tell those from the user,
        // so the baseline has to. Without it the real app posted a snapshot for every page opened,
        // which would have made "no snapshot" meaningless on the C# side.
        contentToReport = "the settled page";
        startWatchingPageForSnapshots(gather);

        changeThePage("a late load-time fix-up");
        await letTheBaselineSettle();
        changeThePage("and another");
        await letTheSnapshotHappen();

        expect(
            posted.length,
            "mutations that do not change the page's saved form are not edits",
        ).toBe(0);
    });

    it("posts the content, with the page id, once the page has been changed and settles", async () => {
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();
        contentToReport = "edited content";
        changeThePage("goodbye");
        await letTheSnapshotHappen();

        expect(posted.length).toBe(1);
        expect(posted[0].body).toBe("edited content");
        expect(posted[0].url).toContain("editView/pageSnapshot");
        expect(posted[0].url).toContain("pageId=page-1");
    });

    it("does not post again when the content has not actually changed", async () => {
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();
        contentToReport = "same every time";
        changeThePage("a");
        await letTheSnapshotHappen();
        expect(posted.length, "sanity: the first change posts").toBe(1);

        // Tools constantly add and remove editing decorations, which the gather strips. Those
        // mutations must not produce a stream of identical posts.
        changeThePage("b");
        await letTheSnapshotHappen();

        expect(posted.length).toBe(1);
    });

    it("stops posting once the page is unloaded", async () => {
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();
        contentToReport = "first";
        changeThePage("a");
        await letTheSnapshotHappen();
        expect(posted.length, "sanity: it was posting before we stopped").toBe(
            1,
        );

        stopWatchingPageForSnapshots();
        contentToReport = "second";
        changeThePage("b");
        await letTheSnapshotHappen();

        expect(posted.length).toBe(1);
    });

    it("waits for the page to be quiet rather than posting per change", async () => {
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();
        contentToReport = "typed a word";

        // Three changes in quick succession, as typing produces.
        changeThePage("a");
        await Promise.resolve();
        vi.advanceTimersByTime(quietMsForTests / 4);
        changeThePage("ab");
        await Promise.resolve();
        vi.advanceTimersByTime(quietMsForTests / 4);
        changeThePage("abc");
        await letTheSnapshotHappen();

        expect(
            posted.length,
            "the debounce should collapse a burst of changes into one snapshot",
        ).toBe(1);
        expect(posted[0].body).toBe("typed a word");
    });

    it("never has two posts in flight at once", async () => {
        // HTTP does not promise that two outstanding POSTs arrive in the order they were sent, so
        // an older snapshot could land after a newer one and C# would keep the older content. That
        // needs a machine slow enough for a post to still be in flight when the next keystroke's
        // snapshot comes round -- so it must be enforced, not left to timing.
        let inFlight = 0;
        let maxInFlight = 0;
        let releasePost: () => void = () => {};
        postHook = () =>
            new Promise<unknown>((resolve) => {
                inFlight++;
                maxInFlight = Math.max(maxInFlight, inFlight);
                releasePost = () => {
                    inFlight--;
                    resolve({ data: true }); // an ordinary successful post
                };
            });

        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        contentToReport = "first";
        changeThePage("a");
        await Promise.resolve();
        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        await Promise.resolve();
        expect(inFlight, "sanity: a post is outstanding").toBe(1);

        // More edits arrive while that post is still outstanding.
        contentToReport = "second";
        changeThePage("b");
        await Promise.resolve();
        vi.advanceTimersByTime(quietMsForTests * 3);
        await vi.runAllTicks();
        await Promise.resolve();

        expect(
            maxInFlight,
            "a second post must not start while one is outstanding",
        ).toBe(1);

        // Once it completes, the newer content still gets sent.
        releasePost();
        await vi.runAllTicks();
        await Promise.resolve();
        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        await Promise.resolve();
        releasePost();
        await vi.runAllTicks();
        await Promise.resolve();

        expect(
            posted.map((p) => p.body),
            "the later edit must still reach C#, just after the first post finished",
        ).toEqual(["first", "second"]);
    });

    it("takes another snapshot when the page changes while one is being gathered", async () => {
        let release: (value: string) => void = () => {};
        let gatherCount = 0;
        const slowGather = () => {
            gatherCount++;
            return new Promise<string>((resolve) => {
                release = resolve;
            });
        };
        startWatchingPageForSnapshots(slowGather);

        // The first gather is the baseline; let it finish.
        expect(gatherCount, "sanity: the baseline gather started").toBe(1);
        release("baseline");
        await letTheBaselineSettle();

        changeThePage("a");
        await Promise.resolve();
        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        expect(gatherCount, "sanity: a snapshot gather started").toBe(2);

        // While that gather is outstanding, the user types again. That change is not in what the
        // gather is about to hand us, so it must not be silently dropped.
        changeThePage("b");
        await Promise.resolve();

        release("first content");
        await vi.runAllTicks();
        await Promise.resolve();
        await Promise.resolve();
        expect(posted.length, "sanity: the first gather posted").toBe(1);

        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        expect(
            gatherCount,
            "the change that landed mid-gather must trigger another snapshot, not be dropped",
        ).toBe(3);
    });
    it("does not treat a failed post as sent, so the content is offered again", async () => {
        // Recording it as sent before the post resolved would mean C# never got this content and
        // we never tried again -- the next save would then write what C# still held, losing
        // everything typed since.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        postHook = () => Promise.reject(new Error("network gone"));
        contentToReport = "second";
        changeThePage("second");
        await letTheSnapshotHappen();
        expect(posted.map((p) => p.body)).toEqual(["second"]);

        // The post failed, so the same content must still be offered on the next attempt.
        postHook = undefined;
        changeThePage("second again");
        await letTheSnapshotHappen();
        expect(posted.map((p) => p.body)).toEqual(["second", "second"]);
    });

    it("reports a gather that throws, once per page, instead of losing the edits silently", async () => {
        // If the gather throws and nobody says so, C# concludes there is nothing to save and the
        // user's work disappears without a word.
        contentToReport = "fine";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        const exploding = () => Promise.reject(new Error("no marginBox"));
        stopWatchingPageForSnapshots();
        startWatchingPageForSnapshots(exploding);
        await letTheBaselineSettle();

        changeThePage("one");
        await letTheSnapshotHappen();
        changeThePage("two");
        await letTheSnapshotHappen();

        expect(reported.length).toBe(1);
        expect(reported[0]).toContain("could not keep track of your changes");
        expect(posted.length).toBe(0);
    });
    it("stamps every post with the id of this page load", async () => {
        // C# refuses a snapshot that does not carry the load it is currently showing, so that a
        // post overtaking a reload of the same page cannot be merged over what the reload built.
        contentToReport = "before";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        contentToReport = "after";
        changeThePage("after");
        await letTheSnapshotHappen();

        expect(posted.length).toBe(1);
        expect(posted[0].url).toContain(
            "loadId=" + encodeURIComponent(getPageLoadId()),
        );
        expect(getPageLoadId()).not.toBe("");
    });

    it("posts when told the content changed in a way it cannot observe", async () => {
        // The user's style definitions are gathered too, but they are changed through the CSSOM --
        // setProperty, deleteRule, insertRule -- which mutates no DOM node, so a MutationObserver
        // cannot see it. Changing a style's size or colour without touching the text would
        // otherwise produce no snapshot at all, and leaving the tab would write the old styles.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();
        expect(posted.length, "sanity: nothing posted yet").toBe(0);

        // The style editor changed a rule. Nothing in the page changed.
        contentToReport = "first, but with bigger type";
        notePageContentMayHaveChanged();
        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        await Promise.resolve();
        await Promise.resolve();

        expect(posted.map((p) => p.body)).toEqual([
            "first, but with bigger type",
        ]);
    });

    it("posts nothing when told of a change that turns out not to be one", async () => {
        // Callers are told to err towards saying so, which is only safe because an unchanged page
        // costs nothing.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        notePageContentMayHaveChanged();
        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        await Promise.resolve();
        await Promise.resolve();

        expect(posted.length).toBe(0);
    });

    it("does not read the page at all while snapshots are suspended", async () => {
        // Reading the page is not free in every state: the game tool's part of it reaches into
        // bloom-player's record of the LIVE page, which is why the page frame stops watching while
        // a game is in its Play tab. A game page can open straight into that tab, because the tab
        // is remembered per page, so even the once-per-page baseline read has to wait.
        let gathers = 0;
        const countingGather = () => {
            gathers++;
            return Promise.resolve(contentToReport);
        };

        setSnapshotsSuspended("a test");
        contentToReport = "the game, in play mode";
        startWatchingPageForSnapshots(countingGather);
        await letTheBaselineSettle();
        changeThePage("the user drags something");
        await letTheSnapshotHappen();

        expect(gathers, "the page must not be read while suspended").toBe(0);
        expect(posted.length).toBe(0);

        // On resuming we do NOT go and take a late baseline. A baseline means "what the page was
        // before the user touched it", and this moment is not that: anything that changed while we
        // were suspended would be folded in and thereby counted as already delivered. So we offer
        // the page instead, whatever it now says.
        contentToReport = "the game, back in start mode";
        setSnapshotsSuspended(undefined);
        await letTheSnapshotHappen();
        expect(
            posted.map((p) => p.body),
            "resuming offers the page rather than quietly adopting it",
        ).toEqual(["the game, back in start mode"]);

        // And from then on it behaves normally.
        contentToReport = "the user typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        expect(posted.map((p) => p.body)).toEqual([
            "the game, back in start mode",
            "the user typed",
        ]);
    });

    it("does not fold an edit made while suspended into the baseline", async () => {
        // The trap this avoids: adopting the page on resuming would make whatever was typed while
        // we were not watching the new definition of "unchanged", so it would never be sent, and
        // the next save would write the page as it was before.
        contentToReport = "before";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        setSnapshotsSuspended("a test");
        contentToReport =
            "before, plus something typed while we were not watching";
        changeThePage("typed while suspended");
        await letTheSnapshotHappen();
        expect(posted.length, "nothing posted while suspended").toBe(0);

        setSnapshotsSuspended(undefined);
        await letTheSnapshotHappen();
        expect(
            posted.map((p) => p.body),
            "the edit made while suspended must still reach C#",
        ).toEqual(["before, plus something typed while we were not watching"]);
    });

    it("offers the content again when C# refuses the snapshot", async () => {
        // C# refuses anything from a page load it is not showing. Because the snapshot endpoint is
        // not ordered against the "page is ready" one, a snapshot can genuinely arrive first and be
        // refused; counting it as delivered would leave C# with nothing to save.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        postReply = { data: false }; // refused
        contentToReport = "typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        expect(posted.map((p) => p.body)).toEqual(["typed"]);

        // Refused, so the very same content must be offered again rather than treated as sent.
        // Nothing the user did caused the refusal, so the retry is on the slower timer.
        postReply = { data: true };
        await letTheRetryHappen();
        expect(posted.map((p) => p.body)).toEqual(["typed", "typed"]);
    });

    it("does not treat a post that failed outright as sent", async () => {
        // The realistic shape of a failed post, and the one that nearly slipped through: the post
        // goes through wrapAxios, which swallows the rejection and resolves with NOTHING. So a
        // failed post is indistinguishable from a successful one except that no response comes
        // back -- and reading only `.data` took that for an acceptance. The content was then
        // recorded as sent and never offered again, and the next save wrote what C# still held.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        postReply = undefined; // the post failed; wrapAxios gives us nothing
        contentToReport = "typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        expect(posted.map((p) => p.body)).toEqual(["typed"]);

        postReply = { data: true };
        await letTheRetryHappen();
        expect(
            posted.map((p) => p.body),
            "content C# never received must be offered again, not counted as sent",
        ).toEqual(["typed", "typed"]);
    });

    it("keeps offering a failing post, backing off, and tells the user only once", async () => {
        // Two things have to be true at the same time here, and they pull against each other.
        //
        // We must not stop retrying: while C# has not got this content, quitting writes what it
        // still holds, so a server that comes back must be given the content even if the user
        // never types again. But we must also not report the failure on every attempt, or an
        // outage puts an error in front of the user again and again. Hence a quiet post and one
        // report per page.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheBaselineSettle();

        postReply = undefined; // every post fails
        contentToReport = "typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        expect(posted.length, "sanity: the first attempt happened").toBe(1);

        // A minute of outage, walked in 10s steps.
        for (let i = 0; i < 6; i++) {
            vi.advanceTimersByTime(10000);
            await vi.runAllTicks();
            await Promise.resolve();
            await Promise.resolve();
        }
        expect(
            posted.length,
            "it must keep offering rather than give up",
        ).toBeGreaterThan(1);
        expect(
            posted.length,
            "backing off: a minute of outage must not mean a minute of attempts",
        ).toBeLessThan(12);
        expect(
            reported.length,
            "the user must be told once, not once per attempt",
        ).toBe(1);

        // When the server comes back, the content gets there with no further typing.
        postReply = { data: true };
        const attemptsWhileDown = posted.length;
        vi.advanceTimersByTime(60000);
        await vi.runAllTicks();
        await Promise.resolve();
        await Promise.resolve();
        expect(posted.length).toBe(attemptsWhileDown + 1);
        expect(posted[posted.length - 1].body).toBe("typed");
    });
});
