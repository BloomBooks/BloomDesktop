import { afterEach, describe, expect, it, vi } from "vitest";
import { renderTestRoot } from "../utils/testRender";
import { CollectionHistoryTable } from "./CollectionHistoryTable";
import { ITeamCollectionCapabilities } from "./teamCollectionApi";

// Tests the Wave-2 addition to the Team Collection history tab: for a cloud Team Collection,
// history comes from the "sharing/history" server-events-feed endpoint while connected, and
// "sharing/historyCache" while disconnected, instead of the folder-TC "teamCollection/getHistory"
// endpoint (fetched via the shared useApiData hook, unchanged); incident event types
// (ForcedUnlock/SyncProblem) get a warning marker, but only for cloud Team Collections.
//
// bloomApi's `useApiData` and `getBoolean` are mocked directly (not just `get`, which they call
// internally within bloomApi.tsx itself — an intra-module call vi.mock cannot intercept; see
// TeamCollectionDialog.test.tsx's file comment for the same lesson re: getBoolean). `get` is
// mocked separately to capture the direct cross-module calls this component makes itself for the
// cloud events fetch.

const { mockUseTeamCollectionCapabilities, mockUseCloudCollectionId } =
    vi.hoisted(() => ({
        mockUseTeamCollectionCapabilities: vi.fn(),
        mockUseCloudCollectionId: vi.fn(() => "collection-123"),
    }));

vi.mock("./teamCollectionApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("./teamCollectionApi")>();
    return {
        ...actual,
        useTeamCollectionCapabilities: mockUseTeamCollectionCapabilities,
        useCloudCollectionId: mockUseCloudCollectionId,
    };
});

const { mockGet, mockGetBoolean, mockUseApiData } = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockGetBoolean: vi.fn(),
    mockUseApiData: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        get: mockGet,
        getBoolean: mockGetBoolean,
        useApiData: mockUseApiData,
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

const oneEvent = (type: number) => [
    {
        Title: "My Book",
        ThumbnailPath: "thumb.png",
        When: "2026-07-07T00:00:00Z",
        Message: "a comment",
        Type: type,
        UserId: "fred@example.com",
        UserName: "Fred",
    },
];

function renderTable() {
    return renderTestRoot(<CollectionHistoryTable />);
}

afterEach(() => {
    mockUseTeamCollectionCapabilities.mockReset();
    mockGet.mockReset();
    mockGetBoolean.mockReset();
    mockUseApiData.mockReset();
});

describe("CollectionHistoryTable: folder Team Collection (unchanged by Wave 2)", () => {
    it("uses the folder-TC useApiData result, makes no cloud-only requests, and shows no incident marker", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(folderCapabilities);
        mockUseApiData.mockReturnValue(oneEvent(5) /* Forced Unlock */);

        const container = renderTable();

        expect(mockUseApiData).toHaveBeenCalledWith(
            "teamCollection/getHistory",
            [],
        );
        // Folder Team Collections must make zero extra requests: neither the
        // isDisconnected check nor a cloud history fetch should ever fire.
        expect(mockGetBoolean).not.toHaveBeenCalled();
        expect(mockGet).not.toHaveBeenCalled();
        expect(
            container.querySelector('[data-testid="history-incident-icon"]'),
        ).toBeNull();
        expect(container.textContent).toContain("Forced Unlock");
    });
});

describe("CollectionHistoryTable: cloud Team Collection additions (Wave 2)", () => {
    it("connected: asks the sharing/history server-events-feed endpoint, scoped to the collection", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseApiData.mockReturnValue([]);
        mockGetBoolean.mockImplementation(
            (_url: string, cb: (v: boolean) => void) => cb(false),
        );
        mockGet.mockImplementation((_url: string, cb: (r: unknown) => void) =>
            cb({ data: oneEvent(0) }),
        );

        renderTable();

        expect(mockGet).toHaveBeenCalledTimes(1);
        const url = mockGet.mock.calls[0][0] as string;
        expect(url).toContain("sharing/history?");
        expect(url).toContain("collectionId=collection-123");
    });

    it("disconnected: falls back to the sharing/historyCache endpoint instead of the live feed", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseApiData.mockReturnValue([]);
        mockGetBoolean.mockImplementation(
            (_url: string, cb: (v: boolean) => void) => cb(true),
        );
        mockGet.mockImplementation((_url: string, cb: (r: unknown) => void) =>
            cb({ data: oneEvent(0) }),
        );

        renderTable();

        expect(mockGet).toHaveBeenCalledTimes(1);
        const url = mockGet.mock.calls[0][0] as string;
        expect(url).toContain("sharing/historyCache?");
    });

    it("marks an incident event (Forced Unlock) with a warning icon", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseApiData.mockReturnValue([]);
        mockGetBoolean.mockImplementation(
            (_url: string, cb: (v: boolean) => void) => cb(false),
        );
        mockGet.mockImplementation((_url: string, cb: (r: unknown) => void) =>
            cb({ data: oneEvent(5) /* Forced Unlock */ }),
        );

        const container = renderTable();

        expect(
            container.querySelector('[data-testid="history-incident-icon"]'),
        ).not.toBeNull();
    });

    it("does not mark a routine event (Check Out) with a warning icon", () => {
        mockUseTeamCollectionCapabilities.mockReturnValue(cloudCapabilities);
        mockUseApiData.mockReturnValue([]);
        mockGetBoolean.mockImplementation(
            (_url: string, cb: (v: boolean) => void) => cb(false),
        );
        mockGet.mockImplementation((_url: string, cb: (r: unknown) => void) =>
            cb({ data: oneEvent(0) /* Check Out */ }),
        );

        const container = renderTable();

        expect(
            container.querySelector('[data-testid="history-incident-icon"]'),
        ).toBeNull();
    });
});
