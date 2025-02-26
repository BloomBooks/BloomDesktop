/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import { useApiString } from "../utils/bloomApi";
import * as React from "react";
import WarningIcon from "@mui/icons-material/Warning";
import CancelIcon from "@mui/icons-material/Cancel";
import { kBloomWarning } from "../utils/colorUtils";
import { Link } from "@mui/material";
import { BoxWithIconAndText } from "../react_components/boxes";
import { useL10n } from "../react_components/l10nHooks";
import { kBloomBlue } from "../bloomMaterialUITheme";
export const SubscriptionStatus: React.FunctionComponent<{
    overrideSubscriptionExpiration?: string;
    minimalUI?: boolean;
}> = props => {
    const deprecatedBrandingsExpiryDate = useApiString(
        "settings/deprecatedBrandingsExpiryDate",
        "dbed-pending"
    );

    let expiryDateString = useApiString(
        "settings/subscriptionExpiration",
        "pending"
    );
    if (props.overrideSubscriptionExpiration !== undefined) {
        expiryDateString = props.overrideSubscriptionExpiration;
    }
    let brandingProjectKey = useApiString(
        "settings/brandingProjectKey",
        "notyet"
    );
    if (props.minimalUI) brandingProjectKey = ""; // in the Settings Dialog context, the backend doesn't yet know what the user is clicking on, so it will give the wrong branding

    const expiryDate = new Date(expiryDateString);

    const formattedDate = expiryDate
        ? expiryDate.toLocaleDateString(undefined, { timeZone: "UTC" }) // UTC here prevents us showing this date as a day earlier depending on the user's timezone
        : "";

    // a "deprecated" subscription is one that used to be eternal but is now being phased out
    const haveDeprecatedSubscription = expiryDateString.startsWith(
        deprecatedBrandingsExpiryDate
    ); // just the year-month-day, ignore the time the time that follows it

    const expiringSoonMessage = useL10n(
        "Your {0} subscription expires on {1}.",
        "SubscriptionStatus.ExpiringSoonMessage",
        "",
        brandingProjectKey,
        formattedDate
    ).replace("  ", " "); // remove extra space
    const expiredMessage = useL10n(
        "Your {0} subscription expired on {1}.",
        "SubscriptionStatus.ExpiredMessage",
        "",
        brandingProjectKey,
        formattedDate
    );
    const defaultStatusMessage = useL10n(
        "Using subscription: {0}. Expires {1}",
        "SubscriptionStatus.DefaultMessage",
        "",
        brandingProjectKey,
        formattedDate
    );

    // don't show anything until we have this info
    if (expiryDateString === "") return null;

    const kMonthsBeforeWarning = 2; // changed from 3 to 2 months

    if (expiryDateString === "incomplete") return null;
    // no subscription
    // else if it's already expired
    else if (expiryDate.getTime() < Date.now()) {
        return (
            <ExpiringSubscriptionStatus
                expired
                message={expiredMessage}
            ></ExpiringSubscriptionStatus>
        );
    } else if (
        haveDeprecatedSubscription || // we want to show these right away so that people notice
        // use getMonthDifference
        getMonthDifference(new Date(), expiryDate) <= kMonthsBeforeWarning
    ) {
        return (
            <ExpiringSubscriptionStatus
                message={expiringSoonMessage}
            ></ExpiringSubscriptionStatus>
        );
    } else if (props.minimalUI) return null;
    // in the Settings dialog, we don't want to echo the expiration date if it's not soon
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
            // NOTE: be careful not to optimize for one of the two locations where this is used (Collection Tab and Enterprise settings)
            backgroundColor={kBloomWarning}
            color={"black"}
            borderColor={kBloomWarning}
            hasBorder={true}
            icon={props.expired ? <CancelIcon /> : <WarningIcon />}
        >
            <div>
                {props.message}
                <br />
                Please{" "}
                <Link
                    href="mailto:subscriptions@bloomlibrary.org"
                    sx={{
                        textDecorationColor: "black",
                        "&:hover": {
                            textDecorationColor: "black"
                        }
                    }}
                >
                    contact us
                </Link>{" "}
                to renew.
            </div>
        </BoxWithIconAndText>
    );
};
function getMonthDifference(startDate, endDate) {
    const yearsDifference = endDate.getFullYear() - startDate.getFullYear();
    const monthsDifference = endDate.getMonth() - startDate.getMonth();

    // Calculate the total difference in months
    const totalMonthsDifference = yearsDifference * 12 + monthsDifference;

    return totalMonthsDifference;
}
