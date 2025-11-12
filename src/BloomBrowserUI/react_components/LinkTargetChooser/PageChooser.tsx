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
    selectedStyle,
    itemGap,
    chooserButtonPadding,
} from "./sharedStyles";
import { useL10n } from "../l10nHooks";

type PageWithXMatter = IPage & { isXMatter?: boolean; disabled?: boolean };

const staticStylesheets: Array<{ href: string; key: string }> = [
    {
        href: "/bloom/bookEdit/pageThumbnailList/pageThumbnailList.css",
        key: "page-thumbnail-list",
    },
    {
        href: "/bloom/bookLayout/basePage.css",
        key: "base-page",
    },
    {
        href: "/bloom/bookLayout/previewMode.css",
        key: "preview-mode",
    },
];
const ensureStylesheet = (
    href: string,
    key: string,
    addedLinks: HTMLLinkElement[],
) => {
    const selector = `link[data-page-chooser-css="${key}"]`;
    let linkElement = document.head.querySelector(
        selector,
    ) as HTMLLinkElement | null;

    if (!linkElement) {
        const absoluteHref = new URL(href, window.location.origin).href;
        linkElement = Array.from(
            document.head.querySelectorAll("link[href]"),
        ).find((candidate) => {
            const element = candidate as HTMLLinkElement;
            return element.href === absoluteHref;
        }) as HTMLLinkElement | null;
    }

    if (!linkElement) {
        linkElement = document.createElement("link");
        linkElement.rel = "stylesheet";
        linkElement.type = "text/css";
        linkElement.href = href;
        linkElement.dataset.pageChooserCss = key;
        document.head.appendChild(linkElement);
        addedLinks.push(linkElement);
    } else if (!linkElement.dataset.pageChooserCss) {
        linkElement.dataset.pageChooserCss = key;
    }
};

const usePageChooserStyles = (bookId?: string) => {
    useEffect(() => {
        const addedLinks: HTMLLinkElement[] = [];

        staticStylesheets.forEach(({ href, key }) => {
            ensureStylesheet(href, key, addedLinks);
        });

        return () => {
            addedLinks.forEach((linkElement) => {
                if (linkElement.parentNode) {
                    linkElement.parentNode.removeChild(linkElement);
                }
            });
        };
    }, []);

    useEffect(() => {
        const existing = document.head.querySelectorAll(
            'link[data-page-chooser-css="appearance"]',
        );
        existing.forEach((link) => {
            link.parentNode?.removeChild(link);
        });

        if (!bookId) {
            return;
        }

        const addedLinks: HTMLLinkElement[] = [];
        const href = `/bloom/api/collections/bookFile?book-id=${encodeURIComponent(
            bookId,
        )}&file=${encodeURIComponent("appearance.css")}`;
        ensureStylesheet(href, "appearance", addedLinks);

        return () => {
            addedLinks.forEach((linkElement) => {
                if (linkElement.parentNode) {
                    linkElement.parentNode.removeChild(linkElement);
                }
            });
        };
    }, [bookId]);
};

const PageItemComponent: React.FunctionComponent<{
    page: PageWithXMatter;
    pageIndex: number;
    isSelected: boolean;
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
    } = props;

    const isFrontCover = pageIndex === 0;
    const isDisabled = Boolean(page.disabled);
    const itemRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        if (!isSelected || !itemRef.current) {
            return;
        }

        itemRef.current.scrollIntoView({
            behavior: "smooth",
            block: "nearest",
            inline: "nearest",
        });
    }, [isSelected]);

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
            disabled: isDisabled,
        });
    };

    const classNames = ["link-target-page"];
    if (isSelected) {
        classNames.push("link-target-page--selected");
    }
    if (isDisabled) {
        classNames.push("link-target-page--disabled");
    }

    return (
        <div
            key={page.key}
            id={page.key}
            data-caption={page.caption}
            data-testid={`page-${page.key}`}
            ref={itemRef}
            className={classNames.join(" ")}
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
                opacity: ${isDisabled ? 0.6 : 1};
                ${isSelected ? selectedStyle : ""}
                & .pageContainer {
                    float: none;
                    margin-left: auto;
                    margin-right: auto;
                }
                & .thumbnailCaption {
                    left: 0 !important;
                    right: 0 !important;
                    text-align: center !important;
                }
                text-align: center; // puts the thumbnail caption in center (which is weird, it's not text)
            `}
            onClick={handleSelect}
            data-selected={isSelected ? "true" : undefined}
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
        </div>
    );
};

const PageItem = React.memo(PageItemComponent, (prevProps, nextProps) => {
    // Only re-render if selection state or page key changes
    const prevDisabled = Boolean(prevProps.page.disabled);
    const nextDisabled = Boolean(nextProps.page.disabled);
    return (
        prevProps.page.key === nextProps.page.key &&
        prevProps.pageIndex === nextProps.pageIndex &&
        prevProps.isSelected === nextProps.isSelected &&
        prevDisabled === nextDisabled &&
        prevProps.pageLayout === nextProps.pageLayout &&
        prevProps.bookId === nextProps.bookId &&
        prevProps.bookFolderPath === nextProps.bookFolderPath
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
    usePageChooserStyles(bookId);
    const [pages, setPages] = useState<PageWithXMatter[]>([]);
    const [pageLayout, setPageLayout] = useState<string>("A5Portrait");
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [bookAttributes, setBookAttributes] = useState<
        Record<string, string>
    >({});
    // Keep the latest selected page without re-triggering the data fetch effect.
    const selectedPageIdRef = useRef<string | undefined>(selectedPageId);

    const selectBookMessage = useL10n(
        "Select a book to see its pages",
        "LinkTargetChooser.PageList.SelectBookPrompt",
    );
    const loadingPagesMessage = useL10n(
        "Loading pages...",
        "LinkTargetChooser.PageList.LoadingMessage",
    );
    const failedToLoadPagesMessage = useL10n(
        "Failed to load pages",
        "LinkTargetChooser.PageList.LoadFailed",
    );

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
                    disabled?: boolean;
                }>;
                setPageLayout(response.data.pageLayout || "A5Portrait");

                const pageList: PageWithXMatter[] = pageData.map((p, index) => {
                    const isFrontCover = index === 0;
                    return {
                        key: p.key,
                        caption: p.caption,
                        content: p.content || "",
                        isXMatter: p.isXMatter,
                        disabled:
                            p.disabled ??
                            (Boolean(p.isXMatter) && !isFrontCover),
                    };
                });

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
                        disabled: page.disabled,
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
                        disabled: pageList[0].disabled,
                    });
                }
            },
            (error: AxiosError) => {
                if (!canceled) {
                    const fallbackErrorText =
                        error.response?.statusText ||
                        error.message ||
                        failedToLoadPagesMessage;
                    setErrorMessage(fallbackErrorText);
                    setLoading(false);
                    setPages([]);
                }
            },
        );

        return () => {
            canceled = true;
        };
    }, [bookId, onSelectPage, onPagesLoaded, failedToLoadPagesMessage]);

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
                    {selectBookMessage}
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
                <Typography>{loadingPagesMessage}</Typography>
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
                                page.disabled ??
                                (Boolean(page.isXMatter) && !isFrontCover);
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
