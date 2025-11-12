// a static registry of features. Each one has a name, a subscription tier, and a flag indicating whether it is experimental or advanced.
using System.Collections.Generic;

namespace Bloom.SubscriptionAndFeatures
{
    public enum FeatureName
    {
        BasicPage, // we don't really need this, it's just a basic feature and not experimental or anything. It's for test cases.
        DeleteBook, // we don't really need this, it's just a basic feature and not experimental or anything. It's for test cases.

        Canvas, // was Overlay
        Game, // This does not include HTML5 widgets
        Widget, //HTML5 Widget
        Spreadsheet,
        TeamCollection,
        ViewBookHistory, // Sort of tied to team collections now, but nothing says it has to be in the future...
        Motion,
        Music,
        FullPageCoverImage, // The whole front cover is one full-bleed image

        WholeTextBoxAudio,

        //PictureDictionary,
        //? CustomizableTemplates,
        ExportEPUB,
        ExportAudioVideo,
        PrintShopReady,
        BulkUpload,
        BulkBloomPub,
        Bookshelf,
    }

    public static class FeatureRegistry
    {
        public static string kNonWidgetGamePageXPath =
            "div[@data-tool-id='game' or contains(@class,'simple-comprehension-quiz') or @data-activity='simple-dom-choice']";

        //should match https://docs.google.com/document/d/1cfwC-ANSrujaIy5LBMA02qji-Ia7sbKXiUJFnh8hcuI

        public static readonly List<FeatureInfo> Features = new List<FeatureInfo>
        {
            // ----------------------------------------
            // Free Tier Features
            // ----------------------------------------
            new FeatureInfo
            {
                Feature = FeatureName.BasicPage,
                SubscriptionTier = SubscriptionTier.Basic,
            },
            new FeatureInfo
            {
                Feature = FeatureName.DeleteBook,
                SubscriptionTier = SubscriptionTier.Basic,
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
                PreventPublishingInDerivativeBooks = PreventionMethod.None,
                PreventPublishingInOriginalBooks = PreventionMethod.DisabledInUi,
                PreventPublishingInUnsupportedMediums = PreventionMethod.DisabledInUi,
            },
            new FeatureInfo
            {
                Feature = FeatureName.Music,
                SubscriptionTier = SubscriptionTier.Pro,
                ExistsInPageXPath =
                    "self::div[@data-backgroundaudio and string-length(@data-backgroundaudio)!=0]",
                PreventPublishingInDerivativeBooks = PreventionMethod.None,
                PreventPublishingInOriginalBooks = PreventionMethod.DisabledByModifyingDom,
                PreventPublishingInUnsupportedMediums = PreventionMethod.DisabledByModifyingDom,
                AttributesToRemoveToDisable = "data-backgroundaudio",
                L10NId = "PublishTab.Feature.Music",
            },
            new FeatureInfo
            {
                // This feature includes importing audio files, which would be for a whole text box.
                Feature = FeatureName.WholeTextBoxAudio,
                SubscriptionTier = SubscriptionTier.Pro,
                PreventPublishingInDerivativeBooks = PreventionMethod.None,
                PreventPublishingInOriginalBooks = PreventionMethod.None,
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
                SubscriptionTier = SubscriptionTier.Pro,
            },
            new FeatureInfo
            {
                Feature = FeatureName.ExportAudioVideo,
                SubscriptionTier = SubscriptionTier.Pro,
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
                // It's unusual to remove pages in derivatives, but (a) our feature matrix
                // does not say we allow this; they are not 'localize-only' like Games, and
                // (b) there isn't any way in Bloom to localize them.
                PreventPublishingInDerivativeBooks = PreventionMethod.Remove,
                PreventPublishingInOriginalBooks = PreventionMethod.Remove,
                PreventPublishingInUnsupportedMediums = PreventionMethod.Remove,
            },
            new FeatureInfo
            {
                Feature = FeatureName.Canvas,
                SubscriptionTier = SubscriptionTier.Pro,
                SupportedMediums = PublishingMediums.All,
                PreventPublishingInDerivativeBooks = PreventionMethod.None,
                PreventPublishingInOriginalBooks = PreventionMethod.Block,
                ExistsInPageXPath =
                    ".//div[contains(@class,'"
                    + Bloom.Book.HtmlDom.kCanvasElementClass
                    + "') and not(contains(@class,'"
                    + Bloom.Book.HtmlDom.kBackgroundImageClass
                    + "'))]",
            },
            new FeatureInfo
            {
                // Note, this refers to games which are NOT widgets, which is its own feature.
                Feature = FeatureName.Game,
                SubscriptionTier = SubscriptionTier.Pro,
                PreventPublishingInDerivativeBooks = PreventionMethod.None,
                PreventPublishingInOriginalBooks = PreventionMethod.Remove,
                ExistsInPageXPath = $"self::{kNonWidgetGamePageXPath}",
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
                PreventPublishingInUnsupportedMediums = PreventionMethod.Remove,
            },
            new FeatureInfo
            {
                Feature = FeatureName.FullPageCoverImage,
                SubscriptionTier = SubscriptionTier.Pro,
                ExistsInPageXPath = "self::div[contains(@class,'cover-is-image')]",
                // This disabling doesn't get a chance to work, because when we bring a book's XMatter
                // up to date, we update the cover-is-image class to something consistent with both
                // the AppearanceSettings.CoverIsImage and Subscription.HaveActiveSubscription.
                // (The latter should be changed to something involving this feature, but even then,
                // the class will be removed by the time we execute the code that processes these properties.)
                PreventPublishingInDerivativeBooks = PreventionMethod.DisabledByModifyingDom,
                PreventPublishingInOriginalBooks = PreventionMethod.DisabledByModifyingDom,
                ClassesToRemoveToDisable = "cover-is-image no-margin-page",
                L10NId = "BookSettings.CoverIsImage",
            },
            // ----------------------------------------
            // LocalCommunity Tier Features
            // ----------------------------------------
            new FeatureInfo
            {
                Feature = FeatureName.Spreadsheet,
                SubscriptionTier = SubscriptionTier.LocalCommunity,
            },
            new FeatureInfo
            {
                Feature = FeatureName.TeamCollection,
                SubscriptionTier = SubscriptionTier.LocalCommunity,
            },
            new FeatureInfo
            {
                Feature = FeatureName.ViewBookHistory,
                SubscriptionTier = SubscriptionTier.LocalCommunity,
            },
            // ----------------------------------------
            // Enterprise Tier Features
            // ----------------------------------------
            new FeatureInfo
            {
                Feature = FeatureName.PrintShopReady,
                SubscriptionTier = SubscriptionTier.Enterprise,
            },
            new FeatureInfo
            {
                Feature = FeatureName.BulkUpload,
                SubscriptionTier = SubscriptionTier.Enterprise,
            },
            new FeatureInfo
            {
                Feature = FeatureName.BulkBloomPub,
                SubscriptionTier = SubscriptionTier.Enterprise,
            },
            new FeatureInfo
            {
                Feature = FeatureName.Bookshelf,
                SubscriptionTier = SubscriptionTier.Enterprise,
            },
        };
    }
}
