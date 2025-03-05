/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useMemo } from "react";
import { Markdown } from "../react_components/markdown";
import "./enterpriseSettings.less";
import { tabMargins } from "./commonTabSettings";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { SubscriptionStatus } from "../collectionsTab/SubscriptionStatus";
import { NoteBox } from "../react_components/boxes";
import { SubscriptionControls } from "./subscriptionCodeControl";

// This component implements the Bloom Enterprise tab of the Settings dialog.
export const EnterpriseSettings: React.FunctionComponent = () => {
    // TODO Put this back in the display <---------------------------------------------------!!!!!!!!!!!
    const sileCodesUrl =
        "https://gateway.sil.org/display/LSDEV/Bloom+enterprise+subscription+codes";

    return (
        <div
            className="enterpriseSettings"
            css={css`
                margin-top: ${tabMargins.top};
                margin-left: ${tabMargins.side};
                margin-right: ${tabMargins.side};
                margin-bottom: ${tabMargins.bottom};
            `}
        >
            <Markdown l10nKey="Settings.Subscription.IntroText">
                To help cover a portion of the costs associated with providing
                Bloom, we offer [advanced features](%1) and customizations as a
                subscription service.
            </Markdown>
            <Markdown
                l10nKey="Settings.Subscription.RequestSubscription"
                l10nParam0={"subscriptions@bloomlibrary.org"}
                l10nParam1={"mailto://subscriptions@bloomlibrary.org"}
            >
                Please contact [%1](%2) to request your subscription code.
            </Markdown>

            <SubscriptionControls />
            <br />
            <SubscriptionStatus minimalUI />
            <NoteBox
                css={css`
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
                    <Markdown l10nKey="Settings.Enterprise.Community.Invitation">
                        If your project is fully funded and managed by your
                        local language community, you may qualify for a free
                        [Bloom Community Subscription](https://example.com).
                    </Markdown>
                    <br />
                    <Markdown
                        l10nKey="Settings.Subscription.RequestSubscription"
                        l10nParam0={"subscriptions@bloomlibrary.org"}
                        l10nParam1={"mailto://subscriptions@bloomlibrary.org"}
                    >
                        Please contact [%1](%2) to request your license.
                    </Markdown>
                </div>
            </NoteBox>
        </div>
    );
};

WireUpForWinforms(() => <EnterpriseSettings />);
