/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useL10n2 } from "../../react_components/l10nHooks";
import { H2 } from "../../react_components/l10nComponents";
import { NoteBox } from "../../react_components/boxes";
import { kBloomUnselectedTabBackground } from "../../utils/colorUtils";
import {
    FeatureStatus,
    openBloomSubscriptionSettings,
    useGetFeatureAvailabilityMessage
} from "../../react_components/featureStatus";
import BloomButton from "../../react_components/bloomButton";

export const PublishingBookRequiresHigherTierNotice: React.FunctionComponent<{
    titleForDisplay: string;
    featurePreventingPublishing: FeatureStatus;
}> = props => {
    const nameTheFeatureMessageL10nParams = React.useMemo(
        () => [
            props.titleForDisplay,
            props.featurePreventingPublishing.localizedFeature
        ],
        [
            props.titleForDisplay,
            props.featurePreventingPublishing.localizedFeature
        ]
    );
    const nameTheFeatureMessage = useL10n2({
        english: 'The book titled "{0}" uses the "{1}" feature.',
        key:
            "PublishTab.PublishingBookRequiresHigherTierNotice.ProblemExplanation",
        params: nameTheFeatureMessageL10nParams
    });
    const requiredTierMessage = useGetFeatureAvailabilityMessage(
        props.featurePreventingPublishing
    );

    const whatToDoMessage = useL10n2({
        english:
            "In order to publish your book, you need to either get a Bloom subscription with the necessary tier, or remove the use of this feature from your book.",
        key: "PublishTab.PublishingBookRequiresHigherTierNotice.Options"
    });

    const firstPageMessageL10nParams = React.useMemo(
        () => [props.featurePreventingPublishing.firstPageNumber ?? "?"],
        [props.featurePreventingPublishing.firstPageNumber]
    );
    const firstPageMessage = useL10n2({
        english: "Page {0} is the first page that uses this feature.",
        key:
            "PublishTab.PublishingBookRequiresHigherTierNotice.FirstProblematicPage",
        params: firstPageMessageL10nParams
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
                        l10nKey="Common.HigherSubscriptionTierRequired"
                        css={css`
                            margin-top: 0;
                        `}
                    >
                        Feature Requires Higher Subscription Tier
                    </H2>
                    <p>
                        {nameTheFeatureMessage} {requiredTierMessage}
                    </p>
                    <p>{whatToDoMessage}</p>
                    <p>{firstPageMessage}</p>
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
