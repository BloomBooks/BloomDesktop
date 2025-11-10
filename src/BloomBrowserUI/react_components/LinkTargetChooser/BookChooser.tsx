import * as React from "react";
import { useRef, useEffect } from "react";
import { css } from "@emotion/react";
import { BookInfoForLinks } from "../BookGridSetup/BookLinkTypes";
import { BookLinkCard } from "../BookGridSetup/BookLinkCard";
import { chooserContainerStyles, itemGap } from "./sharedStyles";
import { chooserButtonPadding } from "./sharedStyles";

const BookChooserComponent: React.FunctionComponent<{
    books: BookInfoForLinks[];
    selectedBook: BookInfoForLinks | null;
    onSelectBook: (book: BookInfoForLinks) => void;
    includeCurrentBook?: boolean;
    disabledBooks?: string[]; // book IDs that should be shown as disabled
}> = (props) => {
    const containerRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (props.selectedBook && containerRef.current) {
            const element = containerRef.current.querySelector(
                `[data-book-id="${props.selectedBook.id}"]`,
            );
            element?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }, [props.selectedBook]);

    const booksToShow = props.includeCurrentBook ? props.books : props.books; // TODO: filter out current book once we have API to identify it

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
                align-content: flex-start;
                padding: ${chooserButtonPadding};
                height: -webkit-fill-available;
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
                        aria-selected={isSelected ? "true" : "false"}
                        aria-disabled={isDisabled ? "true" : undefined}
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
        // Only re-render if the selected book ID changes or the books array changes
        return (
            prevProps.selectedBook?.id === nextProps.selectedBook?.id &&
            prevProps.books === nextProps.books &&
            prevProps.onSelectBook === nextProps.onSelectBook &&
            prevProps.includeCurrentBook === nextProps.includeCurrentBook &&
            prevProps.disabledBooks === nextProps.disabledBooks
        );
    },
);
