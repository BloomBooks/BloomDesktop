import { css } from "@emotion/react";

import * as React from "react";
import { Markdown } from "../react_components/markdown";
import "./enterpriseSettings.less";
import { tabMargins } from "./commonTabSettings";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { SubscriptionStatus } from "../collectionsTab/SubscriptionStatus";
import { NoteBox } from "../react_components/boxes";
import { SubscriptionControls } from "./subscriptionCodeControl";
import { useSubscriptionInfo } from "./useSubscriptionInfo";
import { get, postString } from "../utils/bloomApi";
import { useMountEffect } from "../utils/useMountEffect";

// This component implements the Bloom Subscription tab of the WinForms Settings dialog. (The React
// Collection Settings dialog has its own page, settingsPages/SubscriptionPage.tsx, built from the
// pieces below.) The code being typed lives in C#, which that dialog saves on OK, so each change
// is posted to it.
export const SubscriptionSettings: React.FunctionComponent = () => {
    const [code, setCode] = React.useState<string>();
    // This effect fetches, once, the code the WinForms dialog is editing. A reply that arrives
    // after the tab has gone is dropped.
    useMountEffect(() => {
        let isCurrent = true;
        get("settings/subscriptionCode", (result) => {
            if (isCurrent) setCode(result.data);
        });
        return () => {
            isCurrent = false;
        };
    });
    const { subscriptionCodeIntegrity, haveData } = useSubscriptionInfo(code);

    if (code === undefined || !haveData) {
        return null;
    }
    const changeCode = (newCode: string) => {
        postString("settings/subscriptionCode", newCode);
        setCode(newCode);
    };

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
                <SubscriptionIntro />

                <SubscriptionControls code={code} onCodeChange={changeCode} />
                <br />
                <SubscriptionStatus minimalUI code={code} />
                {subscriptionCodeIntegrity === "none" && (
                    <CommunitySubscriptionInvitation />
                )}
            </>
        </div>
    );
};

// What a subscription is for, and how to get one.
export const SubscriptionIntro: React.FunctionComponent = () => (
    <>
        <Markdown
            l10nKey="Settings.Subscription.IntroText"
            l10nParam0={"https://bloomlibrary.org/feature-matrix"} // redirected by cloudflare
        >
            To help cover a portion of the costs associated with providing
            Bloom, we offer [advanced features](%0) and customizations as a
            subscription service.
        </Markdown>
        <Markdown
            l10nKey="Settings.Subscription.RequestSubscription"
            l10nParam0={"subscriptions@bloomlibrary.org"}
            l10nParam1={"mailto:subscriptions@bloomlibrary.org"}
        >
            Please contact [%0](%1) to purchase your subscription code.
        </Markdown>
    </>
);

// Shown when no subscription code has been entered.
export const CommunitySubscriptionInvitation: React.FunctionComponent = () => (
    <NoteBox
        css={css`
            margin-top: auto; // push to bottom of the WinForms tab
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
                If your project is fully funded and managed by your local
                language community, you may qualify for a free [Bloom Community
                Subscription](https://bloomlibrary.org/subscriptions).
            </Markdown>
        </div>
    </NoteBox>
);

WireUpForWinforms(() => <SubscriptionSettings />);
