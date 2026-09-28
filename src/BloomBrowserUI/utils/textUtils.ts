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
 * Returns the string split around the link, without the brackets. When there is no usable
 * pair, found is false and the whole string is the linkText, which is what most callers
 * want to show anyway; the ones that don't can check found.
 */
export function splitAtLinkText(text: string): {
    found: boolean;
    beforeLink: string;
    linkText: string;
    afterLink: string;
} {
    const close = text.indexOf("]");
    const open = close < 0 ? -1 : text.lastIndexOf("[", close);
    if (open < 0) {
        return { found: false, beforeLink: "", linkText: text, afterLink: "" };
    }
    return {
        found: true,
        beforeLink: text.substring(0, open),
        linkText: text.substring(open + 1, close),
        afterLink: text.substring(close + 1),
    };
}
