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
                ExistsInPageXPath =
                    ".//div[contains(@class,'bloom-canvas') and @data-initial-rect]",
                // PDFs can't move, and EPUB players won't know how to do it.
                SupportedMediums = PublishingMediums.BloomPub | PublishingMediums.Video,
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.DisabledInUi,
                RestrictionInUnsupportedMediums = PublicationRestrictions.DisabledInUi
            },
            new FeatureInfo
            {
                Feature = FeatureName.Music,
                SubscriptionTier = SubscriptionTier.Pro,
                ExistsInPageXPath =
                    "self::div[@data-backgroundaudio and string-length(@data-backgroundaudio)!=0]",
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.DisabledByModifyingDom,
                RestrictionInUnsupportedMediums = PublicationRestrictions.DisabledByModifyingDom
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
                // Do we want to remove these in PDFs? I think so...no knowing whether the
                // widget will be any use printed. Don't think they would work in epubs,
                // but haven't tested that. Definitely not in videos...no opportunity
                // to interact with them.
                SupportedMediums = PublishingMediums.BloomPub,
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.Remove,
                RestrictionInUnsupportedMediums = PublicationRestrictions.Remove,
            },
            new FeatureInfo
            {
                Feature = FeatureName.Overlay,
                SubscriptionTier = SubscriptionTier.Pro,
                SupportedMediums = PublishingMediums.All,
                RestrictionInDerivativeBooks = PublicationRestrictions.None,
                RestrictionInOriginalBooks = PublicationRestrictions.Block,
                ExistsInPageXPath =
                    ".//div[contains(@class,'"
                    + Bloom.Book.HtmlDom.kCanvasElementClass
                    + "') and not(contains(@class,'"
                    + Bloom.Book.HtmlDom.kBackgroundImageClass
                    + "'))]"
            },
            new FeatureInfo
            {
                Feature = FeatureName.Game,
                SubscriptionTier = SubscriptionTier.Pro,
                RestrictionInDerivativeBooks = PublicationRestrictions.Remove,
                RestrictionInOriginalBooks = PublicationRestrictions.Remove,
                ExistsInPageXPath =
                    "self::div[@data-tool-id='game' or contains(@class,'simple-comprehension-quiz') or @data-activity='simple-dom-choice']",
                // Many of our games can potentially be played on paper, though we think it's unlikely
                // that most of our target audience will allow writing in books. PDFs may also be used in checking
                // processes that need all the content. We even made a black-and-white theme specially for PDFs.
                // So for now we're allowing game pages in PDFs. Note that in many cases, if we expect the game
                // to actually be played using the PDF, we need to add code to shuffle the answers before making the PDF.
                // Do we need a way to remove games that don't work at all on paper? UI to decide whether to include
                // any games in a PDF? UI to control it for individual pages?
                // OTOH, we're sure Games don't make sense in a Video publication, where there is no opportunity to play them,
                // or in an EPUB, where the player will not have the code that makes them work.
                SupportedMediums = PublishingMediums.BloomPub | PublishingMediums.PDF,
                RestrictionInUnsupportedMediums = PublicationRestrictions.Remove,
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
