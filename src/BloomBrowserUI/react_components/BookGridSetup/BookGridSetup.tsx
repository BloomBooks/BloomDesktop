import * as React from "react";
import { useState } from "react";
import { Box, Typography } from "@mui/material";
import { BookInfoForLinks, BookGridSetupMode, Link } from "./BookLinkTypes";

import { BookSourcesList } from "./BookSourcesList";
import { BookTargetList } from "./BookTargetList";

import { useL10n } from "../l10nHooks";
import { postJson } from "../../utils/bloomApi";
import BloomButton from "../bloomButton";

let nextBookGridLinkId = 1;
const bookGridLinkIdPrefix = "book-grid-link-";

const createBookGridLinkId = () =>
    `${bookGridLinkIdPrefix}${nextBookGridLinkId++}`;

const syncNextBookGridLinkId = (links: Link[]) => {
    const highestExistingId = links.reduce((currentMax, link) => {
        if (!link.id || !link.id.startsWith(bookGridLinkIdPrefix)) {
            return currentMax;
        }

        const suffix = link.id.substring(bookGridLinkIdPrefix.length);
        const parsed = Number(suffix);
        if (isNaN(parsed)) {
            return currentMax;
        }

        return Math.max(currentMax, parsed);
    }, 0);

    if (highestExistingId >= nextBookGridLinkId) {
        nextBookGridLinkId = highestExistingId + 1;
    }
};

const normalizeLinks = (links: Link[]) => {
    syncNextBookGridLinkId(links);
    return links.map((link) => ({
        ...link,
        id: link.id || createBookGridLinkId(),
    }));
};

const createDefaultLabelForLink = (link: Link, currentBookId?: string) => {
    if (currentBookId && link.book.id === currentBookId) {
        if (!link.page) {
            return "Book";
        }
        if (link.page?.isFrontCover || link.page?.pageId === "cover") {
            return "Front Cover";
        }
        const pageNumber = link.page?.pageIndex ?? 1;
        return `Page ${pageNumber}`;
    }
    return link.book.title || link.book.folderName || "";
};

const createLinkFromBook = (
    book: BookInfoForLinks,
    currentBookId?: string,
): Link => {
    const newLink: Link = {
        id: createBookGridLinkId(),
        book,
        page: undefined,
    };

    newLink.label = createDefaultLabelForLink(newLink, currentBookId);
    return newLink;
};

const BookGridSetup: React.FC<{
    sourceBooks: BookInfoForLinks[];
    currentBookId?: string;
    mode?: BookGridSetupMode;

    links: Link[]; // the set of links that are currently in the grid
    onLinksChanged: ((links: Link[]) => void) | string; // function for normal use, string URL for testing
}> = (props) => {
    const [selectedSource, setSelectedSource] = useState<BookInfoForLinks>();
    const isTocMode = props.mode === "toc";
    const tocBookId = props.currentBookId || props.links[0]?.book.id;
    const visibleSourceBooks = React.useMemo(() => {
        if (!isTocMode) {
            return props.sourceBooks;
        }
        if (!tocBookId) {
            return [];
        }
        return props.sourceBooks.filter((book) => book.id === tocBookId);
    }, [isTocMode, tocBookId, props.sourceBooks]);
    const [targets, setTargets] = useState<Link[]>(
        normalizeLinks(
            isTocMode
                ? tocBookId
                    ? props.links.filter((link) => link.book.id === tocBookId)
                    : props.links
                : props.links,
        ),
    );
    const booksInCollectionLabel = useL10n(
        "Books in this Collection",
        "BookGridSetup.BooksInCollection",
        "Header for the list of books available in the current collection",
    );
    const linksInGridLabelTemplate = useL10n(
        "Links in Grid (%0)",
        "BookGridSetup.LinksInGrid",
        "Header for the list of books that have been added to the grid, %0 is the count",
    );

    React.useEffect(() => {
        const incomingLinks = isTocMode
            ? tocBookId
                ? props.links.filter((link) => link.book.id === tocBookId)
                : props.links
            : props.links;
        setTargets(normalizeLinks(incomingLinks));
    }, [isTocMode, tocBookId, props.links]);

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
        if (!selectedSource) {
            return;
        }

        const newTargets = [
            ...targets,
            createLinkFromBook(selectedSource, props.currentBookId),
        ];
        setTargets(newTargets);
        notifyLinksChanged(newTargets);
    };

    const handleAddTocLink = () => {
        const currentBook = visibleSourceBooks[0];
        if (!currentBook) {
            return;
        }

        const newTargets = [
            ...targets,
            createLinkFromBook(currentBook, props.currentBookId),
        ];
        setTargets(newTargets);
        notifyLinksChanged(newTargets);
    };

    const handleAddAllBooks = () => {
        // Find all books that aren't already in targets
        const availableBooks = visibleSourceBooks.filter(
            (book) => !targets.some((target) => target.book.id === book.id),
        );

        // Add all available books as links
        const newLinks = availableBooks.map((book) =>
            createLinkFromBook(book, props.currentBookId),
        );
        const newTargets = [...targets, ...newLinks];
        setTargets(newTargets);
        notifyLinksChanged(newTargets);

        // Clear selection since all books are now added
        setSelectedSource(undefined);
    };

    const handleRemoveItem = (itemToRemove: Link) => {
        const newTargets = targets.filter(
            (item) => item.id !== itemToRemove.id,
        );
        setTargets(newTargets);
        notifyLinksChanged(newTargets);
    };

    const handleUpdateItem = (updatedLink: Link) => {
        const newTargets = targets.map((link) =>
            link.id === updatedLink.id ? updatedLink : link,
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
                {!isTocMode && (
                    <>
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
                                    {booksInCollectionLabel}
                                </Typography>
                            </Box>
                            <Box
                                sx={{
                                    flex: 1,
                                    minHeight: 0, // allow flex child to shrink below content size
                                }}
                            >
                                <BookSourcesList
                                    books={visibleSourceBooks}
                                    selectedBook={selectedSource}
                                    onSelectBook={handleItemSelect}
                                    disabledBookIds={[]}
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
                                enabled={!!selectedSource}
                                l10nKey="BookGridSetup.AddBook"
                                l10nComment="Button to add the selected book to the grid of links"
                            >
                                Add Book →
                            </BloomButton>
                            <BloomButton
                                variant="outlined"
                                onClick={handleAddAllBooks}
                                enabled={
                                    visibleSourceBooks.filter(
                                        (book) =>
                                            !targets.some(
                                                (target) =>
                                                    target.book.id === book.id,
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
                    </>
                )}

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
                        {linksInGridLabelTemplate.replace(
                            "%0",
                            targets.length.toString(),
                        )}
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
                            onUpdateLink={handleUpdateItem}
                            currentBookId={props.currentBookId}
                            showAddLinkButton={isTocMode}
                            onAddLink={isTocMode ? handleAddTocLink : undefined}
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
