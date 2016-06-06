using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using NUnit.Framework;
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

		[SetUp]
		public void Setup()
		{
			var locations = new List<string>();
			locations.Add(BloomFileLocator.GetInstalledXMatterDirectory());
			_xMatterParentFolder = new TemporaryFolder("UserCollection");
			_xMatterFolder = new TemporaryFolder(_xMatterParentFolder, "User-XMatter");
			locations.Add(_xMatterParentFolder.Path);
			File.WriteAllText(Path.Combine(_xMatterFolder.Path, "SomeRandomXYZABCStyles.css"), "Some arbitrary test data");
			File.WriteAllText(Path.Combine(_xMatterFolder.Path, "Decodable Reader.css"), "Fake DR test data");
			//locations.Add(XMatterAppDataFolder);
			//locations.Add(XMatterCommonDataFolder);
			_xMatterFinder = new XMatterPackFinder(locations);
			_fileLocator = new BloomFileLocator(new CollectionSettings(), _xMatterFinder, ProjectContext.GetFactoryFileLocations(), ProjectContext.GetFoundFileLocations(),
				ProjectContext.GetAfterXMatterFileLocations());
		}

		[TearDown]
		public void Teardown()
		{
			_xMatterFolder.Dispose();
			_xMatterParentFolder.Dispose();
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
	}
}
