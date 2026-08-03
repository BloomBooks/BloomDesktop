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

/**
 * Splits a word into the graphemes the language teaches, using Synphony's own segmentation so
 * that a word typed into this dialog is judged by exactly the same rules as a word that came
 * from a sample text (which Synphony segments when it loads the word into its vocabulary).
 *
 * getGpcForm takes the grapheme list as an argument and reads no state off the LanguageData it
 * hangs from, which is what makes it usable here: this dialog runs in the workspace frame,
 * whose copy of the Synphony data is empty, and the alphabet we want to match against is the
 * one currently in the dialog rather than the one last saved.
 */
const getGpcForm = (word: string, allGpcs: string[]): string[] => {
    // Synphony expects the graphemes pre-sorted longest-first, so that "ch" wins over "c".
    const sortedGpcs = Array.from(
        new Set(allGpcs.map((gpc) => gpc.toLowerCase()).filter(Boolean)),
    ).sort((firstGpc, secondGpc) => secondGpc.length - firstGpc.length);

    return theOneLanguageDataInstance.getGpcForm(
        word.toLowerCase(),
        sortedGpcs,
    );
};

/**
 * Returns whether a word contains only graphemes the reader has been taught, plus any symbols
 * the language allows at any stage (a syllable break, a stress mark, and so on). Those symbols
 * have to be supplied by the caller, because only the toolbox frame has the Synphony data that
 * defines them — see getSynphonyAlwaysMatchSymbols in readerTools.
 */
export const hasOnlyKnownGraphemes = (
    word: string,
    allGpcs: string[],
    knownGpcs: string[],
    alwaysMatchSymbols: string[],
): boolean => {
    const allowedGpcs = new Set(knownGpcs.map((gpc) => gpc.toLowerCase()));
    for (const symbol of alwaysMatchSymbols) {
        allowedGpcs.add(symbol);
    }

    return getGpcForm(word, allGpcs).every((gpc) => allowedGpcs.has(gpc));
};
