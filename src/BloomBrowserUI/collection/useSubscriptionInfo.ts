import { useCallback, useEffect, useState } from "react";
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
    SubscriptionDescriptor: string;
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
        SubscriptionDescriptor: "",
        HaveBrandingFiles: false,
        EditingBlorgBook: false
    });
    const [haveData, setHaveData] = useState(false);

    // This is called once initially, then each time the user types in the subscription code field or does a paste
    const querySubscriptionInfo = useCallback(() => {
        get("settings/subscription", result => {
            setSubscriptionData(result.data);
            setHaveData(true);
        });
    }, [setSubscriptionData]);

    // refresh when the subscription code changes
    useEffect(() => {
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
    useEffect(() => {
        querySubscriptionInfo();
    }, [querySubscriptionInfo]);

    return {
        code: subscriptionData.Code,
        subscriptionCodeIntegrity: subscriptionData.CodeIntegrity as SubscriptionCodeIntegrity,
        expiryDateStringAsYYYYMMDD: subscriptionData.Expiration,
        subscriptionDescriptor: subscriptionData.SubscriptionDescriptor,
        subscriptionSummary: subscriptionData.Summary,
        haveBrandingFiles: subscriptionData.HaveBrandingFiles,
        editingBlorgBook: subscriptionData.EditingBlorgBook,
        haveData
    };
};
