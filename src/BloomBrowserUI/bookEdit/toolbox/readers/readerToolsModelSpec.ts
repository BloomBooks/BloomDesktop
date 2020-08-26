import { ReaderToolsModel } from "./readerToolsModel";
import { theOneLanguageDataInstance } from "./libSynphony/synphony_lib";

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
