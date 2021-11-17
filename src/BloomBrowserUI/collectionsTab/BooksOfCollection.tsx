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
    const [renaming, setRenaming] = useState(false);

    if (!props.collectionId) {
        window.alert("null collectionId");
    }
    const collectionQuery = `collection-id=${encodeURIComponent(
        props.collectionId
    )}`;

    const books = BloomApi.useWatchApiData<Array<IBookInfo>>(
        `collections/books?${collectionQuery}`,
        [],
        "editableCollectionList",
        "reload:" + props.collectionId
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
        if (target.closest(".selected-book-wrapper") == null) {
            // We're not going to do our own right-click menu; let whatever default happens go ahead.
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
            `${command}?collection-id=${props.collectionId}`,
            selectedBookId
        );
    };

    interface MenuItemSpec {
        label: string;
        l10nId: string;
        // One of these two must be provided. If both are, onClick is used and command is ignored.
        command?: string;
        onClick?: React.MouseEventHandler<HTMLElement>;
        // todo: handling of bloom enterprise requirement, handle checkout requirement, maybe primary collection requirement...
    }
    const handleRename = () => {
        handleClose();
        setRenaming(true);
    };

    const finishRename = (name: string) => {
        BloomApi.postString(
            `bookCommand/rename?collection-id=${props.collectionId}&name=${name}`,
            selectedBookId
        );
    };

    const menuItemsSpecs: MenuItemSpec[] = [
        {
            label: "Duplicate Book",
            l10nId: "CollectionTab.BookMenu.DuplicateBook",
            command: "collections/bookCommand/duplicateBook"
        },
        {
            label: "Make Bloom Pack",
            l10nId: "CollectionTab.MakeBloomPackButton",
            command: "bookCommand/makeBloompack"
        },
        {
            label: "Open Folder on Disk",
            l10nId: "CollectionTab.ContextMenu.OpenFolderOnDisk",
            command: "bookCommand/openFolderOnDisk"
        },
        {
            label: "Export to Word or LibreOffice...",
            l10nId: "CollectionTab.BookMenu.ExportToWordOrLibreOffice",
            command: "bookCommand/exportToWord"
        },
        {
            label: "Export to Spreadsheet...",
            l10nId: "CollectionTab.BookMenu.ExportToSpreadsheet",
            command: "bookCommand/exportToSpreadsheet"
        },
        {
            label: "Import content from Spreadsheet...",
            l10nId: "CollectionTab.BookMenu.ImportContentFromSpreadsheet",
            command: "bookCommand/importSpreadsheetContent"
        },
        {
            label: "Save as single file (.bloom)...",
            l10nId: "CollectionTab.BookMenu.SaveAsBloomToolStripMenuItem",
            command: "bookCommand/saveAsDotBloom"
        },
        {
            label: "Update Thumbnail",
            l10nId: "CollectionTab.BookMenu.UpdateThumbnail",
            command: "bookCommand/updateThumbnail"
        },
        {
            label: "Update Book",
            l10nId: "CollectionTab.BookMenu.UpdateFrontMatterToolStrip",
            command: "bookCommand/updateBook"
        },
        {
            label: "Rename",
            l10nId: "CollectionTab.BookMenu.Rename",
            onClick: () => handleRename()
        },
        {
            label: "Delete Book",
            l10nId: "CollectionTab.BookMenu.DeleteBook",
            command: "collections/bookCommand/deleteBook"
        }
    ];

    const menuItems = menuItemsSpecs.map((spec: MenuItemSpec) => (
        <LocalizableMenuItem
            english={spec.label}
            l10nId={spec.l10nId}
            onClick={
                spec.onClick
                    ? spec.onClick
                    : () => handleBookCommand(spec.command!)
            }
        ></LocalizableMenuItem>
    ));

    // Todo: use it; fill out menuItemsSpecs; get rid of junk

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
                    const selected = selectedBookInfo.id === book.id;
                    return (
                        <BookButton
                            key={book.id}
                            book={book}
                            isInEditableCollection={props.isEditableCollection}
                            selected={selected}
                            renaming={selected && renaming}
                            onClick={bookId => {
                                if (!selected) {
                                    // Not only is it useless to select the book that is already selected,
                                    // it might have side effects. This might have been a contributing factor
                                    // to the rename box getting blurred when clicked in.
                                    setSelectedBookIdWithApi(bookId);
                                }
                            }}
                            onRenameComplete={newName => {
                                // Todo: update button with new name.
                                finishRename(newName);
                                setRenaming(false);
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
                {menuItems}
            </Menu>
        </div>
    );
};

const LocalizableMenuItem: React.FunctionComponent<{
    english: string;
    l10nId: string;
    onClick: React.MouseEventHandler<HTMLElement>;
}> = props => {
    const label = useL10n(props.english, props.l10nId);
    return <MenuItem onClick={props.onClick}>{label}</MenuItem>;
};
