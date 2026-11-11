import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// A reader tool restoring a book's stage or level can wait a second or more on a busy machine
// (for the reader settings and, in a book with nothing saved, for Bloom to say what the default
// is), and then applies and saves what it found. A stage or level chosen with the arrows during
// that wait used to be undone by it: Bloom answered in 1.2 seconds, the user had moved to level 2
// by then, and the answer put the tool back on level 1 and saved 1 in the book (BL-17099). So the
// arrows are disabled while a restore runs. These check that they are disabled for the whole
// restore and come back however it ends.

// Requests for the default stage or level are held here until the test answers them.
const requestsForDefault = vi.hoisted(
    () =>
        [] as {
            url: string;
            answer: (defaultPhase: number) => void;
            fail: () => void;
        }[],
);

vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    get: vi.fn(
        (
            url: string,
            success: (result: { data: string }) => void,
            failure: () => void,
        ) => {
            requestsForDefault.push({
                url,
                answer: (defaultPhase) =>
                    success({ data: String(defaultPhase) }),
                fail: failure,
            });
        },
    ),
    // setStageNumber() waits for Bloom to confirm the new default.
    post: vi.fn((_url: string, success?: () => void) => success?.()),
    postString: vi.fn(),
}));

// The settings are put in the model directly, below.
vi.mock("./readerTools", () => ({
    beginInitializeLeveledReaderTool: vi.fn(() => Promise.resolve()),
    beginInitializeDecodableReaderTool: vi.fn(() => Promise.resolve()),
}));

import { beginInitializeLeveledReaderTool } from "./readerTools";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { LeveledReaderTool } from "./leveledReader/leveledReaderTool";
import { DecodableReaderTool } from "./decodableReader/decodableReaderTool";

/** Settings with four stages and four levels. */
function makeSynphonyWithFourOfEach(): ReadersSynphonyWrapper {
    const settings: any = {
        letters: "a b c d e f g h i j k l m n o p q r s t u v w x y z",
        moreWords: "cat sat bob fig",
        stages: [1, 2, 3, 4].map(() => ({
            letters: "a c t",
            sightWords: "the",
        })),
        levels: [1, 2, 3, 4].map(() => ({
            maxWordsPerSentence: 5,
            maxWordsPerPage: 10,
            maxWordsPerBook: 100,
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""],
        })),
    };
    const synphony = new ReadersSynphonyWrapper();
    synphony.loadSettings(settings);
    return synphony;
}

/**
 * Start `tool` restoring a book that has nothing saved, and return the restore's promise and
 * its request for the default, which is still waiting for an answer.
 */
async function beginRestoreOfBookWithNothingSaved(
    tool: LeveledReaderTool | DecodableReaderTool,
    expectedUrl: string,
) {
    const restore = tool.beginRestoreSettings({});
    // Let the restore run as far as its request.
    await vi.advanceTimersByTimeAsync(0);
    expect(requestsForDefault.map((r) => r.url)).toEqual([expectedUrl]);
    return { restore, request: requestsForDefault[0] };
}

describe("the reader tools' arrows while a restore runs", () => {
    beforeEach(() => {
        // setStageNumber() finishes its work half a second later, on a timer.
        vi.useFakeTimers();
        requestsForDefault.length = 0;
        const model = getTheOneReaderToolsModel();
        model.clearForTest();
        model.setSynphony(makeSynphonyWithFourOfEach());
    });

    afterEach(async () => {
        await vi.runAllTimersAsync();
        vi.useRealTimers();
    });

    it("are disabled while the Leveled Reader waits for the default level, then enabled", async () => {
        const model = getTheOneReaderToolsModel();
        // sanity check: nothing is restoring before the restore starts
        expect(model.isRestoringPhase()).toBe(false);

        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new LeveledReaderTool(),
            "readers/io/defaultLevel",
        );
        expect(model.isRestoringPhase()).toBe(true);

        request.answer(3);
        await restore;

        expect(model.levelNumber).toBe(3);
        expect(model.isRestoringPhase()).toBe(false);
    });

    it("are disabled while the Decodable Reader waits for the default stage and saves it, then enabled", async () => {
        const model = getTheOneReaderToolsModel();
        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new DecodableReaderTool(),
            "readers/io/defaultStage",
        );
        expect(model.isRestoringPhase()).toBe(true);

        request.answer(3);
        // setStageNumber() is still waiting for its half-second timer.
        await vi.advanceTimersByTimeAsync(0);
        expect(model.isRestoringPhase()).toBe(true);
        await vi.runAllTimersAsync();
        await restore;

        expect(model.stageNumber).toBe(3);
        expect(model.isRestoringPhase()).toBe(false);
    });

    it("are disabled while a book's saved level waits for the reader settings", async () => {
        const model = getTheOneReaderToolsModel();
        let settingsArrive: () => void = () => {
            throw new Error("the restore never asked for the reader settings");
        };
        vi.mocked(beginInitializeLeveledReaderTool).mockImplementationOnce(
            () => new Promise<void>((resolve) => (settingsArrive = resolve)),
        );

        const restore = new LeveledReaderTool().beginRestoreSettings({
            leveledReaderState: "2",
        });
        await vi.advanceTimersByTimeAsync(0);
        expect(model.isRestoringPhase()).toBe(true);
        expect(model.levelNumber).toBe(1);

        settingsArrive();
        await restore;

        expect(model.levelNumber).toBe(2);
        expect(model.isRestoringPhase()).toBe(false);
    });

    it("are enabled again when Bloom cannot say what the default is", async () => {
        const model = getTheOneReaderToolsModel();
        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new LeveledReaderTool(),
            "readers/io/defaultLevel",
        );
        expect(model.isRestoringPhase()).toBe(true);

        request.fail();
        await restore;

        expect(model.isRestoringPhase()).toBe(false);
    });

    it("are enabled again when the restore throws", async () => {
        const model = getTheOneReaderToolsModel();
        vi.mocked(beginInitializeLeveledReaderTool).mockImplementationOnce(() =>
            Promise.reject(new Error("no reader settings")),
        );

        await expect(
            new LeveledReaderTool().beginRestoreSettings({
                leveledReaderState: "2",
            }),
        ).rejects.toThrow("no reader settings");

        expect(model.isRestoringPhase()).toBe(false);
    });

    it("stay disabled until the last of two overlapping restores finishes", async () => {
        const model = getTheOneReaderToolsModel();
        // A new page starts a second restore while the first still waits for the default.
        const first = await beginRestoreOfBookWithNothingSaved(
            new LeveledReaderTool(),
            "readers/io/defaultLevel",
        );
        const secondRestore = new LeveledReaderTool().beginRestoreSettings({});
        await vi.advanceTimersByTimeAsync(0);
        expect(requestsForDefault).toHaveLength(2);

        first.request.answer(2);
        await first.restore;
        expect(model.isRestoringPhase()).toBe(true);

        requestsForDefault[1].answer(2);
        await secondRestore;
        expect(model.isRestoringPhase()).toBe(false);
    });
});
