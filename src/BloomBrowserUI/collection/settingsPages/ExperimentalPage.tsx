import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrBoolean, ConfigrGroup, ConfigrPage } from "@sillsdev/config-r";
import { useGetFeatureStatus } from "../../react_components/featureStatus";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomSubscriptionIndicatorIconAndText } from "../../react_components/requiresSubscription";
import { ICollectionSettingsResponse } from "../collectionSettingsTypes";

// ExperimentalFeatures.kTeamCollections in C#: the key of the feature in values.experimental.
const kTeamCollectionsFeatureToken = "team-collections";

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
 * The Experimental page of the Collection Settings dialog (BL-16738): the experimental features a
 * user can turn on. It offers exactly what the old dialog's Advanced tab did.
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

    // Asked when the dialog opens, not when Config-R mounts this page (it mounts only the page
    // showing), so the answer is in before anyone gets here. Until then the box stays disabled.
    // Expect to refactor this into a dialog-level subscription lookup with the next page that
    // needs one; see PLAN.md, Step 4.
    const teamCollectionStatus = useGetFeatureStatus(
        props.dialogOpen ? "TeamCollection" : undefined,
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
                        // As in the WinForms dialog, someone in a Team Collection may not turn
                        // the feature off.
                        (props.settings?.isTeamCollection === true &&
                            props.settings.values.experimental[
                                kTeamCollectionsFeatureToken
                            ])
                    }
                />
            </ConfigrGroup>
        </ConfigrPage>
    );
}
