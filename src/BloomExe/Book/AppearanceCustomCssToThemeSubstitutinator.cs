using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using SIL.IO;
using SIL.Code;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Bloom.Book
{
	public class AppearanceCustomCssToThemeSubstitutinator
	{
		private Dictionary<string, string> _checksumsOfCustomCssToSubstitutionThemeNames = new Dictionary<string, string>();

		public AppearanceCustomCssToThemeSubstitutinator()
		{
			foreach(var path in ProjectContext.GetPathsToThemeFiles())
			{ 
				// read the first 20 lines of the file and extract the json from the comments
				var s = RobustFile.ReadAllText(path);
				var v = Regex.Match(s, "substitutionChecksums:.*\"(.*)\"");
				if(v.Groups.Count > 1)
				{
					var themeName = Path.GetFileName(path).Replace("appearance-theme-", "").Replace(".css","");
					var checksums = v.Groups[1].Value.Trim().Split(',')
						.Select(x => x.Trim()).ToList();

					foreach (var checksum in checksums)
					{
						_checksumsOfCustomCssToSubstitutionThemeNames.Add(checksum, themeName);
					}
				}
			}
		}
		public string GetThemeThatSubstitutesForCustomCSS(string css)
		{
			var checksum = GetChecksum(css);
			Debug.WriteLine("checksum of " + checksum);
			if(_checksumsOfCustomCssToSubstitutionThemeNames.ContainsKey(checksum))
			{
				return _checksumsOfCustomCssToSubstitutionThemeNames[checksum];
			}
			return null;
		}
		public static string GetChecksum(string css)
		{
			using (var md5 = MD5.Create())
			{
				var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(css));
				return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
			}
		}
	}
}
