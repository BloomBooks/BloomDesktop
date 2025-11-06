import * as React from "react";
import { useState, useEffect, useRef } from "react";
import { AxiosError } from "axios";
import { css } from "@emotion/react";
import { Box, Typography } from "@mui/material";
import { PageInfoForLinks } from "../BookGridSetup/BookLinkTypes";
import { get } from "../../utils/bloomApi";
import { PageThumbnail } from "../../bookEdit/pageThumbnailList/PageThumbnail";
import { IPage } from "../../bookEdit/pageThumbnailList/pageThumbnailList";
import {
    chooserBackgroundColor,
    itemBackgroundColor,
    getSelectionOutline,
    itemGap,
} from "./sharedStyles";
import "../../bookEdit/pageThumbnailList/pageThumbnailList.less";
import { chooserButtonPadding } from "./sharedStyles";

export const PageChooser: React.FunctionComponent<{
    bookId?: string;
    bookFolderPath?: string;
    selectedPageId?: string;
    onSelectPage: (page: PageInfoForLinks) => void;
}> = (props) => {
    const { bookId, bookFolderPath, selectedPageId, onSelectPage } = props;
    const [pages, setPages] = useState<IPage[]>([]);
    const [pageLayout, setPageLayout] = useState<string>("A5Portrait");
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [bookAttributes, setBookAttributes] = useState<
        Record<string, string>
    >({});
    const [bookCssFiles, setBookCssFiles] = useState<string[]>([]);

    // Persist reload callbacks in case we grow functionality later.
    const pageIdToRefreshMap = useRef(new Map<string, () => void>());
    const injectedCssLinksRef = useRef<HTMLLinkElement[]>([]);

    useEffect(() => {
        pageIdToRefreshMap.current.clear();
    }, [bookId]);

    useEffect(() => {
        if (!bookId) {
            setPages([]);
            setLoading(false);
            setErrorMessage(null);
            setBookAttributes({});
            setBookCssFiles([]);
            return;
        }

        setLoading(true);
        setErrorMessage(null);
        let canceled = false;

        get(
            `pageList/pages?book-id=${encodeURIComponent(bookId)}`,
            (response) => {
                if (canceled) {
                    return;
                }
                const pageData = (response.data.pages || []) as Array<{
                    key: string;
                    caption: string;
                    content: string;
                }>;
                setPageLayout(response.data.pageLayout || "A5Portrait");

                const pageList: IPage[] = pageData.map((p) => ({
                    key: p.key,
                    caption: p.caption,
                    content: p.content || "",
                }));

                setPages(pageList);
                const cssFiles = Array.isArray(response.data.cssFiles)
                    ? (response.data.cssFiles as string[])
                    : [];
                setBookCssFiles(cssFiles);
                setLoading(false);

                // Auto-select the first page when a new book's pages load
                if (pageList.length > 0 && !selectedPageId) {
                    onSelectPage({
                        pageId: pageList[0].key,
                        caption: pageList[0].caption,
                        thumbnail: pageList[0].content,
                    });
                }
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
                    setBookCssFiles([]);
                }
            },
        );

        return () => {
            canceled = true;
        };
    }, [bookId, selectedPageId, onSelectPage]);

    useEffect(() => {
        if (!bookId) {
            setBookAttributes({});
            return;
        }

        let canceled = false;
        get(
            `pageList/bookAttributesThatMayAffectDisplay?book-id=${encodeURIComponent(bookId)}`,
            (response) => {
                if (canceled) {
                    return;
                }
                const attributes = (response.data || {}) as Record<
                    string,
                    string
                >;
                setBookAttributes(attributes);
            },
            () => {
                if (!canceled) {
                    setBookAttributes({});
                }
            },
        );

        return () => {
            canceled = true;
        };
    }, [bookId]);

    useEffect(() => {
        const existingLinks = injectedCssLinksRef.current;
        existingLinks.forEach((link) => {
            if (link.parentElement) {
                link.parentElement.removeChild(link);
            }
        });
        injectedCssLinksRef.current = [];

        if (!bookId || bookCssFiles.length === 0) {
            return;
        }

        const head = document.head;
        if (!head) {
            return;
        }

        const createdLinks: HTMLLinkElement[] = [];
        bookCssFiles.forEach((file) => {
            if (!file) {
                return;
            }

            const link = document.createElement("link");
            link.rel = "stylesheet";
            link.type = "text/css";
            link.setAttribute("data-page-chooser-css", "true");
            const encodedPath = file
                .split("/")
                .map((segment) => encodeURIComponent(segment))
                .join("/");
            link.href = `/bloom/api/pageList/bookFile/${encodeURIComponent(
                bookId,
            )}/${encodedPath}`;
            head.appendChild(link);
            createdLinks.push(link);
        });

        injectedCssLinksRef.current = createdLinks;

        return () => {
            createdLinks.forEach((link) => {
                if (link.parentElement) {
                    link.parentElement.removeChild(link);
                }
            });
            injectedCssLinksRef.current = [];
        };
    }, [bookCssFiles, bookId]);

    if (!bookId) {
        return (
            <Box
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    height: 100%;
                    background-color: ${chooserBackgroundColor};
                    padding: ${chooserButtonPadding};
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
                    background-color: ${chooserBackgroundColor};
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
                    background-color: ${chooserBackgroundColor};
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
                height: 100%;
                //background-color: #2e2e2e;
                display: flex;
                flex-direction: column;
            `}
        >
            <div
                id="wrapperForBodyAttributes"
                {...(bookAttributes as Record<string, string>)}
                css={css`
                    display: flex;
                    flex-direction: column;
                    height: 100%;
                    min-height: 0;
                `}
            >
                <Box
                    id="pageGridWrapper"
                    css={css`
                        flex: 1 1 auto;
                        overflow-y: auto;
                        padding: 10px;
                        background-color: ${chooserBackgroundColor};
                    `}
                >
                    <Box
                        id="pageGrid"
                        css={css`
                            display: flex;
                            flex-wrap: wrap;
                            gap: ${itemGap};
                        `}
                    >
                        {pages.map((page) => {
                            const isSelected = selectedPageId === page.key;
                            return (
                                <div
                                    key={page.key}
                                    id={page.key}
                                    data-caption={page.caption}
                                    data-testid={`page-${page.key}`}
                                    className={"gridItem"}
                                    css={css`
                                        position: relative;
                                        width: 80px;
                                        height: 105px;
                                        float: none;
                                        display: inline-block;
                                        vertical-align: top;
                                        border: 1px solid #ccc;
                                        border-radius: 2px;
                                        padding: 5px;
                                        background-color: ${itemBackgroundColor};
                                        cursor: pointer;
                                        outline: ${getSelectionOutline(
                                            isSelected,
                                        )};
                                        & .pageContainer {
                                            float: none;
                                        }
                                        & .thumbnailCaption {
                                            left: 0 !important;
                                            right: 0 !important;
                                            text-align: center !important;
                                        }
                                    `}
                                    onClick={() =>
                                        onSelectPage({
                                            pageId: page.key,
                                            caption: page.caption,
                                            thumbnail: page.content,
                                        })
                                    }
                                >
                                    <PageThumbnail
                                        page={page}
                                        left={false}
                                        pageLayout={pageLayout}
                                        bookId={bookId}
                                        bookFolderPath={bookFolderPath}
                                        configureReloadCallback={(
                                            id,
                                            callback,
                                        ) =>
                                            pageIdToRefreshMap.current.set(
                                                id,
                                                callback,
                                            )
                                        }
                                    />
                                </div>
                            );
                        })}
                    </Box>
                </Box>
            </div>
        </Box>
    );
};
