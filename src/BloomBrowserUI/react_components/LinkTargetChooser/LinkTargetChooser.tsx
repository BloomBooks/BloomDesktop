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
    const currentUrlProp = props.currentURL;
    const onURLChanged = props.onURLChanged;
    // Parse the currentURL to determine initial state
    const [selectedBookId, setSelectedBookId] = useState<string | null>(null);
    const [selectedPageId, setSelectedPageId] = useState<string | null>(null);
    const [currentURL, setCurrentURL] = useState<string>("");
    const [selectedBook, setSelectedBook] = useState<BookInfoForLinks | null>(
        null,
    );
    const [errorMessage, setErrorMessage] = useState<string>("");
    const [hasAttemptedAutoSelect, setHasAttemptedAutoSelect] =
        useState<boolean>(() => !!currentUrlProp);

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
        const rawUrl = currentUrlProp || "";
        if (rawUrl) {
            setHasAttemptedAutoSelect(true);
        }
        setErrorMessage("");

        const parsed = parseURL(rawUrl);

        if (parsed.urlType === "empty") {
            setCurrentURL("");
            setSelectedBook(null);
            setSelectedBookId(null);
            setSelectedPageId(null);
            setHasAttemptedAutoSelect(false);
            return;
        }

        if (parsed.urlType === "hash") {
            setSelectedBook(null);
            setSelectedBookId(null);
            setSelectedPageId(parsed.pageId);
            setCurrentURL(parsed.parsedUrl);
            setHasAttemptedAutoSelect(true);
            return;
        }

        if (parsed.urlType === "book-path") {
            setSelectedBook(null);
            setSelectedBookId(parsed.bookId);
            setSelectedPageId(parsed.pageId);
            setCurrentURL(parsed.parsedUrl);
            setHasAttemptedAutoSelect(true);
            return;
        }

        // external URL
        setSelectedBook(null);
        setSelectedBookId(null);
        setSelectedPageId(null);
        setCurrentURL(rawUrl);
        onURLChanged?.(rawUrl, false);
        setHasAttemptedAutoSelect(true);
    }, [currentUrlProp, onURLChanged]);

    // Validate and preselect book/page when books load or bookId changes
    useEffect(() => {
        if (!selectedBookId || bookInfoForLinks.length === 0) {
            setSelectedBook(null);
            return;
        }

        const book = bookInfoForLinks.find((b) => b.id === selectedBookId);
        if (!book) {
            const msg = `Book not found: ${selectedBookId}`;
            setErrorMessage(msg);
            setSelectedBook(null);
            onURLChanged?.(currentURL, true);
        } else {
            setSelectedBook(book);
            setErrorMessage("");

            // Validate page if one is selected
            if (selectedPageId !== null) {
                // Optional validation only if page id is numeric
                const numeric = Number(selectedPageId);
                const isNumeric = !isNaN(numeric);
                if (isNumeric) {
                    const pageCount = book.pageLength || 1;
                    if (numeric >= pageCount) {
                        const msg = `Page ${selectedPageId} not found in book "${book.title}"`;
                        setErrorMessage(msg);
                        onURLChanged?.(currentURL, true);
                        return;
                    }
                }
                onURLChanged?.(currentURL, false);
            } else {
                onURLChanged?.(currentURL, false);
            }
        }
    }, [
        selectedBookId,
        bookInfoForLinks,
        selectedPageId,
        currentURL,
        onURLChanged,
    ]);

    const handleBookSelected = useCallback(
        (book: BookInfoForLinks) => {
            setSelectedBook(book);
            setSelectedBookId(book.id);
            setSelectedPageId(null);
            setErrorMessage("");
            setHasAttemptedAutoSelect(true);

            const url = `/book/${book.id}`;
            setCurrentURL(url);
            onURLChanged?.(url, false);
        },
        [onURLChanged],
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
            if (selectedBookId) {
                url =
                    normalizedPageId === "cover"
                        ? `/book/${selectedBookId}`
                        : `/book/${selectedBookId}#${normalizedPageId}`;
            } else {
                url =
                    normalizedPageId === "cover"
                        ? "#cover"
                        : `#${normalizedPageId}`;
            }

            setCurrentURL(url);
            onURLChanged?.(url, false);
        },
        [selectedBookId, onURLChanged],
    );

    const handleURLEditorChanged = useCallback(
        (url: string) => {
            setCurrentURL(url);
            setSelectedBook(null); // Clear book selection
            setSelectedBookId(null);
            setSelectedPageId(null); // Clear page selection
            setErrorMessage("");
            setHasAttemptedAutoSelect(true);

            onURLChanged?.(url, false);
        },
        [onURLChanged],
    );

    useEffect(() => {
        if (
            !currentUrlProp &&
            currentBookId &&
            bookInfoForLinks.length > 0 &&
            !selectedBookId &&
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
        currentUrlProp,
        currentBookId,
        bookInfoForLinks,
        selectedBookId,
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
                !selectedBookId ||
                !actualId ||
                selectedPageId === "cover" ||
                selectedPageId !== actualId
            ) {
                return;
            }

            setSelectedPageId("cover");
            const newUrl = `/book/${selectedBookId}`;
            if (currentURL !== newUrl) {
                setCurrentURL(newUrl);
                onURLChanged?.(newUrl, false);
            }
        },
        [selectedBookId, selectedPageId, currentURL, onURLChanged],
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
                    currentURL={currentURL}
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
                            bookId={selectedBookId ?? undefined}
                            bookFolderPath={selectedBook?.folderPath}
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
