using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.SubscriptionAndFeatures
{
    [Flags]
    public enum PublishingMediums
    {
        None = 0,
        Epub = 1 << 0,
        Video = 1 << 1,
        PDF = 1 << 2,

        // This is also used for Web publishing. Neither medium causes any
        // pages to be removed, and this seems unlikely to change.
        BloomPub = 1 << 3,
        All = Epub | Video | PDF | BloomPub,
    }

    /// <summary>
    /// Ways of preventing a feature being used (currently, specifically from being published) in a book.
    /// </summary>
    public enum PreventionMethod
    {
        // We will publish the feature if it is there, even though we don't allow creating it
        // (e.g., some tiers don't allow recording audio at the text block level, but if we have
        // such recordings in a book we will just publish them. Also, books with canvas elements
        // may be published in any tier in derivatives.)
        None,

        // The book may not be published at all if it has this feature
        // (e.g., canvas elements in non-derivative books).
        // Note: Remove trumps Block; for example, if the only canvas elements in a book
        // are on Game pages, that does not block publishing.
        Block,

        // Pages that contain this feature will be removed from the book.
        // (Detected by ExistsInPageXPath matches something in the page).
        // e.g., Game pages
        Remove,

        // The publication will be created, but will not have the behavior connected with the
        // feature. For example, if the tier does not support it, the control for publishing
        // as a motion book will be disabled. This is implemented by the publication UI control
        // knowing that the feature is disabled, so there is nothing to do in the publication
        // code; the enumeration member functions like "None" and is only distinguished
        // for the sake of documentation.
        DisabledInUi,

        // The book can be published complete with whatever pages use the feature,
        // but the feature will be prevented from working by the publication code changing the DOM.
        // The feature must have ExistsInPageXPath so we can identify pages that need this
        // treatment, and at least one of ClassesToRemoveToDisable or AttributesToRemoveToDisable.
        // Enhance: we may at some point identify other ways of specifying ways to disable features.
        DisabledByModifyingDom,
    }

    public class FeatureInfo
    {
        public FeatureName Feature;
        public SubscriptionTier SubscriptionTier;

        // During publishing, some features have to be removed because the medium does not support them
        public PublishingMediums SupportedMediums = PublishingMediums.All;

        // During publishing, what happens if we encounter this feature already in the book
        // but we don't have an adequate subscription tier?
        public PreventionMethod PreventPublishingInDerivativeBooks;
        public PreventionMethod PreventPublishingInOriginalBooks;

        // And what happens if we're publishing in a medium that does not support this feature?
        // Note, currently only Remove and DisabledByModifyingDom are directly supported by the FeatureStatus code;
        // other features may be disabled in the UI, or by higher-level code that blocks publishing the book entirely.
        public PreventionMethod PreventPublishingInUnsupportedMediums;

        // An xpath that can be used to determine whether the feature exists in the page.
        // This is required at least for features that have any Remove or DisabledByModifyingDom method.
        internal string ExistsInPageXPath;

        public string ClassesToRemoveToDisable;
        public string AttributesToRemoveToDisable;

        // Where needed, an ID for getting a localized string to describe the feature.
        // Currently needed for features that use DisabledByModifyingDom
        public string L10NId;
    }

    /// <summary>
    /// Conveys the availability of a named feature, taking into account
    /// the current subscription, experimental status, and advanced status flags.
    /// </summary>
    public class FeatureStatus
    {
        public FeatureName FeatureName;

        // minimum tier required to use this feature, not the tier of the subscription
        public SubscriptionTier SubscriptionTier;
        public bool Enabled;
        public bool Visible;
        public string FirstPageNumber;

        // using a string (from typescript)
        public static FeatureStatus GetFeatureStatus(
            Subscription subscription,
            string featureName,
            Book.Book book = null,
            bool forPublishing = false
        )
        {
            if (Enum.TryParse<FeatureName>(featureName, true, out FeatureName featureEnum))
            {
                return GetFeatureStatus(subscription, featureEnum, book, forPublishing);
            }
            Debug.Assert(false, $"Feature '{featureName}' not found in FeatureName enum.");
            return null;
        }

        // using an enum (from c#)
        public static FeatureStatus GetFeatureStatus(
            Subscription subscription,
            FeatureName featureName,
            Book.Book book = null,
            bool forPublishing = false
        )
        {
            var feature = FeatureRegistry.Features.Find(f => f.Feature == featureName);
            Debug.Assert(feature != null, $"Feature '{featureName}' not found in registry.");
            return GetFeatureStatus(subscription, feature, book, forPublishing);
        }

        // if we already have a FeatureInfo (e.g., from iterating the registry)
        public static FeatureStatus GetFeatureStatus(
            Subscription subscription,
            FeatureInfo feature,
            Book.Book book = null,
            bool forPublishing = false
        )
        {
            var tier = subscription.Tier;
            if (book != null && book.IsPlayground && feature.Feature != FeatureName.TeamCollection)
                tier = SubscriptionTier.Enterprise;
            var enabled = (int)tier >= (int)feature.SubscriptionTier;
            if (!enabled && forPublishing && book != null)
            {
                // for enabling certain publishing controls (currently motion book behavior),
                // we have a special case for derivatives.
                if (book.BookData.BookIsDerivative())
                {
                    enabled = feature.PreventPublishingInDerivativeBooks == PreventionMethod.None;
                }
                else
                {
                    enabled = feature.PreventPublishingInOriginalBooks == PreventionMethod.None;
                }
            }

            return new FeatureStatus
            {
                FeatureName = feature.Feature,
                SubscriptionTier = feature.SubscriptionTier,
                Enabled = enabled,
                Visible = true, // for now, we have not hooked up the advanced/experimental flags yet.
            };
        }

        // Get the features that should be disabled for the given subscription using the specified method
        // (given that the book is or is not a derivative)
        public static IEnumerable<FeatureInfo> GetFeaturesToDisableUsingMethod(
            Subscription subscription,
            bool forDerivative,
            PreventionMethod method,
            Book.Book book = null
        )
        {
            return FeatureRegistry.Features.Where(feature =>
            {
                var featureStatus = GetFeatureStatus(subscription, feature, book);
                // which property of the feature should we look at to decide
                // whether it uses this method of disabling?
                var methodToMatch = forDerivative
                    ? feature.PreventPublishingInDerivativeBooks
                    : feature.PreventPublishingInOriginalBooks;
                // It's interesting if it does in fact need to be disabled, and if the
                // way we want to disable it matches the one the caller is looking for.
                return !featureStatus.Enabled && method == methodToMatch;
            });
        }

        public enum ReasonForRemoval
        {
            None,
            InsufficientSubscription,
            UnsupportedMedium,
        }

        public static ReasonForRemoval ShouldPageBeRemovedForPublishing(
            SafeXmlElement page,
            Subscription subscription,
            bool bookIsDerivative,
            PublishingMediums medium // should normally only be one of them
        )
        {
            if (
                PageMatchesAnyFeature(
                    page,
                    GetFeaturesToDisableUsingMethod(
                        subscription,
                        bookIsDerivative,
                        PreventionMethod.Remove
                    )
                )
            )
            {
                return ReasonForRemoval.InsufficientSubscription;
            }

            if (
                PageMatchesAnyFeature(
                    page,
                    FeatureRegistry.Features.Where(f =>
                        f.PreventPublishingInUnsupportedMediums == PreventionMethod.Remove
                        && (f.SupportedMediums & medium) == 0
                    )
                )
            )
            {
                return ReasonForRemoval.UnsupportedMedium;
            }

            return ReasonForRemoval.None;
        }

        /// <summary>
        ///  Get the json we want, then deserialize back into c# so that when serialized, the json will be correct.
        /// </summary>
        /// <returns></returns>
        public object ForSerialization()
        {
            return JsonConvert.DeserializeObject(this.ToJson() ?? "null");
        }

        /// <summary>
        /// return a json string of the FeatureStatus, using strings instead of enums and camel case properties as is normal in json/typescript.
        /// </summary>
        public string ToJson()
        {
            // localize (and in the process, convert from camel case to spaces)
            var localizedTier = LocalizationManager.GetDynamicString(
                appId: "Bloom",
                id: "Subscription.Tier." + SubscriptionTier.ToString(),
                englishText: SubscriptionTier
                    .ToString()
                    .Replace("LocalCommunity", "Local Community") // show that we want a space
            );
            var localizedFeature = LocalizationManager.GetDynamicString(
                appId: "Bloom",
                id: "Feature." + FeatureName.ToString(),
                englishText: FeatureName.ToString()
            );
            return $"{{\"localizedFeature\":\"{localizedFeature}\",\"localizedTier\":\"{localizedTier}\",\"subscriptionTier\":\"{SubscriptionTier}\",\"enabled\":{Enabled.ToString().ToLower()},\"visible\":{Visible.ToString().ToLower()},\"firstPageNumber\":\"{FirstPageNumber}\"}}";
        }

        private static bool PageMatchesAnyFeature(
            SafeXmlNode pageNode,
            IEnumerable<FeatureInfo> features
        )
        {
            return features.Any(feature =>
                pageNode.SelectSingleNode(feature.ExistsInPageXPath) != null
            );
        }

        /// <summary>
        /// Gets the page number of the first page that prevents publishing due to insufficient subscription tier.
        /// (Must be a feature that completely blocks publishing, not just removes a page or disables a feature.)
        /// </summary>
        public static FeatureStatus GetFirstFeatureThatIsInvalidForNewBooks(
            Subscription subscription,
            SafeXmlDocument dom
        )
        {
            var pageNodes = dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]");
            if (pageNodes.Length == 0)
                return null;

            var disabledFeaturesToLookFor = new List<FeatureInfo>();
            var removableFeatures = FeatureRegistry
                .Features.Where(f => f.PreventPublishingInOriginalBooks == PreventionMethod.Remove)
                .ToList();

            foreach (
                var feature in FeatureRegistry.Features.Where(
                    // None obviously doesn't restrict anything; Remove gets rid of individual pages but doesn't completely forbid;
                    // The Disabled ones allow publishing but without the feature. So it's only Block ones that mustn't exist at all.
                    f =>
                    f.PreventPublishingInOriginalBooks == PreventionMethod.Block
                    && !string.IsNullOrEmpty(f.ExistsInPageXPath)
                )
            )
            {
                var featureStatus = GetFeatureStatus(subscription, feature.Feature);
                if (!featureStatus.Enabled)
                    disabledFeaturesToLookFor.Add(feature);
            }

            foreach (var pageNode in pageNodes)
            {
                // If the page will be removed, we don't care if it has a feature that would otherwise block publishing.
                if (PageMatchesAnyFeature(pageNode, removableFeatures))
                    continue;
                // for each feature that is disabled and can be checked for in the DOM, check if it exists on the page.
                foreach (var feature in disabledFeaturesToLookFor)
                {
                    if (pageNode.SelectSingleNode(feature.ExistsInPageXPath) != null)
                    {
                        var pageNumberAttribute = pageNode.GetAttribute("data-page-number");
                        if (!string.IsNullOrEmpty(pageNumberAttribute))
                        {
                            return new FeatureStatus
                            {
                                FeatureName = feature.Feature,
                                SubscriptionTier = feature.SubscriptionTier,
                                Enabled = false,
                                Visible = true,
                                FirstPageNumber = pageNumberAttribute,
                            };
                        }
                    }
                }
            }
            return null; // no pages found with disabled features
        }
    }
}
