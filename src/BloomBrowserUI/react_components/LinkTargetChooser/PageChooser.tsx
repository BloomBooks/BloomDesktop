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

type PageWithXMatter = IPage & { isXMatter?: boolean };

const PageItemComponent: React.FunctionComponent<{
    page: PageWithXMatter;
    pageIndex: number;
    isSelected: boolean;
    isDisabled: boolean;
    pageLayout: string;
    bookId: string;
    bookFolderPath?: string;
    onSelectPage: (pageInfo: PageInfoForLinks) => void;
    onConfigureReloadCallback: (id: string, callback: () => void) => void;
}> = (props) => {
    const {
        page,
        pageIndex,
        isSelected,
        pageLayout,
        bookId,
        bookFolderPath,
        onSelectPage,
        onConfigureReloadCallback,
        isDisabled,
    } = props;

    const isFrontCover = pageIndex === 0;

    const handleSelect = () => {
        if (isDisabled) {
            return;
        }

        onSelectPage({
            pageId: isFrontCover ? "cover" : page.key,
            actualPageId: page.key,
            caption: page.caption,
            thumbnail: page.content,
            pageIndex,
            isFrontCover,
            isXMatter: page.isXMatter,
        });
    };

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
                cursor: ${isDisabled ? "not-allowed" : "pointer"};
                opacity: ${isDisabled ? 0.5 : 1};
                outline: ${getSelectionOutline(isSelected)};
                & .pageContainer {
                    float: none;
                }
                & .thumbnailCaption {
                    left: 0 !important;
                    right: 0 !important;
                    text-align: center !important;
                }
            `}
            onClick={handleSelect}
            aria-disabled={isDisabled ? "true" : undefined}
            data-disabled={isDisabled ? "true" : undefined}
        >
            <PageThumbnail
                page={page}
                left={false}
                pageLayout={pageLayout}
                bookId={bookId}
                bookFolderPath={bookFolderPath}
                configureReloadCallback={onConfigureReloadCallback}
            />
            {isDisabled && (
                <div
                    css={css`
                        position: absolute;
                        inset: 0;
                        background-color: rgba(255, 255, 255, 0.5);
                        border-radius: 2px;
                        pointer-events: none;
                    `}
                />
            )}
        </div>
    );
};

const PageItem = React.memo(PageItemComponent, (prevProps, nextProps) => {
    // Only re-render if selection state or page key changes
    return (
        prevProps.page.key === nextProps.page.key &&
        prevProps.pageIndex === nextProps.pageIndex &&
        prevProps.isSelected === nextProps.isSelected &&
        prevProps.isDisabled === nextProps.isDisabled &&
        prevProps.pageLayout === nextProps.pageLayout &&
        prevProps.bookId === nextProps.bookId
    );
});

const PageChooserComponent: React.FunctionComponent<{
    bookId?: string;
    bookFolderPath?: string;
    selectedPageId?: string;
    onSelectPage: (page: PageInfoForLinks) => void;
    onPagesLoaded?: (pages: PageInfoForLinks[]) => void;
}> = (props) => {
    const {
        bookId,
        bookFolderPath,
        selectedPageId,
        onSelectPage,
        onPagesLoaded,
    } = props;
    const [pages, setPages] = useState<PageWithXMatter[]>([]);
    const [pageLayout, setPageLayout] = useState<string>("A5Portrait");
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [bookAttributes, setBookAttributes] = useState<
        Record<string, string>
    >({});
    // Keep the latest selected page without re-triggering the data fetch effect.
    const selectedPageIdRef = useRef<string | undefined>(selectedPageId);

    // Stable callback for configuring reload callbacks
    const handleConfigureReloadCallback = React.useCallback(
        // PageThumbnail expects this hook, but the chooser never calls the callback.
        (_id: string, _callback: () => void) => undefined,
        [],
    );

    useEffect(() => {
        selectedPageIdRef.current = selectedPageId;
    }, [selectedPageId]);

    useEffect(() => {
        if (!bookId) {
            setPages([]);
            setLoading(false);
            setErrorMessage(null);
            setBookAttributes({});
            onPagesLoaded?.([]);
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
                    isXMatter?: boolean;
                }>;
                setPageLayout(response.data.pageLayout || "A5Portrait");

                const pageList: PageWithXMatter[] = pageData.map((p) => ({
                    key: p.key,
                    caption: p.caption,
                    content: p.content || "",
                    isXMatter: p.isXMatter,
                }));

                setPages(pageList);
                setLoading(false);

                onPagesLoaded?.(
                    pageList.map((page, index) => ({
                        pageId: index === 0 ? "cover" : page.key,
                        actualPageId: page.key,
                        caption: page.caption,
                        thumbnail: page.content,
                        pageIndex: index,
                        isFrontCover: index === 0,
                        isXMatter: page.isXMatter,
                    })),
                );

                // Auto-select the first page when a new book's pages load
                if (pageList.length > 0 && !selectedPageIdRef.current) {
                    onSelectPage({
                        pageId: "cover",
                        actualPageId: pageList[0].key,
                        caption: pageList[0].caption,
                        thumbnail: pageList[0].content,
                        pageIndex: 0,
                        isFrontCover: true,
                        isXMatter: pageList[0].isXMatter,
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
                }
            },
        );

        return () => {
            canceled = true;
        };
    }, [bookId, onSelectPage, onPagesLoaded]);

    useEffect(() => {
        // Pull page-level attributes that influence thumbnail rendering.
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
                contain: layout; /* Isolate layout changes to prevent affecting parent dialog */
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
                        {pages.map((page, index) => {
                            const isCoverSelected =
                                selectedPageId === "cover" && index === 0;
                            const isFrontCover = index === 0;
                            const isDisabled =
                                Boolean(page.isXMatter) && !isFrontCover;
                            const isSelected =
                                !isDisabled &&
                                (selectedPageId === page.key ||
                                    isCoverSelected);
                            return (
                                <PageItem
                                    key={page.key}
                                    page={page}
                                    pageIndex={index}
                                    isSelected={isSelected}
                                    isDisabled={isDisabled}
                                    pageLayout={pageLayout}
                                    bookId={bookId}
                                    bookFolderPath={bookFolderPath}
                                    onSelectPage={onSelectPage}
                                    onConfigureReloadCallback={
                                        handleConfigureReloadCallback
                                    }
                                />
                            );
                        })}
                    </Box>
                </Box>
            </div>
        </Box>
    );
};

// Memoized so the chooser doesn't redraw all thumbnails when parent props change.
// Without this, selecting a page would cause every PageItem to re-render because
// LinkTargetChooser hands PageChooser new props on each selection.
export const PageChooser = React.memo(PageChooserComponent);
