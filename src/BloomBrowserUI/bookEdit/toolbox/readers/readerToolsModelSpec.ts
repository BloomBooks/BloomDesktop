import { describe, it, expect, vi } from "vitest";
import { ReaderToolsModel } from "./readerToolsModel";
import { theOneLanguageDataInstance } from "./libSynphony/synphony_lib";
import { TextFragment } from "./libSynphony/bloomSynphonyExtensions";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";

describe("getWordLength tests", () => {
    it("counts simple letters", () => {
        expect(ReaderToolsModel.getWordLength("abcde")).toBe(5);
    });
    it("skips diacritics", () => {
        expect(ReaderToolsModel.getWordLength("a\u0301\u0302bcde")).toBe(5);
    });
    it("counts digraphs and trigraphs once, skipping embedded diacritics", () => {
        theOneLanguageDataInstance.addGrapheme("th");
        theOneLanguageDataInstance.addGrapheme("o\u0301");
        theOneLanguageDataInstance.addGrapheme("ough");
        theOneLanguageDataInstance.addGrapheme("thr");
        expect(ReaderToolsModel.getWordLength("thro\u0301ugh")).toBe(2);
    });
});

describe("averageWordsInPage tests", () => {
    it("exact average", () => {
        // Two pages, each with one sentence of three words
        const data = [
            [{ words: ["one", "two", "three"] } as TextFragment],
            [{ words: ["four", "five", "six"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(3);
    });
    it("multiple sentences on page, fractional result 1", () => {
        // Two pages, first with two sentence of three words
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"],
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment,
            ],
            [{ words: ["four", "five", "six"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(11 / 2);
    });
    it("multiple sentences on page, fractional result 2", () => {
        // Three pages, 13 words
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"],
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment,
            ],
            [{ words: ["four", "five", "six"] } as TextFragment],
            [{ words: ["four", "five"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(13 / 3);
    });
});

describe("averageWordsInSentence tests", () => {
    it("computes average sentence length, ignoring empty ones", () => {
        const data = [
            [
                { words: ["one", "two", "three", "four"] } as TextFragment,
                { words: ["five", "six"] } as TextFragment,
            ],
            [
                { words: ["seven", "eight", "nine"] } as TextFragment,
                { words: [] as string[] } as TextFragment,
            ],
            [
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
            ],
        ];
        expect(ReaderToolsModel.averageWordsInSentence(data)).toBe(3);
    });
});

describe("averageGlyphsInWord tests", () => {
    it("exact average", () => {
        // Simple four letter wordss
        const data = [
            [{ words: ["four", "five"] } as TextFragment],
            [{ words: ["nine"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(4);
    });
    it("fractional result", () => {
        // Four words, 18 letters
        const data = [
            [{ words: ["four", "five"] } as TextFragment],
            [{ words: ["sixes", "seven"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(18 / 4);
    });
    it("fractional result, with diacritics", () => {
        // Four words, 15 letters plus several diacritics
        const data = [
            [
                {
                    words: ["fo\u0301u\u0301r", "fi\u0301ve\u0301"],
                } as TextFragment,
            ],
            [{ words: ["si\u0301x", "se\u0301vn"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(15 / 4);
    });
});

describe("averageSentencesInPage tests", () => {
    it("exact average", () => {
        // Two pages, each with one sentence
        const data = [
            [{ words: ["one", "two", "three"] } as TextFragment],
            [{ words: ["four", "five", "six"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(1);
    });
    it("multiple sentences on page, fractional result", () => {
        // Two pages, first with two sentences
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"],
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment,
            ],
            [{ words: ["four", "five", "six"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(1.5);
    });
    it("multiple sentences on page, fractional result", () => {
        // Three pages, seven sentences
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"],
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment,
                { words: ["the", "third", "sentence"] } as TextFragment,
                { words: ["a", "fourth", "sentence"] } as TextFragment,
            ],
            [
                { words: ["four", "five", "six"] } as TextFragment,
                { words: ["second", "on", "page", "two"] } as TextFragment,
            ],
            [{ words: ["four", "five"] } as TextFragment],
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(7 / 3);
    });
    it("computes max average sentence length, ignoring empty sentences", () => {
        const data = [
            // total of three non-empty sentences on three pages.
            [
                {
                    words: ["one", "two", "three", "four"],
                } as TextFragment,
                { words: ["five", "six"] } as TextFragment,
            ],
            [
                {
                    words: ["seven", "eight", "nine"],
                } as TextFragment,
                { words: [] as string[] } as TextFragment,
            ],
            [
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
            ],
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(1);
    });
    it("computes max average sentence length when no pages", () => {
        const data = [];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(0);
    });
});

describe("updateStageNofM tests", () => {
    // Sets up various spies needed for updateStageNOfMInternal to run as a true unit test (other dependencies are stubbed out)
    //
    // readerToolsModel - the ReaderToolsModel object that will call updateStageNofMInternal
    // stageLabel - the value returned by getStageLabel()
    // numberOfStages - the value returned by getNumberOfStages()
    // localizedFormatString - the value returned by the localization manager
    function setupSpiesForUpdateStageNOfMInternal(params: {
        readerToolsModel: ReaderToolsModel;
        stageLabel: string;
        numberOfStages: number;
    }) {
        vi.spyOn(params.readerToolsModel, "getStageLabel").mockReturnValue(
            params.stageLabel,
        );
        vi.spyOn(params.readerToolsModel, "getNumberOfStages").mockReturnValue(
            params.numberOfStages,
        );
    }

    it("fixes stageNofM with N before M", () => {
        const stageNofM = document.createElement("div");
        stageNofM.innerText = "[fake unlocalized text]";
        const obj = new ReaderToolsModel();

        // Setup various spies to make this a true unit test
        setupSpiesForUpdateStageNOfMInternal({
            readerToolsModel: obj,
            stageLabel: "2",
            numberOfStages: 6,
        });

        theOneLocalizationManager.dictionary[
            "EditTab.Toolbox.DecodableReaderTool.StageNofM"
        ] = "Stage {0} of {1} en español";

        // System under test
        obj.updateStageNOfMInternal(stageNofM);

        // Expect the text to be
        expect(stageNofM.innerText).toBe("Stage 2 of 6 en español");

        // Cleanup
        delete theOneLocalizationManager.dictionary[
            "EditTab.Toolbox.DecodableReaderTool.StageNofM"
        ];
    });

    it("fixes stageNofM with N AFTER M", () => {
        const stageNofM = document.createElement("div");
        stageNofM.innerText = "[fake unlocalized text]";
        const obj = new ReaderToolsModel();

        // Setup various spies to make this a true unit test
        setupSpiesForUpdateStageNOfMInternal({
            readerToolsModel: obj,
            stageLabel: "3",
            numberOfStages: 10,
        });

        theOneLocalizationManager.dictionary[
            "EditTab.Toolbox.DecodableReaderTool.StageNofM"
        ] = "there are {1} stages; {0} is the current one";

        // System under test
        obj.updateStageNOfMInternal(stageNofM);

        // Expect the text to be
        expect(stageNofM.innerText).toBe(
            "there are 10 stages; 3 is the current one",
        );

        // Cleanup
        delete theOneLocalizationManager.dictionary[
            "EditTab.Toolbox.DecodableReaderTool.StageNofM"
        ];
    });
});

describe("maxWordLength tests", () => {
    it("longest word", () => {
        expect(ReaderToolsModel.maxWordLength("the cat sat on the mat")).toBe(
            3,
        );
    });
    it("very long word with diacritics", () => {
        expect(
            ReaderToolsModel.maxWordLength(
                "the cat sat on the 'ma\u0301ntle\u0301pi\u0301ece\u0301'",
            ),
        ).toBe(11);
    });
});
