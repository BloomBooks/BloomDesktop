import Grid from "@material-ui/core/Grid";
import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import { Button } from "@material-ui/core";

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
