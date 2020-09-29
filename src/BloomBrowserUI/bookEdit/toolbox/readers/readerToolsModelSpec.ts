import { ReaderToolsModel } from "./readerToolsModel";
import { theOneLanguageDataInstance } from "./libSynphony/synphony_lib";
import { TextFragment } from "./libSynphony/bloomSynphonyExtensions";

describe("getWordLength tests", () => {
    it("counts simple letters", () => {
        expect(ReaderToolsModel.getWordLength("abcde")).toBe(5);
    });
    it("skips diacritics", () => {
        expect(ReaderToolsModel.getWordLength("a\u0301\u0302bcde")).toBe(5);
    });
    it("counts digraphs and trigraphs once, skiping embedded diacritics", () => {
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
            [{ words: ["four", "five", "six"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(3);
    });
    it("multiple sentences on page, rounding 0.5 up", () => {
        // Two pages, first with two sentence of three words
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"]
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment
            ],
            [{ words: ["four", "five", "six"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(6);
    });
    it("multiple sentences on page, rounding down", () => {
        // Three pages, 13 words
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"]
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment
            ],
            [{ words: ["four", "five", "six"] } as TextFragment],
            [{ words: ["four", "five"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(4);
    });
});

describe("averageWordsInSentence tests", () => {
    it("computes average sentence length, ignoring empty ones", () => {
        const data = [
            [
                { words: ["one", "two", "three", "four"] } as TextFragment,
                { words: ["five", "six"] } as TextFragment
            ],
            [
                { words: ["seven", "eight", "nine"] } as TextFragment,
                { words: [] as string[] } as TextFragment
            ],
            [
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment
            ]
        ];
        expect(ReaderToolsModel.averageWordsInSentence(data)).toBe(3);
    });
});

describe("averageGlyphsInWord tests", () => {
    it("exact average", () => {
        // Simple four letter wordss
        const data = [
            [{ words: ["four", "five"] } as TextFragment],
            [{ words: ["nine"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(4);
    });
    it("rounding up", () => {
        // Four words, 18 letters
        const data = [
            [{ words: ["four", "five"] } as TextFragment],
            [{ words: ["sixes", "seven"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(5);
    });
    it("rounding down, with diacritics", () => {
        // Four words, 17 letters plus several diacritics
        const data = [
            [
                {
                    words: ["fo\u0301u\u0301r", "fi\u0301ve\u0301"]
                } as TextFragment
            ],
            [{ words: ["si\u0301x", "se\u0301vn"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(4);
    });
});

describe("averageSentencesInPage tests", () => {
    it("exact average", () => {
        // Two pages, each with one sentence
        const data = [
            [{ words: ["one", "two", "three"] } as TextFragment],
            [{ words: ["four", "five", "six"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(1);
    });
    it("multiple sentences on page, rounding 0.5 up", () => {
        // Two pages, first with two sentences
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"]
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment
            ],
            [{ words: ["four", "five", "six"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(2);
    });
    it("multiple sentences on page, rounding down", () => {
        // Three pages, seven sentences
        const data = [
            [
                {
                    words: ["one", "two", "three", "four", "five"]
                } as TextFragment,
                { words: ["a", "b", "c"] } as TextFragment,
                { words: ["the", "third", "sentence"] } as TextFragment,
                { words: ["a", "fourth", "sentence"] } as TextFragment
            ],
            [
                { words: ["four", "five", "six"] } as TextFragment,
                { words: ["second", "on", "page", "two"] } as TextFragment
            ],
            [{ words: ["four", "five"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(2);
    });
    it("computes max average sentence length, ignoring empty sentences", () => {
        const data = [
            // total of three non-empty sentences on three pages.
            [
                {
                    words: ["one", "two", "three", "four"]
                } as TextFragment,
                { words: ["five", "six"] } as TextFragment
            ],
            [
                {
                    words: ["seven", "eight", "nine"]
                } as TextFragment,
                { words: [] as string[] } as TextFragment
            ],
            [
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment,
                { words: [] as string[] } as TextFragment
            ]
        ];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(1);
    });
    it("computes max average sentence length when no pages", () => {
        const data = [];
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(0);
    });
});

describe("maxWordLength tests", () => {
    it("longest word", () => {
        expect(ReaderToolsModel.maxWordLength("the cat sat on the mat")).toBe(
            3
        );
    });
    it("very long word with diacritics", () => {
        expect(
            ReaderToolsModel.maxWordLength(
                "the cat sat on the 'ma\u0301ntle\u0301pi\u0301ece\u0301'"
            )
        ).toBe(11);
    });
});
