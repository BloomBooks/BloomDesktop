import * as React from "react";

import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogTitle
} from "../../react_components/BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton
} from "../../react_components/BloomDialog/commonDialogComponents";
import { useWatchApiData } from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../editViewFrame";
import LinkGridSetup from "./LinkGridSetup";
import { css } from "@emotion/react";
import { BookInfoForLinks, Link } from "./BookLinkTypes";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";

export const LinkGridSetupDialog: React.FunctionComponent<{
    initialLinks: Link[];
    setLinksCallback: (links: Link[]) => void;
}> = props => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false
    });

    const dialogTitle = "Link Grid Setup"; // useL10n("Link Grid Setup", "LinkGridSetupDialog.Title");

    function saveLinksAndCloseDialog() {
        props.setLinksCallback(selectedLinks);
        closeDialog();
    }

    const [selectedLinks, setSelectedLinks] = React.useState<Link[]>(
        props.initialLinks
    );

    const unfilteredBooks = useWatchApiData<Array<IBookInfo>>(
        `collections/books?realTitle=true`,
        [],
        "editableCollectionList",
        "unused" // we don't care about updates, so maybe we don't care about this?
    );

    const bookLinks: BookInfoForLinks[] = unfilteredBooks.map(book => ({
        id: book.id,
        folderName: book.folderName,
        title: book.title,
        thumbnail: `/bloom/api/collections/book/thumbnail?book-id=${book.id}`
    }));

    const bookIdsToFolderNames = Object.fromEntries(
        unfilteredBooks.map(b => [b.id, b.folderName])
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
                    padding: 0;
                    height: 80vh;
                `}
            >
                <LinkGridSetup
                    sourceBooks={bookLinks}
                    /*  not using these the trimmed down version
                    collectionNames={collections.map(c => c.name)}
                    currentCollection={currentCollection}
                    onCollectionSelectionChange={setCurrentCollection}
                    */
                    links={selectedLinks.map(link => ({
                        ...link,
                        book: {
                            ...link.book,
                            folderName: bookIdsToFolderNames[link.book.id],
                            title: link.book.title
                        }
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

export function showLinkGridSetupsDialog(
    currentLinks: Link[],
    setLinksCallback: (links: Link[]) => void
) {
    ShowEditViewDialog(
        <LinkGridSetupDialog
            initialLinks={currentLinks}
            setLinksCallback={setLinksCallback}
        />
    );
}
