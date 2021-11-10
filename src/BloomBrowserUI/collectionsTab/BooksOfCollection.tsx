import Grid from "@material-ui/core/Grid";
import React = require("react");
import "BooksOfCollection.less";
import { BloomApi } from "../utils/bloomApi";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import { BookButton } from "./BookButton";
import { useMonitorBookSelection } from "../app/selectedBook";
import { element } from "prop-types";
import { useL10n } from "../react_components/l10nHooks";
import { useState } from "react";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";

interface IBookInfo {
    id: string;
    title: string;
    collectionId: string;
    folderName: string;
}
export const BooksOfCollection: React.FunctionComponent<{
    collectionId: string;
    isEditableCollection: boolean;
}> = props => {
    const [reload, setReload] = useState(0);
    // Force a reload when told the collection needs it.
    useSubscribeToWebSocketForEvent(
        "editableCollectionList",
        "reload:" + props.collectionId,
        () => setReload(old => old + 1)
    );
    if (!props.collectionId) {
        window.alert("null collectionId");
    }
    const collectionQuery = `collection-id=${encodeURIComponent(
        props.collectionId
    )}`;

    const books = BloomApi.useApiData<Array<IBookInfo>>(
        `collections/books?${collectionQuery}`,
        [],
        reload
    );
    const [
        selectedBookId,
        setSelectedBookIdWithApi
    ] = BloomApi.useApiStringState(
        `collections/selected-book-id?${collectionQuery}`,
        ""
    );
    const selectedBookInfo = useMonitorBookSelection();

    const [contextMousePoint, setContextMousePoint] = React.useState<
        | {
              mouseX: number;
              mouseY: number;
          }
        | undefined
    >();

    const handleClick = (event: React.MouseEvent<HTMLDivElement>) => {
        if (!(event.target instanceof Element)) {
            return; // huh?
        }
        const target = event.target as Element;
        if (target.closest(".bloom-no-default-menu") == null) {
            // We're not responsible for the menu here...let the usual Bloom C# context menu appear
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        setContextMousePoint({
            mouseX: event.clientX - 2,
            mouseY: event.clientY - 4
        });
    };

    const handleClose = () => {
        setContextMousePoint(undefined);
    };

    const handleBookCommand = (command: string) => {
        handleClose();
        BloomApi.postString(
            `collections/bookCommand?command=${command}&collection-id=${props.collectionId}`,
            selectedBookId
        );
    };

    const dupBook = useL10n(
        "Duplicate Book",
        "CollectionTab.BookMenu.DuplicateBook"
    );
    const makeBloompack = useL10n(
        "Make Bloom Pack",
        "CollectionTab.MakeBloomPackButton"
    );
    const openFolderOnDisk = useL10n(
        "Open Folder on Disk",
        "CollectionTab.ContextMenu.OpenFolderOnDisk"
    );
    const exportToWord = useL10n(
        "Export to Word or LibreOffice...",
        "CollectionTab.BookMenu.ExportToWordOrLibreOffice"
    );
    const exportToSpreadsheet = useL10n(
        "Export to Spreadsheet...",
        "CollectionTab.BookMenu.ExportToSpreadsheet"
    );
    const importSpreadsheetContent = useL10n(
        "Import content from Spreadsheet...",
        "CollectionTab.BookMenu.ImportContentFromSpreadsheet"
    );
    const saveAsDotBloom = useL10n(
        "Save as single file (.bloom)...",
        "CollectionTab.BookMenu.SaveAsBloomToolStripMenuItem"
    );

    const updateThumbnail = useL10n(
        "Update Thumbnail",
        "CollectionTab.BookMenu.UpdateThumbnail"
    );
    const updateBook = useL10n(
        "Update Book",
        "CollectionTab.BookMenu.UpdateFrontMatterToolStrip"
    );
    const rename = useL10n("Rename", "CollectionTab.BookMenu.Rename");

    const deleteBook = useL10n(
        "Delete Book",
        "CollectionTab.BookMenu.DeleteBook"
    );

    const anchor = !!contextMousePoint
        ? contextMousePoint!.mouseY !== null &&
          contextMousePoint!.mouseX !== null
            ? {
                  top: contextMousePoint!.mouseY,
                  left: contextMousePoint!.mouseX
              }
            : undefined
        : undefined;

    return (
        <div
            key={"BookCollection-" + props.collectionId}
            className="bookButtonPane"
            onContextMenu={e => handleClick(e)}
            style={{ cursor: "context-menu" }}
        >
            <Grid
                container={true}
                spacing={3}
                direction="row"
                justify="flex-start"
                alignItems="flex-start"
            >
                {books?.map(book => {
                    return (
                        <BookButton
                            key={book.id}
                            book={book}
                            isInEditableCollection={props.isEditableCollection}
                            selected={selectedBookInfo.id === book.id}
                            onClick={bookId => {
                                setSelectedBookIdWithApi(bookId);
                            }}
                        />
                    );
                })}
            </Grid>
            <Menu
                keepMounted={true}
                open={!!contextMousePoint}
                onClose={handleClose}
                anchorReference="anchorPosition"
                anchorPosition={anchor}
            >
                <MenuItem onClick={() => handleBookCommand("duplicateBook")}>
                    {dupBook}
                </MenuItem>
                <MenuItem onClick={() => handleBookCommand("makeBloompack")}>
                    {makeBloompack}
                </MenuItem>
                <MenuItem onClick={() => handleBookCommand("openFolderOnDisk")}>
                    {openFolderOnDisk}
                </MenuItem>
                <MenuItem onClick={() => handleBookCommand("exportToWord")}>
                    {exportToWord}
                </MenuItem>
                <MenuItem
                    onClick={() => handleBookCommand("exportToSpreadsheet")}
                >
                    {exportToSpreadsheet}
                </MenuItem>
                <MenuItem
                    onClick={() =>
                        handleBookCommand("importSpreadsheetContent")
                    }
                >
                    {importSpreadsheetContent}
                </MenuItem>
                <MenuItem onClick={() => handleBookCommand("saveAsDotBloom")}>
                    {saveAsDotBloom}
                </MenuItem>
                <MenuItem onClick={() => handleBookCommand("updateThumbnail")}>
                    {updateThumbnail}
                </MenuItem>
                <MenuItem onClick={() => handleBookCommand("updateBook")}>
                    {updateBook}
                </MenuItem>

                <MenuItem
                    onClick={() =>
                        // No. Need to put up an overlay, let user do it, send result to backend.
                        handleBookCommand("rename")
                    }
                >
                    {rename}
                </MenuItem>
                <MenuItem onClick={() => handleBookCommand("deleteBook")}>
                    {deleteBook}
                </MenuItem>
            </Menu>
        </div>
    );
};
