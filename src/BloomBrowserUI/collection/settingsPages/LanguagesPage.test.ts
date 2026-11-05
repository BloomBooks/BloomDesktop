import { describe, expect, it, vi } from "vitest";
import { ICollectionSettingsLanguage } from "../collectionSettingsTypes";
import { ILanguageData } from "../languageData";

// Only the chooser's answer is under test here, not the chooser itself.
vi.mock("../LanguageChooserDialog", () => ({
    LanguageChooserDialog: () => null,
}));

import { languageFromChooser } from "./LanguagesPage";

const kalaba: ICollectionSettingsLanguage = {
    tag: "xkal",
    name: "Kalaba",
    isCustomName: false,
    fontName: "Charis SIL",
    isRightToLeft: false,
    lineHeight: 1.5,
    breaksLinesOnlyAtSpaces: true,
    baseUIFontSizeInPoints: 14,
};

const arabicChosen: ILanguageData = {
    LanguageTag: "ar",
    DefaultName: "العربية",
    DesiredName: "العربية",
    IsRtl: true,
    Country: "Egypt",
};

describe("languageFromChooser", () => {
    it("takes the chosen language's identity and direction, and keeps the rest of the one it replaces", () => {
        expect(kalaba.isRightToLeft).toBe(false);

        const language = languageFromChooser(arabicChosen, kalaba, "Andika");

        expect(language).toEqual({
            ...kalaba,
            tag: "ar",
            name: "العربية",
            isCustomName: false,
            isRightToLeft: true,
        });
    });

    it("counts a name the user typed in the chooser as custom", () => {
        const language = languageFromChooser(
            { ...arabicChosen, DesiredName: "Arabic (Egypt)" },
            kalaba,
            "Andika",
        );

        expect(language.name).toBe("Arabic (Egypt)");
        expect(language.isCustomName).toBe(true);
    });

    it("keeps the direction the user had when the chooser does not know the script's", () => {
        const rightToLeftKalaba = { ...kalaba, isRightToLeft: true };

        const language = languageFromChooser(
            { ...arabicChosen, IsRtl: null },
            rightToLeftKalaba,
            "Andika",
        );

        expect(language.isRightToLeft).toBe(true);
    });

    it("starts a language the collection did not have from Bloom's defaults", () => {
        const language = languageFromChooser(arabicChosen, null, "Andika");

        expect(language).toEqual({
            tag: "ar",
            name: "العربية",
            isCustomName: false,
            fontName: "Andika",
            isRightToLeft: true,
            lineHeight: 0,
            breaksLinesOnlyAtSpaces: false,
            baseUIFontSizeInPoints: 0,
        });
    });
});
