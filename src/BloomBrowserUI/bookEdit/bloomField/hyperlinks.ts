import { get } from "../../utils/bloomApi";

// returns empty string if the URL is not valid.
// returns the url as is if it is external
// returns a simplified version if it is a link to the current book
export function tryProcessHyperlink(
    text: string,
    currentBookId: string,
): string {
    if (!text || !currentBookId) {
        return "";
    }
    // Is this probably a valid URL?
    const allowedPrefixes = ["http://", "https://", "mailto:", "#", "/book/"];
    if (
        !allowedPrefixes.some((prefix) =>
            text.toLocaleLowerCase().startsWith(prefix),
        )
    ) {
        return "";
    }
    const currentBookPrefix = `/book/${currentBookId}`;
    if (text.startsWith(currentBookPrefix)) {
        // It's link within this book, so simplify and make it resilient
        // to the book being duplicated (and getting a new book id)
        // by just returning the page id part.
        return text.substring(currentBookPrefix.length);
    } else {
        return text;
    }
}

export function getHyperlinkFromClipboard(
    callback: (url: string) => void,
): void {
    get("common/clipboardText", (result) => {
        if (!result.data) {
            callback("");
        }
        get("app/selectedBookInfo", (bookInfo) => {
            if (!bookInfo.data) {
                callback("");
            }
            callback(tryProcessHyperlink(result.data, bookInfo.data.id));
        });
    });
}
