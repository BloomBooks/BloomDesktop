import { useCallback, useState } from "react";
import React = require("react");
import { get } from "../utils/bloomApi";

export type SubscriptionCodeIntegrity =
    | "none"
    | "invalid"
    | "incomplete"
    | "ok";

// Interface defining the structure of the data returned by the subscription endpoint
interface SubscriptionData {
    Code: string;
    Tier: string;
    Summary: string;
    Expiration: string;
    CodeIntegrity: string;
    BrandingKey: string;
    HaveBrandingFiles: boolean;
    EditingBlorgBook: boolean;
}

// This hook supplies information about the Subscription as it is in the Collection Settings Dialog.
// It automatically refreshes the values when it anything raises the "subscriptionCodeChanged" event.
export const useSubscriptionInfo = () => {
    const [subscriptionData, setSubscriptionData] = useState<SubscriptionData>({
        Code: "",
        Tier: "",
        Summary: "",
        Expiration: "",
        CodeIntegrity: "none",
        BrandingKey: "",
        HaveBrandingFiles: false,
        EditingBlorgBook: false
    });

    // This is called once initially, then each time the user types in the subscription code field or does a paste
    const querySubscriptionInfo = useCallback(() => {
        get("settings/Subscription", result => {
            setSubscriptionData(result.data);
        });
    }, [setSubscriptionData]);

    // refresh when the subscription code changes
    React.useEffect(() => {
        document.addEventListener(
            "subscriptionCodeChanged",
            querySubscriptionInfo
        );

        return () => {
            document.removeEventListener(
                "subscriptionCodeChanged",
                querySubscriptionInfo
            );
        };
    }, [querySubscriptionInfo]);

    // get initial info once at startup
    React.useEffect(() => {
        querySubscriptionInfo();
    }, [querySubscriptionInfo]);

    return {
        code: subscriptionData.Code,
        subscriptionCodeIntegrity: subscriptionData.CodeIntegrity as SubscriptionCodeIntegrity,
        expiryDateStringAsYYYYMMDD: subscriptionData.Expiration,
        brandingKey: subscriptionData.BrandingKey,
        subscriptionSummary: subscriptionData.Summary,
        haveBrandingFiles: subscriptionData.HaveBrandingFiles,
        editingBlorgBook: subscriptionData.EditingBlorgBook
    };
};
