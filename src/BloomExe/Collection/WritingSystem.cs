using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SIL.Windows.Forms.WritingSystems;
using SIL.WritingSystems;

namespace Bloom.Collection
{
    public class WritingSystem
    {
        private readonly int _languageNumberInCollection;
        private readonly Func<string> _tagOfDefaultLanguageForNaming;
        public static LanguageLookupModel LookupModel = new LanguageLookupModel();
        private string _langTag;
        public bool IsRightToLeft;

        // Line breaks are always wanted only between words.  (ignoring hyphenation)
        // Most alphabetic scripts use spaces to separate words, and line-breaks occur only
        // at those spaces.  CJK scripts (which includes scripts outside China, Japan, and
        // Korea) usually do not have spaces, so either line breaks are acceptable anywhere
        // or some fancy algorithm is used to detect word boundaries. (dictionary lookup?)
        // Some minority languages use the CJK script of the national language but also
        // use spaces to separate words.  For these languages, proper display requires
        // the css setting "word-break: keep-all".  This setting affects only how CJK
        // scripts are handled with regard to line breaking.
        // See https://silbloom.myjetbrains.com/youtrack/issue/BL-5761.
        public bool BreaksLinesOnlyAtSpaces;

        /// <summary>
        /// When we show this writing system in a tool (e.g. Decodable Reader Tool), how
        /// big does it need to be to be visible? Here 0 means "use the default".
        /// </summary>
        public int BaseUIFontSizeInPoints;

        public int GetBaseUIFontSizeInPointsForCss() =>
            BaseUIFontSizeInPoints == 0 ? 12 : BaseUIFontSizeInPoints;

        public decimal LineHeight;
        public string FontName;

        public WritingSystem(
            int languageNumberInCollection,
            Func<string> tagOfDefaultLanguageForNaming
        )
        {
            _languageNumberInCollection = languageNumberInCollection;

            //Note: I'm not convinced we actually ever rely on dynamic name lookups anymore?
            //See: https://issues.bloomlibrary.org/youtrack/issue/BL-7832
            _tagOfDefaultLanguageForNaming = tagOfDefaultLanguageForNaming;
        }

        public string Name { get; private set; }
        public bool IsCustomName { get; private set; }

        public void SetName(string name, bool isCustom)
        {
            Name = name;
            IsCustomName = isCustom;
        }

        public string Tag
        {
            get { return _langTag; }
            set
            {
                _langTag = value;
                Name = GetLanguageName_NoCache(_tagOfDefaultLanguageForNaming());
            }
        }

        public string GetNameInLanguage(string inLanguage)
        {
            if (!string.IsNullOrEmpty(Tag) && !string.IsNullOrEmpty(Name) && IsCustomName)
                return Name;

            return GetLanguageName_NoCache(inLanguage);
        }

        //		/// <remarks>
        //		/// The user has already verified in the language chooser dialog how he wants to display
        //		/// the language name.  Even if it isn't changed, the value of the Name property must be
        //		/// okay regardless of the UI language.
        //		/// </remarks>
        //		public string UiName
        //		{
        //			get { return GetNameInLanguage(L10NSharp.LocalizationManager.UILanguageId); }
        //		}

        private string GetLanguageName_NoCache(string inLanguage)
        {
            try
            {
                if (string.IsNullOrEmpty(Tag))
                    return string.Empty;

                var name = IetfLanguageTag.GetLocalizedLanguageName(Tag, inLanguage);
                if (name == Tag)
                {
                    string match;
                    if (!IetfLanguageTag.GetBestLanguageName(Tag, out match))
                    {
                        if (_languageNumberInCollection < 0)
                            return $"SignLanguage-Unknown-" + Tag;
                        return $"L{_languageNumberInCollection}-Unknown-" + Tag;
                    }
                    return match;
                }
                return name;
            }
            catch (Exception)
            {
                return "Unknown-" + Tag;
            }
        }

        public void ChangeTag(string value)
        {
            Tag = value; // also sets the name
        }

        public void SaveToXElement(XElement xml)
        {
            var pfx =
                _languageNumberInCollection < 0
                    ? "SignLanguage"
                    : "Language" + _languageNumberInCollection;
            xml.Add(new XElement(pfx + "Name", Name));
            xml.Add(new XElement(pfx + "IsCustomName", IsCustomName));
            xml.Add(new XElement(pfx + "Iso639Code", Tag));
            if (_languageNumberInCollection < 0)
                return;
            xml.Add(
                new XElement($"DefaultLanguage{_languageNumberInCollection}FontName", FontName)
            );
            xml.Add(new XElement($"IsLanguage{_languageNumberInCollection}Rtl", IsRightToLeft));
            xml.Add(new XElement(pfx + "LineHeight", LineHeight));
            xml.Add(new XElement(pfx + "BreaksLinesOnlyAtSpaces", BreaksLinesOnlyAtSpaces));
            xml.Add(new XElement(pfx + "BaseUIFontSizeInPoints", BaseUIFontSizeInPoints));
        }

        public void AddSelectorCssRule(StringBuilder sb, bool omitDirection)
        {
            AddSelectorCssRule(
                sb,
                "[lang='" + Tag + "']",
                FontName,
                IsRightToLeft,
                LineHeight,
                BreaksLinesOnlyAtSpaces,
                omitDirection
            );
        }

        public static void AddSelectorCssRule(
            StringBuilder sb,
            string selector,
            string fontName,
            bool isRtl,
            decimal lineHeight,
            bool breakOnlyAtSpaces,
            bool omitDirection
        )
        {
            sb.AppendLine();
            sb.AppendLine(selector);
            sb.AppendLine("{");
            sb.AppendLine(" font-family: '" + fontName + "';");

            // EPUBs don't handle direction: in CSS files.  <-- I think that is wrong. See BL-7835
            if (!omitDirection)
            {
                if (isRtl)
                    sb.AppendLine(" direction: rtl;");
                else // Ensure proper directionality: see https://silbloom.myjetbrains.com/youtrack/issue/BL-6256.
                    sb.AppendLine(" direction: ltr;");
            }
            if (lineHeight > 0)
                sb.AppendLine(
                    " line-height: " + lineHeight.ToString(CultureInfo.InvariantCulture) + ";"
                );

            if (breakOnlyAtSpaces)
                sb.AppendLine(" word-break: keep-all;");

            sb.AppendLine("}");
        }

        /// <summary>
        /// Read in all the persisted values of this class from an XElement
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="defaultToEnglishIfMissing"></param>
        /// <param name="languageForDefaultNameLookup">a language tag or "self" if we should use the language tag for this spec to look it up</param>
        public void ReadFromXml(
            XElement xml,
            bool defaultToEnglishIfMissing,
            string languageForDefaultNameLookup
        )
        {
            var pfx =
                _languageNumberInCollection < 0
                    ? "SignLanguage"
                    : "Language" + _languageNumberInCollection;

            /* Enhance (from JT):
              When you do this for Language1, the Tag setter will initialize Name using
              _tagOfDefaultLanguageForNaming(). But that will retrieve the Language2 Tag, which hasn't been set yet.
              I suppose it doesn't matter, since two lines down you overwrite that Name, typically with one saved in
              the file. If for some reason there isn't one saved in the file, you will look up an English name for
              it (since you pass that as languageForDefaultNameLookup for Language1).

              Seems like it would simplify things if Name had a getter which would initialize it's variable to
              GetLanguageName_NoCache(_tagOfDefaultLanguageForNaming()) if not already set.
              Then the Tag setter could just clear _name.
              The code just below here would initialize _name by reading the string, but could leave it null if it
              doesn't find it.
              By the time anything needs Name, Language2's Tag should be set, so if you need to look up a default
              name you'll do it in the right language.
              You could then get rid of the languageForDefaultNameLookup argument.
            */

            Tag = ReadString(xml, pfx + "Iso639Code", defaultToEnglishIfMissing ? "en" : "");

            Name = ReadString(xml, pfx + "Name", "");
            if (Name == "")
            {
                Name = GetLanguageName_NoCache(
                    languageForDefaultNameLookup == "self" ? Tag : languageForDefaultNameLookup
                );
            }
            IsCustomName = ReadOrComputeIsCustomName(xml, pfx + "IsCustomName");
            if (_languageNumberInCollection < 0)
            {
                // Set the rest (which isn't used for Sign Languages) to default values and return.
                IsRightToLeft = false;
                LineHeight = 0;
                FontName = GetDefaultFontName(); // probably could just be empty string...
                BreaksLinesOnlyAtSpaces = false;
                BaseUIFontSizeInPoints = 0;
                return;
            }
            IsRightToLeft = ReadBoolean(xml, $"IsLanguage{_languageNumberInCollection}Rtl", false);
            LineHeight = ReadDecimal(xml, pfx + "LineHeight", 0);
            FontName = ReadString(
                xml,
                $"DefaultLanguage{_languageNumberInCollection}FontName",
                GetDefaultFontName()
            );
            BreaksLinesOnlyAtSpaces = ReadBoolean(xml, pfx + "BreaksLinesOnlyAtSpaces", false);
            BaseUIFontSizeInPoints = ReadInt(
                xml,
                pfx + "BaseUIFontSizeInPoints",
                0 /* 0 means "default" */
            );
        }

        private bool ReadOrComputeIsCustomName(XElement xml, string id)
        {
            string s = ReadString(xml, id, null);
            if (s != null)
            {
                bool b;
                if (bool.TryParse(s, out b))
                    return b;
            }
            // Compute value since it wasn't stored.
            if (!LookupModel.AreLanguagesLoaded)
            {
                if (!SIL.WritingSystems.Sldr.IsInitialized)
                    SIL.WritingSystems.Sldr.Initialize(true); // needed for tests
                LookupModel.IncludeScriptMarkers = false;
                // The previous line should have loaded the LanguageLookup object: if something changes so that
                // it doesn't, ensure that happens anyway.
                if (!LookupModel.AreLanguagesLoaded)
                    LookupModel.LoadLanguages();
            }
            if (string.IsNullOrWhiteSpace(Tag))
                return false; // undefined (probably language3 or sign language)

            var language = LookupModel.LanguageLookup.GetLanguageFromCode(Tag);
            // (If the lookup didn't find a language, treat the name as custom.)
            return Name != language?.Names?.FirstOrDefault();
        }

        private bool ReadBoolean(XElement xml, string id, bool defaultValue)
        {
            string s = ReadString(xml, id, defaultValue.ToString());
            bool b;
            bool.TryParse(s, out b);
            return b;
        }

        private decimal ReadDecimal(XElement xml, string id, decimal defaultValue)
        {
            var s = ReadString(xml, id, defaultValue.ToString(CultureInfo.InvariantCulture));
            decimal d;
            // REVIEW: if we localize the display of decimal values in the line-height combo box, then this
            // needs to handle the localized version of the number.  (This happens automatically by removing
            // the middle two arguments.)
            Decimal.TryParse(
                s,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out d
            );
            return d;
        }

        private int ReadInt(XElement xml, string id, int defaultValue)
        {
            var s = ReadString(xml, id, defaultValue.ToString(CultureInfo.InvariantCulture));
            int i;
            return int.TryParse(s, out i) ? i : defaultValue;
        }

        private string ReadString(XElement document, string id, string defaultValue)
        {
            var nodes = document.Descendants(id);
            if (nodes != null && nodes.Count() > 0)
                return nodes.First().Value;
            else
            {
                return defaultValue;
            }
        }

        internal static string GetDefaultFontName()
        {
            // Since we always provide Andika, let's just always use that as the default
            return "Andika";
        }

        internal WritingSystem Clone()
        {
            var copy = new WritingSystem(
                this._languageNumberInCollection,
                this._tagOfDefaultLanguageForNaming
            );
            copy.Tag = this.Tag;
            copy.Name = this.Name;
            copy.IsCustomName = this.IsCustomName;
            copy.LineHeight = this.LineHeight;
            copy.FontName = this.FontName;
            copy.BreaksLinesOnlyAtSpaces = this.BreaksLinesOnlyAtSpaces;
            copy.BaseUIFontSizeInPoints = this.BaseUIFontSizeInPoints;
            copy.IsRightToLeft = this.IsRightToLeft;
            return copy;
        }

        /*public string GetWritingSystemDisplayForUICss()
        {
            // I wanted to limit this with the language tag, but after 2 hours I gave up simply getting the current language tag
            // to the decodable reader code. What a mess that code is. So now I'm taking advantage of the fact that there is only
            // one language used in our current tools
             return $".lang1InATool[lang='{Tag}']{{font-size: {(BaseUIFontSizeInPoints == 0 ? 10 : BaseUIFontSizeInPoints)}pt;}}";
        }*/
    }
}
