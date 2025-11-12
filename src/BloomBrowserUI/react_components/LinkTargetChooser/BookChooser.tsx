import * as React from "react";
import { useRef, useEffect } from "react";
import { css } from "@emotion/react";
import { BookInfoForLinks } from "../BookGridSetup/BookLinkTypes";
import { BookLinkCard } from "../BookGridSetup/BookLinkCard";
import {
    chooserContainerStyles,
    chooserButtonPadding,
    itemGap,
} from "./sharedStyles";

// Component that shows all the books in the collection as buttons, and allows selecting one
const BookChooserComponent: React.FunctionComponent<{
    books: BookInfoForLinks[];
    selectedBook: BookInfoForLinks | null;
    onSelectBook: (book: BookInfoForLinks) => void;
}> = (props) => {
    // used for scrolling to selected book
    const bookGridRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (props.selectedBook && bookGridRef.current) {
            const element = bookGridRef.current.querySelector(
                `[data-book-id="${props.selectedBook.id}"]`,
            );
            element?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }, [props.selectedBook]);

    const handleBookClick = (book: BookInfoForLinks) => {
        props.onSelectBook(book);
    };

    return (
        <div
            ref={bookGridRef}
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
            {props.books.map((book) => {
                const isSelected = props.selectedBook?.id === book.id;
                const classNames = ["link-target-book"];
                if (isSelected) {
                    classNames.push("link-target-book--selected");
                }
                return (
                    <div
                        key={book.id}
                        data-book-id={book.id}
                        className={classNames.join(" ")}
                        data-selected={isSelected ? "true" : undefined}
                    >
                        <BookLinkCard
                            link={{ book: book }}
                            selected={isSelected}
                            onClick={() => handleBookClick(book)}
                        />
                    </div>
                );
            })}
        </div>
    );
};

// React.memo keeps the card grid responsive by skipping expensive re-renders unless meaningful data changes.
export const BookChooser = React.memo(
    BookChooserComponent,
    (prevProps, nextProps) => {
        // Return true to prevent re-render when selectedBook, books, and onSelectBook are unchanged
        return (
            prevProps.selectedBook?.id === nextProps.selectedBook?.id &&
            prevProps.books === nextProps.books &&
            prevProps.onSelectBook === nextProps.onSelectBook
        );
    },
);
