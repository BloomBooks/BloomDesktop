import { encode } from "html-entities";

// Takes in an unsafe piece of text, and readies it to be displayed in HTML.
// The result will be:
// 1) HTML-encoded (so, theoretically safe to pass into InnerHTML)
// 2) newlines and carriage returns are converted into <br> elements.
export function formatForHtml(unsafeText: string): string {
    let safeText = encode(unsafeText);

    // Replace literal newlines (which HTML ignores) with <br> elements.
    const htmlNewline = "<br />";
    safeText = safeText
        .replace(/\r\n/g, htmlNewline)
        .replace(/\r/g, htmlNewline)
        .replace(/\n/g, htmlNewline);

    return safeText;
}
