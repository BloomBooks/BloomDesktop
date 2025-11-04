import * as React from "react";
import { useRef, useEffect } from "react";
import { css } from "@emotion/react";
import { BookInfoForLinks } from "../../bookEdit/bookLinkSetup/BookLinkTypes";
import { LinkCard } from "../../bookEdit/bookLinkSetup/LinkCard";

export const BookList: React.FunctionComponent<{
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
                display: flex;
                flex-wrap: wrap;
                gap: 8px;
                height: 100%;
                align-content: flex-start;
                padding-left: 2px;
                overflow-y: scroll;
                background-color: lightgray;
                padding: 10px;
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
                        <LinkCard
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
