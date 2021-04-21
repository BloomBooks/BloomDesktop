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
    it("multiple sentences on page, fractional result 1", () => {
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
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(11 / 2);
    });
    it("multiple sentences on page, fractional result 2", () => {
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
        expect(ReaderToolsModel.averageWordsInPage(data)).toBe(13 / 3);
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
    it("fractional result", () => {
        // Four words, 18 letters
        const data = [
            [{ words: ["four", "five"] } as TextFragment],
            [{ words: ["sixes", "seven"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(18 / 4);
    });
    it("fractional result, with diacritics", () => {
        // Four words, 15 letters plus several diacritics
        const data = [
            [
                {
                    words: ["fo\u0301u\u0301r", "fi\u0301ve\u0301"]
                } as TextFragment
            ],
            [{ words: ["si\u0301x", "se\u0301vn"] } as TextFragment]
        ];
        expect(ReaderToolsModel.averageGlyphsInWord(data)).toBe(15 / 4);
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
    it("multiple sentences on page, fractional result", () => {
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
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(1.5);
    });
    it("multiple sentences on page, fractional result", () => {
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
        expect(ReaderToolsModel.averageSentencesInPage(data)).toBe(7 / 3);
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

function checkText(item: ChildNode, content: string) {
    expect(item instanceof HTMLElement).toBe(false);
    expect(item.textContent).toBe(content);
}
function checkSpan(item: ChildNode, id: string) {
    expect(item instanceof HTMLElement).toBe(true);
    expect((item as HTMLElement).tagName).toBe("SPAN");
    expect((item as HTMLElement).getAttribute("id")).toBe(id);
}

describe("prepareStageNofM tests", () => {
    it("fixes stageNofM with items in order", () => {
        const stageNofM = document.createElement("div");
        stageNofM.innerText = "stage {0} of {1}";
        ReaderToolsModel.prepareStageNofMInternal(stageNofM);
        expect(stageNofM.childNodes.length).toBe(4);
        checkText(stageNofM.childNodes[0], "stage ");
        checkSpan(stageNofM.childNodes[1], "stageNumber");
        checkText(stageNofM.childNodes[2], " of ");
        checkSpan(stageNofM.childNodes[3], "numberOfStages");
    });

    it("fixes stageNofM with items out of order", () => {
        const stageNofM = document.createElement("div");
        stageNofM.innerText = "there are {1} stages; {0} is the current one";
        ReaderToolsModel.prepareStageNofMInternal(stageNofM);
        expect(stageNofM.childNodes.length).toBe(5);
        checkText(stageNofM.childNodes[0], "there are ");
        checkSpan(stageNofM.childNodes[1], "numberOfStages");
        checkText(stageNofM.childNodes[2], " stages; ");
        checkSpan(stageNofM.childNodes[3], "stageNumber");
        checkText(stageNofM.childNodes[4], " is the current one");
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
