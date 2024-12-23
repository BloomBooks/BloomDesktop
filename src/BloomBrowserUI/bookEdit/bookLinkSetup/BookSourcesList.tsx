import * as React from "react";
import { BookInfoForLinks } from "./BookLinkTypes";
import { LinkCard } from "./LinkCard";
import { useRef, useEffect } from "react";
import { css } from "@emotion/react";

interface BookSourcesListProps {
    books: BookInfoForLinks[];
    selectedBook: BookInfoForLinks | null;
    onSelectBook: (book: BookInfoForLinks) => void;
}

export const BookSourcesList: React.FC<BookSourcesListProps> = ({
    books,
    selectedBook,
    onSelectBook
}) => {
    const containerRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (selectedBook && containerRef.current) {
            const element = containerRef.current.querySelector(
                `[data-book-id="${selectedBook.id}"]`
            );
            element?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }, [selectedBook]);

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
                overflow-y: scroll; // always show this
                background-color: lightgray;
                padding: 10px;
            `}
        >
            {books.map(book => (
                <div key={book.id} data-book-id={book.id}>
                    <LinkCard
                        link={{ book: book }}
                        selected={selectedBook?.id === book.id}
                        onClick={() => onSelectBook(book)}
                    />
                </div>
            ))}
        </div>
    );
};
