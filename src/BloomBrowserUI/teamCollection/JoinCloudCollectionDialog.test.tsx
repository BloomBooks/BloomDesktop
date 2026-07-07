import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { normalDialogEnvironmentForStorybook } from "../react_components/BloomDialog/BloomDialogPlumbing";
import { JoinCloudCollectionDialog } from "./JoinCloudCollectionDialog";

// Tests the state-derivation logic (NotSignedIn / ApprovalRemoved / the six folder-TC-style
// scenarios) of JoinCloudCollectionDialog, the pull-down-join dialog opened from "Get my Team
// Collections" in the collection chooser. Per Wave-1 scope (shells against mocked endpoints),
// only sharingApi's pullDownCollection and bloomApi's post are mocked; everything else renders
// for real. MUI's Dialog renders via a portal to document.body, so assertions query
// document.body rather than a local render container (unlike the other tests in this task,
// which test presentational sub-components directly to avoid the portal).
//
// Note: the test-only localizationManager mock (vitest.setup.ts) resolves every l10nKey to the
// key itself rather than the English fallback (see the comment about this in
// SharingPanel.test.tsx), so text assertions here check for the l10nKey rather than the English
// string the component declares as a child.

const { mockPullDownCollection, mockPost } = vi.hoisted(() => ({
    mockPullDownCollection: vi.fn(),
    mockPost: vi.fn(),
}));

vi.mock("./sharingApi", () => ({
    pullDownCollection: mockPullDownCollection,
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        post: mockPost,
    };
});

let mountedRoot: HTMLDivElement | undefined;

function renderDialog(
    overrides: Partial<React.ComponentProps<typeof JoinCloudCollectionDialog>>,
) {
    const root = document.createElement("div");
    document.body.appendChild(root);
    mountedRoot = root;
    act(() => {
        renderRoot(
            <JoinCloudCollectionDialog
                collectionId="collection-123"
                collectionName="My Team's Collection"
                signedIn={true}
                isApproved={true}
                existingCollection={false}
                isAlreadyTcCollection={false}
                isSameCollection={false}
                isCurrentCollection={false}
                existingCollectionFolder=""
                conflictingCollection=""
                dialogEnvironment={normalDialogEnvironmentForStorybook}
                {...overrides}
            />,
            root,
        );
    });
}

// Flushes the microtask queue so a clicked button's pullDownCollection().then(...) chain has
// run before the next assertion.
async function flushPromises() {
    await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
    });
}

function getActionButton(): HTMLButtonElement {
    const button = document.querySelector(
        '[data-testid="join-cloud-collection-action-button"]',
    ) as HTMLButtonElement;
    expect(button).not.toBeNull();
    return button;
}

function getBodyText(): string {
    const body = document.querySelector(
        '[data-testid="join-cloud-collection-body"]',
    );
    expect(body).not.toBeNull();
    return body!.textContent ?? "";
}

beforeEach(() => {
    // Default: pullDownCollection succeeds. Individual tests override with mockRejectedValue
    // to exercise the failure path.
    mockPullDownCollection.mockResolvedValue(undefined);
});

afterEach(() => {
    if (mountedRoot) {
        unmountRoot(mountedRoot);
        mountedRoot.remove();
        mountedRoot = undefined;
    }
    // BloomDialog/MUI Dialog portal their content directly onto document.body, outside our
    // mounted root, so it must be cleaned up separately between tests.
    document.body.innerHTML = "";
    mockPullDownCollection.mockReset();
    mockPost.mockClear();
});

describe("JoinCloudCollectionDialog", () => {
    it("NotSignedIn: prompts to sign in and the action button posts sharing/showSignIn (not pullDownCollection)", () => {
        renderDialog({ signedIn: false });

        expect(getBodyText()).toContain(
            "TeamCollection.Sharing.MustSignInToJoin",
        );

        act(() => getActionButton().click());
        expect(mockPost).toHaveBeenCalledWith("sharing/showSignIn");
        expect(mockPullDownCollection).not.toHaveBeenCalled();
    });

    it("ApprovalRemoved: disables the action button and explains why", () => {
        renderDialog({ signedIn: true, isApproved: false });

        expect(getBodyText()).toContain(
            "TeamCollection.Sharing.ApprovalRemoved",
        );
        expect(getActionButton().disabled).toBe(true);

        act(() => getActionButton().click());
        expect(mockPost).not.toHaveBeenCalled();
        expect(mockPullDownCollection).not.toHaveBeenCalled();
    });

    it("CreateNewCollection: enabled action button calls pullDownCollection with the collectionId", () => {
        renderDialog({
            signedIn: true,
            isApproved: true,
            existingCollection: false,
        });

        expect(getBodyText()).toContain(
            "TeamCollection.Sharing.BloomWillPullDown",
        );
        const button = getActionButton();
        expect(button.disabled).toBe(false);
        act(() => button.click());
        expect(mockPullDownCollection).toHaveBeenCalledWith("collection-123");
    });

    it("MatchesExistingTeamCollection: already-linked case offers to open, not pull down again", () => {
        renderDialog({
            signedIn: true,
            isApproved: true,
            existingCollection: true,
            isAlreadyTcCollection: true,
            isSameCollection: true,
            isCurrentCollection: true,
            existingCollectionFolder: "C:\\Users\\me\\Bloom Collections\\Foo",
        });

        expect(getBodyText()).toContain("TeamCollection.AlreadyJoined");
        expect(getActionButton().disabled).toBe(false);

        act(() => getActionButton().click());
        expect(mockPullDownCollection).toHaveBeenCalledWith("collection-123");
    });

    it("MatchesExistingTeamCollectionElsewhere: moved-local-copy case offers to fix up and open", () => {
        renderDialog({
            signedIn: true,
            isApproved: true,
            existingCollection: true,
            isAlreadyTcCollection: true,
            isSameCollection: true,
            isCurrentCollection: false,
            existingCollectionFolder: "C:\\Users\\me\\Bloom Collections\\Foo",
        });

        expect(getBodyText()).toContain(
            "TeamCollection.AlreadyJoinedElsewhere",
        );
        expect(getActionButton().disabled).toBe(false);
    });

    it("MatchesExistingNonTeamCollection: offers to merge the existing local collection", () => {
        renderDialog({
            signedIn: true,
            isApproved: true,
            existingCollection: true,
            isAlreadyTcCollection: false,
            existingCollectionFolder: "C:\\Users\\me\\Bloom Collections\\Foo",
        });

        expect(getBodyText()).toContain("TeamCollection.Merging");
        expect(getActionButton().disabled).toBe(false);
    });

    it("IncompleteLocalCopy: reports the problem and still allows retrying the pull-down", () => {
        renderDialog({
            signedIn: true,
            isApproved: true,
            incompleteLocalCopy: true,
        });

        expect(getBodyText()).toContain(
            "TeamCollection.Sharing.IncompleteLocalCopy",
        );
        expect(getActionButton().disabled).toBe(false);
        act(() => getActionButton().click());
        expect(mockPullDownCollection).toHaveBeenCalledWith("collection-123");
    });

    it("MatchesDifferentTeamCollection: conflict disables the action button and offers Report", () => {
        renderDialog({
            signedIn: true,
            isApproved: true,
            existingCollection: true,
            isAlreadyTcCollection: true,
            isSameCollection: false,
            existingCollectionFolder: "C:\\Users\\me\\Bloom Collections\\Foo",
            conflictingCollection: "cloud://sil.bloom/collection/other-id",
        });

        expect(getActionButton().disabled).toBe(true);
        expect(getBodyText()).toContain("TeamCollection.ConflictingCollection");
        // The Report button (DialogBottomLeftButtons) is a sibling of the body, not inside it.
        expect(document.body.textContent).toContain("ErrorReport.Report");
    });

    it("calls onClose (so an embedding parent can unmount it) once pullDownCollection succeeds", async () => {
        const onClose = vi.fn();
        renderDialog({ onClose });

        act(() => getActionButton().click());
        await flushPromises();

        expect(mockPullDownCollection).toHaveBeenCalledWith("collection-123");
        expect(onClose).toHaveBeenCalled();
        expect(
            document.querySelector(
                '[data-testid="join-cloud-collection-error"]',
            ),
        ).toBeNull();
    });

    it("shows the server's real error message and stays open when pullDownCollection fails", async () => {
        mockPullDownCollection.mockRejectedValue(
            new Error(
                'There is already a different Team Collection called "My Team\'s Collection" on this computer.',
            ),
        );
        const onClose = vi.fn();
        renderDialog({ onClose });

        act(() => getActionButton().click());
        await flushPromises();

        expect(onClose).not.toHaveBeenCalled();
        const error = document.querySelector(
            '[data-testid="join-cloud-collection-error"]',
        );
        expect(error).not.toBeNull();
        expect(error!.textContent).toContain(
            "There is already a different Team Collection",
        );
        // The action button is re-enabled so the user can retry (e.g. after picking a
        // different name/removing the conflicting collection).
        expect(getActionButton().disabled).toBe(false);
    });
});
