import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderTestRoot } from "../utils/testRender";
import { CollectionChooser } from "./CollectionChooser";
import { ISharingLoginState } from "../teamCollection/sharingApi";
import { IJoinCollectionInfo } from "./CollectionCardList";

// Tests the wiring this task's rewrite adds (dogfood batch 1, item 6): join cards (fetched from
// collections/getJoinCards, gated on the cloud feature + being signed in) render in the main card
// list, and clicking one opens JoinCloudCollectionDialog for that exact card, closing again on
// onClose. Replaces the old MyCloudCollectionsSection sidebar wiring test (that component + its
// test file are deleted; see CollectionCardList.test.tsx for the append-after-slice card-list
// logic itself). Mocks every hook/endpoint (no network layer) plus JoinCloudCollectionDialog
// itself (already covered by its own JoinCloudCollectionDialog.test.tsx).

const {
    mockUseApiData,
    mockGet,
    mockUseIsCloudFeatureEnabled,
    mockUseSharingLoginState,
} = vi.hoisted(() => ({
    mockUseApiData: vi.fn(),
    mockGet: vi.fn(),
    mockUseIsCloudFeatureEnabled: vi.fn(),
    mockUseSharingLoginState: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        useApiData: mockUseApiData,
        get: mockGet,
    };
});

vi.mock("../teamCollection/sharingApi", () => ({
    useIsCloudTeamCollectionsExperimentalFeatureEnabled:
        mockUseIsCloudFeatureEnabled,
    useSharingLoginState: mockUseSharingLoginState,
}));

vi.mock("../teamCollection/JoinCloudCollectionDialog", () => ({
    JoinCloudCollectionDialog: (props: {
        collectionId: string;
        collectionName: string;
        signedIn: boolean;
        onClose?: () => void;
    }) => (
        <div
            data-testid="join-cloud-collection-dialog-stub"
            data-collection-id={props.collectionId}
            data-collection-name={props.collectionName}
            data-signed-in={String(props.signedIn)}
        >
            <button
                data-testid="join-cloud-collection-dialog-stub-close"
                onClick={() => props.onClose?.()}
            >
                close
            </button>
        </div>
    ),
}));

function render(): HTMLDivElement {
    return renderTestRoot(<CollectionChooser />);
}

const signedIn: ISharingLoginState = {
    mode: "dev",
    signedIn: true,
    email: "me@example.com",
};

const joinCardA: IJoinCollectionInfo = {
    collectionId: "aaa-111",
    title: "Team A Collection",
};
const joinCardB: IJoinCollectionInfo = {
    collectionId: "bbb-222",
    title: "Team B Collection",
};

// Makes mockGet respond to collections/getJoinCards with the given join cards; any other
// endpoint (e.g. sign-in state, if ever queried this way) gets an empty array.
function mockJoinCardsResponse(joinCards: IJoinCollectionInfo[]) {
    mockGet.mockImplementation(
        (url: string, callback: (r: unknown) => void) => {
            if (url === "collections/getJoinCards") {
                callback({ data: joinCards });
            }
        },
    );
}

afterEach(() => {
    vi.clearAllMocks();
});

describe("CollectionChooser", () => {
    it("opens JoinCloudCollectionDialog for the exact join card clicked, and closes it on onClose", () => {
        mockUseApiData.mockReturnValue([]);
        mockUseIsCloudFeatureEnabled.mockReturnValue(true);
        mockUseSharingLoginState.mockReturnValue(signedIn);
        mockJoinCardsResponse([joinCardA, joinCardB]);

        const container = render();

        expect(
            container.querySelector(
                '[data-testid="join-cloud-collection-dialog-stub"]',
            ),
        ).toBeNull();

        const joinCards = container.querySelectorAll(
            '[data-testid="join-collection-card"]',
        );
        expect(joinCards.length).toBe(2);
        const cardB = Array.from(joinCards).find((card) =>
            card.textContent?.includes(joinCardB.title),
        ) as HTMLElement;
        act(() => (cardB.querySelector("h5") as HTMLElement).click());

        const dialog = document.querySelector(
            '[data-testid="join-cloud-collection-dialog-stub"]',
        );
        expect(dialog).not.toBeNull();
        expect(dialog!.getAttribute("data-collection-id")).toBe(
            joinCardB.collectionId,
        );
        expect(dialog!.getAttribute("data-collection-name")).toBe(
            joinCardB.title,
        );
        expect(dialog!.getAttribute("data-signed-in")).toBe("true");

        const closeButton = document.querySelector(
            '[data-testid="join-cloud-collection-dialog-stub-close"]',
        ) as HTMLButtonElement;
        act(() => closeButton.click());

        expect(
            document.querySelector(
                '[data-testid="join-cloud-collection-dialog-stub"]',
            ),
        ).toBeNull();
    });

    it("does not query for join cards, render any, or show the dialog when the cloud feature is off", () => {
        mockUseApiData.mockReturnValue([]);
        mockUseIsCloudFeatureEnabled.mockReturnValue(false);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: false,
        });
        mockJoinCardsResponse([joinCardA]);

        const container = render();

        expect(mockGet).not.toHaveBeenCalledWith(
            "collections/getJoinCards",
            expect.anything(),
        );
        expect(
            container.querySelector('[data-testid="join-collection-card"]'),
        ).toBeNull();
        expect(
            container.querySelector(
                '[data-testid="join-cloud-collection-dialog-stub"]',
            ),
        ).toBeNull();
    });

    it("does not query for join cards or render any when the cloud feature is on but signed out", () => {
        mockUseApiData.mockReturnValue([]);
        mockUseIsCloudFeatureEnabled.mockReturnValue(true);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: false,
        });
        mockJoinCardsResponse([joinCardA]);

        const container = render();

        expect(mockGet).not.toHaveBeenCalledWith(
            "collections/getJoinCards",
            expect.anything(),
        );
        expect(
            container.querySelector('[data-testid="join-collection-card"]'),
        ).toBeNull();
    });
});
