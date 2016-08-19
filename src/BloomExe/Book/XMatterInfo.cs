using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using L10NSharp;
using SIL.IO;

namespace Bloom.Book
{
	public class XMatterInfo
	{
		public readonly string PathToFolder;

		public XMatterInfo(string pathToFolder)
		{
			PathToFolder = pathToFolder;

		}

		public override string ToString()
		{
			return Key;
		}
		/// <summary>
		/// E.g. in "Factory-XMatter", the key is "Factory".
		/// </summary>
		public string Key
		{
			get {
				var x = Path.GetFileName(PathToFolder);
				var end = x.ToLowerInvariant().IndexOf("-xmatter");
				return x.Substring(0, end);
			}
		}

		public string EnglishLabel
		{
			get
			{
				var x = Path.GetFileName(PathToFolder);
				var end = x.ToLowerInvariant().IndexOf("-xmatter");
				var label =  x.Substring(0, end);
				if (label == "Factory") //historical name
				{
					label= "PaperSaver";
				}
				return SplitCamelCase(label);
			}
		}

		// from http://stackoverflow.com/a/5796793/723299
		private static string SplitCamelCase(string str)
		{
			return Regex.Replace(
				Regex.Replace(
					str,
					@"(\P{Ll})(\P{Ll}\p{Ll})",
					"$1 $2"
				),
				@"(\p{Ll})(\P{Ll})",
				"$1 $2"
			);
		}
		public string GetDescription()
		{
			const string desc = "description";
			try
			{
				// try to read English XMatter pack description first
				// we need version number at least
				var pathEnglish = Path.Combine(PathToFolder, desc + "-en.txt");
				if (!RobustFile.Exists(pathEnglish))
					return string.Empty;

				var englishDescription = RobustFile.ReadAllText(pathEnglish);
				if (!englishDescription.StartsWith("[V"))
					return englishDescription;

				// Once we put [V1] in the english description we could have translations
				// for other UI languages.
				var uiLangId = LocalizationManager.UILanguageId;
				var enVersion = GetVersionNumberString(englishDescription);
				englishDescription = StripVersionOff(englishDescription);
				var pathUiLang = Path.Combine(PathToFolder, desc + "-" + uiLangId + ".txt");
				if (uiLangId == "en" || !RobustFile.Exists(pathUiLang))
					return englishDescription;

				var uiLangDescription = RobustFile.ReadAllText(pathUiLang);
				var uiVersion = GetVersionNumberString(uiLangDescription);
				uiLangDescription = StripVersionOff(uiLangDescription);
				return enVersion > uiVersion || uiVersion == 0 || uiLangDescription.Length < 2 ? englishDescription : uiLangDescription;
			}
			catch (Exception error)
			{
				return error.Message;
			}
		}

		private static int GetVersionNumberString(string fullDescription)
		{
			if (!fullDescription.StartsWith("[V"))
				return 0;
			var endIndex = fullDescription.IndexOf("]", StringComparison.InvariantCulture);
			if (endIndex < 2)
				return 0;
			int result;
			return Int32.TryParse(fullDescription.Substring(2, endIndex - 2),
				NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
		}

		private static string StripVersionOff(string fullDescription)
		{
			if (!fullDescription.StartsWith("[V"))
				return fullDescription;
			var closeBracketIndex = fullDescription.IndexOf("]", StringComparison.InvariantCulture);
			return (closeBracketIndex > 2 && fullDescription.Length > 4)
				? fullDescription.Substring(closeBracketIndex + 1)
				: fullDescription;
		}
	}
}