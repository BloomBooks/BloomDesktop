/**
 * @jsx jsx
 * @jsxFrag React.Fragment
 **/

import { jsx, css } from "@emotion/react";

import * as React from "react";
import { Markdown } from "../react_components/markdown";
import "./enterpriseSettings.less";
import { tabMargins } from "./commonTabSettings";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { SubscriptionStatus } from "../collectionsTab/SubscriptionStatus";
import { NoteBox } from "../react_components/boxes";
import { SubscriptionControls } from "./subscriptionCodeControl";
import { useSubscriptionInfo } from "./useSubscriptionInfo";

// This component implements the Bloom Subscription tab of the Settings dialog.
export const SubscriptionSettings: React.FunctionComponent = () => {
    const { subscriptionCodeIntegrity, haveData } = useSubscriptionInfo();

    if (!haveData) {
        return null;
    }

    return (
        <div
            className="subscriptionSettings"
            css={css`
                display: flex;
                flex-direction: column;
                height: calc(100% - ${tabMargins.top});
                padding-top: ${tabMargins.top};
                padding-left: ${tabMargins.side};
                padding-right: ${tabMargins.side};
                padding-bottom: ${tabMargins.bottom};
            `}
        >
            <>
                <Markdown
                    l10nKey="Settings.Subscription.IntroText"
                    l10nParam0={"https://bloomlibrary.org/feature-matrix"} // redirected by cloudflare
                >
                    To help cover a portion of the costs associated with
                    providing Bloom, we offer [advanced features](%0) and
                    customizations as a subscription service.
                </Markdown>
                <Markdown
                    l10nKey="Settings.Subscription.RequestSubscription"
                    l10nParam0={"subscriptions@bloomlibrary.org"}
                    l10nParam1={"mailto:subscriptions@bloomlibrary.org"}
                >
                    Please contact [%0](%1) to purchase your subscription code.
                </Markdown>

                <SubscriptionControls />
                <br />
                <SubscriptionStatus minimalUI />
                {subscriptionCodeIntegrity === "none" && (
                    <NoteBox
                        css={css`
                            margin-top: auto; // push to bottom
                            p {
                                margin: 0; // markdown wraps everything in a p tag which adds a big margin we don't need
                            }
                        `}
                    >
                        {/* wrap in a div with display inline */}
                        <div
                            css={css`
                                display: inline;
                            `}
                        >
                            <Markdown l10nKey="Settings.Subscription.Community.Invitation">
                                If your project is fully funded and managed by
                                your local language community, you may qualify
                                for a free [Bloom Community
                                Subscription](https://bloomlibrary.org/subscriptions).
                            </Markdown>
                        </div>
                    </NoteBox>
                )}
            </>
        </div>
    );
};

WireUpForWinforms(() => <SubscriptionSettings />);
