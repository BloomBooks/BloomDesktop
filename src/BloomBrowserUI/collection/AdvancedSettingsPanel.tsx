import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
    ConfigrPane,
} from "@sillsdev/config-r";
import { get, postJson } from "../utils/bloomApi";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { tabMargins } from "./commonTabSettings";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { useGetFeatureStatus } from "../react_components/featureStatus";
import { BloomSubscriptionIndicatorIconAndText } from "../react_components/requiresSubscription";
import { useL10n } from "../react_components/l10nHooks";
import {
    AiTranslationProviderId,
    AiTranslationLanguageControlsContext,
    IAiTranslationEngineValidation,
    IAiTranslationSettings,
    IAiTranslationWireSettings,
    buildAiTranslationWirePayload,
    extractAiTranslationFlatSettings,
    flattenAiTranslationWireSettings,
    omitAiTranslationFlatSettings,
    useAiTranslationSettingsGroup,
} from "./AiTranslationSettingsGroup";

// The shape actually sent/received over the wire for settings/advancedProgramSettings: the
// flat, non-AI settings plus a nested "aiTranslation" object matching the backend contract.
interface IAdvancedSettingsWire {
    autoUpdate?: boolean;
    showExperimentalBookSources?: boolean;
    allowTeamCollection?: boolean;
    allowAppBuilder?: boolean;
    allowAiSourceBubbles?: boolean;
    showQrCode?: boolean;
    qrcodeCaption?: string;
    aiTranslation?: IAiTranslationWireSettings;
}

interface IAdvancedSettingsApiData {
    values: IAdvancedSettingsWire;
    showAutoUpdate?: boolean;
    showExperimentalBookSourcesOption?: boolean;
    allowTeamCollectionEnabled?: boolean;
}

// The flattened shape Configr actually edits: non-AI settings plus one boolean+credential set
// per AI engine (see AiTranslationSettingsGroup for why it's flattened rather than nested).
interface IAdvancedSettings extends IAiTranslationSettings {
    autoUpdate?: boolean;
    showExperimentalBookSources?: boolean;
    allowTeamCollection?: boolean;
    allowAppBuilder?: boolean;
    allowAiSourceBubbles?: boolean;
    showQrCode?: boolean;
    qrcodeCaption?: string;
}

export const AdvancedSettingsPanel: React.FunctionComponent = () => {
    const [settings, setSettings] = React.useState<
        IAdvancedSettings | undefined
    >(undefined);
    const [showAutoUpdate, setShowAutoUpdate] = React.useState<boolean>(false);
    const [allowTeamCollectionEnabled, setAllowTeamCollectionEnabled] =
        React.useState<boolean>(false);
    const [
        showExperimentalBookSourcesOption,
        setShowExperimentalBookSourcesOption,
    ] = React.useState<boolean>(false);
    const [
        aiTranslationInitialValidations,
        setAiTranslationInitialValidations,
    ] =
        React.useState<
            Partial<
                Record<AiTranslationProviderId, IAiTranslationEngineValidation>
            >
        >();

    const advancedProgramSettingsLabel = useL10n(
        "Advanced Program Settings",
        "CollectionSettingsDialog.AdvancedTab.AdvancedProgramSettingsTabLabel",
    );
    const programLabel = useL10n(
        "Program",
        "CollectionSettingsDialog.AdvancedTab.Program",
    );
    const automaticallyUpdateBloomLabel = useL10n(
        "Automatically Update Bloom",
        "CollectionSettingsDialog.AdvancedTab.AutoUpdate",
    );
    const experimentalFeaturesLabel = useL10n(
        "Experimental Features",
        "CollectionSettingsDialog.AdvancedTab.Experimental.ExperimentalFeatures",
    );
    const showExperimentalBookSourcesLabel = useL10n(
        "Show Experimental Book Sources",
        "CollectionSettingsDialog.AdvancedTab.Experimental.ShowExperimentalBookSources",
    );
    const teamCollectionsLabel = useL10n(
        "Team Collections",
        "TeamCollection.TeamCollections",
    );
    const appBuilderLabel = useL10n(
        "App Builder",
        "CollectionSettingsDialog.AdvancedTab.Experimental.AppBuilder",
    );
    const aiSourceBubblesLabel = useL10n(
        "AI Source Translation",
        "CollectionSettingsDialog.AdvancedTab.Experimental.AiSourceBubbles",
    );
    const aiTranslationSectionLabel = useL10n(
        "AI Source Translation",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.SectionLabel",
    );
    const aiTranslationTargetLanguageLabel = useL10n(
        "Target Language",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.TargetLanguageLabel",
    );
    const aiTranslationDeepLEnabledLabel = useL10n(
        "DeepL",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.DeepLEnabledLabel",
    );
    const aiTranslationDeepLApiKeyLabel = useL10n(
        "API Key",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.DeepLApiKeyLabel",
    );
    const aiTranslationDeepLApiKeyDescription = useL10n(
        "The key needs the 'translate:text' and 'languages:read' permissions.",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.DeepLApiKeyDescription",
    );
    const aiTranslationGoogleEnabledLabel = useL10n(
        "Google Translate",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.GoogleEnabledLabel",
    );
    const aiTranslationGoogleServiceAccountEmailLabel = useL10n(
        "Google Service Account Email",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.GoogleServiceAccountEmailLabel",
    );
    const aiTranslationGoogleServiceAccountDescription = useL10n(
        "The service account needs access to the Cloud Translation API (the 'Cloud Translation API User' role).",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.GoogleServiceAccountDescription",
    );
    const aiTranslationGooglePrivateKeyLabel = useL10n(
        "Google Service Account Private Key",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.GooglePrivateKeyLabel",
    );
    const aiTranslationAlpha2EnabledLabel = useL10n(
        "SIL Alpha2",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.Alpha2EnabledLabel",
    );
    const aiTranslationAlpha2ApiKeyLabel = useL10n(
        "API Key",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.Alpha2ApiKeyLabel",
    );
    const aiTranslationAlpha2SourceLanguageLabel = useL10n(
        "Source Language",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.Alpha2SourceLanguageLabel",
    );
    const aiTranslationTranslationTestLabel = useL10n(
        "Translation Test",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.TranslationTestLabel",
    );
    const aiTranslationTargetLanguageNotSupportedTemplate = useL10n(
        "{0} does not support translating to {1}, so it will be skipped.",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.TargetLanguageNotSupported",
    );
    const aiTranslationNoServiceSupportsLanguageNote = useL10n(
        "no enabled service supports this",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.NoServiceSupportsLanguage",
    );
    const aiTranslationNoProviderSelectedNote = useL10n(
        "Select at least one translation provider to get a list of languages it supports.",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.NoProviderSelected",
    );
    const aiTranslationAlpha2LanguagesNoteTemplate = useL10n(
        "SIL Alpha2 shows the languages it can translate from {0}.",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.Alpha2LanguagesNote",
    );
    const qrCodesLabel = useL10n(
        "QR Codes",
        "CollectionSettingsDialog.AdvancedTab.QrCodes",
    );
    const showQrCodesLabel = useL10n(
        "Show QR Codes",
        "CollectionSettingsDialog.AdvancedTab.ShowQrCodes",
    );
    const showQrCodesDescription = useL10n(
        "Scanning the code or clicking the badge will take the user to the collection of books in this collection's primary language. Note, if you want to use this but your branding does not show it, contact us.",
        "CollectionSettingsDialog.AdvancedTab.ShowQrCodes.Description",
    );
    const captionLabel = useL10n(
        "Caption",
        "CollectionSettingsDialog.AdvancedTab.Caption",
    );
    const captionDescription = useL10n(
        'If your caption contains "{0}", Bloom will fill this in with the name of the language.',
        "CollectionSettingsDialog.AdvancedTab.Caption.Description",
    );

    const featureStatus = useGetFeatureStatus("TeamCollection");
    const teamCollectionOptionEnabled =
        featureStatus === undefined ? true : featureStatus.enabled;
    const appBuilderFeatureStatus = useGetFeatureStatus("AppBuilder");
    const appBuilderOptionEnabled =
        appBuilderFeatureStatus === undefined
            ? false
            : appBuilderFeatureStatus.enabled;
    const aiSourceBubblesFeatureStatus = useGetFeatureStatus("AiSourceBubbles");
    const aiSourceBubblesOptionEnabled =
        aiSourceBubblesFeatureStatus === undefined
            ? false
            : aiSourceBubblesFeatureStatus.enabled;
    const canChangeTeamCollectionOption = allowTeamCollectionEnabled !== false;

    const normalizeConfigrSettings = React.useCallback(
        (
            settingsValue: string | IAdvancedSettings | undefined,
        ): IAdvancedSettings | undefined => {
            if (!settingsValue) {
                return undefined;
            }
            if (typeof settingsValue === "string") {
                return JSON.parse(settingsValue) as IAdvancedSettings;
            }
            return settingsValue;
        },
        [],
    );

    React.useEffect(() => {
        get("settings/advancedProgramSettings", (result) => {
            if (!result || !result.data || result.data === "{}") {
                return;
            }
            let data = result.data;
            if (typeof result.data === "string") {
                data = JSON.parse(result.data);
            }
            const advancedSettingsData = data as IAdvancedSettingsApiData;
            const { aiTranslation, ...restOfWireValues } =
                advancedSettingsData.values;
            const { flatSettings, initialValidations } =
                flattenAiTranslationWireSettings(aiTranslation);
            setSettings({
                ...restOfWireValues,
                ...flatSettings,
            } as IAdvancedSettings);
            setShowAutoUpdate(advancedSettingsData.showAutoUpdate ?? false);
            setAllowTeamCollectionEnabled(
                advancedSettingsData.allowTeamCollectionEnabled ?? false,
            );
            setShowExperimentalBookSourcesOption(
                advancedSettingsData.showExperimentalBookSourcesOption ?? false,
            );
            setAiTranslationInitialValidations(initialValidations);
        });
    }, []);

    // Memoized so the object reference is stable across renders that don't actually change
    // the AI settings; the group's validation/language-fetch effects key off this reference.
    const aiTranslationFlatSettings = React.useMemo(
        () =>
            settings
                ? extractAiTranslationFlatSettings(
                      settings as unknown as Record<string, unknown>,
                  )
                : undefined,
        [settings],
    );

    const {
        group: aiTranslationSettingsGroup,
        languageControlsData: aiTranslationLanguageControlsData,
    } = useAiTranslationSettingsGroup({
        settings: aiTranslationFlatSettings,
        initialValidations: aiTranslationInitialValidations,
        groupLabel: aiTranslationSectionLabel,
        targetLanguageLabel: aiTranslationTargetLanguageLabel,
        deepLEnabledLabel: aiTranslationDeepLEnabledLabel,
        deepLApiKeyLabel: aiTranslationDeepLApiKeyLabel,
        deepLApiKeyDescription: aiTranslationDeepLApiKeyDescription,
        googleEnabledLabel: aiTranslationGoogleEnabledLabel,
        googleServiceAccountEmailLabel:
            aiTranslationGoogleServiceAccountEmailLabel,
        googleServiceAccountDescription:
            aiTranslationGoogleServiceAccountDescription,
        googlePrivateKeyLabel: aiTranslationGooglePrivateKeyLabel,
        alpha2EnabledLabel: aiTranslationAlpha2EnabledLabel,
        alpha2ApiKeyLabel: aiTranslationAlpha2ApiKeyLabel,
        alpha2SourceLanguageLabel: aiTranslationAlpha2SourceLanguageLabel,
        translationTestLabel: aiTranslationTranslationTestLabel,
        targetLanguageNotSupportedTemplate:
            aiTranslationTargetLanguageNotSupportedTemplate,
        noServiceSupportsLanguageNote:
            aiTranslationNoServiceSupportsLanguageNote,
        noProviderSelectedNote: aiTranslationNoProviderSelectedNote,
        alpha2LanguagesNoteTemplate: aiTranslationAlpha2LanguagesNoteTemplate,
    });

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                padding-top: ${tabMargins.top};
                padding-left: ${tabMargins.side};
                padding-right: ${tabMargins.side};
                padding-bottom: ${tabMargins.bottom};
            `}
        >
            {settings && (
                // Provide the target-language control's data here, above the Configr pane, so the
                // stable module-scope AiTranslationTargetLanguageControl (rendered somewhere inside
                // the pane) can read it via context without being redefined each render.
                <AiTranslationLanguageControlsContext.Provider
                    value={aiTranslationLanguageControlsData}
                >
                    <ConfigrPane
                        label={advancedProgramSettingsLabel}
                        showAppBar={false}
                        showSearch={false}
                        initialValues={
                            settings as React.ComponentProps<
                                typeof ConfigrPane
                            >["initialValues"]
                        }
                        themeOverrides={{
                            palette: {
                                primary: { main: kBloomBlue },
                            },
                        }}
                        onChange={(newSettings) => {
                            const normalized =
                                normalizeConfigrSettings(newSettings);
                            if (normalized) {
                                setSettings(normalized);
                                const aiFlatSettings =
                                    extractAiTranslationFlatSettings(
                                        normalized as unknown as Record<
                                            string,
                                            unknown
                                        >,
                                    );
                                const wirePayload = {
                                    ...omitAiTranslationFlatSettings(
                                        normalized as unknown as Record<
                                            string,
                                            unknown
                                        >,
                                    ),
                                    aiTranslation:
                                        buildAiTranslationWirePayload(
                                            aiFlatSettings,
                                        ),
                                };
                                postJson(
                                    "settings/advancedProgramSettings",
                                    wirePayload,
                                );
                            }
                        }}
                    >
                        <ConfigrPage
                            label={""}
                            pageKey="settings"
                            topLevel={true}
                        >
                            <ConfigrGroup label={programLabel}>
                                {showAutoUpdate && (
                                    <ConfigrBoolean
                                        label={automaticallyUpdateBloomLabel}
                                        path="autoUpdate"
                                    />
                                )}
                            </ConfigrGroup>
                            <ConfigrGroup label={qrCodesLabel}>
                                <ConfigrBoolean
                                    label={showQrCodesLabel}
                                    path="showQrCode"
                                    description={showQrCodesDescription}
                                ></ConfigrBoolean>
                                <ConfigrInput
                                    path="qrcodeCaption"
                                    label={captionLabel}
                                    description={captionDescription}
                                    charactersWide={45}
                                    allowNewLines={true}
                                    maxLinesToShowBeforeScrolling={4}
                                    disabled={!settings.showQrCode}
                                />
                            </ConfigrGroup>
                            <ConfigrGroup label={experimentalFeaturesLabel}>
                                {showExperimentalBookSourcesOption && (
                                    <ConfigrBoolean
                                        label={showExperimentalBookSourcesLabel}
                                        path="showExperimentalBookSources"
                                    />
                                )}
                                <div
                                    css={css`
                                        .Mui-disabled {
                                            opacity: 1;
                                        }
                                    `}
                                >
                                    <ConfigrBoolean
                                        label={teamCollectionsLabel}
                                        path="allowTeamCollection"
                                        disabled={
                                            !teamCollectionOptionEnabled ||
                                            !canChangeTeamCollectionOption
                                        }
                                    ></ConfigrBoolean>{" "}
                                    <div
                                        css={css`
                                            display: flex;
                                            justify-content: flex-end;
                                            .bloom-subscriptionIndicator {
                                                font-size: 10pt;
                                                font-weight: 700;
                                            }
                                        `}
                                    >
                                        <BloomSubscriptionIndicatorIconAndText
                                            feature="TeamCollection"
                                            className="bloom-subscriptionIndicator"
                                        />
                                    </div>
                                </div>
                                <div
                                    css={css`
                                        .Mui-disabled {
                                            opacity: 1;
                                        }
                                    `}
                                >
                                    <ConfigrBoolean
                                        label={appBuilderLabel}
                                        path="allowAppBuilder"
                                        disabled={!appBuilderOptionEnabled}
                                    />
                                    <div
                                        css={css`
                                            display: flex;
                                            justify-content: flex-end;
                                            .bloom-subscriptionIndicator {
                                                font-size: 10pt;
                                                font-weight: 700;
                                            }
                                        `}
                                    >
                                        <BloomSubscriptionIndicatorIconAndText
                                            feature="AppBuilder"
                                            className="bloom-subscriptionIndicator"
                                        />
                                    </div>
                                </div>
                                <div
                                    css={css`
                                        .Mui-disabled {
                                            opacity: 1;
                                        }
                                    `}
                                >
                                    <ConfigrBoolean
                                        label={aiSourceBubblesLabel}
                                        path="allowAiSourceBubbles"
                                        disabled={!aiSourceBubblesOptionEnabled}
                                    />
                                    <div
                                        css={css`
                                            display: flex;
                                            justify-content: flex-end;
                                            .bloom-subscriptionIndicator {
                                                font-size: 10pt;
                                                font-weight: 700;
                                            }
                                        `}
                                    >
                                        <BloomSubscriptionIndicatorIconAndText
                                            feature="AiSourceBubbles"
                                            className="bloom-subscriptionIndicator"
                                        />
                                    </div>
                                </div>
                            </ConfigrGroup>
                            {settings.allowAiSourceBubbles &&
                                aiTranslationSettingsGroup}
                        </ConfigrPage>
                    </ConfigrPane>
                </AiTranslationLanguageControlsContext.Provider>
            )}
        </div>
    );
};

WireUpForWinforms(AdvancedSettingsPanel);
