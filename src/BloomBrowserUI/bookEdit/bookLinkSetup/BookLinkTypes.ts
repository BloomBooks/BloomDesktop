export interface BookInfoForLinks {
    id: string;
    title: string;
    realTitle?: string;
    thumbnail?: string;
    pageLength?: number;
}

export interface PageInfoForLinks {
    pageId: number;
    thumbnail: string;
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
