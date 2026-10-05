using System.Collections.Generic;

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

        /// <summary>
        /// Dotted paths into Values whose change means Bloom has to restart, so the dialog can
        /// relabel its OK button and show the restart reminder.
        /// </summary>
        public string[] RestartPaths;

        /// <summary>
        /// Whether the open collection is a Team Collection (even if disconnected). The
        /// Experimental page uses it to stop the user turning the Team Collections feature off
        /// while they are in one.
        /// </summary>
        public bool IsTeamCollection;

        /// <summary>
        /// Set only when the user may not edit the collection settings (a Team Collection member
        /// who is not an administrator); the other fields are then null.
        /// </summary>
        public string NotAllowedMessage;
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
    }

    public class LanguagesValues
    {
        public LanguageValues Language1;
        public LanguageValues Language2;

        /// <summary>
        /// Null when the collection has no third language; posting null removes it.
        /// </summary>
        public LanguageValues Language3;

        /// <summary>
        /// Null when the collection has no sign language; posting null removes it.
        /// </summary>
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
    /// The body of POST collection/settings. The dialog works out RestartRequired from the
    /// restart paths, which it needs anyway to label OK.
    /// </summary>
    public class CollectionSettingsSaveRequest
    {
        public CollectionSettingsValues Values;
        public bool RestartRequired;
    }

    /// <summary>
    /// The reply to POST collection/settings. ErrorMessage is null when the settings were saved
    /// (and Bloom restarts by itself if that was needed); otherwise nothing was saved, and the
    /// dialog shows the message and stays up.
    /// </summary>
    public class CollectionSettingsSaveResult
    {
        public string ErrorMessage;
    }
}
