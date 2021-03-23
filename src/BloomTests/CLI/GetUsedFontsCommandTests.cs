using System.IO;
using System.Linq;
using System.Reflection;
using Bloom.CLI;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.CLI
{
	class GetUsedFontsCommandTests
	{
		[TestCase(new[] { "en" }, new[] { "en" })]
		[TestCase(new[] { "en", "en" }, new[] { "en" })]
		[TestCase(new[] { "en" }, new[] { "en", "ta" })]
		[TestCase(new[] { "en" }, new[] { "abc", "en", "ta" })]
		[TestCase(new[] { "en", "abc" }, new[] { "abc", "en", "ta" })]
		[TestCase(new[] { "ta", "en", "abc" }, new[] { "abc", "en", "ta" })]
		[TestCase(new string[0], new[] { "abc", "en", "ta" })]
		public void GetFontsUsed_ExcludeFontForLanguageNotInBook(string[] languagesInHtml, string[] languagesInCss)
		{
			var htmlContents = $"<html><body><div class='bloom-page'>{string.Join("", languagesInHtml.Select(GetLangHtml))}</div></body></html>";
			var cssContents = string.Join("", languagesInCss.Select(GetLangCss));

			using (var bookFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
			{
				RobustFile.WriteAllText(Path.Combine(bookFolder.FolderPath, "book.htm"), htmlContents);
				RobustFile.WriteAllText(Path.Combine(bookFolder.FolderPath, "defaultLangStyles.css"), cssContents);

				var fontsUsed = GetUsedFontsCommand.GetFontsUsed(bookFolder.FolderPath).ToList();

				foreach (var langInBook in languagesInHtml)
					Assert.That(fontsUsed, Does.Contain($"{langInBook} Font"));

				foreach (var langNotInBook in languagesInCss.Except(languagesInHtml))
					Assert.That(fontsUsed, Does.Not.Contain($"{langNotInBook} Font"));
			}
		}

		private string GetLangHtml(string lang)
		{
			return $@"<div lang='{lang}' class='bloom-editable'>{lang} text</div>";
		}

		private string GetLangCss(string lang)
		{
			return $@"
[lang='{lang}']
{{
 font-family: '{lang} Font';
 direction: ltr;
}}";
		}
	}

}
