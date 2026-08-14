import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
    addRequestPageContentDelay,
    getActiveDelayIdsForTesting,
    kMaxWaitTimeMs,
    removeRequestPageContentDelay,
    whenNoActiveDelays,
    wrapWithRequestPageContentDelay,
} from "./pageContentDelays";

// The gate that keeps a save from reading a page that is still being changed. Everything that
// gathers page content waits on whenNoActiveDelays(), so if this is wrong, half-finished work
// (an image still being sized, a paste still in progress) gets written into the user's book.

// Has the promise settled? Attaches a callback and then lets the microtask queue drain, which is
// enough for a promise that is already resolved (or resolves synchronously from a call we just
// made) and not enough for one still waiting on a timer.
const isResolved = async (p: Promise<unknown>): Promise<boolean> => {
    let resolved = false;
    void p.then(() => {
        resolved = true;
    });
    for (let i = 0; i < 5; i++) await Promise.resolve();
    return resolved;
};

describe("pageContentDelays", () => {
    beforeEach(() => {
        vi.useFakeTimers();
        // Sanity check: nothing left over from another test, or the assertions below are meaningless.
        expect(getActiveDelayIdsForTesting()).toEqual([]);
    });

    afterEach(() => {
        vi.useRealTimers();
        if (getActiveDelayIdsForTesting().length)
            throw new Error(
                "test leaked delays: " +
                    getActiveDelayIdsForTesting().join(", "),
            );
    });

    it("resolves immediately when nothing is registered", async () => {
        expect(await isResolved(whenNoActiveDelays())).toBe(true);
    });

    it("waits while work is registered, and resolves when the last of it finishes", async () => {
        addRequestPageContentDelay("sizingAnImage");
        addRequestPageContentDelay("fittingACanvasElement");
        const gate = whenNoActiveDelays();

        expect(await isResolved(gate)).toBe(false);

        removeRequestPageContentDelay("sizingAnImage");
        expect(await isResolved(gate)).toBe(false); // one still outstanding

        removeRequestPageContentDelay("fittingACanvasElement");
        expect(await isResolved(gate)).toBe(true);
    });

    it("counts repeats of the same id separately", async () => {
        // The same operation can legitimately be in flight twice (two images sizing at once).
        addRequestPageContentDelay("sizingAnImage");
        addRequestPageContentDelay("sizingAnImage");
        const gate = whenNoActiveDelays();

        removeRequestPageContentDelay("sizingAnImage");
        expect(await isResolved(gate)).toBe(false);

        removeRequestPageContentDelay("sizingAnImage");
        expect(await isResolved(gate)).toBe(true);
    });

    it("releases every waiter, not just the first", async () => {
        addRequestPageContentDelay("work");
        const first = whenNoActiveDelays();
        const second = whenNoActiveDelays();

        removeRequestPageContentDelay("work");

        expect(await isResolved(first)).toBe(true);
        expect(await isResolved(second)).toBe(true);
    });

    it("gives up after the maximum wait rather than blocking the save forever", async () => {
        const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
        addRequestPageContentDelay("workThatNeverFinishes");
        const gate = whenNoActiveDelays();

        await vi.advanceTimersByTimeAsync(kMaxWaitTimeMs - 1);
        expect(await isResolved(gate)).toBe(false);

        await vi.advanceTimersByTimeAsync(2);
        expect(await isResolved(gate)).toBe(true);
        expect(warn).toHaveBeenCalled();
        expect(warn.mock.calls[0][0]).toContain("workThatNeverFinishes");

        warn.mockRestore();
        removeRequestPageContentDelay("workThatNeverFinishes"); // tidy up for afterEach
    });

    it("does not fire the timeout warning for a wait that finished normally", async () => {
        const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
        addRequestPageContentDelay("work");
        const gate = whenNoActiveDelays();
        removeRequestPageContentDelay("work");
        await gate;

        // Well past the deadline: the timeout must have been cleared, not merely ignored.
        await vi.advanceTimersByTimeAsync(kMaxWaitTimeMs * 2);
        expect(warn).not.toHaveBeenCalled();
        warn.mockRestore();
    });

    it("wrapWithRequestPageContentDelay holds the gate for the whole operation", async () => {
        let releaseTheWork: (() => void) | undefined;
        const work = new Promise<void>((r) => (releaseTheWork = r));

        const wrapped = wrapWithRequestPageContentDelay(() => work, "theWork");
        const gate = whenNoActiveDelays();
        expect(getActiveDelayIdsForTesting()).toEqual(["theWork"]);
        expect(await isResolved(gate)).toBe(false);

        releaseTheWork!();
        await wrapped;

        expect(await isResolved(gate)).toBe(true);
        expect(getActiveDelayIdsForTesting()).toEqual([]);
    });

    it("wrapWithRequestPageContentDelay releases the gate even when the work throws", async () => {
        await expect(
            wrapWithRequestPageContentDelay(
                () => Promise.reject(new Error("the work failed")),
                "theWork",
            ),
        ).rejects.toThrow("the work failed");

        // The point: a failed operation must not block every save from now on.
        expect(getActiveDelayIdsForTesting()).toEqual([]);
        expect(await isResolved(whenNoActiveDelays())).toBe(true);
    });

    it("complains about, and ignores, a removal of something never registered", () => {
        const error = vi.spyOn(console, "error").mockImplementation(() => {});
        addRequestPageContentDelay("realWork");

        removeRequestPageContentDelay("neverRegistered");

        expect(error).toHaveBeenCalled();
        expect(getActiveDelayIdsForTesting()).toEqual(["realWork"]);
        error.mockRestore();
        removeRequestPageContentDelay("realWork");
    });
});
