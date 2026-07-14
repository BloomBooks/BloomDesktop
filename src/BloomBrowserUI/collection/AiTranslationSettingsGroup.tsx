import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrCustomObjectInput,
    ConfigrGroup,
    ConfigrInput,
} from "@sillsdev/config-r";
import { MenuItem, TextField } from "@mui/material";
import { postJsonAsync } from "../utils/bloomApi";

export type AiTranslationProviderId = "deepl" | "google" | "alpha2";

export interface ITargetLanguageOption {
    value: string;
    label: string;
    providerIds: AiTranslationProviderId[];
}

export interface IAiTranslationEngineValidation {
    succeeded: boolean;
    message: string;
    upToDate: boolean;
}

// The flat shape our React/Configr state uses. One boolean+credential set per engine,
// plus the target language shared by all engines. This is translated to/from the nested
// wire contract (IAiTranslationWireSettings) at the AdvancedSettingsPanel API boundary.
export interface IAiTranslationSettings {
    aiTranslationTargetLanguageTag?: string;
    aiTranslationDeepLEnabled?: boolean;
    aiTranslationDeepLApiKey?: string;
    aiTranslationGoogleEnabled?: boolean;
    aiTranslationGoogleServiceAccountEmail?: string;
    aiTranslationGooglePrivateKey?: string;
    aiTranslationAlpha2Enabled?: boolean;
    aiTranslationAlpha2ApiKey?: string;
}

// Every key of IAiTranslationSettings, used to split it out of / merge it into the
// larger IAdvancedSettings object that AdvancedSettingsPanel round-trips with the API.
export const aiTranslationFlatSettingsKeys = [
    "aiTranslationTargetLanguageTag",
    "aiTranslationDeepLEnabled",
    "aiTranslationDeepLApiKey",
    "aiTranslationGoogleEnabled",
    "aiTranslationGoogleServiceAccountEmail",
    "aiTranslationGooglePrivateKey",
    "aiTranslationAlpha2Enabled",
    "aiTranslationAlpha2ApiKey",
] as const;

export interface IAiTranslationWireEngineSettings {
    providerId: AiTranslationProviderId;
    enabled: boolean;
    apiKey: string;
    serviceAccountEmail: string;
    privateKey: string;
    validation: IAiTranslationEngineValidation;
}

// The exact shape posted/received under the "aiTranslation" key of the advanced-settings payload.
export interface IAiTranslationWireSettings {
    targetLanguageTag: string;
    engines: IAiTranslationWireEngineSettings[];
}

interface IEngineFieldSpec {
    providerId: AiTranslationProviderId;
    enabledPath: keyof IAiTranslationSettings;
    credentialPaths: Array<keyof IAiTranslationSettings>;
}

const deepLFieldSpec: IEngineFieldSpec = {
    providerId: "deepl",
    enabledPath: "aiTranslationDeepLEnabled",
    credentialPaths: ["aiTranslationDeepLApiKey"],
};

const googleFieldSpec: IEngineFieldSpec = {
    providerId: "google",
    enabledPath: "aiTranslationGoogleEnabled",
    credentialPaths: [
        "aiTranslationGoogleServiceAccountEmail",
        "aiTranslationGooglePrivateKey",
    ],
};

const alpha2FieldSpec: IEngineFieldSpec = {
    providerId: "alpha2",
    enabledPath: "aiTranslationAlpha2Enabled",
    credentialPaths: ["aiTranslationAlpha2ApiKey"],
};

// Always exactly these three engines, in this order, matching the pinned backend contract.
const aiTranslationEngineSpecs: IEngineFieldSpec[] = [
    deepLFieldSpec,
    googleFieldSpec,
    alpha2FieldSpec,
];

interface IAiTranslationSupportedLanguagesResponse {
    languages?: unknown[];
    message?: string;
}

function readField<T>(
    candidate: Record<string, unknown>,
    camelCaseName: string,
    pascalCaseName: string,
): T | undefined {
    return (candidate[camelCaseName] ?? candidate[pascalCaseName]) as
        | T
        | undefined;
}

// Parses the response of settings/aiTranslationSupportedLanguages. Tolerant of PascalCase
// field names since we don't control the exact casing the backend's JSON serializer produces.
export function parseSupportedTargetLanguageOptions(
    data?: IAiTranslationSupportedLanguagesResponse,
): ITargetLanguageOption[] {
    const rawLanguages = data?.languages;
    if (!Array.isArray(rawLanguages)) {
        return [];
    }

    return rawLanguages
        .map((language) => {
            const candidate = language as Record<string, unknown>;
            const value = readField<string>(candidate, "tag", "Tag") ?? "";
            const rawLabel =
                readField<string>(candidate, "name", "Name") ?? value;
            const providerIds =
                readField<AiTranslationProviderId[]>(
                    candidate,
                    "providerIds",
                    "ProviderIds",
                ) ?? [];
            const labelSuffix = ` (${value})`;
            const label = rawLabel.endsWith(labelSuffix)
                ? rawLabel.substring(0, rawLabel.length - labelSuffix.length)
                : rawLabel;

            if (!value) {
                return undefined;
            }

            return { value, label, providerIds };
        })
        .filter((language): language is ITargetLanguageOption => !!language);
}

function parseAiTranslationEngineValidation(
    data: unknown,
): IAiTranslationEngineValidation | undefined {
    if (!data || typeof data !== "object") {
        return undefined;
    }

    const candidate = data as Record<string, unknown>;
    return {
        succeeded:
            readField<boolean>(candidate, "succeeded", "Succeeded") ?? false,
        message: readField<string>(candidate, "message", "Message") ?? "",
        // A freshly-run validation is by definition current for the settings that produced it.
        upToDate: true,
    };
}

// Splits the nested wire settings for one engine's "validation" sub-object into our
// flat, tolerant-of-casing shape.
function parseWireEngineValidation(
    data: unknown,
): IAiTranslationEngineValidation | undefined {
    if (!data || typeof data !== "object") {
        return undefined;
    }
    const candidate = data as Record<string, unknown>;
    return {
        succeeded:
            readField<boolean>(candidate, "succeeded", "Succeeded") ?? false,
        message: readField<string>(candidate, "message", "Message") ?? "",
        upToDate:
            readField<boolean>(candidate, "upToDate", "UpToDate") ?? false,
    };
}

// Converts the nested wire contract (as received from settings/advancedProgramSettings)
// into our flat Configr-friendly settings plus a per-provider map of initial validation state.
export function flattenAiTranslationWireSettings(
    wire: IAiTranslationWireSettings | undefined,
): {
    flatSettings: IAiTranslationSettings;
    initialValidations: Partial<
        Record<AiTranslationProviderId, IAiTranslationEngineValidation>
    >;
} {
    const wireCandidate = (wire ?? {}) as unknown as Record<string, unknown>;
    const targetLanguageTag =
        readField<string>(
            wireCandidate,
            "targetLanguageTag",
            "TargetLanguageTag",
        ) ?? "";
    const rawEngines =
        readField<unknown[]>(wireCandidate, "engines", "Engines") ?? [];

    const flatSettings: IAiTranslationSettings = {
        aiTranslationTargetLanguageTag: targetLanguageTag,
    };
    const initialValidations: Partial<
        Record<AiTranslationProviderId, IAiTranslationEngineValidation>
    > = {};

    aiTranslationEngineSpecs.forEach((spec) => {
        const rawEngine = rawEngines
            .map((engine) => engine as Record<string, unknown>)
            .find(
                (engine) =>
                    readField<string>(engine, "providerId", "ProviderId") ===
                    spec.providerId,
            );

        const enabled =
            (rawEngine &&
                readField<boolean>(rawEngine, "enabled", "Enabled")) ??
            false;
        flatSettings[spec.enabledPath] = enabled as never;

        if (spec.providerId === "google") {
            flatSettings.aiTranslationGoogleServiceAccountEmail =
                (rawEngine &&
                    readField<string>(
                        rawEngine,
                        "serviceAccountEmail",
                        "ServiceAccountEmail",
                    )) ??
                "";
            flatSettings.aiTranslationGooglePrivateKey =
                (rawEngine &&
                    readField<string>(rawEngine, "privateKey", "PrivateKey")) ??
                "";
        } else {
            const apiKey =
                (rawEngine &&
                    readField<string>(rawEngine, "apiKey", "ApiKey")) ??
                "";
            flatSettings[spec.credentialPaths[0]] = apiKey as never;
        }

        const validation = parseWireEngineValidation(
            rawEngine && readField(rawEngine, "validation", "Validation"),
        );
        if (validation) {
            initialValidations[spec.providerId] = validation;
        }
    });

    return { flatSettings, initialValidations };
}

// Converts our flat Configr-friendly settings back into the nested wire contract for posting
// to settings/advancedProgramSettings. The validation sub-object is server-computed and ignored
// on store, so we send it back empty.
export function buildAiTranslationWirePayload(
    flat: IAiTranslationSettings,
): IAiTranslationWireSettings {
    return {
        targetLanguageTag: flat.aiTranslationTargetLanguageTag ?? "",
        engines: aiTranslationEngineSpecs.map((spec) => ({
            providerId: spec.providerId,
            enabled: !!flat[spec.enabledPath],
            apiKey:
                spec.providerId === "google"
                    ? ""
                    : ((flat[spec.credentialPaths[0]] as string) ?? ""),
            // Google's credentials belong only on the google engine record. Sending them on the
            // deepl/alpha2 records too made the backend see those engines' credentials "change"
            // whenever Google's were edited, wiping their validation (and silently dropping them
            // from the active engines) -- and stored Google's private key in all three records.
            serviceAccountEmail:
                spec.providerId === "google"
                    ? (flat.aiTranslationGoogleServiceAccountEmail ?? "")
                    : "",
            privateKey:
                spec.providerId === "google"
                    ? (flat.aiTranslationGooglePrivateKey ?? "")
                    : "",
            validation: { succeeded: false, message: "", upToDate: false },
        })),
    };
}

// Pulls the flat AI-translation keys out of a larger settings object (e.g. IAdvancedSettings).
export function extractAiTranslationFlatSettings(
    source: Record<string, unknown>,
): IAiTranslationSettings {
    const flat: Record<string, unknown> = {};
    aiTranslationFlatSettingsKeys.forEach((key) => {
        flat[key] = source[key];
    });
    return flat as IAiTranslationSettings;
}

// The inverse of extractAiTranslationFlatSettings: returns a shallow copy of source with the
// flat AI-translation keys removed, ready to have a nested "aiTranslation" key added back in.
export function omitAiTranslationFlatSettings<
    T extends Record<string, unknown>,
>(source: T): T {
    const clone: Record<string, unknown> = { ...source };
    aiTranslationFlatSettingsKeys.forEach((key) => {
        delete clone[key];
    });
    return clone as T;
}

function isEngineEnabled(
    settings: IAiTranslationSettings | undefined,
    spec: IEngineFieldSpec,
): boolean {
    return !!settings?.[spec.enabledPath];
}

function hasEngineCredentials(
    settings: IAiTranslationSettings | undefined,
    spec: IEngineFieldSpec,
): boolean {
    return spec.credentialPaths.every(
        (path) => !!(settings?.[path] as string | undefined)?.trim(),
    );
}

function isEngineReady(
    settings: IAiTranslationSettings | undefined,
    spec: IEngineFieldSpec,
): boolean {
    return (
        isEngineEnabled(settings, spec) && hasEngineCredentials(settings, spec)
    );
}

function getReadyProviderIds(
    settings: IAiTranslationSettings | undefined,
): AiTranslationProviderId[] {
    return aiTranslationEngineSpecs
        .filter((spec) => isEngineReady(settings, spec))
        .map((spec) => spec.providerId);
}

function getEngineProbeKey(
    settings: IAiTranslationSettings | undefined,
    spec: IEngineFieldSpec,
): string {
    return JSON.stringify({
        enabled: isEngineEnabled(settings, spec),
        targetLanguageTag: settings?.aiTranslationTargetLanguageTag ?? "",
        credentials: spec.credentialPaths.map((path) => settings?.[path] ?? ""),
    });
}

// If option is supported by only some (not all, not none) of the ready (enabled+credentialed)
// engines, returns a comma-joined display-name note (e.g. "DeepL") to show next to that option
// in the target-language dropdown. Returns "" when no note is needed.
export function getLanguageSupportNote(
    option: ITargetLanguageOption,
    readyProviderIds: AiTranslationProviderId[],
    engineDisplayNames: Record<AiTranslationProviderId, string>,
): string {
    const supportingReadyProviders = option.providerIds.filter((id) =>
        readyProviderIds.includes(id),
    );
    if (
        supportingReadyProviders.length === 0 ||
        supportingReadyProviders.length === readyProviderIds.length
    ) {
        return "";
    }
    return supportingReadyProviders
        .map((id) => engineDisplayNames[id])
        .join(", ");
}

function getSupportedLanguagesConfigKey(
    settings: IAiTranslationSettings | undefined,
): string {
    return JSON.stringify(
        aiTranslationEngineSpecs.map((spec) => ({
            providerId: spec.providerId,
            ready: isEngineReady(settings, spec),
            credentials: spec.credentialPaths.map(
                (path) => settings?.[path] ?? "",
            ),
        })),
    );
}

// Debounces live validation for a single engine: whenever its enabled state, credentials, or the
// shared target language change (and it has everything it needs), waits ~600ms and then posts
// settings/validateAiTranslationEngine for just that engine.
function useAiTranslationEngineValidation(
    spec: IEngineFieldSpec,
    settings: IAiTranslationSettings | undefined,
    initialValidation: IAiTranslationEngineValidation | undefined,
): {
    validation: IAiTranslationEngineValidation | undefined;
    isPending: boolean;
} {
    const [validation, setValidation] = React.useState<
        IAiTranslationEngineValidation | undefined
    >(initialValidation?.upToDate ? initialValidation : undefined);
    const [isPending, setIsPending] = React.useState(false);
    const lastProbeKeyRef = React.useRef<string>("");
    const latestSettingsRef = React.useRef(settings);
    latestSettingsRef.current = settings;

    React.useEffect(() => {
        setValidation(
            initialValidation?.upToDate ? initialValidation : undefined,
        );
        const loadedProbeKey = getEngineProbeKey(
            latestSettingsRef.current,
            spec,
        );
        if (initialValidation?.upToDate && initialValidation.message) {
            lastProbeKeyRef.current = loadedProbeKey;
        } else {
            lastProbeKeyRef.current = "";
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [initialValidation]);

    React.useEffect(() => {
        if (!isEngineEnabled(settings, spec)) {
            setIsPending(false);
            return;
        }

        const probeKey = getEngineProbeKey(settings, spec);
        if (probeKey === lastProbeKeyRef.current) {
            return;
        }

        lastProbeKeyRef.current = probeKey;

        if (
            !hasEngineCredentials(settings, spec) ||
            !settings?.aiTranslationTargetLanguageTag?.trim()
        ) {
            setIsPending(false);
            setValidation(undefined);
            return;
        }

        setIsPending(true);
        setValidation(undefined);

        let cancelled = false;
        const timeoutId = window.setTimeout(() => {
            void (async () => {
                try {
                    const response = await postJsonAsync(
                        "settings/validateAiTranslationEngine",
                        { providerId: spec.providerId },
                    );
                    if (cancelled) {
                        return;
                    }
                    setValidation(
                        parseAiTranslationEngineValidation(response?.data),
                    );
                } finally {
                    if (!cancelled) {
                        setIsPending(false);
                    }
                }
            })();
        }, 600);

        return () => {
            cancelled = true;
            window.clearTimeout(timeoutId);
        };
    }, [settings, spec]);

    return { validation, isPending };
}

function getEngineValidationDisplay(
    isPending: boolean,
    validation: IAiTranslationEngineValidation | undefined,
): { text: string; color: string } {
    if (isPending) {
        return { text: "Testing translation...", color: "#555" };
    }

    if (!validation?.message) {
        return { text: "", color: "#555" };
    }

    return {
        text: validation.succeeded
            ? `"Today a reader, tomorrow a leader." --> ${validation.message}`
            : `Translation test failed: ${validation.message}`,
        color: validation.succeeded ? "#2e7d32" : "#b3261e",
    };
}

// Shared by all three engines: reads its text/color/testId from overrideValue rather than the
// usual value/onChange, since this row is purely a read-only status display.
const EngineValidationStatusControl: React.FunctionComponent<{
    value: { text: string; color: string; testId: string };
}> = (controlProps) => {
    const display = controlProps.value;
    return (
        <div
            data-testid={display.testId}
            css={css`
                min-height: 2.5em;
                display: flex;
                align-items: flex-start;
                color: ${display.color};
                white-space: pre-wrap;
                overflow-wrap: anywhere;
                word-break: break-word;
                line-height: 1.35;
                padding-top: 4px;
                padding-bottom: 4px;
                max-width: 500px;
            `}
        >
            {display.text}
        </div>
    );
};

export const useAiTranslationSettingsGroup = (props: {
    settings: IAiTranslationSettings | undefined;
    initialValidations?: Partial<
        Record<AiTranslationProviderId, IAiTranslationEngineValidation>
    >;
    groupLabel: string;
    targetLanguageLabel: string;
    deepLEnabledLabel: string;
    deepLApiKeyLabel: string;
    googleEnabledLabel: string;
    googleServiceAccountEmailLabel: string;
    googlePrivateKeyLabel: string;
    alpha2EnabledLabel: string;
    alpha2ApiKeyLabel: string;
    translationTestLabel: string;
}): React.ReactElement => {
    const deepLValidation = useAiTranslationEngineValidation(
        deepLFieldSpec,
        props.settings,
        props.initialValidations?.deepl,
    );
    const googleValidation = useAiTranslationEngineValidation(
        googleFieldSpec,
        props.settings,
        props.initialValidations?.google,
    );
    const alpha2Validation = useAiTranslationEngineValidation(
        alpha2FieldSpec,
        props.settings,
        props.initialValidations?.alpha2,
    );

    const [supportedTargetLanguages, setSupportedTargetLanguages] =
        React.useState<ITargetLanguageOption[]>([]);
    const [supportedLanguagesMessage, setSupportedLanguagesMessage] =
        React.useState<string>("");
    const [isLoadingSupportedLanguages, setIsLoadingSupportedLanguages] =
        React.useState(false);
    const [languageOptionsVersion, setLanguageOptionsVersion] =
        React.useState(0);
    const lastSupportedLanguagesConfigKeyRef = React.useRef<string>("");

    const loadSupportedLanguages = React.useCallback(async () => {
        const languageConfigKey = getSupportedLanguagesConfigKey(
            props.settings,
        );
        if (getReadyProviderIds(props.settings).length === 0) {
            setSupportedTargetLanguages([]);
            setSupportedLanguagesMessage("");
            lastSupportedLanguagesConfigKeyRef.current = languageConfigKey;
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
                "settings/aiTranslationSupportedLanguages",
                props.settings,
            );
            const data = response?.data as
                | IAiTranslationSupportedLanguagesResponse
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
        const currentLanguageConfigKey = getSupportedLanguagesConfigKey(
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

    // Provider-backed target-language lists depend on external credentials and should be ready
    // as soon as any engine's config becomes usable.
    React.useEffect(() => {
        if (getReadyProviderIds(props.settings).length === 0) {
            return;
        }

        void loadSupportedLanguages();
    }, [loadSupportedLanguages, props.settings]);

    const readyProviderIds = getReadyProviderIds(props.settings);
    const usesEngineManagedTargetLanguages = readyProviderIds.length > 0;

    const engineDisplayNames: Record<AiTranslationProviderId, string> = {
        deepl: props.deepLEnabledLabel,
        google: props.googleEnabledLabel,
        alpha2: props.alpha2EnabledLabel,
    };

    const AiTranslationTargetLanguageControl: React.FunctionComponent<{
        value: string;
        disabled?: boolean;
        onChange: (value: string) => void;
    }> = (controlProps) => {
        if (!usesEngineManagedTargetLanguages) {
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
                        "data-testid": "ai-translation-target-language-input",
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
                        providerIds: [],
                    },
                ]
              : supportedTargetLanguages;

        return (
            // Constrain the width so a long error message wraps within the dialog instead of
            // forcing it to scroll horizontally (the message is rendered below, not as the
            // TextField's helperText, so we can style it as a red, wrapping error).
            <div
                css={css`
                    width: 100%;
                    max-width: 500px;
                `}
            >
                <TextField
                    select={true}
                    fullWidth={true}
                    size="small"
                    value={currentValue}
                    disabled={controlProps.disabled}
                    onChange={(event) => {
                        controlProps.onChange(event.target.value);
                    }}
                    SelectProps={{
                        onOpen: () => {
                            void loadSupportedLanguages();
                        },
                    }}
                    inputProps={{
                        "data-testid": "ai-translation-target-language-select",
                        "data-language-options-version": languageOptionsVersion,
                    }}
                >
                    <MenuItem value={""}></MenuItem>
                    {isLoadingSupportedLanguages && (
                        <MenuItem value="" disabled={true}>
                            Loading languages...
                        </MenuItem>
                    )}
                    {knownOptions.map((option) => {
                        const note = getLanguageSupportNote(
                            option,
                            readyProviderIds,
                            engineDisplayNames,
                        );
                        return (
                            <MenuItem key={option.value} value={option.value}>
                                {option.label}
                                {note && (
                                    <span
                                        css={css`
                                            margin-left: 6px;
                                            font-size: 0.8em;
                                            opacity: 0.65;
                                        `}
                                    >
                                        ({note})
                                    </span>
                                )}
                            </MenuItem>
                        );
                    })}
                </TextField>
                {supportedLanguagesMessage && (
                    <div
                        data-testid="ai-translation-supported-languages-message"
                        css={css`
                            margin-top: 4px;
                            color: #b3261e;
                            white-space: pre-wrap;
                            overflow-wrap: anywhere;
                            word-break: break-word;
                            line-height: 1.35;
                        `}
                    >
                        {supportedLanguagesMessage}
                    </div>
                )}
            </div>
        );
    };

    return (
        <ConfigrGroup label={props.groupLabel}>
            <ConfigrBoolean
                label={props.deepLEnabledLabel}
                path="aiTranslationDeepLEnabled"
            />
            {props.settings?.aiTranslationDeepLEnabled && (
                <>
                    <ConfigrInput
                        path="aiTranslationDeepLApiKey"
                        label={props.deepLApiKeyLabel}
                        charactersWide={30}
                    />
                    <ConfigrCustomObjectInput<{
                        text: string;
                        color: string;
                        testId: string;
                    }>
                        path="aiTranslationDeepLValidationDisplay"
                        control={EngineValidationStatusControl}
                        label={props.translationTestLabel}
                        overrideValue={{
                            ...getEngineValidationDisplay(
                                deepLValidation.isPending,
                                deepLValidation.validation,
                            ),
                            testId: "ai-translation-deepl-validation-status",
                        }}
                    />
                </>
            )}
            <ConfigrBoolean
                label={props.googleEnabledLabel}
                path="aiTranslationGoogleEnabled"
            />
            {props.settings?.aiTranslationGoogleEnabled && (
                <>
                    <ConfigrInput
                        path="aiTranslationGoogleServiceAccountEmail"
                        charactersWide={30}
                        label={props.googleServiceAccountEmailLabel}
                    />
                    <ConfigrInput
                        path="aiTranslationGooglePrivateKey"
                        label={props.googlePrivateKeyLabel}
                        charactersWide={30}
                        allowNewLines={true}
                        minLinesToShow={2}
                        maxLinesToShowBeforeScrolling={6}
                    />
                    <ConfigrCustomObjectInput<{
                        text: string;
                        color: string;
                        testId: string;
                    }>
                        path="aiTranslationGoogleValidationDisplay"
                        control={EngineValidationStatusControl}
                        label={props.translationTestLabel}
                        overrideValue={{
                            ...getEngineValidationDisplay(
                                googleValidation.isPending,
                                googleValidation.validation,
                            ),
                            testId: "ai-translation-google-validation-status",
                        }}
                    />
                </>
            )}
            <ConfigrBoolean
                label={props.alpha2EnabledLabel}
                path="aiTranslationAlpha2Enabled"
            />
            {props.settings?.aiTranslationAlpha2Enabled && (
                <>
                    <ConfigrInput
                        path="aiTranslationAlpha2ApiKey"
                        label={props.alpha2ApiKeyLabel}
                        charactersWide={30}
                    />
                    <ConfigrCustomObjectInput<{
                        text: string;
                        color: string;
                        testId: string;
                    }>
                        path="aiTranslationAlpha2ValidationDisplay"
                        control={EngineValidationStatusControl}
                        label={props.translationTestLabel}
                        overrideValue={{
                            ...getEngineValidationDisplay(
                                alpha2Validation.isPending,
                                alpha2Validation.validation,
                            ),
                            testId: "ai-translation-alpha2-validation-status",
                        }}
                    />
                </>
            )}
            <ConfigrCustomObjectInput<string>
                path="aiTranslationTargetLanguageTag"
                control={AiTranslationTargetLanguageControl}
                label={props.targetLanguageLabel}
            />
        </ConfigrGroup>
    );
};
