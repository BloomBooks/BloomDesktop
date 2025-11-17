import * as React from "react";
import { useState, useEffect, useCallback } from "react";

import "../../lib/errorHandler";
import { get } from "../../utils/bloomApi";
import { IPage } from "./pageThumbnailList";

// Global information across all PageThumbnails...see comments in requestPage()
let lastPageRequestTime = 0;
let activePageRequestCount = 0;
let pendingPageRequestCount = 0;

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
            ? `pageList/pageContent?page-id=${props.page.key}&book-id=${encodeURIComponent(props.bookId)}`
            : `pageList/pageContent?page-id=${props.page.key}`;
        get(
            pageContentRequestEncodedUrl,
            (response) => {
                activePageRequestCount--;
                let htmlContent = response.data.content; // axios already parsed the JSON response

                // When loading from a different book, we need to fix image URLs to point to that book's API
                if (props.bookId) {
                    htmlContent = makeRelativeUrlsAbsolute(
                        htmlContent,
                        props.bookId,
                    );
                }

                setContent(htmlContent);
            },
            (_error) => {
                // Handle errors gracefully (e.g., when a page is deleted or moved while
                // requests are in-flight). Just decrement the counter and leave content empty.
                activePageRequestCount--;
            },
        );
    }, [props.bookId, props.page.key]);

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

// This component is used not only for the current book, but also
// for displaying pages from other books when choosing link targets.
// In the latter case, we need to fix up resource URLs so they load through the book-file API.
// For example, if we have <img src="images/pic.png">, we need to change that to
// something like <img src="/bloom/api/collections/bookFile?book-id=...&file=images%2Fpic.png">.
// Callers without a bookId should skip this rewriting step entirely.
function makeRelativeUrlsAbsolute(html: string, bookId: string): string {
    if (!html) {
        return "";
    }

    // Treat anything that already looks absolute as untouched.
    const absolutePattern = /^(?:[a-z][a-z\d+\-.]*:|\/|#)/i;

    // Resolve relative URLs through the book file API so assets come from the requested book.
    const resolveRelativeUrl = (rawPath: string): string => {
        try {
            const resolved = new URL(rawPath, "http://bloom.invalid/");
            let normalizedPath = resolved.pathname.replace(/^\/+/, "");
            try {
                normalizedPath = decodeURIComponent(normalizedPath);
            } catch {
                // keep normalizedPath as-is if decoding fails
            }

            // Include query string in the file parameter (e.g., image.jpg?thumbnail=1)
            // because the server provides file references with query strings as part of the path.
            let fileParam = normalizedPath;
            if (resolved.search) {
                fileParam += resolved.search;
            }

            const params = new URLSearchParams();
            params.set("book-id", bookId);
            params.set("file", fileParam);

            return `/bloom/api/collections/bookFile?${params.toString()}${resolved.hash}`;
        } catch {
            return rawPath;
        }
    };

    // Skip strings that are empty or already absolute so we do not mangle them.
    const shouldSkip = (candidateUrl: string): boolean => {
        const trimmed = candidateUrl.trim();
        return trimmed.length === 0 || absolutePattern.test(trimmed);
    };

    // Rewrite src/href attributes in-line so thumbnails fetch the right assets.
    const replaceAttributeUrls = (inputHtml: string): string =>
        inputHtml.replace(
            /(src|href)=("|')([^"']*?)\2/gi,
            (match, attrName, quote, rawValue) => {
                if (shouldSkip(rawValue)) {
                    return match;
                }

                return `${attrName}=${quote}${resolveRelativeUrl(
                    rawValue,
                )}${quote}`;
            },
        );

    // Handle inline CSS url() references the same way.
    const replaceStyleUrls = (inputHtml: string): string =>
        inputHtml.replace(
            /url\(("|')?([^"')]+)\1\)/gi,
            (match, quote, rawValue) => {
                const trimmedValue = rawValue.trim();
                if (shouldSkip(trimmedValue)) {
                    return match;
                }

                const normalizedQuote = quote || "";
                return `url(${normalizedQuote}${resolveRelativeUrl(
                    trimmedValue,
                )}${normalizedQuote})`;
            },
        );

    return replaceStyleUrls(replaceAttributeUrls(html));
}
