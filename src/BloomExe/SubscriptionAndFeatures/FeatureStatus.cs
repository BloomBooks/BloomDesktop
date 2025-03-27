using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        BloomPub = 1 << 3,
        All = Epub | Video | PDF | BloomPub
    }

    public enum PublicationRestrictions
    {
        None,
        Block,
        Remove
    }

    public class FeatureInfo
    {
        public FeatureName Feature;
        public SubscriptionTier SubscriptionTier;

        // During publishing, some features have to be removed because the medium does not support them
        public PublishingMediums SupportedMediums = PublishingMediums.All;

        // During publishing, what happens if we encounter this feature already in the book
        // but we don't have an adequate subscription tier?
        public PublicationRestrictions RestrictionInDerivativeBooks;
        public PublicationRestrictions RestrictionInOriginalBooks;

        // Prior to 6.2b, some pages had `<div  class="bloom-page enterprise-only ...`
        // We were short-sighted, locating the subscription tier in the book itself.
        // Starting in 6.2b, we instead specify the feature that the page represents, e.g.:
        // `data-feature="overlay"`
        // This field is used to see if a page we are migrating in should get the `data-feature` attribute for this feature.
        public string MatchesLegacyPageXPath;

        // Some features are are tied not to the page itself, but to children of the page. To this point, we have detected these
        // by the existence of a css class or a data attribute. As of 6.2b, there are three examples: .bloom-canvas-element, .custom-widget-page, and data-activity.
        // These, too, could be converted to just explicitly specifying the `data-feature` (both in the templates and using forward migration).
        // Even if we go with migration to add data-activity, we will still need this value in order to detect the feature in the DOM.
        // That step has not been taken yet, so we still have this way detecting them.  <--- REVIEW
        internal string ExistsInPageXPath;
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
        public static FeatureStatus GetFeatureStatus(Subscription subscription, string featureName)
        {
            if (Enum.TryParse<FeatureName>(featureName, true, out FeatureName featureEnum))
            {
                return GetFeatureUseStatus(subscription, featureEnum);
            }
            Debug.Assert(false, $"Feature '{featureName}' not found in FeatureName enum.");
            return null;
        }

        // using an enum (from c#)
        public static FeatureStatus GetFeatureUseStatus(
            Subscription subscription,
            FeatureName featureName
        )
        {
            var tier = subscription.Tier;
            var feature = FeatureRegistry.Features.Find(f => f.Feature == featureName);
            Debug.Assert(feature != null, $"Feature '{featureName}' not found in registry.");
            return new FeatureStatus
            {
                FeatureName = feature.Feature,
                SubscriptionTier = feature.SubscriptionTier,
                Enabled = (int)tier >= (int)feature.SubscriptionTier,
                Visible = true // for now, we have not hooked up the advanced/experimental flags yet.
            };
        }

        public static bool GetShouldRemoveForPublishing(
            Subscription subscription,
            FeatureName featureName,
            PublishingMediums publishingFormat,
            bool bookIsDerivative
        )
        {
            // TODO BL-14587, use feature.RemoveFromFormats

            var featureStatus = GetFeatureUseStatus(subscription, featureName);
            return !featureStatus.Enabled;
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

        /// <summary>
        /// Gets the page number of the first page that prevents publishing due to insufficient subscription tier.
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

            foreach (
                var feature in FeatureRegistry.Features.Where(
                    f => !string.IsNullOrEmpty(f.ExistsInPageXPath)
                )
            )
            {
                var featureStatus = GetFeatureUseStatus(subscription, feature.Feature);
                if (!featureStatus.Enabled)
                    disabledFeaturesToLookFor.Add(feature);
            }

            foreach (var pageNode in pageNodes)
            {
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
