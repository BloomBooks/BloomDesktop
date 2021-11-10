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

    const handleDuplicateBook = () => {
        handleClose();
        BloomApi.postString(
            "collections/duplicateBook?collection-id=" + props.collectionId,
            selectedBookId
        );
    };

    const handleDeleteBook = () => {
        handleClose();
        BloomApi.postString(
            "collections/deleteBook?collection-id=" + props.collectionId,
            selectedBookId
        );
    };

    const dupBook = useL10n(
        "Duplicate Book",
        "CollectionTab.BookMenu.DuplicateBook"
    );
    const delBook = useL10n("Delete Book", "CollectionTab.BookMenu.DeleteBook");

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
                <MenuItem onClick={handleDuplicateBook}>{dupBook}</MenuItem>
                <MenuItem onClick={handleDeleteBook}>{delBook}</MenuItem>
            </Menu>
        </div>
    );
};
