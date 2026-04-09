import { css } from "@emotion/react";
import * as React from "react";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../../react_components/BloomDialog/commonDialogComponents";
import { BookGridSetup } from "../../react_components/BookGridSetup/BookGridSetup";
import {
    BookInfoForLinks,
    Link as BookSelectionLink,
} from "../../react_components/BookGridSetup/BookLinkTypes";
import { useL10n } from "../../react_components/l10nHooks";
import { postJson, useWatchApiData } from "../../utils/bloomApi";
import {
    EstimatedAppSizeIndicator,
    IAppSizeEstimates,
} from "./AppSizeIndicator";
import { IAppBuilderTrackedBook } from "./appBuilderShared";

const kChooseBooksDialogWidth = "860px";

export const AppBuilderChooseBooksDialog: React.FunctionComponent<{
    currentBooks: IAppBuilderTrackedBook[];
    sizeEstimates: IAppSizeEstimates;
    onClose: () => void;
    onSaved: () => void;
}> = (props) => {
    const dialogTitle = useL10n(
        "Choose Books",
        "PublishTab.Apps.ChooseBooksDialog.Title",
    );
    const collectionBooks = useWatchApiData<Array<IBookInfo>>(
        "collections/books?realTitle=true",
        [],
        "editableCollectionList",
        "unused",
    );

    // BookGridSetup expects richer UI metadata than the RAB API persists, so rebuild that shape here.
    const sourceBooks: BookInfoForLinks[] = collectionBooks.map((book) => ({
        id: book.id,
        folderName: book.folderName,
        folderPath: book.folderPath,
        title: book.title,
        thumbnail: `/bloom/api/collections/book/coverImage?book-id=${book.id}`,
    }));
    const initialLinks: BookSelectionLink[] = props.currentBooks.map(
        (book) => ({
            book: {
                id: book.bookId,
                folderPath: book.folderPath,
                title: book.title,
                folderName:
                    collectionBooks.find(
                        (collectionBook) => collectionBook.id === book.bookId,
                    )?.folderName ?? "",
                thumbnail: `/bloom/api/collections/book/coverImage?book-id=${book.bookId}`,
            },
        }),
    );
    const [selectedLinks, setSelectedLinks] =
        React.useState<BookSelectionLink[]>(initialLinks);

    async function saveBooks(): Promise<void> {
        // Preserve the UI order; RAB should show books in the same sequence the user chose here.
        await postJson(
            "publish/rab/books",
            selectedLinks.map((link) => ({
                bookId: link.book.id,
                folderPath: link.book.folderPath,
                title: link.book.title,
            })),
        );
        props.onSaved();
        props.onClose();
    }

    return (
        <BloomDialog
            open={true}
            dialogFrameProvidedExternally={false}
            onClose={props.onClose}
            onCancel={() => props.onClose()}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    width: min(${kChooseBooksDialogWidth}, calc(100vw - 96px));
                    height: 600px;
                    display: flex;
                    flex-direction: column;
                    gap: 16px;
                `}
            >
                {props.sizeEstimates.books.length > 0 && (
                    <EstimatedAppSizeIndicator
                        sizeEstimates={props.sizeEstimates}
                        books={selectedLinks.map((link) => ({
                            bookId: link.book.id,
                            folderPath: link.book.folderPath,
                            title: link.book.title,
                        }))}
                    />
                )}
                <div
                    css={css`
                        flex: 1;
                        min-height: 0;

                        > :first-child {
                            padding: 0 !important;
                            width: 100%;
                            height: 100%;
                        }
                    `}
                >
                    <BookGridSetup
                        sourceBooks={sourceBooks}
                        links={selectedLinks}
                        onLinksChanged={setSelectedLinks}
                        targetLabel="books-in-app"
                    />
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    enabled={selectedLinks.length > 0}
                    onClick={() => {
                        void saveBooks();
                    }}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
