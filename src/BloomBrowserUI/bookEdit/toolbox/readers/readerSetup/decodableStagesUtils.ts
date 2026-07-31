import { ReaderSettings } from "../ReaderSettings";
import { theOneLanguageDataInstance } from "../libSynphony/synphony_lib";

/**
 * Copies the settings so the setup dialog can edit a draft without touching the live
 * model. The stage and level objects are copied too, since the dialog edits their fields;
 * the arrays inside them (e.g. stage.words) are shared, as the dialog never mutates those.
 */
export const cloneReaderSettings = (source: ReaderSettings): ReaderSettings => {
    return {
        ...source,
        levels: source.levels.map((level) => ({ ...level })),
        stages: source.stages.map((stage) => ({ ...stage })),
    } as ReaderSettings;
};

/**
 * if the user enters a comma-separated list, remove the commas before saving (this is a space-delimited list)
 * Also converts newlines to spaces.
 * @param original
 * @returns {string}
 */
export function cleanSpaceDelimitedList(original: string): string {
    let cleaned: string = original
        .replace(/,/g, " ")
        .replace(/\r/g, " ")
        .replace(/\n/g, " "); // replace commas and newlines
    cleaned = cleaned.trim().replace(/ ( )+/g, " "); // remove consecutive spaces

    return cleaned;
}

/**
 * Normalizes the typed sample-word list for storage: a space-delimited list with no
 * duplicates. The legacy dialog also de-duplicated (`_.uniq` in readerSetup.io.ts), and it
 * matters because every copy is fed to LanguageData.addWord, which counts repeats as
 * higher word frequency.
 */
export const cleanSampleWordList = (original: string): string => {
    const words = cleanSpaceDelimitedList(original)
        .split(" ")
        .filter((word) => word !== "");
    return Array.from(new Set(words)).join(" ");
};

/**
 * Returns a copy of the settings with every space-delimited field normalized the way
 * storage expects. Each of these fields is later re-split on plain spaces (see
 * ReadersSynphonyWrapper.loadSettings), so the commas and newlines a user may type into
 * the dialog's text boxes have to be collapsed here; otherwise a line like "cat\ndog" is
 * stored — and then loaded — as a single run-together word.
 */
export const prepareSettingsForSave = (
    settings: ReaderSettings,
): ReaderSettings => {
    const settingsToSave = cloneReaderSettings(settings);
    settingsToSave.letters = cleanSpaceDelimitedList(settingsToSave.letters);
    settingsToSave.moreWords = cleanSampleWordList(settingsToSave.moreWords);
    for (const stage of settingsToSave.stages) {
        stage.letters = cleanSpaceDelimitedList(stage.letters);
        stage.sightWords = cleanSpaceDelimitedList(stage.sightWords);
    }
    return settingsToSave;
};

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
