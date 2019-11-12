using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bloom.ToPalaso;
using SIL.Windows.Forms.WritingSystems;

namespace Bloom.Collection
{
	public class WritingSystem
	{
		private readonly int _languageNumberInCollection;
		private readonly Func<string> _codeOfDefaultLanguageForNaming;
		public static LanguageLookupModel LookupIsoCode = new LanguageLookupModel();
		private string _iso639Code;
		public string Name;
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
		public int BaseUiFontSizeInPoints;
		public decimal LineHeight;
		public string FontName;

		public WritingSystem(int languageNumberInCollection, Func<string> codeOfDefaultLanguageForNaming)
		{
			_languageNumberInCollection = languageNumberInCollection;
			_codeOfDefaultLanguageForNaming = codeOfDefaultLanguageForNaming;
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
			if (!string.IsNullOrEmpty(Iso639Code) && !String.IsNullOrEmpty(Name))
				return Name;

			return GetLanguageName_NoCache(inLanguage);
		}
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
						if (!LookupIsoCode.GetBestLanguageName("en", out match))
							return $"L{_languageNumberInCollection}-Unknown-" + Iso639Code;
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
			Iso639Code = value;
			Name = GetLanguageName_NoCache(_codeOfDefaultLanguageForNaming());
		}

		public void SaveToXElement(XElement xml)
		{
			var pfx = "Language" + _languageNumberInCollection;
			xml.Add(new XElement(pfx+"Name", Name));
			xml.Add(new XElement(pfx + "Iso639Code", Iso639Code));
			xml.Add(new XElement($"DefaultLanguage{_languageNumberInCollection}FontName", FontName));
			xml.Add(new XElement($"IsLanguage{_languageNumberInCollection}Rtl", IsRightToLeft));
			xml.Add(new XElement(pfx + "LineHeight", LineHeight));
			xml.Add(new XElement(pfx+"BreaksLinesOnlyAtSpaces", BreaksLinesOnlyAtSpaces));

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

			// EPUBs don't handle direction: in CSS files.
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
		/// <param name="defaultLanguageCode"></param>
		/// <param name="languageForDefaultNameLookup">a code or "self" if we should use the iso code for this spec to look it up</param>
		public void ReadSpecFromXml(XElement xml, bool defaultToEnglishIfMissing,  string languageForDefaultNameLookup)
		{
			var pfx = "Language" + _languageNumberInCollection;
			Iso639Code = ReadString(xml, $"Language{this._languageNumberInCollection}Iso639Code", defaultToEnglishIfMissing?"en":"");
			IsRightToLeft = ReadBoolean(xml, $"IsLanguage{_languageNumberInCollection}Rtl", false);
			
			Name = ReadString(xml, pfx+"Name", "");
			if (Name == "")
			{
				Name = GetLanguageName_NoCache(languageForDefaultNameLookup=="self"?Iso639Code:languageForDefaultNameLookup);
			}

			LineHeight = ReadDecimal(xml, pfx+"LineHeight", 0);

			FontName = ReadString(xml, $"DefaultLanguage{_languageNumberInCollection}FontName", GetDefaultFontName());
			
			BreaksLinesOnlyAtSpaces = ReadBoolean(xml, pfx+"BreaksLinesOnlyAtSpaces", false);
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
			Decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d);
			return d;
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
			//Since we always install Andika New Basic, let's just always use that as the default
			//Note (BL-3674) the font installer may not have completed yet, so we don't even check to make
			//sure it's there. It's possible that the user actually uninstalled Andika, but that's ok. Until they change to another font,
			// they'll get a message that this font is not actually installed when they try to edit a book.
			return "Andika New Basic";
		}
	}
}
