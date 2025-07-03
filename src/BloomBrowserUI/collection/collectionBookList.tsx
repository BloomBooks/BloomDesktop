import { css, List, ListItem, ListItemText } from "@mui/material";
import { useApiData } from "../utils/bloomApi";
import { IBookInfo } from "../collectionsTab/BooksOfCollection";

export const CollectionBookList: React.FunctionComponent<{
    className?: string;
    listItemClassName?: string; // the className of the rows
    callbackfn?: (books: Array<IBookInfo>) => void;
}> = props => {
    const bookCollection = useApiData<Array<IBookInfo>>(
        `collections/books`,
        []
    );

    if (props.callbackfn) {
        props.callbackfn(bookCollection);
    }

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
            {bookCollection?.map(book => (
                <ListItem
                    key={book.id}
                    className={props.listItemClassName}
                    css={css`
                        padding-block: 2px; // padding puts 4px between each line and the borders
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
