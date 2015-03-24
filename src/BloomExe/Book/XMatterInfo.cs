using System;
using System.IO;
using L10NSharp;

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

		public string GetDescription()
		{
			const string desc = "description-";
			try
			{
				// try to read English XMatter pack description first
				// we need version number at least
				var pathEnglish = Path.Combine(PathToFolder, desc + "en.txt");
				if (!File.Exists(pathEnglish))
					return string.Empty;

				var englishDescription = File.ReadAllText(pathEnglish);
				if (!englishDescription.StartsWith("[V"))
					return englishDescription;

				// Once we put [V1] in the english description we could have translations
				// for other UI languages.
				var uiLangId = LocalizationManager.UILanguageId;
				var enVersion = GetVersionNumberString(englishDescription);
				englishDescription = StripVersionOff(englishDescription);
				var pathUiLang = Path.Combine(PathToFolder, desc + uiLangId + ".txt");
				if (uiLangId == "en" || !File.Exists(pathUiLang))
					return englishDescription;

				var uiLangDescription = File.ReadAllText(pathUiLang);
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
			try
			{
				return Convert.ToInt32(fullDescription.Substring(2, endIndex - 2));
			}
			// Catch both known exceptions, since humans will create these version strings.
			catch (FormatException)
			{
				return 0;
			}
			catch (OverflowException)
			{
				return 0;
			}
		}

		private static string StripVersionOff(string fullDescription)
		{
			if (!fullDescription.StartsWith("[V"))
				return fullDescription;
			return fullDescription.Substring(fullDescription.IndexOf("]", StringComparison.InvariantCulture) + 1);
		}
	}
}