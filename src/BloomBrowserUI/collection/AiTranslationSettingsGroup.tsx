import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrCustomObjectInput,
    ConfigrGroup,
    ConfigrInput,
} from "@sillsdev/config-r";
import { MenuItem, TextField } from "@mui/material";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { postJsonAsync } from "../utils/bloomApi";
import {
    AiTranslationProviderLogo,
    SilAlpha2WordmarkLogo,
} from "./AiTranslationProviderLogos";

// The order in which provider logos appear as columns in the target-language dropdown, and the
// order the engine sections appear in the settings group. Alpha2 leads (it is the SIL-hosted
// service this feature is built around), followed by the third-party engines.
const kProviderDisplayOrder: AiTranslationProviderId[] = [
    "alpha2",
    "deepl",
    "google",
];

export type AiTranslationProviderId = "deepl" | "google" | "alpha2";

export interface ITargetLanguageOption {
    value: string;
    label: string;
    providerIds: AiTranslationProviderId[];
}

export interface IAiTranslationEngineValidation {
    succeeded: boolean;
    // True when validation failed only because the provider doesn't support the chosen target
    // language; the engine is auto-skipped rather than treated as broken.
    targetLanguageNotSupported: boolean;
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
    aiTranslationAlpha2SourceLanguageTag?: string;
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
    "aiTranslationAlpha2SourceLanguageTag",
] as const;

export interface IAiTranslationWireEngineSettings {
    providerId: AiTranslationProviderId;
    enabled: boolean;
    apiKey: string;
    serviceAccountEmail: string;
    privateKey: string;
    sourceLanguageTag: string;
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
    // Non-credential settings (e.g. alpha2's source language) that still affect validation and the
    // supported-language lists, so they belong in the probe/language-config keys but NOT in
    // credentialPaths (changing them must not be treated as a credential change that would, say,
    // leak between engines).
    configPaths?: Array<keyof IAiTranslationSettings>;
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
    configPaths: ["aiTranslationAlpha2SourceLanguageTag"],
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
        targetLanguageNotSupported:
            readField<boolean>(
                candidate,
                "targetLanguageNotSupported",
                "TargetLanguageNotSupported",
            ) ?? false,
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
        targetLanguageNotSupported:
            readField<boolean>(
                candidate,
                "targetLanguageNotSupported",
                "TargetLanguageNotSupported",
            ) ?? false,
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

        if (spec.providerId === "alpha2") {
            flatSettings.aiTranslationAlpha2SourceLanguageTag =
                (rawEngine &&
                    readField<string>(
                        rawEngine,
                        "sourceLanguageTag",
                        "SourceLanguageTag",
                    )) ??
                "";
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
            // Only alpha2 uses a fixed source language; the other engines send it empty.
            sourceLanguageTag:
                spec.providerId === "alpha2"
                    ? (flat.aiTranslationAlpha2SourceLanguageTag ?? "")
                    : "",
            validation: {
                succeeded: false,
                targetLanguageNotSupported: false,
                message: "",
                upToDate: false,
            },
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
        // e.g. alpha2's source language: a change here should re-run validation (the pair changed).
        config: (spec.configPaths ?? []).map((path) => settings?.[path] ?? ""),
    });
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
            // e.g. alpha2's source language changes which target languages it can offer.
            config: (spec.configPaths ?? []).map(
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

    // Effect justified: synchronizes this engine's displayed validation with the initialValidation
    // prop, which arrives asynchronously from the server (an external source) after the settings
    // load. When an up-to-date validation loads we adopt it and seed the "last probed" key so we
    // don't immediately re-probe; otherwise we clear it. That is external-state synchronization,
    // which is what an Effect is for.
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

    // Effect justified: debounced live validation against the external translation service. When
    // this engine's enabled state, credentials, or the target language change, we wait ~600ms of
    // quiet and then probe the provider over the network and reflect the result, cancelling the
    // pending probe (timer) on further change or unmount. Talking to that external service with
    // proper cleanup is a legitimate use of an Effect.
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

// Fills placeholders {0}, {1}, ... in a localized template with the given values.
function formatTemplate(template: string, ...values: string[]): string {
    return values.reduce(
        (text, value, index) => text.split(`{${index}}`).join(value),
        template,
    );
}

function getEngineValidationDisplay(
    isPending: boolean,
    validation: IAiTranslationEngineValidation | undefined,
    engineDisplayName: string,
    targetLanguageLabel: string,
    targetLanguageNotSupportedTemplate: string,
): { text: string; color: string } {
    if (isPending) {
        return { text: "Testing translation...", color: "#555" };
    }

    // The engine works, it just can't do this language, so it will be auto-skipped: an amber note,
    // not a red failure.
    if (validation?.targetLanguageNotSupported) {
        return {
            text: formatTemplate(
                targetLanguageNotSupportedTemplate,
                engineDisplayName,
                targetLanguageLabel,
            ),
            color: "#b26a00",
        };
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

// The live data the module-scope language controls (target-language and alpha2 source-language)
// need, supplied via context (below) so the controls can live at module scope. Everything here is
// derived from the hook's state and refreshed each render; the controls read it through the context.
export interface IAiTranslationLanguageControlsData {
    usesEngineManagedTargetLanguages: boolean;
    supportedTargetLanguages: ITargetLanguageOption[];
    supportedLanguagesMessage: string;
    isLoadingSupportedLanguages: boolean;
    languageOptionsVersion: number;
    loadSupportedLanguages: () => Promise<void>;
    readyProviderIds: AiTranslationProviderId[];
    engineDisplayNames: Record<AiTranslationProviderId, string>;
    noServiceSupportsLanguageNote: string;
    // Shown (disabled state) when no provider is ready yet, in place of a language list.
    noProviderSelectedNote: string;
    // Explains that the Alpha2 language list is those it can translate from its source language;
    // empty unless Alpha2 is a ready provider.
    alpha2LanguagesNote: string;
    // Alpha2 source-language chooser data:
    alpha2SourceLanguages: ITargetLanguageOption[];
    alpha2SourceLanguagesMessage: string;
    isLoadingAlpha2SourceLanguages: boolean;
    alpha2SourceLanguageOptionsVersion: number;
    loadAlpha2SourceLanguages: () => Promise<void>;
}

// Provided by AdvancedSettingsPanel (wrapping the Configr pane) so the module-scope language
// controls can reach the hook's live data without being redefined on every render.
export const AiTranslationLanguageControlsContext = React.createContext<
    IAiTranslationLanguageControlsData | undefined
>(undefined);

// The target-language chooser passed to ConfigrCustomObjectInput's `control` prop. It MUST live at
// module scope (not inside useAiTranslationSettingsGroup): a component defined inside the hook gets
// a new identity on every render, which makes React unmount/remount it -- closing the dropdown and
// losing focus whenever any other settings field changes. It gets its selection via the usual
// value/onChange, and everything else (options, load callback, etc.) from context.
// The target-language picker is kept narrow (250px), so long language names are clipped to a
// fixed number of characters with an ellipsis; the full name and code are shown in a tooltip.
const kLanguagePickerWidthPx = 250;
const kMaxLanguageLabelChars = 16;

function clipLanguageLabel(label: string): string {
    return label.length > kMaxLanguageLabelChars
        ? label.slice(0, kMaxLanguageLabelChars).trimEnd() + "…"
        : label;
}

// The tooltip shown on a language option / the chosen value: full name plus its language code.
function getLanguageTooltip(option: ITargetLanguageOption): string {
    return `${option.label} (${option.value})`;
}

const AiTranslationTargetLanguageControl: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
}> = (controlProps) => {
    const data = React.useContext(AiTranslationLanguageControlsContext)!;

    if (!data.usesEngineManagedTargetLanguages) {
        // No provider is ready yet, so we have no language list to offer. Rather than let the user
        // type a free-form tag (which no enabled service could act on), disable the field and
        // explain what to do to populate it.
        return (
            <div
                css={css`
                    width: 100%;
                    max-width: 500px;
                `}
            >
                <TextField
                    fullWidth={true}
                    size="small"
                    value={controlProps.value || ""}
                    disabled={true}
                    css={css`
                        max-width: ${kLanguagePickerWidthPx}px;
                        margin-left: auto;
                        display: block;
                    `}
                    inputProps={{
                        "data-testid": "ai-translation-target-language-input",
                    }}
                />
                <div
                    data-testid="ai-translation-no-provider-note"
                    css={css`
                        margin-top: 4px;
                        color: #555;
                        font-size: 0.85em;
                        line-height: 1.35;
                    `}
                >
                    {data.noProviderSelectedNote}
                </div>
            </div>
        );
    }

    // Which ready providers actually contribute a language to the current list. We reserve a logo
    // column only for these, so the columns take exactly the width the selected-and-relevant
    // providers need -- not a slot for every provider that merely could exist.
    const providersWithSupportedLanguages = new Set<AiTranslationProviderId>();
    data.supportedTargetLanguages.forEach((option) =>
        option.providerIds.forEach((id) =>
            providersWithSupportedLanguages.add(id),
        ),
    );

    // The relevant ready providers, in a stable order, become the logo columns each language row
    // lines up against (a per-provider slot, filled when that provider supports the language). Only
    // shown when 2+ such providers exist -- with one there is nothing to distinguish.
    const logoColumnProviderIds = kProviderDisplayOrder.filter(
        (id) =>
            data.readyProviderIds.includes(id) &&
            providersWithSupportedLanguages.has(id),
    );
    const showLogoColumns = logoColumnProviderIds.length >= 2;

    const currentValue = controlProps.value || "";
    const knownOptions = data.supportedTargetLanguages.some(
        (option) => option.value === currentValue,
    )
        ? data.supportedTargetLanguages
        : currentValue
          ? [
                ...data.supportedTargetLanguages,
                {
                    value: currentValue,
                    label: currentValue,
                    providerIds: [],
                },
            ]
          : data.supportedTargetLanguages;

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
                css={css`
                    max-width: ${kLanguagePickerWidthPx}px;
                    margin-left: auto;
                    display: block;
                `}
                onChange={(event) => {
                    controlProps.onChange(event.target.value);
                }}
                SelectProps={{
                    onOpen: () => {
                        void data.loadSupportedLanguages();
                    },
                }}
                inputProps={{
                    "data-testid": "ai-translation-target-language-select",
                    "data-language-options-version":
                        data.languageOptionsVersion,
                }}
            >
                <MenuItem value={""}></MenuItem>
                {data.isLoadingSupportedLanguages && (
                    <MenuItem value="" disabled={true}>
                        Loading languages...
                    </MenuItem>
                )}
                {knownOptions.map((option) => {
                    // The synthetic current-value option (no supporting providers) is the only one
                    // that shows the "no enabled service supports this language" text note.
                    const noServiceNote =
                        option.providerIds.length === 0
                            ? data.noServiceSupportsLanguageNote
                            : "";
                    return (
                        <MenuItem
                            key={option.value}
                            value={option.value}
                            css={css`
                                display: flex;
                                align-items: center;
                            `}
                        >
                            <span
                                title={getLanguageTooltip(option)}
                                css={css`
                                    flex: 1;
                                    white-space: nowrap;
                                `}
                            >
                                {clipLanguageLabel(option.label)}
                            </span>
                            {showLogoColumns && (
                                <span
                                    css={css`
                                        display: inline-flex;
                                        align-items: center;
                                        margin-left: 8px;
                                    `}
                                >
                                    {/* One fixed-width slot per ready provider so the logos line
                                        up in columns down the list; the slot is filled only when
                                        this language is supported by that provider. */}
                                    {logoColumnProviderIds.map((id) => (
                                        <span
                                            key={id}
                                            css={css`
                                                width: 28px;
                                                display: inline-flex;
                                                justify-content: center;
                                                align-items: center;
                                            `}
                                        >
                                            {option.providerIds.includes(
                                                id,
                                            ) && (
                                                <AiTranslationProviderLogo
                                                    providerId={id}
                                                    title={
                                                        data.engineDisplayNames[
                                                            id
                                                        ]
                                                    }
                                                />
                                            )}
                                        </span>
                                    ))}
                                </span>
                            )}
                            {noServiceNote && (
                                <span
                                    css={css`
                                        margin-left: 6px;
                                        font-size: 0.8em;
                                        opacity: 0.65;
                                    `}
                                >
                                    ({noServiceNote})
                                </span>
                            )}
                        </MenuItem>
                    );
                })}
            </TextField>
            {data.supportedLanguagesMessage && (
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
                    {data.supportedLanguagesMessage}
                </div>
            )}
        </div>
    );
};

// The alpha2 source-language chooser passed to ConfigrCustomObjectInput's `control` prop. Like
// AiTranslationTargetLanguageControl, it MUST live at module scope so React doesn't unmount/remount
// it on every render (which would close the dropdown and lose focus). It reads its options and load
// callback from the shared context and its selection via the usual value/onChange. When the endpoint
// returns no options (no target chosen yet, or alpha2 not configured) it falls back to a free-text
// field, defaulting to "en".
const AiTranslationAlpha2SourceLanguageControl: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
}> = (controlProps) => {
    const data = React.useContext(AiTranslationLanguageControlsContext)!;
    const currentValue = controlProps.value || "en";

    if (data.alpha2SourceLanguages.length === 0) {
        return (
            <TextField
                fullWidth={true}
                size="small"
                value={currentValue}
                disabled={controlProps.disabled}
                css={css`
                    max-width: ${kLanguagePickerWidthPx}px;
                    margin-left: auto;
                    display: block;
                `}
                onChange={(event) => {
                    controlProps.onChange(event.target.value);
                }}
                inputProps={{
                    "data-testid":
                        "ai-translation-alpha2-source-language-input",
                }}
            />
        );
    }

    const knownOptions = data.alpha2SourceLanguages.some(
        (option) => option.value === currentValue,
    )
        ? data.alpha2SourceLanguages
        : [
              ...data.alpha2SourceLanguages,
              { value: currentValue, label: currentValue, providerIds: [] },
          ];

    return (
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
                css={css`
                    max-width: ${kLanguagePickerWidthPx}px;
                    margin-left: auto;
                    display: block;
                `}
                onChange={(event) => {
                    controlProps.onChange(event.target.value);
                }}
                SelectProps={{
                    onOpen: () => {
                        void data.loadAlpha2SourceLanguages();
                    },
                }}
                inputProps={{
                    "data-testid":
                        "ai-translation-alpha2-source-language-select",
                    "data-language-options-version":
                        data.alpha2SourceLanguageOptionsVersion,
                }}
            >
                {data.isLoadingAlpha2SourceLanguages && (
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
            {data.alpha2SourceLanguagesMessage && (
                <div
                    data-testid="ai-translation-alpha2-source-languages-message"
                    css={css`
                        margin-top: 4px;
                        color: #b3261e;
                        white-space: pre-wrap;
                        overflow-wrap: anywhere;
                        word-break: break-word;
                        line-height: 1.35;
                    `}
                >
                    {data.alpha2SourceLanguagesMessage}
                </div>
            )}
            {/* Alpha2's available target languages depend on this source language, so the
                explanation lives right under the source picker. */}
            {data.alpha2LanguagesNote && (
                <div
                    data-testid="ai-translation-alpha2-languages-note"
                    css={css`
                        margin-top: 4px;
                        display: flex;
                        align-items: flex-start;
                        gap: 4px;
                        color: #555;
                        font-size: 0.85em;
                        line-height: 1.35;
                    `}
                >
                    <InfoOutlinedIcon
                        css={css`
                            font-size: 1.1em;
                            flex-shrink: 0;
                            margin-top: 1px;
                        `}
                    />
                    <span>{data.alpha2LanguagesNote}</span>
                </div>
            )}
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
    deepLApiKeyDescription: string;
    googleEnabledLabel: string;
    googleServiceAccountEmailLabel: string;
    googleServiceAccountDescription: string;
    googlePrivateKeyLabel: string;
    alpha2EnabledLabel: string;
    alpha2ApiKeyLabel: string;
    alpha2SourceLanguageLabel: string;
    translationTestLabel: string;
    targetLanguageNotSupportedTemplate: string;
    noServiceSupportsLanguageNote: string;
    noProviderSelectedNote: string;
    // Template with a single {0} placeholder for the Alpha2 source-language name.
    alpha2LanguagesNoteTemplate: string;
}): {
    group: React.ReactElement;
    languageControlsData: IAiTranslationLanguageControlsData;
} => {
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
    // The live "Translation Test" is intentionally NOT run for Alpha2 (its display row is hidden
    // below). Unlike DeepL/Google, an Alpha2 validation isn't a cheap single call: it has to spin
    // up a whole translation system (create a text collection, kick off and poll a translation
    // job, then tear it down), which is far too expensive to fire on every credential/target
    // change. So we skip the alpha2 validation hook entirely for now.

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

    // Effect justified: keeps the cached supported-languages list in sync with the engine
    // configuration. The list is fetched from the providers (external services) and keyed to a
    // specific credential/target configuration; when that configuration changes, the cached list
    // and message no longer apply, so we clear them (the refetch is triggered by the effect below).
    // This synchronizes cached external data with its inputs, which warrants an Effect.
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

    // --- Alpha2 source-language list (depends on the chosen target language) ---
    const [alpha2SourceLanguages, setAlpha2SourceLanguages] = React.useState<
        ITargetLanguageOption[]
    >([]);
    const [alpha2SourceLanguagesMessage, setAlpha2SourceLanguagesMessage] =
        React.useState<string>("");
    const [isLoadingAlpha2SourceLanguages, setIsLoadingAlpha2SourceLanguages] =
        React.useState(false);
    const [
        alpha2SourceLanguageOptionsVersion,
        setAlpha2SourceLanguageOptionsVersion,
    ] = React.useState(0);
    const lastAlpha2SourceLanguagesConfigKeyRef = React.useRef<string>("");

    // The alpha2 source list depends on the chosen target and alpha2's own credentials. This key
    // captures those inputs so we refetch only when they change.
    const alpha2SourceLanguagesConfigKey = JSON.stringify({
        enabled: isEngineEnabled(props.settings, alpha2FieldSpec),
        targetLanguageTag: props.settings?.aiTranslationTargetLanguageTag ?? "",
        apiKey: props.settings?.aiTranslationAlpha2ApiKey ?? "",
    });

    const loadAlpha2SourceLanguages = React.useCallback(async () => {
        const configKey = JSON.stringify({
            enabled: isEngineEnabled(props.settings, alpha2FieldSpec),
            targetLanguageTag:
                props.settings?.aiTranslationTargetLanguageTag ?? "",
            apiKey: props.settings?.aiTranslationAlpha2ApiKey ?? "",
        });
        if (
            !isEngineEnabled(props.settings, alpha2FieldSpec) ||
            !props.settings?.aiTranslationAlpha2ApiKey?.trim() ||
            !props.settings?.aiTranslationTargetLanguageTag?.trim()
        ) {
            setAlpha2SourceLanguages([]);
            setAlpha2SourceLanguagesMessage("");
            lastAlpha2SourceLanguagesConfigKeyRef.current = configKey;
            return;
        }

        if (
            configKey === lastAlpha2SourceLanguagesConfigKeyRef.current &&
            alpha2SourceLanguages.length > 0
        ) {
            return;
        }

        setIsLoadingAlpha2SourceLanguages(true);
        setAlpha2SourceLanguagesMessage("");
        try {
            const response = await postJsonAsync(
                "settings/aiTranslationAlpha2SourceLanguages",
                props.settings,
            );
            const data = response?.data as
                | IAiTranslationSupportedLanguagesResponse
                | undefined;
            const languages = parseSupportedTargetLanguageOptions(data);
            setAlpha2SourceLanguages(languages);
            setAlpha2SourceLanguagesMessage(data?.message ?? "");
            lastAlpha2SourceLanguagesConfigKeyRef.current = configKey;
            setAlpha2SourceLanguageOptionsVersion((value) => value + 1);
        } finally {
            setIsLoadingAlpha2SourceLanguages(false);
        }
    }, [props.settings, alpha2SourceLanguages.length]);

    // Effect justified: keeps the cached alpha2 source-language list in sync with its inputs (the
    // target language and alpha2's credentials), which come from an external service. When those
    // change the cached list no longer applies, so clear it; the refetch is triggered below. This
    // is external-data synchronization, which warrants an Effect.
    React.useEffect(() => {
        if (
            alpha2SourceLanguagesConfigKey !==
            lastAlpha2SourceLanguagesConfigKeyRef.current
        ) {
            setAlpha2SourceLanguages([]);
            setAlpha2SourceLanguagesMessage("");
        }
    }, [alpha2SourceLanguagesConfigKey]);

    // Fetch the alpha2 source list as soon as alpha2 is configured and a target is chosen.
    React.useEffect(() => {
        if (
            !isEngineEnabled(props.settings, alpha2FieldSpec) ||
            !props.settings?.aiTranslationAlpha2ApiKey?.trim() ||
            !props.settings?.aiTranslationTargetLanguageTag?.trim()
        ) {
            return;
        }
        void loadAlpha2SourceLanguages();
    }, [loadAlpha2SourceLanguages, props.settings]);

    const readyProviderIds = getReadyProviderIds(props.settings);
    const usesEngineManagedTargetLanguages = readyProviderIds.length > 0;

    const engineDisplayNames: Record<AiTranslationProviderId, string> = {
        deepl: props.deepLEnabledLabel,
        google: props.googleEnabledLabel,
        alpha2: props.alpha2EnabledLabel,
    };

    // The human-readable label of the currently-chosen target language, used in the per-engine
    // "does not support ⟨language⟩" note; falls back to the tag if we have no matching option.
    const currentTargetTag =
        props.settings?.aiTranslationTargetLanguageTag ?? "";
    const currentTargetLabel =
        supportedTargetLanguages.find(
            (option) => option.value === currentTargetTag,
        )?.label ?? currentTargetTag;

    // Alpha2's target languages are those it can translate FROM its configured source language
    // (default English), so when Alpha2 is a ready provider we explain that against the list. Use
    // the source language's human label when we have it, else its tag.
    const alpha2SourceTag =
        props.settings?.aiTranslationAlpha2SourceLanguageTag || "en";
    const alpha2SourceLabel =
        alpha2SourceLanguages.find((option) => option.value === alpha2SourceTag)
            ?.label ?? alpha2SourceTag;
    const alpha2LanguagesNote = readyProviderIds.includes("alpha2")
        ? formatTemplate(props.alpha2LanguagesNoteTemplate, alpha2SourceLabel)
        : "";

    // Bundle the controls' live inputs for the context. The controls themselves are stable
    // module-scope components; this data is what changes over time.
    const languageControlsData: IAiTranslationLanguageControlsData = {
        usesEngineManagedTargetLanguages,
        supportedTargetLanguages,
        supportedLanguagesMessage,
        isLoadingSupportedLanguages,
        languageOptionsVersion,
        loadSupportedLanguages,
        readyProviderIds,
        engineDisplayNames,
        noServiceSupportsLanguageNote: props.noServiceSupportsLanguageNote,
        noProviderSelectedNote: props.noProviderSelectedNote,
        alpha2LanguagesNote,
        alpha2SourceLanguages,
        alpha2SourceLanguagesMessage,
        isLoadingAlpha2SourceLanguages,
        alpha2SourceLanguageOptionsVersion,
        loadAlpha2SourceLanguages,
    };

    // Each engine's enable toggle is branded with its logo before the label. ConfigrBoolean types
    // `label` as string, but config-r renders it straight into MUI's ListItemText `primary`, which
    // accepts any node -- so the cast is safe here and lets us prepend the logo.
    const brandedLabel = (logo: React.ReactNode, text: string) =>
        (
            <span
                css={css`
                    display: inline-flex;
                    align-items: center;
                    gap: 8px;
                    font-weight: 700;
                `}
            >
                {logo}
                {text}
            </span>
        ) as unknown as string;

    const alpha2EnabledLabelWithLogo = brandedLabel(
        <SilAlpha2WordmarkLogo />,
        props.alpha2EnabledLabel,
    );
    const deepLEnabledLabelWithLogo = brandedLabel(
        <AiTranslationProviderLogo
            providerId="deepl"
            title={props.deepLEnabledLabel}
        />,
        props.deepLEnabledLabel,
    );
    const googleEnabledLabelWithLogo = brandedLabel(
        <AiTranslationProviderLogo
            providerId="google"
            title={props.googleEnabledLabel}
        />,
        props.googleEnabledLabel,
    );

    // Each engine's enable toggle and its (conditional) settings are wrapped in a single fragment
    // so config-r treats the whole engine as one group child: it draws a separator line only
    // BETWEEN engines, not between an engine's checkbox and its own settings.
    const group = (
        <ConfigrGroup label={props.groupLabel}>
            {/* Target language is the first thing to choose, so it leads the group. */}
            <ConfigrCustomObjectInput<string>
                path="aiTranslationTargetLanguageTag"
                control={AiTranslationTargetLanguageControl}
                label={props.targetLanguageLabel}
            />
            {/* Alpha2 (SIL's own service) leads the engine list. */}
            <>
                <ConfigrBoolean
                    label={alpha2EnabledLabelWithLogo}
                    path="aiTranslationAlpha2Enabled"
                />
                {props.settings?.aiTranslationAlpha2Enabled && (
                    <>
                        <ConfigrInput
                            path="aiTranslationAlpha2ApiKey"
                            label={props.alpha2ApiKeyLabel}
                            charactersWide={39}
                        />
                        <ConfigrCustomObjectInput<string>
                            path="aiTranslationAlpha2SourceLanguageTag"
                            control={AiTranslationAlpha2SourceLanguageControl}
                            label={props.alpha2SourceLanguageLabel}
                        />
                        {/* The "Translation Test" row is intentionally omitted for Alpha2: running
                            a live test means spinning up a whole Alpha2 translation system (create
                            a text collection, start and poll a translation job, then tear it down),
                            which is far too expensive to run on every settings change. Disabled and
                            hidden for now; the deepl/google tests remain since those are cheap. */}
                    </>
                )}
            </>
            <>
                <ConfigrBoolean
                    label={deepLEnabledLabelWithLogo}
                    path="aiTranslationDeepLEnabled"
                />
                {props.settings?.aiTranslationDeepLEnabled && (
                    <>
                        <ConfigrInput
                            path="aiTranslationDeepLApiKey"
                            label={props.deepLApiKeyLabel}
                            description={props.deepLApiKeyDescription}
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
                                    props.deepLEnabledLabel,
                                    currentTargetLabel,
                                    props.targetLanguageNotSupportedTemplate,
                                ),
                                testId: "ai-translation-deepl-validation-status",
                            }}
                        />
                    </>
                )}
            </>
            <>
                <ConfigrBoolean
                    label={googleEnabledLabelWithLogo}
                    path="aiTranslationGoogleEnabled"
                />
                {props.settings?.aiTranslationGoogleEnabled && (
                    <>
                        <ConfigrInput
                            path="aiTranslationGoogleServiceAccountEmail"
                            charactersWide={30}
                            label={props.googleServiceAccountEmailLabel}
                            description={props.googleServiceAccountDescription}
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
                                    props.googleEnabledLabel,
                                    currentTargetLabel,
                                    props.targetLanguageNotSupportedTemplate,
                                ),
                                testId: "ai-translation-google-validation-status",
                            }}
                        />
                    </>
                )}
            </>
        </ConfigrGroup>
    );

    return { group, languageControlsData };
};
