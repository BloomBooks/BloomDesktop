using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Api;
using L10NSharp;
using SIL.IO;

namespace Bloom.Book
{
    public class XMatterInfo
    {
        public readonly string PathToFolder;

        /// <summary>
        /// Whether the pack ships with Bloom, rather than being one a user installed.
        /// </summary>
        public readonly bool ShipsWithBloom;

        public XMatterInfo(string pathToFolder, bool shipsWithBloom)
        {
            PathToFolder = pathToFolder;
            ShipsWithBloom = shipsWithBloom;
        }

        private const string kL10nIdPrefix =
            "CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack.";

        public override string ToString()
        {
            return Key;
        }

        /// <summary>
        /// E.g. in "Factory-XMatter", the key is "Factory".
        /// </summary>
        public string Key
        {
            get
            {
                var x = Path.GetFileName(PathToFolder);
                // Must be ordinal: a culture-sensitive search under th-TH ignores the hyphen
                // and leaves it on the end of the key.
                var end = x.IndexOf("-xmatter", StringComparison.OrdinalIgnoreCase);
                return x.Substring(0, end);
            }
        }

        /// <summary>
        /// The key split into words for display, e.g. "Paper Saver" for "Factory-XMatter".
        /// </summary>
        public string EnglishLabel => SplitCamelCase(KeyWithCurrentName);

        /// <summary>
        /// The key, except for the pack whose folder still has its historical name.
        /// </summary>
        private string KeyWithCurrentName => Key == "Factory" ? "PaperSaver" : Key;

        /// <summary>
        /// The localization id of this pack's name.
        /// </summary>
        public string NameL10nId => kL10nIdPrefix + EnglishLabel;

        // from http://stackoverflow.com/a/5796793/723299
        private static string SplitCamelCase(string str)
        {
            return Regex.Replace(
                Regex.Replace(str, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2"),
                @"(\p{Ll})(\P{Ll})",
                "$1 $2"
            );
        }

        /// <summary>
        /// The localization id of this pack's description: the words of its key, with hyphens
        /// dropped and camel case split, e.g. "...ABC Reader Device.Description" for
        /// ABC-Reader-Device-XMatter. Every pack that ships with Bloom has its description in one
        /// of the XLF files under this id. (The name ids keep their older form, hyphens and all,
        /// because they are already translated.)
        /// </summary>
        public string DescriptionL10nId =>
            kL10nIdPrefix
            + string.Join(" ", KeyWithCurrentName.Split('-').Select(SplitCamelCase))
            + ".Description";

        /// <summary>
        /// The description of the pack in the current UI language, if available, otherwise in English.
        /// Packs that ship with Bloom have their descriptions in the XLF files. A pack a user
        /// installed themselves may instead have description-{lang}.txt files in its folder, even
        /// when it has the same name as a pack that ships with Bloom.
        /// </summary>
        public string GetDescription()
        {
            return ShipsWithBloom
                ? I18NApi.GetTranslation(DescriptionL10nId)
                : GetDescriptionFromFiles();
        }

        /// <summary>
        /// Read the description from the description-{lang}.txt files in the pack's folder, as
        /// a pack the user installed may provide. A translation is used only when its [Vn] version
        /// prefix is at least the English file's.
        /// </summary>
        private string GetDescriptionFromFiles()
        {
            const string desc = "description";
            try
            {
                // try to read English XMatter pack description first
                // we need version number at least
                var pathEnglish = Path.Combine(PathToFolder, desc + "-en.txt");
                if (!RobustFile.Exists(pathEnglish))
                    return string.Empty;

                var englishDescription = RobustFile.ReadAllText(pathEnglish);
                if (!englishDescription.StartsWith("[V", StringComparison.Ordinal))
                    return englishDescription;

                // Once we put [V1] in the english description we could have translations
                // for other UI languages.
                var uiLangId = LocalizationManager.UILanguageId;
                var enVersion = GetVersionNumberString(englishDescription);
                englishDescription = StripVersionOff(englishDescription);
                var pathUiLang = Path.Combine(PathToFolder, desc + "-" + uiLangId + ".txt");
                if (uiLangId == "en" || !RobustFile.Exists(pathUiLang))
                    return englishDescription;

                var uiLangDescription = RobustFile.ReadAllText(pathUiLang);
                var uiVersion = GetVersionNumberString(uiLangDescription);
                uiLangDescription = StripVersionOff(uiLangDescription);
                return enVersion > uiVersion || uiVersion == 0 || uiLangDescription.Length < 2
                    ? englishDescription
                    : uiLangDescription;
            }
            catch (Exception error)
            {
                return error.Message;
            }
        }

        private static int GetVersionNumberString(string fullDescription)
        {
            if (!fullDescription.StartsWith("[V", StringComparison.Ordinal))
                return 0;
            var endIndex = fullDescription.IndexOf("]", StringComparison.InvariantCulture);
            if (endIndex < 2)
                return 0;
            int result;
            return Int32.TryParse(
                fullDescription.Substring(2, endIndex - 2),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result
            )
                ? result
                : 0;
        }

        private static string StripVersionOff(string fullDescription)
        {
            if (!fullDescription.StartsWith("[V", StringComparison.Ordinal))
                return fullDescription;
            var closeBracketIndex = fullDescription.IndexOf("]", StringComparison.InvariantCulture);
            return (closeBracketIndex > 2 && fullDescription.Length > 4)
                ? fullDescription.Substring(closeBracketIndex + 1)
                : fullDescription;
        }
    }
}
