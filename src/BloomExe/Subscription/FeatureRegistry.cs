// a static registry of features. Each one has a name, a subscription tier, and a flag indicating whether it is experimental or advanced.
using System.Collections.Generic;
using static Subscription;

public static class FeatureRegistry
{
    public enum FeatureName
    {
        Overlay,
        Game,
        Spreadsheet,
        TeamCollection,
        MotionTool,
        MusicTool,
        PictureDictionary,
        CreateOverlayTool,
        CustomizableTemplates,
        EPUB,
        AudioVideo,
        DecodableReader,
        LeveledReader,
        PrintshopReadyFiles,
        BulkUpload,
    }

    public static readonly List<FeatureInfo> Features = new List<FeatureInfo>
    {
        // ----------------------------------------
        // Pro Tier Features
        // ----------------------------------------
        new FeatureInfo
        {
            Feature = FeatureName.MotionTool,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo
        {
            Feature = FeatureName.MusicTool,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo
        {
            Feature = FeatureName.PictureDictionary,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo
        {
            Feature = FeatureName.CreateOverlayTool,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo
        {
            Feature = FeatureName.Spreadsheet,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo
        {
            Feature = FeatureName.CustomizableTemplates,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo { Feature = FeatureName.EPUB, SubscriptionTier = SubscriptionTier.Pro },
        new FeatureInfo
        {
            Feature = FeatureName.AudioVideo,
            SubscriptionTier = SubscriptionTier.Pro
        },
        // ----------------------------------------
        // LocalCommunity Tier Features
        // ----------------------------------------
        new FeatureInfo
        {
            Feature = FeatureName.Overlay,
            SubscriptionTier = SubscriptionTier.LocalCommunity,
            existsInPageXPath =
                ".//div[contains(@class,'" + Bloom.Book.HtmlDom.kCanvasElementClass + "')]"
        },
        new FeatureInfo
        {
            Feature = FeatureName.Game,
            SubscriptionTier = SubscriptionTier.LocalCommunity,
            existsInPageXPath = ".//div[contains(@data-activity,'game')]"
        },
        new FeatureInfo
        {
            Feature = FeatureName.TeamCollection,
            SubscriptionTier = SubscriptionTier.LocalCommunity
        },
        // ----------------------------------------
        // Enterprise Tier Features
        // ----------------------------------------
        new FeatureInfo
        {
            Feature = FeatureName.DecodableReader,
            SubscriptionTier = SubscriptionTier.Enterprise
        },
        new FeatureInfo
        {
            Feature = FeatureName.LeveledReader,
            SubscriptionTier = SubscriptionTier.Enterprise
        },
        new FeatureInfo
        {
            Feature = FeatureName.PrintshopReadyFiles,
            SubscriptionTier = SubscriptionTier.Enterprise
        },
        new FeatureInfo
        {
            Feature = FeatureName.BulkUpload,
            SubscriptionTier = SubscriptionTier.Enterprise
        }
    };
}
