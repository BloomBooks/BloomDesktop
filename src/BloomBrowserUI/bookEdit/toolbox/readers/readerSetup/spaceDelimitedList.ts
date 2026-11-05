// Kept apart from decodableStagesUtils, which loads Synphony, so that the Collection Settings
// dialog can clean an alphabet the same way Reader Setup does without loading the reader tools.

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
