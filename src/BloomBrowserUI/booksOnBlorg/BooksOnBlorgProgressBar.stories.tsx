import { css } from "@emotion/react";

import { BooksOnBlorgProgressBar } from "./BooksOnBlorgProgressBar";

const barFrame = (progressBar: JSX.Element) => (
    <div
        css={css`
            width: 500px;
            height: 75px;
            padding-left: 20px;
            background-color: rgb(45, 45, 45);
        `}
    >
        {progressBar}
    </div>
);

export default {
    title: "Books on Blorg/Progress",
};

export const Default = () => barFrame(<BooksOnBlorgProgressBar />);
