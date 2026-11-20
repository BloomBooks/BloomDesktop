import { describe, it, expect, vi } from "vitest";
import {
    getFileExtension,
    isValidSampleTextFileType,
    ReaderToolsModel,
} from "./readerToolsModel";
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

// The setup dialog lists these files and Bloom loads them; both go through this, so a file the
// dialog shows as usable is one Bloom will actually read.
describe("sample text file readability", () => {
    it("accepts the readable extensions", () => {
        expect(isValidSampleTextFileType("words.txt")).toBe(true);
        expect(isValidSampleTextFileType("words.js")).toBe(true);
        expect(isValidSampleTextFileType("words.json")).toBe(true);
    });

    it("ignores case, so a .TXT file is readable too", () => {
        expect(isValidSampleTextFileType("WORDS.TXT")).toBe(true);
        expect(isValidSampleTextFileType("Words.Txt")).toBe(true);
        expect(isValidSampleTextFileType("words.JSON")).toBe(true);
    });

    it("rejects a format Bloom cannot read", () => {
        expect(isValidSampleTextFileType("words.docx")).toBe(false);
        expect(isValidSampleTextFileType("words.pdf")).toBe(false);
    });

    it("rejects a file with no extension at all", () => {
        expect(isValidSampleTextFileType("words")).toBe(false);
        expect(getFileExtension("words")).toBeUndefined();
    });

    it("uses the last extension, lowercased, on a path with dots and folders", () => {
        expect(getFileExtension("C:/texts/my.word.list.TXT")).toBe("txt");
        expect(isValidSampleTextFileType("C:/texts/my.word.list.TXT")).toBe(
            true,
        );
    });

    // A dot in a folder name is not a file type. Getting this wrong made a file with no
    // extension look like it had one, so the dialog labelled it "Cannot read this format"
    // instead of "File needs .TXT extension".
    it("does not mistake a dot in a folder name for an extension", () => {
        expect(getFileExtension("C:/My.Books/wordlist")).toBeUndefined();
        expect(getFileExtension("C:\\My.Books\\wordlist")).toBeUndefined();
        expect(isValidSampleTextFileType("C:/My.Books/wordlist")).toBe(false);
        // ...but a real extension under such a folder is still found.
        expect(getFileExtension("C:/My.Books/wordlist.TXT")).toBe("txt");
        expect(isValidSampleTextFileType("C:/My.Books/wordlist.txt")).toBe(
            true,
        );
    });
});

describe("ReaderToolsModel.canUndo", () => {
    it("declines once the box it remembers has gone from the page", () => {
        // A table undo, for instance, rebuilds its cells; undoing typing in the old cell would
        // change nothing visible and keep the Undo button from reaching what comes next.
        const box = document.createElement("div");
        box.textContent = "before";
        document.body.appendChild(box);
        const model = new ReaderToolsModel();
        model.noteFocus(box);
        box.textContent = "after typing";
        expect(model.canUndo()).toBe(true); // sanity check: there is typing to undo

        box.remove();

        expect(model.canUndo()).toBe(false);
    });
});

// The reader tools' undo restores a snapshot of one text block's HTML. Every language's block has
// its own copy of each inline image, and the image's menus and toolbar hold the live wrapper.
describe("ReaderToolsModel undo with an inline image in the block", () => {
    function makeBoxWithInlineImage(): HTMLDivElement {
        const box = document.createElement("div");
        box.innerHTML =
            '<div class="bloom-inlineImage" contenteditable="false" ' +
            'data-bloom-inline-image-id="pic1"><img src="old.png"></div>' +
            "<p>before</p>";
        document.body.appendChild(box);
        return box;
    }

    it("puts the text back but keeps the picture as it is now", () => {
        const box = makeBoxWithInlineImage();
        const model = new ReaderToolsModel();
        model.noteFocus(box);
        const liveWrapper = box.querySelector(".bloom-inlineImage")!;
        box.querySelector("img")!.setAttribute("src", "new.png");
        box.querySelector("p")!.textContent = "after typing";
        // Sanity check: there is typing to undo.
        expect(model.canUndo()).toBe(true);

        model.undo();

        expect(box.querySelector("p")!.textContent).toBe("before");
        expect(box.querySelector("img")!.getAttribute("src")).toBe("new.png");
        expect(
            box.querySelector(".bloom-inlineImage"),
            "The wrapper that the picture's menu holds should still be the one on the page.",
        ).toBe(liveWrapper);
        box.remove();
    });

    it("starts the history again when asked for a container that holds the block", () => {
        const group = document.createElement("div");
        const box = makeBoxWithInlineImage();
        group.appendChild(box);
        document.body.appendChild(group);
        const elsewhere = document.createElement("div");
        const model = new ReaderToolsModel();
        model.noteFocus(box);
        box.querySelector("p")!.textContent = "after typing";
        // Sanity check: there is typing to undo.
        expect(model.canUndo()).toBe(true);

        model.restartHistoryIn(elsewhere);
        expect(
            model.canUndo(),
            "A container that does not hold the block should leave its history alone.",
        ).toBe(true);

        model.restartHistoryIn(group);
        expect(model.canUndo()).toBe(false);
        group.remove();
    });
});
