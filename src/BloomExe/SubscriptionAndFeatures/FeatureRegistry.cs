// a static registry of features. Each one has a name, a subscription tier, and a flag indicating whether it is experimental or advanced.
using System.Collections.Generic;

namespace Bloom.SubscriptionAndFeatures
{
    public enum FeatureName
    {
        BasicPage, // we don't really need this, it's just a basic feature and not experimental or anything. It's for test cases.
        DeleteBook, // we don't really need this, it's just a basic feature and not experimental or anything. It's for test cases.

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
        PrintShopReady,
        BulkUpload,
        BulkBloomPub,
        Bookshelf
    }

    public static class FeatureRegistry
    {
        //should match https://docs.google.com/document/d/1cfwC-ANSrujaIy5LBMA02qji-Ia7sbKXiUJFnh8hcuI

        public static readonly List<FeatureInfo> Features = new List<FeatureInfo>
        {
            // ----------------------------------------
            // Free Tier Features
            // ----------------------------------------
            new FeatureInfo
            {
                Feature = FeatureName.BasicPage,
                SubscriptionTier = SubscriptionTier.Basic
            },
            new FeatureInfo
            {
                Feature = FeatureName.DeleteBook,
                SubscriptionTier = SubscriptionTier.Basic
            },
            // ----------------------------------------
            // Pro Tier Features
            // ----------------------------------------
            new FeatureInfo
            {
                Feature = FeatureName.Motion,
                SubscriptionTier = SubscriptionTier.Pro,
                ExistsInPageXPath = ".//div[contains(@class,'bloom-canvas') and @data-initial-rect]",
                SupportedMediums = PublishingMediums.BloomPub | PublishingMediums.Video,
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.Remove
            },
            new FeatureInfo
            {
                Feature = FeatureName.Music,
                SubscriptionTier = SubscriptionTier.Pro,
                ExistsInPageXPath = "self::div[@data-backgroundaudio and string-length(@data-backgroundaudio)!=0]",
            },
            //new FeatureInfo
            //{
            //    Feature = FeatureName.PictureDictionary,
            //    SubscriptionTier = SubscriptionTier.Pro
            //},
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
                ExistsInPageXPath = "self::div[contains(@class,'custom-widget-page')]",
            },
            new FeatureInfo
            {
                Feature = FeatureName.Overlay,
                SubscriptionTier = SubscriptionTier.Pro,
                SupportedMediums = PublishingMediums.All,
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.Block,
                ExistsInPageXPath =
                    ".//div[contains(@class,'" + Bloom.Book.HtmlDom.kCanvasElementClass + "') and not(contains(@class,'" + Bloom.Book.HtmlDom.kBackgroundImageClass + "'))]"
            },
            new FeatureInfo
            {
                Feature = FeatureName.Game,
                SubscriptionTier = SubscriptionTier.Pro,
                RestrictionInDerivativeBooks = PublicationRestrictions.Remove,
                RestrictionInOriginalBooks = PublicationRestrictions.Remove,
                ExistsInPageXPath = "self::div[@data-tool-id='game' or contains(@class,'simple-comprehension-quiz') or @data-activity='simple-dom-choice']"
			},
            // ----------------------------------------
            // LocalCommunity Tier Features
            // ----------------------------------------
            new FeatureInfo
            {
                Feature = FeatureName.Spreadsheet,
                SubscriptionTier = SubscriptionTier.LocalCommunity
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
                Feature = FeatureName.PrintShopReady,
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
            },
            new FeatureInfo
            {
                Feature = FeatureName.Bookshelf,
                SubscriptionTier = SubscriptionTier.Enterprise
            }
        };
    }
}
