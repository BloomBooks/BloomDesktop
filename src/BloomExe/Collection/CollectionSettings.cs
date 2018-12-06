using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using SIL.Reporting;
using SIL.Windows.Forms.WritingSystems;
using SIL.WritingSystems;
using SIL.Extensions;
using SIL.IO;

namespace Bloom.Collection
{

	public class NewCollectionSettings : CollectionSettings
	{
		public string PathToSettingsFile;
	}

	/// <summary>
	/// A library corresponds to a single folder (with subfolders) on the disk.
	/// In that folder is a file which persists the properties of this class, then a folder for each book
	/// </summary>
	public class CollectionSettings
	{
		private const int kCurrentOneTimeCheckVersionNumber = 1; // bumping this will trigger a new one time check
		public const string kDefaultXmatterName = "Traditional";
		private string _language1Iso639Code;
		private string _language2Iso639Code;
		private string _language3Iso639Code;
		private LanguageLookupModel _lookupIsoCode = new LanguageLookupModel();

		/// <summary>
		/// The branding the user wanted, but not confirmed by current SubscriptionCode, if any.
		/// </summary>
		public string InvalidBranding { get; private set; }

		public static readonly Dictionary<string, string> CssNumberStylesToCultureOrDigits =
			new Dictionary<string, string>()
			{
				// Initially, Bloom used CSS for page numbering and css counter styles for
				// controlling the script the page numbers are drawn in. For various reasons
				// we then switched to having code keep the page number in data-page-number,
				// so we can't make use of that CSS feature anymore but want to keep the same
				// list and keep working for users of previous versions.
				// In this dictionary, we're pairing css counting styles (the key) with
				// the 10 digits used by the script. As a side benefit, this will allow us to support
				// other number systems, if people request them (so long as they can be represented by just
				// replacing digits).
				// In many cases, as commented, the ten digits were obtained from Microsoft cultures using this expression:
				// new CultureInfo(cultureCode).NumberFormat.NativeDigits
				// Some of the results are empty strings when it doesn't seem they should be (Hebrew, Armenian, Georgian)
				// This reflects that these systems can't be done with simple digit substitution, so we fall
				// back to not converting
				{ "Arabic-Indic", "٠١٢٣٤٥٦٧٨٩"}, // from ar-SA, not certain that this is correct one
				//{ "Armenian", ""}, // hy-AM yields 0123456789; not true Armenian, an ancient letter-value system so we can't do it
				//{ "Upper-Armenian", ""}, // hy-AM, probably a variation on Armenian also not permitting digit-substitution
				//{ "Lower-Armenian", ""},//haven't found the culture or list of number for this
				{ "Bengali", "০১২৩৪৫৬৭৮৯"}, // from bn-BD
				{ "Cambodian", "០១២៣៤៥៦៧៨៩"}, // from km-KH
				{ "Khmer", "០១២៣៤៥៦៧៨៩"}, // from km-KH"
				{ "Chakma", "𑄶𑄷𑄸𑄹𑄺𑄻𑄼𑄽𑄾𑄿" }, // see https://codepoints.net/search?sc=Cakm
				{ "Cjk-Decimal", "〇一二三四五六七八九"},// haven't found a culture for this
				{ "Decimal", "" },
				{ "Devanagari", "०१२३४५६७८९"}, // from hi-IN
				//{ "Georgian", ""}, //  ka-GE yields 0123456789; https://en.wikipedia.org/wiki/Georgian_numerals says Georgian is not a simple positional system so we can't do it
				{ "Gujarati", "૦૧૨૩૪૫૬૭૮૯"}, // from gu-IN
				{ "Gurmukhi", "੦੧੨੩੪੫੬੭੮੯"}, // from pa-IN
				// { "Hebrew", ""}, // he-IL yields 0123456789; not true Hebrew, which uses a non-positional letter-value system, so we can't do it.
				{ "Kannada", "೦೧೨೩೪೫೬೭೮೯"}, // from kn-IN
				{ "Lao", "໐໑໒໓໔໕໖໗໘໙"}, // from lo-LA
				{ "Malayalam", "൦൧൨൩൪൫൬൭൮൯"}, // ml-IN
				{ "Mongolian", "᠐᠑᠒᠓᠔᠕᠖᠗᠘᠙"}, // from https://en.wikipedia.org/wiki/Mongolian_numerals; was mn-Mong-MN, which would wrongly be used as a digit string.
				{ "Myanmar", "၀၁၂၃၄၅၆၇၈၉"}, // from my-MM
				{ "Oriya", "୦୧୨୩୪୫୬୭୮୯"}, // haven't found a culture for this
				{ "Persian", "۰۱۲۳۴۵۶۷۸۹"}, // from fa-IR
				{ "Tamil", "௦௧௨௩௪௫௬௭௮௯"}, // from ta-IN"
				{ "Telugu", "౦౧౨౩౪౫౬౭౮౯"}, // from te-IN
				{ "Thai", "๐๑๒๓๔๕๖๗๘๙"}, // from th-TH
				{ "Tibetan", "༠༡༢༣༤༥༦༧༨༩"}, // from bo-CN
			};

		/// <summary>
		/// for moq in unit tests only
		/// </summary>
		public CollectionSettings()
		{
			BrandingProjectKey = "Default";
			PageNumberStyle = "Decimal";
			XMatterPackName = kDefaultXmatterName;
			Language2Iso639Code = "en";
			AllowNewBooks = true;
			CollectionName = "dummy collection";
			AudioRecordingMode = TalkingBookApi.AudioRecordingMode.Sentence;
		}

		public static void CreateNewCollection(NewCollectionSettings collectionInfo)
		{
			// For some reason this constructor is used to create new collections. But I think a static method is much clearer.
			new CollectionSettings(collectionInfo);
		}

		public CollectionSettings(NewCollectionSettings collectionInfo)
			:this(collectionInfo.PathToSettingsFile)
		{
			AllowNewBooks = collectionInfo.AllowNewBooks;
			DefaultLanguage1FontName = collectionInfo.DefaultLanguage1FontName;
			Language1LineHeight = collectionInfo.Language1LineHeight;
			IsLanguage1Rtl = collectionInfo.IsLanguage1Rtl;
			DefaultLanguage2FontName = DefaultLanguage3FontName = GetDefaultFontName();

			Language1Iso639Code = collectionInfo.Language1Iso639Code;
			Language2Iso639Code = collectionInfo.Language2Iso639Code;
			Language3Iso639Code = collectionInfo.Language3Iso639Code;
			Language1Name = collectionInfo.Language1Name;
			Country = collectionInfo.Country;
			Province = collectionInfo.Province;
			District = collectionInfo.District;
			IsSourceCollection = collectionInfo.IsSourceCollection;
			XMatterPackName = collectionInfo.XMatterPackName;
			PageNumberStyle = collectionInfo.PageNumberStyle;
			BrandingProjectKey = collectionInfo.BrandingProjectKey;
			SubscriptionCode = collectionInfo.SubscriptionCode;
			if (BrandingProjectKey == "Local Community")
			{
				// migrate for 4.4
				BrandingProjectKey = "Local-Community";
			}

			AudioRecordingMode = collectionInfo.AudioRecordingMode;

			Save();
		}

		/// <summary>
		/// can be used whether the library exists already, or not
		/// </summary>
		public CollectionSettings(string desiredOrExistingSettingsFilePath)
			:this()
		{
			SettingsFilePath = desiredOrExistingSettingsFilePath;
			CollectionName = Path.GetFileNameWithoutExtension(desiredOrExistingSettingsFilePath);
			var libraryDirectory = Path.GetDirectoryName(desiredOrExistingSettingsFilePath);
			var parentDirectoryPath = Path.GetDirectoryName(libraryDirectory);

			if (RobustFile.Exists(desiredOrExistingSettingsFilePath))
			{
				DoDefenderFolderProtectionCheck();
				Load();
			}
			else
			{
				if (!Directory.Exists(parentDirectoryPath))
					Directory.CreateDirectory(parentDirectoryPath);

				if (!Directory.Exists(libraryDirectory))
					Directory.CreateDirectory(libraryDirectory);

				DoDefenderFolderProtectionCheck();
				Save();
			}
		}

		private void DoDefenderFolderProtectionCheck()
		{
			// We check for a Windows Defender "Controlled Access" problem when we start Bloom,
			// but the user may have moved their startup collection to a "safe" place and now be opening a different
			// collection in a "controlled" place. Test again with this settings file path.
			// 'FolderPath' is the directory part of 'SettingsFilePath'.
			if (!DefenderFolderProtectionCheck.CanWriteToDirectory(FolderPath))
			{
				Environment.Exit(-1);
			}
		}

		#region Persisted properties

		//these are virtual for the sake of the unit test mock framework
		public virtual string Language1Iso639Code
		{
			get { return _language1Iso639Code; }
			set
			{
				_language1Iso639Code = value;
				Language1Name = GetLanguage1Name_NoCache(Language2Iso639Code);
			}
		}
		public virtual string Language2Iso639Code
		{
			get { return _language2Iso639Code; }
			set
			{
				_language2Iso639Code = value;
				Language2Name = GetLanguage2Name_NoCache(_language2Iso639Code);
			}
		}
		public virtual string Language3Iso639Code
		{
			get { return _language3Iso639Code; }
			set
			{
				_language3Iso639Code = value;
				Language3Name = GetLanguage3Name_NoCache(Language2Iso639Code);
			}
		}

		public virtual string Language1Name { get; set; }
		public virtual string Language2Name { get; set; }
		public virtual string Language3Name { get; set; }

		public virtual bool IsLanguage1Rtl { get; set; }
		public virtual bool IsLanguage2Rtl { get; set; }
		public virtual bool IsLanguage3Rtl { get; set; }

		public bool GetLanguageRtl(int langNum)
		{
			switch (langNum)
			{
				case 1:
					return IsLanguage1Rtl;
				case 2:
					return IsLanguage2Rtl;
				case 3:
					return IsLanguage3Rtl;
				default:
					throw new ArgumentException("The language number is not valid.");
			}
		}

		public void SetLanguageRtl(int langNum, bool isRtl)
		{
			switch (langNum)
			{
				case 1:
					IsLanguage1Rtl = isRtl;
					break;
				case 2:
					IsLanguage2Rtl = isRtl;
					break;
				case 3:
					IsLanguage3Rtl = isRtl;
					break;
				default:
					throw new ArgumentException("The language number is not valid.");
			}
		}

		public virtual decimal Language1LineHeight { get; set; }
		public virtual decimal Language2LineHeight { get; set; }
		public virtual decimal Language3LineHeight { get; set; }

		public decimal GetLanguageLineHeight(int langNum)
		{
			switch (langNum)
			{
				case 1:
					return Language1LineHeight;
				case 2:
					return Language2LineHeight;
				case 3:
					return Language3LineHeight;
				default:
					throw new ArgumentException("The language number is not valid.");
			}
		}

		public void SetLanguageLineHeight(int langNum, decimal lineHeight)
		{
			switch (langNum)
			{
				case 1:
					Language1LineHeight = lineHeight;
					break;
				case 2:
					Language2LineHeight = lineHeight;
					break;
				case 3:
					Language3LineHeight = lineHeight;
					break;
				default:
					throw new ArgumentException("The language number is not valid.");
			}
		}

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
		public virtual bool Language1BreaksLinesOnlyAtSpaces { get; set; }
		public virtual bool Language2BreaksLinesOnlyAtSpaces { get; set; }
		public virtual bool Language3BreaksLinesOnlyAtSpaces { get; set; }

		public bool GetBreakLinesOnlyAtSpaces(int langNum)
		{
			switch (langNum)
			{
				case 1:
					return Language1BreaksLinesOnlyAtSpaces;
				case 2:
					return Language2BreaksLinesOnlyAtSpaces;
				case 3:
					return Language3BreaksLinesOnlyAtSpaces;
				default:
					throw new ArgumentException("The language number is not valid.");
			}
		}

		public void SetBreakLinesOnlyAtSpaces(int langNum, bool breakOnlyAtSpaces)
		{
			switch (langNum)
			{
				case 1:
					Language1BreaksLinesOnlyAtSpaces = breakOnlyAtSpaces;
					break;
				case 2:
					Language2BreaksLinesOnlyAtSpaces = breakOnlyAtSpaces;
					break;
				case 3:
					Language3BreaksLinesOnlyAtSpaces = breakOnlyAtSpaces;
					break;
				default:
					throw new ArgumentException("The language number is not valid.");
			}
		}

		/// <summary>
		/// Intended for making shell books and templates, not vernacular
		/// </summary>
		public virtual bool IsSourceCollection { get; set; }

		public string GetLanguage1Name(string inLanguage)
		{
			if(!string.IsNullOrEmpty(this.Language1Name))
				return Language1Name;

			return GetLanguage1Name_NoCache(inLanguage);
		}

		private string GetLanguage1Name_NoCache(string inLanguage)
		{
			var name = _lookupIsoCode.GetLocalizedLanguageName(Language1Iso639Code, inLanguage);
			if (name == Language1Iso639Code)
			{
				string exactLanguageMatch;
				if (!_lookupIsoCode.GetBestLanguageName(Language1Iso639Code, out exactLanguageMatch))
					return "L1-Unknown-" + Language1Iso639Code;
				return exactLanguageMatch;
			}
			return name;
		}

		/// <summary>
		/// Get a name for Language1 that is safe for using as part of a file name.
		/// (Currently used for suggesting a pdf filename when publishing.)
		/// </summary>
		/// <param name="inLanguage"></param>
		/// <returns></returns>
		public object GetFilesafeLanguage1Name(string inLanguage)
		{
			var languageName = GetLanguage1Name(inLanguage);
			return Path.GetInvalidFileNameChars().Aggregate(
				languageName, (current, character) => current.Replace(character, ' '));
		}

		public string GetLanguage2Name(string inLanguage)
		{
			if(!string.IsNullOrEmpty(Language2Iso639Code) && !string.IsNullOrEmpty(Language2Name))
				return Language2Name;
			return GetLanguage2Name_NoCache(inLanguage);
		}

		private string GetLanguage2Name_NoCache(string inLanguage)
		{
			try
			{
				if (string.IsNullOrEmpty(Language2Iso639Code))
					return string.Empty;

				//TODO: we are going to need to show "French" as "Français"... but if the name isn't available, we should have a fall-back mechanism, at least to english
				//So, we'd rather have GetBestLanguageMatch()

				return GetLanguageName(Language2Iso639Code, inLanguage);
			}
			catch (Exception)
			{
				Debug.Fail("check this out. BL-193 Reproduction");
				// a user reported this, and I saw it happen once: had just installed 0.8.38, made a new vernacular
				//project, added a picture dictionary, the above failed (no debugger, so I don't know why).
				return "L2-Unknown-" + Language2Iso639Code;
			}
		}

		/// <summary>
		/// Get the name of the language whose code is the first argument, if possible in the language specified by the second.
		/// If the language code is unknown, return it unchanged.
		/// </summary>
		public string GetLanguageName(string code, string inLanguage)
		{
			return _lookupIsoCode.GetLocalizedLanguageName(code, inLanguage);
		}

		public string GetLanguage3Name(string inLanguage)
		{
			if (!string.IsNullOrEmpty(Language3Iso639Code) && !string.IsNullOrEmpty(Language3Name))
				return Language3Name;
			return GetLanguage3Name_NoCache(inLanguage);
		}
		private string GetLanguage3Name_NoCache(string inLanguage)
		{
			try
			{
				if (string.IsNullOrEmpty(Language3Iso639Code))
					return string.Empty;

				return GetLanguageName(Language3Iso639Code, inLanguage);
			}
			catch (Exception)
			{
				return "L2N-Unknown-" + Language3Iso639Code;
			}
		}
		#endregion

		/// ------------------------------------------------------------------------------------
		public void Save()
		{
			Logger.WriteEvent("Saving Collection Settings");

			XElement library = new XElement("Collection");
			library.Add(new XAttribute("version", "0.2"));
			library.Add(new XElement("Language1Name", Language1Name));
			library.Add(new XElement("Language2Name", Language2Name));
			library.Add(new XElement("Language3Name", Language3Name));
			library.Add(new XElement("Language1Iso639Code", Language1Iso639Code));
			library.Add(new XElement("Language2Iso639Code", Language2Iso639Code));
			library.Add(new XElement("Language3Iso639Code", Language3Iso639Code));
			library.Add(new XElement("DefaultLanguage1FontName", DefaultLanguage1FontName));
			library.Add(new XElement("DefaultLanguage2FontName", DefaultLanguage2FontName));
			library.Add(new XElement("DefaultLanguage3FontName", DefaultLanguage3FontName));
			library.Add(new XElement("OneTimeCheckVersionNumber", OneTimeCheckVersionNumber));
			library.Add(new XElement("IsLanguage1Rtl", IsLanguage1Rtl));
			library.Add(new XElement("IsLanguage2Rtl", IsLanguage2Rtl));
			library.Add(new XElement("IsLanguage3Rtl", IsLanguage3Rtl));
			library.Add(new XElement("Language1LineHeight", Language1LineHeight));
			library.Add(new XElement("Language2LineHeight", Language2LineHeight));
			library.Add(new XElement("Language3LineHeight", Language3LineHeight));
			library.Add(new XElement("Language1BreaksLinesOnlyAtSpaces", Language1BreaksLinesOnlyAtSpaces));
			library.Add(new XElement("Language2BreaksLinesOnlyAtSpaces", Language2BreaksLinesOnlyAtSpaces));
			library.Add(new XElement("Language3BreaksLinesOnlyAtSpaces", Language3BreaksLinesOnlyAtSpaces));
			library.Add(new XElement("IsSourceCollection", IsSourceCollection.ToString()));
			library.Add(new XElement("XMatterPack", XMatterPackName));
			library.Add(new XElement("PageNumberStyle", PageNumberStyle));
			library.Add(new XElement("BrandingProjectName", BrandingProjectKey));
			library.Add(new XElement("SubscriptionCode", SubscriptionCode));
			library.Add(new XElement("Country", Country));
			library.Add(new XElement("Province", Province));
			library.Add(new XElement("District", District));
			library.Add(new XElement("AllowNewBooks", AllowNewBooks.ToString()));
			library.Add(new XElement("AudioRecordingMode", AudioRecordingMode.ToString()));
			SIL.IO.RobustIO.SaveXElement(library, SettingsFilePath);

			SaveSettingsCollectionStylesCss();
		}

		private void SaveSettingsCollectionStylesCss()
		{
			string path = FolderPath.CombineForPath("settingsCollectionStyles.css");
			SaveCollectionStylesCss(path, false);
		}

		public void SaveCollectionStylesCss(string path, bool omitDirection)
		{
			try
			{
				var sb = new StringBuilder();
				sb.AppendLine("/* These styles are controlled by the Settings dialog box in Bloom. */");
				sb.AppendLine("/* They many be over-ridden by rules in customCollectionStyles.css or customBookStyles.css */");
				// REVIEW: is BODY always ltr, or should it be the same as Language1?  Having BODY be ltr for a book in Arabic or Hebrew
				// seems counterintuitive even if all the div elements are marked correctly.
				AddSelectorCssRule(sb, "BODY", GetDefaultFontName(), false, 0, false, omitDirection);
				// note: css pseudo elements  cannot have a @lang attribute. So this is needed to show page numbers in scripts
				// not covered by Andika New Basic.
				AddSelectorCssRule(sb, ".numberedPage::after", DefaultLanguage1FontName, IsLanguage1Rtl, Language1LineHeight, Language1BreaksLinesOnlyAtSpaces, omitDirection);
				AddSelectorCssRule(sb, "[lang='" + Language1Iso639Code + "']", DefaultLanguage1FontName, IsLanguage1Rtl, Language1LineHeight, Language1BreaksLinesOnlyAtSpaces, omitDirection);
				AddSelectorCssRule(sb, "[lang='" + Language2Iso639Code + "']", DefaultLanguage2FontName, IsLanguage2Rtl, Language2LineHeight, Language2BreaksLinesOnlyAtSpaces, omitDirection);
				if (!string.IsNullOrEmpty(Language3Iso639Code))
				{
					AddSelectorCssRule(sb, "[lang='" + Language3Iso639Code + "']", DefaultLanguage3FontName, IsLanguage3Rtl, Language3LineHeight, Language3BreaksLinesOnlyAtSpaces, omitDirection);
				}
				RobustFile.WriteAllText(path, sb.ToString());
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error, "Bloom was unable to update this file: {0}",path);
			}
		}

		private void AddSelectorCssRule(StringBuilder sb, string selector, string fontName, bool isRtl, decimal lineHeight, bool breakOnlyAtSpaces, bool omitDirection)
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
				else	// Ensure proper directionality: see https://silbloom.myjetbrains.com/youtrack/issue/BL-6256.
					sb.AppendLine(" direction: ltr;");
			}
			if (lineHeight > 0)
				sb.AppendLine(" line-height: " + lineHeight.ToString(CultureInfo.InvariantCulture) + ";");

			if (breakOnlyAtSpaces)
				sb.AppendLine(" word-break: keep-all;");

			sb.AppendLine("}");
		}

		/// ------------------------------------------------------------------------------------
		public void Load()
		{
			try
			{
				// Previously was SIL.IO.RobustIO.LoadXElement(SettingsFilePath). However, we had problems with this
				// using some non-roman collection names...specifically, one involving the Northern Pashti
				// localization of 'books' (کتابونه)...see BL-5416. It seems that somewhere in the
				// implementation of Linq.XElement.Load() the path is converted to a URL and then back
				// to a path and something changes in that process so that a valid path passed to Load()
				// raises an invalid path exception. Reading the file directly and then parsing the string
				// works around this problem.
				var settingsContent = RobustFile.ReadAllText(SettingsFilePath, Encoding.UTF8);
				XElement library = XElement.Parse(settingsContent);

				Language1Iso639Code = GetValue(library, "Language1Iso639Code", /* old name */GetValue(library, "Language1Iso639Code", ""));
				Language2Iso639Code = GetValue(library, "Language2Iso639Code",  /* old name */GetValue(library, "National1Iso639Code", "en"));
				Language3Iso639Code = GetValue(library, "Language3Iso639Code",  /* old name */GetValue(library, "National2Iso639Code", ""));
				XMatterPackName = GetValue(library, "XMatterPack", "Factory");

				var style = GetValue(library, "PageNumberStyle", "Decimal");

				//for historical (and maybe future?) reasons, we collect the page number style as one of the
				//CSS counter number styles
				PageNumberStyle = CssNumberStylesToCultureOrDigits.Keys.Contains(style) ? style : "Decimal";

				BrandingProjectKey = GetValue(library, "BrandingProjectName", "Default");
				SubscriptionCode = GetValue(library, "SubscriptionCode", null);
				if (BrandingProjectKey == "Local Community")
				{
					// migrate for 4.4
					BrandingProjectKey = "Local-Community";
				}

				if (BrandingProjectKey != "Default" && BrandingProjectKey != "Local-Community")
				{
					// Validate branding, so things can't be circumvented by just typing something into settings
					var expirationDate = CollectionSettingsApi.GetExpirationDate(SubscriptionCode);
					if (expirationDate < DateTime.Now || BrandingProject.GetProjectChoices().All(bp => bp.Key != BrandingProjectKey))
					{
						InvalidBranding = BrandingProjectKey;
						BrandingProjectKey = "Default"; // keep the code, but don't use it as active branding.
					}
				}

				Language1Name = GetValue(library, "Language1Name",  /* old name */GetValue(library, "LanguageName", ""));
				Language2Name = GetValue(library, "Language2Name", GetLanguage2Name_NoCache(Language2Iso639Code));
				Language3Name = GetValue(library, "Language3Name", GetLanguage3Name_NoCache(Language2Iso639Code));
				DefaultLanguage1FontName = GetValue(library, "DefaultLanguage1FontName", GetDefaultFontName());
				DefaultLanguage2FontName = GetValue(library, "DefaultLanguage2FontName", GetDefaultFontName());
				DefaultLanguage3FontName = GetValue(library, "DefaultLanguage3FontName", GetDefaultFontName());
				OneTimeCheckVersionNumber = GetIntegerValue(library, "OneTimeCheckVersionNumber", 0);
				IsLanguage1Rtl = GetBoolValue(library, "IsLanguage1Rtl", false);
				IsLanguage2Rtl = GetBoolValue(library, "IsLanguage2Rtl", false);
				IsLanguage3Rtl = GetBoolValue(library, "IsLanguage3Rtl", false);
				Language1LineHeight = GetDecimalValue(library, "Language1LineHeight", 0);
				Language2LineHeight = GetDecimalValue(library, "Language2LineHeight", 0);
				Language3LineHeight = GetDecimalValue(library, "Language3LineHeight", 0);
				Language1BreaksLinesOnlyAtSpaces = GetBoolValue(library, "Language1BreaksLinesOnlyAtSpaces", false);
				Language2BreaksLinesOnlyAtSpaces = GetBoolValue(library, "Language2BreaksLinesOnlyAtSpaces", false);
				Language3BreaksLinesOnlyAtSpaces = GetBoolValue(library, "Language3BreaksLinesOnlyAtSpaces", false);

				Country = GetValue(library, "Country", "");
				Province = GetValue(library, "Province", "");
				District = GetValue(library, "District", "");
				AllowNewBooks = GetBoolValue(library, "AllowNewBooks", true);
				IsSourceCollection = GetBoolValue(library, "IsSourceCollection", GetBoolValue(library, "IsShellLibrary" /*the old name*/, GetBoolValue(library, "IsShellMakingProject" /*an even older name*/, false)));

				string audioRecordingModeStr = GetValue(library, "AudioRecordingMode", "Unknown");
				TalkingBookApi.AudioRecordingMode parsedAudioRecordingMode;
				if (!Enum.TryParse(audioRecordingModeStr, out parsedAudioRecordingMode))
				{
					parsedAudioRecordingMode = TalkingBookApi.AudioRecordingMode.Unknown;
				}
				AudioRecordingMode = parsedAudioRecordingMode;
			}
			catch (Exception e)
			{
				string settingsContents = "";
				try
				{
					settingsContents = RobustFile.ReadAllText(SettingsFilePath);
				}
				catch (Exception error)
				{
					settingsContents = error.Message;
				}
				Logger.WriteEvent("Contents of "+SettingsFilePath+": /r/n"+ settingsContents);
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(e, "There was an error reading the file {0}.  Please report this error to the developers. To get access to your books, you should make a new collection, then copy your book folders from this broken collection into the new one, then run Bloom again.",SettingsFilePath);
				throw;
			}

			try
			{
				string oldcustomCollectionStylesPath = FolderPath.CombineForPath("collection.css");
				if(RobustFile.Exists(oldcustomCollectionStylesPath))
				{
					string newcustomCollectionStylesPath = FolderPath.CombineForPath("customCollectionStyles.css");

					RobustFile.Move(oldcustomCollectionStylesPath, newcustomCollectionStylesPath);
				}
			}
			catch (Exception)
			{
				//ah well, we tried, no big deal, only a couple of beta testers used this old name
			}

			// Check if we need to do a one time check (perhaps migrate to a new Settings value)
			if (OneTimeCheckVersionNumber < kCurrentOneTimeCheckVersionNumber)
			{
				DoOneTimeCheck();
			}

			// Remove an obsolete page numbering rule if it exists in the collection styles file.
			// See https://issues.bloomlibrary.org/youtrack/issue/BL-5017.
			// Saving the styles doesn't write the obsolete rule, effectively removing it.  Doing
			// this unconditionally ensures any future similar problems are covered automatically.
			SaveSettingsCollectionStylesCss();

			SetAnalyticsProperties();
		}

		private void DoOneTimeCheck()
		{
			// If we ever have to do another one of these besides our minimal Andika New Basic migration
			// we should refactor this so it calls a method based on the OneTimeCheckVersionNumber
			do
			{
				if(!MigrateSettingsToAndikaNewBasicFont())
					break; // in case of failed migration
				OneTimeCheckVersionNumber++;

			} while (OneTimeCheckVersionNumber < kCurrentOneTimeCheckVersionNumber);
			Save(); // save updated settings
		}

		private bool MigrateSettingsToAndikaNewBasicFont()
		{
			const string newFont = "Andika New Basic";
			if (GetDefaultFontName() != newFont) // sanity check to make sure Andika New Basic is installed
				return false;

			const string id = "CollectionSettingsDialog.AndikaNewBasicUpdate";
			var basicMessage = LocalizationManager.GetDynamicString("Bloom", id + "1",
				"Bloom is switching the default font for \"{0}\" to the new \"Andika New Basic\".");
			var secondaryMessage = LocalizationManager.GetDynamicString("Bloom", id + "2",
				"This will improve the printed output for most languages. If your language is one of the few that need \"Andika\", you can switch it back in Settings:Book Making.");
			const string oldFont = "Andika";
			var safeLanguages = new[] {"en", "es", "fr", "id", "tpi"};
			string msg = string.Empty;
			if(DefaultLanguage1FontName == oldFont)
			{
				DefaultLanguage1FontName = newFont;
				if (!safeLanguages.Contains(Language1Iso639Code))
				{
					msg += String.Format(basicMessage, Language1Name) + Environment.NewLine;
				}
			}
			if (DefaultLanguage2FontName == oldFont)
			{
				DefaultLanguage2FontName = newFont;
				if (!String.IsNullOrEmpty(Language2Iso639Code) && !safeLanguages.Contains(Language2Iso639Code))
				{
					msg += String.Format(basicMessage, GetLanguage2Name(Language2Iso639Code)) + Environment.NewLine;
				}
			}
			if (DefaultLanguage3FontName == oldFont)
			{
				DefaultLanguage3FontName = newFont;
				if (!String.IsNullOrEmpty(Language3Iso639Code) && !safeLanguages.Contains(Language3Iso639Code))
				{
					msg += String.Format(basicMessage, GetLanguage3Name(Language2Iso639Code)) + Environment.NewLine;
				}
			}
			// Only notify the user if the change involves a language that is not known to be okay with
			// the new font.
			if (!String.IsNullOrEmpty(msg) && ErrorReport.IsOkToInteractWithUser)
			{
				msg += Environment.NewLine + secondaryMessage;
				// NB: this MessageBoxOptions.DefaultDesktopOnly option is more than the name implies. It changes the message to a "service message" which is the only
				// way I've found to get the box into the taskbar.
				MessageBox.Show(msg, "Bloom", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
			}
			return true;
		}

		private bool GetBoolValue(XElement library, string id, bool defaultValue)
		{
			string s = GetValue(library, id, defaultValue.ToString());
			bool b;
			bool.TryParse(s, out b);
			return b;
		}

		private int GetIntegerValue(XElement library, string id, int defaultValue)
		{
			var s = GetValue(library, id, defaultValue.ToString(CultureInfo.InvariantCulture));
			int i;
			int.TryParse(s, out i);
			return i;
		}

		private decimal GetDecimalValue(XElement library, string id, decimal defaultValue)
		{
			var s = GetValue(library, id, defaultValue.ToString(CultureInfo.InvariantCulture));
			decimal d;
			// REVIEW: if we localize the display of decimal values in the line-height combo box, then this
			// needs to handle the localized version of the number.  (This happens automatically by removing
			// the middle two arguments.)
			decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d);
			return d;
		}

		private string GetValue(XElement document, string id, string defaultValue)
		{
			var nodes = document.Descendants(id);
			if (nodes != null && nodes.Count() > 0)
				return nodes.First().Value;
			else
			{
				return defaultValue;
			}
		}


		public virtual string CollectionName { get; protected set; }

		[XmlIgnore]
		public string FolderPath
		{
			get { return Path.GetDirectoryName(SettingsFilePath); }
		}

		[XmlIgnore]
		public string SettingsFilePath { get; set; }

		/// <summary>
		/// for the "Factory-XMatter.htm", this would be named "Factory"
		/// </summary>
		virtual public string XMatterPackName { get; set; }

		virtual public string Country { get; set; }
		virtual public string Province { get; set; }
		virtual public string District { get; set; }

		public string VernacularCollectionNamePhrase
		{
			get
			{
				//review: in June 2013, I made it just use the collectionName regardless of the type. I wish I'd make a comment with the previous approach
				//explaining *why* we would wnat to just say, for example, "Foobar Books". Probably for some good reason.
				//But it left us with the weird situation of being able to chang the collection name in the settings, and have that only affect the  title
				//bar of the window (and the on-disk name). People wanted to change to a language name they want to see. (We'll probably have to do something
				//to enable that anyhow because it shows up elsewhere, but this is a step).
				//if(IsSourceCollection)
					return CollectionName;
				//var fmt = L10NSharp.LocalizationManager.GetString("CollectionTab.Vernacular Collection Heading", "{0} Books", "The {0} is where we fill in the name of the Vernacular");
				//return string.Format(fmt, Language1Name);
			}
		}

		public string DefaultLanguage1FontName { get; set; }

		public string DefaultLanguage2FontName { get; set; }

		public string DefaultLanguage3FontName { get; set; }

		public string PageNumberStyle { get; set; }

		public string BrandingProjectKey { get; set; }

		public string SubscriptionCode { get; set; }

		public int OneTimeCheckVersionNumber { get; set; }

		public bool AllowNewBooks { get; set; }

		public TalkingBookApi.AudioRecordingMode AudioRecordingMode { get; set; }

		public bool AllowDeleteBooks
		{
			get { return AllowNewBooks; } //at the moment, we're combining these two concepts; we can split them if a good reason to comes along
		}


		public static string GetPathForNewSettings(string parentFolderPath, string newCollectionName)
		{
			return parentFolderPath.CombineForPath(newCollectionName, newCollectionName + ".bloomCollection");
		}


		public static string RenameCollection(string fromDirectory, string toDirectory)
		{
			if (!Directory.Exists(fromDirectory))
			{
				throw new ApplicationException("Bloom could not complete the renaming of the collection, because there isn't a directory with the source name anymore: " + fromDirectory);
			}

			if (Directory.Exists(toDirectory)) //there's already a folder taking this name
			{
				throw new ApplicationException("Bloom could not complete the renaming of the collection, because there is already a directory with the new name: " + toDirectory);
			}

			//this is just a sanity check, it will throw if the existing directory doesn't have a collection
			FindSettingsFileInFolder(fromDirectory);

//first rename the directory, as that is the part more likely to fail (because *any* locked file in there will cause a failure)
			SIL.IO.RobustIO.MoveDirectory(fromDirectory, toDirectory);
			string  collectionSettingsPath;
			try
			{
				collectionSettingsPath = FindSettingsFileInFolder(toDirectory);
			}
			catch (Exception)
			{
				throw;
			}

			try
			{
				//we now make a default name based on the name of the directory
				string destinationPath = Path.Combine(toDirectory, Path.GetFileName(toDirectory)+".bloomCollection");
				if (!RobustFile.Exists(destinationPath))
					RobustFile.Move(collectionSettingsPath, destinationPath);

				return destinationPath;
			}
			catch (Exception error)
			{
				//change the directory name back, so the rename isn't half-done.
				SIL.IO.RobustIO.MoveDirectory(toDirectory, fromDirectory);
				throw new ApplicationException(string.Format("Could change the folder name, but not the collection file name",fromDirectory,toDirectory),error);
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

		public static string FindSettingsFileInFolder(string folderPath)
		{
			try
			{
				return Directory.GetFiles(folderPath, "*.bloomCollection").First();
			}
			catch (Exception)
			{
				throw new ApplicationException(string.Format("Bloom expected to find a .bloomCollectionFile in {0}, but there isn't one.", folderPath));
			}
		}

		internal LanguageDescriptor[] MakeLanguageUploadData(string[] isoCodes)
		{
			var result = new LanguageDescriptor[isoCodes.Length];
			for (int i = 0; i < isoCodes.Length; i++)
			{
				var code = isoCodes[i];
				string name = Language1Name;
				if (code != Language1Iso639Code)
					_lookupIsoCode.GetBestLanguageName(code, out name);
				string ethCode;
				LanguageSubtag data;
				if (!StandardSubtags.RegisteredLanguages.TryGet(code.ToLowerInvariant(), out data))
					ethCode = code;
				else
				{
					ethCode = data.Iso3Code;
					if (string.IsNullOrEmpty(ethCode))
						ethCode = code;
				}
				result[i] = new LanguageDescriptor() { IsoCode = code, Name = name, EthnologueCode = ethCode };
			}
			return result;
		}

		/// <summary>
		/// Given a choice, what language should we use to describe the license on the page (not in the UI, which is controlled by the UI Language)
		/// </summary>
		public IEnumerable<string> LicenseDescriptionLanguagePriorities
		{
			get
			{
				var result = new[] { Language1Iso639Code, Language2Iso639Code, Language3Iso639Code, "en" };
				// reverse-order loop so that given e.g. zh-Hans followed by zh-Hant we insert zh-CN after the second one.
				// That is, we'll prefer either of the explicit variants to the fall-back.
				// The part before the hyphen (if there is one) is the main language.
				for (int i = result.Length - 1; i >= 0; i--)
				{
					var fullLangTag = result[i];
					if (fullLangTag == null)
						continue;
					var language = fullLangTag.Split('-')[0]; // Generally insert corresponding language for longer culture
					if (language == "zh")
					{
						language = "zh-CN"; // Insert this instead for Chinese
					}
					if (result.IndexOf(language) >= 0)
						continue;
					var temp = result.ToList();
					temp.Insert(i + 1, language);
					result = temp.ToArray();
				}
				return result;
			}
		}

		/// <summary>
		/// The user settings can define a number system. This gives the digits, 0..9 of the selected system.
		/// </summary>
		public string CharactersForDigitsForPageNumbers
		{
			get
			{
				string info;
				if(CssNumberStylesToCultureOrDigits.TryGetValue(PageNumberStyle, out info))
				{
					// normal info.length gives 20 for chakma's 10 characters... I gather because it is converted to utf 16  and then
					// those bytes are counted? Here's all the info:
					// "In short, the length of a string is actually a ridiculously complex question and calculating it can take a lot of CPU time as well as data tables."
					// https://stackoverflow.com/questions/26975736/why-is-the-length-of-this-string-longer-than-the-number-of-characters-in-it
					var infoOnDigitsCharacters = new StringInfo(info);
					if (infoOnDigitsCharacters.LengthInTextElements == 10) // string of digits
						return info; //we've just listed the digits out, no need to look up a culture

					if(infoOnDigitsCharacters.LengthInTextElements == 5) // Microsoft culture code
					{
						try
						{
							var digits = new CultureInfo(info).NumberFormat.NativeDigits;
							Debug.Assert(digits.Length == 10);
							var joined = string.Join("", digits);
							Debug.Assert(joined.Length == 10);
							return joined;
						}
						catch(CultureNotFoundException)
						{
							// fall through to default return value
						}
						catch(Exception)
						{
							//there's no scenario
							//where this is worth stopping people in their tracks. I just want a
							//problem report saying "Hey page numbers don't look right on this machine".
						}
					}
				}
				//Missing or malformed value for this identifier.
				return "0123456789";
			}
		}

		public bool HaveEnterpriseFeatures => this.BrandingProjectKey != "Default";

		/// <summary>
		/// The collection settings point to object which might not exist. For example, the xmatter pack might not exist.
		/// So this should be called as soon as it is OK to show some UI. It will find any dependencies it can't meet,
		/// revert them to defaults, and notify the user.
		/// </summary>
		public void CheckAndFixDependencies(BloomFileLocator bloomFileLocator)
		{
			var errorTemplate = LocalizationManager.GetString("Errors.XMatterNotFound",
					"This Collection called for Front/Back Matter pack named '{0}', but this version of Bloom does not have it, and Bloom could not find it on this computer. The collection has been changed to use the default Front/Back Matter pages.");
			var errorMessage = String.Format(errorTemplate, XMatterPackName);
			XMatterPackName = XMatterHelper.MigrateXMatterName(XMatterPackName);
			if(string.IsNullOrEmpty(XMatterHelper.GetXMatterDirectory(XMatterPackName, bloomFileLocator, errorMessage, false)))
			{
				this.XMatterPackName = kDefaultXmatterName;
				Save();
			}
		}

		/// <summary>
		/// Set some properties related to this collection, which will go out with every subsequent event
		/// </summary>
		public void SetAnalyticsProperties()
		{
			if (!Analytics.AllowTracking)
			{
				return; //e.g. in unit tests
			}
			// this is ambiguous with what country we are *in*. I'm preserving it for now so we don't have a discontinuity in the analytics database,
			// but then adding an unambiguous duplicate with CollectionCountry
			Analytics.SetApplicationProperty("Country", Country);
			Analytics.SetApplicationProperty("CollectionCountry", Country);
			Analytics.SetApplicationProperty("Language1Iso639Code", Language1Iso639Code);
			Analytics.SetApplicationProperty("Language2Iso639Code", Language2Iso639Code);
			Analytics.SetApplicationProperty("Language3Iso639Code", Language3Iso639Code ?? "---");
			Analytics.SetApplicationProperty("Language1Iso639Name", Language1Name);
			Analytics.SetApplicationProperty("BrandingProjectName", BrandingProjectKey);
		}
	}
}
