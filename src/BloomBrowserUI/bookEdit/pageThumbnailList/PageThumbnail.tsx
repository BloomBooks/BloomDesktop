import * as React from "react";
import { useState, useEffect, useCallback } from "react";

import "../../lib/errorHandler";
import { get } from "../../utils/bloomApi";
import { IPage } from "./pageThumbnailList";

// Global information across all PageThumbnails...see comments in requestPage()
let lastPageRequestTime = 0;
let activePageRequestCount = 0;
let pendingPageRequestCount = 0;

// Fix relative resource URLs to point to the correct book folder
const fixResourceUrls = (
    html: string,
    bookId?: string,
    bookFolderPath?: string,
): string => {
    if (!html || (!bookId && !bookFolderPath)) {
        return html || "";
    }

    const makeBookFileEncodedUrl = (rawPath: string): string => {
        if (!bookId) {
            return rawPath;
        }

        let decodedPath = rawPath;
        try {
            decodedPath = decodeURIComponent(rawPath);
        } catch {
            decodedPath = rawPath;
        }

        let remainingPath = decodedPath;
        let hash = "";
        const hashIndex = remainingPath.indexOf("#");
        if (hashIndex >= 0) {
            hash = remainingPath.substring(hashIndex);
            remainingPath = remainingPath.substring(0, hashIndex);
        }

        let query = "";
        const queryIndex = remainingPath.indexOf("?");
        if (queryIndex >= 0) {
            query = remainingPath.substring(queryIndex);
            remainingPath = remainingPath.substring(0, queryIndex);
        }

        const normalizedPath = remainingPath
            .replace(/\\/g, "/")
            .replace(/^\/+/, "");
        const fileParam = encodeURIComponent(normalizedPath);
        const baseEncodedUrl = `/bloom/api/collections/bookFile?book-id=${encodeURIComponent(
            bookId,
        )}&file=${fileParam}`;
        const extraQuery = query ? `&${query.substring(1)}` : "";

        return `${baseEncodedUrl}${extraQuery}${hash}`;
    };

    const makeFolderEncodedUrl = (rawPath: string): string => {
        if (!bookFolderPath) {
            return rawPath;
        }

        const normalizedFolder = bookFolderPath.replace(/\\/g, "/");
        const encodedFolderSegments = normalizedFolder
            .split("/")
            .map((segment) => encodeURIComponent(segment));
        const baseEncodedPath = `/bloom/${encodedFolderSegments.join("/")}`;

        if (typeof window !== "undefined") {
            try {
                const baseEncodedUrlWithSlash = baseEncodedPath.endsWith("/")
                    ? baseEncodedPath
                    : `${baseEncodedPath}/`;
                const baseBrowserEncodedUrl = new URL(
                    baseEncodedUrlWithSlash,
                    window.location.origin,
                );
                const resolvedBrowserEncodedUrl = new URL(
                    rawPath,
                    baseBrowserEncodedUrl,
                );
                if (
                    resolvedBrowserEncodedUrl.origin !== window.location.origin
                ) {
                    return resolvedBrowserEncodedUrl.href;
                }
                return `${resolvedBrowserEncodedUrl.pathname}${resolvedBrowserEncodedUrl.search}${resolvedBrowserEncodedUrl.hash}`;
            } catch {
                // fall through to simple concatenation
            }
        }

        const encodedRelativePath = rawPath
            .split("/")
            .map((segment) => encodeURIComponent(segment))
            .join("/");
        return `${baseEncodedPath}/${encodedRelativePath}`;
    };

    const isRelative = (candidateUrl: string): boolean => {
        const lowered = candidateUrl.toLowerCase();
        return !(
            lowered.startsWith("http://") ||
            lowered.startsWith("https://") ||
            lowered.startsWith("data:") ||
            lowered.startsWith("/") ||
            lowered.startsWith("javascript:") ||
            lowered.startsWith("mailto:") ||
            lowered.startsWith("#")
        );
    };

    const getResolvedEncodedUrl = (rawPath: string): string => {
        if (bookFolderPath) {
            return makeFolderEncodedUrl(rawPath);
        }
        return makeBookFileEncodedUrl(rawPath);
    };

    // Fix src and href attributes that reference relative paths
    let updatedHtml = html.replace(
        /(src|href)=("|')([^"']*?)\2/gi,
        (match, attrName, quote, attributeUnencodedRelativeUrl) => {
            if (
                !attributeUnencodedRelativeUrl ||
                !isRelative(attributeUnencodedRelativeUrl)
            ) {
                return match;
            }
            return `${attrName}=${quote}${getResolvedEncodedUrl(attributeUnencodedRelativeUrl)}${quote}`;
        },
    );

    // Fix inline style url(...) references (e.g., background-image)
    updatedHtml = updatedHtml.replace(
        /url\(("|')?([^"')]+)\1\)/gi,
        (match, quote, styleUnencodedUrlValue) => {
            const trimmedRelativeUnencodedUrl = styleUnencodedUrlValue.trim();
            if (
                !trimmedRelativeUnencodedUrl ||
                !isRelative(trimmedRelativeUnencodedUrl)
            ) {
                return match;
            }
            const normalizedQuote = quote || "";
            return `url(${normalizedQuote}${getResolvedEncodedUrl(trimmedRelativeUnencodedUrl)}${normalizedQuote})`;
        },
    );

    return updatedHtml;
};

// Component to display the thumbnail of one page, with its caption and possibly
// an overflow indicator.
export const PageThumbnail: React.FunctionComponent<{
    page: IPage;
    left: boolean;
    pageLayout: string;
    // PageThumbnail will call this function to provide the client with
    // a callback that the client can call to get the page thumbnail to
    // refresh itself by re-doing the axios call that gets the page content.
    configureReloadCallback: (id: string, callback: () => void) => void;
    onClick?: React.MouseEventHandler<HTMLDivElement>;
    onContextMenu?: React.MouseEventHandler<HTMLDivElement>;
    // Optional bookId for cross-book usage (e.g., in LinkTargetChooser)
    bookId?: string;
    // Folder path for the book when we need to resolve resources directly from disk
    bookFolderPath?: string;
}> = (props) => {
    // The initial content here is a blank page. It will be replaced with
    // the real content when we retrieve it from the server.
    const [content, setContent] = useState(
        `<div class="${props.pageLayout} bloom-page side-${
            props.left ? "left" : "right"
        }"><div class="marginBox">content</div></div>`,
    );
    // This is something of a hack. When the page content needs to be reloaded,
    // an event comes to the parent list. Using the key, it will look up our callback
    // function and call it. This increments the value. A new value in reloadValue
    // causes the useEffect below to run again and request the new page content.
    const [reloadValue, setReloadValue] = useState(1);
    props.configureReloadCallback(props.page.key, () =>
        setReloadValue(reloadValue + 1),
    );
    // Get the actual page content. This is appreciably slow...80ms or so on
    // a fast desktop for a complex page...mainly because of XhtmlToHtml conversion.
    // So we do it lazily after setting up the initial framework of pages.
    const requestPage = useCallback(() => {
        // We don't want a lot of page requests running at the same time.
        // There are various limits on simultaneous requests, including
        // the number of threads in the BloomServer and the number of active
        // requests the browser allows to the same origin. If we use too
        // many, we may starve the main page or the toolbox, which are often
        // loading at the same time. The activePageRequestCount is used
        // to prevent more than four running at the same time, and the
        // pending count helps us estimate how long to wait if we can't do
        // it at once. Both are global across all instances of PageThumbnail.
        // The current value is quite a bit shorter than the time (80ms) that
        // it was taking to generate a complex page on a developer machine.
        // The timeout doesn't cost much, and we don't want to slow things down
        // for simple pages on fast machines.
        if (activePageRequestCount > 3) {
            window.setTimeout(requestPage, pendingPageRequestCount * 5);
            return;
        }
        pendingPageRequestCount--;
        activePageRequestCount++;
        const pageContentRequestEncodedUrl = props.bookId
            ? `pageList/pageContent?id=${props.page.key}&book-id=${encodeURIComponent(props.bookId)}`
            : `pageList/pageContent?id=${props.page.key}`;
        get(
            pageContentRequestEncodedUrl,
            (response) => {
                activePageRequestCount--;
                let htmlContent = response.data.content; // automatically unJsonified?

                // When loading from a different book, we need to fix image URLs to point to that book's folder
                if (props.bookId || props.bookFolderPath) {
                    htmlContent = fixResourceUrls(
                        htmlContent,
                        props.bookId,
                        props.bookFolderPath,
                    );
                }

                setContent(htmlContent);
            },
            (error) => {
                // Handle errors gracefully (e.g., when a page is deleted or moved while
                // requests are in-flight). Just decrement the counter and leave content empty.
                activePageRequestCount--;
            },
        );
    }, [props.bookId, props.bookFolderPath, props.page.key]);

    const reForOverflow = /^[^>]*class="[^"]*pageOverflows/;
    const overflowing = reForOverflow.test(content); // enhance: memo?

    const scrollingWillBeAvailable = props.pageLayout.indexOf("Device") > -1;

    useEffect(() => {
        if (Math.abs(Date.now() - lastPageRequestTime) > 5000) {
            activePageRequestCount = 0; // something weird happened, don't block forever
            pendingPageRequestCount = 0;
        }
        lastPageRequestTime = Date.now();
        pendingPageRequestCount++;
        requestPage();
    }, [reloadValue, requestPage]);
    return (
        <div>
            {props.page.key === "placeholder" || (
                <>
                    <div className={"pageContainer " + props.pageLayout}>
                        <div
                            dangerouslySetInnerHTML={{
                                __html: content,
                            }}
                        />
                        {/* This div overlays the page and intercepts clicks that might otherwise
                        do something within the HTML of the thumbnail (plus sending those clicks
                        to the appropriate handler for a thumbnail click) */}
                        <div
                            className="invisibleThumbnailCover"
                            onClick={props.onClick}
                            onContextMenu={props.onContextMenu}
                        />
                        {overflowing && !scrollingWillBeAvailable && (
                            <div className="pageOverflowsIcon" />
                        )}
                    </div>
                    <div className="thumbnailCaption">{props.page.caption}</div>
                </>
            )}
        </div>
    );
};
