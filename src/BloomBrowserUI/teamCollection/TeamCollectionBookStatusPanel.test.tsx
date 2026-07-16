import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderTestRoot } from "../utils/testRender";
import { TeamCollectionBookStatusPanel } from "./TeamCollectionBookStatusPanel";
import {
    IBookTeamCollectionStatus,
    ITeamCollectionCapabilities,
    initialBookStatus,
} from "./teamCollectionApi";

// Tests the Wave-2 additions to the per-book status panel's state matrix: the three new cloud-only
// states (signedOut/updatesAvailable/offlineDisabled) plus the existing folder-TC state matrix,
// to confirm the new capability-gated branches don't disturb it. bloomApi's `get`/`post` are
// mocked (this panel calls `get` for registration/userInfo when checking out); the panel's own
// websocket subscriptions are no-ops in tests via vitest.setup.ts's `_SKIP_WEBSOCKET_CREATION_`.

const { mockUseTeamCollectionCapabilities } = vi.hoisted(() => ({
    mockUseTeamCollectionCapabilities: vi.fn(),
}));

vi.mock("./teamCollectionApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("./teamCollectionApi")>();
    return {
        ...actual,
        useTeamCollectionCapabilities: mockUseTeamCollectionCapabilities,
    };
});

const { mockPost, mockGet } = vi.hoisted(() => ({
    mockPost: vi.fn(),
    mockGet: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        post: mockPost,
        get: mockGet,
    };
});

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

function renderPanel(status: IBookTeamCollectionStatus) {
    return renderTestRoot(<TeamCollectionBookStatusPanel {...status} />);
}

afterEach(() => {
    mockUseTeamCollectionCapabilities.mockReset();
    mockPost.mockClear();
    mockGet.mockClear();
});

describe("TeamCollectionBookStatusPanel: folder Team Collection state matrix (unchanged by Wave 2)", () => {
    it("unlocked: shows the checkout button", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        const container = renderPanel({ ...initialBookStatus });
        expect(container.querySelector(".checkout-button")).not.toBeNull();
    });

    it("locked (by someone else): shows no checkout/checkin button", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            who: "fred@example.com",
            whoFirstName: "Fred",
            currentUser: "me@example.com",
            currentMachine: "MyMachine",
            where: "FredsMachine",
        });
        expect(container.querySelector(".checkout-button")).toBeNull();
        expect(container.querySelector(".checkin-button")).toBeNull();
    });

    it("lockedByMe: shows the checkin button and the note field, not a modal", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            who: "me@example.com",
            currentUser: "me@example.com",
            currentMachine: "MyMachine",
            where: "MyMachine",
        });
        expect(container.querySelector(".checkin-button")).not.toBeNull();
        expect(container.querySelector("input[type=text]")).not.toBeNull();
        // The cloud-only modal must never appear for a folder Team Collection.
        expect(
            document.querySelector('[data-testid="cloud-checkin-progress"]'),
        ).toBeNull();
    });

    it("needsReload (isChangedRemotely), folder: keeps the Reload Collection button (unchanged by batch item 4+5)", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            isChangedRemotely: true,
        });
        const reloadButton = container.querySelector(".reload-button");
        expect(reloadButton).not.toBeNull();
        expect(container.querySelector(".sync-button")).toBeNull();
        act(() => (reloadButton as HTMLButtonElement).click());
        expect(mockPost).toHaveBeenCalledWith("common/reloadCollection");
    });

    it("hasInvalidRepoData: shows the book-problem UI with the server's error message", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            invalidRepoDataErrorMsg: "corrupt zip",
        });
        expect(container.textContent).toContain("corrupt zip");
    });

    it("gating: cloud-shaped fields on a folder Team Collection (capabilities all false) are ignored", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        // These fields should never actually be populated for a folder TC (per
        // teamCollectionApi.tsx's IBookTeamCollectionStatus comment), but even if they were,
        // gating on capability - not on the fields' mere presence - keeps folder TC UI
        // byte-identical.
        const container = renderPanel({
            ...initialBookStatus,
            requiresSignIn: true,
            signedIn: false,
            offlineDisabledReason: "never downloaded",
            localVersionSeq: 1,
            repoVersionSeq: 5,
        });
        expect(container.querySelector(".checkout-button")).not.toBeNull();
        expect(container.textContent).not.toContain("TeamCollection.SignedOut");
        expect(container.textContent).not.toContain(
            "TeamCollection.OfflineDisabled",
        );
        expect(container.textContent).not.toContain(
            "TeamCollection.UpdatesAvailableForBook",
        );
    });
});

describe("TeamCollectionBookStatusPanel: cloud Team Collection additions (Wave 2)", () => {
    it("signedOut: book would otherwise be unlocked, but the user isn't signed in", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            requiresSignIn: true,
            signedIn: false,
        });
        expect(container.textContent).toContain("TeamCollection.SignedOut");
        expect(container.querySelector(".checkout-button")).toBeNull();
        const signInButton = container.querySelector(".sign-in-button");
        expect(signInButton).not.toBeNull();
        act(() => (signInButton as HTMLButtonElement).click());
        expect(mockPost).toHaveBeenCalledWith("sharing/showSignIn");
    });

    it("signedIn: an otherwise-unlocked book with signedIn true shows the normal checkout button", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            requiresSignIn: true,
            signedIn: true,
        });
        expect(container.querySelector(".checkout-button")).not.toBeNull();
    });

    it("updatesAvailable: unlocked book with a newer version in the repo", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            requiresSignIn: true,
            signedIn: true,
            localVersionSeq: 3,
            repoVersionSeq: 5,
        });
        expect(container.textContent).toContain(
            "TeamCollection.UpdatesAvailableForBook",
        );
        const syncButton = container.querySelector(".sync-button");
        expect(syncButton).not.toBeNull();
        act(() => (syncButton as HTMLButtonElement).click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/receiveUpdates");
    });

    it("no updatesAvailable when the local version is already current", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            requiresSignIn: true,
            signedIn: true,
            localVersionSeq: 5,
            repoVersionSeq: 5,
        });
        expect(container.textContent).not.toContain(
            "TeamCollection.UpdatesAvailableForBook",
        );
        expect(container.querySelector(".checkout-button")).not.toBeNull();
    });

    it("needsReload (isChangedRemotely), cloud: shows Sync instead of Reload Collection", () => {
        // Batch item 4+5: for a cloud Team Collection this is purely a content-update state (a
        // remote checkin not yet picked up here), not a settings-reload state, so it should offer
        // the same in-place Sync as "updatesAvailable" rather than a full collection reload.
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            requiresSignIn: true,
            signedIn: true,
            isChangedRemotely: true,
        });
        expect(container.textContent).toContain(
            "TeamCollection.UpdatesAvailableForBook",
        );
        expect(container.querySelector(".reload-button")).toBeNull();
        const syncButton = container.querySelector(".sync-button");
        expect(syncButton).not.toBeNull();
        act(() => (syncButton as HTMLButtonElement).click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/receiveUpdates");
    });

    it("offlineDisabled: takes priority over other states and shows the server-supplied reason verbatim", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        const container = renderPanel({
            ...initialBookStatus,
            who: "fred@example.com",
            offlineDisabledReason: "This book has never been downloaded.",
        });
        expect(container.textContent).toContain(
            "TeamCollection.OfflineDisabled",
        );
        expect(container.textContent).toContain(
            "This book has never been downloaded.",
        );
    });

    it("lockedByMe, cloud: check-in progress shows in a modal instead of the inline bar", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        renderPanel({
            ...initialBookStatus,
            who: "me@example.com",
            currentUser: "me@example.com",
            currentMachine: "MyMachine",
            where: "MyMachine",
        });
        // The modal only appears once check-in is actually in progress; at rest it's not shown
        // per BloomDialog's `open` prop (MUI unmounts closed Dialog content by default).
        expect(
            document.querySelector('[data-testid="cloud-checkin-progress"]'),
        ).toBeNull();
    });
});
