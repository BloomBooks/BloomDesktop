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
        ViewBookHistory, // Sort of tied to team collections now, but nothing says it has to be in the future...
        Motion,
        Music,

        CoverIsImage, // The whole front cover is one full-bleed image

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
                ExistsInPageXPath = "foobar", // TODO what is the way to know?
                SupportedMediums = PublishingMediums.BloomPub | PublishingMediums.Video,
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.Remove
            },
            new FeatureInfo
            {
                Feature = FeatureName.Music,
                SubscriptionTier = SubscriptionTier.Pro
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
                ExistsInPageXPath = ".//div[contains(@class,'custom-widget-page')]",
            },
            new FeatureInfo
            {
                Feature = FeatureName.Overlay,
                SubscriptionTier = SubscriptionTier.Pro,
                SupportedMediums = PublishingMediums.All,
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.Block,
                ExistsInPageXPath =
                    ".//div[contains(@class,'" + Bloom.Book.HtmlDom.kCanvasElementClass + "')]"
            },
            new FeatureInfo
            {
                Feature = FeatureName.Game,
                SubscriptionTier = SubscriptionTier.Pro,
                RestrictionInDerivativeBooks = PublicationRestrictions.Remove,
                RestrictionInOriginalBooks = PublicationRestrictions.Remove,
                ExistsInPageXPath = ".//div[contains(@data-activity,'game')]"
            },
            new FeatureInfo
            {
                Feature = FeatureName.CoverIsImage,
                SubscriptionTier = SubscriptionTier.Pro
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
            new FeatureInfo
            {
                Feature = FeatureName.ViewBookHistory,
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
