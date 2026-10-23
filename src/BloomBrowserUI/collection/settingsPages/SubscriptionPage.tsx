import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPage, ConfigrStatic } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { SubscriptionStatus } from "../../collectionsTab/SubscriptionStatus";
import { SubscriptionControls } from "../subscriptionCodeControl";
import {
    CommunitySubscriptionInvitation,
    SubscriptionIntro,
} from "../subscriptionSettingsTab";
import "../enterpriseSettings.less";

// Config-R's own rows are padded like this (MUI ListItem).
const kRowPadding = "8px 16px";

/**
 * The Subscription page of the Collection Settings dialog: the subscription code, what it gives
 * the collection, and how to get one. The pieces are those of the WinForms dialog's Subscription
 * tab. The code is not a Config-R value, because its editor is not a Config-R control; the dialog
 * keeps it and puts it into the values it saves.
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

    // The "subscriptionSettings" class brings in the tab's styles (enterpriseSettings.less), such
    // as the red error messages and the layout of the branding summary. The box holds the pieces
    // stacked in one element, as the WinForms tab does.
    return (
        <ConfigrPage label={label} pageKey="subscription" topLevel={true}>
            <ConfigrStatic>
                <div className="subscriptionSettings">
                    <SubscriptionIntro />
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
                        <CommunitySubscriptionInvitation />
                    )}
                </div>
            </ConfigrGroup>
        </ConfigrPage>
    );
}
