using System.Collections.Generic;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests
{
	/// <summary>
	/// As yet this is a very incomplete set of tests for this class, but it makes sure we don't regress on problems we've encountered,
	/// and documents at least some of the things we think important.
	/// </summary>
	[TestFixture]
	public class BloomFileLocatorTests
	{
		private BloomFileLocator _fileLocator;
		private XMatterPackFinder _xMatterFinder;
		private TemporaryFolder _xMatterParentFolder;
		private TemporaryFolder _xMatterFolder;
		private TemporaryFolder _otherFilesForTestingFolder;

		[SetUp]
		public void Setup()
		{
			var locations = new List<string>();
			locations.Add(BloomFileLocator.GetInstalledXMatterDirectory());
			_xMatterParentFolder = new TemporaryFolder("UserCollection");
			_xMatterFolder = new TemporaryFolder(_xMatterParentFolder, "User-XMatter");
			locations.Add(_xMatterParentFolder.Path);
			RobustFile.WriteAllText(Path.Combine(_xMatterFolder.Path, "SomeRandomXYZABCStyles.css"), "Some arbitrary test data");
			RobustFile.WriteAllText(Path.Combine(_xMatterFolder.Path, "Decodable Reader.css"), "Fake DR test data");
			//locations.Add(XMatterAppDataFolder);
			//locations.Add(XMatterCommonDataFolder);
			_xMatterFinder = new XMatterPackFinder(locations);

			_otherFilesForTestingFolder = new TemporaryFolder("BloomFileLocatorTests");
			var userInstalledSearchPaths = new List<string>( ProjectContext.GetFoundFileLocations());
			userInstalledSearchPaths.Add(_otherFilesForTestingFolder.Path);
			_fileLocator = new BloomFileLocator(new CollectionSettings(), _xMatterFinder, ProjectContext.GetFactoryFileLocations(), userInstalledSearchPaths,
				ProjectContext.GetAfterXMatterFileLocations());

			//Without this, tests can interact with one another, leaving the language set as something unexpected.
			LocalizationManager.SetUILanguage("en", false);
		}

		[TearDown]
		public void Teardown()
		{
			_xMatterFolder.Dispose();
			_xMatterParentFolder.Dispose();
			_otherFilesForTestingFolder.Dispose();
		}

		/// <summary>
		/// This factory CSS is also found in various factory templates.
		/// </summary>
		[Test]
		public void FactoryXMatterStylesheets_AreFoundInFactoryXMatter()
		{
			var path = _fileLocator.LocateFile("Factory-XMatter.css");
			Assert.That(Path.GetDirectoryName(path), Is.StringContaining("Factory-XMatter"));
		}

		/// <summary>
		/// It's not just one xMatter folder that has things we want to find before factory templates.
		/// </summary>
		[Test]
		public void BigBookXMatterStylesheets_AreFoundInBigBookXMatter()
		{
			var path = _fileLocator.LocateFile("BigBook-XMatter.css");
			Assert.That(Path.GetDirectoryName(path), Is.StringContaining("BigBook-XMatter"));
		}

		/// <summary>
		/// This file is found in a 'non-factory' xMatter location because the test set things
		/// up that way. It's also found in one of the Template folders that are searched
		/// AFTER factory xMatter. This test forced breaking the xMatterPackFinder list into
		/// factory and non-factory.
		/// </summary>
		[Test]
		public void TemplateFilesAreFoundInFactoryLocation()
		{
			var path = _fileLocator.LocateFile("Decodable Reader.css");
			Assert.That(Path.GetDirectoryName(path), Is.StringContaining("Decodable Reader"));
		}

		/// <summary>
		/// This is a very special case; there is an editMode.css in xMatter/BigBook-XMatter
		/// which should NOT be returned, as well as the correct one in BloomBrowserUI.
		/// This test forced BloomFileLocator to have a special list of folders to search
		/// AFTER (factory) xMatter folders.
		/// This test may need to be adjusted if we conclude that we really don't need
		/// BigBook-XMatter/editMode.css and remove it.
		/// </summary>
		[Test]
		public void BloomBrowserUIIsPrefferedOverFactorXMatter()
		{
			var path = _fileLocator.LocateFile("editMode.css");
			Assert.That(Path.GetDirectoryName(path).Replace("\\", "/"), Is.StringContaining(BloomFileLocator.BrowserRoot.Replace("\\", "/")));
		}

		/// <summary>
		/// Make sure we DO search the remaining xmatter paths.
		/// </summary>
		[Test]
		public void CanFindFileInUserXMatter()
		{
			var path = _fileLocator.LocateFile("SomeRandomXYZABCStyles.css");
			Assert.That(Path.GetDirectoryName(path), Is.StringContaining("User-XMatter"));
		}

		[Test]
		public void GetLocalizableFileDistributedWithApplication_CurrentlyInEnglish_FindsIt()
		{
			var path = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(false, "infoPages", "TrainingVideos-en.md");
			Assert.IsTrue(path.EndsWith("TrainingVideos-en.md"));
		}

		[Test]
		public void GetLocalizableFileDistributedWithApplication_DontHaveThatTranslation_GetEnglishOne()
		{
			LocalizationManager.SetUILanguage("gd", false);
			var path = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(false, "infoPages", "TrainingVideos-en.md");
			Assert.IsTrue(path.EndsWith("TrainingVideos-en.md"));
		}

		[Test]
		public void GetBestLocalizedFile_EnglishIsCurrentLang_GetEnglishOne()
		{
			var englishPath = BloomFileLocator.DirectoryOfTheApplicationExecutable.CombineForPath(
				"../browser/templates/xMatter/Traditional-XMatter/description-en.txt");
			var bestLocalizedFile = BloomFileLocator.GetBestLocalizedFile(englishPath);
			Assert.AreEqual(englishPath, bestLocalizedFile);
		}

		[Test]
		public void GetBestLocalizedFile_DontHaveThatTranslation_GetEnglishOne()
		{
			LocalizationManager.SetUILanguage("gd", false);
			var englishPath = BloomFileLocator.DirectoryOfTheApplicationExecutable.CombineForPath(
				"../browser/templates/xMatter/Traditional-XMatter/description-en.txt");
			var bestLocalizedFile = BloomFileLocator.GetBestLocalizedFile(englishPath);
			Assert.AreEqual(englishPath, bestLocalizedFile);
		}

		[Test]
		public void GetBestLocalizedFile_HaveFrench_FindsIt()
		{
			LocalizationManager.SetUILanguage("fr", false);
			var englishPath = BloomFileLocator.DirectoryOfTheApplicationExecutable.CombineForPath(
				"../browser/templates/xMatter/Traditional-XMatter/description-en.txt");
			var bestLocalizedFile = BloomFileLocator.GetBestLocalizedFile(englishPath);
			Assert.IsTrue(bestLocalizedFile.Contains("-fr"));
		}
	}
}
