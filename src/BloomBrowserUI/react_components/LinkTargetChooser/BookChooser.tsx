import * as React from "react";
import { useRef, useEffect, useMemo } from "react";
import { css } from "@emotion/react";
import { BookInfoForLinks } from "../BookGridSetup/BookLinkTypes";
import { BookLinkCard } from "../BookGridSetup/BookLinkCard";
import {
    chooserContainerStyles,
    chooserButtonPadding,
    itemGap,
} from "./sharedStyles";
import { useApiString } from "../../utils/bloomApi";

const BookChooserComponent: React.FunctionComponent<{
    books: BookInfoForLinks[];
    selectedBook: BookInfoForLinks | null;
    onSelectBook: (book: BookInfoForLinks) => void;
    excludeBookBeingEdited?: boolean;
    disabledBooks?: string[]; // book IDs that should be shown as disabled
}> = (props) => {
    const containerRef = useRef<HTMLDivElement>(null);

    // Get the current book ID to filter it out if needed
    const currentBookId = useApiString("editView/currentBookId", "");

    useEffect(() => {
        if (props.selectedBook && containerRef.current) {
            const element = containerRef.current.querySelector(
                `[data-book-id="${props.selectedBook.id}"]`,
            );
            element?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }, [props.selectedBook]);

    const booksToShow = useMemo(() => {
        if (!props.excludeBookBeingEdited) {
            return props.books;
        }
        // Filter out current book if we have a valid current book ID
        if (currentBookId) {
            return props.books.filter((book) => book.id !== currentBookId);
        }
        // If no current book ID yet, return all books
        return props.books;
    }, [props.excludeBookBeingEdited, props.books, currentBookId]);

    const handleBookClick = (book: BookInfoForLinks, isDisabled: boolean) => {
        if (!isDisabled) {
            props.onSelectBook(book);
        }
    };

    return (
        <div
            ref={containerRef}
            css={css`
                ${chooserContainerStyles}
                display: flex;
                flex-wrap: wrap;
                gap: ${itemGap};
                align-content: flex-start;
                padding: ${chooserButtonPadding};
                // we only run in modern chromium
                height: -webkit-fill-available;
            `}
        >
            {booksToShow.map((book) => {
                const isDisabled = props.disabledBooks?.includes(book.id);
                const isSelected = props.selectedBook?.id === book.id;
                const classNames = ["link-target-book"];
                if (isSelected) {
                    classNames.push("link-target-book--selected");
                }
                if (isDisabled) {
                    classNames.push("link-target-book--disabled");
                }
                return (
                    <div
                        key={book.id}
                        data-book-id={book.id}
                        className={classNames.join(" ")}
                        data-selected={isSelected ? "true" : undefined}
                        data-disabled={isDisabled ? "true" : undefined}
                        css={css`
                            ${isDisabled ? `opacity: 0.5;` : ""}
                        `}
                    >
                        <BookLinkCard
                            link={{ book: book }}
                            selected={isSelected}
                            onClick={() => handleBookClick(book, !!isDisabled)}
                        />
                    </div>
                );
            })}
        </div>
    );
};

export const BookChooser = React.memo(
    BookChooserComponent,
    (prevProps, nextProps) => {
        // Return true to prevent re-render when selectedBook, books, onSelectBook, excludeBookBeingEdited, and disabledBooks are unchanged
        return (
            prevProps.selectedBook?.id === nextProps.selectedBook?.id &&
            prevProps.books === nextProps.books &&
            prevProps.onSelectBook === nextProps.onSelectBook &&
            prevProps.excludeBookBeingEdited ===
                nextProps.excludeBookBeingEdited &&
            prevProps.disabledBooks === nextProps.disabledBooks
        );
    },
);
