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
const BookshelfList: React.FunctionComponent<{ enabled: boolean }> = (
    props,
) => {
    const bookshelves = useGetEnterpriseBookshelves(props.enabled);
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

    const render = async (enabled: boolean) => {
        await act(async () => {
            renderRoot(<BookshelfList enabled={enabled} />, container);
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

    it("asks nothing while disabled", async () => {
        await render(false);

        expect(mockGet).not.toHaveBeenCalled();
        expect(mockGetEntries).not.toHaveBeenCalled();
    });

    it("asks Contentful again each time it is enabled, so a later open sees the current list", async () => {
        mockGetEntries.mockResolvedValueOnce(
            contentfulReplyWithShelves(["shelf-a", "shelf-b"]),
        );
        await render(true);
        expect(shownShelves()).toBe("none,shelf-a,shelf-b");

        // The Collection Settings dialog stays mounted while closed, then opens again after the
        // subscription's bookshelves have changed.
        await render(false);
        mockGetEntries.mockResolvedValueOnce(
            contentfulReplyWithShelves(["shelf-a", "shelf-c"]),
        );
        await render(true);

        expect(mockGetEntries).toHaveBeenCalledTimes(2);
        expect(shownShelves()).toBe("none,shelf-a,shelf-c");
    });

    it("recovers on a later open from failing to reach Contentful", async () => {
        mockGetEntries.mockRejectedValueOnce(new Error("offline"));
        await render(true);
        expect(shownShelves()).toBe("none error");

        await render(false);
        mockGetEntries.mockResolvedValueOnce(
            contentfulReplyWithShelves(["shelf-a"]),
        );
        await render(true);

        expect(shownShelves()).toBe("none,shelf-a");
    });
});
