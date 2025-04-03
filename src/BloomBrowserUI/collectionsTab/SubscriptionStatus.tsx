/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import { useApiString } from "../utils/bloomApi";
import * as React from "react";
import WarningIcon from "@mui/icons-material/Warning";
import CancelIcon from "@mui/icons-material/Cancel";
import { kBloomWarning } from "../utils/colorUtils";
import { BoxWithIconAndText } from "../react_components/boxes";
import { useL10n } from "../react_components/l10nHooks";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { Markdown } from "../react_components/markdown";
export const SubscriptionStatus: React.FunctionComponent<{
    overrideSubscriptionExpiration?: string;
    minimalUI?: boolean;
}> = props => {
    const deprecatedBrandingsExpiryDateAsYYYYMMDD = useApiString(
        "settings/deprecatedBrandingsExpiryDate",
        "dbed-pending"
    );

    let expiryDateStringAsYYYYMMDD = useApiString(
        "settings/subscriptionExpiration",
        "pending"
    );
    if (props.overrideSubscriptionExpiration !== undefined) {
        expiryDateStringAsYYYYMMDD = props.overrideSubscriptionExpiration;
    }
    if (
        expiryDateStringAsYYYYMMDD === null ||
        expiryDateStringAsYYYYMMDD === undefined
    ) {
        expiryDateStringAsYYYYMMDD = ""; // avoid crashing later on when not set
    }
    let brandingProjectKey = useApiString(
        "settings/brandingProjectKey",
        "notyet"
    );
    if (props.minimalUI) brandingProjectKey = ""; // in the Settings Dialog context, the backend doesn't yet know what the user is clicking on, so it will give the wrong branding

    // a "deprecated" subscription is one that used to be eternal but is now being phased out
    const haveDeprecatedSubscription = expiryDateStringAsYYYYMMDD.startsWith(
        deprecatedBrandingsExpiryDateAsYYYYMMDD
    );

    // We have to fight javascript if we don't want timezones to
    // interfere.
    const dateParts = expiryDateStringAsYYYYMMDD.split("-");
    const localizedDateString = new Date(
        Number(dateParts[0]),
        Number(dateParts[1]) - 1,
        Number(dateParts[2])
    ).toLocaleDateString();

    const expiringSoonMessage = useL10n(
        "Your {0} subscription expires on {1}.",
        "SubscriptionStatus.ExpiringSoonMessage",
        "",
        brandingProjectKey,
        localizedDateString
    ).replace("  ", " "); // remove extra space
    const expiredMessage = useL10n(
        "Your {0} subscription expired on {1}.",
        "SubscriptionStatus.ExpiredMessage",
        "",
        brandingProjectKey,
        localizedDateString
    );
    const defaultStatusMessage = useL10n(
        "Using subscription: {0}. Expires {1}",
        "SubscriptionStatus.DefaultMessage",
        "",
        brandingProjectKey,
        localizedDateString
    );

    // don't show anything until we have this info
    if (expiryDateStringAsYYYYMMDD === "") return null;

    // if the license is deprecated, we want to show the warning right away. Otherwise,
    // we only want to show it if the expiration is within 2 months (approximately 60 days).
    const kDaysBeforeWarningForNormalExpiration = 60;

    const nowAsYYYYMMDD = new Date().toISOString().slice(0, 10);

    // no subscription
    if (expiryDateStringAsYYYYMMDD === "incomplete") return null;
    // else if it's already expired
    else if (nowAsYYYYMMDD >= expiryDateStringAsYYYYMMDD) {
        return (
            <ExpiringSubscriptionStatus
                expired
                message={expiredMessage}
            ></ExpiringSubscriptionStatus>
        );
    } else if (
        // it's a special deprecated case, so  want to show it right away so that people notice
        haveDeprecatedSubscription ||
        // or if it's expiring soon
        (expiryDateStringAsYYYYMMDD &&
            getDaysDifference(nowAsYYYYMMDD, expiryDateStringAsYYYYMMDD) <=
                kDaysBeforeWarningForNormalExpiration)
    ) {
        return (
            <ExpiringSubscriptionStatus
                message={expiringSoonMessage}
            ></ExpiringSubscriptionStatus>
        );
    }
    // in the Settings dialog, we don't want to echo the expiration date if it's not soon
    else if (props.minimalUI) return null;
    // in the collection tab, we show a subtle message
    else {
        return (
            <div
                css={css`
                    font-size: 12px;
                    color: ${kBloomBlue};
                    margin-top: 2px;
                    margin-bottom: 10px;
                `}
            >
                {defaultStatusMessage}
            </div>
        );
    }
};

const ExpiringSubscriptionStatus: React.FunctionComponent<{
    message: string;
    expired?: boolean;
}> = props => {
    return (
        <BoxWithIconAndText
            // NOTE: be careful not to optimize for only one of the two locations where this is used (Collection Tab and Enterprise settings)
            backgroundColor={kBloomWarning}
            color={"black"}
            borderColor={kBloomWarning}
            hasBorder={true}
            icon={props.expired ? <CancelIcon /> : <WarningIcon />}
        >
            <div>
                {props.message}
                <Markdown
                    l10nKey="SubscriptionStatus.RenewalMessage"
                    l10nParam0={"mailto://subscriptions@bloomlibrary.org"}
                    css={css`
                        p {
                            margin: 0; // markdown wraps everything in a p tag which adds a big margin we don't need
                        }
                    `}
                >
                    Please [contact us](%1) to renew.
                </Markdown>
            </div>
        </BoxWithIconAndText>
    );
};
function getDaysDifference(startAsYYYYMMDD: string, endAsYYYYMMDD: string) {
    // Explicitly parse the YYYY-MM-DD format to avoid browser/locale inconsistencies
    const startParts = startAsYYYYMMDD.split("-");
    const endParts = endAsYYYYMMDD.split("-");

    if (startParts.length !== 3 || endParts.length !== 3) {
        console.error("Date format error: expected YYYY-MM-DD format");
        return 0;
    }

    const startYear = parseInt(startParts[0], 10);
    const startMonth = parseInt(startParts[1], 10) - 1; // Months are 0-indexed in JavaScript Date
    const startDay = parseInt(startParts[2], 10);

    const endYear = parseInt(endParts[0], 10);
    const endMonth = parseInt(endParts[1], 10) - 1; // Months are 0-indexed in JavaScript Date
    const endDay = parseInt(endParts[2], 10);

    const startDate = new Date(startYear, startMonth, startDay);
    const endDate = new Date(endYear, endMonth, endDay);

    // Calculate difference in milliseconds and convert to days
    const diffInMs = endDate.getTime() - startDate.getTime();
    const diffInDays = Math.floor(diffInMs / (1000 * 60 * 60 * 24));

    return diffInDays;
}
