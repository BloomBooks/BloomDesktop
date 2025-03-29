/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useL10n2 } from "../../react_components/l10nHooks";
import { H2 } from "../../react_components/l10nComponents";
import { NoteBox } from "../../react_components/boxes";
import { kBloomUnselectedTabBackground } from "../../utils/colorUtils";
import {
    FeatureStatus,
    openBloomSubscriptionSettings
} from "../../react_components/featureStatus";
import BloomButton from "../../react_components/bloomButton";

export const PublishingBookRequiresHigherTierNotice: React.FunctionComponent<{
    titleForDisplay: string;
    featurePreventingPublishing: FeatureStatus;
}> = props => {
    const needsEnterpriseText1 = useL10n2({
        english:
            'The book titled "{0}" uses the "{1}" feature. This feature requires a Bloom subscription of at least tier "{2}".', // Enhance: use some standard method to get a localized message about what current your tier is
        key:
            "PublishTab.PublishingBookRequiresHigherTierNotice.ProblemExplanation",
        params: [
            props.titleForDisplay,
            props.featurePreventingPublishing.localizedFeature,
            props.featurePreventingPublishing.localizedTier
        ]
    });

    const needsEnterpriseText2 = useL10n2({
        english:
            "In order to publish your book, you need to either get a Bloom subscription with the necessary tier, or remove the use of this feature from your book.",
        key: "PublishTab.PublishingBookRequiresHigherTierNotice.Options"
    });

    const needsEnterpriseText3 = useL10n2({
        english: "Page {0} is the first page that uses this feature.",
        key:
            "PublishTab.PublishingBookRequiresHigherTierNotice.FirstProblematicPage",
        params: [props.featurePreventingPublishing.firstPageNumber ?? "?"]
    });

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
                    <BloomButton
                        l10nKey="Subscription.OpenSettings"
                        enabled={true}
                        onClick={() => {
                            openBloomSubscriptionSettings();
                        }}
                    >
                        Subscription Settings
                    </BloomButton>
                </div>
            </NoteBox>
        </div>
    );
};
