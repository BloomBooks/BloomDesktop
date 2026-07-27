import { ReaderSettings } from "../ReaderSettings";
import { theOneLanguageDataInstance } from "../libSynphony/synphony_lib";

export const cloneReaderSettings = (source: ReaderSettings): ReaderSettings => {
    return {
        ...source,
        levels: source.levels.map((level) => ({ ...level })),
        stages: source.stages.map((stage) => ({ ...stage })),
    } as ReaderSettings;
};

export function cleanSpaceDelimitedList(original: string): string {
    let cleaned: string = original
        .replace(/,/g, " ")
        .replace(/\r/g, " ")
        .replace(/\n/g, " "); // replace commas and newlines
    cleaned = cleaned.trim().replace(/ ( )+/g, " "); // remove consecutive spaces

    return cleaned;
}

/** Splits a word into configured graphemes using Synphony's legacy matching order. */
const getGpcForm = (word: string, allGpcs: string[]): string[] => {
    const sortedGpcs = Array.from(
        new Set(allGpcs.map((gpc) => gpc.toLowerCase()).filter(Boolean)),
    ).sort((firstGpc, secondGpc) => secondGpc.length - firstGpc.length);
    const gpcForm: string[] = [];
    let remainingWord = word.toLowerCase();

    while (remainingWord.length > 0) {
        const matchingGpc = sortedGpcs.find((gpc) =>
            remainingWord.endsWith(gpc),
        );
        if (matchingGpc) {
            gpcForm.unshift(matchingGpc);
            remainingWord = remainingWord.slice(0, -matchingGpc.length);
            continue;
        }

        const lastCharacter = remainingWord.charAt(remainingWord.length - 1);
        const lastCharacterCode = lastCharacter.charCodeAt(0);
        const characterLength =
            0xd800 <= lastCharacterCode && lastCharacterCode <= 0xdfff ? 2 : 1;
        gpcForm.unshift(remainingWord.slice(-characterLength));
        remainingWord = remainingWord.slice(0, -characterLength);
    }

    return gpcForm;
};

/** Returns whether a word contains only taught or language-defined allowed graphemes. */
export const hasOnlyKnownGraphemes = (
    word: string,
    allGpcs: string[],
    knownGpcs: string[],
): boolean => {
    const allowedGpcs = new Set(knownGpcs.map((gpc) => gpc.toLowerCase()));
    for (const value of [
        theOneLanguageDataInstance["AlwaysMatch"],
        theOneLanguageDataInstance["SyllableBreak"],
        theOneLanguageDataInstance["StressSymbol"],
        theOneLanguageDataInstance["MorphemeBreak"],
    ]) {
        if (typeof value === "string" && value !== "") {
            allowedGpcs.add(value);
        }
    }

    return getGpcForm(word, allGpcs).every((gpc) => allowedGpcs.has(gpc));
};
