using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Palaso.UI.WindowsForms.WritingSystems;

namespace Bloom.ToPalaso
{
	public static class LookupIsoCodeModelExtensions
	{
		/// <summary>
		/// A smarter way to get a name for an iso code. Currently LookupIsoCodeModel.GetExactLanguageMatch() does not find
		/// a name at all for some languages, typically macro ones. This adds a fall-back which still finds a language
		/// that has the exact requested language code.
		/// If we can't find ANY name, the out param is set to the code itself, and we return false.
		/// </summary>
		/// <returns>true if it found a name</returns>
		public static bool GetBestLanguageName(this LookupIsoCodeModel isoModel, string code, out string name)
		{
			var match = isoModel.GetExactLanguageMatch(code);
			if (match != null)
			{
				name = match.Name;
				return true;
			}
			var lang = isoModel.GetMatchingLanguages(code).FirstOrDefault(x => x.Code == code);
			if (lang != null)
			{
				name = lang.DesiredName;
				return true;
			}
			name = code; // best name we can come up with is the code itself
			return false;
		}
	}
}
