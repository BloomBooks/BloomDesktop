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
