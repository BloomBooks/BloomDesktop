import * as React from "react";

import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogTitle,
} from "../BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../BloomDialog/commonDialogComponents";
import { useWatchApiData, useApiString } from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../../bookEdit/editViewFrame";
import { BookGridSetup } from "./BookGridSetup";
import { css } from "@emotion/react";
import { BookInfoForLinks, Link } from "./BookLinkTypes";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";
import { useL10n } from "../l10nHooks";

export const BookGridSetupDialog: React.FunctionComponent<{
    initialLinks: Link[];
    setLinksCallback: (links: Link[]) => void;
}> = (props) => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false,
    });

    const dialogTitle = useL10n("Book Grid Setup", "BookGridSetupDialog.Title");

    function saveLinksAndCloseDialog() {
        props.setLinksCallback(selectedLinks);
        closeDialog();
    }

    const [selectedLinks, setSelectedLinks] = React.useState<Link[]>(
        props.initialLinks,
    );

    const unfilteredBooks = useWatchApiData<Array<IBookInfo>>(
        `collections/books?realTitle=true`,
        [],
        "editableCollectionList",
        "unused", // we don't care about updates, so maybe we don't care about this?
    );

    // Get the current book ID so we can exclude it from the source list.
    // A book shouldn't be able to link to itself.
    const currentBookId = useApiString("editView/currentBookId", "");

    // Filter out the current book from the source list
    const filteredBooks = unfilteredBooks.filter(
        (book) => book.id !== currentBookId,
    );

    const bookLinks: BookInfoForLinks[] = filteredBooks.map((book) => ({
        id: book.id,
        folderName: book.folderName,
        title: book.title,
        thumbnail: `/bloom/api/collections/book/thumbnail?book-id=${book.id}`,
    }));

    // Create lookup tables to efficiently map book IDs to their folder names and titles.
    // This allows us to enrich the link data with current book information when it changes.
    const bookIdsToFolderNames = Object.fromEntries(
        unfilteredBooks.map((b) => [b.id, b.folderName]),
    );

    const bookIdsToTitles = Object.fromEntries(
        unfilteredBooks.map((b) => [b.id, b.title]),
    );

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onClose={closeDialog}
            onCancel={() => {
                closeDialog();
            }}
            draggable={false}
            maxWidth={false}
            fullWidth={true}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    > :first-child {
                        // for some reason this dialog looks dumb with padding of the contents
                        padding: 0 !important;
                    }
                    height: 80vh;
                `}
            >
                <BookGridSetup
                    sourceBooks={bookLinks}
                    /*  not using these the trimmed down version
                    collectionNames={collections.map(c => c.name)}
                    currentCollection={currentCollection}
                    onCollectionSelectionChange={setCurrentCollection}
                    */
                    // Enrich links with current book data from the collection.
                    // This ensures that if a book's title or folder name has changed,
                    // we display the most up-to-date information.
                    links={selectedLinks.map((link) => ({
                        ...link,
                        book: {
                            ...link.book,
                            folderName: bookIdsToFolderNames[link.book.id],
                            title:
                                bookIdsToTitles[link.book.id] ||
                                link.book.title,
                        },
                    }))}
                    onLinksChanged={setSelectedLinks}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={saveLinksAndCloseDialog}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export function showLinkGridSetupDialog(
    currentLinks: Link[],
    setLinksCallback: (links: Link[]) => void,
) {
    ShowEditViewDialog(
        <BookGridSetupDialog
            initialLinks={currentLinks}
            setLinksCallback={setLinksCallback}
        />,
    );
}
