import { css } from "@emotion/react";
import * as React from "react";
import { BloomSubscriptionIndicatorIconAndText } from "../../react_components/requiresSubscription";

/**
 * A row of a settings page (a Config-R control), with the badge of the subscription feature it
 * needs beneath it on the right. Use it as a child of a ConfigrGroup: being a component, it drops
 * the props Config-R gives a group's children rather than passing them on to the DOM.
 */
export const RowWithSubscriptionBadge: React.FunctionComponent<{
    // The feature name features/status knows it by, e.g. "Bookshelf".
    subscriptionFeature: string;
    children: React.ReactNode;
}> = (props) => (
    // One element around both keeps Config-R from drawing a divider between them.
    <div>
        {props.children}
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
    </div>
);
