import Grid from "@material-ui/core/Grid";
import React = require("react");
import "BooksOfCollection.less";
import { BloomApi } from "../utils/bloomApi";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import { Button } from "@material-ui/core";

export const BooksOfCollection: React.FunctionComponent<{
    collectionId: string;
}> = props => {
    if (!props.collectionId) {
        window.alert("null collectionId");
    }
    const [books] = BloomApi.useApiJson(
        `collections/books?collection-id=${encodeURIComponent(
            props.collectionId
        )}`
    );
    const bookArray = books as Array<any>;
    const [selectedBookId, selectBook] = BloomApi.useApiString(
        "collections/selected-book-id",
        ""
    );

    const [contextMousePoint, setContextMousePoint] = React.useState<
        | {
              mouseX: number;
              mouseY: number;
          }
        | undefined
    >();

    const handleClick = (event: React.MouseEvent<HTMLDivElement>) => {
        // event.preventDefault();
        // setContextMousePoint({
        //     mouseX: event.clientX - 2,
        //     mouseY: event.clientY - 4
        // });
    };

    const handleClose = () => {
        setContextMousePoint(undefined);
    };

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
                {bookArray?.map(book => {
                    return (
                        <BookButton
                            key={book.id}
                            book={book}
                            selected={selectedBookId === book.id}
                            onClick={bookId => {
                                selectBook(bookId);
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
                <MenuItem onClick={handleClose}>
                    Do something with book
                </MenuItem>
                <MenuItem onClick={handleClose}>
                    Do something else with book
                </MenuItem>
            </Menu>
        </div>
    );
};

export const BookButton: React.FunctionComponent<{
    book: any;
    selected: boolean;
    onClick: (bookId: string) => void;
}> = props => {
    // const [thumbnailUrl] = BloomApi.useApiString(
    //     `collection/book/thumbnail?book-id=${props.book.id}`,
    //     ""
    // );
    // TODO: the c# had Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont,
    return (
        <Grid item={true}>
            {/* <div className="bookButton">
                <img
                    src={`/bloom/api/collection/book/thumbnail?book-id=${props.book.id}`}
                    alt="book thumbnail"
                />
                <div className="bookTitle">{props.book.title}</div>
            </div> */}
            <Button
                className={"bookButton" + (props.selected ? " selected" : "")}
                variant="outlined"
                size="large"
                onClick={() => props.onClick(props.book.id)}
                startIcon={
                    <img
                        src={`/bloom/api/collections/book/thumbnail?book-id=${
                            props.book.id
                        }&collection-id=${encodeURIComponent(
                            props.book.collectionId
                        )}`}
                    />
                }
            >
                {props.book.title}
            </Button>
        </Grid>
    );
};
