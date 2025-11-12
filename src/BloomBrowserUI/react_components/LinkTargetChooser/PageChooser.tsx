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
type StylesheetDefinition = { href: string; key: string };

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

const attachStylesheets = (stylesheets: StylesheetDefinition[]) => {
    const addedLinks: HTMLLinkElement[] = [];
    stylesheets.forEach(({ href, key }) => {
        ensureStylesheet(href, key, addedLinks);
    });
    return addedLinks;
};

const removeStylesheets = (links: HTMLLinkElement[]) => {
    links.forEach((linkElement) => {
        if (linkElement.parentNode) {
            linkElement.parentNode.removeChild(linkElement);
        }
    });
};

const usePageChooserStyles = (bookId?: string) => {
    useEffect(() => {
        const addedLinks = attachStylesheets(staticStylesheets);
        return () => removeStylesheets(addedLinks);
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

        const href = `/bloom/api/collections/bookFile?book-id=${encodeURIComponent(
            bookId,
        )}&file=${encodeURIComponent("appearance.css")}`;
        const addedLinks = attachStylesheets([{ href, key: "appearance" }]);
        return () => removeStylesheets(addedLinks);
    }, [bookId]);
};

const toPageInfo = (
    page: PageWithXMatter,
    index: number,
): PageInfoForLinks => ({
    pageId: index === 0 ? "cover" : page.key,
    actualPageId: page.key,
    caption: page.caption,
    thumbnail: page.content,
    pageIndex: index,
    isFrontCover: index === 0,
    isXMatter: page.isXMatter,
    disabled: page.disabled,
});

const renderStatus = (
    message: string,
    options?: { color?: "error" | "textSecondary"; padding?: string },
) => (
    <Box
        css={css`
            display: flex;
            align-items: center;
            justify-content: center;
            height: 100%;
            background-color: ${chooserBackgroundColor};
            padding: ${options?.padding ?? "10px"};
        `}
    >
        <Typography color={options?.color}>{message}</Typography>
    </Box>
);

const PageItemComponent: React.FunctionComponent<{
    page: PageWithXMatter;
    pageInfo: PageInfoForLinks;
    isSelected: boolean;
    pageLayout: string;
    bookId: string;
    bookFolderPath?: string;
    onSelectPage: (pageInfo: PageInfoForLinks) => void;
    onConfigureReloadCallback: (id: string, callback: () => void) => void;
}> = (props) => {
    const isDisabled = Boolean(props.pageInfo.disabled);
    const itemRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        if (!props.isSelected || !itemRef.current) {
            return;
        }

        itemRef.current.scrollIntoView({
            behavior: "smooth",
            block: "nearest",
            inline: "nearest",
        });
    }, [props.isSelected]);

    const handleSelect = () => {
        if (isDisabled) {
            return;
        }
        props.onSelectPage(props.pageInfo);
    };

    const classNames = ["link-target-page"];
    if (props.isSelected) {
        classNames.push("link-target-page--selected");
    }
    if (isDisabled) {
        classNames.push("link-target-page--disabled");
    }

    return (
        <div
            id={props.page.key}
            data-caption={props.page.caption}
            data-testid={`page-${props.page.key}`}
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
                ${props.isSelected ? selectedStyle : ""}
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
            data-selected={props.isSelected ? "true" : undefined}
            data-disabled={isDisabled ? "true" : undefined}
        >
            <PageThumbnail
                page={props.page}
                left={false}
                pageLayout={props.pageLayout}
                bookId={props.bookId}
                bookFolderPath={props.bookFolderPath}
                configureReloadCallback={props.onConfigureReloadCallback}
            />
        </div>
    );
};

const PageItem = React.memo(PageItemComponent, (prevProps, nextProps) => {
    // Only re-render if selection state or page key changes
    return (
        prevProps.page.key === nextProps.page.key &&
        prevProps.isSelected === nextProps.isSelected &&
        prevProps.pageInfo === nextProps.pageInfo &&
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
    usePageChooserStyles(props.bookId);
    const [pages, setPages] = useState<PageWithXMatter[]>([]);
    const [pageLayout, setPageLayout] = useState<string>("A5Portrait");
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [bookAttributes, setBookAttributes] = useState<
        Record<string, string>
    >({});
    // Keep the latest selected page without re-triggering the data fetch effect.
    const selectedPageIdRef = useRef<string | undefined>(props.selectedPageId);
    const onSelectPageRef = useRef(props.onSelectPage);
    const onPagesLoadedRef = useRef(props.onPagesLoaded);

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
        selectedPageIdRef.current = props.selectedPageId;
    }, [props.selectedPageId]);

    useEffect(() => {
        onSelectPageRef.current = props.onSelectPage;
    }, [props.onSelectPage]);

    useEffect(() => {
        onPagesLoadedRef.current = props.onPagesLoaded;
    }, [props.onPagesLoaded]);

    useEffect(() => {
        if (!props.bookId) {
            setPages([]);
            setLoading(false);
            setErrorMessage(null);
            setBookAttributes({});
            onPagesLoadedRef.current?.([]);
            return;
        }

        setLoading(true);
        setErrorMessage(null);
        let canceled = false;

        get(
            `pageList/pages?book-id=${encodeURIComponent(props.bookId)}`,
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

                const pageInfos = pageList.map((page, index) =>
                    toPageInfo(page, index),
                );
                onPagesLoadedRef.current?.(pageInfos);

                // Auto-select the first page when a new book's pages load
                if (pageInfos.length > 0 && !selectedPageIdRef.current) {
                    onSelectPageRef.current(pageInfos[0]);
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
    }, [props.bookId, failedToLoadPagesMessage]);

    useEffect(() => {
        // Pull page-level attributes that influence thumbnail rendering.
        if (!props.bookId) {
            setBookAttributes({});
            return;
        }

        let canceled = false;
        get(
            `pageList/bookAttributesThatMayAffectDisplay?book-id=${encodeURIComponent(
                props.bookId,
            )}`,
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
    }, [props.bookId]);

    const pageInfos = React.useMemo(
        () => pages.map((page, index) => toPageInfo(page, index)),
        [pages],
    );

    if (!props.bookId) {
        return renderStatus(selectBookMessage, {
            color: "textSecondary",
            padding: chooserButtonPadding,
        });
    }

    if (loading) {
        return renderStatus(loadingPagesMessage);
    }

    if (errorMessage) {
        return renderStatus(errorMessage, { color: "error" });
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
                            const pageInfo = pageInfos[index]!;
                            const matchesSelection =
                                props.selectedPageId === pageInfo.pageId ||
                                props.selectedPageId === pageInfo.actualPageId;
                            const isSelected =
                                !pageInfo.disabled && matchesSelection;
                            return (
                                <PageItem
                                    key={page.key}
                                    page={page}
                                    pageInfo={pageInfo}
                                    isSelected={isSelected}
                                    pageLayout={pageLayout}
                                    bookId={props.bookId!}
                                    bookFolderPath={props.bookFolderPath}
                                    onSelectPage={props.onSelectPage}
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
