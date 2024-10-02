// utilities for text manipulation

export function splitIntoGraphemes(text: string): string[] {
    // Regular expression to match a base character (or space) followed by any number of diacritics
    // Enhance: could make use of data from Decodable Reader to allow characters that are not
    // normally word-forming to be treated as such here.
    const graphemeRegex = /(\p{L}| )\p{M}*/gu;
    return text.match(graphemeRegex) || [];
}
