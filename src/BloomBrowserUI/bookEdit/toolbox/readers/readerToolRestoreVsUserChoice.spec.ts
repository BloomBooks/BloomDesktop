import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// A reader tool restoring a book that has no stage or level saved asks Bloom for the default,
// and the arrows work while it waits for the answer. These show that a stage or level the
// user chooses during that wait is not undone when the answer arrives. (Bloom answered in
// 1.2 seconds on a busy machine, the user had moved to level 2 by then, and the answer put
// the tool back on level 1 and saved 1 in the book: BL-17099.)

// Requests for the default stage or level are held here until the test answers them.
const requestsForDefault = vi.hoisted(
    () =>
        [] as {
            url: string;
            answer: (defaultPhase: number) => void;
        }[],
);

vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    get: vi.fn((url: string, success: (result: { data: string }) => void) => {
        requestsForDefault.push({
            url,
            answer: (defaultPhase) => success({ data: String(defaultPhase) }),
        });
    }),
    // setStageNumber() waits for Bloom to confirm the new default.
    post: vi.fn((_url: string, success?: () => void) => success?.()),
    postString: vi.fn(),
}));

// The settings are put in the model directly, below.
vi.mock("./readerTools", () => ({
    beginInitializeLeveledReaderTool: vi.fn(() => Promise.resolve()),
    beginInitializeDecodableReaderTool: vi.fn(() => Promise.resolve()),
}));

import { post } from "../../../utils/bloomApi";
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

/** The defaults for new books that the model has told Bloom to remember, oldest first. */
function defaultsPostedToBloom(): string[] {
    return vi
        .mocked(post)
        .mock.calls.map((call) => call[0])
        .filter((url) => url.startsWith("readers/io/default"));
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

describe("a reader tool asking Bloom for the default while the user works the arrows", () => {
    beforeEach(() => {
        // setStageNumber() finishes its work half a second later, on a timer.
        vi.useFakeTimers();
        requestsForDefault.length = 0;
        vi.mocked(post).mockClear();
        const model = getTheOneReaderToolsModel();
        model.clearForTest();
        model.setSynphony(makeSynphonyWithFourOfEach());
    });

    afterEach(async () => {
        await vi.runAllTimersAsync();
        vi.useRealTimers();
    });

    it("keeps the level the user chose while the default was on its way", async () => {
        const model = getTheOneReaderToolsModel();
        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new LeveledReaderTool(),
            "readers/io/defaultLevel",
        );
        // sanity check: the tool starts on level 1, so the 2 below is the user's doing
        expect(model.levelNumber).toBe(1);

        model.incrementLevel();
        expect(model.levelNumber).toBe(2);
        // The answer is the default from before the user chose.
        request.answer(1);
        await restore;

        expect(model.levelNumber).toBe(2);
        // The late answer must not become the default for new books either.
        expect(defaultsPostedToBloom()).toEqual([
            "readers/io/defaultLevel?level=2",
        ]);
    });

    it("applies the default level when the user has chosen nothing", async () => {
        const model = getTheOneReaderToolsModel();
        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new LeveledReaderTool(),
            "readers/io/defaultLevel",
        );
        expect(model.levelNumber).toBe(1);

        request.answer(3);
        await restore;

        expect(model.levelNumber).toBe(3);
    });

    it("applies the default level to a new book after the user chose one in the book before", async () => {
        const model = getTheOneReaderToolsModel();
        // The model outlives a change of book, so a choice made in the last book must not
        // stop the next book's restore.
        model.incrementLevel();
        expect(model.levelNumber).toBe(2);

        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new LeveledReaderTool(),
            "readers/io/defaultLevel",
        );
        request.answer(4);
        await restore;

        expect(model.levelNumber).toBe(4);
    });

    it("keeps the stage the user chose while the default was on its way", async () => {
        const model = getTheOneReaderToolsModel();
        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new DecodableReaderTool(),
            "readers/io/defaultStage",
        );
        expect(model.stageNumber).toBe(1);

        model.incrementStage();
        model.incrementStage();
        expect(model.stageNumber).toBe(3);
        request.answer(1);
        await vi.runAllTimersAsync();
        await restore;

        expect(model.stageNumber).toBe(3);
        // The late answer must not become the default for new books either. (Each click
        // posts the stage the model is on when its half-second timer fires.)
        expect(defaultsPostedToBloom()).toEqual([
            "readers/io/defaultStage?stage=3",
            "readers/io/defaultStage?stage=3",
        ]);
    });

    it("applies the default stage when the user has chosen nothing", async () => {
        const model = getTheOneReaderToolsModel();
        const { restore, request } = await beginRestoreOfBookWithNothingSaved(
            new DecodableReaderTool(),
            "readers/io/defaultStage",
        );
        expect(model.stageNumber).toBe(1);

        request.answer(3);
        await vi.runAllTimersAsync();
        await restore;

        expect(model.stageNumber).toBe(3);
    });

    it("still restores a book's saved sort when the user chose a stage during the restore", async () => {
        const model = getTheOneReaderToolsModel();
        // sanity check: the model starts on another sort, so byLength below is the book's
        expect(model.sort).toBe("alphabetic");

        const restore = new DecodableReaderTool().beginRestoreSettings({
            decodableReaderState: "stage:1;sort:byLength",
        });
        model.incrementStage();
        expect(model.stageNumber).toBe(2);
        await vi.runAllTimersAsync();
        await restore;

        expect(model.stageNumber).toBe(2);
        expect(model.sort).toBe("byLength");
    });

    it("keeps the level the user chose while a book's saved level waited for the settings", async () => {
        const model = getTheOneReaderToolsModel();
        // The book has level 1 saved. The restore waits for the reader settings before it
        // applies that, and the user moves to level 2 during the wait.
        const restore = new LeveledReaderTool().beginRestoreSettings({
            leveledReaderState: "1",
        });
        model.incrementLevel();
        expect(model.levelNumber).toBe(2);
        await restore;

        expect(model.levelNumber).toBe(2);
    });
});
