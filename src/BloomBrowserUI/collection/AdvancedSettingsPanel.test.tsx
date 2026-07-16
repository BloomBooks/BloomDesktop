import * as React from "react";
import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderTestRoot } from "../utils/testRender";

// Tests the "Cloud Team Collections (experimental)" checkbox this task adds alongside the
// existing "Team Collections" one: same wiring (settings/advancedProgramSettings GET/POST,
// ExperimentalFeatures.kCloudTeamCollections on the C# side), same enabled/disabled rules
// (subscription-tier feature status AND "not currently connected to one" from the host dialog).

const { mockGet, mockPostJson, mockUseGetFeatureStatus } = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockPostJson: vi.fn(),
    mockUseGetFeatureStatus: vi.fn(),
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        get: mockGet,
        postJson: mockPostJson,
    };
});

vi.mock("../react_components/featureStatus", async (importOriginal) => {
    const actual =
        await importOriginal<
            typeof import("../react_components/featureStatus")
        >();
    return {
        ...actual,
        useGetFeatureStatus: mockUseGetFeatureStatus,
    };
});

// A minimal stand-in for @sillsdev/config-r that's just enough to (a) render each ConfigrBoolean
// as an inspectable checkbox carrying its path/label/disabled, and (b) let a test simulate the
// user toggling a specific field via ConfigrPane's onChange, the same way
// BookAndPageSettingsDialog.saving.test.tsx's config-r mock does.
vi.mock("@sillsdev/config-r", () => ({
    ConfigrPane: (props: {
        children: React.ReactNode;
        initialValues: Record<string, unknown>;
        onChange: (settings: unknown) => void;
    }) => (
        <div>
            <button
                data-testid="toggle-allowCloudTeamCollection"
                onClick={() =>
                    props.onChange({
                        ...props.initialValues,
                        allowCloudTeamCollection:
                            !props.initialValues.allowCloudTeamCollection,
                    })
                }
            >
                Toggle
            </button>
            {props.children}
        </div>
    ),
    ConfigrPage: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrGroup: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrBoolean: (props: {
        label: string;
        path: string;
        disabled?: boolean;
    }) => (
        <input
            type="checkbox"
            readOnly
            data-testid={`configr-boolean-${props.path}`}
            aria-label={props.label}
            disabled={props.disabled}
        />
    ),
    ConfigrInput: () => null,
}));

import { AdvancedSettingsPanel } from "./AdvancedSettingsPanel";

function render(): HTMLDivElement {
    return renderTestRoot(<AdvancedSettingsPanel />);
}

// Mirrors the shape CollectionSettingsApi.GetAdvancedSettingsData returns.
function respondWith(overrides: {
    allowCloudTeamCollection?: boolean;
    allowCloudTeamCollectionEnabled?: boolean;
}) {
    mockGet.mockImplementation(
        (url: string, callback: (result: { data: unknown }) => void) => {
            if (url === "settings/advancedProgramSettings") {
                callback({
                    data: {
                        values: {
                            autoUpdate: false,
                            showExperimentalBookSources: false,
                            allowTeamCollection: false,
                            allowCloudTeamCollection:
                                overrides.allowCloudTeamCollection ?? false,
                            allowAppBuilder: false,
                            showQrCode: false,
                            qrcodeCaption: "",
                        },
                        showAutoUpdate: false,
                        showExperimentalBookSourcesOption: false,
                        allowTeamCollectionEnabled: true,
                        allowCloudTeamCollectionEnabled:
                            overrides.allowCloudTeamCollectionEnabled ?? true,
                    },
                });
            }
        },
    );
}

afterEach(() => {
    vi.clearAllMocks();
    mockUseGetFeatureStatus.mockReturnValue({ enabled: true });
});

describe("AdvancedSettingsPanel: Cloud Team Collections (experimental) checkbox", () => {
    it("renders the checkbox, labeled distinctly from the folder-based Team Collections one", () => {
        respondWith({});

        const container = render();

        const checkbox = container.querySelector(
            '[data-testid="configr-boolean-allowCloudTeamCollection"]',
        );
        expect(checkbox).not.toBeNull();
        // The test-only localizationManager mock (vitest.setup.ts) resolves every l10nKey to the
        // key itself rather than the English fallback (see JoinCloudCollectionDialog.test.tsx's
        // own comment about this), so we assert on the l10n id here, not the English text.
        expect(checkbox!.getAttribute("aria-label")).toBe(
            "CollectionSettingsDialog.AdvancedTab.Experimental.CloudTeamCollections",
        );
    });

    it("is enabled when the subscription feature status allows it and the host dialog reports allowCloudTeamCollectionEnabled=true", () => {
        respondWith({ allowCloudTeamCollectionEnabled: true });
        mockUseGetFeatureStatus.mockReturnValue({ enabled: true });

        const container = render();

        const checkbox = container.querySelector(
            '[data-testid="configr-boolean-allowCloudTeamCollection"]',
        ) as HTMLInputElement;
        expect(checkbox.disabled).toBe(false);
    });

    it("is disabled when the CloudTeamCollection subscription feature status is not enabled", () => {
        respondWith({ allowCloudTeamCollectionEnabled: true });
        mockUseGetFeatureStatus.mockImplementation(
            (featureName: string | undefined) =>
                featureName === "CloudTeamCollection"
                    ? { enabled: false }
                    : { enabled: true },
        );

        const container = render();

        const checkbox = container.querySelector(
            '[data-testid="configr-boolean-allowCloudTeamCollection"]',
        ) as HTMLInputElement;
        expect(checkbox.disabled).toBe(true);
    });

    it("is disabled when the host dialog reports allowCloudTeamCollectionEnabled=false (currently connected to a cloud collection)", () => {
        respondWith({ allowCloudTeamCollectionEnabled: false });
        mockUseGetFeatureStatus.mockReturnValue({ enabled: true });

        const container = render();

        const checkbox = container.querySelector(
            '[data-testid="configr-boolean-allowCloudTeamCollection"]',
        ) as HTMLInputElement;
        expect(checkbox.disabled).toBe(true);
    });

    it("posts the toggled value back to settings/advancedProgramSettings", () => {
        respondWith({ allowCloudTeamCollection: false });

        const container = render();

        const toggleButton = container.querySelector(
            '[data-testid="toggle-allowCloudTeamCollection"]',
        ) as HTMLButtonElement;
        act(() => {
            toggleButton.click();
        });

        expect(mockPostJson).toHaveBeenCalledWith(
            "settings/advancedProgramSettings",
            expect.objectContaining({ allowCloudTeamCollection: true }),
        );
    });
});
