import * as React from "react";
import { useState, useEffect, useMemo, useCallback } from "react";
import { css } from "@emotion/react";
import { Box, Typography } from "@mui/material";
import {
    BookInfoForLinks,
    PageInfoForLinks,
} from "../BookGridSetup/BookLinkTypes";
import { URLEditor } from "./URLEditor";
import { BookChooser } from "./BookChooser";
import { PageChooser } from "./PageChooser";
import { useWatchApiData, useApiString } from "../../utils/bloomApi";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";
import { headingStyle } from "./sharedStyles";
import { parseURL } from "./urlParser";

export interface LinkTargetInfo {
    url: string;
    bookThumbnail: string | null;
    bookTitle: string | null;
    hasError?: boolean;
}

// This component lets the user specify or create a url. The url can be
// * an external url
// * a book in the collection
// * a page in a book in the collection
// * a "back in history" instruction
export const LinkTargetChooser: React.FunctionComponent<{
    currentURL: string;
    onURLChanged?: (url: string, hasError: boolean) => void;
}> = (props) => {
    const [selectedPageId, setSelectedPageId] = useState<string | null>(null);
    const [selectedBook, setSelectedBook] = useState<BookInfoForLinks | null>(
        null,
    );
    const [errorMessage, setErrorMessage] = useState<string>("");
    const [hasAttemptedAutoSelect, setHasAttemptedAutoSelect] =
        useState<boolean>(() => !!props.currentURL);

    // Get the current book ID so we can select it by default
    const currentBookId = useApiString("editView/currentBookId", "");

    // Fetch books from the API
    const allBooks = useWatchApiData<Array<IBookInfo>>(
        `collections/books?realTitle=false`, // happy with the folder name (i.e. if they renamed in the collection tab, that's what we show)
        [],
        "editableCollectionList",
        "unused",
    );
    const bookInfoForLinks = useMemo(() => {
        return allBooks.map((book) => {
            const extendedBook = book as IBookInfo & {
                pageLength?: number;
                pageCount?: number;
                thumbnail?: string;
            };
            const pageLength =
                extendedBook.pageLength ?? extendedBook.pageCount ?? undefined;
            return {
                id: book.id,
                title: book.title,
                folderName: book.folderName,
                folderPath: book.folderPath,
                thumbnail:
                    extendedBook.thumbnail ??
                    `/bloom/api/collections/book/thumbnail?book-id=${book.id}`,
                pageLength,
            } as BookInfoForLinks;
        });
    }, [allBooks]);

    useEffect(() => {
        const rawUrl = props.currentURL || "";
        if (rawUrl) {
            setHasAttemptedAutoSelect(true);
        }
        setErrorMessage("");

        const parsed = parseURL(rawUrl);

        if (parsed.urlType === "empty") {
            setSelectedBook(null);
            setSelectedPageId(null);
            setHasAttemptedAutoSelect(false);
            return;
        }

        if (parsed.urlType === "hash") {
            // Hash-only (#the-page-id) URLs refer to pages in the book currently being edited (or played, during playback).
            // Auto-select the current book if available, but keep the #pageid format.
            if (currentBookId && bookInfoForLinks.length > 0) {
                const currentBook = bookInfoForLinks.find(
                    (b) => b.id === currentBookId,
                );
                if (currentBook) {
                    setSelectedBook(currentBook);
                    setSelectedPageId(parsed.pageId);
                    setHasAttemptedAutoSelect(true);
                    // keep the #pageid format.
                    props.onURLChanged?.(parsed.parsedUrl, false);
                    return;
                }
            }
            // No current book available, keep the #pageid format.
            setSelectedBook(null);
            setSelectedPageId(parsed.pageId);
            setHasAttemptedAutoSelect(true);
            return;
        }

        if (parsed.urlType === "book-path") {
            const book = bookInfoForLinks.find((b) => b.id === parsed.bookId);
            setSelectedBook(book || null);
            setSelectedPageId(parsed.pageId);
            setHasAttemptedAutoSelect(true);
            if (!book) {
                const msg = `Book not found: ${parsed.bookId}`;
                setErrorMessage(msg);
                props.onURLChanged?.(parsed.parsedUrl, true);
            } else {
                // Validate page if one is selected
                if (parsed.pageId !== null) {
                    const numeric = Number(parsed.pageId);
                    const isNumeric = !isNaN(numeric);
                    if (isNumeric) {
                        const pageCount = book.pageLength || 1;
                        if (numeric >= pageCount) {
                            const msg = `Page ${parsed.pageId} not found in book "${book.title}"`;
                            setErrorMessage(msg);
                            props.onURLChanged?.(parsed.parsedUrl, true);
                            return;
                        }
                    }
                }
                // Simplify to keep the #pageid format if this is the current book
                if (currentBookId && parsed.bookId === currentBookId) {
                    const simplifiedUrl =
                        parsed.pageId === "cover"
                            ? "#cover"
                            : `#${parsed.pageId}`;
                    props.onURLChanged?.(simplifiedUrl, false);
                } else {
                    props.onURLChanged?.(parsed.parsedUrl, false);
                }
            }
            return;
        }

        // external URL
        setSelectedBook(null);
        setSelectedPageId(null);
        props.onURLChanged?.(rawUrl, false);
        setHasAttemptedAutoSelect(true);
    }, [props.currentURL, props.onURLChanged, bookInfoForLinks, currentBookId]);
    const handleBookSelected = useCallback(
        (book: BookInfoForLinks) => {
            setSelectedBook(book);
            setSelectedPageId(null);
            setErrorMessage("");
            setHasAttemptedAutoSelect(true);

            // Use #pageid-only format if this is the current book
            const url =
                currentBookId && book.id === currentBookId
                    ? "#cover"
                    : `/book/${book.id}`;
            props.onURLChanged?.(url, false);
        },
        [props, currentBookId],
    );

    const handlePageSelected = useCallback(
        (pageInfo: PageInfoForLinks) => {
            if (pageInfo.disabled) {
                return;
            }
            const isFrontCover = Boolean(pageInfo.isFrontCover);
            const normalizedPageId = isFrontCover
                ? "cover"
                : !pageInfo.pageId || pageInfo.pageId === "cover"
                  ? (pageInfo.actualPageId ?? pageInfo.pageId)
                  : pageInfo.pageId;

            setSelectedPageId(normalizedPageId);
            setErrorMessage("");

            // Build URL and update URL box
            let url: string;
            if (selectedBook?.id) {
                // Use #pageid-only format if this is the current book
                if (currentBookId && selectedBook.id === currentBookId) {
                    url =
                        normalizedPageId === "cover"
                            ? "#cover"
                            : `#${normalizedPageId}`;
                } else {
                    url =
                        normalizedPageId === "cover"
                            ? `/book/${selectedBook.id}`
                            : `/book/${selectedBook.id}#${normalizedPageId}`;
                }
            } else {
                url =
                    normalizedPageId === "cover"
                        ? "#cover"
                        : `#${normalizedPageId}`;
            }

            props.onURLChanged?.(url, false);
        },
        [props, selectedBook, currentBookId],
    );

    const handleURLEditorChanged = useCallback(
        (url: string) => {
            setSelectedBook(null); // Clear book selection
            setSelectedPageId(null); // Clear page selection
            setErrorMessage("");
            setHasAttemptedAutoSelect(true);

            props.onURLChanged?.(url, false);
        },
        [props],
    );

    useEffect(() => {
        if (
            !props.currentURL &&
            currentBookId &&
            bookInfoForLinks.length > 0 &&
            !selectedBook &&
            !hasAttemptedAutoSelect
        ) {
            const currentBook = bookInfoForLinks.find(
                (b) => b.id === currentBookId,
            );
            if (currentBook) {
                setHasAttemptedAutoSelect(true);
                handleBookSelected(currentBook);
            }
        }
    }, [
        props,
        currentBookId,
        bookInfoForLinks,
        selectedBook,
        hasAttemptedAutoSelect,
        handleBookSelected,
    ]);

    const handlePagesLoaded = useCallback(
        (pages: PageInfoForLinks[]) => {
            if (pages.length === 0) {
                return;
            }

            const frontCoverInfo = pages[0];
            const actualId =
                frontCoverInfo.actualPageId ?? frontCoverInfo.pageId;

            if (
                !selectedBook?.id ||
                !actualId ||
                selectedPageId === "cover" ||
                selectedPageId !== actualId
            ) {
                return;
            }

            setSelectedPageId("cover");
            const newUrl = `/book/${selectedBook.id}`;
            if (props.currentURL !== newUrl) {
                props.onURLChanged?.(newUrl, false);
            }
        },
        [props, selectedBook, selectedPageId],
    );

    return (
        <Box
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                gap: 10px;
            `}
        >
            <Box css={css``}>
                <URLEditor
                    currentURL={props.currentURL}
                    onChange={handleURLEditorChanged}
                />
            </Box>

            <Box
                css={css`
                    display: flex;
                    flex: 1;
                    gap: 10px;
                    min-height: 0;
                `}
            >
                {/* Left: BookList */}
                <Box
                    css={css`
                        flex: 1;
                        display: flex;
                        flex-direction: column;
                    `}
                >
                    <Typography css={headingStyle}>
                        Books in this Collection
                    </Typography>
                    <Box
                        css={css`
                            flex: 1;
                            border: 1px solid #ccc;
                            overflow: hidden;
                        `}
                    >
                        <BookChooser
                            books={bookInfoForLinks}
                            selectedBook={selectedBook}
                            onSelectBook={handleBookSelected}
                        />
                    </Box>
                </Box>

                {/* Right: PageList */}
                <Box
                    css={css`
                        flex: 1;
                        display: flex;
                        flex-direction: column;
                    `}
                >
                    <Typography css={headingStyle}>
                        Pages in the selected book
                    </Typography>
                    <Box
                        css={css`
                            flex: 1;
                            border: 1px solid #ccc;
                            overflow: hidden;
                        `}
                    >
                        <PageChooser
                            bookId={selectedBook?.id}
                            selectedPageId={selectedPageId ?? undefined}
                            onSelectPage={handlePageSelected}
                            onPagesLoaded={handlePagesLoaded}
                        />
                    </Box>
                </Box>
            </Box>

            {/* Error message display */}
            {errorMessage && (
                <Box
                    css={css`
                        background-color: #ffebee;
                        color: #c62828;
                        padding: 10px;
                        border: 1px solid #ef5350;
                        border-radius: 4px;
                        margin-top: 10px;
                    `}
                    data-testid="error-message"
                >
                    <Typography variant="body2">{errorMessage}</Typography>
                </Box>
            )}
        </Box>
    );
};
