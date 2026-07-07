import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { CollectionChooser } from "./CollectionChooser";
import {
    ICloudCollectionSummary,
    ISharingLoginState,
} from "../teamCollection/sharingApi";

// Tests the wiring this task adds: clicking "Get" on a row in "Get my Team Collections" opens
// JoinCloudCollectionDialog for that exact collection (matched by id out of the already-fetched
// cloudCollections list), and the dialog disappears again once it reports onClose. Mocks every
// hook (no network layer) plus JoinCloudCollectionDialog itself (already covered by its own
// JoinCloudCollectionDialog.test.tsx) so this file only tests the chooser's own glue.

const {
    mockUseApiData,
    mockUseIsCloudFeatureEnabled,
    mockUseMyCloudCollections,
    mockUseSharingLoginState,
    mockPost,
} = vi.hoisted(() => ({
    mockUseApiData: vi.fn(),
    mockUseIsCloudFeatureEnabled: vi.fn(),
    mockUseMyCloudCollections: vi.fn(),
    mockUseSharingLoginState: vi.fn(),
    mockPost: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        useApiData: mockUseApiData,
        post: mockPost,
    };
});

vi.mock("../teamCollection/sharingApi", () => ({
    useIsCloudTeamCollectionsExperimentalFeatureEnabled:
        mockUseIsCloudFeatureEnabled,
    useMyCloudCollections: mockUseMyCloudCollections,
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

let renderedContainer: HTMLDivElement | undefined;

function render(): HTMLDivElement {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;
    act(() => {
        renderRoot(<CollectionChooser />, container);
    });
    return container;
}

const signedIn: ISharingLoginState = {
    mode: "dev",
    signedIn: true,
    email: "me@example.com",
};

const collectionA: ICloudCollectionSummary = {
    collectionId: "aaa-111",
    name: "Team A Collection",
    role: "admin",
};
const collectionB: ICloudCollectionSummary = {
    collectionId: "bbb-222",
    name: "Team B Collection",
    role: "member",
};

afterEach(() => {
    if (renderedContainer) {
        unmountRoot(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    document.body.innerHTML = "";
    vi.clearAllMocks();
});

describe("CollectionChooser", () => {
    it("opens JoinCloudCollectionDialog for the exact row clicked, and closes it on onClose", () => {
        mockUseApiData.mockReturnValue([]);
        mockUseIsCloudFeatureEnabled.mockReturnValue(true);
        mockUseSharingLoginState.mockReturnValue(signedIn);
        mockUseMyCloudCollections.mockReturnValue({
            collections: [collectionA, collectionB],
            loading: false,
        });

        const container = render();

        expect(
            container.querySelector(
                '[data-testid="join-cloud-collection-dialog-stub"]',
            ),
        ).toBeNull();

        const rows = container.querySelectorAll(
            '[data-testid="my-cloud-collection-row"]',
        );
        const rowB = Array.from(rows).find(
            (row) =>
                row.getAttribute("data-collection-id") ===
                collectionB.collectionId,
        ) as HTMLElement;
        const pullDownButton = rowB.querySelector(
            '[data-testid="my-cloud-collection-pulldown-button"]',
        ) as HTMLButtonElement;
        act(() => pullDownButton.click());

        const dialog = document.querySelector(
            '[data-testid="join-cloud-collection-dialog-stub"]',
        );
        expect(dialog).not.toBeNull();
        expect(dialog!.getAttribute("data-collection-id")).toBe(
            collectionB.collectionId,
        );
        expect(dialog!.getAttribute("data-collection-name")).toBe(
            collectionB.name,
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

    it("does not render the dialog when the cloud feature is off", () => {
        mockUseApiData.mockReturnValue([]);
        mockUseIsCloudFeatureEnabled.mockReturnValue(false);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: false,
        });
        mockUseMyCloudCollections.mockReturnValue({
            collections: [],
            loading: false,
        });

        const container = render();

        expect(
            container.querySelector(
                '[data-testid="my-cloud-collections-section"]',
            ),
        ).toBeNull();
        expect(
            container.querySelector(
                '[data-testid="join-cloud-collection-dialog-stub"]',
            ),
        ).toBeNull();
    });
});
