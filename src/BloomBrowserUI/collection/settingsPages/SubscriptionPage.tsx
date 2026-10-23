import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPage, ConfigrStatic } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { Markdown } from "../../react_components/markdown";
import { NoteBox } from "../../react_components/boxes";
import { SubscriptionStatus } from "../../collectionsTab/SubscriptionStatus";
import { SubscriptionControls } from "../subscriptionCodeControl";
import "../enterpriseSettings.less";

// Config-R's own rows are padded like this (MUI ListItem).
const kRowPadding = "8px 16px";

/**
 * The Subscription page of the Collection Settings dialog: what a subscription is for and how to
 * get one, then the subscription code and what it gives the collection. The code is not a
 * Config-R value, because its editor is not a Config-R control; the dialog keeps it and puts it
 * into the values it saves.
 */
export function useSubscriptionPage(props: {
    // Undefined until the dialog's GET collection/settings has answered.
    code: string | undefined;
    onCodeChange: (code: string) => void;
}): React.ReactElement {
    const label = useL10n(
        "Subscription",
        "CollectionSettingsDialog.SubscriptionPage",
    );

    // The "subscriptionSettings" class brings in the styles shared with the WinForms tab
    // (enterpriseSettings.less), such as the red error messages and the layout of the branding
    // summary.
    return (
        <ConfigrPage label={label} pageKey="subscription" topLevel={true}>
            <ConfigrStatic>
                <div className="subscriptionSettings">
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
                        Please contact [%0](%1) to purchase your subscription
                        code.
                    </Markdown>
                </div>
            </ConfigrStatic>
            <ConfigrGroup>
                <div
                    className="subscriptionSettings"
                    css={css`
                        padding: ${kRowPadding};
                    `}
                >
                    <SubscriptionControls
                        // The dialog shows its pages only once the settings have loaded.
                        code={props.code!}
                        onCodeChange={props.onCodeChange}
                    />
                    <SubscriptionStatus minimalUI code={props.code} />
                    {/* C# calls a blank code "none", so this is the WinForms tab's rule. */}
                    {props.code?.trim() === "" && (
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
                                <Markdown l10nKey="Settings.Subscription.Community.Invitation">
                                    If your project is fully funded and managed
                                    by your local language community, you may
                                    qualify for a free [Bloom Community
                                    Subscription](https://bloomlibrary.org/subscriptions).
                                </Markdown>
                            </div>
                        </NoteBox>
                    )}
                </div>
            </ConfigrGroup>
        </ConfigrPage>
    );
}
