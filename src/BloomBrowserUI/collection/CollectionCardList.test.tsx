import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderTestRoot as render } from "../utils/testRender";
import { CollectionCardList } from "./CollectionCardList";
import { ICollectionInfo } from "./CollectionCard";

// Tests the card-list logic added for dogfood batch 1, item 6: join cards (cloud collections the
// user belongs to but has no local copy of) are appended AFTER the regular collections' maxCardCount
// (10) slice, so they never count against the MRU limit, and clicking one reports its
// collectionId/title rather than opening a local collection. Mocks bloomApi's get/postString (no
// network layer) so CollectionCard's own per-card fetch effect is observable.

const { mockGet, mockPostString } = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockPostString: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        get: mockGet,
        postString: mockPostString,
    };
});

function makeCollection(index: number): ICollectionInfo {
    return {
        path: `C:/Collections/Collection${index}`,
        title: `Collection ${index}`,
        bookCount: index,
        // Set so CollectionCard's per-card unpublished-count effect doesn't fire an unmocked get().
        unpublishedCount: 0,
    };
}

afterEach(() => {
    vi.clearAllMocks();
});

describe("CollectionCardList", () => {
    it("renders all join cards even when regular collections already fill the maxCardCount slice", () => {
        const collections = Array.from({ length: 12 }, (_, i) =>
            makeCollection(i + 1),
        );
        const joinCollections = [
            { collectionId: "join-1", title: "Sunshine Books" },
            { collectionId: "join-2", title: "Rainforest Readers" },
        ];

        const container = render(
            <CollectionCardList
                collections={collections}
                joinCollections={joinCollections}
            />,
        );

        // Only the first 10 (maxCardCount) regular collections should render.
        expect(
            container.querySelectorAll('[data-testid="join-collection-card"]')
                .length,
        ).toBe(2);
        const allCardTitles = Array.from(container.querySelectorAll("h5")).map(
            (el) => el.textContent,
        );
        expect(allCardTitles).not.toContain("Collection 11");
        expect(allCardTitles).not.toContain("Collection 12");
        expect(allCardTitles).toContain("Collection 10");
        expect(allCardTitles).toContain("Sunshine Books");
        expect(allCardTitles).toContain("Rainforest Readers");
    });

    it("calls onJoinCardClick with the collectionId and title when a join card is clicked", () => {
        const onJoinCardClick = vi.fn();
        const container = render(
            <CollectionCardList
                collections={[]}
                joinCollections={[
                    { collectionId: "join-1", title: "Sunshine Books" },
                ]}
                onJoinCardClick={onJoinCardClick}
            />,
        );

        const joinCard = container.querySelector(
            '[data-testid="join-collection-card"]',
        ) as HTMLElement;
        expect(joinCard).not.toBeNull();
        // Click something INSIDE CardActionArea (its title), not the outer Card itself -- click
        // events only bubble UP from the target through ancestors, so clicking the Card root
        // would not reach CardActionArea's onClick handler.
        act(() => (joinCard.querySelector("h5") as HTMLElement).click());

        expect(onJoinCardClick).toHaveBeenCalledWith(
            "join-1",
            "Sunshine Books",
        );
        // Clicking a join card must never try to open a local collection.
        expect(mockPostString).not.toHaveBeenCalledWith(
            "workspace/openCollection",
            expect.anything(),
        );
    });

    it("never fetches an unpublished count for a join card (no local folder exists yet)", () => {
        render(
            <CollectionCardList
                collections={[]}
                joinCollections={[
                    { collectionId: "join-1", title: "Sunshine Books" },
                ]}
            />,
        );

        expect(mockGet).not.toHaveBeenCalled();
    });

    it("renders no join cards when joinCollections is omitted", () => {
        const container = render(
            <CollectionCardList collections={[makeCollection(1)]} />,
        );

        expect(
            container.querySelector('[data-testid="join-collection-card"]'),
        ).toBeNull();
    });
});
