import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { BookButton } from "./BookButton";
import { IBookInfo, ICollection } from "./BooksOfCollection";
import { BookSelectionManager } from "./bookSelectionManager";
import {
    ITeamCollectionCapabilities,
    initialBookStatus,
} from "../teamCollection/teamCollectionApi";

// Tests the placeholder rendering added for dogfood batch 1, item 7 (progressive join): a book
// with notYetDownloaded=true (a Cloud Team Collection repo book that hasn't finished downloading
// to this computer yet) renders as a simple, non-interactive-looking placeholder instead of the
// normal, fully-interactive book button -- no thumbnail request, no context menu (SAFETY: no
// dangerous action -- edit/checkout/publish/delete/rename -- must be reachable on a placeholder).
// Clicking it still posts to collections/selected-book, which is how CollectionApi's
// TryPrioritizeNotYetDownloadedBook bumps the book's background download to the front of the
// queue (see CollectionApiTests for the server-side merge logic and
// TeamCollectionAutoApplyTests/RemoteBookAutoApplyQueueTests for the queue itself).
//
// bloomApi's get/post/postString are mocked (no network layer); teamCollectionApi's
// useTColBookStatus/useTeamCollectionCapabilities are mocked to avoid needing a real server round
// trip for the per-book status badge (unrelated to this test). Websocket subscriptions are no-ops
// in tests via vitest.setup.ts's `_SKIP_WEBSOCKET_CREATION_`.

const { mockUseTColBookStatus, mockUseTeamCollectionCapabilities } = vi.hoisted(
    () => ({
        mockUseTColBookStatus: vi.fn(),
        mockUseTeamCollectionCapabilities: vi.fn(),
    }),
);

vi.mock("../teamCollection/teamCollectionApi", async (importOriginal) => {
    const actual =
        await importOriginal<
            typeof import("../teamCollection/teamCollectionApi")
        >();
    return {
        ...actual,
        useTColBookStatus: mockUseTColBookStatus,
        useTeamCollectionCapabilities: mockUseTeamCollectionCapabilities,
    };
});

const { mockGet, mockPost, mockPostString } = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockPost: vi.fn(),
    mockPostString: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        get: mockGet,
        post: mockPost,
        postString: mockPostString,
    };
});

const folderCapabilities: ITeamCollectionCapabilities = {
    supportsVersionHistory: false,
    supportsSharingUi: false,
    requiresSignIn: false,
};

let renderedContainer: HTMLDivElement | undefined;

function render(book: IBookInfo): HTMLDivElement {
    const collection: ICollection = {
        isEditableCollection: true,
        isFactoryInstalled: false,
        containsDownloadedBooks: false,
        id: "C:/Collections/My Collection",
        languageFont: "Andika",
    };
    const manager = new BookSelectionManager();

    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;
    act(() => {
        renderRoot(
            <BookButton
                book={book}
                collection={collection}
                manager={manager}
                lockedToOneDownloadedBook={false}
            />,
            container,
        );
    });
    return container;
}

function makeBook(overrides: Partial<IBookInfo> = {}): IBookInfo {
    return {
        id: "instance-1",
        title: "My Book",
        collectionId: "C:/Collections/My Collection",
        folderName: "My Book",
        folderPath: "C:/Collections/My Collection/My Book",
        isFactory: false,
        ...overrides,
    };
}

afterEach(() => {
    if (renderedContainer) {
        unmountRoot(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    document.body.innerHTML = "";
    mockUseTColBookStatus.mockReset();
    mockUseTeamCollectionCapabilities.mockReset();
    mockGet.mockClear();
    mockPost.mockClear();
    mockPostString.mockClear();
});

describe("BookButton: not-yet-downloaded placeholder (dogfood batch 1, item 7)", () => {
    it("renders the placeholder look for a notYetDownloaded book, not the normal button", () => {
        mockUseTColBookStatus.mockReturnValue(initialBookStatus);
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);

        const container = render(makeBook({ notYetDownloaded: true }));

        expect(container.querySelector(".not-yet-downloaded")).not.toBeNull();
        // The normal interactive button (with its thumbnail image) must not render.
        expect(container.querySelector(".bookButton")).toBeNull();
        expect(container.querySelector("img")).toBeNull();
    });

    it("still shows the book's title on the placeholder", () => {
        mockUseTColBookStatus.mockReturnValue(initialBookStatus);
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);

        const container = render(
            makeBook({ notYetDownloaded: true, title: "Not Yet Here" }),
        );

        expect(container.textContent).toContain("Not Yet Here");
    });

    it("clicking the placeholder posts to collections/selected-book with the book's id (priority-bump plumbing)", () => {
        mockUseTColBookStatus.mockReturnValue(initialBookStatus);
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);

        const container = render(
            makeBook({ notYetDownloaded: true, id: "instance-42" }),
        );
        const placeholder = container.querySelector(
            ".not-yet-downloaded",
        ) as HTMLElement;
        expect(placeholder).not.toBeNull();

        act(() => placeholder.click());

        expect(mockPostString).toHaveBeenCalledWith(
            expect.stringContaining("collections/selected-book"),
            "instance-42",
        );
    });

    it("renders the normal interactive button (not the placeholder) when notYetDownloaded is false", () => {
        mockUseTColBookStatus.mockReturnValue(initialBookStatus);
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);

        const container = render(makeBook({ notYetDownloaded: false }));

        expect(container.querySelector(".not-yet-downloaded")).toBeNull();
        expect(container.querySelector(".bookButton")).not.toBeNull();
    });

    it("renders the normal interactive button when notYetDownloaded is undefined (folder TCs / ordinary collections)", () => {
        mockUseTColBookStatus.mockReturnValue(initialBookStatus);
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);

        const container = render(makeBook()); // notYetDownloaded omitted entirely

        expect(container.querySelector(".not-yet-downloaded")).toBeNull();
        expect(container.querySelector(".bookButton")).not.toBeNull();
    });
});
