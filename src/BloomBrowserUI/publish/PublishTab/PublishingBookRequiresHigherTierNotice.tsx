/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { H2 } from "../../react_components/l10nComponents";
import { NoteBox } from "../../react_components/boxes";
import { kBloomUnselectedTabBackground } from "../../utils/colorUtils";
import { FeatureStatus } from "../../react_components/featureStatus";

export const PublishingBookRequiresHigherTierNotice: React.FunctionComponent<{
    titleForDisplay: string;
    featurePreventingPublishing: FeatureStatus;
}> = props => {
    const needsEnterpriseText1 = useL10n(
        "The book titled '{0}' uses a feature, {1}, that requires the subscription tier {2} or higher.", // Enhance: use some standard method to get a localized message about what current your tier is
        "PublishTab.PublishingBookRequiresHigherTierNotice.ProblemExplanation",
        props.titleForDisplay,
        props.featurePreventingPublishing.Feature,
        props.featurePreventingPublishing.SubscriptionTier
    );

    const needsEnterpriseText2 = useL10n(
        "In order to publish your book, you need to either get a Bloom subscription with the necessary tier, or remove the use of this feature from your book.",
        "PublishTab.PublishingBookRequiresHigherTierNotice.Options"
    );

    const needsEnterpriseText3 = useL10n(
        "Page {0} is the first page that uses this feature.",
        "PublishTab.PublishingBookRequiresHigherTierNotice.FirstProblematicPage",
        "",
        "" + props.featurePreventingPublishing.FirstPageNumber
    );

    return (
        <div
            css={css`
                background-color: ${kBloomUnselectedTabBackground};
                margin: 0;
                height: 100%;
                width: 100%;
                position: absolute;
            `}
        >
            <NoteBox
                css={css`
                    max-width: 800px;
                    width: fit-content;
                    margin: 30px;
                `}
                iconSize="large"
            >
                <div>
                    <H2
                        l10nKey="Common.SubscriptionRequired"
                        css={css`
                            margin-top: 0;
                        `}
                    >
                        Feature Requires Higher Subscription Tier
                    </H2>
                    <p>{needsEnterpriseText1}</p>
                    <p>{needsEnterpriseText2}</p>
                    <p>{needsEnterpriseText3}</p>
                </div>
            </NoteBox>
        </div>
    );
};
