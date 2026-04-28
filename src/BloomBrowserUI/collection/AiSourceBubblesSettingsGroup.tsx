import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrCustomObjectInput,
    ConfigrGroup,
    ConfigrInput,
    ConfigrSelect,
} from "@sillsdev/config-r";
import { MenuItem, TextField } from "@mui/material";
import { postJsonAsync } from "../utils/bloomApi";

export interface ITargetLanguageOption {
    value: string;
    label: string;
}

export interface IAiSourceBubblesValidationState {
    currentFingerprint?: string;
    validatedFingerprint?: string;
    succeeded?: boolean;
    message?: string;
}

export interface IAiSourceBubblesSettings {
    allowAiSourceBubbles?: boolean;
    aiSourceBubblesProvider?: string;
    aiSourceBubblesTargetLanguageTag?: string;
    aiSourceBubblesDeepLApiKey?: string;
    aiSourceBubblesGoogleServiceAccountEmail?: string;
    aiSourceBubblesGooglePrivateKey?: string;
}

interface IAiSourceBubblesSupportedLanguagesResponse {
    languages?: ITargetLanguageOption[];
    message?: string;
}

export function parseSupportedTargetLanguageOptions(
    data?: IAiSourceBubblesSupportedLanguagesResponse,
): ITargetLanguageOption[] {
    const rawLanguages = data?.languages;
    if (!Array.isArray(rawLanguages)) {
        return [];
    }

    return rawLanguages
        .map((language) => {
            const candidate = language as {
                value?: string;
                label?: string;
                Value?: string;
                Label?: string;
            };
            const value = candidate.value ?? candidate.Value ?? "";
            const rawLabel = candidate.label ?? candidate.Label ?? value;
            const labelSuffix = ` (${value})`;
            const label = rawLabel.endsWith(labelSuffix)
                ? rawLabel.substring(0, rawLabel.length - labelSuffix.length)
                : rawLabel;

            if (!value) {
                return undefined;
            }

            return {
                value,
                label,
            };
        })
        .filter((language): language is ITargetLanguageOption => !!language);
}

function hasAiSourceBubblesRequiredConfig(
    settingsValue?: IAiSourceBubblesSettings,
): boolean {
    if (!settingsValue?.allowAiSourceBubbles) {
        return false;
    }

    if (!settingsValue.aiSourceBubblesProvider) {
        return false;
    }

    if (settingsValue.aiSourceBubblesProvider === "google") {
        return !!(
            settingsValue.aiSourceBubblesGoogleServiceAccountEmail?.trim() &&
            settingsValue.aiSourceBubblesGooglePrivateKey?.trim()
        );
    }

    return !!settingsValue.aiSourceBubblesDeepLApiKey?.trim();
}

function hasAiSourceBubblesRequiredValidationConfig(
    settingsValue?: IAiSourceBubblesSettings,
): boolean {
    return (
        hasAiSourceBubblesRequiredConfig(settingsValue) &&
        !!settingsValue?.aiSourceBubblesTargetLanguageTag?.trim()
    );
}

function usesProviderManagedTargetLanguages(
    settingsValue?: IAiSourceBubblesSettings,
): boolean {
    return !!settingsValue?.aiSourceBubblesProvider;
}

function getAiSourceBubblesProbeKey(
    settingsValue?: IAiSourceBubblesSettings,
): string {
    return JSON.stringify({
        allowAiSourceBubbles: settingsValue?.allowAiSourceBubbles ?? false,
        aiSourceBubblesProvider: settingsValue?.aiSourceBubblesProvider ?? "",
        aiSourceBubblesTargetLanguageTag:
            settingsValue?.aiSourceBubblesTargetLanguageTag ?? "",
        aiSourceBubblesDeepLApiKey:
            settingsValue?.aiSourceBubblesDeepLApiKey ?? "",
        aiSourceBubblesGoogleServiceAccountEmail:
            settingsValue?.aiSourceBubblesGoogleServiceAccountEmail ?? "",
        aiSourceBubblesGooglePrivateKey:
            settingsValue?.aiSourceBubblesGooglePrivateKey ?? "",
    });
}

function getAiSourceBubblesLanguageConfigKey(
    settingsValue?: IAiSourceBubblesSettings,
): string {
    return JSON.stringify({
        allowAiSourceBubbles: settingsValue?.allowAiSourceBubbles ?? false,
        aiSourceBubblesProvider: settingsValue?.aiSourceBubblesProvider ?? "",
        aiSourceBubblesDeepLApiKey:
            settingsValue?.aiSourceBubblesDeepLApiKey ?? "",
        aiSourceBubblesGoogleServiceAccountEmail:
            settingsValue?.aiSourceBubblesGoogleServiceAccountEmail ?? "",
        aiSourceBubblesGooglePrivateKey:
            settingsValue?.aiSourceBubblesGooglePrivateKey ?? "",
    });
}

function parseAiSourceBubblesValidationState(
    data: unknown,
): IAiSourceBubblesValidationState | undefined {
    if (!data || typeof data !== "object") {
        return undefined;
    }

    const candidate = data as {
        currentFingerprint?: string;
        CurrentFingerprint?: string;
        validatedFingerprint?: string;
        ValidatedFingerprint?: string;
        succeeded?: boolean;
        Succeeded?: boolean;
        message?: string;
        Message?: string;
        configurationFingerprint?: string;
        ConfigurationFingerprint?: string;
    };
    return {
        currentFingerprint:
            candidate.currentFingerprint ||
            candidate.CurrentFingerprint ||
            candidate.configurationFingerprint ||
            candidate.ConfigurationFingerprint,
        validatedFingerprint:
            candidate.validatedFingerprint ||
            candidate.ValidatedFingerprint ||
            candidate.configurationFingerprint ||
            candidate.ConfigurationFingerprint,
        succeeded: candidate.succeeded ?? candidate.Succeeded,
        message: candidate.message || candidate.Message,
    };
}

export const useAiSourceBubblesSettingsGroup = (props: {
    settings: IAiSourceBubblesSettings | undefined;
    initialValidation?: IAiSourceBubblesValidationState;
    groupLabel: string;
    providerLabel: string;
    targetLanguageLabel: string;
    deepLApiKeyLabel: string;
    googleServiceAccountEmailLabel: string;
    googlePrivateKeyLabel: string;
    translationTestLabel: string;
}): React.ReactElement => {
    const [aiSourceBubblesValidation, setAiSourceBubblesValidation] =
        React.useState<IAiSourceBubblesValidationState | undefined>(
            props.initialValidation,
        );
    const [
        isAiSourceBubblesValidationPending,
        setIsAiSourceBubblesValidationPending,
    ] = React.useState(false);
    const [supportedTargetLanguages, setSupportedTargetLanguages] =
        React.useState<ITargetLanguageOption[]>([]);
    const [supportedLanguagesMessage, setSupportedLanguagesMessage] =
        React.useState<string>("");
    const [isLoadingSupportedLanguages, setIsLoadingSupportedLanguages] =
        React.useState(false);
    const [languageOptionsVersion, setLanguageOptionsVersion] =
        React.useState(0);
    const lastAiSourceBubblesProbeKeyRef = React.useRef<string>("");
    const lastSupportedLanguagesConfigKeyRef = React.useRef<string>("");
    const latestSettingsRef = React.useRef(props.settings);
    latestSettingsRef.current = props.settings;

    React.useEffect(() => {
        setAiSourceBubblesValidation(props.initialValidation);
        const loadedProbeKey = getAiSourceBubblesProbeKey(
            latestSettingsRef.current,
        );
        if (
            props.initialValidation?.validatedFingerprint &&
            props.initialValidation.validatedFingerprint ===
                props.initialValidation.currentFingerprint &&
            props.initialValidation.message
        ) {
            lastAiSourceBubblesProbeKeyRef.current = loadedProbeKey;
        } else {
            lastAiSourceBubblesProbeKeyRef.current = "";
        }
    }, [props.initialValidation]);

    const loadSupportedLanguages = React.useCallback(async () => {
        const languageConfigKey = getAiSourceBubblesLanguageConfigKey(
            props.settings,
        );
        if (!usesProviderManagedTargetLanguages(props.settings)) {
            setSupportedTargetLanguages([]);
            setSupportedLanguagesMessage("");
            lastSupportedLanguagesConfigKeyRef.current = languageConfigKey;
            return;
        }

        if (!hasAiSourceBubblesRequiredConfig(props.settings)) {
            setSupportedTargetLanguages([]);
            setSupportedLanguagesMessage("");
            lastSupportedLanguagesConfigKeyRef.current = "";
            return;
        }

        if (
            languageConfigKey === lastSupportedLanguagesConfigKeyRef.current &&
            supportedTargetLanguages.length > 0
        ) {
            return;
        }

        setIsLoadingSupportedLanguages(true);
        setSupportedLanguagesMessage("");
        try {
            const response = await postJsonAsync(
                "settings/aiSourceBubblesSupportedLanguages",
                props.settings,
            );
            const data = response?.data as
                | IAiSourceBubblesSupportedLanguagesResponse
                | undefined;
            const languages = parseSupportedTargetLanguageOptions(data);
            setSupportedTargetLanguages(languages);
            setSupportedLanguagesMessage(data?.message ?? "");
            lastSupportedLanguagesConfigKeyRef.current = languageConfigKey;
            setLanguageOptionsVersion((value) => value + 1);
        } finally {
            setIsLoadingSupportedLanguages(false);
        }
    }, [props.settings, supportedTargetLanguages.length]);

    React.useEffect(() => {
        const currentLanguageConfigKey = getAiSourceBubblesLanguageConfigKey(
            props.settings,
        );
        if (
            currentLanguageConfigKey !==
            lastSupportedLanguagesConfigKeyRef.current
        ) {
            setSupportedTargetLanguages([]);
            setSupportedLanguagesMessage("");
        }
    }, [props.settings]);

    // This effect is necessary because provider-backed target-language lists depend on
    // external credentials and should be ready as soon as the current provider config is usable.
    React.useEffect(() => {
        if (!hasAiSourceBubblesRequiredConfig(props.settings)) {
            return;
        }

        void loadSupportedLanguages();
    }, [loadSupportedLanguages, props.settings]);

    // This effect is necessary because validation must synchronize the current Settings form values
    // with the backend/provider after the user stops typing relevant AI configuration fields.
    React.useEffect(() => {
        if (!props.settings?.allowAiSourceBubbles) {
            setIsAiSourceBubblesValidationPending(false);
            return;
        }

        const probeKey = getAiSourceBubblesProbeKey(props.settings);
        if (probeKey === lastAiSourceBubblesProbeKeyRef.current) {
            return;
        }

        lastAiSourceBubblesProbeKeyRef.current = probeKey;
        if (!hasAiSourceBubblesRequiredValidationConfig(props.settings)) {
            setIsAiSourceBubblesValidationPending(false);
            setAiSourceBubblesValidation(undefined);
            return;
        }

        setIsAiSourceBubblesValidationPending(true);
        setAiSourceBubblesValidation(undefined);

        let cancelled = false;
        const timeoutId = window.setTimeout(() => {
            void (async () => {
                try {
                    const response = await postJsonAsync(
                        "settings/validateAiSourceBubbles",
                        props.settings,
                    );
                    if (cancelled) {
                        return;
                    }

                    setAiSourceBubblesValidation(
                        parseAiSourceBubblesValidationState(response?.data),
                    );
                } finally {
                    if (!cancelled) {
                        setIsAiSourceBubblesValidationPending(false);
                    }
                }
            })();
        }, 600);

        return () => {
            cancelled = true;
            window.clearTimeout(timeoutId);
        };
    }, [props.settings]);

    const AiSourceBubblesTargetLanguageControl: React.FunctionComponent<{
        value: string;
        disabled?: boolean;
        onChange: (value: string) => void;
    }> = (controlProps) => {
        if (!usesProviderManagedTargetLanguages(props.settings)) {
            return (
                <TextField
                    fullWidth={true}
                    size="small"
                    value={controlProps.value || ""}
                    disabled={controlProps.disabled}
                    onChange={(event) => {
                        controlProps.onChange(event.target.value);
                    }}
                    inputProps={{
                        "data-testid":
                            "ai-source-bubbles-target-language-input",
                    }}
                />
            );
        }

        const currentValue = controlProps.value || "";
        const knownOptions = supportedTargetLanguages.some(
            (option) => option.value === currentValue,
        )
            ? supportedTargetLanguages
            : currentValue
              ? [
                    ...supportedTargetLanguages,
                    {
                        value: currentValue,
                        label: currentValue,
                    },
                ]
              : supportedTargetLanguages;

        return (
            <TextField
                select={true}
                fullWidth={true}
                size="small"
                value={currentValue}
                disabled={controlProps.disabled}
                helperText={supportedLanguagesMessage || undefined}
                onChange={(event) => {
                    controlProps.onChange(event.target.value);
                }}
                SelectProps={{
                    onOpen: () => {
                        void loadSupportedLanguages();
                    },
                }}
                inputProps={{
                    "data-testid": "ai-source-bubbles-target-language-select",
                    "data-language-options-version": languageOptionsVersion,
                }}
            >
                <MenuItem value={""}></MenuItem>
                {isLoadingSupportedLanguages && (
                    <MenuItem value="" disabled={true}>
                        Loading languages...
                    </MenuItem>
                )}
                {knownOptions.map((option) => (
                    <MenuItem key={option.value} value={option.value}>
                        {option.label}
                    </MenuItem>
                ))}
            </TextField>
        );
    };

    const validationText = React.useMemo(() => {
        if (isAiSourceBubblesValidationPending) {
            return {
                text: "Testing translation...",
                color: "#555",
            };
        }

        if (!aiSourceBubblesValidation?.message) {
            return {
                text: "",
                color: "#555",
            };
        }

        return {
            text: aiSourceBubblesValidation.succeeded
                ? `\"Today a reader, tomorrow a leader.\" --> ${aiSourceBubblesValidation.message}`
                : `Translation test failed: ${aiSourceBubblesValidation.message}`,
            color: aiSourceBubblesValidation.succeeded ? "#2e7d32" : "#b3261e",
        };
    }, [aiSourceBubblesValidation, isAiSourceBubblesValidationPending]);

    const AiSourceBubblesValidationStatusControl: React.FunctionComponent<{
        value: string;
        disabled?: boolean;
        onChange: (value: string) => void;
    }> = () => {
        return (
            <div
                data-testid="ai-source-bubbles-validation-status"
                css={css`
                    min-height: 2.5em;
                    display: flex;
                    align-items: flex-start;
                    color: ${validationText.color};
                    white-space: pre-wrap;
                    overflow-wrap: anywhere;
                    word-break: break-word;
                    line-height: 1.35;
                    padding-top: 4px;
                    padding-bottom: 4px;

                    max-width: 500px;
                    margin-top: 50px;
                `}
            >
                {validationText.text}
            </div>
        );
    };

    return (
        <ConfigrGroup label={props.groupLabel}>
            <ConfigrSelect
                label={props.providerLabel}
                path="aiSourceBubblesProvider"
                options={[
                    { label: "DeepL", value: "deepl" },
                    { label: "Google Translate", value: "google" },
                ]}
            />
            {props.settings?.aiSourceBubblesProvider === "deepl" && (
                <ConfigrInput
                    path="aiSourceBubblesDeepLApiKey"
                    label={props.deepLApiKeyLabel}
                    charactersWide={30}
                />
            )}
            {props.settings?.aiSourceBubblesProvider === "google" && (
                <>
                    <ConfigrInput
                        path="aiSourceBubblesGoogleServiceAccountEmail"
                        charactersWide={30}
                        label={props.googleServiceAccountEmailLabel}
                    />
                    <ConfigrInput
                        path="aiSourceBubblesGooglePrivateKey"
                        label={props.googlePrivateKeyLabel}
                        charactersWide={30}
                        allowNewLines={true}
                        minLinesToShow={2}
                        maxLinesToShowBeforeScrolling={6}
                    />
                </>
            )}
            <ConfigrCustomObjectInput<string>
                path="aiSourceBubblesTargetLanguageTag"
                control={AiSourceBubblesTargetLanguageControl}
                label={props.targetLanguageLabel}
            />
            <ConfigrCustomObjectInput<string>
                path="aiSourceBubblesValidationMessage"
                control={AiSourceBubblesValidationStatusControl}
                label={props.translationTestLabel}
                overrideValue={`${languageOptionsVersion}:${validationText.text}`}
            />
        </ConfigrGroup>
    );
};
