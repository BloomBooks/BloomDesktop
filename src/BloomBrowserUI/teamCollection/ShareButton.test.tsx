import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderTestRoot } from "../utils/testRender";
import { ShareButton } from "./ShareButton";
import { ITeamCollectionCapabilities } from "./teamCollectionApi";

// Tests the Wave-2 Share button shown beside the Team Collection status button: it must be
// invisible for folder Team Collections (no sharing concept there) and, for cloud Team
// Collections, open SharingPanel with the right collectionId/currentUserEmail/isAdmin props.
// SharingPanel itself is unit-tested separately (SharingPanel.test.tsx), so it's mocked here to
// a prop-recording stub; MUI's Popover renders via a portal to document.body, so assertions
// query document rather than the local render container (see JoinCloudCollectionDialog.test.tsx
// for the same pattern/rationale).

const { mockUseTeamCollectionCapabilities } = vi.hoisted(() => ({
    mockUseTeamCollectionCapabilities: vi.fn(),
}));
const { mockUseCloudCollectionId, mockUseIsTeamCollectionAdmin } = vi.hoisted(
    () => ({
        mockUseCloudCollectionId: vi.fn(),
        mockUseIsTeamCollectionAdmin: vi.fn(),
    }),
);
const { mockUseSharingLoginState } = vi.hoisted(() => ({
    mockUseSharingLoginState: vi.fn(),
}));
const { mockSharingPanel } = vi.hoisted(() => ({
    mockSharingPanel: vi.fn(() => (
        <div data-testid="mock-sharing-panel">mock sharing panel</div>
    )),
}));

vi.mock("./teamCollectionApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("./teamCollectionApi")>();
    return {
        ...actual,
        useTeamCollectionCapabilities: mockUseTeamCollectionCapabilities,
        useCloudCollectionId: mockUseCloudCollectionId,
        useIsTeamCollectionAdmin: mockUseIsTeamCollectionAdmin,
    };
});

vi.mock("./sharingApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("./sharingApi")>();
    return {
        ...actual,
        useSharingLoginState: mockUseSharingLoginState,
    };
});

vi.mock("./SharingPanel", () => ({
    SharingPanel: mockSharingPanel,
}));

const folderCapabilities: ITeamCollectionCapabilities = {
    supportsVersionHistory: false,
    supportsSharingUi: false,
    requiresSignIn: false,
};

const cloudCapabilities: ITeamCollectionCapabilities = {
    supportsVersionHistory: true,
    supportsSharingUi: true,
    requiresSignIn: true,
};

function renderShareButton() {
    renderTestRoot(<ShareButton />);
}

afterEach(() => {
    mockUseTeamCollectionCapabilities.mockReset();
    mockUseCloudCollectionId.mockReset();
    mockUseIsTeamCollectionAdmin.mockReset();
    mockUseSharingLoginState.mockReset();
    mockSharingPanel.mockClear();
});

describe("ShareButton", () => {
    it("folder Team Collection: renders nothing", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        mockUseCloudCollectionId.mockReturnValue("");
        mockUseIsTeamCollectionAdmin.mockReturnValue(false);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: false,
        });

        renderShareButton();

        expect(document.getElementById("teamCollectionShareButton")).toBeNull();
        expect(mockSharingPanel).not.toHaveBeenCalled();
    });

    it("cloud Team Collection: shows the Share button, initially without opening the panel", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseCloudCollectionId.mockReturnValue("collection-123");
        mockUseIsTeamCollectionAdmin.mockReturnValue(true);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: true,
            email: "admin@example.com",
        });

        renderShareButton();

        expect(
            document.getElementById("teamCollectionShareButton"),
        ).not.toBeNull();
        expect(mockSharingPanel).not.toHaveBeenCalled();
    });

    it("cloud Team Collection, admin: clicking Share opens SharingPanel with isAdmin true and the current user's email", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseCloudCollectionId.mockReturnValue("collection-123");
        mockUseIsTeamCollectionAdmin.mockReturnValue(true);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: true,
            email: "admin@example.com",
        });

        renderShareButton();
        act(() =>
            document.getElementById("teamCollectionShareButton")!.click(),
        );

        expect(mockSharingPanel).toHaveBeenCalledWith(
            {
                collectionId: "collection-123",
                currentUserEmail: "admin@example.com",
                isAdmin: true,
            },
            {},
        );
    });

    it("cloud Team Collection, non-admin member: clicking Share opens SharingPanel with isAdmin false", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseCloudCollectionId.mockReturnValue("collection-123");
        mockUseIsTeamCollectionAdmin.mockReturnValue(false);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: true,
            email: "member@example.com",
        });

        renderShareButton();
        act(() =>
            document.getElementById("teamCollectionShareButton")!.click(),
        );

        expect(mockSharingPanel).toHaveBeenCalledWith(
            {
                collectionId: "collection-123",
                currentUserEmail: "member@example.com",
                isAdmin: false,
            },
            {},
        );
    });
});
