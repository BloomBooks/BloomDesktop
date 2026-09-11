// utilities for text manipulation

export function splitIntoGraphemes(text: string): string[] {
    // Regular expression to match a base character (or space) followed by any number of diacritics
    // Enhance: could make use of data from Decodable Reader to allow characters that are not
    // normally word-forming to be treated as such here.
    const graphemeRegex = /(\p{L}| )\p{M}*/gu;
    return text.match(graphemeRegex) || [];
}

/**
 * Several of our UI strings mark the words that should become a hyperlink by wrapping
 * them in square brackets, e.g. "Most ePUB readers are very low quality ([see our
 * research and recommendations])."
 *
 * Finding that pair by taking the first "[" and the next "]" breaks in the
 * Pseudo-English UI language (BL-16748), because pseudo-localization wraps the whole
 * string in brackets of its own: the first "[" is then the wrapper's, and everything up
 * to the real link's "]" gets swallowed into the link. So we anchor on the first "]"
 * instead and take the nearest "[" before it, which picks the innermost pair -- the real
 * link markup in both the plain and the pseudo-localized string.
 *
 * Returns undefined when there is no usable pair, in which case the caller decides what
 * to do with the unmarked string.
 */
export function findLinkTextBrackets(
    text: string,
): { open: number; close: number } | undefined {
    const close = text.indexOf("]");
    if (close < 0) return undefined;
    const open = text.lastIndexOf("[", close);
    if (open < 0) return undefined;
    return { open, close };
}
