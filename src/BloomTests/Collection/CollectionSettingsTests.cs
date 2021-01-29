using System.IO;
using Bloom.Book;
using Bloom.Collection;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Collection
{
	[TestFixture]
	public class CollectionSettingsTests
	{
		private TemporaryFolder _folder;

		[OneTimeSetUp]
		public void FixtureSetup()
		{
			SIL.Reporting.ErrorReport.IsOkToInteractWithUser = false;
			_folder = new TemporaryFolder("CollectionSettingsTests");
		}

		[OneTimeTearDown]
		public void Cleanup()
		{
			_folder.Dispose(); // fixture teardown
		}

		/// <summary>
		/// Creates a new CollectionSettings object. May or may not load a .bloomCollection file depending on
		/// whether one exists in the collection or not.
		/// </summary>
		/// <returns></returns>
		private CollectionSettings CreateCollectionSettings(string parentFolderPath, string collectionName)
		{
			return new CollectionSettings(CollectionSettings.GetPathForNewSettings(parentFolderPath, collectionName));
		}

		/// <summary>
		/// This is a regression test related to https://jira.sil.org/browse/BL-685.
		/// Apparently calculating the name is expensive, so it is cached. This
		/// test ensures that the cache doesn't keep the name from tracking the iso.
		/// </summary>
		[Test]
		public void Language1IsoCodeChanged_NameChangedToo()
		{
			const string collectionName = "test";
			var settings = CreateCollectionSettings(_folder.Path, collectionName);
			settings.Language1Iso639Code = "fr";
			Assert.AreEqual("French", settings.Language1.GetNameInLanguage("en"));
			settings.Language1Iso639Code = "en";
			Assert.AreEqual("English", settings.Language1.GetNameInLanguage("en"));
		}

		[Test]
		public void PageNumberStyle_BadNameInFile()
		{
			var bloomCollectionFileContents =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
	<PageNumberStyle>xyz</PageNumberStyle>
</Collection>";
			const string collectionName = "test";
			var collectionPath = CollectionSettings.GetPathForNewSettings(_folder.Path, collectionName);
			Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
			RobustFile.WriteAllText(collectionPath, bloomCollectionFileContents);
			var settings = CreateCollectionSettings(_folder.Path, collectionName);
			Assert.AreEqual("Decimal", settings.PageNumberStyle,
				"'xyz' is not in the approved list of numbering styles, should default to 'Decimal'");
		}

		[Test]
		public void PageNumberStyle_NotInFile()
		{
			var bloomCollectionFileContents =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
</Collection>";
			const string collectionName = "test";
			var collectionPath = CollectionSettings.GetPathForNewSettings(_folder.Path, collectionName);
			Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
			RobustFile.WriteAllText(collectionPath, bloomCollectionFileContents);
			var settings = CreateCollectionSettings(_folder.Path, collectionName);
			Assert.AreEqual("Decimal", settings.PageNumberStyle,
				"If the bloomCollection has no value for numbering style, assume 'decimal'");
		}

		[Test]
		public void PageNumberStyle_CanRoundTrip()
		{
			const string style = "Gurmukhi";
			const string collectionName = "test";
			var settings = CreateCollectionSettings(_folder.Path, collectionName);
			settings.Language1Iso639Code = "en";
			settings.PageNumberStyle = style;
			settings.Save();
			var newSettings = CreateCollectionSettings(_folder.Path, collectionName);
			Assert.AreEqual(style, newSettings.PageNumberStyle, "Numbering style 'Gurmukhi' should round trip");
		}


		[TestCase(null, null, null, new[] { "en" })]
		[TestCase("", "", "", new[] { "en" })]
		[TestCase("pt", "pt", null, new[] { "pt", "en" })]	// don't duplicate "pt"
		[TestCase("en", "es", "de", new[] { "en", "es", "de" })] // don't duplicate "en"
		[TestCase("id", "es", "de", new[] { "id", "es", "de", "en" })] // more typical case where adding English is important
		[TestCase("zh-CN", "es", "de", new[] { "zh-CN", "es", "de", "en" })] // zh-CN does not require an insertion
		[TestCase("zh-Hans", "es", "de", new[] { "zh-Hans", "zh-CN", "es", "de", "en" })] // any other zh-X requires zh-CN to be inserted following it.
		[TestCase("es", "zh-Hans", "de", new[] { "es", "zh-Hans", "zh-CN", "de", "en" })] // try in all 3 positions.
		[TestCase("es", "id", "zh-Hant", new[] { "es", "id", "zh-Hant", "zh-CN", "en" })]
		[TestCase("es", "zh-Hans", "zh-Hant", new[] { "es", "zh-Hans", "zh-Hant", "zh-CN", "en" })] // if we have two locale-specific ones, the insertion should be after both.
		[TestCase("fr-CA", "es", "de", new[] { "fr-CA", "fr", "es", "de", "en" })]
		[TestCase("es", "fr-CA", "de", new[] { "es", "fr-CA", "fr", "de", "en" })]
		[TestCase("es", "id", "fr-LU", new[] { "es", "id", "fr-LU", "fr", "en" })]
		[TestCase("rub", "", "en", new [] { "rub", "en" })] // don't stick in Russian as an alternative to an unrelated 3 letter code, and don't duplicate "en"
		// given two fr-X codes, insert fr after the last of them. The main point here is that fr should be tried after fr-FR and fr-LU.
		// But the result here is actually debatable: should es be preferred to fr in this case?
		// Maybe the right result is fr-FR, fr-LU, fr, es, en in this case, since the original order indicates that French is better than Spanish?
		// But it's a very obscure and unlikely case; I think we can live with what the current algorithm does.
		[TestCase("fr-FR", "es", "fr-LU", new[] {"fr-FR", "es", "fr-LU", "fr", "en" })]
		[TestCase("zh-Hans", "fr-CA", "es-SV", new[] { "zh-Hans", "zh-CN", "fr-CA", "fr", "es-SV", "es", "en" })] // all three!!
		[TestCase("fr", "fr-CA", "de", new[] { "fr", "fr-CA", "de", "en" })] // already have the fall-back, don't add again.

		// The following test cases are special cases for Pashto languages.
		// See comments in LicenseDescriptionLanguagePriorities.
		[TestCase("pbt", "pbt", null, new[] { "pbt", "pbu", "ps", "pus", "en" })]	// don't duplicate "pbt"
		[TestCase("pst", "pst", null, new[] { "pst", "pbu", "ps", "pus", "en" })]	// don't duplicate "pst"
		[TestCase("ps", "ps", null, new[] { "ps", "pbu", "pus", "en" })]	// don't duplicate "ps"
		[TestCase("pus", "pus", null, new[] { "pus", "pbu", "ps", "en" })]	// don't duplicate "pus"
		[TestCase("pbu", "pbu", null, new[] { "pbu", "ps", "pus", "en" })]	// don't duplicate "pbu"
		[TestCase("xyz", "pbu", "abc", new[] { "xyz", "pbu", "ps", "pus", "abc", "en" })]
		public void GetLanguagePrioritiesForTranslatedTextOnPage_GetsCorrectListOfLanguages(string lang1, string lang2, string lang3, string[] results)
		{
			var settings = CreateCollectionSettings(_folder.Path, "test");
			var bookData = new BookData(new HtmlDom("<html><body></body></html>"), settings, null);
			settings.Language1Iso639Code = lang1;
			settings.Language2Iso639Code = lang2;
			settings.Language3Iso639Code = lang3;
			Assert.That(bookData.GetLanguagePrioritiesForLocalizedTextOnPage(), Is.EqualTo(results));
		}

		[TestCase("xyz", "abc", null, new[] { "abc", "en" })]
		public void GetLanguagePrioritiesForTranslatedTextOnPage_DoNotIncludeLang1_GetsCorrectListOfLanguages(string lang1, string lang2, string lang3, string[] results)
		{
			var settings = CreateCollectionSettings(_folder.Path, "test");
			var bookData = new BookData(new HtmlDom("<html><body></body></html>"), settings, null);
			settings.Language1Iso639Code = lang1;
			settings.Language2Iso639Code = lang2;
			settings.Language3Iso639Code = lang3;
			Assert.That(bookData.GetLanguagePrioritiesForLocalizedTextOnPage(false), Is.EqualTo(results));
		}

		[TestCase("", "2")] // default
		[TestCase("Decimal", "2")]
		[TestCase("Devanagari", "२")]
		[TestCase("Khmer", "២")]
		[TestCase("Cjk-Decimal", "二")]
		public void CharactersForDigitsForPageNumbers_Tests(string numberStyleName, string digitForNumber2)
		{
			var settings = CreateCollectionSettings(_folder.Path, "test");
			settings.PageNumberStyle = numberStyleName;
			Assert.AreEqual(digitForNumber2, settings.CharactersForDigitsForPageNumbers.Substring(2,1));
		}
	}
}
