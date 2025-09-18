import { css, List, ListItem, ListItemText } from "@mui/material";
import { useApiData } from "../utils/bloomApi";
import { IBookInfo } from "../collectionsTab/BooksOfCollection";
import { useEffect } from "react";

export const CollectionBookList: React.FunctionComponent<{
    className?: string;
    onBooksLoaded?: (books: Array<IBookInfo>) => void;
}> = (props) => {
    const bookCollection = useApiData<Array<IBookInfo>>(
        `collections/books`,
        [],
    );

    useEffect(() => {
        if (props.onBooksLoaded) {
            props.onBooksLoaded(bookCollection);
        }
    }, [bookCollection, props.onBooksLoaded]);

    return (
        <List
            className={props.className}
            css={css`
                flex-direction: column;
                overflow-y: auto;
                border: solid;
                border-width: thin;
                padding-block: 2px;
                white-space: nowrap;
            `}
        >
            {bookCollection?.map((book) => (
                <ListItem
                    key={book.id}
                    css={css`
                        padding-block: 2px;
                        padding-inline: 4px;
                    `}
                >
                    <ListItemText
                        primary={book.title}
                        css={css`
                            margin: 0px;
                        `}
                    />
                </ListItem>
            ))}
        </List>
    );
};
