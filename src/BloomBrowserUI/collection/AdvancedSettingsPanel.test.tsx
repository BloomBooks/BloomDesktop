// This test harness deliberately mounts/unmounts via the React 17-style ReactDOM.render /
// unmountComponentAtNode API (synchronous, simple for these unit tests). Disable the
// React-18-deprecation rule for the file rather than migrate the harness to createRoot here.
/* eslint-disable react/no-deprecated */
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
            aiTranslation: {
                targetLanguageTag: "en",
                engines: [
                    {
                        providerId: "deepl",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                    {
                        providerId: "google",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                    {
                        providerId: "alpha2",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                ],
            },
            showQrCode: true,
            qrcodeCaption: "caption",
        } as Record<string, unknown>,
        showAutoUpdate: true,
        showExperimentalBookSourcesOption: false,
        allowTeamCollectionEnabled: true,
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
                    data-testid="enable-google"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiTranslationTargetLanguageTag: "es",
                            aiTranslationGoogleEnabled: true,
                            aiTranslationGoogleServiceAccountEmail:
                                "service-account@example.com",
                            aiTranslationGooglePrivateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        });
                    }}
                >
                    Enable Google
                </button>
                <button
                    data-testid="enable-google-without-target"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiTranslationTargetLanguageTag: "",
                            aiTranslationGoogleEnabled: true,
                            aiTranslationGoogleServiceAccountEmail:
                                "service-account@example.com",
                            aiTranslationGooglePrivateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        });
                    }}
                >
                    Enable Google Without Target
                </button>
                <button
                    data-testid="set-french-target"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            aiTranslationTargetLanguageTag: "fr",
                        });
                    }}
                >
                    Set French Target
                </button>
                <button
                    data-testid="enable-deepl-and-google"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiTranslationTargetLanguageTag: "es",
                            aiTranslationDeepLEnabled: true,
                            aiTranslationDeepLApiKey: "deepl-key",
                            aiTranslationGoogleEnabled: true,
                            aiTranslationGoogleServiceAccountEmail:
                                "service-account@example.com",
                            aiTranslationGooglePrivateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        });
                    }}
                >
                    Enable DeepL And Google
                </button>
                <button
                    data-testid="enable-all-three"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiTranslationTargetLanguageTag: "es",
                            aiTranslationDeepLEnabled: true,
                            aiTranslationDeepLApiKey: "deepl-key",
                            aiTranslationGoogleEnabled: true,
                            aiTranslationGoogleServiceAccountEmail:
                                "service-account@example.com",
                            aiTranslationGooglePrivateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                            aiTranslationAlpha2Enabled: true,
                            aiTranslationAlpha2ApiKey: "alpha2-key",
                        });
                    }}
                >
                    Enable All Three
                </button>
                <button
                    data-testid="enable-alpha2-with-source"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            allowAiSourceBubbles: true,
                            aiTranslationTargetLanguageTag: "es",
                            aiTranslationAlpha2Enabled: true,
                            aiTranslationAlpha2ApiKey: "alpha2-key",
                            aiTranslationAlpha2SourceLanguageTag: "fr",
                        });
                    }}
                >
                    Enable Alpha2 With Source
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
    ConfigrBoolean: (props: { path: string; label: string }) => (
        <div data-testid={`configr-boolean-${props.path}`}>{props.label}</div>
    ),
    ConfigrInput: (props: { path: string; label: string }) => (
        <div data-testid={`configr-input-${props.path}`}>{props.label}</div>
    ),
    ConfigrSelect: () => null,
    ConfigrCustomObjectInput: (props: {
        control: React.FunctionComponent<{
            value: unknown;
            disabled?: boolean;
            onChange: (value: unknown) => void;
        }>;
        overrideValue?: unknown;
    }) => {
        const Control = props.control;
        return (
            <Control value={props.overrideValue ?? ""} onChange={() => {}} />
        );
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
import { parseSupportedTargetLanguageOptions } from "./AiTranslationSettingsGroup";

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
            aiTranslation: {
                targetLanguageTag: "en",
                engines: [
                    {
                        providerId: "deepl",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                    {
                        providerId: "google",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                    {
                        providerId: "alpha2",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                ],
            },
            showQrCode: true,
            qrcodeCaption: "caption",
        };
        mockPostJsonAsync.mockImplementation(
            async (endpoint: string, body?: unknown) => {
                if (endpoint === "settings/validateAiTranslationEngine") {
                    const providerId = (body as { providerId: string })
                        .providerId;
                    return {
                        data: {
                            succeeded: true,
                            message: `translated by ${providerId}`,
                        },
                    };
                }

                if (endpoint === "settings/aiTranslationSupportedLanguages") {
                    return {
                        data: {
                            languages: [
                                {
                                    tag: "es",
                                    name: "Spanish",
                                    providerIds: ["deepl", "google"],
                                },
                                {
                                    tag: "fra",
                                    name: "French (fra)",
                                    providerIds: ["deepl"],
                                },
                            ],
                        },
                    };
                }

                if (
                    endpoint === "settings/aiTranslationAlpha2SourceLanguages"
                ) {
                    return { data: { languages: [] } };
                }

                throw new Error(`Unexpected async POST endpoint: ${endpoint}`);
            },
        );
    });

    afterEach(() => {
        ReactDOM.unmountComponentAtNode(container);
        container.remove();
        document.body.innerHTML = "";
        vi.useRealTimers();
    });

    it("shows an engine's credential fields only once it is enabled", async () => {
        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        expect(
            container.querySelector(
                '[data-testid="configr-input-aiTranslationGoogleServiceAccountEmail"]',
            ),
        ).toBeNull();

        click('[data-testid="enable-google"]');

        expect(
            container.querySelector(
                '[data-testid="configr-input-aiTranslationGoogleServiceAccountEmail"]',
            ),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="configr-input-aiTranslationGooglePrivateKey"]',
            ),
        ).not.toBeNull();
        // DeepL and Alpha2 were never enabled, so their credential fields stay hidden.
        expect(
            container.querySelector(
                '[data-testid="configr-input-aiTranslationDeepLApiKey"]',
            ),
        ).toBeNull();
        expect(
            container.querySelector(
                '[data-testid="configr-input-aiTranslationAlpha2ApiKey"]',
            ),
        ).toBeNull();
        // The three enable toggles are always present, regardless of enabled state.
        expect(
            container.querySelector(
                '[data-testid="configr-boolean-aiTranslationDeepLEnabled"]',
            ),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="configr-boolean-aiTranslationAlpha2Enabled"]',
            ),
        ).not.toBeNull();
    });

    it("posts the store payload in the pinned nested wire shape", async () => {
        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        click('[data-testid="enable-all-three"]');

        expect(mockPostJson).toHaveBeenCalledWith(
            "settings/advancedProgramSettings",
            expect.objectContaining({
                allowAiSourceBubbles: true,
                aiTranslation: {
                    targetLanguageTag: "es",
                    engines: [
                        // Google's serviceAccountEmail/privateKey must NOT leak onto the other
                        // engines' records (doing so wiped their validation when Google creds
                        // were edited); non-google engines send empty Google fields.
                        expect.objectContaining({
                            providerId: "deepl",
                            enabled: true,
                            apiKey: "deepl-key",
                            serviceAccountEmail: "",
                            privateKey: "",
                        }),
                        expect.objectContaining({
                            providerId: "google",
                            enabled: true,
                            serviceAccountEmail: "service-account@example.com",
                            privateKey:
                                "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        }),
                        expect.objectContaining({
                            providerId: "alpha2",
                            enabled: true,
                            apiKey: "alpha2-key",
                            serviceAccountEmail: "",
                            privateKey: "",
                        }),
                    ],
                },
            }),
        );

        // The flat, Configr-internal AI keys must not leak into the wire payload.
        const [, wirePayload] = mockPostJson.mock.calls[0];
        expect(wirePayload).not.toHaveProperty("aiTranslationGoogleEnabled");
        expect(wirePayload).not.toHaveProperty(
            "aiTranslationTargetLanguageTag",
        );
    });

    it("debounces per-engine validation and posts only the providerId", async () => {
        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        click('[data-testid="enable-google"]');

        // Only google is enabled+credentialed, so only it should be probed.
        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/validateAiTranslationEngine",
            { providerId: "google" },
        );
        expect(mockPostJsonAsync).not.toHaveBeenCalledWith(
            "settings/validateAiTranslationEngine",
            { providerId: "deepl" },
        );
        expect(container.textContent).toContain("translated by google");

        click('[data-testid="set-french-target"]');

        expect(container.textContent).toContain("Testing translation...");

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/validateAiTranslationEngine",
            { providerId: "google" },
        );
    });

    it("clears validation immediately when the target language is removed, and waits for one before re-validating", async () => {
        initialAdvancedSettingsData.values = {
            ...initialAdvancedSettingsData.values,
            allowAiSourceBubbles: true,
            aiTranslation: {
                targetLanguageTag: "es",
                engines: [
                    {
                        providerId: "deepl",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                    {
                        providerId: "google",
                        enabled: true,
                        apiKey: "",
                        serviceAccountEmail: "service-account@example.com",
                        privateKey:
                            "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
                        validation: {
                            succeeded: true,
                            message: "translated by google",
                            upToDate: true,
                        },
                    },
                    {
                        providerId: "alpha2",
                        enabled: false,
                        apiKey: "",
                        serviceAccountEmail: "",
                        privateKey: "",
                        validation: {
                            succeeded: false,
                            message: "",
                            upToDate: false,
                        },
                    },
                ],
            },
        };

        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        expect(container.textContent).toContain("translated by google");

        click('[data-testid="enable-google-without-target"]');

        expect(container.textContent).not.toContain("translated by google");
        expect(container.textContent).not.toContain("Testing translation...");

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).not.toHaveBeenCalledWith(
            "settings/validateAiTranslationEngine",
            { providerId: "google" },
        );

        click('[data-testid="set-french-target"]');

        expect(container.textContent).toContain("Testing translation...");

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/validateAiTranslationEngine",
            { providerId: "google" },
        );
    });

    it("fetches the union of provider-supported languages when the ready engine set changes", async () => {
        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        click('[data-testid="enable-deepl-and-google"]');

        const targetLanguageSelect = container.querySelector(
            '[data-testid="ai-translation-target-language-select"]',
        ) as HTMLElement;
        expect(targetLanguageSelect).not.toBeNull();

        await act(async () => {
            targetLanguageSelect.dispatchEvent(
                new Event("mousedown", { bubbles: true }),
            );
        });

        expect(mockPostJsonAsync).toHaveBeenCalledWith(
            "settings/aiTranslationSupportedLanguages",
            expect.anything(),
        );

        expect(
            parseSupportedTargetLanguageOptions({
                languages: [
                    { tag: "es", name: "Spanish (es)", providerIds: ["deepl"] },
                ],
            }),
        ).toEqual([{ value: "es", label: "Spanish", providerIds: ["deepl"] }]);
    });

    it("round-trips alpha2 sourceLanguageTag through the wire payload", async () => {
        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        click('[data-testid="enable-alpha2-with-source"]');

        expect(mockPostJson).toHaveBeenCalledWith(
            "settings/advancedProgramSettings",
            expect.objectContaining({
                aiTranslation: expect.objectContaining({
                    engines: expect.arrayContaining([
                        expect.objectContaining({
                            providerId: "alpha2",
                            enabled: true,
                            apiKey: "alpha2-key",
                            sourceLanguageTag: "fr",
                        }),
                    ]),
                }),
            }),
        );
        // The non-alpha2 engines must send an empty source language.
        const [, wirePayload] = mockPostJson.mock.calls[0];
        const engines = (
            wirePayload as {
                aiTranslation: {
                    engines: Array<{
                        providerId: string;
                        sourceLanguageTag: string;
                    }>;
                };
            }
        ).aiTranslation.engines;
        expect(
            engines.find((e) => e.providerId === "deepl")?.sourceLanguageTag,
        ).toBe("");
    });

    it("shows the amber 'will be skipped' note when an engine doesn't support the target", async () => {
        mockPostJsonAsync.mockImplementation(
            async (endpoint: string, body?: unknown) => {
                if (endpoint === "settings/validateAiTranslationEngine") {
                    const providerId = (body as { providerId: string })
                        .providerId;
                    return {
                        data: {
                            succeeded: false,
                            targetLanguageNotSupported: true,
                            message: `${providerId} cannot do this language`,
                        },
                    };
                }
                if (endpoint === "settings/aiTranslationSupportedLanguages") {
                    return { data: { languages: [] } };
                }
                if (
                    endpoint === "settings/aiTranslationAlpha2SourceLanguages"
                ) {
                    return { data: { languages: [] } };
                }
                throw new Error(`Unexpected async POST endpoint: ${endpoint}`);
            },
        );

        await act(async () => {
            ReactDOM.render(<AdvancedSettingsPanel />, container);
        });

        click('[data-testid="enable-google"]');

        await act(async () => {
            await vi.advanceTimersByTimeAsync(601);
        });

        expect(container.textContent).toContain(
            "does not support translating to",
        );
        // The red "Translation test failed" text must NOT be shown for the not-supported case.
        expect(container.textContent).not.toContain("Translation test failed");
    });
});
