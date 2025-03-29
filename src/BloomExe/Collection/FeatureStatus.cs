using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using L10NSharp;

// tiers are ordered, so if you have a higher tier, you can use the features of the lower tiers.
public enum SubscriptionTier
{
    Basic = 0,
    LocalCommunity = 1,
    Pro = 2,
    Enterprise = 3,
}

internal class FeatureInfo
{
    public FeatureNames Feature;
    public SubscriptionTier SubscriptionTier;
    internal string existsInPageXPath;
}

public enum FeatureNames
{
    Overlay,
    Game,
    Spreadsheet,
    TeamCollection,
}

// a static registry of features. Each one has a name, a subscription tier, and a flag indicating whether it is experimental or advanced.
internal static class FeatureRegistry
{
    public static readonly List<FeatureInfo> Features = new List<FeatureInfo>
    {
        new FeatureInfo
        {
            Feature = FeatureNames.Overlay,
            SubscriptionTier = SubscriptionTier.LocalCommunity,
            existsInPageXPath = ".//div[contains(@class,'" + HtmlDom.kCanvasElementClass + "')]"
        },
        new FeatureInfo
        {
            Feature = FeatureNames.Game,
            SubscriptionTier = SubscriptionTier.LocalCommunity,
            // TODO: is this right? Probably need to restrict to just the new games.
            existsInPageXPath = ".//div[contains(@data-activity,'game')]"
        },
        new FeatureInfo
        {
            Feature = FeatureNames.Spreadsheet,
            SubscriptionTier = SubscriptionTier.Pro,
        },
        new FeatureInfo
        {
            Feature = FeatureNames.TeamCollection,
            SubscriptionTier = SubscriptionTier.Enterprise,
        },
    };
}

/// <summary>
/// Conveys the availability of a named feature, taking into account
/// the current subscription, experimental status, and advanced status flags.
/// </summary>
public class FeatureStatus
{
    public FeatureNames Feature;
    public SubscriptionTier SubscriptionTier;
    public bool Enabled;
    public bool Visible;
    public string FirstPageNumber;

    public static FeatureStatus GetFeatureStatus(
        Subscription subscription,
        FeatureNames featureName
    )
    {
        var tier = subscription.Tier;
        var feature = FeatureRegistry.Features.Find(f => f.Feature == featureName);
        Debug.Assert(feature != null, $"Feature {featureName} not found in registry.");
        return new FeatureStatus
        {
            Feature = feature.Feature,
            SubscriptionTier = feature.SubscriptionTier,
            Enabled = (int)tier >= (int)feature.SubscriptionTier,
            Visible = true // for now, we have not hooked up the advanced/experimental flags yet.
        };
    }

    /// <summary>
    /// return a json string of the FeatureStatus, using strings instead of enums
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
            id: "Feature." + Feature.ToString(),
            englishText: Feature.ToString()
        );
        return $"{{\"localizedFeature\":\"{localizedFeature}\",\"localizedTier\":\"{localizedTier}\",\"enabled\":{Enabled.ToString().ToLower()},\"visible\":{Visible.ToString().ToLower()},\"firstPageNumber\":\"{FirstPageNumber}\"}}";
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
                            Feature = feature.Feature,
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
