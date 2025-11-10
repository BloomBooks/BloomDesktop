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

        if (!rawUrl) {
            setCurrentURL("");
            setSelectedBook(null);
            setSelectedBookId(null);
            setSelectedPageId(null);
            setHasAttemptedAutoSelect(false);
            return;
        }

        if (rawUrl.startsWith("#")) {
            const pageIdStr = rawUrl.substring(1) || "cover";
            const normalizedPageId =
                pageIdStr === "cover" ? "cover" : pageIdStr;
            setSelectedBook(null);
            setSelectedBookId(null);
            setSelectedPageId(normalizedPageId);
            setCurrentURL(
                normalizedPageId === "cover"
                    ? "#cover"
                    : `#${normalizedPageId}`,
            );
            setHasAttemptedAutoSelect(true);
            return;
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

            setSelectedBook(null);
            setSelectedBookId(bookId);
            setSelectedPageId(normalizedPageId);
            setCurrentURL(
                normalizedPageId === "cover"
                    ? `/book/${bookId}`
                    : `/book/${bookId}#${normalizedPageId}`,
            );
            setHasAttemptedAutoSelect(true);
            return;
        }

        setSelectedBook(null);
        setSelectedBookId(null);
        setSelectedPageId(null);
        setCurrentURL(rawUrl);
        onURLChangedRef.current?.({
            url: rawUrl,
            bookThumbnail: null,
            bookTitle: null,
            hasError: false,
        });
        setHasAttemptedAutoSelect(true);
    }, [props.currentURL]);

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
            setErrorMessage("");
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
            if (pageInfo.isXMatter && !pageInfo.isFrontCover) {
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
            setErrorMessage("");
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
                            includeCurrentBook={true}
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
