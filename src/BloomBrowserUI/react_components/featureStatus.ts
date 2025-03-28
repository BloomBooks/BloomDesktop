// See also: the backend version of this in FeatureStatus.cs

import {
    useHaveSubscription,
    useGetSubscriptionTier
} from "./requiresSubscription";

import { useApiObject } from "../utils/bloomApi";

// Equivalent to C# SubscriptionTier enum
export enum SubscriptionTier {
    Basic = "Basic",
    LocalCommunity = "Local Community",
    Pro = "Pro",
    Enterprise = "Enterprise"
}

// Equivalent to C# FeatureNames enum
export enum FeatureNames {
    Overlay = "Overlay",
    Game = "Game",
    Spreadsheet = "Spreadsheet",
    TeamCollection = "TeamCollection"
}

// Must match C# FeatureStatus class, including the uppercase property names
export interface FeatureStatus {
    Feature: FeatureNames;
    SubscriptionTier: SubscriptionTier;
    Enabled: boolean;
    Visible: boolean;
    FirstPageNumber?: string;
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
