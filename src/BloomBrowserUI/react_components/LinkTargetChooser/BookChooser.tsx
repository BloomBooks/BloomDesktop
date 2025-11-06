import * as React from "react";
import { useRef, useEffect } from "react";
import { css } from "@emotion/react";
import { BookInfoForLinks } from "../BookGridSetup/BookLinkTypes";
import { BookLinkCard } from "../BookGridSetup/BookLinkCard";
import { chooserContainerStyles, itemGap } from "./sharedStyles";
import { chooserButtonPadding } from "./sharedStyles";

export const BookChooser: React.FunctionComponent<{
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

    return (
        <div
            ref={containerRef}
            css={css`
                ${chooserContainerStyles}
                display: flex;
                flex-wrap: wrap;
                gap: ${itemGap};
                height: 100%;
                align-content: flex-start;
                padding-left: 2px;
                padding: ${chooserButtonPadding};
            `}
        >
            {booksToShow.map((book) => {
                const isDisabled = props.disabledBooks?.includes(book.id);
                return (
                    <div
                        key={book.id}
                        data-book-id={book.id}
                        css={css`
                            ${isDisabled
                                ? `opacity: 0.5; pointer-events: none;`
                                : ""}
                        `}
                    >
                        <BookLinkCard
                            link={{ book: book }}
                            selected={props.selectedBook?.id === book.id}
                            onClick={() => {
                                if (!isDisabled) {
                                    props.onSelectBook(book);
                                }
                            }}
                        />
                    </div>
                );
            })}
        </div>
    );
};
