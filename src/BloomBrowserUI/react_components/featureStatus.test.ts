import { beforeEach, describe, expect, it, vi } from "vitest";

const { mockPost, workspace } = vi.hoisted(() => ({
    mockPost: vi.fn(),
    // What tryGetWorkspaceBundleExports finds: nothing, or a workspace bundle whose
    // showCollectionSettingsDialog says whether a dialog received the launch.
    workspace: {
        exports: undefined as
            | { showCollectionSettingsDialog: (page: string) => boolean }
            | undefined,
    },
}));

vi.mock("../utils/bloomApi", () => ({
    post: mockPost,
    get: vi.fn(),
    useApiObject: vi.fn(),
}));

vi.mock("../bookEdit/js/workspaceFrames", () => ({
    tryGetWorkspaceBundleExports: () => workspace.exports,
}));

import { openBloomSubscriptionSettings } from "./featureStatus";

describe("openBloomSubscriptionSettings", () => {
    beforeEach(() => {
        mockPost.mockReset();
        workspace.exports = undefined;
    });

    it("opens the Collection Settings dialog on its Subscription page, without asking Bloom", () => {
        const show = vi.fn(() => true);
        workspace.exports = { showCollectionSettingsDialog: show };

        openBloomSubscriptionSettings();

        expect(show).toHaveBeenCalledWith("subscription");
        expect(mockPost).not.toHaveBeenCalled();
    });

    it("asks Bloom when the window has the workspace code but no dialog to open", () => {
        // The WinForms Settings dialog's tabs load the workspace bundle code, but no dialog.
        const show = vi.fn(() => false);
        workspace.exports = { showCollectionSettingsDialog: show };

        openBloomSubscriptionSettings();

        expect(show).toHaveBeenCalledTimes(1);
        expect(mockPost).toHaveBeenCalledWith(
            "common/showSettingsDialog?tab=subscription",
        );
    });

    it("asks Bloom when there is no workspace at all", () => {
        expect(workspace.exports).toBeUndefined();

        openBloomSubscriptionSettings();

        expect(mockPost).toHaveBeenCalledWith(
            "common/showSettingsDialog?tab=subscription",
        );
    });
});
