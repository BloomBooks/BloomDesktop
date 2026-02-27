import * as React from "react";
import { css } from "@emotion/react";
import AutoStoriesIcon from "@mui/icons-material/AutoStories";
import { Menu, MenuItem, Select } from "@mui/material";
import { Link } from "./BookLinkTypes";
import { BookLinkCard } from "./BookLinkCard";
import {
    DndContext,
    closestCenter,
    KeyboardSensor,
    PointerSensor,
    useSensor,
    useSensors,
    DragEndEvent,
    DragOverlay,
    DragStartEvent,
} from "@dnd-kit/core";
import {
    arrayMove,
    SortableContext,
    sortableKeyboardCoordinates,
    useSortable,
    rectSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useEffect, useRef } from "react";
import { bookGridContainerStyles } from "./sharedStyles";
import { get } from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { useL10n } from "../l10nHooks";

const pageIconThumbnailUrl =
    "/bloom/bookEdit/pageThumbnailList/pageControls/bookGridPageIcon.svg";

interface PageOption {
    pageId: string;
    actualPageId: string;
    caption: string;
    pageIndex: number;
    disabled?: boolean;
}

interface ThumbnailOption {
    id: string;
    thumbnailUrl: string;
    source: "pageIcon" | "pageImage";
    imageIndex?: number;
}

const getDefaultLabelForLink = (link: Link, currentBookId?: string) => {
    if (currentBookId && link.book.id === currentBookId) {
        if (!link.page) {
            return "Book";
        }
        if (link.page?.isFrontCover || link.page?.pageId === "cover") {
            return "Front Cover";
        }
        const pageNumber = link.page?.pageIndex ?? 1;
        return `Page ${pageNumber}`;
    }
    return link.book.title || link.book.folderName || "";
};

const resolveRelativeToBookFileUrl = (
    rawPath: string,
    bookId: string,
): string => {
    const resolved = new URL(rawPath, "http://bloom.invalid/");
    let normalizedPath = resolved.pathname.replace(/^\/+/, "");
    try {
        normalizedPath = decodeURIComponent(normalizedPath);
    } catch {
        // keep normalizedPath as-is if decoding fails
    }

    let fileParam = normalizedPath;
    if (resolved.search) {
        fileParam += resolved.search;
    }

    const params = new URLSearchParams();
    params.set("book-id", bookId);
    params.set("file", fileParam);

    return `/bloom/api/collections/bookFile?${params.toString()}${resolved.hash}`;
};

const toFetchableImageUrl = (
    rawValue: string,
    bookId: string,
): string | undefined => {
    const trimmed = rawValue.trim();
    if (!trimmed || trimmed.startsWith("#") || trimmed.startsWith("data:")) {
        return undefined;
    }

    if (trimmed.startsWith("/")) {
        return trimmed;
    }

    if (trimmed.startsWith("http://") || trimmed.startsWith("https://")) {
        return trimmed;
    }

    return resolveRelativeToBookFileUrl(trimmed, bookId);
};

const extractImageUrlsFromPageContent = (
    pageContentHtml: string,
    bookId: string,
): string[] => {
    if (!pageContentHtml) {
        return [];
    }

    const parser = new DOMParser();
    const pageDocument = parser.parseFromString(pageContentHtml, "text/html");
    const urls = new Set<string>();

    pageDocument.querySelectorAll("img").forEach((imageElement) => {
        const src = imageElement.getAttribute("src");
        if (!src) {
            return;
        }
        const fetchableUrl = toFetchableImageUrl(src, bookId);
        if (fetchableUrl) {
            urls.add(fetchableUrl);
        }
    });

    pageDocument.querySelectorAll("[style]").forEach((styledElement) => {
        const styleValue = styledElement.getAttribute("style") || "";
        const regex = /url\((['"]?)([^'")]+)\1\)/gi;
        let match = regex.exec(styleValue);
        while (match) {
            const fetchableUrl = toFetchableImageUrl(match[2], bookId);
            if (fetchableUrl) {
                urls.add(fetchableUrl);
            }
            match = regex.exec(styleValue);
        }
    });

    return Array.from(urls);
};

const IndividualPageControls: React.FunctionComponent<{
    link: Link;
    onUpdateLink: (updatedLink: Link) => void;
    currentBookId?: string;
    thumbnailMenuAnchorEl?: HTMLElement | null;
    thumbnailMenuOpen?: boolean;
    onCloseThumbnailMenu?: () => void;
}> = (props) => {
    const [pageOptions, setPageOptions] = React.useState<PageOption[]>([]);
    const [thumbnailOptions, setThumbnailOptions] = React.useState<
        ThumbnailOption[]
    >([]);
    const [loadingPages, setLoadingPages] = React.useState<boolean>(false);
    const [loadingThumbnails, setLoadingThumbnails] =
        React.useState<boolean>(false);
    const isSameBookAsCurrent =
        !!props.currentBookId && props.link.book.id === props.currentBookId;

    const selectedPageId = props.link.page?.pageId || "book";

    React.useEffect(() => {
        let canceled = false;
        setLoadingPages(true);
        get(
            `pageList/pages?book-id=${encodeURIComponent(props.link.book.id)}`,
            (response) => {
                if (canceled) {
                    return;
                }

                const rawPages =
                    (response.data.pages as Array<{
                        key: string;
                        caption: string;
                        isXMatter?: boolean;
                    }>) || [];

                const contentPages = rawPages.filter(
                    (page, index) => index !== 0 && !page.isXMatter,
                );

                const options: PageOption[] = [
                    {
                        pageId: "book",
                        actualPageId: "",
                        caption: "Book",
                        pageIndex: 0,
                        disabled: isSameBookAsCurrent,
                    },
                    ...contentPages.map((page, index) => ({
                        pageId: page.key,
                        actualPageId: page.key,
                        caption: `Page ${index + 1}`,
                        pageIndex: index + 1,
                    })),
                ];
                setPageOptions(options);
                setLoadingPages(false);

                let selectedOption =
                    options.find(
                        (option) => option.pageId === selectedPageId,
                    ) || options[0];
                if (selectedOption?.disabled) {
                    selectedOption = options.find((option) => !option.disabled);
                }
                if (
                    !selectedOption ||
                    selectedOption.pageId === selectedPageId
                ) {
                    return;
                }

                if (selectedOption.pageId === "book") {
                    const updatedLink: Link = {
                        ...props.link,
                        page: undefined,
                    };
                    updatedLink.label = getDefaultLabelForLink(
                        updatedLink,
                        props.currentBookId,
                    );
                    props.onUpdateLink(updatedLink);
                    return;
                }

                const currentPage = props.link.page || {
                    pageId: selectedOption.pageId,
                    thumbnail: pageIconThumbnailUrl,
                };
                const updatedLink: Link = {
                    ...props.link,
                    page: {
                        ...currentPage,
                        pageId: selectedOption.pageId,
                        actualPageId: selectedOption.actualPageId,
                        caption: selectedOption.caption,
                        pageIndex: selectedOption.pageIndex,
                        isFrontCover: selectedOption.pageId === "cover",
                        thumbnail: pageIconThumbnailUrl,
                        thumbnailSource: undefined,
                        thumbnailImageIndex: undefined,
                    },
                };

                updatedLink.label = getDefaultLabelForLink(
                    updatedLink,
                    props.currentBookId,
                );
                props.onUpdateLink(updatedLink);
            },
            () => {
                if (!canceled) {
                    setPageOptions([]);
                    setLoadingPages(false);
                }
            },
        );

        return () => {
            canceled = true;
        };
    }, [props.link.book.id, isSameBookAsCurrent]);

    React.useEffect(() => {
        const selectedOption = pageOptions.find(
            (option) => option.pageId === selectedPageId,
        );
        if (!selectedOption) {
            setThumbnailOptions([]);
            return;
        }

        if (selectedOption.pageId === "book") {
            setLoadingThumbnails(false);
            setThumbnailOptions([]);
            return;
        }

        let canceled = false;
        setLoadingThumbnails(true);
        get(
            `pageList/pageContent?page-id=${encodeURIComponent(
                selectedOption.actualPageId,
            )}&book-id=${encodeURIComponent(props.link.book.id)}`,
            (response) => {
                if (canceled) {
                    return;
                }

                const imageUrls = extractImageUrlsFromPageContent(
                    response.data.content || "",
                    props.link.book.id,
                );

                const options: ThumbnailOption[] = [
                    {
                        id: "page-icon",
                        thumbnailUrl: pageIconThumbnailUrl,
                        source: "pageIcon",
                    },
                    ...imageUrls.map((url, index) => ({
                        id: `page-image-${index}`,
                        thumbnailUrl: url,
                        source: "pageImage",
                        imageIndex: index,
                    })),
                ];

                setThumbnailOptions(options);
                setLoadingThumbnails(false);

                const currentPage = props.link.page || {
                    pageId: selectedPageId,
                    thumbnail: pageIconThumbnailUrl,
                };

                let selectedThumbnailOption: ThumbnailOption | undefined;

                if (
                    currentPage.thumbnailSource === "pageImage" &&
                    currentPage.thumbnailImageIndex !== undefined
                ) {
                    selectedThumbnailOption = options.find(
                        (option) =>
                            option.source === "pageImage" &&
                            option.imageIndex ===
                                currentPage.thumbnailImageIndex,
                    );
                }

                if (
                    !selectedThumbnailOption &&
                    currentPage.thumbnail &&
                    !!currentPage.thumbnailSource
                ) {
                    selectedThumbnailOption = options.find(
                        (option) =>
                            option.thumbnailUrl === currentPage.thumbnail,
                    );
                }

                if (!selectedThumbnailOption) {
                    selectedThumbnailOption =
                        options.find(
                            (option) => option.source === "pageImage",
                        ) || options[0];
                }

                if (
                    !selectedThumbnailOption ||
                    (currentPage.thumbnail ===
                        selectedThumbnailOption.thumbnailUrl &&
                        currentPage.thumbnailSource ===
                            selectedThumbnailOption.source &&
                        currentPage.thumbnailImageIndex ===
                            selectedThumbnailOption.imageIndex)
                ) {
                    return;
                }

                const updatedLink: Link = {
                    ...props.link,
                    page: {
                        ...currentPage,
                        pageId: selectedOption.pageId,
                        actualPageId: selectedOption.actualPageId,
                        caption: selectedOption.caption,
                        pageIndex: selectedOption.pageIndex,
                        isFrontCover: selectedOption.pageId === "cover",
                        thumbnail: selectedThumbnailOption.thumbnailUrl,
                        thumbnailSource: selectedThumbnailOption.source,
                        thumbnailImageIndex: selectedThumbnailOption.imageIndex,
                    },
                };

                updatedLink.label = getDefaultLabelForLink(
                    updatedLink,
                    props.currentBookId,
                );

                props.onUpdateLink(updatedLink);
            },
            () => {
                if (!canceled) {
                    setThumbnailOptions([]);
                    setLoadingThumbnails(false);
                }
            },
        );

        return () => {
            canceled = true;
        };
    }, [pageOptions, props.link.book.id, selectedPageId]);

    const selectedThumbnailId = React.useMemo(() => {
        if (props.link.page?.thumbnailSource === "pageImage") {
            const index = props.link.page.thumbnailImageIndex;
            if (index !== undefined) {
                return `page-image-${index}`;
            }
        }
        const thumbnailUrl = props.link.page?.thumbnail;
        if (!thumbnailUrl) {
            return "page-icon";
        }
        return (
            thumbnailOptions.find(
                (option) => option.thumbnailUrl === thumbnailUrl,
            )?.id || "page-icon"
        );
    }, [props.link.page, thumbnailOptions]);

    const handlePageChanged = (chosenPageId: string) => {
        const chosenPage = pageOptions.find(
            (option) => option.pageId === chosenPageId,
        );
        if (!chosenPage || chosenPage.disabled) {
            return;
        }

        if (chosenPage.pageId === "book") {
            const updatedLink: Link = {
                ...props.link,
                page: undefined,
            };

            updatedLink.label = getDefaultLabelForLink(
                updatedLink,
                props.currentBookId,
            );

            props.onUpdateLink(updatedLink);
            return;
        }

        const currentPage = props.link.page || {
            pageId: chosenPage.pageId,
            thumbnail: pageIconThumbnailUrl,
        };
        const updatedLink: Link = {
            ...props.link,
            page: {
                ...currentPage,
                pageId: chosenPage.pageId,
                actualPageId: chosenPage.actualPageId,
                caption: chosenPage.caption,
                pageIndex: chosenPage.pageIndex,
                isFrontCover: chosenPage.pageId === "cover",
                thumbnail: pageIconThumbnailUrl,
                thumbnailSource: undefined,
                thumbnailImageIndex: undefined,
            },
        };

        updatedLink.label = getDefaultLabelForLink(
            updatedLink,
            props.currentBookId,
        );

        props.onUpdateLink(updatedLink);
    };

    const handleThumbnailChangedById = (thumbnailOptionId: string) => {
        const chosenThumbnailOption = thumbnailOptions.find(
            (option) => option.id === thumbnailOptionId,
        );
        if (!chosenThumbnailOption || !props.link.page) {
            return;
        }

        props.onUpdateLink({
            ...props.link,
            page: {
                ...props.link.page,
                thumbnail: chosenThumbnailOption.thumbnailUrl,
                thumbnailSource: chosenThumbnailOption.source,
                thumbnailImageIndex: chosenThumbnailOption.imageIndex,
            },
        });
    };

    const linkId = props.link.id || props.link.book.id;

    return (
        <div
            css={css`
                width: 90px;
                display: flex;
                flex-direction: column;
                gap: 4px;
                margin-top: 4px;
            `}
        >
            <Select
                size="small"
                value={selectedPageId}
                onChange={(event) =>
                    handlePageChanged(event.target.value as string)
                }
                disabled={loadingPages || pageOptions.length === 0}
                data-testid={`target-page-select-${linkId}`}
                css={css`
                    width: 100%;
                    font-size: 12px;
                    .MuiSelect-select {
                        padding: 2px 24px 2px 6px !important;
                        min-height: 24px;
                        display: flex;
                        align-items: center;
                    }
                `}
            >
                {loadingPages && <MenuItem>Loading pages...</MenuItem>}
                {!loadingPages &&
                    pageOptions.map((option) => (
                        <MenuItem
                            key={option.pageId}
                            value={option.pageId}
                            disabled={!!option.disabled}
                        >
                            {option.caption}
                        </MenuItem>
                    ))}
            </Select>
            <Menu
                anchorEl={props.thumbnailMenuAnchorEl || null}
                open={!!props.thumbnailMenuOpen && !!props.link.page}
                onClose={props.onCloseThumbnailMenu}
                PaperProps={{
                    style: {
                        maxHeight: 280,
                    },
                }}
            >
                {loadingThumbnails && (
                    <MenuItem disabled={true}>Loading thumbnails...</MenuItem>
                )}
                {!loadingThumbnails &&
                    thumbnailOptions.map((option) => {
                        const isSelected = option.id === selectedThumbnailId;
                        return (
                            <MenuItem
                                key={option.id}
                                value={option.id}
                                selected={isSelected}
                                onClick={() => {
                                    handleThumbnailChangedById(option.id);
                                    props.onCloseThumbnailMenu?.();
                                }}
                            >
                                {option.source === "pageIcon" ? (
                                    <AutoStoriesIcon
                                        css={css`
                                            color: ${kBloomBlue};
                                            font-size: 20px;
                                        `}
                                    />
                                ) : (
                                    <img
                                        src={option.thumbnailUrl}
                                        css={css`
                                            width: 36px;
                                            height: 36px;
                                            object-fit: cover;
                                        `}
                                    />
                                )}
                            </MenuItem>
                        );
                    })}
            </Menu>
        </div>
    );
};

interface BookTargetListProps {
    links: Link[];
    onRemoveBook: (link: Link) => void;
    onUpdateLink?: (updatedLink: Link) => void;
    currentBookId?: string;
    showAddLinkButton?: boolean;
    onAddLink?: () => void;
    onReorderBooks?: (newLinks: Link[]) => void;
}

const SortableBookItem: React.FC<{
    link: Link;
    onRemove: () => void;
    onUpdateLink?: (updatedLink: Link) => void;
    currentBookId?: string;
}> = ({ link, onRemove, onUpdateLink, currentBookId }) => {
    const [thumbnailMenuAnchorEl, setThumbnailMenuAnchorEl] =
        React.useState<HTMLElement | null>(null);

    const {
        attributes,
        listeners,
        setNodeRef,
        transform,
        transition,
        isDragging,
    } = useSortable({
        id: link.id || link.book.id,
    });

    const style = {
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
    };

    return (
        <div
            ref={setNodeRef}
            style={style}
            data-testid={`target-book-${link.book.id}`}
        >
            <div {...attributes} {...listeners}>
                <BookLinkCard
                    link={link}
                    onRemove={onRemove}
                    onLabelChange={
                        onUpdateLink
                            ? (newLabel) =>
                                  onUpdateLink({
                                      ...link,
                                      label: newLabel,
                                  })
                            : undefined
                    }
                    onThumbnailClick={
                        onUpdateLink && !!link.page
                            ? (event) => {
                                  setThumbnailMenuAnchorEl(
                                      event.currentTarget as HTMLElement,
                                  );
                              }
                            : undefined
                    }
                    showThumbnailDropdownAffordance={
                        !!onUpdateLink && !!link.page
                    }
                    preferFolderName={false}
                    isDragging={isDragging}
                    matchOnPageStyle={true}
                />
            </div>
            {onUpdateLink && !isDragging && (
                <IndividualPageControls
                    link={link}
                    onUpdateLink={onUpdateLink}
                    currentBookId={currentBookId}
                    thumbnailMenuAnchorEl={thumbnailMenuAnchorEl}
                    thumbnailMenuOpen={!!thumbnailMenuAnchorEl}
                    onCloseThumbnailMenu={() => setThumbnailMenuAnchorEl(null)}
                />
            )}
        </div>
    );
};

export const BookTargetList: React.FC<BookTargetListProps> = ({
    links,
    onRemoveBook,
    onUpdateLink,
    currentBookId,
    showAddLinkButton,
    onAddLink,
    onReorderBooks,
}) => {
    const addLinkLabel = useL10n(
        "+ Add Link",
        "BookGridSetup.AddLink",
        "Button text to add one more link tile in Table of Contents grid setup",
    );
    const linkRefs = useRef<Map<string, HTMLDivElement>>(new Map());
    const prevLinksLengthRef = useRef(links.length);
    const [activeId, setActiveId] = React.useState<string>();

    // Auto-scroll to newly added books so users can see confirmation of their action.
    // We track the previous length to distinguish additions from removals or reordering.
    useEffect(() => {
        // Only scroll if a new link was added (length increased)
        if (links.length > prevLinksLengthRef.current) {
            const lastLinkId = links[links.length - 1]?.id;
            if (lastLinkId) {
                const element = linkRefs.current.get(lastLinkId);
                element?.scrollIntoView({
                    behavior: "smooth",
                    block: "nearest",
                });
            }
        }
        prevLinksLengthRef.current = links.length;
    }, [links]);

    // Configure drag sensors for both mouse and keyboard interaction.
    // The distance threshold prevents accidental drags when clicking the remove button.
    const sensors = useSensors(
        useSensor(PointerSensor, {
            activationConstraint: {
                distance: 8, // Require 8px of movement before drag starts
            },
        }),
        useSensor(KeyboardSensor, {
            coordinateGetter: sortableKeyboardCoordinates,
        }),
    );

    const handleDragStart = (event: DragStartEvent) => {
        setActiveId(event.active.id as string);
    };

    const handleDragEnd = (event: DragEndEvent) => {
        const { active, over } = event;
        // Reorder the links array based on where the item was dropped.
        // The dnd-kit library provides the active (dragged) and over (drop target) elements.
        if (over && active.id !== over.id) {
            const oldIndex = links.findIndex((link) => link.id === active.id);
            const newIndex = links.findIndex((link) => link.id === over.id);
            if (oldIndex >= 0 && newIndex >= 0) {
                const newLinks = arrayMove(links, oldIndex, newIndex);
                onReorderBooks?.(newLinks);
            }
        }
        setActiveId(undefined);
    };

    // Track which link is being dragged so we can render it in the DragOverlay.
    // This provides visual feedback during the drag operation.
    const activeLink = activeId
        ? links.find((link) => link.id === activeId)
        : null;

    return (
        <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
        >
            <SortableContext
                items={links.map((link) => link.id || link.book.id)}
                strategy={rectSortingStrategy} // Use rect strategy for grid layouts
            >
                <div css={bookGridContainerStyles}>
                    {links.map((link, index) => (
                        <div
                            key={link.id || `${link.book.id}-${index}`}
                            ref={(el) => {
                                const linkId = link.id;
                                if (!linkId) {
                                    return;
                                }
                                if (el) {
                                    linkRefs.current.set(linkId, el);
                                } else {
                                    linkRefs.current.delete(linkId);
                                }
                            }}
                        >
                            <SortableBookItem
                                link={link}
                                onRemove={() => onRemoveBook(link)}
                                onUpdateLink={onUpdateLink}
                                currentBookId={currentBookId}
                            />
                        </div>
                    ))}
                    {showAddLinkButton && onAddLink && (
                        <button
                            type="button"
                            onClick={onAddLink}
                            data-testid="toc-grid-add-link"
                            css={css`
                                width: 96px;
                                min-height: 85px;
                                border: 1px dashed #c8c8c8;
                                border-radius: 5px;
                                background: white;
                                color: #444;
                                cursor: pointer;
                                font-size: 13px;
                                font-family: "Andika", sans-serif;
                            `}
                        >
                            {addLinkLabel}
                        </button>
                    )}
                </div>
            </SortableContext>
            <DragOverlay>
                {activeLink ? (
                    <BookLinkCard
                        link={activeLink}
                        preferFolderName={false}
                        isDragging={true}
                        matchOnPageStyle={true}
                    />
                ) : null}
            </DragOverlay>
        </DndContext>
    );
};
