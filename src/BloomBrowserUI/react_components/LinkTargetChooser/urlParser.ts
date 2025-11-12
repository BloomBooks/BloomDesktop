export interface ParsedURL {
    bookId: string | null;
    pageId: string | null;
    parsedUrl: string;
    urlType: "hash" | "book-path" | "external" | "empty";
}

/**
 * Pure function that parses a raw URL string and returns normalized URL components.
 * Handles four URL formats:
 * - Hash-only URLs: "#pageId" or "#cover". Bloom Player handles these as referring to the current book.
 * - Book path URLs: "/book/bookId" or "/book/bookId#pageId"
 * - External/other URLs: any other string
 * - Empty URLs: empty string
 */ export const parseURL = (rawUrl: string): ParsedURL => {
    if (!rawUrl) {
        return {
            bookId: null,
            pageId: null,
            parsedUrl: "",
            urlType: "empty",
        };
    }

    if (rawUrl.startsWith("#")) {
        const pageIdStr = rawUrl.substring(1) || "cover";
        const normalizedPageId = pageIdStr === "cover" ? "cover" : pageIdStr;
        return {
            bookId: null,
            pageId: normalizedPageId,
            parsedUrl:
                normalizedPageId === "cover"
                    ? "#cover"
                    : `#${normalizedPageId}`,
            urlType: "hash",
        };
    }

    if (rawUrl.startsWith("/book/")) {
        const hashIndex = rawUrl.indexOf("#");
        const bookId =
            hashIndex === -1
                ? rawUrl.substring(6)
                : rawUrl.substring(6, hashIndex);
        const rawPagePart =
            hashIndex === -1 ? "cover" : rawUrl.substring(hashIndex + 1);
        const normalizedPageId =
            !rawPagePart || rawPagePart === "cover" ? "cover" : rawPagePart;

        return {
            bookId,
            pageId: normalizedPageId,
            parsedUrl:
                normalizedPageId === "cover"
                    ? `/book/${bookId}`
                    : `/book/${bookId}#${normalizedPageId}`,
            urlType: "book-path",
        };
    }

    return {
        bookId: null,
        pageId: null,
        parsedUrl: rawUrl,
        urlType: "external",
    };
};
