import * as React from "react";
import { useState } from "react";
import { Box, Button, Typography } from "@mui/material";
import {
    BookInfoForLinks,
    Link,
    PageInfoForLinks,
    ThumbnailGenerator
} from "./BookLinkTypes";
import { BookSourcesList } from "./BookSourcesList";
import { BookTargetList } from "./BookTargetList";
import { PageLinkChooserDialog } from "./PageLinkChooserDialog";

interface Props {
    sourceBooks: BookInfoForLinks[];
    collectionNames: string[];
    currentCollection: number;
    onCollectionChange: (index: number) => void; // when the user changes the collection, this callback will change the set of source books
    onLinksChanged: (links: Link[]) => void; // add this line
    links: Link[]; // add this line
    thumbnailGenerator: ThumbnailGenerator;
}

const LinkGridSetup: React.FC<Props> = ({
    sourceBooks: books,
    collectionNames,
    currentCollection,
    onCollectionChange,
    onLinksChanged: onBooksChanged, // add this line
    links, // add this line
    thumbnailGenerator
}) => {
    const [
        selectedSource,
        setSelectedSource
    ] = useState<BookInfoForLinks | null>(null);
    const [targets, setTargets] = useState<Link[]>(links); // initialize with links prop
    const [isPageDialogOpen, setIsPageDialogOpen] = useState(false);

    const handleItemSelect = (item: BookInfoForLinks) => {
        setSelectedSource(item);
    };

    const handleAddBookLink = () => {
        if (
            selectedSource &&
            !targets.some(item => item.book.id === selectedSource.id)
        ) {
            const newTargets = [...targets, { book: selectedSource }];
            setTargets(newTargets);
            onBooksChanged(newTargets); // add this line

            // Find next available book
            const currentIndex = books.findIndex(
                book => book.id === selectedSource.id
            );
            const availableBooks = books.filter(
                book =>
                    !targets.some(added => added.book.id === book.id) &&
                    book.id !== selectedSource.id
            );

            // Try to select the next book in sequence, or the first available book
            const nextBook =
                availableBooks.find(
                    book => books.indexOf(book) > currentIndex
                ) ||
                availableBooks[0] ||
                null;

            setSelectedSource(nextBook);
        }
    };

    const handleAddPageLink = () => {
        setIsPageDialogOpen(true);
    };

    const handlePageDialogClose = () => {
        setIsPageDialogOpen(false);
    };

    const handlePageSelected = (pageInfo: PageInfoForLinks) => {
        if (selectedSource) {
            const linkWithPage = {
                book: selectedSource,
                page: pageInfo
            };
            const newTargets = [...targets, linkWithPage];
            setTargets(newTargets);
            onBooksChanged(newTargets);
            setIsPageDialogOpen(false);
        }
    };

    const handleRemoveItem = (itemToRemove: Link) => {
        const newTargets = targets.filter(
            item => item.book.id !== itemToRemove.book.id
        );
        setTargets(newTargets);
        onBooksChanged(newTargets); // add this line
    };

    return (
        <Box
            sx={{
                width: "calc(100% - 50px)", //hack
                height: "100%",
                margin: 0,
                padding: 0,
                bgcolor: "white",
                p: 3,
                borderRadius: 1,
                display: "flex",
                flexDirection: "column",
                overflow: "hidden"
            }}
        >
            <Box
                sx={{
                    display: "flex",
                    gap: 3,
                    alignItems: "flex-start",
                    flex: 1,
                    overflow: "hidden"
                }}
            >
                <Box
                    sx={{
                        flex: 1,
                        display: "flex",
                        flexDirection: "column",
                        height: "100%",
                        overflow: "hidden"
                    }}
                >
                    <Box
                        sx={{
                            display: "flex",
                            alignItems: "center",
                            gap: 1,
                            minHeight: 40
                        }}
                    >
                        <Typography variant="h6" sx={{ mb: 2 }}>
                            Books in this Collection
                        </Typography>
                        {/* <Typography
                            sx={{ display: "flex", alignItems: "center" }}
                        >
                            Collection:
                        </Typography>
                        <select
                            value={currentCollection}
                            onChange={e =>
                                onCollectionChange(Number(e.target.value))
                            }
                            style={{
                                padding: "4px",
                                fontSize: "16px",
                                width: "200px",
                                margin: "4px 0"
                            }}
                        >
                            {collectionNames.map((name, index) => (
                                <option key={name} value={index}>
                                    {name}
                                </option>
                            ))}
                        </select> */}
                    </Box>
                    <Box sx={{ flex: 1, overflow: "auto" }}>
                        <BookSourcesList
                            books={books}
                            selectedBook={selectedSource}
                            onSelectBook={handleItemSelect}
                        />
                    </Box>
                </Box>

                <Box
                    sx={{
                        display: "flex",
                        flexDirection: "column",
                        pt: 1,
                        marginTop: "100px"
                    }}
                >
                    <Button
                        variant="contained"
                        onClick={handleAddBookLink}
                        disabled={
                            !selectedSource ||
                            targets.some(
                                item => item.book.id === selectedSource.id
                            )
                        }
                        // sx={{
                        //     mb: 2,
                        //     "&.Mui-disabled": {
                        //         bgcolor: "rgba(255, 255, 255, 0.12)" // visible on dark background
                        //     },
                        //     "&:not(:disabled)": {
                        //         bgcolor: theme => theme.palette.primary.main,
                        //         "&:hover": {
                        //             bgcolor: theme => theme.palette.primary.dark
                        //         }
                        //     }
                        // }}
                    >
                        Book →
                    </Button>
                    <Button
                        variant="text"
                        onClick={handleAddPageLink}
                        disabled={!selectedSource}
                        sx={{ mb: 2 }}
                    >
                        Page →
                    </Button>
                </Box>

                <Box
                    sx={{
                        flex: 1,
                        display: "flex",
                        flexDirection: "column",
                        height: "100%",
                        overflow: "hidden"
                    }}
                >
                    <Typography variant="h6" sx={{ mb: 2 }}>
                        Links in Grid ({targets.length})
                    </Typography>
                    <Box sx={{ flex: 1, overflow: "auto" }}>
                        <BookTargetList
                            links={targets}
                            onRemoveBook={handleRemoveItem}
                            onReorderBooks={newOrder => {
                                setTargets(newOrder);
                                onBooksChanged(newOrder);
                            }}
                        />
                    </Box>
                </Box>
            </Box>

            <PageLinkChooserDialog
                open={isPageDialogOpen}
                selectedBook={selectedSource}
                onClose={handlePageDialogClose}
                onPageSelected={handlePageSelected}
                thumbnailGenerator={thumbnailGenerator}
            />
        </Box>
    );
};

export default LinkGridSetup;
