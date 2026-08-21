import { describe, it, expect, beforeEach, vi } from "vitest";

// beginLoadSynphonySettings() remembers, in module state, that it has already loaded the
// collection's reader settings. But the ReaderToolsModel those settings live in is held on
// the top window and gets *replaced* whenever the frame that created it is reloaded, so the
// "already loaded" memory can outlive the data. When that happened, this function used to
// resolve without loading anything, leaving synphony undefined for the rest of the session --
// which is why "Set Up Levels" did nothing until Bloom was restarted. (BL-16732)

// Loading the settings starts a watcher on the Sample Texts folder, which really polls the
// server; there is nothing for it to talk to here, so stub it out.
vi.mock("./directoryWatcher", () => ({
    DirectoryWatcher: class {
        public onChanged(): void {
            // nothing to watch in this test
        }
        public start(): void {
            // nothing to watch in this test
        }
        public stop(): void {
            // nothing to watch in this test
        }
    },
}));

// The reload path fetches the sample-texts list through axios directly rather than bloomApi.
vi.mock("axios", () => ({
    default: {
        get: () => Promise.resolve({ data: "" }),
        post: () => Promise.resolve({ data: "" }),
        all: (promises: Promise<unknown>[]) => Promise.all(promises),
        spread: (fn: (...args: unknown[]) => unknown) => (args: unknown[]) =>
            fn(...args),
    },
}));

vi.mock("../../../utils/bloomApi", () => {
    // Defined in here rather than at file scope because vi.mock() is hoisted above it.
    const answers: { [endpoint: string]: unknown } = {
        "readers/io/readerToolSettings": {
            letters: "a b c",
            moreWords: "cat sat",
            stages: [{ letters: "a c", sightWords: "feline" }],
            levels: [
                { maxWordsPerPage: 6, thingsToRemember: [""] },
                { maxWordsPerPage: 10, thingsToRemember: [""] },
            ],
        },
        "collection/defaultFont": "Andika",
        "readers/ui/sampleTextsList": "",
    };
    return {
        get: (
            endpoint: string,
            handler?: (result: { data: unknown }) => void,
        ) => {
            const key = endpoint.split("?")[0];
            if (handler && key in answers) {
                handler({ data: answers[key] });
            }
        },
        getWithConfig: () => undefined,
        post: () => undefined,
        postData: () => undefined,
        postString: () => undefined,
        postBoolean: () => undefined,
        postJson: () => undefined,
    };
});

import { beginLoadSynphonySettings } from "./readerTools";
import { getTheOneReaderToolsModel } from "./readerToolsModel";

describe("beginLoadSynphonySettings", () => {
    beforeEach(() => {
        getTheOneReaderToolsModel().clearForTest();
    });

    it("loads the settings again when the model it loaded them into has been replaced", async () => {
        // sanity check: nothing loaded yet
        expect(getTheOneReaderToolsModel().synphony).toBeUndefined();

        await beginLoadSynphonySettings();

        // sanity check: the first load is what sets up the "already loaded" module state
        // that the second call has to see past.
        expect(getTheOneReaderToolsModel().synphony).toBeDefined();
        expect(getTheOneReaderToolsModel().getNumberOfLevels()).toBe(2);

        // Stand in for the model having been thrown away and remade: the model in front of
        // us no longer has the settings, but this module still thinks it loaded them.
        getTheOneReaderToolsModel().synphony = undefined;

        await beginLoadSynphonySettings();

        expect(getTheOneReaderToolsModel().synphony).toBeDefined();
        expect(getTheOneReaderToolsModel().getNumberOfLevels()).toBe(2);
    });
});
