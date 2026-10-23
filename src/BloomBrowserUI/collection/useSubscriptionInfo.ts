import { useEffect, useState } from "react";
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

// This hook supplies information about a subscription. Given a code, it describes that code, and
// asks again whenever the code changes; the Collection Settings dialogs use this for the code being
// typed. Without one, it describes the collection's subscription.
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

    // This effect fetches from Bloom: once, then again for each change of the code (each keystroke
    // or paste). An answer that arrives after a newer question was asked is dropped.
    useEffect(() => {
        let isCurrent = true;
        get(
            code === undefined
                ? "settings/subscription"
                : `settings/subscription?code=${encodeURIComponent(code)}`,
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
