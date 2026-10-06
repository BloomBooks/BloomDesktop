import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
    notePageContentMayHaveChanged,
    startWatchingPageForSnapshots,
    stopWatchingPageForSnapshots,
    quietMsForTests,
    sendSnapshotNow,
    getPageLoadId,
} from "./pageSnapshot";
import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
    whenNoActiveDelays,
} from "./pageContentDelays";

const posted: Array<{ url: string; body: string }> = [];

// Lets a test hold a POST open, to check that a second one never starts alongside it.
let postHook: (() => Promise<unknown>) | undefined;

// What C# answers. A real post resolves to the axios response, and this endpoint answers with a
// boolean, so `{ data: true }` is an ordinary success. `{ data: false }` is a refusal: the snapshot
// was for a page load it is not showing. And `undefined` -- no response at all -- is what a FAILED
// post looks like, because postStringQuietly goes through wrapAxios, which turns a rejected request
// into a resolved promise carrying nothing.
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

// startWatching... sends the page as it has loaded, after the usual quiet time. This lets that
// post go out -- releasing the gather first, for a test whose gather the test holds -- and then
// forgets it, so that a test counts only what happens after loading.
async function letTheLoadedPageBeSent(releaseGather?: () => void) {
    vi.advanceTimersByTime(quietMsForTests);
    await vi.runAllTicks();
    releaseGather?.();
    await vi.runAllTicks();
    await Promise.resolve(); // the gather's await
    await Promise.resolve(); // the post's await
    await Promise.resolve();
    posted.length = 0;
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
        stopWatchingPageForSnapshots();
        vi.useRealTimers();
        document.body.innerHTML = "";
    });

    it("posts the page once when it has loaded, and then nothing while nobody changes it", async () => {
        // Loading can itself change the page -- after a change of page size, images and canvas
        // elements are laid out afresh -- and some of that happens before we start watching, where
        // the observer cannot see it. So the page as loaded is always sent; C# decides whether it
        // differs from the book.
        contentToReport = "the untouched page";
        startWatchingPageForSnapshots(gather);
        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        await Promise.resolve();
        await Promise.resolve();
        expect(posted.length, "the loaded page is sent").toBe(1);
        expect(posted[0].body).toBe("the untouched page");

        vi.advanceTimersByTime(quietMsForTests * 5);
        await vi.runAllTicks();
        expect(posted.length, "and nothing more while nobody changes it").toBe(
            1,
        );
    });

    it("sends what loading changed in that one post, not a post per change", async () => {
        // Image sizing and canvas-element layout finish after bootstrap() and mutate the page;
        // those results belong in the book, but they should arrive as the settled page, once.
        startWatchingPageForSnapshots(gather);
        changeThePage("a late load-time fix-up");
        await Promise.resolve();
        changeThePage("and another");
        contentToReport = "the settled page";
        await letTheSnapshotHappen();
        expect(posted.map((p) => p.body)).toEqual(["the settled page"]);

        changeThePage("a mutation that does not change the saved form");
        await letTheSnapshotHappen();
        expect(
            posted.length,
            "mutations that do not change the page's saved form are not sent",
        ).toBe(1);
    });

    it("posts the content, with the page id, once the page has been changed and settles", async () => {
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();
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
        await letTheLoadedPageBeSent();
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
        await letTheLoadedPageBeSent();
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
        await letTheLoadedPageBeSent();
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
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();

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
        await letTheLoadedPageBeSent(() => release("the loaded page"));
        expect(gatherCount, "sanity: the loaded page was gathered").toBe(1);

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

    // A gather that behaves like the real one in the respect these tests are about: it waits for
    // the delay register to empty before it reads the page.
    const gatherWhenIdle = async () => {
        await whenNoActiveDelays();
        return contentToReport;
    };

    // Lets queued posts and the gathers behind them run to completion.
    async function letEverythingSettle() {
        await vi.runAllTicks();
        for (let i = 0; i < 8; i++) await Promise.resolve();
    }

    const kinds = () =>
        posted.map((p) => (p.url.includes("busy=true") ? "busy" : "snapshot"));

    it("tells C# when work begins, and sends the page once the work is done", async () => {
        // A save C# makes from the snapshot meanwhile would miss the work, so it waits for the
        // snapshot that follows; that snapshot is also what tells it the page is idle.
        contentToReport = "before the work";
        startWatchingPageForSnapshots(gatherWhenIdle);
        await letTheLoadedPageBeSent();

        addRequestPageContentDelay("sizing an image");
        await letEverythingSettle();
        expect(kinds()).toEqual(["busy"]);
        expect(posted[0].url).toContain("editView/pageSnapshot");
        expect(posted[0].body).toBe("sizing an image");

        // The work changes the page; the snapshot for that waits for the work to finish.
        contentToReport = "after the work";
        changeThePage("after the work");
        await letTheSnapshotHappen();
        expect(kinds(), "no snapshot while the page is busy").toEqual(["busy"]);

        removeRequestPageContentDelay("sizing an image");
        await letEverythingSettle();
        expect(kinds()).toEqual(["busy", "snapshot"]);
        expect(posted[1].body).toBe("after the work");
    });

    it("sends the page after work finishes even if the work changed nothing, because that is what says idle", async () => {
        contentToReport = "unchanged";
        startWatchingPageForSnapshots(gatherWhenIdle);
        await letTheLoadedPageBeSent();

        addRequestPageContentDelay("settling a paste");
        await letEverythingSettle();
        removeRequestPageContentDelay("settling a paste");
        await letEverythingSettle();

        expect(kinds()).toEqual(["busy", "snapshot"]);
        expect(posted[1].body).toBe("unchanged");

        // Having said so once, an unchanged page is not sent again.
        changeThePage("a mutation that does not change the saved form");
        await letTheSnapshotHappen();
        expect(posted.length).toBe(2);
    });

    it("reports work that was already registered when watching began", async () => {
        addRequestPageContentDelay("sizing an image");
        startWatchingPageForSnapshots(gatherWhenIdle);
        await letEverythingSettle();

        expect(kinds()).toEqual(["busy"]);
        expect(posted[0].body).toBe("sizing an image");

        removeRequestPageContentDelay("sizing an image");
        await letTheLoadedPageBeSent();
    });

    it("says busy again after sending a page read just before work began", async () => {
        // The gather has read the page, and before we get to post it, work begins and we tell C#
        // the page is busy. C# takes a snapshot to mean idle, so the snapshot must be followed by
        // another busy notice, or a save would go ahead without the work.
        let release: (value: string) => void = () => {};
        const heldGather = () =>
            new Promise<string>((resolve) => {
                release = resolve;
            });
        startWatchingPageForSnapshots(heldGather);
        await letTheLoadedPageBeSent(() => release("the loaded page"));

        changeThePage("typed");
        await letTheSnapshotHappen();
        addRequestPageContentDelay("sizing an image");
        release("typed, read before the work");
        await letEverythingSettle();
        expect(kinds()).toEqual(["busy", "snapshot", "busy"]);
        expect(posted[1].body).toBe("typed, read before the work");

        removeRequestPageContentDelay("sizing an image");
        release("after the work");
        await letEverythingSettle();
        expect(kinds()).toEqual(["busy", "snapshot", "busy", "snapshot"]);
        expect(posted[3].body).toBe("after the work");
    });

    it("still sends the page when work outlasts the gather's wait, and says busy after it", async () => {
        // Work that never deregisters (a bug elsewhere) must not stop the page from being sent:
        // the gather gives up waiting (kMaxWaitTimeMs) and reads the page anyway.
        contentToReport = "first";
        startWatchingPageForSnapshots(gatherWhenIdle);
        await letTheLoadedPageBeSent();

        addRequestPageContentDelay("work that never finishes");
        await letEverythingSettle();
        contentToReport = "typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        vi.advanceTimersByTime(4000); // the register's cap
        await letEverythingSettle();
        expect(kinds()).toEqual(["busy", "snapshot", "busy"]);
        expect(posted[1].body).toBe("typed");
        removeRequestPageContentDelay("work that never finishes");
        await letEverythingSettle();
    });

    it("sends busy notices and snapshots one at a time, in the order they arose", async () => {
        // They share one unsynchronised endpoint. Two in flight could be processed in either
        // order, and a busy notice cleared by a snapshot from before the work would let a save go
        // ahead in the middle of it.
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();

        let releasePost: () => void = () => {};
        postHook = () =>
            new Promise<unknown>((resolve) => {
                releasePost = () => resolve({ data: true });
            });
        contentToReport = "typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        expect(kinds(), "sanity: the snapshot is in flight").toEqual([
            "snapshot",
        ]);

        addRequestPageContentDelay("sizing an image");
        await letEverythingSettle();
        expect(
            kinds(),
            "the busy notice waits for the snapshot to be answered",
        ).toEqual(["snapshot"]);

        releasePost();
        await letEverythingSettle();
        expect(kinds()).toEqual(["snapshot", "busy"]);

        releasePost();
        removeRequestPageContentDelay("sizing an image");
        await letEverythingSettle();
        releasePost();
        await letEverythingSettle();
        expect(kinds()).toEqual(["snapshot", "busy", "snapshot"]);
    });

    it("reports a post that fails, once per page, and does not retry it", async () => {
        // Posting a string to localhost should never fail. If it does, the user needs to know
        // their changes may not be saved; offering it again on a timer would only hide that.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();

        postReply = undefined; // no response at all: wrapAxios swallowed a failed request
        contentToReport = "typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        await letEverythingSettle();
        expect(posted.length).toBe(1);
        expect(reported.length).toBe(1);
        expect(reported[0]).toContain("could not keep track of your changes");

        vi.advanceTimersByTime(60000);
        await letEverythingSettle();
        expect(posted.length, "no retry").toBe(1);

        // A mutation that leaves the saved form as it was: C# never got that form, so it is
        // still sent, rather than skipped as already delivered.
        postReply = { data: true };
        changeThePage("a mutation that does not change the saved form");
        await letTheSnapshotHappen();
        await letEverythingSettle();
        expect(posted.map((p) => p.body)).toEqual(["typed", "typed"]);

        postReply = undefined;
        contentToReport = "typed more";
        changeThePage("typed more");
        await letTheSnapshotHappen();
        await letEverythingSettle();
        expect(posted.length, "a later change is still sent").toBe(3);
        expect(reported.length, "and the user is told only once").toBe(1);
    });

    it("sends the page at once when asked, and resolves only after C# has answered", async () => {
        // saveChangesAndRethinkPage restructures the page and then asks C# to save and reload it
        // straight away, before the usual quiet time is up.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();

        let releasePost: () => void = () => {};
        postHook = () =>
            new Promise<unknown>((resolve) => {
                releasePost = () => resolve({ data: true });
            });
        contentToReport = "a new origami layout";
        let done = false;
        const sending = sendSnapshotNow().then((ok) => {
            done = ok;
        });
        await letEverythingSettle();
        expect(posted.map((p) => p.body)).toEqual(["a new origami layout"]);
        expect(done, "not until C# has answered").toBe(false);

        releasePost();
        await sending;
        expect(done).toBe(true);
    });

    it("says so when asked to send the page at once and the post fails", async () => {
        // The caller is about to have C# save and reload the page; if C# did not get it, the
        // reload would rebuild the page without the change that prompted it.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();

        postReply = undefined; // no response: the post failed
        contentToReport = "a new origami layout";
        expect(await sendSnapshotNow()).toBe(false);
        expect(reported.length).toBe(1);
    });

    it("says so when asked to send the page at once and it cannot be read", async () => {
        startWatchingPageForSnapshots(() =>
            Promise.reject(new Error("no marginBox")),
        );
        expect(await sendSnapshotNow()).toBe(false);
        expect(reported.length).toBe(1);
        expect(posted.length).toBe(0);
    });

    it("drops a message C# refuses, without reporting it or offering it again", async () => {
        // C# accepts our load before we send anything, so a refusal means it has moved on from
        // this page, and nobody wants the message any more.
        contentToReport = "first";
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();

        postReply = { data: false };
        contentToReport = "typed";
        changeThePage("typed");
        await letTheSnapshotHappen();
        vi.advanceTimersByTime(60000);
        await letEverythingSettle();

        expect(posted.length).toBe(1);
        expect(reported.length).toBe(0);
    });

    it("reports a gather that throws, once per page, instead of losing the edits silently", async () => {
        // If the gather throws and nobody says so, C# concludes there is nothing to save and the
        // user's work disappears without a word.
        contentToReport = "fine";
        startWatchingPageForSnapshots(gather);
        await letTheLoadedPageBeSent();

        const exploding = () => Promise.reject(new Error("no marginBox"));
        stopWatchingPageForSnapshots();
        startWatchingPageForSnapshots(exploding);
        await letTheLoadedPageBeSent();

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
        await letTheLoadedPageBeSent();

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
        await letTheLoadedPageBeSent();
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
        await letTheLoadedPageBeSent();

        notePageContentMayHaveChanged();
        vi.advanceTimersByTime(quietMsForTests);
        await vi.runAllTicks();
        await Promise.resolve();
        await Promise.resolve();

        expect(posted.length).toBe(0);
    });
});
