import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrBoolean, ConfigrGroup, ConfigrPage } from "@sillsdev/config-r";
import { useGetFeatureStatus } from "../../react_components/featureStatus";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomSubscriptionIndicatorIconAndText } from "../../react_components/requiresSubscription";
import { ICollectionSettingsResponse } from "../collectionSettingsTypes";

// ExperimentalFeatures.kTeamCollections in C#: the key of the feature in values.experimental.
const kTeamCollectionsFeatureToken = "team-collections";
// ExperimentalFeatures.kTables in C#.
const kTablesFeatureToken = "tables";

// One row of the Experimental page: a checkbox that turns an experimental feature on or off, with
// the badge for the subscription feature it needs.
const ExperimentalFeatureSetting: React.FunctionComponent<{
    label: string;
    // The feature's token in ExperimentalFeatures.cs, which is its key in values.experimental.
    featureToken: string;
    // The feature name features/status knows it by. We assume every experimental feature needs a
    // subscription tier; supporting one that doesn't isn't worth the extra code until there is one.
    subscriptionFeature: string;
    disabled: boolean;
}> = (props) => (
    // One element around both keeps Config-R from drawing a divider between them.
    <div>
        <ConfigrBoolean
            label={props.label}
            path={`experimental.${props.featureToken}`}
            disabled={props.disabled}
        />
        <div
            css={css`
                display: flex;
                padding-bottom: 5px;
                font-size: 12px;
                font-weight: bold;
            `}
        >
            <BloomSubscriptionIndicatorIconAndText
                feature={props.subscriptionFeature}
                css={css`
                    margin-left: auto;
                `}
            />
        </div>
    </div>
);

/**
 * The Experimental page of the Collection Settings dialog: the experimental features a user can
 * turn on.
 */
export function useExperimentalPage(props: {
    dialogOpen: boolean;
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
}): React.ReactElement {
    const label = useL10n(
        "Experimental",
        "CollectionSettingsDialog.ExperimentalPage",
    );
    const teamCollectionsLabel = useL10n(
        "Team Collections",
        "TeamCollection.TeamCollections",
    );
    const tablesLabel = useL10n(
        "Tables",
        "CollectionSettingsDialog.AdvancedTab.Experimental.Tables",
    );

    // Asked when the dialog opens, not when Config-R mounts this page (it mounts only the page
    // showing), so the answer is in before anyone gets here. Until then the box stays disabled.
    // Expect to refactor this into a dialog-level subscription lookup with the next page that
    // needs one, probably the Subscription page (whose typed-but-unsaved code may need to drive
    // this too).
    const teamCollectionStatus = useGetFeatureStatus(
        props.dialogOpen ? "TeamCollection" : undefined,
    );
    const tablesStatus = useGetFeatureStatus(
        props.dialogOpen ? "Table" : undefined,
    );

    return (
        <ConfigrPage label={label} pageKey="experimental" topLevel={true}>
            {/* No label: the page title already says "Experimental". */}
            <ConfigrGroup>
                <ExperimentalFeatureSetting
                    label={teamCollectionsLabel}
                    featureToken={kTeamCollectionsFeatureToken}
                    subscriptionFeature="TeamCollection"
                    disabled={
                        teamCollectionStatus?.enabled !== true ||
                        // Someone in a Team Collection may not turn the feature off.
                        (props.settings?.isTeamCollection === true &&
                            props.settings.values.experimental[
                                kTeamCollectionsFeatureToken
                            ])
                    }
                />
                <ExperimentalFeatureSetting
                    label={tablesLabel}
                    featureToken={kTablesFeatureToken}
                    subscriptionFeature="Table"
                    disabled={tablesStatus?.enabled !== true}
                />
            </ConfigrGroup>
        </ConfigrPage>
    );
}
