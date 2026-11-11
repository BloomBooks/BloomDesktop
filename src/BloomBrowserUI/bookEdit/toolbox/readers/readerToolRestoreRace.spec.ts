import { describe, it, expect, vi, beforeEach } from "vitest";

// A reader tool shows its stepper before it has finished restoring the book's stage or level.
// For a book with nothing saved, the restore asks the server for the default, and the user can
// move the stepper while it waits. These show that the answer, when it comes, does not put the
// stepper back over the user's choice. On the 2026-11-11 nightly it did: the server took over a
// second to answer, the late answer saved level 1 over the user's 2, and the e2e test "each book
// remembers its own level" (Test Case ID 442) found the book back on level 1.

// Holds each get() until the test answers it, the way a slow server does.
const pendingGets: { url: string; answer: (data: string) => void }[] = [];
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    get: vi.fn(
        (url: string, success: (result: { data: string }) => void) => {
            pendingGets.push({ url, answer: (data) => success({ data }) });
        },
    ),
    // setStageNumber() waits for its post of the default stage to succeed.
    post: vi.fn((_url: string, success?: () => void) => success?.()),
    postString: vi.fn(),
}));

// The Synphony settings are already loaded (see beforeEach), so the tools' own init has
// nothing to wait for.
vi.mock("./readerTools", () => ({
    beginInitializeLeveledReaderTool: vi.fn(() => Promise.resolve()),
    beginInitializeDecodableReaderTool: vi.fn(() => Promise.resolve()),
}));

import { post } from "../../../utils/bloomApi";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { LeveledReaderTool } from "./leveledReader/leveledReaderTool";
import { DecodableReaderTool } from "./decodableReader/decodableReaderTool";

/** Settings with four stages and four levels, so stage/level 2 and 3 are real choices. */
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

/** Let the restore run up to its request for the default, and return that request. */
async function waitForTheDefaultRequest(
    url: string,
): Promise<(data: string) => void> {
    await vi.waitFor(() => expect(pendingGets.length).toBe(1));
    expect(pendingGets[0].url).toBe(url);
    return pendingGets[0].answer;
}

/** The levels the model has posted as the default for new books, in order. */
function postedDefaultLevels(): string[] {
    return vi
        .mocked(post)
        .mock.calls.map((call) => call[0])
        .filter((url) => url.startsWith("readers/io/defaultLevel"));
}

describe("a reader tool's restore answering after the user has moved the stepper", () => {
    beforeEach(() => {
        pendingGets.length = 0;
        vi.mocked(post).mockClear();
        const model = getTheOneReaderToolsModel();
        model.clearForTest();
        model.setSynphony(makeSynphonyWithFourOfEach());
    });

    it("leaves the level the user chose", async () => {
        const model = getTheOneReaderToolsModel();
        expect(model.levelNumber).toBe(1);

        const restored = new LeveledReaderTool().beginRestoreSettings({});
        const answer = await waitForTheDefaultRequest(
            "readers/io/defaultLevel",
        );
        model.incrementLevel();
        // sanity check: the user's choice took, and was saved as the default
        expect(model.levelNumber).toBe(2);
        expect(postedDefaultLevels()).toEqual([
            "readers/io/defaultLevel?level=2",
        ]);

        answer("1");
        await restored;

        expect(model.levelNumber).toBe(2);
        expect(postedDefaultLevels()).toEqual([
            "readers/io/defaultLevel?level=2",
        ]);
    });

    it("still applies the default level when the user has not moved the stepper", async () => {
        // The control for the test above: the restore does apply what the server answers.
        const model = getTheOneReaderToolsModel();
        expect(model.levelNumber).toBe(1);

        const restored = new LeveledReaderTool().beginRestoreSettings({});
        const answer = await waitForTheDefaultRequest(
            "readers/io/defaultLevel",
        );
        answer("3");
        await restored;

        expect(model.levelNumber).toBe(3);
    });

    it("leaves the stage the user chose", async () => {
        const model = getTheOneReaderToolsModel();
        expect(model.stageNumber).toBe(1);

        const restored = new DecodableReaderTool().beginRestoreSettings({});
        const answer = await waitForTheDefaultRequest(
            "readers/io/defaultStage",
        );
        model.incrementStage();
        expect(model.stageNumber).toBe(2);

        // A default of 3 rather than 1: the tool already skips a default equal to the stage
        // it is showing, so this answer is one it would otherwise apply.
        answer("3");
        await restored;

        expect(model.stageNumber).toBe(2);
    });

    it("still applies the default stage when the user has not moved the stepper", async () => {
        const model = getTheOneReaderToolsModel();
        expect(model.stageNumber).toBe(1);

        const restored = new DecodableReaderTool().beginRestoreSettings({});
        const answer = await waitForTheDefaultRequest(
            "readers/io/defaultStage",
        );
        answer("3");
        await restored;

        expect(model.stageNumber).toBe(3);
    });
});
