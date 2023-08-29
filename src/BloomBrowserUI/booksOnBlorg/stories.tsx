/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { storiesOf } from "@storybook/react";
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

storiesOf("Books on Blorg/Progress", module).add("Default", () =>
    barFrame(<BooksOnBlorgProgressBar />)
);
