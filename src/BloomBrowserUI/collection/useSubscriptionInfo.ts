import { useCallback, useEffect, useState } from "react";
import { get } from "../utils/bloomApi";
import { useL10n2 } from "../react_components/l10nHooks";

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
    MissingBrandingFiles: boolean;
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
        MissingBrandingFiles: false,
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
        tier: subscriptionData.Tier,
        subscriptionCodeIntegrity: subscriptionData.CodeIntegrity as SubscriptionCodeIntegrity,
        expiryDateStringAsYYYYMMDD: subscriptionData.Expiration || "",
        subscriptionDescriptor: subscriptionData.SubscriptionDescriptor,
        subscriptionSummary: subscriptionData.Summary,
        missingBrandingFiles: subscriptionData.MissingBrandingFiles,
        editingBlorgBook: subscriptionData.EditingBlorgBook,
        haveData
    };
};

// useLocalizedTier
export function useLocalizedTier(tier: string) {
    return useL10n2({
        english: tier,
        key: "Subscription.Tier." + tier
    });
}
