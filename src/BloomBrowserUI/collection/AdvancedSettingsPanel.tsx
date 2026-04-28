import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrCustomObjectInput,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
    ConfigrPane,
    ConfigrSelect,
} from "@sillsdev/config-r";
import {
    defaultDisplayName,
    parseLangtagFromLangChooser,
} from "@ethnolib/language-chooser-react-mui";
import { MenuItem, TextField } from "@mui/material";
import { get, postJson } from "../utils/bloomApi";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { tabMargins } from "./commonTabSettings";
import {
    ILanguageData,
    showLanguageChooserDialog,
} from "./LanguageChooserDialog";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { BloomSubscriptionIndicatorIconAndText } from "../react_components/requiresSubscription";
import { useGetFeatureStatus } from "../react_components/featureStatus";
import { useL10n } from "../react_components/l10nHooks";

const kOtherTargetLanguageValue = "__other__";

interface ITargetLanguageOption {
    value: string;
    label: string;
}

interface IAdvancedSettingsApiData {
    values: IAdvancedSettings;
    showAutoUpdate?: boolean;
    showExperimentalBookSourcesOption?: boolean;
    allowTeamCollectionEnabled?: boolean;
    aiSourceBubblesKnownTargetLanguages?: ITargetLanguageOption[];
}

interface IAdvancedSettings {
    autoUpdate?: boolean;
    showExperimentalBookSources?: boolean;
    allowTeamCollection?: boolean;
    allowAppBuilder?: boolean;
    allowAiSourceBubbles?: boolean;
    aiSourceBubblesProvider?: string;
    aiSourceBubblesTargetLanguageTag?: string;
    aiSourceBubblesDeepLApiKey?: string;
    aiSourceBubblesGoogleServiceAccountEmail?: string;
    aiSourceBubblesGooglePrivateKey?: string;
    showQrCode?: boolean;
    qrcodeCaption?: string;
}

function getLanguageOptionLabel(languageTag: string): string {
    const parsedLanguage = parseLangtagFromLangChooser(languageTag);
    const nameInScript = parsedLanguage?.script?.languageNameInScript;
    const defaultName =
        nameInScript ||
        (parsedLanguage?.language
            ? defaultDisplayName(parsedLanguage.language)
            : undefined);
    return defaultName || languageTag;
}

export const AdvancedSettingsPanel: React.FunctionComponent = () => {
    const [settings, setSettings] = React.useState<
        IAdvancedSettings | undefined
    >(undefined);
    const [showAutoUpdate, setShowAutoUpdate] = React.useState<boolean>(false);
    const [allowTeamCollectionEnabled, setAllowTeamCollectionEnabled] =
        React.useState<boolean>(false);
    const [
        aiSourceBubblesKnownTargetLanguages,
        setAiSourceBubblesKnownTargetLanguages,
    ] = React.useState<ITargetLanguageOption[]>([]);
    const [
        aiSourceBubblesCustomTargetLanguage,
        setAiSourceBubblesCustomTargetLanguage,
    ] = React.useState<ITargetLanguageOption>();
    const [
        showExperimentalBookSourcesOption,
        setShowExperimentalBookSourcesOption,
    ] = React.useState<boolean>(false);

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
    const aiSourceBubblesTargetLanguageDescription = useL10n(
        "Choose one of this collection's languages, or Other... to select another language.",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.TargetLanguageDescription",
    );
    const aiSourceBubblesOtherLanguageLabel = useL10n(
        "Other...",
        "CollectionSettingsDialog.AdvancedTab.AiSourceBubbles.OtherLanguageLabel",
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

    const makeCustomTargetLanguageOption = React.useCallback(
        (languageTag: string, displayName?: string): ITargetLanguageOption => {
            const label = displayName || getLanguageOptionLabel(languageTag);
            return {
                value: languageTag,
                label: `${label} (${languageTag})`,
            };
        },
        [],
    );

    const getAiSourceBubblesTargetLanguageOptions = React.useCallback(() => {
        const options = [...aiSourceBubblesKnownTargetLanguages];
        const selectedTargetLanguageTag =
            settings?.aiSourceBubblesTargetLanguageTag?.trim();
        if (
            selectedTargetLanguageTag &&
            !options.some(
                (option) => option.value === selectedTargetLanguageTag,
            )
        ) {
            const customOption =
                aiSourceBubblesCustomTargetLanguage?.value ===
                selectedTargetLanguageTag
                    ? aiSourceBubblesCustomTargetLanguage
                    : makeCustomTargetLanguageOption(selectedTargetLanguageTag);
            options.push(customOption);
        }

        options.push({
            value: kOtherTargetLanguageValue,
            label: aiSourceBubblesOtherLanguageLabel,
        });

        return options;
    }, [
        aiSourceBubblesCustomTargetLanguage,
        aiSourceBubblesKnownTargetLanguages,
        aiSourceBubblesOtherLanguageLabel,
        makeCustomTargetLanguageOption,
        settings?.aiSourceBubblesTargetLanguageTag,
    ]);

    const AiSourceBubblesTargetLanguageControl: React.FunctionComponent<{
        value: string;
        disabled?: boolean;
        onChange: (value: string) => void;
    }> = (props) => {
        return (
            <TextField
                select={true}
                fullWidth={true}
                size="small"
                value={props.value || ""}
                disabled={props.disabled}
                onChange={(event) => {
                    const nextValue = event.target.value;
                    if (nextValue === kOtherTargetLanguageValue) {
                        const selectedTargetLanguageTag =
                            props.value || undefined;
                        showLanguageChooserDialog(
                            selectedTargetLanguageTag,
                            undefined,
                            (languageData: ILanguageData) => {
                                if (!languageData.LanguageTag) {
                                    return;
                                }

                                setAiSourceBubblesCustomTargetLanguage(
                                    makeCustomTargetLanguageOption(
                                        languageData.LanguageTag,
                                        languageData.DesiredName ||
                                            languageData.DefaultName ||
                                            undefined,
                                    ),
                                );
                                props.onChange(languageData.LanguageTag);
                            },
                        );
                        return;
                    }

                    props.onChange(nextValue);
                }}
            >
                <MenuItem value={""}></MenuItem>
                {getAiSourceBubblesTargetLanguageOptions().map((option) => (
                    <MenuItem key={option.value} value={option.value}>
                        {option.label}
                    </MenuItem>
                ))}
            </TextField>
        );
    };

    // Load current advanced settings from the host dialog so Config-r starts with matching values.
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
            const loadedSettings = advancedSettingsData.values;
            setSettings(loadedSettings);
            setShowAutoUpdate(advancedSettingsData.showAutoUpdate ?? false);
            setAllowTeamCollectionEnabled(
                advancedSettingsData.allowTeamCollectionEnabled ?? false,
            );
            setShowExperimentalBookSourcesOption(
                advancedSettingsData.showExperimentalBookSourcesOption ?? false,
            );
            setAiSourceBubblesKnownTargetLanguages(
                advancedSettingsData.aiSourceBubblesKnownTargetLanguages ?? [],
            );
            if (
                loadedSettings?.aiSourceBubblesTargetLanguageTag &&
                !(
                    advancedSettingsData.aiSourceBubblesKnownTargetLanguages ??
                    []
                ).some(
                    (option) =>
                        option.value ===
                        loadedSettings.aiSourceBubblesTargetLanguageTag,
                )
            ) {
                setAiSourceBubblesCustomTargetLanguage(
                    makeCustomTargetLanguageOption(
                        loadedSettings.aiSourceBubblesTargetLanguageTag,
                    ),
                );
            }
        });
    }, [makeCustomTargetLanguageOption]);

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
                                        // The color already sets opacity to 0.26.  We don't
                                        // want to get any lighter, but MUI defaults to an
                                        // additional "opacity: 0.38" for disabled elements.
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
                        {settings.allowAiSourceBubbles && (
                            <ConfigrGroup label={aiSourceBubblesSectionLabel}>
                                <ConfigrSelect
                                    label={aiSourceBubblesProviderLabel}
                                    path="aiSourceBubblesProvider"
                                    options={[
                                        { label: "DeepL", value: "deepl" },
                                        {
                                            label: "Google Translate",
                                            value: "google",
                                        },
                                    ]}
                                />
                                <ConfigrCustomObjectInput<string>
                                    path="aiSourceBubblesTargetLanguageTag"
                                    control={
                                        AiSourceBubblesTargetLanguageControl
                                    }
                                    label={aiSourceBubblesTargetLanguageLabel}
                                    description={
                                        aiSourceBubblesTargetLanguageDescription
                                    }
                                />
                                {settings.aiSourceBubblesProvider ===
                                    "deepl" && (
                                    <ConfigrInput
                                        path="aiSourceBubblesDeepLApiKey"
                                        label={aiSourceBubblesDeepLApiKeyLabel}
                                    />
                                )}
                                {settings.aiSourceBubblesProvider ===
                                    "google" && (
                                    <>
                                        <ConfigrInput
                                            path="aiSourceBubblesGoogleServiceAccountEmail"
                                            label={
                                                aiSourceBubblesGoogleServiceAccountEmailLabel
                                            }
                                        />
                                        <ConfigrInput
                                            path="aiSourceBubblesGooglePrivateKey"
                                            label={
                                                aiSourceBubblesGooglePrivateKeyLabel
                                            }
                                            allowNewLines={true}
                                            maxLines={6}
                                        />
                                    </>
                                )}
                            </ConfigrGroup>
                        )}
                    </ConfigrPage>
                </ConfigrPane>
            )}
        </div>
    );
};

WireUpForWinforms(AdvancedSettingsPanel);
