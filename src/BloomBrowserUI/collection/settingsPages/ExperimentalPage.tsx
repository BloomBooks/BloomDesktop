import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrBoolean, ConfigrGroup, ConfigrPage } from "@sillsdev/config-r";
import { useGetFeatureStatus } from "../../react_components/featureStatus";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomSubscriptionIndicatorIconAndText } from "../../react_components/requiresSubscription";
import { ICollectionSettingsResponse } from "../collectionSettingsTypes";

// ExperimentalFeatures.kTeamCollections in C#: the key of the feature in values.experimental.
const kTeamCollectionsFeatureToken = "team-collections";

// One row of the Experimental page: a checkbox that turns an experimental feature on or off,
// with the badge for the subscription feature it needs, if any.
const ExperimentalFeatureSetting: React.FunctionComponent<{
    label: string;
    // The feature's token in ExperimentalFeatures.cs, which is its key in values.experimental.
    featureToken: string;
    // The feature name features/status knows it by, when it needs a subscription tier.
    subscriptionFeature?: string;
    disabled: boolean;
}> = (props) => (
    // One element around both keeps Config-R from drawing a divider between the checkbox and its
    // subscription badge.
    <div>
        <ConfigrBoolean
            label={props.label}
            path={`experimental.${props.featureToken}`}
            disabled={props.disabled}
        />
        {props.subscriptionFeature && (
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
        )}
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

    // This hook runs whenever the dialog renders, so the subscription check goes out as soon as the
    // dialog opens rather than when Config-R mounts this page (it mounts only the page showing).
    // The answer is then in before anyone gets here and the checkbox doesn't flash greyed out.
    // Until it arrives the checkbox stays disabled, so nobody can tick it and save on a tier that
    // lacks it. (The old dialog treated "not known yet" as available.)
    // Expect to refactor this with the next page that needs subscription status (likely the
    // Subscription page, BL-16734): probably into a small dialog-level subscription context that
    // every page reads, possibly following a code typed but not yet saved. See PLAN.md, Step 4.
    const teamCollectionStatus = useGetFeatureStatus(
        props.dialogOpen ? "TeamCollection" : undefined,
    );

    return (
        <ConfigrPage
            key="experimental"
            label={label}
            pageKey="experimental"
            topLevel={true}
        >
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
