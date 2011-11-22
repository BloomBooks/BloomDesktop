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
		[STAThread]
		public void ConfigureBook_xxxxxx()
		{
			var c = new Configurator(_projectFolder.Path);
			c.ConfigureBook(GetCalendardBookStorage().PathToExistingHtml);
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

		[Test, Ignore("need to compensate for removing 'project'")]
		public void ProjectSettingsAreRoundTriped()
		{
			var first = new Configurator(_projectFolder.Path);
			var stringRep = DynamicJson.Serialize(new
						{
							project = new {stuff = "foo"}
						});
			var internalsOnly = DynamicJson.Serialize(new {stuff = "foo"});

			first.CollectJsonData(stringRep.ToString());

			var second = new Configurator(_projectFolder.Path);
			AssertEqual(internalsOnly, second.GetProjectData());
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
