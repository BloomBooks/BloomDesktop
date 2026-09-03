import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
    startWatchingPageForSnapshots,
    stopWatchingPageForSnapshots,
    quietMsForTests,
} from "./pageSnapshot";

const posted: Array<{ url: string; body: string }> = [];

// Lets a test hold a POST open, to check that a second one never starts alongside it.
let postHook: (() => Promise<void>) | undefined;

vi.mock("../../utils/bloomApi", () => ({
    postString: (url: string, body: string) => {
        posted.push({ url, body });
        return postHook ? postHook() : Promise.resolve();
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
        contentToReport = "";
        postHook = undefined;
        setUpPage();
    });

    afterEach(() => {
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
            new Promise<void>((resolve) => {
                inFlight++;
                maxInFlight = Math.max(maxInFlight, inFlight);
                releasePost = () => {
                    inFlight--;
                    resolve();
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
});
