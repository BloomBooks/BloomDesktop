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
import { Divider } from "@material-ui/core";
import { Book } from "@material-ui/icons";

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
    const [clickedBookId, setClickedBookId] = useState("");
    const [
        selectedBookId,
        setSelectedBookIdWithApi
    ] = BloomApi.useApiStringState(
        `collections/selected-book-id?${collectionQuery}`,
        ""
    );
    const selectedBookInfo = useMonitorBookSelection();
    const collection = BloomApi.useApiData(
        `collections/collectionProps?collection-id=${props.collectionId}`,
        {
            isFactoryInstalled: true,
            containsDownloadedBooks: false
        }
    );

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
        let bookTarget = target.closest(".selected-book-wrapper");
        if (bookTarget == null) {
            bookTarget = target.closest(".book-wrapper");
            if (bookTarget == null) {
                // We're not going to do our own right-click menu; let whatever default happens go ahead.
                return;
            }
            // BookButton puts this attribute on the {selected-}book-wrapper element so this code
            // can use it to determine which book was selected.
            const bookId = bookTarget.getAttribute("data-book-id")!;
            setSelectedBookIdWithApi(bookId);
        }
        setClickedBookId(bookTarget.getAttribute("data-book-id")!);

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
        l10nId?: string;
        // One of these two must be provided. If both are, onClick is used and command is ignored.
        command?: string;
        onClick?: React.MouseEventHandler<HTMLElement>;
        // Involves making changes to the book; therefore, can only be done in the one editable collection
        // (unless shouldInclude returns true), and if we're in a Team Collection, the book must be checked out.
        requiresEditPermission?: boolean;
        // by default, a menu item is shown if we're in the editable collection and
        // editability requirements are satisfied, and not otherwise.
        // A menu item which should be shown in other collections may define this
        // function and have it return true for those cases where it should be shown.
        // The function is not currently used for the editable collection.
        shouldInclude?: () => boolean;
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
            command: "bookCommand/openFolderOnDisk",
            shouldInclude: () => true // show for all collections (except factory)
        },
        { label: "-" },
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
            command: "bookCommand/importSpreadsheetContent",
            requiresEditPermission: true
        },
        {
            label: "Save as single file (.bloom)...",
            l10nId: "CollectionTab.BookMenu.SaveAsBloomToolStripMenuItem",
            command: "bookCommand/saveAsDotBloom"
        },
        { label: "-" },
        {
            label: "Update Thumbnail",
            l10nId: "CollectionTab.BookMenu.UpdateThumbnail",
            command: "bookCommand/updateThumbnail",
            requiresEditPermission: true // marginal, but it does change the content of the book folder
        },
        {
            label: "Update Book",
            l10nId: "CollectionTab.BookMenu.UpdateFrontMatterToolStrip",
            command: "bookCommand/updateBook",
            requiresEditPermission: true // marginal, but it does change the content of the book folder
        },
        {
            label: "Rename",
            l10nId: "CollectionTab.BookMenu.Rename",
            onClick: () => handleRename(),
            requiresEditPermission: true
        },
        { label: "-" },
        {
            label: "Delete Book",
            l10nId: "CollectionTab.BookMenu.DeleteBook",
            command: "collections/bookCommand/deleteBook",
            requiresEditPermission: true,
            // also shown if it's the editable collection and we have edit permission.
            // This just covers the one other case where it is shown.
            shouldInclude: () => collection.containsDownloadedBooks
        }
    ];

    const menuItemsT = menuItemsSpecs
        .map((spec: MenuItemSpec) => {
            if (spec.label === "-") {
                return <Divider />;
            }
            if (props.isEditableCollection) {
                // eliminate commands that require permission to change the book, if we don't have it
                if (spec.requiresEditPermission && !selectedBookInfo.editable) {
                    return undefined;
                }
            } else {
                // remove all commands except those that have a shouldInclude() function that returns true
                if (!spec.shouldInclude || !spec.shouldInclude()) {
                    return undefined;
                }
            }

            // It should be possible to use spec.onClick || () => handleBookCommand(spec.command!) inline,
            // but I can't make Typescript accept it.
            let clickAction: React.MouseEventHandler = () =>
                handleBookCommand(spec.command!);
            if (spec.onClick) {
                clickAction = spec.onClick;
            }
            return (
                <LocalizableMenuItem
                    english={spec.label}
                    l10nId={spec.l10nId!}
                    onClick={clickAction}
                ></LocalizableMenuItem>
            );
        })
        .filter(x => x); // that is, remove ones where the map function returned undefined

    // Can't find a really good way to tell that an element is a Divider.
    // But we only have Dividers and LocalizableMenuItems in this list,
    // so it's a Dividier if it doesn't have one of the required props of LocalizableMenuItem.
    const isDivider = (element: JSX.Element): boolean => {
        return !element.props.english;
    };
    // filter out dividers if (a) followed by another divider, or (b) at the start or end of the list
    const menuItems = menuItemsT.filter(
        (elt, index) =>
            !isDivider(elt!) ||
            (index > 0 &&
                index < menuItemsT.length - 1 &&
                !isDivider(menuItemsT[index + 1]!))
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
            {collection.isFactoryInstalled || (
                <Menu
                    keepMounted={true}
                    // When we right-click on a book that's not selected, we invoke a BloomApi to select it,
                    // and eventually that produces a render where it is selected. But before that, we go ahead
                    // and set contextMousePoint. So there tends to be a brief flash of the context menu that should be
                    // rendered for the (previously) selected book. To prevent this, the click action sets clickedBookId
                    // to the book we want the menu for, and this check stops the menu appearing until the render
                    // with the right book selected.
                    open={
                        !!contextMousePoint &&
                        clickedBookId === selectedBookInfo.id
                    }
                    onClose={handleClose}
                    anchorReference="anchorPosition"
                    anchorPosition={anchor}
                >
                    {menuItems}
                </Menu>
            )}
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
