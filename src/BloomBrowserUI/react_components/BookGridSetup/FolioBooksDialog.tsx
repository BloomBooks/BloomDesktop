import * as React from "react";
import { css } from "@emotion/react";
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
import {
    useWatchApiData,
    useApiString,
    postBoolean,
} from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../../bookEdit/workspaceRoot";
import { BookGridSetup } from "./BookGridSetup";
import { BookInfoForLinks, Link } from "./BookLinkTypes";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";
import { useL10n } from "../l10nHooks";

// Choose the books a folio's table of contents lists, and their order. The collection's books are
// offered except the folio itself and other folios (a folio cannot hold one).
export const FolioBooksDialog: React.FunctionComponent<{
    initialLinks: Link[];
    setLinksCallback: (links: Link[]) => void;
}> = (props) => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false,
    });
    const dialogTitle = useL10n(
        "Choose Books for Folio",
        "FolioBooksDialog.Title",
    );
    const [selectedLinks, setSelectedLinks] = React.useState<Link[]>(
        props.initialLinks,
    );

    // The Edit tab must not take clicks while this dialog is up.
    React.useEffect(() => {
        if (propsForBloomDialog.open !== undefined) {
            postBoolean("editView/setModalState", propsForBloomDialog.open);
        }
    }, [propsForBloomDialog.open]);

    const collectionBooks = useWatchApiData<Array<IBookInfo>>(
        `collections/books?realTitle=true`,
        [],
        "editableCollectionList",
        "unused",
    );
    const currentBookId = useApiString("editView/currentBookId", "");

    const sourceBooks: BookInfoForLinks[] = collectionBooks
        .filter((book) => book.id !== currentBookId && !book.isFolio)
        .map((book) => ({
            id: book.id,
            folderName: book.folderName,
            title: book.title,
            thumbnail: `/bloom/api/collections/book/coverImage?book-id=${book.id}`,
        }));
    const titles = Object.fromEntries(
        collectionBooks.map((b) => [b.id, b.title]),
    );

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onClose={closeDialog}
            onCancel={() => closeDialog()}
            draggable={false}
            maxWidth={false}
            fullWidth={true}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    > :first-child {
                        padding: 0 !important;
                    }
                    height: 80vh;
                `}
            >
                <BookGridSetup
                    sourceBooks={sourceBooks}
                    links={selectedLinks.map((link) => ({
                        ...link,
                        book: {
                            ...link.book,
                            title: titles[link.book.id] ?? link.book.title,
                        },
                    }))}
                    onLinksChanged={setSelectedLinks}
                    targetLabel="books-in-folio"
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={() => {
                        props.setLinksCallback(
                            selectedLinks.map((link) => ({
                                ...link,
                                book: {
                                    ...link.book,
                                    title:
                                        titles[link.book.id] ?? link.book.title,
                                },
                            })),
                        );
                        closeDialog();
                    }}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export function showFolioBooksDialog(
    currentLinks: Link[],
    setLinksCallback: (links: Link[]) => void,
) {
    ShowEditViewDialog(
        <FolioBooksDialog
            initialLinks={currentLinks}
            setLinksCallback={setLinksCallback}
        />,
    );
}
