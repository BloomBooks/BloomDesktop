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
		/// A smarter way to get a name for an iso code. Currently StandardSubtags.RegisteredLanguages.TryGet does not find
		/// an entry at all using 3-letter codes. This adds a fall-back which still finds a language
		/// that has the exact requested language code.
		/// If we can't find ANY name, the out param is set to the code itself, and we return false.
		/// Possibly obsolete, I don't know whether the recent rework of writing systems in libpalaso fixed the problem.
		/// </summary>
		/// <returns>true if it found a name</returns>
		public static bool GetBestLanguageName(this LanguageLookupModel isoModel, string code, out string name)
		{
			LanguageSubtag match;
			var codeToMatch = code.ToLowerInvariant();
			if (StandardSubtags.RegisteredLanguages.TryGet(codeToMatch, out match))
			{
				name = match.Name;
				return true;
			}
			foreach (var lang in StandardSubtags.RegisteredLanguages)
			{
				if (lang.Iso3Code == codeToMatch)
				{
					name = lang.Name;
					return true;
				}
			}
			name = code; // best name we can come up with is the code itself
			return false;
		}

		private static Dictionary<Tuple<string, string>, string> _mapIsoCodesToLanguageName = new Dictionary<Tuple<string, string>, string>();
#if USING_ICU
		private static Dictionary<string, Icu.Locale> _mapCodeToIcuLocale = new Dictionary<string, Icu.Locale>();
#endif

		/// <summary>
		/// Get the language name in the indicated language if possible.  Otherwise, get the name in the language
		/// itself if possible.  It that doesn't work, return the English name.  If we don't even know that, return
		/// the code as the name.
		/// </summary>
		public static string GetLocalizedLanguageName(this LanguageLookupModel isoModel, string code, string uiCode)
		{
			return isoModel.GetLocalizedLanguageNameIfPossible(code, uiCode, false);
		}

		/// <summary>
		/// Get the language name in its own language and script if possible.  If it's not a Latin script, add an
		/// English name suffix.
		/// </summary>
		public static string GetNativeLanguageNameWithEnglishSubtitle(this LanguageLookupModel isoModel, string code)
		{
			return isoModel.GetLocalizedLanguageNameIfPossible(code, code, true);
		}

		private static string GetLocalizedLanguageNameIfPossible(this LanguageLookupModel isoModel, string code, string uiCode, bool addEnglishSubtitle)
		{
#if USING_ICU
			// ICU is cheaply available on Linux, but very expensive on Windows (adds ~28MB to the Bloom installer).
			// But it's the only solution that doesn't seem to require coding up our own generalized fix.
			// I'm leaving the code here in case a workable Windows solution to providing ICU is found.
			string icuCode = code.Replace("-", "_");
			string icuDisplayCode = codeOfUILanguage.Replace("-", "_");
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
			name = locale.GetDisplayName(displayLocale);
#endif
			string name = null;
			string englishName = String.Empty;
			string nativeName = null;
			Console.WriteLine("DEBUG LanguageLookup - code = {0}, uiCode = {1}, addEnglishSubtitle={2}", code, uiCode, addEnglishSubtitle);
			try
			{
				var generalUiCode = GetGeneralCode(uiCode);
				var generalCode = GetGeneralCode(code);
				var ci = CultureInfo.GetCultureInfo(generalCode);
				if (addEnglishSubtitle)
				{
					nativeName = ci.NativeName;
					var testChar = nativeName[0];
					if (ci.EnglishName != ci.NativeName && !IsLatinChar(testChar))
						englishName = " (" + ci.EnglishName + ")";
					// Remove any country (or script?) names apart from Chinese (Simplified)
					if (ci.Name != "zh-CN")
					{
						var idxCountry = englishName.IndexOf(" (");
						if (englishName.Length > 0 && idxCountry > 0)
							englishName = englishName.Substring(0, idxCountry) + ")";
						idxCountry = nativeName.IndexOf(" (");
						if (idxCountry > 0)
							nativeName = nativeName.Substring(0, idxCountry);
					}
					else
					{
						// some people have seen more cruft after the country name, so remove that as well.
						var idxCountry = englishName.IndexOf(", China");
						if (englishName.Length > 0 && idxCountry > 0)
							englishName = englishName.Substring(0, idxCountry) + "))";
					}
					name = nativeName + englishName;
				}
				else
				{
					// CultureInfo.DisplayName is known to be broken in Mono.  It always returns EnglishName.
					// I can't tell that Windows behaves any differently, but maybe it does if the system is
					// installed as a Spanish language, French language, or whatever language, system.
					name = ci.DisplayName;
					if (name == ci.EnglishName && generalUiCode != "en")
						name = ci.NativeName;
				}
				// See http://issues.bloomlibrary.org/youtrack/issue/BL-5223.
				if (name == "Indonesia")
					name = "Bahasa Indonesia";
				if (!name.StartsWith("Unknown Language"))
					return name;
			}
			catch (Exception e)
			{
				// ignore exception, but log on terminal.
				Console.WriteLine(@"LanguageLookup ignoring exception: {0}", e.Message);
			}
			if (!isoModel.GetBestLanguageName(code, out englishName))
			{
				switch (code)
				{
					case "pbu":  englishName = "Northern Pashto";  break;
					case "prs":  englishName = "Dari";             break;
					default:     return code;
				}
			}
			switch (code)
			{
				case "pbu":  nativeName = "پښتو";   break;
				case "prs":  nativeName = "دری";    break;
				default:     nativeName = null;     break;
			}
			if (addEnglishSubtitle)
			{
				if (nativeName != null)
					return nativeName + " (" + englishName + ")";
				return "(" + englishName + ")";
			}
			if (uiCode == "en" || nativeName == null)
				return englishName;
			return nativeName;
		}

		/// <summary>
		/// Remove any country or script identifier from a language code, except leave zh-CN alone.
		/// </summary>
		private static string GetGeneralCode(string code)
		{
			var idxCountry = code.IndexOf("-");
			if (idxCountry == -1 || code == "zh-CN")
				return code;
			return code.Substring(0, idxCountry);
		}

		/// <summary>
		/// Return true for ASCII, Latin-1, Latin Ext. A, Latin Ext. B, IPA Extensions, and Spacing Modifier Letters.
		/// </summary>
		private static bool IsLatinChar(char test)
		{
			return ((int)test <= 0x02FF);
		}
	}
}
