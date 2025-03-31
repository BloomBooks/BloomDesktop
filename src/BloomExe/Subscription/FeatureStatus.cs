using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using L10NSharp;
using Newtonsoft.Json;
using static FeatureRegistry;
using static Subscription;

public class FeatureInfo
{
    public FeatureName Feature;
    public SubscriptionTier SubscriptionTier;
    internal string existsInPageXPath;
}

/// <summary>
/// Conveys the availability of a named feature, taking into account
/// the current subscription, experimental status, and advanced status flags.
/// </summary>
public class FeatureStatus
{
    public FeatureName FeatureName;
    public SubscriptionTier SubscriptionTier;
    public bool Enabled;
    public bool Visible;
    public string FirstPageNumber;

    // using a string (from typescript)
    public static FeatureStatus GetFeatureStatus(Subscription subscription, string featureName)
    {
        if (Enum.TryParse<FeatureName>(featureName, true, out FeatureName featureEnum))
        {
            return GetFeatureStatus(subscription, featureEnum);
        }
        Debug.Assert(false, $"Feature '{featureName}' not found in FeatureName enum.");
        return null;
    }

    // using an enum (from c#)
    public static FeatureStatus GetFeatureStatus(Subscription subscription, FeatureName featureName)
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

    /// <summary>
    ///  Get the json we want, then deserialize bck into c# so that when serialized, the json will be correct.
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
            englishText: SubscriptionTier.ToString().Replace("LocalCommunity", "Local Community") // show that we want a space
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
                f => !string.IsNullOrEmpty(f.existsInPageXPath)
            )
        )
        {
            var featureStatus = GetFeatureStatus(subscription, feature.Feature);
            if (!featureStatus.Enabled)
                disabledFeaturesToLookFor.Add(feature);
        }

        foreach (var pageNode in pageNodes)
        {
            // for each feature that is disabled and can be checked for in the DOM, check if it exists on the page.
            foreach (var feature in disabledFeaturesToLookFor)
            {
                if (pageNode.SelectSingleNode(feature.existsInPageXPath) != null)
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

    internal static void GetFeatureThatShouldBlockPublishing(
        Book currentSelection,
        out FeatureStatus featurePreventingPublishing
    )
    {
        throw new NotImplementedException();
    }
}
