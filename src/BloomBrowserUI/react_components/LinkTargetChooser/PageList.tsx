import * as React from "react";
import { useState, useEffect } from "react";
import { AxiosError } from "axios";
import { css } from "@emotion/react";
import { Box, Typography } from "@mui/material";
import {
    PageInfoForLinks,
    BookInfoForLinks,
} from "../BookGridSetup/BookLinkTypes";
import { get } from "../../utils/bloomApi";

export const PageList: React.FunctionComponent<{
    selectedBook: BookInfoForLinks | null;
    selectedPageId: string | null;
    onSelectPage: (page: PageInfoForLinks) => void;
}> = (props) => {
    const [pages, setPages] = useState<PageInfoForLinks[]>([]);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const bookId = props.selectedBook?.id ?? null;

    useEffect(() => {
        if (!bookId) {
            setPages([]);
            setLoading(false);
            setErrorMessage(null);
            return;
        }

        setLoading(true);
        setErrorMessage(null);
        let canceled = false;

        const query = `?book-id=${encodeURIComponent(bookId)}`;

        get(
            `pageList/pages${query}`,
            (response) => {
                if (canceled) {
                    return;
                }
                const skeletonPages = (response.data.pages || []) as Array<{
                    key: string;
                    caption: string;
                }>;

                // Initialize with empty content; we'll fill via per-page calls
                const initial: PageInfoForLinks[] = skeletonPages.map(
                    (p, index) => ({
                        pageId: p.key,
                        thumbnail: "",
                        caption:
                            p.caption ||
                            (index === 0 ? "Cover Page" : `Page ${index}`),
                    }),
                );
                setPages(initial);

                // Fetch content for each page
                skeletonPages.forEach((p, _idx) => {
                    get(
                        `pageList/pageContent?id=${encodeURIComponent(p.key)}&book-id=${encodeURIComponent(bookId)}`,
                        (contentResp) => {
                            if (canceled) {
                                return;
                            }
                            setPages((prev) => {
                                const next = [...prev];
                                const i = next.findIndex(
                                    (x) => x.pageId === p.key,
                                );
                                if (i >= 0) {
                                    next[i] = {
                                        ...next[i],
                                        thumbnail:
                                            contentResp.data.content || "",
                                    };
                                }
                                return next;
                            });
                        },
                    );
                });
                setLoading(false);
            },
            (error: AxiosError) => {
                if (!canceled) {
                    setErrorMessage(
                        error.response?.statusText ||
                            error.message ||
                            "Failed to load pages",
                    );
                    setLoading(false);
                    setPages([]);
                }
            },
        );

        return () => {
            canceled = true;
        };
    }, [bookId]);

    if (!props.selectedBook) {
        return (
            <Box
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    height: 100%;
                    background-color: lightgray;
                    padding: 10px;
                `}
            >
                <Typography color="textSecondary">
                    Select a book to see its pages
                </Typography>
            </Box>
        );
    }

    if (loading) {
        return (
            <Box
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    height: 100%;
                    background-color: lightgray;
                    padding: 10px;
                `}
            >
                <Typography>Loading pages...</Typography>
            </Box>
        );
    }

    if (errorMessage) {
        return (
            <Box
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    height: 100%;
                    background-color: lightgray;
                    padding: 10px;
                `}
            >
                <Typography color="error">{errorMessage}</Typography>
            </Box>
        );
    }

    return (
        <Box
            css={css`
                display: flex;
                flex-wrap: wrap;
                gap: 8px;
                height: 100%;
                align-content: flex-start;
                overflow-y: scroll;
                background-color: lightgray;
                padding: 10px;
            `}
        >
            {pages.map((page) => (
                <Box
                    key={page.pageId}
                    data-testid={`page-${page.pageId}`}
                    onClick={() => props.onSelectPage(page)}
                    css={css`
                        width: 140px;
                        cursor: pointer;
                        background-color: ${props.selectedPageId === page.pageId
                            ? "rgb(25, 118, 210)"
                            : "#505050"};
                        outline: ${props.selectedPageId === page.pageId
                            ? "3px solid rgb(25, 118, 210)"
                            : "none"};
                        color: white;
                        padding: 0 0 8px 0;
                        display: flex;
                        flex-direction: column;
                        align-items: center;
                    `}
                >
                    <div
                        // Render the HTML content returned by the API. If we don't have content yet, fall back to an empty box.
                        dangerouslySetInnerHTML={{ __html: page.thumbnail }}
                        css={css`
                            width: 100%;
                            aspect-ratio: 16/9;
                            height: 100px;
                            overflow: hidden;
                            background: #333;
                            margin-bottom: 8px;
                        `}
                    />
                    <Typography
                        css={css`
                            color: white;
                            font-size: 12px;
                            text-align: center;
                        `}
                    >
                        {page.caption ||
                            (page.pageId === "cover" ? "Cover Page" : `Page`)}
                    </Typography>
                </Box>
            ))}
        </Box>
    );
};
