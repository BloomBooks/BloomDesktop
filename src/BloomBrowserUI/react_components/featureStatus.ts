// See also: the backend version of this in FeatureStatus.cs

import { useMemo } from "react";
import { post, useApiObject, get } from "../utils/bloomApi";
import { useL10n2 } from "./l10nHooks";

// NB: this must match c# enum SubscriptionTier in Subscription.cs
export type SubscriptionTier =
    | "Basic"
    | "LocalCommunity"
    | "Pro"
    | "Enterprise";

// Must match what the api sends us
export interface FeatureStatus {
    localizedFeature: string;
    localizedTier: string;
    subscriptionTier: SubscriptionTier;
    enabled: boolean;
    visible: boolean;
    firstPageNumber?: string;
}

/**
 * Non-React version of useGetFeatureStatus for use in non-React contexts.
 * Returns a Promise that resolves to the FeatureStatus or undefined.
 */
export async function getFeatureStatusAsync(
    featureName: string | undefined
): Promise<FeatureStatus | undefined> {
    if (!featureName) return undefined;

    return new Promise<FeatureStatus | undefined>(resolve => {
        get(`features/status?featureName=${featureName}`, result => {
            resolve(result.data);
        });
    });
}

export function useGetFeatureStatus(
    featureName: string | undefined
): FeatureStatus | undefined {
    const featureStatus = useApiObject<FeatureStatus | undefined>(
        `features/status?featureName=${featureName}`,
        undefined,
        !featureName // skip the query if we don't have a feature name
    );

    return featureName ? featureStatus : undefined;
}

export function openBloomSubscriptionSettings() {
    post("common/showSettingsDialog?tab=subscription");
}

// Use this when you just want a minimal label type message, not a fully informative sentence.
export function useGetFeatureAvailabilityMessage(
    featureStatus: FeatureStatus | undefined
): string {
    // Get localized strings first
    const params = useMemo(() => [featureStatus?.localizedTier || ""], [
        featureStatus?.localizedTier
    ]);
    const featureNotInTierMessage = useL10n2({
        english:
            'This feature requires a Bloom subscription tier of at least "{0}".',
        key: "Subscription.RequiredTierForFeatureSentence",
        params: params
    });
    const featureEnabledMessage = useL10n2({
        english: "This feature is included in your {0} subscription.",
        key: "Subscription.FeatureIsIncludedSentence",
        params: params
    });

    // Calculate and return the message directly
    if (!featureStatus) {
        return "";
    }

    if (featureStatus.enabled) {
        return featureEnabledMessage;
    } else {
        return featureNotInTierMessage;
    }
}
