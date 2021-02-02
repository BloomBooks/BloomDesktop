using System.IO;
using System.Linq;
using System.Xml;
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
		[TestCase("pt", "pt", null, new[] { "pt", "pt", "en" })]
		[TestCase("en", "es", "de", new[] { "en", "es", "de", "en" })] // we don't really need it, but English is put at the end even if already present
		[TestCase("id", "es", "de", new[] { "id", "es", "de", "en" })] // more typical case where adding English is important
		[TestCase("zh-CN", "es", "de", new[] { "zh-CN", "es", "de", "en" })] // zh-CN does not require an insertion
		[TestCase("zh-Hans", "es", "de", new[] { "zh-Hans", "zh-CN", "es", "de", "en" })] // any other zh-X requires zh-CN to be inserted following it.
		[TestCase("es", "zh-Hans", "de", new[] { "es", "zh-Hans", "zh-CN", "de", "en" })] // try in all 3 positions.
		[TestCase("es", "id", "zh-Hant", new[] { "es", "id", "zh-Hant", "zh-CN", "en" })]
		[TestCase("es", "zh-Hans", "zh-Hant", new[] { "es", "zh-Hans", "zh-Hant", "zh-CN", "en" })] // if we have two locale-specific ones, the insertion should be after both.
		[TestCase("fr-CA", "es", "de", new[] { "fr-CA", "fr", "es", "de", "en" })]
		[TestCase("es", "fr-CA", "de", new[] { "es", "fr-CA", "fr", "de", "en" })]
		[TestCase("es", "id", "fr-LU", new[] { "es", "id", "fr-LU", "fr", "en" })]
		[TestCase("rub", "", "en", new [] { "rub", "en", "en" })] // don't stick in Russian as an alternative to an unrelated 3 letter code.
		// given two fr-X codes, insert fr after the last of them. The main point here is that fr should be tried after fr-FR and fr-LU.
		// But the result here is actually debatable: should es be preferred to fr in this case?
		// Maybe the right result is fr-FR, fr-LU, fr, es, en in this case, since the original order indicates that French is better than Spanish?
		// But it's a very obscure and unlikely case; I think we can live with what the current algorithm does.
		[TestCase("fr-FR", "es", "fr-LU", new[] {"fr-FR", "es", "fr-LU", "fr", "en" })]
		[TestCase("zh-Hans", "fr-CA", "es-SV", new[] { "zh-Hans", "zh-CN", "fr-CA", "fr", "es-SV", "es", "en" })] // all three!!
		[TestCase("fr", "fr-CA", "de", new[] { "fr", "fr-CA", "de", "en" })] // already have the fall-back, don't add again.

		// The following test cases are special cases for Pashto languages.
		// See comments in LicenseDescriptionLanguagePriorities.
		[TestCase("pbt", "pbt", null, new[] { "pbt", "pbu", "ps", "pus", "pbt", "en" })]
		[TestCase("pst", "pst", null, new[] { "pst", "pbu", "ps", "pus", "pst", "en" })]
		[TestCase("ps", "ps", null, new[] { "ps", "pbu", "pus", "ps", "en" })]
		[TestCase("pus", "pus", null, new[] { "pus", "pbu", "ps", "pus", "en" })]
		[TestCase("pbu", "pbu", null, new[] { "pbu", "ps", "pus", "pbu", "en" })]
		[TestCase("xyz", "pbu", "abc", new[] { "xyz", "pbu", "ps", "pus", "abc", "en" })]
		public void GetLanguagePrioritiesForTranslatedTextOnPage_GetsCorrectListOfLanguages(string lang1, string lang2, string lang3, string[] results)
		{
			var settings = CreateCollectionSettings(_folder.Path, "test");
			settings.Language1Iso639Code = lang1;
			settings.Language2Iso639Code = lang2;
			settings.Language3Iso639Code = lang3;
			Assert.That(settings.GetLanguagePrioritiesForTranslatedTextOnPage(), Is.EqualTo(results));
		}

		[TestCase("xyz", "abc", null, new[] { "abc", "en" })]
		public void GetLanguagePrioritiesForTranslatedTextOnPage_DoNotIncludeLang1_GetsCorrectListOfLanguages(string lang1, string lang2, string lang3, string[] results)
		{
			var settings = CreateCollectionSettings(_folder.Path, "test");
			settings.Language1Iso639Code = lang1;
			settings.Language2Iso639Code = lang2;
			settings.Language3Iso639Code = lang3;
			Assert.That(settings.GetLanguagePrioritiesForTranslatedTextOnPage(false), Is.EqualTo(results));
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

		[Test]
		public void CollectionLoad_HandlesOlderVersion()
		{
			var bloomCollectionFileContents =
				@"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
	<Language1Name>Babanki</Language1Name>
	<Language2Name>French</Language2Name>
	<Language3Name>Sogur</Language3Name>
	<Language1Iso639Code>bbk</Language1Iso639Code>
	<Language2Iso639Code>fr</Language2Iso639Code>
	<Language3Iso639Code>sok</Language3Iso639Code>
	<Language3IsCustomName>true</Language3IsCustomName>
	<DefaultLanguage1FontName>Some Strange African Font</DefaultLanguage1FontName>
	<DefaultLanguage2FontName>Andika New Basic</DefaultLanguage2FontName>
	<DefaultLanguage3FontName>Andika New Basic</DefaultLanguage3FontName>
	<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
	<IsLanguage1Rtl>true</IsLanguage1Rtl>
	<IsLanguage2Rtl>false</IsLanguage2Rtl>
	<Language2BaseUIFontSizeInPoints>12</Language2BaseUIFontSizeInPoints>
	<IsLanguage3Rtl>false</IsLanguage3Rtl>
	<Language1LineHeight>1.1</Language1LineHeight>
	<Language2LineHeight>0</Language2LineHeight>
	<Language3LineHeight>0</Language3LineHeight>
	<Language1BreaksLinesOnlyAtSpaces>true</Language1BreaksLinesOnlyAtSpaces>
	<Language2BreaksLinesOnlyAtSpaces>false</Language2BreaksLinesOnlyAtSpaces>
	<Language3BreaksLinesOnlyAtSpaces>false</Language3BreaksLinesOnlyAtSpaces>
</Collection>";
			const string collectionName = "test";
			var collectionPath = CollectionSettings.GetPathForNewSettings(_folder.Path, collectionName);
			Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
			RobustFile.WriteAllText(collectionPath, bloomCollectionFileContents);

			// SUT
			var settings = CreateCollectionSettings(_folder.Path, collectionName);

			// Verify Languages
			Assert.That(settings.Languages.Count(), Is.EqualTo(3));
			var firstLang = settings.Languages.First();
			Assert.That(firstLang.Iso639Code, Is.EqualTo("bbk"));
			Assert.That(firstLang.Name, Is.EqualTo("Babanki"));
			Assert.That(firstLang.IsRightToLeft, Is.True);
			Assert.That(firstLang.FontName, Is.EqualTo("Some Strange African Font"));
			Assert.That(firstLang.LineHeight, Is.EqualTo(1.1));
			Assert.That(firstLang.BreaksLinesOnlyAtSpaces, Is.True);
			Assert.That(firstLang.BaseUIFontSizeInPoints, Is.EqualTo(0));
			Assert.That(firstLang.IsCustomName, Is.False);
			var secondLang = settings.Languages.Skip(1).First();
			Assert.That(secondLang.Iso639Code, Is.EqualTo("fr"));
			Assert.That(secondLang.Name, Is.EqualTo("French"));
			Assert.That(secondLang.IsRightToLeft, Is.False);
			Assert.That(secondLang.FontName, Is.EqualTo("Andika New Basic"));
			Assert.That(secondLang.LineHeight, Is.EqualTo(0.0));
			Assert.That(secondLang.BreaksLinesOnlyAtSpaces, Is.False);
			Assert.That(secondLang.BaseUIFontSizeInPoints, Is.EqualTo(12));

			Assert.That(settings.Languages.Skip(2).First().IsCustomName, Is.True);

			// Check for default LanguageRoles
			Assert.That(settings.LanguageRoles.Count(), Is.EqualTo(2));
			var firstRole = settings.LanguageRoles.First();
			Assert.That(firstRole.Language, Is.EqualTo("bbk"));
			Assert.That(firstRole.Id, Is.EqualTo("content1"));
			var secondRole = settings.LanguageRoles.Last();
			Assert.That(secondRole.Language, Is.EqualTo("fr"));
			Assert.That(secondRole.Id, Is.EqualTo("contentNational1"));
		}

		[Test]
		public void CollectionLoad_HandlesNewVersion()
		{
			var bloomCollectionFileContents =
				@"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
	<Language1Name>Babanki</Language1Name>
	<Language2Name>French</Language2Name>
	<Language3Name></Language3Name>
	<Language1Iso639Code>bbk</Language1Iso639Code>
	<Language2Iso639Code>fr</Language2Iso639Code>
	<Language3Iso639Code></Language3Iso639Code>
	<DefaultLanguage1FontName>Some Strange African Font</DefaultLanguage1FontName>
	<DefaultLanguage2FontName>Andika New Basic</DefaultLanguage2FontName>
	<DefaultLanguage3FontName>Andika New Basic</DefaultLanguage3FontName>
	<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
	<IsLanguage1Rtl>true</IsLanguage1Rtl>
	<IsLanguage2Rtl>false</IsLanguage2Rtl>
	<Language2BaseUIFontSizeInPoints>12</Language2BaseUIFontSizeInPoints>
	<IsLanguage3Rtl>false</IsLanguage3Rtl>
	<Language1LineHeight>1.1</Language1LineHeight>
	<Language2LineHeight>0</Language2LineHeight>
	<Language3LineHeight>0</Language3LineHeight>
	<Language1BreaksLinesOnlyAtSpaces>true</Language1BreaksLinesOnlyAtSpaces>
	<Language2BreaksLinesOnlyAtSpaces>false</Language2BreaksLinesOnlyAtSpaces>
	<Language3BreaksLinesOnlyAtSpaces>false</Language3BreaksLinesOnlyAtSpaces>
	<!-- If this section is defined, we should ignore the other fields on load. -->
	<Languages>
		<Language tag='en' name='English' direction='ltr' lineHeight='0'
		breakLinesOnlyAtSpaces='false' defaultFontName='Andika New Basic' isCustomName='false' />
		<Language tag='fr' name='French' direction='ltr' lineHeight='0'
		breakLinesOnlyAtSpaces='false' defaultFontName='Andika New Basic' />
		<Language tag='th' name='Thai' direction='ltr' lineHeight='1.5'
		breakLinesOnlyAtSpaces='true' defaultFontName='Sarabun' />
		<Language tag='ar' name='Arabish' direction='rtl' lineHeight='1.2'
		breakLinesOnlyAtSpaces='true' baseUIFontSizeInPoints='12' defaultFontName='Scheherazade'
		isCustomName='true' />
	</Languages>
	<LanguageRoles>
		<LanguageRole id='content1' language='en' name='Ethnocentricity reigns!' />
		<LanguageRole id='contentNational1' language='th' name='National Language' />
		<LanguageRole id='contentNational2' language='ar' name='Regional Language' />
		<LanguageRole id='meta1' language='' name='Title, Credits, Back Cover, etc.' />
	</LanguageRoles>
</Collection>";
			const string collectionName = "test";
			var collectionPath = CollectionSettings.GetPathForNewSettings(_folder.Path, collectionName);
			Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
			RobustFile.WriteAllText(collectionPath, bloomCollectionFileContents);

			// SUT
			var settings = CreateCollectionSettings(_folder.Path, collectionName);

			// Verify Languages
			Assert.That(settings.Languages.Count(), Is.EqualTo(4));
			var firstLang = settings.Languages.First();
			Assert.That(firstLang.Iso639Code, Is.EqualTo("en"));
			Assert.That(firstLang.Name, Is.EqualTo("English"));
			Assert.That(firstLang.IsRightToLeft, Is.False);
			Assert.That(firstLang.IsCustomName, Is.False);
			Assert.That(firstLang.FontName, Is.EqualTo("Andika New Basic"));
			Assert.That(firstLang.LineHeight, Is.EqualTo(0));
			Assert.That(firstLang.BreaksLinesOnlyAtSpaces, Is.False);
			Assert.That(firstLang.BaseUIFontSizeInPoints, Is.EqualTo(0));
			var thirdLang = settings.Languages.Skip(2).First();
			Assert.That(thirdLang.Iso639Code, Is.EqualTo("th"));
			Assert.That(thirdLang.Name, Is.EqualTo("Thai"));
			Assert.That(thirdLang.IsRightToLeft, Is.False);
			Assert.That(thirdLang.FontName, Is.EqualTo("Sarabun"));
			Assert.That(thirdLang.LineHeight, Is.EqualTo(1.5));
			Assert.That(thirdLang.BreaksLinesOnlyAtSpaces, Is.True);
			var lastLang = settings.Languages.Last();
			Assert.That(lastLang.Iso639Code, Is.EqualTo("ar"));
			Assert.That(lastLang.Name, Is.EqualTo("Arabish"));
			Assert.That(lastLang.IsRightToLeft, Is.True);
			Assert.That(lastLang.IsCustomName, Is.True);
			Assert.That(lastLang.FontName, Is.EqualTo("Scheherazade"));
			Assert.That(lastLang.LineHeight, Is.EqualTo(1.2));
			Assert.That(lastLang.BreaksLinesOnlyAtSpaces, Is.True);
			Assert.That(lastLang.BaseUIFontSizeInPoints, Is.EqualTo(12));

			// Check for correctly imported LanguageRoles
			Assert.That(settings.LanguageRoles.Count(), Is.EqualTo(4));
			var firstRole = settings.LanguageRoles.First();
			Assert.That(firstRole.Language, Is.EqualTo("en"));
			Assert.That(firstRole.Id, Is.EqualTo("content1"));
			Assert.That(firstRole.Name, Is.EqualTo("Ethnocentricity reigns!"));
			var secondRole = settings.LanguageRoles.Skip(1).First();
			Assert.That(secondRole.Language, Is.EqualTo("th"));
			Assert.That(secondRole.Id, Is.EqualTo("contentNational1"));
			Assert.That(secondRole.Name, Is.EqualTo("National Language"));
			var lastRole = settings.LanguageRoles.Last();
			Assert.That(lastRole.Language, Is.EqualTo(""));
			Assert.That(lastRole.Id, Is.EqualTo("meta1"));
			Assert.That(lastRole.Name, Is.EqualTo("Title, Credits, Back Cover, etc."));
		}

		[Test]
		public void CollectionSave_UpdatedVersion()
		{
			const string collectionName = "test2";
			var settings = CreateCollectionSettings(_folder.Path, collectionName);
			settings.Language1Iso639Code = "sok";
			settings.Language1.IsRightToLeft = true;
			settings.Language2Iso639Code = "fr";

			// SUT
			settings.Save();

			// Verify
			var collectionPath = Path.Combine(settings.FolderPath, collectionName + ".bloomCollection");
			var assertFileContents = AssertThatXmlIn.File(collectionPath);

			assertFileContents.HasSpecifiedNumberOfMatchesForXpath("//*[@deprecated='true']", 24);
			assertFileContents.HasSpecifiedNumberOfMatchesForXpath("//Language1Iso639Code[@deprecated='true' and text()='sok']", 1);
			assertFileContents.HasSpecifiedNumberOfMatchesForXpath("//Language1Name[@deprecated='true' and text()='Sokoro']", 1);
			assertFileContents.HasSpecifiedNumberOfMatchesForXpath("//Languages/Language", 2);
			assertFileContents.HasAtLeastOneMatchForXpath("//Languages/Language[@tag='sok']");
			assertFileContents.HasAtLeastOneMatchForXpath("//Languages/Language[@tag='fr']");
			assertFileContents.HasNoMatchForXpath("//Languages/Language[@tag='en']");
			assertFileContents.HasAtLeastOneMatchForXpath("//LanguageRoles/LanguageRole[@id='content1' and @language='sok']");
			assertFileContents.HasAtLeastOneMatchForXpath("//LanguageRoles/LanguageRole[@id='contentNational1' and @language='fr']");
		}
	}
}
