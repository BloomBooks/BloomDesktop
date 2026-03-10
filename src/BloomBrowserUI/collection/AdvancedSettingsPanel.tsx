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
import Typography from "@mui/material/Typography/Typography";
import { BloomEnterpriseIconWithTooltip } from "../react_components/requiresSubscription";
import { useGetFeatureStatus } from "../react_components/featureStatus";
import { useL10n } from "../react_components/l10nHooks";

export const AdvancedSettingsPanel: React.FunctionComponent = () => {
    const [settings, setSettings] = React.useState<object | undefined>(
        undefined,
    );
    const [showAutoUpdate, setShowAutoUpdate] = React.useState<boolean>(false);
    const [allowTeamCollectionEnabled, setAllowTeamCollectionEnabled] =
        React.useState<boolean>(false);
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
    const showExperimentalBookSourcesLabel = useL10n(
        "Show Experimental Book Sources",
        "CollectionSettingsDialog.AdvancedTab.Experimental.ShowExperimentalBookSources",
    );
    const teamCollectionsLabel = useL10n(
        "Team Collections",
        "TeamCollection.TeamCollections",
    );
    const availableWithSubscriptionLabel = useL10n(
        "Available with your Bloom Subscription",
        "AvailableWithSubscription",
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
    const canChangeTeamCollectionOption = allowTeamCollectionEnabled !== false;

    const normalizeConfigrSettings = React.useCallback(
        (settingsValue: string | object | undefined): object | undefined => {
            if (!settingsValue) {
                return undefined;
            }
            if (typeof settingsValue === "string") {
                return JSON.parse(settingsValue) as object;
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
            setShowExperimentalBookSourcesOption(
                data["showExperimentalBookSourcesOption"] ?? false,
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
                                ></ConfigrBoolean>
                            </div>
                            <div
                                css={css`
                                    display: flex;
                                    justify-content: flex-end;
                                `}
                            >
                                <BloomEnterpriseIconWithTooltip featureName="TeamCollection" />
                                <Typography
                                    css={css`
                                        padding-left: 7px;
                                        font-size: 10pt;
                                        font-weight: 700;
                                    `}
                                >
                                    {availableWithSubscriptionLabel}
                                </Typography>
                            </div>
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
                                disabled={!settings["showQrCode"]}
                            />
                        </ConfigrGroup>
                    </ConfigrPage>
                </ConfigrPane>
            )}
        </div>
    );
};

WireUpForWinforms(AdvancedSettingsPanel);
