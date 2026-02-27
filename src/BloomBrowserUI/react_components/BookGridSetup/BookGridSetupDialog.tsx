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
import { BookInfoForLinks, BookGridSetupMode, Link } from "./BookLinkTypes";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";
import { useL10n } from "../l10nHooks";

export const BookGridSetupDialog: React.FunctionComponent<{
    initialLinks: Link[];
    setLinksCallback: (links: Link[]) => void;
    mode?: BookGridSetupMode;
}> = (props) => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false,
    });

    const bookGridDialogTitle = useL10n(
        "Book Grid Setup",
        "BookGridSetupDialog.Title",
    );
    const tocGridDialogTitle = useL10n(
        "Table of Contents Grid Setup",
        "BookGridSetupDialog.TocTitle",
    );
    const isTocMode = props.mode === "toc";
    const dialogTitle = isTocMode ? tocGridDialogTitle : bookGridDialogTitle;

    function saveLinksAndCloseDialog() {
        props.setLinksCallback(selectedLinks);
        closeDialog();
    }

    const [selectedLinks, setSelectedLinks] = React.useState<Link[]>(
        props.initialLinks,
    );

    // Get the current book ID so whole-book mode can exclude it while
    // individual-page mode can include it.
    const currentBookId = useApiString("editView/currentBookId", "");
    const lastKnownTocBookIdRef = React.useRef<string | undefined>(
        props.initialLinks[0]?.book.id,
    );
    if (currentBookId) {
        lastKnownTocBookIdRef.current = currentBookId;
    }
    const resolvedCurrentBookId =
        currentBookId ||
        lastKnownTocBookIdRef.current ||
        props.initialLinks[0]?.book.id;

    React.useEffect(() => {
        if (!isTocMode || !resolvedCurrentBookId) {
            return;
        }
        setSelectedLinks((links) =>
            links.filter((link) => link.book.id === resolvedCurrentBookId),
        );
    }, [resolvedCurrentBookId, isTocMode]);

    const unfilteredBooks = useWatchApiData<Array<IBookInfo>>(
        `collections/books?realTitle=true`,
        [],
        "editableCollectionList",
        "unused", // we don't care about updates, so maybe we don't care about this?
    );

    const bookLinks: BookInfoForLinks[] = unfilteredBooks.map((book) => ({
        id: book.id,
        folderName: book.folderName,
        title: book.title,
        thumbnail: `/bloom/api/collections/book/coverImage?book-id=${book.id}`,
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
                    currentBookId={resolvedCurrentBookId}
                    mode={props.mode}
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
                            folderName:
                                // note: the book interface here has a foldername, and we get that
                                // from api calls, but links we just fished out of the dom don't know their folder name
                                bookIdsToFolderNames[link.book.id],
                            title: bookIdsToTitles[link.book.id],
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

export function showBookGridSetupDialog(
    currentLinks: Link[],
    setLinksCallback: (links: Link[]) => void,
    mode?: BookGridSetupMode,
) {
    ShowEditViewDialog(
        <BookGridSetupDialog
            initialLinks={currentLinks}
            setLinksCallback={setLinksCallback}
            mode={mode}
        />,
    );
}
