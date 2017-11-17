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
		/// Get the language name in the indicated language if possible. [n.b., it isn't without ICU or something similar]
		/// Otherwise, get the name in the language itself if possible.
		/// It that doesn't work, return the English name.
		/// If we don't even know that, return the code as the name.
		/// </summary>
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
				var name = ci.DisplayName;
				if (name == ci.EnglishName && generalUiCode != "en")
					name = ci.NativeName;
				name = FixBotchedName(name);
				if (String.IsNullOrEmpty(name))
					name = ci.EnglishName;
				if (!name.StartsWith("Unknown Language"))
					return name;
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
			var nativeName = GetBestNativeName(code);
			if (generalUiCode == "en" || nativeName == null)
				return GetBestEnglishName(isoModel, code);
			return nativeName;
		}

		/// <summary>
		/// Get the language name in its own language and script if possible.
		/// If it's not a Latin script, add an English name suffix.
		/// If we don't know a native name, but do know an English name, return the code with an English name suffix.
		/// If we know nothing, return the code.
		/// </summary>
		public static string GetNativeLanguageNameWithEnglishSubtitle(this LanguageLookupModel isoModel, string code)
		{
			string nativeName;
			var generalCode = GetGeneralCode(code);
			try
			{
				string englishNameSuffix = String.Empty;
				var ci = CultureInfo.GetCultureInfo(generalCode);
				nativeName = ci.NativeName;
				if (String.IsNullOrEmpty(nativeName))
					nativeName = code;
				var testChar = nativeName[0];
				if ((ci.EnglishName != ci.NativeName && !IsLatinChar(testChar)) || String.IsNullOrEmpty(ci.NativeName))
					englishNameSuffix = " (" + ci.EnglishName + ")";
				// Remove any country (or script?) names apart from Chinese (Simplified)
				if (ci.Name != "zh-CN")
				{
					var idxCountry = englishNameSuffix.IndexOf(" (");
					if (englishNameSuffix.Length > 0 && idxCountry > 0)
						englishNameSuffix = englishNameSuffix.Substring(0, idxCountry) + ")";
					idxCountry = nativeName.IndexOf(" (");
					if (idxCountry > 0)
						nativeName = nativeName.Substring(0, idxCountry);
				}
				else
				{
					// Some people have seen more cruft after the country name, so remove that as well.
					// We need double close parentheses because there's one open parenthesis before
					// "Chinese" and another open parenthesis before "Simplified" (which precedes ", China").
					var idxCountry = englishNameSuffix.IndexOf(", China");
					if (englishNameSuffix.Length > 0 && idxCountry > 0)
						englishNameSuffix = englishNameSuffix.Substring(0, idxCountry) + "))";
				}
				var name = nativeName + englishNameSuffix;
				name = FixBotchedName(name);
				if (!name.StartsWith("Unknown Language"))
					return name;
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
			var englishName = GetBestEnglishName(isoModel, generalCode);
			nativeName = GetBestNativeName(generalCode);
			if (!String.IsNullOrEmpty(nativeName))
			{
				if (!IsLatinChar(nativeName[0]))
					return nativeName + " (" + englishName + ")";
				return nativeName;
			}
			if (englishName != generalCode)
				return code + " (" + englishName + ")";
			return code;
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

		/// <summary>
		/// For what languages we know about, return the English name.  If we don't know anything, return the code.
		/// </summary>
		private static string GetBestEnglishName(this LanguageLookupModel isoModel, string code)
		{
			string englishName;
			if (!isoModel.GetBestLanguageName(code, out englishName))
			{
				switch (code)
				{
				case "pbu":  englishName = "Northern Pashto";  break;
				case "prs":  englishName = "Dari";             break;
				default:     englishName = code;               break;
				}
			}
			return englishName;
		}

		/// <summary>
		/// For the languages we know about, return the native name.  If we don't know anything, return null.
		/// </summary>
		private static string GetBestNativeName(string code)
		{
			switch (code)
			{
			case "pbu":  return "پښتو";
			case "prs":  return "دری";
			default:     return null;
			}
		}

		/// <summary>
		/// Fix any names that we know either .Net or Mono gets wrong.
		/// </summary>
		private static string FixBotchedName(string name)
		{
			// See http://issues.bloomlibrary.org/youtrack/issue/BL-5223.
			if (name == "Indonesia")
				return "Bahasa Indonesia";
			return name;
		}
	}
}
