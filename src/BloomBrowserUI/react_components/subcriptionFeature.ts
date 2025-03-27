import {
    useHaveSubscription,
    useGetSubscriptionTier
} from "./requiresSubscription";

const enabledFeatures = ["overlay"];
export function useGetFeatureStatus(
    subscriptionFeature: string | undefined
): { requiresSubscription: boolean; enabled: boolean; visible: boolean } {
    // return true if we have a subscription at all. Ignore the tier and feature id for now
    const haveSubscription = useHaveSubscription();
    return {
        requiresSubscription: !!subscriptionFeature,
        enabled:
            !subscriptionFeature ||
            enabledFeatures.includes(subscriptionFeature),
        visible: true // enhance: hide if experimental and not enabled
    };
}

/*     const subscriptionAvailable = useHaveSubscription();
    let subscriptionStatus = useGetSubscriptionTier();*/
