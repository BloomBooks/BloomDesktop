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
    IAiSourceBubblesSettings,
    IAiSourceBubblesValidationState,
    useAiSourceBubblesSettingsGroup,
} from "./AiSourceBubblesSettingsGroup";

interface IAdvancedSettingsApiData {
    values: IAdvancedSettings;
    showAutoUpdate?: boolean;
    showExperimentalBookSourcesOption?: boolean;
    allowTeamCollectionEnabled?: boolean;
    aiSourceBubblesValidation?: IAiSourceBubblesValidationState;
}

interface IAdvancedSettings extends IAiSourceBubblesSettings {
    autoUpdate?: boolean;
    showExperimentalBookSources?: boolean;
    allowTeamCollection?: boolean;
    allowAppBuilder?: boolean;
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
    const [aiSourceBubblesValidation, setAiSourceBubblesValidation] =
        React.useState<IAiSourceBubblesValidationState>();

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
        "AI Source Bubbles",
        "CollectionSettingsDialog.AdvancedTab.Experimental.AiSourceBubbles",
    );
    const aiSourceBubblesSectionLabel = useL10n(
        "AI Source Bubbles",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.SectionLabel",
    );
    const aiSourceBubblesProviderLabel = useL10n(
        "Provider",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.ProviderLabel",
    );
    const aiSourceBubblesTargetLanguageLabel = useL10n(
        "Target Language",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.TargetLanguageLabel",
    );
    const aiSourceBubblesDeepLApiKeyLabel = useL10n(
        "DeepL API Key",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.DeepLApiKeyLabel",
    );
    const aiSourceBubblesGoogleServiceAccountEmailLabel = useL10n(
        "Google Service Account Email",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.GoogleServiceAccountEmailLabel",
    );
    const aiSourceBubblesGooglePrivateKeyLabel = useL10n(
        "Google Service Account Private Key",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.GooglePrivateKeyLabel",
    );
    const aiSourceBubblesTranslationTestLabel = useL10n(
        "Translation Test",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.TranslationTestLabel",
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
            setSettings(advancedSettingsData.values);
            setShowAutoUpdate(advancedSettingsData.showAutoUpdate ?? false);
            setAllowTeamCollectionEnabled(
                advancedSettingsData.allowTeamCollectionEnabled ?? false,
            );
            setShowExperimentalBookSourcesOption(
                advancedSettingsData.showExperimentalBookSourcesOption ?? false,
            );
            setAiSourceBubblesValidation(
                advancedSettingsData.aiSourceBubblesValidation,
            );
        });
    }, []);

    const aiSourceBubblesSettingsGroup = useAiSourceBubblesSettingsGroup({
        settings,
        initialValidation: aiSourceBubblesValidation,
        groupLabel: aiSourceBubblesSectionLabel,
        providerLabel: aiSourceBubblesProviderLabel,
        targetLanguageLabel: aiSourceBubblesTargetLanguageLabel,
        deepLApiKeyLabel: aiSourceBubblesDeepLApiKeyLabel,
        googleServiceAccountEmailLabel:
            aiSourceBubblesGoogleServiceAccountEmailLabel,
        googlePrivateKeyLabel: aiSourceBubblesGooglePrivateKeyLabel,
        translationTestLabel: aiSourceBubblesTranslationTestLabel,
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
                            postJson(
                                "settings/advancedProgramSettings",
                                normalized,
                            );
                        }
                    }}
                >
                    <ConfigrPage label={""} pageKey="settings" topLevel={true}>
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
                            aiSourceBubblesSettingsGroup}
                    </ConfigrPage>
                </ConfigrPane>
            )}
        </div>
    );
};

WireUpForWinforms(AdvancedSettingsPanel);
