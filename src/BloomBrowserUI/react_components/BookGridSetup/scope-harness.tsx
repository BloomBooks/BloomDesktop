// This file is used by `yarn scope` to open the component in a browser. It does so in a way that works with skills/scope/skill.md.

import * as React from "react";
import { css } from "@emotion/react";
import { BookGridSetup } from "./BookGridSetup";
import { BookInfoForLinks, Link } from "./BookLinkTypes";

type BookGridSetupHarnessProps = {
    sourceBooks: BookInfoForLinks[];
    initialLinks: Link[];
};

const BookGridSetupHarness: React.FC<BookGridSetupHarnessProps> = (props) => {
    const [links, setLinks] = React.useState<Link[]>(props.initialLinks);

    return (
        <div
            css={css`
                height: 100vh;
                background: #f5f5f5;
                padding: 20px;
                box-sizing: border-box;
            `}
        >
            <div
                css={css`
                    margin-bottom: 12px;
                    font-family: sans-serif;
                    font-size: 13px;
                    color: #333;
                `}
            >
                Current links: {links.length}
            </div>
            <div
                css={css`
                    height: calc(100% - 28px);
                `}
            >
                <BookGridSetup
                    sourceBooks={props.sourceBooks}
                    links={links}
                    onLinksChanged={(newLinks) => {
                        setLinks(newLinks);
                        // Keep a visible trace in DevTools without needing a backend.
                        console.log("BookGridSetup links changed:", newLinks);
                    }}
                />
            </div>
        </div>
    );
};

const placeholderPng =
    "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

const makeBook = (id: string, title: string): BookInfoForLinks => {
    return {
        id,
        title,
        folderName: title,
        thumbnail: placeholderPng,
    };
};

const defaultBooks: BookInfoForLinks[] = [
    makeBook("book1", "The Moon Book"),
    makeBook("book2", "Counting Fun"),
    makeBook("book3", "Animal Friends"),
    makeBook("book4", "Story Builders"),
];

export const withPreselectedLinks: React.FC = () => {
    return (
        <BookGridSetupHarness
            sourceBooks={defaultBooks}
            initialLinks={[
                { book: defaultBooks[0]! },
                { book: defaultBooks[1]! },
            ]}
        />
    );
};

export const blank: React.FC = () => {
    return (
        <BookGridSetupHarness sourceBooks={defaultBooks} initialLinks={[]} />
    );
};

export default blank;
