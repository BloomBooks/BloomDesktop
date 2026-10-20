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
// Given a code, it describes that code, and asks again whenever the code changes; the React
// Collection Settings dialog uses this for the code being typed, which C# does not know until OK.
// Without one, it describes the code the WinForms dialog is editing, and refreshes when anything
// raises the "subscriptionCodeChanged" event.
export const useSubscriptionInfo = (code?: string) => {
    const [subscriptionData, setSubscriptionData] = useState<SubscriptionData>({
        Code: "",
        Tier: "",
        Summary: "",
        Expiration: "",
        CodeIntegrity: "none",
        SubscriptionDescriptor: "",
        MissingBrandingFiles: false,
        EditingBlorgBook: false,
    });
    const [haveData, setHaveData] = useState(false);

    // This is called once initially, then each time the user types in the subscription code field or does a paste
    const querySubscriptionInfo = useCallback(() => {
        get("settings/subscription", (result) => {
            setSubscriptionData(result.data);
            setHaveData(true);
        });
    }, [setSubscriptionData]);

    const followsGivenCode = code !== undefined;

    // Describe the given code, if there is one. Each keystroke asks again, so an answer that
    // arrives after a newer question was asked is dropped.
    useEffect(() => {
        if (code === undefined) return;
        let isCurrent = true;
        get(
            `settings/subscription?code=${encodeURIComponent(code)}`,
            (result) => {
                if (!isCurrent) return;
                setSubscriptionData(result.data);
                setHaveData(true);
            },
        );
        return () => {
            isCurrent = false;
        };
    }, [code]);

    // refresh when the subscription code changes
    useEffect(() => {
        if (followsGivenCode) return;
        document.addEventListener(
            "subscriptionCodeChanged",
            querySubscriptionInfo,
        );

        return () => {
            document.removeEventListener(
                "subscriptionCodeChanged",
                querySubscriptionInfo,
            );
        };
    }, [querySubscriptionInfo, followsGivenCode]);

    // get initial info once at startup
    useEffect(() => {
        if (followsGivenCode) return;
        querySubscriptionInfo();
    }, [querySubscriptionInfo, followsGivenCode]);

    return {
        code: subscriptionData.Code,
        tier: subscriptionData.Tier,
        subscriptionCodeIntegrity:
            subscriptionData.CodeIntegrity as SubscriptionCodeIntegrity,
        expiryDateStringAsYYYYMMDD: subscriptionData.Expiration || "",
        subscriptionDescriptor: subscriptionData.SubscriptionDescriptor,
        subscriptionSummary: subscriptionData.Summary,
        missingBrandingFiles: subscriptionData.MissingBrandingFiles,
        editingBlorgBook: subscriptionData.EditingBlorgBook,
        haveData,
    };
};

// useLocalizedTier
export function useLocalizedTier(tier: string) {
    return useL10n2({
        english: tier,
        key: "Subscription.Tier." + tier,
    });
}
