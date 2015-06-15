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
		/// </summary>
		/// <returns></returns>
		public static string GetBestLanguageName(this LookupIsoCodeModel isoModel, string code)
		{
			var match = isoModel.GetExactLanguageMatch(code);
			if (match != null)
				return match.Name;
			var lang = isoModel.GetMatchingLanguages(code).FirstOrDefault(x => x.Code == code);
			if (lang != null)
				return lang.DesiredName;
			return code; // best name we can come up with is the code itself
		}
	}
}
