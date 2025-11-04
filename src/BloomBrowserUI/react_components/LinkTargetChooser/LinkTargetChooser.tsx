import * as React from "react";
import { useState, useEffect, useMemo, useRef } from "react";
import { css } from "@emotion/react";
import { Box, Typography } from "@mui/material";
import {
    BookInfoForLinks,
    PageInfoForLinks,
} from "../../bookEdit/bookLinkSetup/BookLinkTypes";
import { URLEditor } from "./URLEditor";
import { BookList } from "./BookList";
import { PageList } from "./PageList";
import { useWatchApiData } from "../../utils/bloomApi";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";

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
    const [errorMessage, setErrorMessage] = useState<string>("");

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
                thumbnail:
                    extendedBook.thumbnail ??
                    `/bloom/api/collections/book/thumbnail?book-id=${book.id}`,
                pageLength,
            } as BookInfoForLinks;
        });
    }, [allBooks]);

    useEffect(() => {
        // Parse the incoming URL to set initial state
        const url = props.currentURL;
        setCurrentURL(url);

        if (!url) {
            return;
        }

        // Check if it's a bloom link (# or /book/)
        if (url.startsWith("#")) {
            // Page link within current book: #PAGEID or #cover
            const pageIdStr = url.substring(1);
            if (pageIdStr === "cover") {
                setSelectedPageId("cover");
            } else {
                // accept any string id; keep numeric ids as strings too
                setSelectedPageId(pageIdStr);
            }
        } else if (url.startsWith("/book/")) {
            // Book link: /book/BOOKID or /book/BOOKID#PAGEID
            const hashIndex = url.indexOf("#");
            if (hashIndex === -1) {
                // Just book link
                const bookId = url.substring(6); // length of "/book/"
                setSelectedBookId(bookId);
            } else {
                // Book and page
                const bookId = url.substring(6, hashIndex);
                const pageIdStr = url.substring(hashIndex + 1);
                setSelectedBookId(bookId);
                if (pageIdStr === "cover") {
                    setSelectedPageId("cover");
                } else {
                    setSelectedPageId(pageIdStr);
                }
            }
        } else {
            // External URL - notify parent immediately
            onURLChangedRef.current?.({
                url,
                bookThumbnail: null,
                bookTitle: null,
                hasError: false,
            });
        }
    }, [props.currentURL]);

    // Validate and preselect book/page when books load or bookId changes
    useEffect(() => {
        if (!selectedBookId || bookInfoForLinks.length === 0) {
            return;
        }

        const book = bookInfoForLinks.find((b) => b.id === selectedBookId);
        if (!book) {
            const msg = `Book not found: ${selectedBookId}`;
            setErrorMessage(msg);
            setSelectedBook(null);
            onURLChangedRef.current?.({
                url: currentURL,
                bookThumbnail: null,
                bookTitle: null,
                hasError: true,
            });
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
                        onURLChangedRef.current?.({
                            url: currentURL,
                            bookThumbnail: book.thumbnail || null,
                            bookTitle: book.title || null,
                            hasError: true,
                        });
                        return;
                    }
                }
                onURLChangedRef.current?.({
                    url: currentURL,
                    bookThumbnail: book.thumbnail || null,
                    bookTitle: book.title || null,
                    hasError: false,
                });
            } else {
                onURLChangedRef.current?.({
                    url: currentURL,
                    bookThumbnail: book.thumbnail || null,
                    bookTitle: book.title || null,
                    hasError: false,
                });
            }
        }
    }, [selectedBookId, bookInfoForLinks, selectedPageId, currentURL]);

    const notifyParent = (
        url: string,
        thumbnail: string | null,
        title: string | null,
        hasError: boolean,
    ) => {
        props.onURLChanged?.({
            url,
            bookThumbnail: thumbnail,
            bookTitle: title,
            hasError,
        });
    };

    const handleBookSelected = (book: BookInfoForLinks) => {
        setSelectedBook(book);
        setSelectedBookId(book.id);
        setSelectedPageId(null); // Clear page selection when book changes
        setErrorMessage("");

        // Build URL and update URL box
        const url = `/book/${book.id}`;
        setCurrentURL(url);
        notifyParent(url, book.thumbnail || null, book.title || null, false);
    };

    const handlePageSelected = (pageInfo: PageInfoForLinks) => {
        setSelectedPageId(pageInfo.pageId);
        setErrorMessage("");

        // Build URL and update URL box
        let url: string;
        if (selectedBookId) {
            url = `/book/${selectedBookId}#${pageInfo.pageId === "cover" ? "cover" : pageInfo.pageId}`;
        } else {
            url = `#${pageInfo.pageId === "cover" ? "cover" : pageInfo.pageId}`;
        }

        setCurrentURL(url);
        notifyParent(
            url,
            selectedBook?.thumbnail || null,
            selectedBook?.title || null,
            false,
        );
    };

    const handleURLEditorChanged = (url: string) => {
        setCurrentURL(url);
        setSelectedBook(null); // Clear book selection
        setSelectedBookId(null);
        setSelectedPageId(null); // Clear page selection
        setErrorMessage("");

        notifyParent(url, null, null, false);
    };

    return (
        <Box
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                gap: 10px;
            `}
        >
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
                        border: 1px solid #ccc;
                        overflow: hidden;
                    `}
                >
                    <BookList
                        books={bookInfoForLinks}
                        selectedBook={selectedBook}
                        onSelectBook={handleBookSelected}
                        includeCurrentBook={true}
                    />
                </Box>

                {/* Right: PageList */}
                <Box
                    css={css`
                        flex: 1;
                        border: 1px solid #ccc;
                        overflow: hidden;
                    `}
                >
                    <PageList
                        selectedBook={selectedBook}
                        selectedPageId={selectedPageId}
                        onSelectPage={handlePageSelected}
                    />
                </Box>
            </Box>

            {/* Bottom: URLEditor */}
            <Box
                css={css`
                    border: 1px solid #ccc;
                    padding: 10px;
                `}
            >
                <URLEditor
                    currentURL={currentURL}
                    onChange={handleURLEditorChanged}
                />
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
