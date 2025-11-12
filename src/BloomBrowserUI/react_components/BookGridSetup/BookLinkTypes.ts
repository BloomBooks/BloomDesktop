export interface BookInfoForLinks {
    id: string;
    title?: string;
    folderName?: string;
    folderPath?: string;
    thumbnail?: string;
    pageLength?: number;
}

export interface PageInfoForLinks {
    pageId: string; // Bloom page ids are strings
    thumbnail: string; // For current book this may contain HTML content for the page thumbnail
    caption?: string; // optional label/caption for display
    actualPageId?: string; // original id from backend when pageId is normalized (e.g., cover)
    pageIndex?: number; // zero-based index within the book
    isFrontCover?: boolean; // true when this page represents the front cover
    isXMatter?: boolean; // true when this page is part of the book's XMatter (front/back matter)
    disabled?: boolean; // true when the page should be displayed but not selectable
}

export interface Link {
    book: BookInfoForLinks;
    page?: PageInfoForLinks;
}

export interface CollectionInfoForLinkChoosing {
    name: string;
    books: BookInfoForLinks[];
}

export type ThumbnailGenerator = (bookId: string, pageNumber: number) => string;
