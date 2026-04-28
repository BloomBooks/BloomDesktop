import * as React from "react";
import ReactDOM from "react-dom";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const {
    mockPostJson,
    mockPostJsonAsync,
    configrPaneState,
    initialAdvancedSettingsData,
} = vi.hoisted(() => ({
    mockPostJson: vi.fn(),
    mockPostJsonAsync: vi.fn(),
    configrPaneState: {
        lastInitialValues: undefined as Record<string, unknown> | undefined,
    },
    initialAdvancedSettingsData: {
        values: {
            autoUpdate: true,
            showExperimentalBookSources: false,
            allowTeamCollection: false,
            allowAppBuilder: false,
            allowAiSourceBubbles: false,
            aiSourceBubblesProvider: "deepl",
            aiSourceBubblesTargetLanguageTag: "en",
            aiSourceBubblesDeepLApiKey: "",
            aiSourceBubblesGoogleServiceAccountEmail: "",
            aiSourceBubblesGooglePrivateKey: "",
            showQrCode: true,
            qrcodeCaption: "caption",
        },
        showAutoUpdate: true,
        showExperimentalBookSourcesOption: false,
        allowTeamCollectionEnabled: true,
        aiSourceBubblesValidation: {
            currentFingerprint: "",
            validatedFingerprint: "",
            succeeded: false,
            message: "",
        },
    },
}));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();

    return {
        ...actual,
        get: (
            endpoint: string,
            callback: (result: { data: unknown }) => void,
        ) => {
            if (endpoint === "settings/advancedProgramSettings") {
                callback({ data: initialAdvancedSettingsData });
                return;
            }

            throw new Error(`Unexpected GET endpoint: ${endpoint}`);
        },
        postJson: mockPostJson,
        postJsonAsync: mockPostJsonAsync,
    };
});

vi.mock("../react_components/featureStatus", () => ({
    useGetFeatureStatus: () => ({ enabled: true }),
}));

vi.mock("../react_components/l10nHooks", () => ({
    useL10n: (englishText: string) => englishText,
}));

vi.mock("../react_components/requiresSubscription", () => ({
    BloomSubscriptionIndicatorIconAndText: () => null,
}));

vi.mock("../utils/WireUpWinform", () => ({
    WireUpForWinforms: vi.fn(),
}));

vi.mock("@sillsdev/config-r", () => ({
    ConfigrPane: (props: {
        children: React.ReactNode;
        initialValues: Record<string, unknown>;
        onChange: (settings: unknown) => void;
    }) => {
        configrPaneState.lastInitialValues = props.initialValues;

        return (
            <div>
                <button
                    data-testid="set-google-config"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiSourceBubblesProvider: "google",
                            aiSourceBubblesTargetLanguageTag: "es",
                            aiSourceBubblesDeepLApiKey: "",
                            aiSourceBubblesGoogleServiceAccountEmail:
                                "service-account@example.com",
                            aiSourceBubblesGooglePrivateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        });
                    }}
                >
                    Set Google Config
                </button>
                <button
                    data-testid="set-google-without-target"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiSourceBubblesProvider: "google",
                            aiSourceBubblesTargetLanguageTag: "",
                            aiSourceBubblesDeepLApiKey: "",
                            aiSourceBubblesGoogleServiceAccountEmail:
                                "service-account@example.com",
                            aiSourceBubblesGooglePrivateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        });
                    }}
                >
                    Set Google Without Target
                </button>
                <button
                    data-testid="set-french-target"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiSourceBubblesProvider: "google",
                            aiSourceBubblesTargetLanguageTag: "fr",
                            aiSourceBubblesDeepLApiKey: "",
                            aiSourceBubblesGoogleServiceAccountEmail:
                                "service-account@example.com",
                            aiSourceBubblesGooglePrivateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        });
                    }}
                >
                    Set French Target
                </button>
                {props.children}
            </div>
        );
    },
    ConfigrPage: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrGroup: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrBoolean: () => null,
    ConfigrInput: () => null,
    ConfigrSelect: () => null,
    ConfigrCustomObjectInput: (props: {
        control: React.FunctionComponent<{
            value: string;
            disabled?: boolean;
            onChange: (value: string) => void;
        }>;
    }) => {
        const Control = props.control;
        return <Control value="" onChange={() => {}} />;
    },
    ConfigrCustomStringInput: (props: {
        control: React.FunctionComponent<{
            value: string;
            disabled?: boolean;
            onChange: (value: string) => void;
        }>;
    }) => {
        const Control = props.control;
        return <Control value="" onChange={() => {}} />;
    },
}));

import { AdvancedSettingsPanel } from "./AdvancedSettingsPanel";
import { parseSupportedTargetLanguageOptions } from "./AiSourceBubblesSettingsGroup";

describe("AdvancedSettingsPanel", () => {
    let container: HTMLDivElement;

    const click = (selector: string) => {
        const button = container.querySelector(selector) as HTMLButtonElement;
        expect(button).not.toBeNull();
        act(() => {
            button.click();
        });
    };

    beforeEach(() => {
        vi.useFakeTimers();
        container = document.createElement("div");
        document.body.appendChild(container);
        mockPostJson.mockReset();
        mockPostJsonAsync.mockReset();
        configrPaneState.lastInitialValues = undefined;
        initialAdvancedSettingsData.values = {
            autoUpdate: true,
            showExperimentalBookSources: false,
            allowTeamCollection: false,
            allowAppBuilder: false,
            allowAiSourceBubbles: false,
            aiSourceBubblesProvider: "deepl",
            aiSourceBubblesTargetLanguageTag: "en",
            aiSourceBubblesDeepLApiKey: "",
            aiSourceBubblesGoogleServiceAccountEmail: "",
            aiSourceBubblesGooglePrivateKey: "",
            showQrCode: true,
            qrcodeCaption: "caption",
        };
        initialAdvancedSettingsData.aiSourceBubblesValidation = {
            currentFingerprint: "",
            validatedFingerprint: "",
            succeeded: false,
            message: "",
        };
        mockPostJsonAsync.mockImplementation(async (endpoint: string) => {
            if (endpoint === "settings/validateAiSourceBubbles") {
                return {
                    data: {
                        currentFingerprint: "fingerprint",
                        validatedFingerprint: "fingerprint",
                        succeeded: true,
                        message: "La lectura es importante",
                    },
                };
            }

            if (endpoint === "settings/aiSourceBubblesSupportedLanguages") {
                return {
                    data: {
                        languages: [
                            { Value: "es", Label: "Spanish" },
                            { Value: "fra", Label: "French (fra)" },
                        ],
                    },
                };
            }

            throw new Error(`Unexpected async POST endpoint: ${endpoint}`);
        });
    });

    afterEach(() => {
        ReactDOM.unmountComponentAtNode(container);
        container.remove();
        document.body.innerHTML = "";
        vi.useRealTimers();
    });

    it("debounces AI validation and renders the translated probe result", async () => {
        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        click('[data-testid="set-google-config"]');

        expect(mockPostJson).toHaveBeenCalledWith(
            "settings/advancedProgramSettings",
            expect.objectContaining({
                allowAiSourceBubbles: true,
                aiSourceBubblesProvider: "google",
                aiSourceBubblesTargetLanguageTag: "es",
                aiSourceBubblesGoogleServiceAccountEmail:
                    "service-account@example.com",
            }),
        );

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/validateAiSourceBubbles",
            expect.objectContaining({
                allowAiSourceBubbles: true,
                aiSourceBubblesProvider: "google",
                aiSourceBubblesTargetLanguageTag: "es",
                aiSourceBubblesGoogleServiceAccountEmail:
                    "service-account@example.com",
            }),
        );
        expect(container.textContent).toContain(
            '"Today a reader, tomorrow a leader." --> La lectura es importante',
        );

        click('[data-testid="set-french-target"]');

        expect(container.textContent).toContain("Testing translation...");

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/validateAiSourceBubbles",
            expect.objectContaining({
                allowAiSourceBubbles: true,
                aiSourceBubblesProvider: "google",
                aiSourceBubblesTargetLanguageTag: "fr",
                aiSourceBubblesGoogleServiceAccountEmail:
                    "service-account@example.com",
            }),
        );
    });

    it("clears the previous translation result immediately and waits for a target language before rerunning", async () => {
        initialAdvancedSettingsData.values = {
            ...initialAdvancedSettingsData.values,
            allowAiSourceBubbles: true,
            aiSourceBubblesProvider: "google",
            aiSourceBubblesTargetLanguageTag: "es",
            aiSourceBubblesGoogleServiceAccountEmail:
                "service-account@example.com",
            aiSourceBubblesGooglePrivateKey:
                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
        };
        initialAdvancedSettingsData.aiSourceBubblesValidation = {
            currentFingerprint: "loaded-fingerprint",
            validatedFingerprint: "loaded-fingerprint",
            succeeded: true,
            message: "La lectura es importante",
        };

        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        expect(container.textContent).toContain(
            '"Today a reader, tomorrow a leader." --> La lectura es importante',
        );

        click('[data-testid="set-google-without-target"]');

        expect(container.textContent).not.toContain(
            '"Today a reader, tomorrow a leader." --> La lectura es importante',
        );
        expect(container.textContent).not.toContain("Testing translation...");

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).not.toHaveBeenCalledWith(
            "settings/validateAiSourceBubbles",
            expect.objectContaining({
                aiSourceBubblesProvider: "google",
                aiSourceBubblesTargetLanguageTag: "",
            }),
        );

        click('[data-testid="set-french-target"]');

        expect(container.textContent).toContain("Testing translation...");

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/validateAiSourceBubbles",
            expect.objectContaining({
                allowAiSourceBubbles: true,
                aiSourceBubblesProvider: "google",
                aiSourceBubblesTargetLanguageTag: "fr",
                aiSourceBubblesGoogleServiceAccountEmail:
                    "service-account@example.com",
            }),
        );
    });

    it("fetches provider-supported languages for the target language selector", async () => {
        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        click('[data-testid="set-google-config"]');

        const targetLanguageSelect = container.querySelector(
            '[data-testid="ai-source-bubbles-target-language-select"]',
        ) as HTMLElement;
        expect(targetLanguageSelect).not.toBeNull();

        await act(async () => {
            targetLanguageSelect.dispatchEvent(
                new Event("mousedown", { bubbles: true }),
            );
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/aiSourceBubblesSupportedLanguages",
            expect.objectContaining({
                allowAiSourceBubbles: true,
                aiSourceBubblesProvider: "google",
                aiSourceBubblesGoogleServiceAccountEmail:
                    "service-account@example.com",
            }),
        );

        expect(
            parseSupportedTargetLanguageOptions({
                languages: [
                    { Value: "es", Label: "Spanish (es)" } as unknown as {
                        value: string;
                        label: string;
                    },
                ],
            }),
        ).toEqual([{ value: "es", label: "Spanish" }]);
    });
});
