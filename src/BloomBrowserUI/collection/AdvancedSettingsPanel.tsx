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
import { BloomSubscriptionIndicatorIconAndText } from "../react_components/requiresSubscription";
import { useGetFeatureStatus } from "../react_components/featureStatus";
import { useL10n } from "../react_components/l10nHooks";

interface IAdvancedSettings {
    autoUpdate?: boolean;
    showExperimentalBookSources?: boolean;
    allowTeamCollection?: boolean;
    allowCloudTeamCollection?: boolean;
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
        allowCloudTeamCollectionEnabled,
        setAllowCloudTeamCollectionEnabled,
    ] = React.useState<boolean>(false);
    const [
        showExperimentalBookSourcesOption,
        setShowExperimentalBookSourcesOption,
    ] = React.useState<boolean>(false);
    // For 6.5 the Cloud Team Collections checkbox is hidden from most end users; the host only
    // reports this true when the `cloudCollections` environment variable is set (see
    // CollectionSettingsApi.CloudTeamCollectionOptionVisible).
    const [showAllowCloudTeamCollection, setShowAllowCloudTeamCollection] =
        React.useState<boolean>(false);

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
    const cloudTeamCollectionsLabel = useL10n(
        "Cloud Team Collections",
        "CollectionSettingsDialog.AdvancedTab.Experimental.CloudTeamCollections",
    );
    const appBuilderLabel = useL10n(
        "App Builder",
        "CollectionSettingsDialog.AdvancedTab.Experimental.AppBuilder",
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
    const cloudTeamCollectionFeatureStatus = useGetFeatureStatus(
        "CloudTeamCollection",
    );
    const cloudTeamCollectionOptionEnabled =
        cloudTeamCollectionFeatureStatus === undefined
            ? true
            : cloudTeamCollectionFeatureStatus.enabled;
    const appBuilderFeatureStatus = useGetFeatureStatus("AppBuilder");
    const appBuilderOptionEnabled =
        appBuilderFeatureStatus === undefined
            ? false
            : appBuilderFeatureStatus.enabled;
    const canChangeTeamCollectionOption = allowTeamCollectionEnabled !== false;
    const canChangeCloudTeamCollectionOption =
        allowCloudTeamCollectionEnabled !== false;

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
            setSettings(data["values"]);
            setShowAutoUpdate(data["showAutoUpdate"] ?? false);
            setAllowTeamCollectionEnabled(
                data["allowTeamCollectionEnabled"] ?? false,
            );
            setAllowCloudTeamCollectionEnabled(
                data["allowCloudTeamCollectionEnabled"] ?? false,
            );
            setShowExperimentalBookSourcesOption(
                data["showExperimentalBookSourcesOption"] ?? false,
            );
            setShowAllowCloudTeamCollection(
                data["showAllowCloudTeamCollection"] ?? false,
            );
        });
    }, []);

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
                            {showAllowCloudTeamCollection && (
                                <div
                                    css={css`
                                        .Mui-disabled {
                                            opacity: 1;
                                        }
                                    `}
                                >
                                    <ConfigrBoolean
                                        label={cloudTeamCollectionsLabel}
                                        path="allowCloudTeamCollection"
                                        disabled={
                                            !cloudTeamCollectionOptionEnabled ||
                                            !canChangeCloudTeamCollectionOption
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
                                            feature="CloudTeamCollection"
                                            className="bloom-subscriptionIndicator"
                                        />
                                    </div>
                                </div>
                            )}
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
                        </ConfigrGroup>
                    </ConfigrPage>
                </ConfigrPane>
            )}
        </div>
    );
};

WireUpForWinforms(AdvancedSettingsPanel);
