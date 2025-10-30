import { css } from "@emotion/react";
import { CollectionBookList } from "./collectionBookList";

import { Meta, StoryObj } from "@storybook/react-vite";
import { IBookInfo } from "../collectionsTab/BooksOfCollection";
import { useState } from "react";

const meta: Meta = {
    title: "CollectionBookList",
};

export default meta;

type Story = StoryObj;

export const BookList: Story = {
    name: "BookList",
    render: () => {
        const [bookCount, setBookCount] = useState(0);

        return (
            <div
                css={css`
                    .booklist {
                        width: 350px;
                        height: 150px;
                    }
                `}
            >
                <CollectionBookList
                    className="booklist"
                    onBooksLoaded={(bookCollection: Array<IBookInfo>) => {
                        setBookCount(bookCollection?.length);
                    }}
                    css={css``}
                />
                <p>The number of books in this collection is {bookCount}.</p>
            </div>
        );
    },
};
