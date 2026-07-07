import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { TeamCollectionDialog } from "./TeamCollectionDialog";
import { ITeamCollectionCapabilities } from "./teamCollectionApi";

// Tests the Wave-2 addition to the Team Collection status dialog: for a cloud Team Collection,
// "Check In All Books" becomes "Send All" and a new "Receive Updates" button (successor of
// "Reload Collection") appears; folder Team Collections must keep the exact previous labels and
// endpoints. Per Wave-2 scope (shells against mocked endpoints), teamCollectionApi's capabilities
// hook is mocked; bloomApi's `post` is mocked to assert calls, and `getBoolean` is mocked so the
// dialog's tabs mount synchronously (see TeamCollectionDialog.tsx's defaultTabIndex dance) instead
// of waiting on a real (and here, unavailable) `teamCollection/logImportant` network call.
//
// MUI's Dialog renders via a portal to document.body, so assertions query document rather than a
// local render container (see JoinCloudCollectionDialog.test.tsx, which documents the same thing).
// Also as noted there: the test-only localizationManager mock (vitest.setup.ts) resolves every
// l10nKey to the key itself rather than the English fallback, so button lookups below use each
// button's stable `id` (present in TeamCollectionDialog.tsx) rather than its visible text.

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

const { mockPost, mockGetBoolean } = vi.hoisted(() => ({
    mockPost: vi.fn(),
    // Resolve "logImportant" as true so defaultTabIndex becomes 0 (Status tab), which is the tab
    // that hosts the buttons under test; react-tabs does not render a non-selected TabPanel's
    // children by default.
    mockGetBoolean: vi.fn((_url: string, cb: (v: boolean) => void) => cb(true)),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        post: mockPost,
        getBoolean: mockGetBoolean,
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

let renderedContainer: HTMLDivElement | undefined;

function renderDialog(showReloadButton: boolean) {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;
    act(() => {
        renderRoot(
            <TeamCollectionDialog
                showReloadButton={showReloadButton}
                closeDialog={vi.fn()}
                propsForBloomDialog={{
                    open: true,
                    onClose: vi.fn(),
                }}
            />,
            container,
        );
    });
}

function getButtonById(id: string): HTMLButtonElement | null {
    return document.getElementById(id) as HTMLButtonElement | null;
}

afterEach(() => {
    if (renderedContainer) {
        unmountRoot(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    // BloomDialog/MUI Dialog portal their content directly onto document.body, outside our
    // mounted root, so it must be cleaned up separately between tests.
    document.body.innerHTML = "";
    mockPost.mockClear();
    mockUseTeamCollectionCapabilities.mockReset();
});

describe("TeamCollectionDialog", () => {
    it("folder Team Collection: keeps 'Check In All Books' and posts the folder endpoint", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        renderDialog(false);

        const checkInAll = getButtonById("checkInAll");
        expect(checkInAll).not.toBeNull();
        expect(checkInAll!.textContent).toContain("TeamCollection.checkInAll");
        act(() => checkInAll!.click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/checkInAllBooks");
    });

    it("folder Team Collection: never shows 'Receive Updates'", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        renderDialog(false);
        expect(getButtonById("receiveUpdates")).toBeNull();
    });

    it("cloud Team Collection: renames the button to 'Send All' and posts the cloud endpoint", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        renderDialog(false);

        const sendAll = getButtonById("checkInAll");
        expect(sendAll).not.toBeNull();
        expect(sendAll!.textContent).toContain("TeamCollection.SendAll");
        act(() => sendAll!.click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/sendAllBooks");
    });

    it("cloud Team Collection without a pending reload: shows 'Receive Updates' and posts its endpoint", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        renderDialog(false);

        const receiveUpdates = getButtonById("receiveUpdates");
        expect(receiveUpdates).not.toBeNull();
        act(() => receiveUpdates!.click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/receiveUpdates");
    });

    it("cloud Team Collection with a pending settings reload: shows only 'Reload Collection', not 'Receive Updates'", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        renderDialog(true);

        expect(getButtonById("reload")).not.toBeNull();
        expect(getButtonById("receiveUpdates")).toBeNull();
    });
});
