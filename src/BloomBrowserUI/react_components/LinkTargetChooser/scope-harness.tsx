// This file is used by `yarn scope` to open the component in a browser. It does so in a way that works with skills/scope/skill.md.

import * as React from "react";
import { css } from "@emotion/react";
import { LinkTargetChooserDialog } from "./LinkTargetChooserDialog";
import { mockReplies } from "../../utils/bloomApi";

type BookInfo = {
    id: string;
    title: string;
    folderName: string;
    folderPath: string;
    thumbnail: string;
    pageLength: number;
};

type PageListResponse = {
    pages: Array<{
        key: string;
        caption: string;
        content: string;
        isXMatter?: boolean;
        disabled?: boolean;
    }>;
    selectedPageId: string;
    pageLayout: string;
};

const setMockReply = (urlSuffix: string, data: unknown): void => {
    // bloomApi.get() expects mockReplies entries to look like an AxiosResponse.
    const table = mockReplies as unknown as Record<string, unknown>;
    table[urlSuffix] = { data };
};

const svgThumbnail = (label: string, color: string): string => {
    const svg =
        `<svg xmlns='http://www.w3.org/2000/svg' width='100' height='120'>` +
        `<rect width='100' height='120' fill='${color}'/>` +
        `<text x='50' y='60' text-anchor='middle' fill='white' font-size='12'>${label}</text>` +
        `</svg>`;
    return `data:image/svg+xml,${encodeURIComponent(svg)}`;
};

const makeBooks = (count: number): BookInfo[] => {
    const palette = [
        "#4CAF50",
        "#2196F3",
        "#FF9800",
        "#E91E63",
        "#9C27B0",
        "#00BCD4",
        "#CDDC39",
        "#FF5722",
        "#607D8B",
        "#795548",
    ];

    return Array.from({ length: count }, (_, index) => {
        const bookNumber = index + 1;
        const id = `book${bookNumber}`;
        const color = palette[index % palette.length]!;
        const title = `Book ${bookNumber}`;
        const pageLength = 6; // includes cover
        return {
            id,
            title,
            folderName: `${bookNumber} ${title}`,
            folderPath: `C:/fake/${id}`,
            thumbnail: svgThumbnail(title, color),
            pageLength,
        };
    });
};

const pageHtml = (caption: string, color: string): string => {
    return (
        `<div class='A5Portrait bloom-page'>` +
        `<div class='marginBox'>` +
        `<div style='width:160px;height:100px;background:${color};color:#fff;display:flex;align-items:center;justify-content:center;font-family:sans-serif;'>` +
        `${caption}` +
        `</div>` +
        `</div>` +
        `</div>`
    );
};

const setLinkTargetChooserMocks = (options: {
    books: BookInfo[];
    currentBookId: string;
    clipboardText: string;
}): void => {
    setMockReply("collections/books?realTitle=false", options.books);
    setMockReply("editView/currentBookId", options.currentBookId);
    setMockReply("common/clipboardText", { data: options.clipboardText });

    for (const book of options.books) {
        const pages: PageListResponse = {
            pages: [
                {
                    key: "cover",
                    caption: "Cover",
                    content: "",
                    isXMatter: true,
                },
                { key: "1", caption: "Page 1", content: "" },
                { key: "2", caption: "Page 2", content: "" },
                { key: "3", caption: "Page 3", content: "" },
                { key: "4", caption: "Page 4", content: "" },
                { key: "5", caption: "Page 5", content: "" },
            ],
            selectedPageId: "cover",
            pageLayout: "A5Portrait",
        };

        setMockReply(
            `pageList/pages?book-id=${encodeURIComponent(book.id)}`,
            pages,
        );

        setMockReply(
            `pageList/bookAttributesThatMayAffectDisplay?book-id=${encodeURIComponent(
                book.id,
            )}`,
            {},
        );

        // PageThumbnail requests page content lazily as thumbnails render.
        const palette = {
            cover: "#4CAF50",
            "1": "#2196F3",
            "2": "#FF9800",
            "3": "#E91E63",
            "4": "#9C27B0",
            "5": "#00BCD4",
        } as Record<string, string>;

        for (const page of pages.pages) {
            const color = palette[page.key] ?? "#607D8B";
            setMockReply(
                `pageList/pageContent?page-id=${page.key}&book-id=${encodeURIComponent(
                    book.id,
                )}`,
                { content: pageHtml(page.caption, color) },
            );
        }
    }
};

type LinkTargetChooserHarnessProps = {
    currentURL: string;
    books: BookInfo[];
    currentBookId: string;
    clipboardText: string;
};

const LinkTargetChooserHarness: React.FC<LinkTargetChooserHarnessProps> = (
    props,
) => {
    // This harness runs without a Bloom backend by mocking bloomApi.get()/getAsync calls.
    // We must do this as a side-effect to affect the shared bloomApi mockReplies table.
    React.useEffect(() => {
        setLinkTargetChooserMocks({
            books: props.books,
            currentBookId: props.currentBookId,
            clipboardText: props.clipboardText,
        });
    }, [props.books, props.currentBookId, props.clipboardText]);

    return (
        <div
            css={css`
                height: 100vh;
                background: #f5f5f5;
                padding: 16px;
                box-sizing: border-box;
            `}
        >
            <LinkTargetChooserDialog
                currentURL={props.currentURL}
                onSetUrl={(url) =>
                    console.log("LinkTargetChooser onSetUrl:", url)
                }
            />
        </div>
    );
};

const defaultBooks = makeBooks(10);

export const pageOnlyUrl: React.FC = () => {
    return (
        <LinkTargetChooserHarness
            currentURL="#5"
            books={defaultBooks}
            currentBookId="book3"
            clipboardText="https://example.com"
        />
    );
};

export const hashOnlyUrlWithCover: React.FC = () => {
    return (
        <LinkTargetChooserHarness
            currentURL="#cover"
            books={defaultBooks}
            currentBookId="book2"
            clipboardText="https://example.com"
        />
    );
};

export const missingBook: React.FC = () => {
    return (
        <LinkTargetChooserHarness
            currentURL="/book/999"
            books={defaultBooks}
            currentBookId="book2"
            clipboardText="https://example.com"
        />
    );
};

export const bookPathUrlSimplifiedToHash: React.FC = () => {
    return (
        <LinkTargetChooserHarness
            currentURL="/book/book3#2"
            books={defaultBooks}
            currentBookId="book3"
            clipboardText="https://example.com"
        />
    );
};

export const preselectedPageScroll: React.FC = () => {
    const books = makeBooks(25);
    return (
        <LinkTargetChooserHarness
            currentURL="/book/book23#3"
            books={books}
            currentBookId="book3"
            clipboardText="https://example.com"
        />
    );
};

export const blank: React.FC = () => {
    return (
        <LinkTargetChooserHarness
            currentURL="/book/book10#4"
            books={defaultBooks}
            currentBookId="book3"
            clipboardText="https://example.com"
        />
    );
};

export default blank;
