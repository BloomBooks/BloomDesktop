using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIL.Windows.Forms.WritingSystems;
using SIL.WritingSystems;

namespace Bloom.ToPalaso
{
	public static class LookupIsoCodeModelExtensions
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
	}
}
