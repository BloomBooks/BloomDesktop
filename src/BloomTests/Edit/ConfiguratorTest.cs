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
		private TemporaryFolder _projectFolder;

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool SetDllDirectory(string lpPathName);


		[SetUp]
		public void Setup()
		{
			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "A5Portrait")
											});
			_starter = new BookStarter(dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]));
			_shellCollectionFolder = new TemporaryFolder("BookStarterTests_ShellCollection");
			_projectFolder = new TemporaryFolder("BookStarterTests_ProjectCollection");

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
			var c = new Configurator(_projectFolder.Path);

			var stringRep = DynamicJson.Serialize(new
			{
				project = new { calendar = new { year = "2088" } }
			});
			c.CollectJsonData(stringRep);

			c.ShowConfigurationDialog(GetCalendardBookStorage().FolderPath);
			Assert.IsTrue(c.GetProjectData().Contains("year"));
		}


		[Test]
		public void GetAllData_LocalOnly_ReturnLocal()
		{
			var c = new Configurator(_projectFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			c.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(c.GetAllData()));
		}

		[Test]
		public void ProjectSettingsAreRoundTriped()
		{
			var first = new Configurator(_projectFolder.Path);
			var stringRep = DynamicJson.Serialize(new
						{
							project = new {stuff = "foo"}
						});

			first.CollectJsonData(stringRep.ToString());

			var second = new Configurator(_projectFolder.Path);
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetProjectData());
			Assert.AreEqual("foo", j.project.stuff);
		}



		[Test]
		public void CollectJsonData_NewTopLevelData_DataMerged()
		{
			var firstData = DynamicJson.Serialize(new
			{
				project = new { one = "1", color="red" }
			});
			var secondData = DynamicJson.Serialize(new
			{
				project = new { two = "2", color = "blue" }
			});

			var first = new Configurator(_projectFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_projectFolder.Path);
			dynamic j= (DynamicJson) DynamicJson.Parse(second.GetProjectData());
			Assert.AreEqual("2", j.project.two);
			Assert.AreEqual("1", j.project.one);
			Assert.AreEqual("blue", j.project.color);
		}

		[Test]
		public void CollectJsonData_HasArrayValue_DataMerged()
		{
			var firstData = "{\"project\":{\"days\":[\"1\",\"2\"]}}";
			var secondData = "{\"project\":{\"days\":[\"one\",\"two\"]}}";

			var first = new Configurator(_projectFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_projectFolder.Path);
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetProjectData());
			Assert.AreEqual("one", j.project.days[0]);
			Assert.AreEqual("two", j.project.days[1]);
		}


		[Test]
		public void CollectJsonData_NewArrayItems_DataMerged()
		{
			var firstData = DynamicJson.Serialize(new
													{
														project = new {food = new {veg="v", fruit = "f"}}
													});
			var secondData = DynamicJson.Serialize(new
			{
				project = new { food = new { bread = "b", fruit = "f" } }
			});

			var first = new Configurator(_projectFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_projectFolder.Path);
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetProjectData());
			Assert.AreEqual("v", j.project.food.veg);
			Assert.AreEqual("f", j.project.food.fruit);
			Assert.AreEqual("b", j.project.food.bread);
		}

		private void AssertEqual(string a, string b)
		{
			Assert.AreEqual(DynamicJson.Parse(a), DynamicJson.Parse(b));
		}

		[Test]
		public void WhenCollectedNoLocalDataThenLocalDataIsEmpty()
		{
			var first = new Configurator(_projectFolder.Path);
			dynamic j = new DynamicJson();
			j.project = new DynamicJson();
			j.project.projectstuff = "foo";
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
			var first = new Configurator(_projectFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(first.LocalData));
		}

		[Test]
		public void GetProjectData_NoGlobalData_Empty()
		{
			var first = new Configurator(_projectFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual("", first.GetProjectData());
		}
		[Test]
		public void GetProjectData_NothingCollected_Empty()
		{
			var first = new Configurator(_projectFolder.Path);
			Assert.AreEqual("", first.GetProjectData());
		}
		[Test]
		public void LocalData_NothingCollected_Empty()
		{
			var first = new Configurator(_projectFolder.Path);
			Assert.AreEqual("", first.LocalData);
		}
		private BookStorage GetCalendardBookStorage()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells", "A5 Wall Calendar");
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
			var bs = new BookStorage(Path.GetDirectoryName(path), _fileLocator);
			return bs;
		}


		private string GetPathToHtml(string bookFolderPath)
		{
			return Path.Combine(bookFolderPath, Path.GetFileName(bookFolderPath)) + ".htm";
		}
	}
}
