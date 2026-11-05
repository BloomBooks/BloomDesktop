// Kept apart from decodableStagesUtils, which loads Synphony, so that the Collection Settings
// dialog can clean and explain an alphabet the same way Reader Setup does without loading the
// reader tools.

/**
 * The example alphabet shown under the help on how to type one ("here is what we might use for the
 * English language:"), in Reader Setup's Letters tab and on Collection Settings' Languages page. It
 * is not translated.
 */
export const kEnglishAlphabetExample =
    "a b c ch d e f g h i j k l m n ng o p q r s sh t th u v w x y z ' -";

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
