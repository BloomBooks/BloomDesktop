import * as React from "react";
import { useState } from "react";
import { Box, Typography } from "@mui/material";
import { BookInfoForLinks, Link } from "./BookLinkTypes";

import { BookSourcesList } from "./BookSourcesList";
import { BookTargetList } from "./BookTargetList";

import { useL10n } from "../l10nHooks";
import { postJson } from "../../utils/bloomApi";
import BloomButton from "../bloomButton";

const BookGridSetup: React.FC<{
    sourceBooks: BookInfoForLinks[];

    links: Link[]; // the set of links that are currently in the grid
    onLinksChanged: ((links: Link[]) => void) | string; // function for normal use, string URL for testing
}> = (props) => {
    const [selectedSource, setSelectedSource] =
        useState<BookInfoForLinks | null>(null);
    const [targets, setTargets] = useState<Link[]>(props.links); // initialize with links prop

    React.useEffect(() => {
        setTargets(props.links);
    }, [props.links]);

    // Helper to call onLinksChanged, handling both function and string (test URL) cases.
    // This dual-mode approach allows the component to be tested via HTTP endpoints
    // while maintaining normal callback behavior in production.
    const notifyLinksChanged = (links: Link[]) => {
        if (typeof props.onLinksChanged === "string") {
            // For testing: POST to the URL
            void postJson(props.onLinksChanged, links);
        } else {
            // Normal use: call the function
            props.onLinksChanged(links);
        }
    };

    const handleItemSelect = (item: BookInfoForLinks) => {
        setSelectedSource(item);
    };

    const handleAddBookLink = () => {
        if (
            selectedSource &&
            !targets.some((item) => item.book.id === selectedSource.id)
        ) {
            const newTargets = [...targets, { book: selectedSource }];
            setTargets(newTargets);
            notifyLinksChanged(newTargets);

            // Auto-select the next book to improve workflow: after adding a book,
            // we want to position the selection for the next likely action.
            const currentIndex = props.sourceBooks.findIndex(
                (book) => book.id === selectedSource.id,
            );
            // Filter out books that are already in the targets list
            const availableBooks = props.sourceBooks.filter(
                (book) =>
                    !newTargets.some((added) => added.book.id === book.id) &&
                    book.id !== selectedSource.id,
            );

            // Prefer the next book in the original sequence (to maintain collection order),
            // but fall back to the first available book if we're at the end.
            const nextBook =
                availableBooks.find(
                    (book) => props.sourceBooks.indexOf(book) > currentIndex,
                ) ||
                availableBooks[0] ||
                null;

            setSelectedSource(nextBook);
        }
    };

    const handleAddAllBooks = () => {
        // Find all books that aren't already in targets
        const availableBooks = props.sourceBooks.filter(
            (book) => !targets.some((target) => target.book.id === book.id),
        );

        // Add all available books as links
        const newLinks = availableBooks.map((book) => ({ book }));
        const newTargets = [...targets, ...newLinks];
        setTargets(newTargets);
        notifyLinksChanged(newTargets);

        // Clear selection since all books are now added
        setSelectedSource(null);
    };

    const handleRemoveItem = (itemToRemove: Link) => {
        const newTargets = targets.filter(
            (item) => item.book.id !== itemToRemove.book.id,
        );
        setTargets(newTargets);
        notifyLinksChanged(newTargets);
    };

    return (
        <Box
            sx={{
                width: "100%",
                height: "100%",
                margin: 0,
                bgcolor: "white",
                p: 3,
                borderRadius: 1,
                display: "flex",
                flexDirection: "column",
                overflow: "hidden",
                boxSizing: "border-box", // include padding in width calculation
            }}
        >
            <Box
                sx={{
                    display: "flex",
                    gap: 3,
                    alignItems: "flex-start",
                    flex: 1,
                    overflow: "hidden",
                    minHeight: 0, // allow flex child to shrink
                }}
            >
                <Box
                    sx={{
                        flex: 1,
                        display: "flex",
                        flexDirection: "column",
                        height: "100%",
                        overflow: "hidden",
                    }}
                >
                    <Box
                        sx={{
                            display: "flex",
                            alignItems: "center",
                            gap: 1,
                            minHeight: 40,
                        }}
                    >
                        <Typography variant="h6" sx={{ mb: 0.5 }}>
                            {useL10n(
                                "Books in this Collection",
                                "BookGridSetup.BooksInCollection",
                                "Header for the list of books available in the current collection",
                            )}
                        </Typography>
                    </Box>
                    <Box
                        sx={{
                            flex: 1,
                            minHeight: 0, // allow flex child to shrink below content size
                        }}
                    >
                        <BookSourcesList
                            books={props.sourceBooks}
                            selectedBook={selectedSource}
                            onSelectBook={handleItemSelect}
                            disabledBookIds={targets.map(
                                (target) => target.book.id,
                            )}
                        />
                    </Box>
                </Box>

                <Box
                    sx={{
                        display: "flex",
                        flexDirection: "column",
                        pt: 1,
                        marginTop: "100px",
                        gap: 2,
                    }}
                >
                    <BloomButton
                        variant="contained"
                        onClick={handleAddBookLink}
                        enabled={
                            !!selectedSource &&
                            !targets.some(
                                (item) => item.book.id === selectedSource.id,
                            )
                        }
                        l10nKey="BookGridSetup.AddBook"
                        l10nComment="Button to add the selected book to the grid of links"
                    >
                        Add Book →
                    </BloomButton>
                    <BloomButton
                        variant="outlined"
                        onClick={handleAddAllBooks}
                        enabled={
                            props.sourceBooks.filter(
                                (book) =>
                                    !targets.some(
                                        (target) => target.book.id === book.id,
                                    ),
                            ).length > 0
                        }
                        sx={{ whiteSpace: "nowrap" }}
                        l10nKey="BookGridSetup.AddAllBooks"
                        l10nComment="Button to add all available books to the grid of links"
                    >
                        Add All Books →
                    </BloomButton>
                </Box>

                <Box
                    sx={{
                        flex: 1,
                        display: "flex",
                        flexDirection: "column",
                        height: "100%",
                        overflow: "hidden",
                        minWidth: 0, // prevent flex child from overflowing
                    }}
                >
                    <Typography variant="h6" sx={{ mb: 0.5 }}>
                        {useL10n(
                            "Links in Grid (%0)",
                            "BookGridSetup.LinksInGrid",
                            "Header for the list of books that have been added to the grid, %0 is the count",
                        ).replace("%0", targets.length.toString())}
                    </Typography>
                    <Box
                        sx={{
                            flex: 1,
                            minHeight: 0,
                            overflow: "hidden", // no scroll on parent
                        }}
                    >
                        <BookTargetList
                            links={targets}
                            onRemoveBook={handleRemoveItem}
                            onReorderBooks={(newOrder) => {
                                setTargets(newOrder);
                                notifyLinksChanged(newOrder);
                            }}
                        />
                    </Box>
                </Box>
            </Box>
        </Box>
    );
};

export { BookGridSetup };
