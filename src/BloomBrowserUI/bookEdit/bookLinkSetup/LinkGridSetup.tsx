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

const LinkGridSetup: React.FC<{
    sourceBooks: BookInfoForLinks[];
    /* not using right now

    collectionNames: string[];
    currentCollection: number;
    onCollectionSelectionChange: (index: number) => void; // when the user changes the collection, this callback will change the set of source books
    */
    links: Link[]; // the set of links that are currently in the grid
    onLinksChanged: (links: Link[]) => void;
}> = props => {
    const [
        selectedSource,
        setSelectedSource
    ] = useState<BookInfoForLinks | null>(null);
    const [targets, setTargets] = useState<Link[]>(props.links); // initialize with links prop
    const [isPageDialogOpen, setIsPageDialogOpen] = useState(false);

    React.useEffect(() => {
        setTargets(props.links);
    }, [props.links]);

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
            props.onLinksChanged(newTargets);

            // Find next available book
            const currentIndex = props.sourceBooks.findIndex(
                book => book.id === selectedSource.id
            );
            const availableBooks = props.sourceBooks.filter(
                book =>
                    !targets.some(added => added.book.id === book.id) &&
                    book.id !== selectedSource.id
            );

            // Try to select the next book in sequence, or the first available book
            const nextBook =
                availableBooks.find(
                    book => props.sourceBooks.indexOf(book) > currentIndex
                ) ||
                availableBooks[0] ||
                null;

            setSelectedSource(nextBook);
        }
    };

    /* not used right now
    const handleAddPageLink = () => {
        setIsPageDialogOpen(true);
    };
*/
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
            props.onLinksChanged(newTargets);
            setIsPageDialogOpen(false);
        }
    };

    const handleRemoveItem = (itemToRemove: Link) => {
        const newTargets = targets.filter(
            item => item.book.id !== itemToRemove.book.id
        );
        setTargets(newTargets);
        props.onLinksChanged(newTargets);
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
                    <Box
                        sx={{
                            flex: 1,
                            overflow: "hidden" // we want the list itself to scroll, not the parent box
                        }}
                    >
                        <BookSourcesList
                            books={props.sourceBooks}
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
                        Add Book →
                    </Button>
                    {/* <Button
                        variant="text"
                        onClick={handleAddPageLink}
                        disabled={!selectedSource}
                        sx={{ mb: 2 }}
                    >
                        Add Page →
                    </Button> */}
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
                                props.onLinksChanged(newOrder);
                            }}
                        />
                    </Box>
                </Box>
            </Box>

            {/* <PageLinkChooserDialog
                open={isPageDialogOpen}
                selectedBook={selectedSource}
                onClose={handlePageDialogClose}
                onPageSelected={handlePageSelected}
                thumbnailGenerator={props.thumbnailGenerator}
            /> */}
        </Box>
    );
};

export default LinkGridSetup;
