import { describe, it, expect, vi, beforeEach } from "vitest";

// Picks up where readerToolsSettingsLoadFailure.spec.ts leaves off: that one shows a failed
// settings load settling with the model's synphony still undefined; these show what then
// becomes of the stage or level the book had saved.

// The restore path must not reach the server. get() takes its failure callback, which is
// the "no saved default" answer, so a lost restore shows up as a wrong stage or level
// rather than as a network error.
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    get: vi.fn((_url: string, _success: unknown, fail?: () => void) =>
        fail?.(),
    ),
    post: vi.fn(),
    postString: vi.fn(),
}));

// Stand in for a Synphony settings load that has not delivered. beginLoadSynphonySettings
// resolves on its failure path too (deliberately -- BL-16732: a caller left waiting forever
// is what the user sees as a button that does nothing), so a tool's init promise resolving
// says nothing about whether the settings actually arrived. That is the situation here.
vi.mock("./readerTools", () => ({
    beginInitializeLeveledReaderTool: vi.fn(() => Promise.resolve()),
    beginInitializeDecodableReaderTool: vi.fn(() => Promise.resolve()),
}));

import { getTheOneReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { LeveledReaderTool } from "./leveledReader/leveledReaderTool";
import { DecodableReaderTool } from "./decodableReader/decodableReaderTool";

/** Settings with four stages and four levels, so stage/level 3 is a real choice. */
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

describe("restoring a reader tool before the Synphony settings have arrived", () => {
    beforeEach(() => {
        getTheOneReaderToolsModel().clearForTest();
    });

    it("still applies the book's saved level once the settings arrive", async () => {
        const model = getTheOneReaderToolsModel();
        // Sanity checks: we really are starting from the default with no settings, so a 3
        // at the end must have come from the saved state rather than from where we began.
        expect(model.levelNumber).toBe(1);
        expect(model.synphony).toBeUndefined();

        await new LeveledReaderTool().beginRestoreSettings({
            leveledReaderState: "3",
        });
        model.setSynphony(makeSynphonyWithFourOfEach());

        expect(model.levelNumber).toBe(3);
    });

    it("still applies the book's saved stage once the settings arrive", async () => {
        const model = getTheOneReaderToolsModel();
        expect(model.stageNumber).toBe(1);
        expect(model.synphony).toBeUndefined();

        await new DecodableReaderTool().beginRestoreSettings({
            decodableReaderState: "stage:3;sort:alphabetic",
        });
        model.setSynphony(makeSynphonyWithFourOfEach());

        expect(model.stageNumber).toBe(3);
    });

    it("does not resurrect a saved level that the collection no longer has", async () => {
        const model = getTheOneReaderToolsModel();

        await new LeveledReaderTool().beginRestoreSettings({
            leveledReaderState: "9",
        });
        model.setSynphony(makeSynphonyWithFourOfEach());

        // Only four levels exist, so level 9 is not a choice we can honor. Holding the
        // request must not mean imposing a level that isn't there.
        expect(model.levelNumber).toBe(1);
    });
});
