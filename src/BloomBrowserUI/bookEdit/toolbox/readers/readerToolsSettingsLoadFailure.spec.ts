import { describe, it, expect, vi } from "vitest";

// A caller that waits forever is indistinguishable, to the user, from a button that does
// nothing -- which is the whole complaint behind BL-16732. bloomApi.get() reports a failed
// request and then calls nobody back, so loadReaderSettingsWithRetry() has to treat an
// outright failure as a failed attempt itself; otherwise the promise every caller of
// beginLoadSynphonySettings() is waiting on never settles. This lives in its own file
// because readerTools.ts keeps its "already loaded" state in module scope, and these tests
// are about what that module does on a cold start.

let settingsRequests = 0;

vi.mock("axios", () => ({
    default: {
        get: () => Promise.resolve({ data: "" }),
        post: () => Promise.resolve({ data: "" }),
        all: (promises: Promise<unknown>[]) => Promise.all(promises),
        spread: (fn: (...args: unknown[]) => unknown) => (args: unknown[]) =>
            fn(...args),
    },
}));

vi.mock("../../../utils/bloomApi", () => ({
    // Fail the reader settings request the way bloomApi.get() does when the server errors
    // and the caller passed an error callback: the success handler is never called.
    get: (
        endpoint: string,
        successCallback?: (result: { data: unknown }) => void,
        errorCallback?: (error: unknown) => void,
    ) => {
        if (endpoint.split("?")[0] === "readers/io/readerToolSettings") {
            settingsRequests++;
            errorCallback?.(new Error("simulated 500 from the server"));
            return;
        }
        successCallback?.({ data: "" });
    },
    getWithConfig: () => undefined,
    post: () => undefined,
    postData: () => undefined,
    postString: () => undefined,
    postBoolean: () => undefined,
    postJson: () => undefined,
}));

import { beginLoadSynphonySettings } from "./readerTools";
import { getTheOneReaderToolsModel } from "./readerToolsModel";

describe("beginLoadSynphonySettings when the settings request fails", () => {
    it("settles instead of leaving its caller waiting forever", async () => {
        vi.useFakeTimers();
        try {
            // sanity check: the mock hasn't been asked for anything yet
            expect(settingsRequests).toBe(0);

            let settled = false;
            const promise = beginLoadSynphonySettings().always(() => {
                settled = true;
            });

            // The retries are on 150ms timers; run them all out.
            await vi.runAllTimersAsync();
            await promise;

            expect(settled).toBe(true);
            // sanity check: it really did retry rather than giving up on the first failure
            expect(settingsRequests).toBeGreaterThan(1);
            // Nothing to load, so the model is left without settings -- callers that need
            // them report that clearly rather than hanging (see readerSetupDialog).
            expect(getTheOneReaderToolsModel().synphony).toBeUndefined();
        } finally {
            vi.useRealTimers();
        }
    });
});
