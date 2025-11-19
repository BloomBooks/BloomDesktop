import * as React from "react";
import { BookInfoForLinks } from "./BookLinkTypes";
import { BookLinkCard } from "./BookLinkCard";
import { useRef, useEffect } from "react";
import { bookGridContainerStyles } from "./sharedStyles";

interface BookSourcesListProps {
    books: BookInfoForLinks[];
    selectedBook: BookInfoForLinks | null;
    onSelectBook: (book: BookInfoForLinks) => void;
    disabledBookIds?: string[]; // IDs of books that should be shown as disabled
}

export const BookSourcesList: React.FC<BookSourcesListProps> = ({
    books,
    selectedBook,
    onSelectBook,
    disabledBookIds = [],
}) => {
    const containerRef = useRef<HTMLDivElement>(null);
    // Auto-scroll to keep the selected book visible when selection changes.
    useEffect(() => {
        if (selectedBook && containerRef.current) {
            // book id are guids, so no need to escape special characters
            const element = containerRef.current.querySelector(
                `[data-book-id="${selectedBook.id}"]`,
            );
            element?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }, [selectedBook]);

    // Convert disabled book IDs array to a Set for O(1) lookup performance.
    // This might matter when rendering large collections with many books.
    const disabledSet = React.useMemo(
        () => new Set(disabledBookIds),
        [disabledBookIds],
    );

    return (
        <div ref={containerRef} css={bookGridContainerStyles}>
            {books.map((book) => {
                const isDisabled = disabledSet.has(book.id);
                return (
                    <div
                        key={book.id}
                        data-book-id={book.id}
                        data-testid={`source-book-${book.id}`}
                    >
                        <BookLinkCard
                            link={{ book: book }}
                            selected={selectedBook?.id === book.id}
                            onClick={
                                isDisabled
                                    ? undefined
                                    : () => onSelectBook(book)
                            }
                            preferFolderName={true}
                            disabled={isDisabled}
                        />
                    </div>
                );
            })}
        </div>
    );
};
