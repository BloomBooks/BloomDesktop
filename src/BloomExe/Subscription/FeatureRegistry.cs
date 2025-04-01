// a static registry of features. Each one has a name, a subscription tier, and a flag indicating whether it is experimental or advanced.
using System.Collections.Generic;
using static Subscription;

public static class FeatureRegistry
{
    public enum FeatureName
    {
        Overlay,
        Game,
        Widget, //HTML5 Widget
        Spreadsheet,
        TeamCollection,
        Motion,
        Music,

        //PictureDictionary,
        //? CustomizableTemplates,
        ExportEPUB,
        ExportAudioVideo,
        DecodableReader,
        LeveledReader,
        PrintshopReady,
        BulkUpload,
        BulkBloomPub
    }

    public static readonly List<FeatureInfo> Features = new List<FeatureInfo>
    {
        // ----------------------------------------
        // Pro Tier Features
        // ----------------------------------------
        new FeatureInfo
        {
            Feature = FeatureName.Motion,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo { Feature = FeatureName.Music, SubscriptionTier = SubscriptionTier.Pro },
        //new FeatureInfo
        //{
        //    Feature = FeatureName.PictureDictionary,
        //    SubscriptionTier = SubscriptionTier.Pro
        //},
        new FeatureInfo
        {
            Feature = FeatureName.Spreadsheet,
            SubscriptionTier = SubscriptionTier.Pro
        },
        //new FeatureInfo
        //{
        //    Feature = FeatureName.CustomizableTemplates,
        //    SubscriptionTier = SubscriptionTier.Pro
        //},
        new FeatureInfo
        {
            Feature = FeatureName.ExportEPUB,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo
        {
            Feature = FeatureName.ExportAudioVideo,
            SubscriptionTier = SubscriptionTier.Pro
        },
        new FeatureInfo
        {
            // HTML5 Widgets
            Feature = FeatureName.Widget,
            SubscriptionTier = SubscriptionTier.Pro,
            existsInPageXPath = ".//div[contains(@class,'custom-widget-page')]"
        },
        // ----------------------------------------
        // LocalCommunity Tier Features
        // ----------------------------------------
        new FeatureInfo
        {
            Feature = FeatureName.Overlay,
            SubscriptionTier = SubscriptionTier.Pro,
            existsInPageXPath =
                ".//div[contains(@class,'" + Bloom.Book.HtmlDom.kCanvasElementClass + "')]"
        },
        new FeatureInfo
        {
            Feature = FeatureName.Game,
            SubscriptionTier = SubscriptionTier.Pro,
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
            Feature = FeatureName.PrintshopReady,
            SubscriptionTier = SubscriptionTier.Enterprise
        },
        new FeatureInfo
        {
            Feature = FeatureName.BulkUpload,
            SubscriptionTier = SubscriptionTier.Enterprise
        },
        new FeatureInfo
        {
            Feature = FeatureName.BulkBloomPub,
            SubscriptionTier = SubscriptionTier.Enterprise
        }
    };
}
