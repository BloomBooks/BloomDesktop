import { afterEach, describe, expect, it, vi } from "vitest";
import { renderTestRoot } from "../utils/testRender";
import { TeamCollectionSettingsPanel } from "./TeamCollectionSettingsPanel";

// Tests the cloud-vs-folder branch this task wires up in the isTeamCollection half of the
// panel: cloud Team Collections get SharingPanel (task 07's Wave-1 shell) in place of the old
// free-text administrator-emails field; folder Team Collections keep that old field completely
// unchanged. Mocks every hook the panel calls (no network layer), plus SharingPanel itself
// (already covered by its own SharingPanel.test.tsx) so this file only tests the branching.

const {
    mockUseApiStringState,
    mockGet,
    mockUseIsCloudFeatureEnabled,
    mockUseTeamCollectionCapabilities,
    mockUseCloudCollectionId,
    mockUseIsTeamCollectionAdmin,
    mockUseSharingLoginState,
} = vi.hoisted(() => ({
    mockUseApiStringState: vi.fn(),
    mockGet: vi.fn(),
    mockUseIsCloudFeatureEnabled: vi.fn(),
    mockUseTeamCollectionCapabilities: vi.fn(),
    mockUseCloudCollectionId: vi.fn(),
    mockUseIsTeamCollectionAdmin: vi.fn(),
    mockUseSharingLoginState: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        useApiStringState: mockUseApiStringState,
        get: mockGet,
    };
});

vi.mock("./sharingApi", () => ({
    useIsCloudTeamCollectionsExperimentalFeatureEnabled:
        mockUseIsCloudFeatureEnabled,
    useSharingLoginState: mockUseSharingLoginState,
}));

vi.mock("./teamCollectionApi", () => ({
    isCloudTeamCollection: (capabilities: {
        supportsVersionHistory: boolean;
        supportsSharingUi: boolean;
        requiresSignIn: boolean;
    }) =>
        capabilities.supportsVersionHistory ||
        capabilities.supportsSharingUi ||
        capabilities.requiresSignIn,
    useTeamCollectionCapabilities: mockUseTeamCollectionCapabilities,
    useCloudCollectionId: mockUseCloudCollectionId,
    useIsTeamCollectionAdmin: mockUseIsTeamCollectionAdmin,
}));

vi.mock("./SharingPanel", () => ({
    SharingPanel: (props: {
        collectionId: string;
        currentUserEmail: string;
        isAdmin: boolean;
    }) => (
        <div
            data-testid="sharing-panel-stub"
            data-collection-id={props.collectionId}
            data-current-user-email={props.currentUserEmail}
            data-is-admin={String(props.isAdmin)}
        />
    ),
}));

function render(): HTMLDivElement {
    return renderTestRoot(<TeamCollectionSettingsPanel />);
}

const folderCapabilities = {
    supportsVersionHistory: false,
    supportsSharingUi: false,
    requiresSignIn: false,
};
const cloudCapabilities = {
    supportsVersionHistory: true,
    supportsSharingUi: true,
    requiresSignIn: true,
};

afterEach(() => {
    vi.clearAllMocks();
    mockUseIsCloudFeatureEnabled.mockReturnValue(false);
    mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
    mockUseCloudCollectionId.mockReturnValue("");
    mockUseIsTeamCollectionAdmin.mockReturnValue(false);
    mockUseSharingLoginState.mockReturnValue({
        mode: "dev",
        signedIn: false,
    });
    mockUseApiStringState.mockReturnValue([""]);
    mockGet.mockImplementation(() => undefined);
});

// afterEach above doesn't run before the first test, so set the same defaults up front too.
mockUseIsCloudFeatureEnabled.mockReturnValue(false);
mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
mockUseCloudCollectionId.mockReturnValue("");
mockUseIsTeamCollectionAdmin.mockReturnValue(false);
mockUseSharingLoginState.mockReturnValue({ mode: "dev", signedIn: false });
mockUseApiStringState.mockReturnValue([""]);
mockGet.mockImplementation(() => undefined);

describe("TeamCollectionSettingsPanel", () => {
    it("folder Team Collection: shows the old administrator-emails field, not SharingPanel", () => {
        mockUseApiStringState.mockReturnValue([
            "\\\\server\\share\\MyCollection",
        ]);

        const container = render();

        expect(
            container.querySelector('[id="adminstratorEmails"]'),
        ).not.toBeNull();
        expect(
            container.querySelector('[data-testid="sharing-panel-stub"]'),
        ).toBeNull();
    });

    it("cloud Team Collection: shows SharingPanel wired to the real capability/login hooks, not the old field", () => {
        mockUseApiStringState.mockReturnValue([
            "cloud://sil.bloom/collection/abc-123",
        ]);
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseCloudCollectionId.mockReturnValue("abc-123");
        mockUseIsTeamCollectionAdmin.mockReturnValue(true);
        mockUseSharingLoginState.mockReturnValue({
            mode: "dev",
            signedIn: true,
            email: "me@example.com",
        });

        const container = render();

        const stub = container.querySelector(
            '[data-testid="sharing-panel-stub"]',
        );
        expect(stub).not.toBeNull();
        expect(stub!.getAttribute("data-collection-id")).toBe("abc-123");
        expect(stub!.getAttribute("data-current-user-email")).toBe(
            "me@example.com",
        );
        expect(stub!.getAttribute("data-is-admin")).toBe("true");
        expect(container.querySelector('[id="adminstratorEmails"]')).toBeNull();
    });

    it("not yet a Team Collection: shows neither the old field nor SharingPanel", () => {
        const container = render();

        expect(
            container.querySelector('[data-testid="sharing-panel-stub"]'),
        ).toBeNull();
        expect(container.querySelector('[id="adminstratorEmails"]')).toBeNull();
        expect(
            container.querySelector('[data-testid="share-on-cloud-button"]'),
        ).not.toBeNull();
    });
});
