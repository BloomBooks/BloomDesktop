using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Api;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;
using Gecko;

namespace BloomTests.Edit
{
	[TestFixture]
#if __MonoCS__
	[Apartment(System.Threading.ApartmentState.STA)]
#endif
	public class ConfiguratorTest
	{
		private FileLocator _fileLocator;
		private BookStarter _starter;
		private TemporaryFolder _shellCollectionFolder;
		private TemporaryFolder _libraryFolder;

		[SetUp]
		public void Setup()
		{
			var library = new CollectionSettings
			{
				IsSourceCollection = false,
				Language2Iso639Code = "en",
				Language1Iso639Code = "xyz",
				XMatterPackName = "Factory"
			};

			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												//FileLocationUtilities.GetDirectoryDistributedWithApplication( "factoryCollections"),
												BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book"),
												BloomFileLocator.GetFactoryBookTemplateDirectory("Wall Calendar"),
												FileLocationUtilities.GetDirectoryDistributedWithApplication( BloomFileLocator.BrowserRoot),
												BloomFileLocator.GetBrowserDirectory("bookLayout"),
												BloomFileLocator.GetBrowserDirectory("bookEdit","css"),
												BloomFileLocator.GetInstalledXMatterDirectory()
											});

			var projectFolder = new TemporaryFolder("BookStarterTests_ProjectCollection");
			var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));

			_starter = new BookStarter(_fileLocator, (dir, forSelectedBook) => new BookStorage(dir, _fileLocator, new BookRenamedEvent(), collectionSettings), library);
			_shellCollectionFolder = new TemporaryFolder("BookStarterTests_ShellCollection");
			_libraryFolder = new TemporaryFolder("BookStarterTests_LibraryCollection");
		}

		[Test]
		public void IsConfigurable_Calendar_True()
		{
			Assert.IsTrue(Configurator.IsConfigurable(Get_NotYetConfigured_CalendardBookStorage().FolderPath));
		}

		[Test, Ignore("UI-By hand")]
		[STAThread]
		public void ShowConfigureDialog()
		{
			var c = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());

			var stringRep = DynamicJson.Serialize(new
			{
				library = new { calendar = new { year = "2088" } }
			});
			c.CollectJsonData(stringRep);

			c.ShowConfigurationDialog(Get_NotYetConfigured_CalendardBookStorage().FolderPath);
			Assert.IsTrue(c.GetLibraryData().Contains("year"));
		}


		[Test]
		public void GetAllData_LocalOnly_ReturnLocal()
		{
			var c = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			dynamic j = new DynamicJson();
			j.one = 1;
			c.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(c.GetAllData()));
		}

		[Test]
		public void LibrarySettingsAreRoundTriped()
		{
			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			var stringRep = DynamicJson.Serialize(new
						{
							library = new {stuff = "foo"}
						});

			first.CollectJsonData(stringRep.ToString());

			var second = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetLibraryData());
			Assert.AreEqual("foo", j.library.stuff);
		}

		[Test]
		public void CollectJsonData_NewTopLevelData_DataMerged()
		{
			var firstData = DynamicJson.Serialize(new
			{
				library = new { one = "1", color="red" }
			});
			var secondData = DynamicJson.Serialize(new
			{
				library = new { two = "2", color = "blue" }
			});

			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			dynamic j= (DynamicJson) DynamicJson.Parse(second.GetLibraryData());
			Assert.AreEqual("2", j.library.two);
			Assert.AreEqual("1", j.library.one);
			Assert.AreEqual("blue", j.library.color);
		}

		// Also covers case of string value in list containing colon
		[Test]
		public void CollectJsonData_HasArrayValue_DataMerged()
		{
			var firstData = "{\"library\":{\"days\":[\"1\",\"2\"]}}";
			var secondData = "{\"library\":{\"days\":[\"o:e\",\"two\"]}}";

			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetLibraryData());
			Assert.AreEqual("o:e", j.library.days[0]);
			Assert.AreEqual("two", j.library.days[1]);
		}


		// Also tests edge case of value containing a colon, starting with a { (so it sort of looks like an object),
		// and containing quotes and backslashes.
		[Test]
		public void CollectJsonData_NewArrayItems_DataMerged()
		{
			var firstData = DynamicJson.Serialize(new
													{
														library = new {food = new {veg="v", fruit = "f", nuts="n"}}
													});
			var secondData = DynamicJson.Serialize(new
			{
				library = new { food = new { bread = "b", fruit = "{f\\:", nuts = "\"nut\"" } }
			});

			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetLibraryData());
			Assert.AreEqual("v", j.library.food.veg);
			Assert.AreEqual("{f\\:", j.library.food.fruit);
			Assert.AreEqual("b", j.library.food.bread);
			Assert.AreEqual("\"nut\"", j.library.food.nuts);
		}

		private void AssertEqual(string a, string b)
		{
			Assert.AreEqual(DynamicJson.Parse(a), DynamicJson.Parse(b));
		}

		[Test]
		public void WhenCollectedNoLocalDataThenLocalDataIsEmpty()
		{
			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			var stringRep = DynamicJson.Serialize(new
				{
					library = new {librarystuff = "foo"}
				});

			first.CollectJsonData(stringRep.ToString());
			AssertEmpty(first.LocalData);
		}

		private static void AssertEmpty(string json)
		{
			Assert.IsTrue(DynamicJson.Parse(json).IsEmpty);
		}

		[Test]
		public void WhenCollectedNoGlobalDataThenGlobalDataIsEmpty()
		{
			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(first.LocalData));
		}

		[Test]
		public void GetLibraryData_NoGlobalData_Empty()
		{
			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual("{}", first.GetLibraryData());
		}
		[Test]
		public void GetLibraryData_NothingCollected_Empty()
		{
			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			Assert.AreEqual("{}", first.GetLibraryData());
		}
		[Test]
		public void LocalData_NothingCollected_Empty()
		{
			var first = new Configurator(_libraryFolder.Path, NavigationIsolator.GetOrCreateTheOneNavigationIsolator());
			Assert.AreEqual("", first.LocalData);
		}


		private BookStorage Get_NotYetConfigured_CalendardBookStorage()
		{
			var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Wall Calendar");
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _libraryFolder.Path));
			var projectFolder = new TemporaryFolder("ConfiguratorTests_ProjectCollection");
			//review
			var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));

			var bs = new BookStorage(Path.GetDirectoryName(path), _fileLocator, new BookRenamedEvent(), collectionSettings);
			return bs;
		}


		private string GetPathToHtml(string bookFolderPath)
		{
			return Path.Combine(bookFolderPath, Path.GetFileName(bookFolderPath)) + ".htm";
		}
	}
}
