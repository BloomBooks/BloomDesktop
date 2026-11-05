import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";

const { mockGet, mockGetEntries } = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockGetEntries: vi.fn(),
}));

vi.mock("../utils/bloomApi", () => ({
    get: mockGet,
}));

vi.mock("../contentful/ContentfulContext", () => ({
    getContentfulClient: () => ({ getEntries: mockGetEntries }),
}));

import { useGetEnterpriseBookshelves } from "./useGetEnterpriseBookshelves";

// Shows the bookshelf values the hook reports, so the test can read them from the DOM.
const BookshelfList: React.FunctionComponent = () => {
    const bookshelves = useGetEnterpriseBookshelves();
    return (
        <div data-testid="shelves">
            {bookshelves.validBookshelves.map((shelf) => shelf.value).join(",")}
            {bookshelves.error ? " error" : ""}
        </div>
    );
};

// What Contentful returns for a subscription whose bookshelves have these url keys.
function contentfulReplyWithShelves(urlKeys: string[]) {
    return {
        items: [
            {
                fields: {
                    collections: urlKeys.map((urlKey) => ({
                        fields: { urlKey, label: urlKey },
                    })),
                },
            },
        ],
    };
}

describe("useGetEnterpriseBookshelves", () => {
    let container: HTMLDivElement;

    const render = async () => {
        await act(async () => {
            renderRoot(<BookshelfList />, container);
        });
    };

    const shownShelves = () =>
        container.querySelector('[data-testid="shelves"]')?.textContent;

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        mockGet.mockReset();
        mockGet.mockImplementation(
            (_url: string, successCallback: (r: unknown) => void) => {
                successCallback({
                    data: {
                        subscriptionDescriptor: "Acme",
                        defaultBookshelfUrlKey: "",
                    },
                });
            },
        );
        mockGetEntries.mockReset();
    });

    afterEach(() => {
        unmountRoot(container);
        container.remove();
    });

    it("lists the subscription's bookshelves after none", async () => {
        mockGetEntries.mockResolvedValueOnce(
            contentfulReplyWithShelves(["shelf-a", "shelf-b"]),
        );
        await render();

        expect(mockGet).toHaveBeenCalledWith(
            "settings/bookShelfData",
            expect.any(Function),
        );
        expect(shownShelves()).toBe("none,shelf-a,shelf-b");
    });

    it("reports an error when it cannot reach Contentful", async () => {
        mockGetEntries.mockRejectedValueOnce(new Error("offline"));
        await render();

        expect(mockGetEntries).toHaveBeenCalledTimes(1);
        expect(shownShelves()).toBe("none error");
    });
});
