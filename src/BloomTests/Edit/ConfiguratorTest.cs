using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Bloom;
using Bloom.Book;
using Bloom.Edit;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;

namespace BloomTests.Edit
{
	[TestFixture]
	public class ConfiguratorTest
	{
		private FileLocator _fileLocator;
		private BookStarter _starter;
		private TemporaryFolder _shellCollectionFolder;
		private TemporaryFolder _libraryFolder;

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool SetDllDirectory(string lpPathName);


		[SetUp]
		public void Setup()
		{
			var library = new Moq.Mock<LibrarySettings>();
			library.SetupGet(x => x.IsShellLibrary).Returns(false);

			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "A5Portrait"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Factory-XMatter")
											});
			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), library.Object);
			_shellCollectionFolder = new TemporaryFolder("BookStarterTests_ShellCollection");
			_libraryFolder = new TemporaryFolder("BookStarterTests_LibraryCollection");

			Browser.SetUpXulRunner();



		}

		[Test]
		public void IsConfigurable_Calendar_True()
		{
			Assert.IsTrue(Configurator.IsConfigurable(GetCalendardBookStorage().FolderPath));
		}

		[Test, Ignore("UI-By hand")]
		[STAThread]
		public void ShowConfigureDialog()
		{
			var c = new Configurator(_libraryFolder.Path);

			var stringRep = DynamicJson.Serialize(new
			{
				library = new { calendar = new { year = "2088" } }
			});
			c.CollectJsonData(stringRep);

			c.ShowConfigurationDialog(GetCalendardBookStorage().FolderPath);
			Assert.IsTrue(c.GetLibraryData().Contains("year"));
		}


		[Test]
		public void GetAllData_LocalOnly_ReturnLocal()
		{
			var c = new Configurator(_libraryFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			c.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(c.GetAllData()));
		}

		[Test]
		public void LibrarySettingsAreRoundTriped()
		{
			var first = new Configurator(_libraryFolder.Path);
			var stringRep = DynamicJson.Serialize(new
						{
							library = new {stuff = "foo"}
						});

			first.CollectJsonData(stringRep.ToString());

			var second = new Configurator(_libraryFolder.Path);
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

			var first = new Configurator(_libraryFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path);
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

			var first = new Configurator(_libraryFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path);
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

			var first = new Configurator(_libraryFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_libraryFolder.Path);
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
			var first = new Configurator(_libraryFolder.Path);
			dynamic j = new DynamicJson();
			j.library = new DynamicJson();
			j.library.librarystuff = "foo";
			first.CollectJsonData(j.ToString());
			AssertEmpty(first.LocalData);
		}

		private static void AssertEmpty(string json)
		{
			Assert.IsTrue(DynamicJson.Parse(json).IsEmpty);
		}

		[Test]
		public void WhenCollectedNoGlobalDataThenGlobalDataIsEmpty()
		{
			var first = new Configurator(_libraryFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(first.LocalData));
		}

		[Test]
		public void GetLibraryData_NoGlobalData_Empty()
		{
			var first = new Configurator(_libraryFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual("{}", first.GetLibraryData());
		}
		[Test]
		public void GetLibraryData_NothingCollected_Empty()
		{
			var first = new Configurator(_libraryFolder.Path);
			Assert.AreEqual("{}", first.GetLibraryData());
		}
		[Test]
		public void LocalData_NothingCollected_Empty()
		{
			var first = new Configurator(_libraryFolder.Path);
			Assert.AreEqual("", first.LocalData);
		}
		private BookStorage GetCalendardBookStorage()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells", "A5 Wall Calendar");
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _libraryFolder.Path));
			var bs = new BookStorage(Path.GetDirectoryName(path), _fileLocator);
			return bs;
		}


		private string GetPathToHtml(string bookFolderPath)
		{
			return Path.Combine(bookFolderPath, Path.GetFileName(bookFolderPath)) + ".htm";
		}
	}
}
