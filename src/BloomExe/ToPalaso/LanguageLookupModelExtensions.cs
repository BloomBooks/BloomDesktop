using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIL.Windows.Forms.WritingSystems;
using SIL.WritingSystems;

namespace Bloom.ToPalaso
{
	public static class LanguageLookupModelExtensions
	{
		/// <summary>
		/// A smarter way to get a name for an iso code. Recent rework on writing systems in libpalaso has
		/// apparently fixed much of our problems as StandardSubtags.TryGetLanguageFromIso3Code() finds 3-letter
		/// entries now. This adds fall-backs for 2-letter codes and strips off Script/Region/Variant codes.
		/// If we can't find ANY name, the out param is set to the isoCode itself, and we return false.
		/// </summary>
		/// <returns>true if it found a name</returns>
		public static bool GetBestLanguageName(this LanguageLookupModel isoModel, string isoCode, out string name)
		{
			// BL-8081/8096: Perhaps we got in here with Script/Region/Variant tag(s).
			// Try to get a match on the part of the isoCode up to the first hyphen.
			var codeToMatch = GetGeneralCode(isoCode.ToLowerInvariant());
			if (!string.IsNullOrEmpty(codeToMatch))
			{
				LanguageSubtag match;
				if (StandardSubtags.TryGetLanguageFromIso3Code(codeToMatch, out match))
				{
					name = match.Name;
					return true;
				}
				// Perhaps we only have a 2-letter code (e.g. 'fr'), in that case, this will likely find it.
				if (StandardSubtags.RegisteredLanguages.TryGet(codeToMatch, out match))
				{
					name = match.Name;
					return true;
				}
			}
			name = isoCode; // At this point, the best name we can come up with is the isoCode itself.
			return false;
		}

		private static Dictionary<Tuple<string, string>, string> _mapIsoCodesToLanguageName = new Dictionary<Tuple<string, string>, string>();
#if USING_ICU
		private static Dictionary<string, Icu.Locale> _mapCodeToIcuLocale = new Dictionary<string, Icu.Locale>();
#endif

		/// <summary>
		/// Get the language name in the indicated language if possible. [n.b., it isn't without ICU or something similar]
		/// Otherwise, get the name in the language itself if possible.
		/// It that doesn't work, return the English name.
		/// If we don't know even that, return the code as the name.
		/// </summary>
		/// <remarks>
		/// This method is called by the CollectionSettings dialog to get the language names displayed
		/// on two on its tabs for the vernacular, national, and regional languages.  Returning the
		/// English name (from the Ethnologue data via SIL.Windows.Forms.WritingSystems.LanguageLookupModel)
		/// is the best we can do for most minor languages and what was done for all languages before
		/// this method was written.  Unfortunately, the name and inputs of this method remain more of
		/// an aspiration than a reality.  But returning the names of languages that the system (or
		/// our code) does know about in the languages themselves is less objectional (as in less
		/// blatantly ethnocentric) than always returning the English name for every language.
		/// This method is also called by the WorkspaceView class to get the English names of
		/// languages.
		/// </remarks>
		public static string GetLocalizedLanguageName(this LanguageLookupModel isoModel, string code, string uiCode)
		{
#if USING_ICU
			// ICU is cheaply available on Linux, but very expensive on Windows (adds ~28MB to the Bloom installer).
			// But it's the only solution that doesn't seem to require coding up our own generalized fix.
			// I'm leaving the code here in case a workable Windows solution to providing ICU is found.
			// But we want identical behavior as much as possible on both platforms, so this code is not
			// used on Linux either.
			string icuCode = code.Replace("-", "_");
			string icuDisplayCode = uiCode.Replace("-", "_");
			Icu.Locale locale;
			if (!_mapCodeToIcuLocale.TryGetValue(icuCode, out locale))
			{
				locale = new Icu.Locale(icuCode);
				_mapCodeToIcuLocale.Add(icuCode, locale);
			}
			Icu.Locale displayLocale;
			if (!_mapCodeToIcuLocale.TryGetValue(icuDisplayCode, out displayLocale))
			{
				displayLocale = new Icu.Locale(icuDisplayCode);
				_mapCodeToIcuLocale.Add(icuDisplayCode, displayLocale);
			}
			return locale.GetDisplayName(displayLocale);
#endif
			var key = new Tuple<string, string>(code, uiCode);
			string langName;
			if (_mapIsoCodesToLanguageName.TryGetValue(key, out langName))
				return langName;
			var generalUiCode = GetGeneralCode(uiCode);
			try
			{
				var generalCode = GetGeneralCode(code);
				var ci = CultureInfo.GetCultureInfo(generalCode);
				// We are depending on CultureInfo.DisplayName returning the language name in the current
				// UI language implicitly.  (The current UI language should match uiCode.)
				// CultureInfo.DisplayName is known to be broken in Mono as it always returns EnglishName.
				// I can't tell that Windows behaves any differently, but maybe it does if the system is
				// installed as a Spanish language, French language, or whatever language, system.
				// If DisplayName does not do what we want (but returns the same as EnglishName), then
				// we return just NativeName if it exists.  This seems less objectionable (ethnocentric)
				// than returning the EnglishName value.
				langName = ci.DisplayName;
				if (langName == ci.EnglishName && generalUiCode != "en")
					langName = FixBotchedNativeName(ci.NativeName);
				if (String.IsNullOrWhiteSpace(langName))
					langName = ci.EnglishName;
				if (!ci.EnglishName.StartsWith("Unknown Language"))	// Windows .Net behavior
				{
					_mapIsoCodesToLanguageName.Add(key, langName);
					return langName;
				}
			}
			catch (Exception e)
			{
				// ignore exception, but log on terminal.
				Console.WriteLine(@"GetLocalizedLanguageName ignoring exception: {0}", e.Message);
			}
			// We get here after either an exception was thrown or the returned CultureInfo
			// helpfully told us it is for an unknown language (instead of throwing).
			// Handle a few languages that we do know the English and native names for,
			// and that are being localized for Bloom.
			langName = GetNativeNameIfKnown(code);
			if (generalUiCode == "en" || String.IsNullOrWhiteSpace(langName))
				langName = GetEnglishNameIfKnown(isoModel, code);
			if (String.IsNullOrWhiteSpace(langName))
				langName = code;
			_mapIsoCodesToLanguageName.Add(key, langName);
			return langName;
		}

		private static Dictionary<string, string> _mapIsoCodeToSubtitledLanguageName = new Dictionary<string, string>();

		/// <summary>
		/// Get the language name in its own language and script if possible.  If it's not a Latin
		///     script, add an English name suffix.
		/// If we don't know a native name, but do know an English name, return the language code
		///     with an English name suffix.
		/// If we know nothing, return the language code.
		/// </summary>
		/// <remarks>
		/// This might be easier to implement reliably with ICU for a larger set of languages, but we
		/// can't use that approach for the reasons given in the previous method.
		/// This method is used to generate menu labels in the UI language chooser menu, which are
		///     mostly (but not entirely) major languages known to both Windows and Linux.
		/// GetEnglishNameIfKnown and GetNativeNameIfKnown may need to be updated if localizations are
		///     done into regional (or national) languages of some countries.
		/// </remarks>
		public static string GetNativeLanguageNameWithEnglishSubtitle(this LanguageLookupModel isoModel, string code)
		{
			string langName;
			if (_mapIsoCodeToSubtitledLanguageName.TryGetValue(code, out langName))
				return langName;
			string nativeName;
			var generalCode = GetGeneralCode(code);
			try
			{
				// englishNameSuffix is always an empty string if we don't need it.
				string englishNameSuffix = String.Empty;
				var ci = CultureInfo.GetCultureInfo(generalCode);	// this may throw or produce worthless empty object
				if (NeedEnglishSuffixForLanguageName(ci))
					englishNameSuffix = " (" + ci.EnglishName + ")";
				nativeName = FixBotchedNativeName(ci.NativeName);
				if (String.IsNullOrWhiteSpace(nativeName))
					nativeName = code;
				// Remove any country (or script?) names apart from Chinese (Simplified)
				if (ci.Name != "zh-CN")
				{
					var idxCountry = englishNameSuffix.LastIndexOf(" (");
					if (englishNameSuffix.Length > 0 && idxCountry > 0)
						englishNameSuffix = englishNameSuffix.Substring(0, idxCountry) + ")";
					idxCountry = nativeName.IndexOf(" (");
					if (idxCountry > 0)
						nativeName = nativeName.Substring(0, idxCountry);
				}
				else
				{
					// I have seen more cruft after the country name a few times, so remove that as well.
					// The parenthetical expansion always seems to start "(Simplified", which we want to keep.
					// We need double close parentheses because there's one open parenthesis before
					// "Chinese" and another open parenthesis before "Simplified" (which precedes ", China").
					// Also, we don't worry about the parenthetical content of the native Chinese name.
					var idxCountry = englishNameSuffix.IndexOf(", China");
					if (englishNameSuffix.Length > 0 && idxCountry > 0)
						englishNameSuffix = englishNameSuffix.Substring(0, idxCountry) + "))";
				}
				langName = nativeName + englishNameSuffix;
				if (!ci.EnglishName.StartsWith("Unknown Language"))	// Windows .Net behavior
				{
					_mapIsoCodeToSubtitledLanguageName.Add(code, langName);
					return langName;
				}
			}
			catch (Exception e)
			{
				// ignore exception, but log on terminal.
				Console.WriteLine(@"GetNativeLanguageNameWithEnglishSubtitle ignoring exception: {0}", e.Message);
			}
			// We get here after either an exception was thrown or the returned CultureInfo
			// helpfully told us it is for an unknown language (instead of throwing).
			// Handle a few languages that we do know the English and native names for,
			// and that are being localized for Bloom.
			var englishName = GetEnglishNameIfKnown(isoModel, generalCode);
			nativeName = GetNativeNameIfKnown(generalCode);
			if (String.IsNullOrWhiteSpace(nativeName) && String.IsNullOrWhiteSpace(englishName))
			{
				langName = code;
			}
			else if (String.IsNullOrWhiteSpace(nativeName))
			{
				langName = code + " (" + englishName + ")";
			}
			else if (String.IsNullOrWhiteSpace(englishName))
			{
				// I don't think this will ever happen...
				if (IsLatinChar(nativeName[0]))
					langName = nativeName;
				else
					langName = nativeName + " (" + code + ")";
			}
			else
			{
				if (IsLatinChar(nativeName[0]))
					langName = nativeName;
				else
					langName = nativeName + " (" + englishName + ")";
			}
			_mapIsoCodeToSubtitledLanguageName.Add(code, langName);
			return langName;
		}

		/// <summary>
		/// Check whether we need to add an English suffix to the native language name.  This is true if we don't know
		/// the native name at all or if the native name is not in a Latin alphabet.
		/// </summary>
		private static bool NeedEnglishSuffixForLanguageName(CultureInfo ci)
		{
			if (String.IsNullOrWhiteSpace(ci.NativeName))
				return true;
			var testChar = ci.NativeName[0];
			return ci.EnglishName != ci.NativeName && !IsLatinChar(testChar);
		}

		/// <summary>
		/// Remove any country or script identifier from a language code, except leave zh-CN alone.
		/// </summary>
		public static string GetGeneralCode(string code)
		{
			var idxCountry = code.IndexOf("-");
			if (idxCountry == -1 || code == "zh-CN")
				return code;
			return code.Substring(0, idxCountry);
		}

		/// <summary>
		/// Return true for ASCII, Latin-1, Latin Ext. A, Latin Ext. B, IPA Extensions, and Spacing Modifier Letters.
		/// </summary>
		public static bool IsLatinChar(char test)
		{
			return ((int)test <= 0x02FF);
		}

		/// <summary>
		/// For what languages we know about, return the English name.  If we don't know anything, return null.
		/// This is called only when CultureInfo doesn't supply the information we need.
		/// </summary>
		private static string GetEnglishNameIfKnown(this LanguageLookupModel isoModel, string code)
		{
			string englishName;
			if (!isoModel.GetBestLanguageName(code, out englishName))
			{
				switch (code)
				{
				case "pbu":  englishName = "Northern Pashto";  break;
				case "prs":  englishName = "Dari";             break;
				case "tpi":  englishName = "New Guinea Pidgin English"; break;
				default:     englishName = null;               break;
				}
			}
			return englishName;
		}

		/// <summary>
		/// For the languages we know about, return the native name.  If we don't know anything, return null.
		/// (This applies only to languages that CultureInfo doesn't know about on at least one of Linux and
		/// Windows.)
		/// </summary>
		private static string GetNativeNameIfKnown(string code)
		{
			switch (code)
			{
			case "pbu":  return "پښتو";
			case "prs":  return "دری";
			case "tpi":  return "Tok Pisin";
			default:     return null;
			}
		}

		/// <summary>
		/// Fix any native language names that we know either .Net or Mono gets wrong.
		/// </summary>
		/// <remarks>
		/// At the moment, there's only one name we know .Net get wrong (but Mono gets right).
		/// </remarks>
		private static string FixBotchedNativeName(string name)
		{
			// See http://issues.bloomlibrary.org/youtrack/issue/BL-5223.
			if (name == "Indonesia")
				return "Bahasa Indonesia";
			return name;
		}
	}
}
