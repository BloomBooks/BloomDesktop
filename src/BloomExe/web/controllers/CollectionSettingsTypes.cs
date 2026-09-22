using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The reply to GET collection/settings. The TypeScript interface ICollectionSettingsResponse
    /// in src/BloomBrowserUI/collection/collectionSettingsTypes.ts must match these classes; they
    /// are serialized with camelCase names.
    /// </summary>
    public class CollectionSettingsResponse
    {
        public CollectionSettingsValues Values;
        public CollectionSettingsContext Context;

        /// <summary>
        /// Dotted paths into Values whose change means Bloom has to restart, so the dialog can
        /// relabel its OK button and show the restart reminder.
        /// </summary>
        public string[] RestartPaths;
    }

    /// <summary>
    /// The editable settings, grouped by the page of the dialog that shows them. The subscription,
    /// the team collection administrators and the Bloom Library bookshelf have endpoints of their
    /// own and so are not here.
    /// </summary>
    public class CollectionSettingsValues
    {
        public LanguagesValues Languages;
        public FrontBackMatterValues FrontBackMatter;
        public AdvancedValues Advanced;

        /// <summary>
        /// Keyed by the tokens in ExperimentalFeatures, one entry per feature the dialog offers.
        /// </summary>
        public Dictionary<string, bool> Experimental;

        /// <summary>
        /// The dotted paths whose change means Bloom has to restart. These are the changes that
        /// make the WinForms dialog call ChangeThatRequiresRestart.
        /// </summary>
        public static string[] GetRestartPaths()
        {
            var paths = new List<string>();
            foreach (var language in new[] { "language1", "language2", "language3" })
            {
                paths.Add($"languages.{language}.tag");
                paths.Add($"languages.{language}.name");
                paths.Add($"languages.{language}.fontName");
                paths.Add($"languages.{language}.isRightToLeft");
            }
            // A sign language has no font and no text direction.
            paths.Add("languages.signLanguage.tag");
            paths.Add("languages.signLanguage.name");
            paths.Add("frontBackMatter.xmatter");
            paths.Add("frontBackMatter.pageNumberStyle");
            // The QR code settings do not really need anything as drastic as a restart, but the
            // badge in every book has to be rebuilt somehow.
            paths.Add("frontBackMatter.showQrCode");
            paths.Add("frontBackMatter.qrcodeCaption");
            paths.Add("advanced.collectionName");
            paths.Add("experimental." + ExperimentalFeatures.kTeamCollections);
            return paths.ToArray();
        }

        /// <summary>
        /// Whether the user changed any of the settings that need a restart.
        /// </summary>
        public static bool AnyRestartPathChanged(
            CollectionSettingsValues before,
            CollectionSettingsValues after
        )
        {
            // Looking the paths up in the serialized form resolves them by the same camelCase
            // names the TypeScript side walks.
            var beforeJson = ToJson(before);
            var afterJson = ToJson(after);
            foreach (var path in GetRestartPaths())
            {
                if (!JToken.DeepEquals(beforeJson.SelectToken(path), afterJson.SelectToken(path)))
                    return true;
            }
            return false;
        }

        private static JObject ToJson(CollectionSettingsValues values)
        {
            return JObject.Parse(
                JsonConvert.SerializeObject(values, CollectionSettingsApi.kCamelCaseSettings)
            );
        }
    }

    public class LanguagesValues
    {
        public LanguageValues Language1;
        public LanguageValues Language2;

        /// <summary>
        /// Null when the collection has no third language. A dialog that wants to remove the
        /// third language posts it back with an empty tag rather than leaving it out.
        /// </summary>
        public LanguageValues Language3;
        public SignLanguageValues SignLanguage;
    }

    public class LanguageValues
    {
        public string Tag;
        public string Name;
        public bool IsCustomName;
        public string FontName;
        public bool IsRightToLeft;
        public decimal LineHeight;
        public bool BreaksLinesOnlyAtSpaces;
        public int BaseUIFontSizeInPoints;
    }

    /// <summary>
    /// A sign language has no font, text direction or line spacing.
    /// </summary>
    public class SignLanguageValues
    {
        public string Tag;
        public string Name;
        public bool IsCustomName;
    }

    public class FrontBackMatterValues
    {
        public string Xmatter;
        public string PageNumberStyle;
        public bool ShowQrCode;
        public string QrcodeCaption;
        public string Country;
        public string Province;
        public string District;
    }

    public class AdvancedValues
    {
        /// <summary>
        /// A user-level setting (Settings.Default.AutoUpdate), not part of the collection.
        /// </summary>
        public bool AutoUpdate;
        public string CollectionName;
    }

    /// <summary>
    /// What the dialog needs in order to render, but cannot edit.
    /// </summary>
    public class CollectionSettingsContext
    {
        public bool IsTeamCollection;
        public bool EditingBlorgBook;
        public bool ShowAutoUpdate;

        /// <summary>
        /// Whether the subscription tier allows the Team Collections feature.
        /// </summary>
        public bool TeamCollectionsAllowed;
        public XmatterOffering[] XmatterOfferings;

        /// <summary>
        /// The xmatter pack the branding insists on, or null when the user may choose.
        /// </summary>
        public string BrandingForcedXmatter;
        public NumberingStyleOffering[] NumberingStyles;
    }

    public class XmatterOffering
    {
        public string DisplayName;
        public string InternalName;
        public string Description;
    }

    public class NumberingStyleOffering
    {
        public string LocalizedStyle;
        public string StyleKey;
    }

    /// <summary>
    /// The reply to POST collection/settings. ErrorMessage is null when the settings were saved;
    /// otherwise nothing was saved, the editing session is still open, and the dialog shows the
    /// message and stays up.
    /// </summary>
    public class CollectionSettingsSaveResult
    {
        public bool RestartRequired;
        public string ErrorMessage;
    }
}
