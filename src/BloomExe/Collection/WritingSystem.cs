using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Bloom.ToPalaso;
using SIL.Windows.Forms.WritingSystems;

namespace Bloom.Collection
{
	public class WritingSystem
	{
		private readonly Func<string> _codeOfDefaultLanguageForNaming;
		public static LanguageLookupModel LookupIsoCode = new LanguageLookupModel();
		private string _iso639Code;
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

		public int GetBaseUIFontSizeInPointsForCss() => BaseUIFontSizeInPoints == 0 ? 12 : BaseUIFontSizeInPoints;

		public decimal LineHeight;

		private string _fontName;
		public string FontName
		{
			get => _fontName ?? (_fontName = GetDefaultFontName());
			set => _fontName = value;
		}

		public WritingSystem(Func<string> codeOfDefaultLanguageForNaming)
		{
			//Note: I'm not convinced we actually ever rely on dynamic name lookups anymore?
			//See: https://issues.bloomlibrary.org/youtrack/issue/BL-7832
			_codeOfDefaultLanguageForNaming = codeOfDefaultLanguageForNaming;
			_iso639Code = string.Empty;
		}

		public string Name { get; private set; }
		public bool IsCustomName { get; private set; }

		public void SetName(string name, bool isCustom)
		{
			Name = name;
			IsCustomName = isCustom;
		}

		public string Iso639Code
		{
			get { return _iso639Code; }
			set {
				_iso639Code = value;
				Name = GetLanguageName_NoCache(_codeOfDefaultLanguageForNaming());
			}
		}

		public string GetNameInLanguage(string inLanguage)
		{
			if (!string.IsNullOrEmpty(Iso639Code) && !String.IsNullOrEmpty(Name) && IsCustomName)
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
			try {
				if (string.IsNullOrEmpty(Iso639Code))
					return string.Empty;

				var name = LookupIsoCode.GetLocalizedLanguageName(Iso639Code, inLanguage);
				if (name == Iso639Code)
				{
					string match;
					if (!LookupIsoCode.GetBestLanguageName(Iso639Code, out match))
						return "L1-Unknown-" + Iso639Code;
					return match;
				}
				return name;
			}
			catch (Exception)
			{
				return "Unknown-" + Iso639Code;
			}
		}

		public void ChangeIsoCode(string value)
		{
			Iso639Code = value; // also sets the name
		}

		/// <summary>
		/// Save old-style settings for one writing system.
		/// </summary>
		/// <param name="indexInCollection">1-based index!</param>
		public void SaveToXElement(XElement xml, int indexInCollection)
		{
			var pfx = "Language" + indexInCollection;
			xml.Add(new XElement(pfx+"Name",
				new XAttribute("deprecated", "true"), Name));
			xml.Add(new XElement(pfx+"IsCustomName",
				new XAttribute("deprecated", "true"), IsCustomName));
			xml.Add(new XElement(pfx + "Iso639Code",
				new XAttribute("deprecated", "true"), Iso639Code));
			xml.Add(new XElement($"DefaultLanguage{indexInCollection}FontName",
				new XAttribute("deprecated", "true"), FontName));
			xml.Add(new XElement($"IsLanguage{indexInCollection}Rtl",
				new XAttribute("deprecated", "true"), IsRightToLeft));
			xml.Add(new XElement(pfx + "LineHeight",
				new XAttribute("deprecated", "true"), LineHeight));
			xml.Add(new XElement(pfx+"BreaksLinesOnlyAtSpaces",
				new XAttribute("deprecated", "true"), BreaksLinesOnlyAtSpaces));
			xml.Add(new XElement(pfx+ "BaseUIFontSizeInPoints",
				new XAttribute("deprecated", "true"), BaseUIFontSizeInPoints));
		}

		public void SaveAsLanguageXElement(XElement xml)
		{
			xml.Add(new XElement("Language",
				new XAttribute("tag", Iso639Code),
				new XAttribute("name", Name),
				new XAttribute("direction", IsRightToLeft ? "rtl" : "ltr"),
				new XAttribute("lineHeight", LineHeight),
				new XAttribute("breakLinesOnlyAtSpaces", BreaksLinesOnlyAtSpaces),
				new XAttribute("baseUIFontSizeInPoints", BaseUIFontSizeInPoints),
				new XAttribute("defaultFontName", FontName),
				new XAttribute("isCustomName", IsCustomName)));
		}

		public void AddSelectorCssRule(StringBuilder sb, bool omitDirection)
		{
			AddSelectorCssRule(sb, "[lang='" + Iso639Code + "']", FontName, IsRightToLeft, LineHeight, BreaksLinesOnlyAtSpaces, omitDirection);
		}

		public static void AddSelectorCssRule(StringBuilder sb, string selector, string fontName, bool isRtl, decimal lineHeight, bool breakOnlyAtSpaces, bool omitDirection)
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
				else    // Ensure proper directionality: see https://silbloom.myjetbrains.com/youtrack/issue/BL-6256.
					sb.AppendLine(" direction: ltr;");
			}
			if (lineHeight > 0)
				sb.AppendLine(" line-height: " + lineHeight.ToString(CultureInfo.InvariantCulture) + ";");

			if (breakOnlyAtSpaces)
				sb.AppendLine(" word-break: keep-all;");

			sb.AppendLine("}");
		}

		/// <summary>
		/// Read in all the persisted values of this class from an XElement
		/// </summary>
		/// <param name="xml"></param>
		/// <param name="defaultToEnglishIfMissing"></param>
		/// <param name="languageForDefaultNameLookup">a code or "self" if we should use the iso code for this spec to look it up</param>
		/// <param name="indexInCollection">1-based index!</param>
		public void ReadFromXml(XElement xml, bool defaultToEnglishIfMissing, string languageForDefaultNameLookup,
			int indexInCollection)
		{
			var pfx = "Language" + indexInCollection;

			/* Enhance (from JT):
			 When you do this for Language1, the Iso639 setter will initialize Name using _codeOfDefaultLanguageForNaming(). But that will retrieve the Language2 Iso639 code, which hasn't been set yet. I suppose it doesn't matter, since two lines down you overwrite that Name, typically with one saved in the file. If for some reason there isn't one saved in the file, you will look up an English name for it (since you pass that as languageForDefaultNameLookup for Language1).
			Seems like it would simplify things if Name had a getter which would initialize it's variable to GetLanguageName_NoCache(_codeOfDefaultLanguageForNaming()) if not already set.
			Then the Iso639Code setter could just clear _name.
			The code just below here would initialize _name by reading the string, but could leave it null if it doesn't find it
			By the time anything needs Name, Language2's ISO code should be set, so if you need to look up a default name you'll do it in the right language.
			You could then get rid of the languageForDefaultNameLookup argument.*/

			Iso639Code = CollectionSettings.ReadString(xml, $"Language{indexInCollection}Iso639Code", defaultToEnglishIfMissing?"en":"");
			IsRightToLeft = CollectionSettings.ReadBoolean(xml, $"IsLanguage{indexInCollection}Rtl", false);
			
			Name = CollectionSettings.ReadString(xml, pfx+"Name", "");
			if (Name == "")
			{
				Name = GetLanguageName_NoCache(languageForDefaultNameLookup=="self"?Iso639Code:languageForDefaultNameLookup);
			}
			IsCustomName = ReadOrComputeIsCustomName(xml, pfx+"IsCustomName");
			LineHeight = ReadDecimal(xml, pfx+"LineHeight", 0);
			FontName = CollectionSettings.ReadString(xml, $"DefaultLanguage{indexInCollection}FontName", GetDefaultFontName());
			BreaksLinesOnlyAtSpaces = CollectionSettings.ReadBoolean(xml, pfx+"BreaksLinesOnlyAtSpaces", false);
			BaseUIFontSizeInPoints = CollectionSettings.ReadInteger(xml, pfx + "BaseUIFontSizeInPoints", 0 /* 0 means "default" */);
		}

		private bool ReadOrComputeIsCustomName(XElement xml, string id)
		{
			string s = CollectionSettings.ReadString(xml, id, null);
			if (s != null)
			{
				bool b;
				if (bool.TryParse(s, out b))
					return b;
			}
			// Compute value since it wasn't stored.
			if (!LookupIsoCode.AreLanguagesLoaded)
			{
				if (!SIL.WritingSystems.Sldr.IsInitialized)
					SIL.WritingSystems.Sldr.Initialize(true);	// needed for tests
				LookupIsoCode.IncludeScriptMarkers = false;
				// The previous line should have loaded the LanguageLookup object: if something changes so that
				// it doesn't, ensure that happens anyway.
				if (!LookupIsoCode.AreLanguagesLoaded)
					LookupIsoCode.LoadLanguages();
			}
			if (String.IsNullOrWhiteSpace(Iso639Code))
				return false;	// undefined (probably language3)

			var language = LookupIsoCode.LanguageLookup.GetLanguageFromCode(Iso639Code);
			// (If the lookup didn't find a language, treat the name as custom.)
			return Name != language?.Names?.FirstOrDefault();
		}

		private static decimal ReadDecimal(XElement xml, string id, decimal defaultValue)
		{
			var s = CollectionSettings.ReadString(xml, id, defaultValue.ToString(CultureInfo.InvariantCulture));
			decimal d;
			// REVIEW: if we localize the display of decimal values in the line-height combo box, then this
			// needs to handle the localized version of the number.  (This happens automatically by removing
			// the middle two arguments.)
			Decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d);
			return d;
		}

		internal static string GetDefaultFontName()
		{
			//Since we always install Andika New Basic, let's just always use that as the default
			//Note (BL-3674) the font installer may not have completed yet, so we don't even check to make
			//sure it's there. It's possible that the user actually uninstalled Andika, but that's ok. Until they change to another font,
			// they'll get a message that this font is not actually installed when they try to edit a book.
			return "Andika New Basic";
		}

		/*public string GetWritingSystemDisplayForUICss()
		{
			// I wanted to limit this with the language tag, but after 2 hours I gave up simply getting the current language tag
			// to the decodable reader code. What a mess that code is. So now I'm taking advantage of the fact that there is only
			// one language used in our current tools
			 return $".lang1InATool[lang='{Iso639Code}']{{font-size: {(BaseUIFontSizeInPoints == 0 ? 10 : BaseUIFontSizeInPoints)}pt;}}";
		}*/

		public void ReadFromLanguageSubElement(XElement languageElement)
		{
			Iso639Code = CollectionSettings.ReadAttribute(languageElement, "tag", "");
			Name = CollectionSettings.ReadAttribute(languageElement, "name", "Unknown Language");

			IsRightToLeft = string.Equals(CollectionSettings.ReadAttribute(languageElement, "direction", "ltr"),"rtl");

			LineHeight = CollectionSettings.ReadDecimalAttribute(languageElement, "lineHeight", 0);

			BreaksLinesOnlyAtSpaces =
				string.Equals(CollectionSettings.ReadAttribute(languageElement, "breakLinesOnlyAtSpaces", "false"),"true");
			FontName = CollectionSettings.ReadAttribute(languageElement, "defaultFontName", GetDefaultFontName());
			BaseUIFontSizeInPoints = CollectionSettings.ReadIntegerAttribute(languageElement, "baseUIFontSizeInPoints", 0);
			IsCustomName = string.Equals(CollectionSettings.ReadAttribute(languageElement, "isCustomName", "false"),"true");
		}
	}
}
