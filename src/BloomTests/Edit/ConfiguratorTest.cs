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
	[RequiresSTA]
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
			var library = new Moq.Mock<CollectionSettings>();
			library.SetupGet(x => x.IsSourceCollection).Returns(false);
			library.SetupGet(x => x.Language2Iso639Code).Returns("en");
			library.SetupGet(x => x.Language1Iso639Code).Returns("xyz");
			library.SetupGet(x => x.XMatterPackName).Returns("Factory");

			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												//FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book"),
												BloomFileLocator.GetFactoryBookTemplateDirectory("Wall Calendar"),
												FileLocator.GetDirectoryDistributedWithApplication( BloomFileLocator.BrowserRoot),
												BloomFileLocator.GetBrowserDirectory("bookLayout"),
												BloomFileLocator.GetBrowserDirectory("bookEdit","css"),
												BloomFileLocator.GetInstalledXMatterDirectory()
											});

			var projectFolder = new TemporaryFolder("BookStarterTests_ProjectCollection");
			var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));

			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator, new BookRenamedEvent(), collectionSettings), library.Object);
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
			var c = new Configurator(_libraryFolder.Path, new NavigationIsolator());

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
			var c = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			dynamic j = new DynamicJson();
			j.one = 1;
			c.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(c.GetAllData()));
		}

		[Test]
		public void LibrarySettingsAreRoundTriped()
		{
			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			var stringRep = DynamicJson.Serialize(new
						{
							library = new {stuff = "foo"}
						});

			first.CollectJsonData(stringRep.ToString());

			var second = new Configurator(_libraryFolder.Path, new NavigationIsolator());
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

			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			dynamic j= (DynamicJson) DynamicJson.Parse(second.GetLibraryData());
			Assert.AreEqual("2", j.library.two);
			Assert.AreEqual("1", j.library.one);
			Assert.AreEqual("blue", j.library.color);
		}

		[Test]
		public void CollectJsonData_HasArrayValue_DataMerged()
		{
			var firstData = "{\"library\":{\"days\":[\"1\",\"2\"]}}";
			var secondData = "{\"library\":{\"days\":[\"one\",\"two\"]}}";

			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetLibraryData());
			Assert.AreEqual("one", j.library.days[0]);
			Assert.AreEqual("two", j.library.days[1]);
		}


		[Test]
		public void CollectJsonData_NewArrayItems_DataMerged()
		{
			var firstData = DynamicJson.Serialize(new
													{
														library = new {food = new {veg="v", fruit = "f"}}
													});
			var secondData = DynamicJson.Serialize(new
			{
				library = new { food = new { bread = "b", fruit = "f" } }
			});

			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetLibraryData());
			Assert.AreEqual("v", j.library.food.veg);
			Assert.AreEqual("f", j.library.food.fruit);
			Assert.AreEqual("b", j.library.food.bread);
		}

		private void AssertEqual(string a, string b)
		{
			Assert.AreEqual(DynamicJson.Parse(a), DynamicJson.Parse(b));
		}

		[Test]
		public void WhenCollectedNoLocalDataThenLocalDataIsEmpty()
		{
			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
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
			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(first.LocalData));
		}

		[Test]
		public void GetLibraryData_NoGlobalData_Empty()
		{
			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual("{}", first.GetLibraryData());
		}
		[Test]
		public void GetLibraryData_NothingCollected_Empty()
		{
			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
			Assert.AreEqual("{}", first.GetLibraryData());
		}
		[Test]
		public void LocalData_NothingCollected_Empty()
		{
			var first = new Configurator(_libraryFolder.Path, new NavigationIsolator());
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
