// See also: the backend version of this in FeatureStatus.cs

import {
    useHaveSubscription,
    useGetSubscriptionTier
} from "./requiresSubscription";

import { post, useApiObject } from "../utils/bloomApi";

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

// Must match what the api sends us
export interface FeatureStatus {
    localizedFeature: string;
    localizedTier: string;
    enabled: boolean;
    visible: boolean;
    firstPageNumber?: string;
}

export function useGetFeatureStatus(
    featureName: string | undefined
): FeatureStatus | undefined {
    const featureStatus = useApiObject<FeatureStatus | undefined>(
        `features/status?featureName=${featureName}`,
        undefined
    );

    return featureStatus;
}

export function openBloomSubscriptionSettings() {
    post("common/showSettingsDialog?tab=subscription");
}
