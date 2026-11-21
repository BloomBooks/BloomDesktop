import { css } from "@emotion/react";
import { ConfigrGroup, ConfigrPage, ConfigrStatic } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { Markdown } from "../../react_components/markdown";
import { NoteBox } from "../../react_components/boxes";
import { SubscriptionStatus } from "../../collectionsTab/SubscriptionStatus";
import { SubscriptionControls } from "../subscriptionCodeControl";
import { kUiFontStack } from "../../bloomMaterialUITheme";
import { ISettingsPage, ISettingsPageContext } from "./settingsPage";

const kIntroEnglish =
    "To help cover a portion of the costs associated with providing Bloom, we offer [advanced features](%0) and customizations as a subscription service.";
const kRequestEnglish =
    "Please contact [%0](%1) to purchase your subscription code.";
const kFeatureMatrixUrl = "https://bloomlibrary.org/feature-matrix"; // redirected by cloudflare
const kSubscriptionsEmail = "subscriptions@bloomlibrary.org";
const kSubscriptionsMailto = "mailto:subscriptions@bloomlibrary.org";

// Config-R's own rows are padded like this (MUI ListItem).
const kRowPadding = "8px 16px";
// The page's text is in Bloom's UI font, as in the rest of Bloom's subscription UI.
const kTextStyle = css`
    font-family: ${kUiFontStack};
    font-size: 10pt;
`;

/**
 * The Subscription page of the Collection Settings dialog: what a subscription is for and how to
 * get one, then the subscription code and what it gives the collection. The code's editor is not a
 * Config-R control, so it is edited through context.editValues.
 */
export function useSubscriptionPage(
    context: ISettingsPageContext,
): ISettingsPage {
    const label = useL10n(
        "Subscription",
        "CollectionSettingsDialog.SubscriptionPage",
    );

    // As typed; undefined until the values have loaded.
    const code = context.values?.subscription.code;

    // The introduction as the user reads it. Search finds the English written below; these let
    // it find the translation the Markdown shows instead.
    const introText = useL10n(
        kIntroEnglish,
        "Settings.Subscription.IntroText",
        undefined,
        kFeatureMatrixUrl,
    );
    const requestText = useL10n(
        kRequestEnglish,
        "Settings.Subscription.RequestSubscription",
        undefined,
        kSubscriptionsEmail,
        kSubscriptionsMailto,
    );

    const element = (
        <ConfigrPage label={label} pageKey="subscription" topLevel={true}>
            <ConfigrStatic searchTerms={`${introText} ${requestText}`}>
                <div css={kTextStyle}>
                    <Markdown
                        l10nKey="Settings.Subscription.IntroText"
                        l10nParam0={kFeatureMatrixUrl}
                    >
                        {kIntroEnglish}
                    </Markdown>
                    <Markdown
                        l10nKey="Settings.Subscription.RequestSubscription"
                        l10nParam0={kSubscriptionsEmail}
                        l10nParam1={kSubscriptionsMailto}
                    >
                        {kRequestEnglish}
                    </Markdown>
                </div>
            </ConfigrStatic>
            <ConfigrGroup>
                {/* ConfigrStatic takes the props Config-R gives a group's children, which a
                    plain div would pass on to the DOM. */}
                <ConfigrStatic>
                    <div
                        css={[
                            kTextStyle,
                            css`
                                padding: ${kRowPadding};
                            `,
                        ]}
                    >
                        <SubscriptionControls
                            // The dialog shows its pages only once the settings have loaded.
                            code={code!}
                            onCodeChange={(newCode) =>
                                context.editValues({
                                    subscription: { code: newCode },
                                })
                            }
                        />
                        <SubscriptionStatus minimalUI code={code} />
                        {/* A blank code means the collection has no subscription. */}
                        {code?.trim() === "" && (
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
                                        If your project is fully funded and
                                        managed by your local language
                                        community, you may qualify for a free
                                        [Bloom Community
                                        Subscription](https://bloomlibrary.org/subscriptions).
                                    </Markdown>
                                </div>
                            </NoteBox>
                        )}
                    </div>
                </ConfigrStatic>
            </ConfigrGroup>
        </ConfigrPage>
    );
    return {
        element,
        helpTopic: "Tasks/Basic_tasks/Enter_Subscription_Code.htm",
    };
}
