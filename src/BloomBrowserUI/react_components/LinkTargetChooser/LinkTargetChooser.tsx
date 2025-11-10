import * as React from "react";
import { useState, useEffect, useMemo, useRef, useCallback } from "react";
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
import { useL10n } from "../l10nHooks";
import { parseURL } from "./urlParser";

export interface LinkTargetInfo {
    url: string;
    bookThumbnail: string | null;
    bookTitle: string | null;
    hasError?: boolean;
}

export const LinkTargetChooser: React.FunctionComponent<{
    currentURL: string;
    onURLChanged?: (info: LinkTargetInfo) => void;
}> = (props) => {
    // Use ref to store the latest onURLChanged callback to avoid dependency issues
    const onURLChangedRef = useRef(props.onURLChanged);
    useEffect(() => {
        onURLChangedRef.current = props.onURLChanged;
    }, [props.onURLChanged]);

    // Parse the currentURL to determine initial state
    const [selectedBookId, setSelectedBookId] = useState<string | null>(null);
    const [selectedPageId, setSelectedPageId] = useState<string | null>(null);
    const [currentURL, setCurrentURL] = useState<string>("");
    const [selectedBook, setSelectedBook] = useState<BookInfoForLinks | null>(
        null,
    );
    const [errorInfo, setErrorInfo] = useState<
        | { type: "bookNotFound"; bookId: string }
        | { type: "pageNotFound"; pageId: string; bookTitle: string }
        | null
    >(null);
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
        const parsed = parseURL(rawUrl);

        // Set hasAttemptedAutoSelect if we have a URL
        if (rawUrl) {
            setHasAttemptedAutoSelect(true);
        }

        // Clear error info initially
        setErrorInfo(null);

        // Set component state based on parsed result
        setSelectedBook(null);
        setSelectedBookId(parsed.bookId);
        setSelectedPageId(parsed.pageId);
        setCurrentURL(parsed.parsedUrl);

        // For empty URLs, reset all state
        if (parsed.urlType === "empty") {
            setHasAttemptedAutoSelect(false);
            return;
        }

        // For external URLs, notify parent immediately
        if (parsed.urlType === "external") {
            setHasAttemptedAutoSelect(true);
            onURLChangedRef.current?.({
                url: parsed.parsedUrl,
                bookThumbnail: null,
                bookTitle: null,
                hasError: false,
            });
        }
    }, [props.currentURL]);

    // Validate and preselect book/page when books load or bookId changes
    useEffect(() => {
        let nextSelectedBook: BookInfoForLinks | null = null;
        let nextError:
            | { type: "bookNotFound"; bookId: string }
            | { type: "pageNotFound"; pageId: string; bookTitle: string }
            | null = null;
        let bookThumbnail: string | null = null;
        let bookTitle: string | null = null;

        if (selectedBookId && bookInfoForLinks.length > 0) {
            const book = bookInfoForLinks.find((b) => b.id === selectedBookId);
            if (!book) {
                nextError = { type: "bookNotFound", bookId: selectedBookId };
            } else {
                nextSelectedBook = book;
                bookThumbnail = book.thumbnail || null;
                bookTitle = book.title || null;

                if (selectedPageId !== null) {
                    const numeric = Number(selectedPageId);
                    const isNumeric = !isNaN(numeric);
                    if (isNumeric) {
                        const pageCount = book.pageLength || 1;
                        if (numeric >= pageCount) {
                            nextError = {
                                type: "pageNotFound",
                                pageId: selectedPageId,
                                bookTitle: book.title || "",
                            };
                        }
                    }
                }
            }
        } else {
            nextSelectedBook = null;
            nextError = null;
        }

        let hasError = nextError !== null;

        if (!selectedBookId) {
            const trimmedUrl = currentURL.trim();
            if (trimmedUrl !== "" && trimmedUrl.startsWith("#")) {
                hasError = true;
            }
        }

        if (selectedBookId && bookInfoForLinks.length === 0) {
            hasError = true;
        }

        setSelectedBook(nextSelectedBook);
        setErrorInfo(nextError);

        onURLChangedRef.current?.({
            url: currentURL,
            bookThumbnail,
            bookTitle,
            hasError,
        });
    }, [selectedBookId, bookInfoForLinks, selectedPageId, currentURL]);

    const notifyParent = useCallback(
        (
            url: string,
            thumbnail: string | null,
            title: string | null,
            hasError: boolean,
        ) => {
            onURLChangedRef.current?.({
                url,
                bookThumbnail: thumbnail,
                bookTitle: title,
                hasError,
            });
        },
        [],
    );

    const handleBookSelected = useCallback(
        (book: BookInfoForLinks) => {
            setSelectedBook(book);
            setSelectedBookId(book.id);
            setSelectedPageId(null);
            setErrorInfo(null);
            setHasAttemptedAutoSelect(true);

            const url = `/book/${book.id}`;
            setCurrentURL(url);
            notifyParent(
                url,
                book.thumbnail || null,
                book.title || null,
                false,
            );
        },
        [notifyParent],
    );

    const handlePageSelected = useCallback(
        (pageInfo: PageInfoForLinks) => {
            // we've already disabled selection of x-matter pages except front cover
            if (pageInfo.isXMatter && !pageInfo.isFrontCover) {
                return;
            }
            const isFrontCover = Boolean(pageInfo.isFrontCover);
            let normalizedPageId: string;
            if (isFrontCover) {
                normalizedPageId = "cover";
            } else if (!pageInfo.pageId || pageInfo.pageId === "cover") {
                normalizedPageId = pageInfo.actualPageId ?? pageInfo.pageId;
            } else {
                normalizedPageId = pageInfo.pageId;
            }

            setSelectedPageId(normalizedPageId);
            setErrorInfo(null);

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
            notifyParent(
                url,
                selectedBook?.thumbnail || null,
                selectedBook?.title || null,
                false,
            );
        },
        [selectedBookId, selectedBook, notifyParent],
    );

    const handleURLEditorChanged = useCallback(
        (url: string) => {
            setCurrentURL(url);
            setSelectedBook(null); // Clear book selection
            setSelectedBookId(null);
            setSelectedPageId(null); // Clear page selection
            setErrorInfo(null);
            setHasAttemptedAutoSelect(true);

            notifyParent(url, null, null, false);
        },
        [notifyParent],
    );

    useEffect(() => {
        if (
            !props.currentURL &&
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
        props.currentURL,
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
                notifyParent(
                    newUrl,
                    selectedBook?.thumbnail || null,
                    selectedBook?.title || null,
                    false,
                );
            }
        },
        [
            selectedBookId,
            selectedPageId,
            currentURL,
            notifyParent,
            selectedBook,
        ],
    );

    const booksHeading = useL10n(
        "Books in this Collection",
        "LinkTargetChooser.BookList.Heading",
    );
    const pagesHeading = useL10n(
        "Pages in the selected book",
        "LinkTargetChooser.PageList.Heading",
    );
    const bookNotFoundMessage = useL10n(
        "Book not found: {0}",
        "LinkTargetChooser.SelectionError.BookNotFound",
        undefined,
        errorInfo?.type === "bookNotFound" ? errorInfo.bookId : "",
    );
    const pageNotFoundMessage = useL10n(
        'Page {0} not found in book "{1}"',
        "LinkTargetChooser.SelectionError.PageNotFound",
        undefined,
        errorInfo?.type === "pageNotFound" ? errorInfo.pageId : "",
        errorInfo?.type === "pageNotFound" ? errorInfo.bookTitle : "",
    );

    const errorMessage = useMemo(() => {
        if (!errorInfo) {
            return "";
        }
        if (errorInfo.type === "bookNotFound") {
            return bookNotFoundMessage;
        }
        if (errorInfo.type === "pageNotFound") {
            return pageNotFoundMessage;
        }
        return "";
    }, [errorInfo, bookNotFoundMessage, pageNotFoundMessage]);

    return (
        <Box
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                gap: 10px;
            `}
        >
            {" "}
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
                    <Typography css={headingStyle}>{booksHeading}</Typography>
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
                            excludeBookBeingEdited={false}
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
                    <Typography css={headingStyle}>{pagesHeading}</Typography>
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
