using System.IO;
using System.Text.RegularExpressions;
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
			Assert.AreEqual("French", settings.GetLanguage1Name("en"));
			settings.Language1Iso639Code = "en";
			Assert.AreEqual("English", settings.GetLanguage1Name("en"));
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
				"'xyz' is not in the approved list of numbering styles, should default to 'decimal'");
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
			VerifyCorrectCssRuleExists(collectionName, settings.PageNumberStyle.ToLower());
			settings = null; // dispose of the old one
			var newSettings = CreateCollectionSettings(_folder.Path, collectionName);
			Assert.AreEqual(style, newSettings.PageNumberStyle, "Numbering style 'Gurmukhi' should round trip");
		}

		private void VerifyCorrectCssRuleExists(string collectionName, string oldNumberStyle)
		{
			var mainFile = CollectionSettings.GetPathForNewSettings(_folder.Path, collectionName);
			var cssFile = Path.Combine(Path.GetDirectoryName(mainFile), "settingsCollectionStyles.css");
			var css = RobustFile.ReadAllText(cssFile);
			Assert.IsTrue(Regex.Match(css, @"\.numberedPage:after\s+{\s+content:\s+counter\(pageNumber,\s+" + oldNumberStyle + @"\);\s+}").Success,
							  "Css did not generate PageNumber style rule match.");
		}
	}
}
