import type { IBloomComponentConfig } from "../../component-tester/componentTypes";
import type { LinkTargetInfo } from "../LinkTargetChooser";

export const createMockBook = (index: number) => {
    const colors = [
        "4CAF50",
        "2196F3",
        "FF9800",
        "E91E63",
        "9C27B0",
        "00BCD4",
        "CDDC39",
        "FF5722",
        "607D8B",
        "795548",
    ];
    const color = colors[index % colors.length];
    const bookNumber = index + 1;
    const guid = `${bookNumber}aaaa-bbbb-cccc-dddd-eeeeeeee${bookNumber.toString().padStart(4, "0")}`;
    const pageCount = bookNumber * 10;

    return {
        id: guid,
        title: `Book ${bookNumber}`,
        folderName: `${bookNumber} Book ${bookNumber}`,
        folderPath: `c:/books/${bookNumber} Book ${bookNumber}`,
        thumbnail: `data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%23${color}'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook ${bookNumber}%3C/text%3E%3C/svg%3E`,
        pageLength: pageCount + 1,
    };
};

export const mockBooks = Array.from({ length: 10 }, (_, i) =>
    createMockBook(i),
);

// Set up mock API responses for manual testing without backend
// This needs to happen at module load time, which occurs when the component is requested
import { mockReplies } from "../../../utils/bloomApi";

mockReplies["editView/currentBookId"] = {
    data: mockBooks[0].id,
};

mockReplies["collections/books?realTitle=false"] = {
    data: mockBooks,
};

mockReplies["collections/books?realTitle=true"] = {
    data: mockBooks,
};

// Mock page data for all books - we'll use a generic response
// Since mockReplies uses exact URL matches, we need to create entries for each book
mockBooks.forEach((book, index) => {
    const bookId = encodeURIComponent(book.id);
    const bookNumber = index + 1;
    const pageCount = bookNumber * 10;

    const pages = [
        { key: "cover", caption: "Cover", content: "" },
        ...Array.from({ length: pageCount }, (_, i) => ({
            key: `${i + 1}`,
            caption: `Page ${i + 1}`,
            content: "",
        })),
    ];

    mockReplies[`pageList/pages?book-id=${bookId}`] = {
        data: {
            pages,
            pageLayout: "A5Portrait",
            cssFiles: [],
        },
    };

    mockReplies[
        `pageList/bookAttributesThatMayAffectDisplay?book-id=${bookId}`
    ] = {
        data: {},
    };
});

const config: IBloomComponentConfig<{
    open: boolean;
    currentURL: string;
    onClose?: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
}> = {
    defaultProps: {
        open: true,
        currentURL: "",
        onClose: undefined,
        onSelect: undefined,
    },
    modulePath: "../LinkTargetChooser/LinkTargetChooserDialog",
    exportName: "LinkTargetChooserDialog",
};

export default config;
