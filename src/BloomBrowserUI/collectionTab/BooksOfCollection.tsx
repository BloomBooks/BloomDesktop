import Grid from "@material-ui/core/Grid";
import React = require("react");
import "BooksOfCollection.less";
import { BloomApi } from "../utils/bloomApi";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import { BookButton } from "./BookButton";

export const BooksOfCollection: React.FunctionComponent<{
    collectionId: string;
}> = props => {
    if (!props.collectionId) {
        window.alert("null collectionId");
    }
    const collectionQuery = React.useMemo(() => {
        return `collection-id=${encodeURIComponent(props.collectionId)}`;
    }, [props.collectionId]);
    const [books] = BloomApi.useApiJson(`collections/books?${collectionQuery}`);
    const bookArray = books as Array<any>;
    const [selectedBookId, selectBook] = BloomApi.useApiString(
        `collections/selected-book-id?${collectionQuery}`,
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
