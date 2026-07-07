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

const { mockUseTeamCollectionCapabilities } = vi.hoisted(() => ({
    mockUseTeamCollectionCapabilities: vi.fn(),
}));

vi.mock("./teamCollectionApi", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("./teamCollectionApi")>();
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

function renderDialog(showReloadButton: boolean) {
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
            document.body.appendChild(document.createElement("div")),
        );
    });
}

function getButtonByText(text: string): HTMLButtonElement | null {
    const found = Array.from(document.querySelectorAll("button")).find((b) =>
        b.textContent?.includes(text),
    );
    return (found as HTMLButtonElement) ?? null;
}

afterEach(() => {
    document.body.innerHTML = "";
    mockPost.mockClear();
    mockUseTeamCollectionCapabilities.mockReset();
});

describe("TeamCollectionDialog", () => {
    it("folder Team Collection: keeps 'Check In All Books' and posts the folder endpoint", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        renderDialog(false);
        // eslint-disable-next-line no-console
        console.log(
            "DEBUG checkInAll el:",
            document.getElementById("checkInAll")?.outerHTML ?? "NOT FOUND",
        );
        // eslint-disable-next-line no-console
        console.log(
            "DEBUG all button texts:",
            JSON.stringify(
                Array.from(document.querySelectorAll("button")).map(
                    (b) => b.textContent,
                ),
            ),
        );

        expect(getButtonByText("Send All")).toBeNull();
        const checkInAll = getButtonByText("Check In All Books");
        expect(checkInAll).not.toBeNull();
        act(() => checkInAll!.click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/checkInAllBooks");
    });

    it("folder Team Collection: never shows 'Receive Updates'", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        renderDialog(false);
        expect(getButtonByText("Receive Updates")).toBeNull();
    });

    it("cloud Team Collection: renames the button to 'Send All' and posts the cloud endpoint", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        renderDialog(false);

        expect(getButtonByText("Check In All Books")).toBeNull();
        const sendAll = getButtonByText("Send All");
        expect(sendAll).not.toBeNull();
        act(() => sendAll!.click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/sendAllBooks");
    });

    it("cloud Team Collection without a pending reload: shows 'Receive Updates' and posts its endpoint", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        renderDialog(false);

        const receiveUpdates = getButtonByText("Receive Updates");
        expect(receiveUpdates).not.toBeNull();
        act(() => receiveUpdates!.click());
        expect(mockPost).toHaveBeenCalledWith("teamCollection/receiveUpdates");
    });

    it("cloud Team Collection with a pending settings reload: shows only 'Reload Collection', not 'Receive Updates'", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        renderDialog(true);

        expect(getButtonByText("Reload Collection")).not.toBeNull();
        expect(getButtonByText("Receive Updates")).toBeNull();
    });
});
