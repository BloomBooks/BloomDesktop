// See also: the backend version of this in FeatureStatus.cs

import { post, useApiObject } from "../utils/bloomApi";
import { useL10n2 } from "./l10nHooks";

// Equivalent to C# SubscriptionTier enum
// export enum SubscriptionTier {
//     Basic = "Basic",
//     LocalCommunity = "Local Community",
//     Pro = "Pro",
//     Enterprise = "Enterprise"
// }

// Equivalent to C# FeatureNames enum
export enum FeatureName {
    Overlay = "Overlay",
    Game = "Game",
    Spreadsheet = "Spreadsheet",
    TeamCollection = "TeamCollection"
}

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

export function useGetFeatureStatus(
    featureName: string | undefined
): FeatureStatus | undefined {
    const featureStatus = useApiObject<FeatureStatus | undefined | null>(
        `features/status?featureName=${featureName}`,
        undefined,
        featureName ? undefined : null // just return null if we don't have a feature name (complexity because of rule of hooks)
    );

    return featureName ? featureStatus ?? undefined : undefined;
}

export function openBloomSubscriptionSettings() {
    post("common/showSettingsDialog?tab=subscription");
}

export function useGetFeatureTierMessage(
    featureStatus: FeatureStatus | undefined
): string {
    const message = useL10n2({
        english:
            'This feature requires a Bloom subscription tier of at least "{0}".',
        key: "Subscription.RequiredTierForFeatureSentence",
        params: [featureStatus?.localizedTier || ""]
    });

    return featureStatus ? message : "";
}
