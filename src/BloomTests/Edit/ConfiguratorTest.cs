using System;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Api;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;

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
		private TemporaryFolder _collectionFolder;

		[SetUp]
		public void Setup()
		{
			var collection = new CollectionSettings
			{
				IsSourceCollection = false,
				Language2Tag = "en",
				Language1Tag = "xyz",
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
												BloomFileLocator.GetFactoryXMatterDirectory()
											});

			var projectFolder = new TemporaryFolder("BookStarterTests_ProjectCollection");
			var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));

			_starter = new BookStarter(_fileLocator, (dir, fullyUpdateBookFiles) => new BookStorage(dir, _fileLocator, new BookRenamedEvent(), collectionSettings), collection);
			_shellCollectionFolder = new TemporaryFolder("BookStarterTests_ShellCollection");
			_collectionFolder = new TemporaryFolder("BookStarterTests_Collection");
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
			var c = new Configurator(_collectionFolder.Path);

			var stringRep = DynamicJson.Serialize(new
			{
				library = new { calendar = new { year = "2088" } }
			});
			c.CollectJsonData(stringRep);

			c.ShowConfigurationDialog(Get_NotYetConfigured_CalendardBookStorage().FolderPath);
			Assert.IsTrue(c.GetCollectionData().Contains("year"));
		}


		[Test]
		public void GetAllData_LocalOnly_ReturnLocal()
		{
			var c = new Configurator(_collectionFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			c.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(c.GetAllData()));
		}

		[Test]
		public void CollectionSettingsAreRoundTriped()
		{
			var first = new Configurator(_collectionFolder.Path);
			var stringRep = DynamicJson.Serialize(new
						{
							library = new {stuff = "foo"}
						});

			first.CollectJsonData(stringRep.ToString());

			var second = new Configurator(_collectionFolder.Path);
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetCollectionData());
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

			var first = new Configurator(_collectionFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_collectionFolder.Path);
			dynamic j= (DynamicJson) DynamicJson.Parse(second.GetCollectionData());
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

			var first = new Configurator(_collectionFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_collectionFolder.Path);
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetCollectionData());
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

			var first = new Configurator(_collectionFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_collectionFolder.Path);
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetCollectionData());
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
		public void CollectJsonData_Quotes_DataMerged()
		{
			var firstData = DynamicJson.Serialize(new
			{
				library = new
				{
					food = new
					{
						vegetables = new string[] { @"beans", @"""fruit""", @"nu""ts" },
						meats = new string[] { @"'fish'", @"be'ef", @"chicken" }
					}
				}
			});
			var secondData = DynamicJson.Serialize(new
			{
				library = new
				{
					food = new
					{
						vegetables = new string[] { @"green beans", @"""fruit""", @"nu""ts" },
						meats = new string[] { @"'fresh fish'", @"be'ef", @"chicken", @"turkey" }
					}
				}
			});

			var first = new Configurator(_collectionFolder.Path);
			first.CollectJsonData(firstData.ToString());
			first.CollectJsonData(secondData.ToString());

			var second = new Configurator(_collectionFolder.Path);
			dynamic j = (DynamicJson)DynamicJson.Parse(second.GetCollectionData());
			Assert.AreEqual("green beans", j.library.food.vegetables[0]);
			Assert.AreEqual("\"fruit\"", j.library.food.vegetables[1]);
			Assert.AreEqual("nu\"ts", j.library.food.vegetables[2]);
			Assert.AreEqual("'fresh fish'", j.library.food.meats[0]);
			Assert.AreEqual("be'ef", j.library.food.meats[1]);
			Assert.AreEqual("chicken", j.library.food.meats[2]);
			Assert.AreEqual("turkey", j.library.food.meats[3]);
		}

		[Test]
		public void WhenCollectedNoLocalDataThenLocalDataIsEmpty()
		{
			var first = new Configurator(_collectionFolder.Path);
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
			var first = new Configurator(_collectionFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual(j, DynamicJson.Parse(first.LocalData));
		}

		[Test]
		public void GetCollectionData_NoGlobalData_Empty()
		{
			var first = new Configurator(_collectionFolder.Path);
			dynamic j = new DynamicJson();
			j.one = 1;
			first.CollectJsonData(j.ToString());
			Assert.AreEqual("{}", first.GetCollectionData());
		}
		[Test]
		public void GetCollectionData_NothingCollected_Empty()
		{
			var first = new Configurator(_collectionFolder.Path);
			Assert.AreEqual("{}", first.GetCollectionData());
		}
		[Test]
		public void LocalData_NothingCollected_Empty()
		{
			var first = new Configurator(_collectionFolder.Path);
			Assert.AreEqual("", first.LocalData);
		}


		private BookStorage Get_NotYetConfigured_CalendardBookStorage()
		{
			var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Wall Calendar");
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _collectionFolder.Path));
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

		[Test]
		public void SetupConfigurationHtml_AddsScripts()
		{
			var inputHtml = @"<!DOCTYPE html>
<!-- configuration form for Bloom Wall Calendar template-->
<html>
  <head>
    <meta charset=""UTF-8"">
    <title>Set Up Calendar</title>
    <link rel=""stylesheet"" href=""configuration.css"" type=""text/css"">
    <script type=""text/javascript"" src=""jquery-1.10.1.js""></script>
    <script type=""text/javascript"" src=""configure.js""></script>
  </head>
  <body onload=""getYear()"">
    <form id=""form"">
	</form>
  </body>
</html>";
			var settings =
				@"{""calendar"":{""dayAbbreviations"":[""Sun"",""Mon"",""Tues"",""Wed"",""Thur"",""Fri"",""Sat""],""monthNames"":[""JanuaryD"",""FebruaryD"",""MarchD"",""April"",""May"",""June"",""July"",""August"",""September"",""October"",""November"",""December""]}}";

			var output = Configurator.SetupConfigurationHtml(inputHtml, settings);
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(output, false);
			var AssertXml = AssertThatXmlIn.Element(dom.DocumentElement);
			AssertXml.HasSpecifiedNumberOfMatchesForXpath("//script[@type='text/javascript' and @src='jquery-1.10.1.js']",1);
			AssertXml.HasSpecifiedNumberOfMatchesForXpath("//script[@type='text/javascript' and @src='form2object.js']", 1);
			AssertXml.HasSpecifiedNumberOfMatchesForXpath("//script[@type='text/javascript' and @src='js2form.js']", 1);
			AssertXml.HasSpecifiedNumberOfMatchesForXpath("//script[@type='text/javascript' and @src='underscore.js']", 1);
			// It's rather arbitrary what we check here, but there's no point in copying the whole text
			// of the code we insert. It is expected that the settings data gets inserted into it somewhere.
			AssertXml.HasSpecifiedNumberOfMatchesForXpath("//script[@type='text/javascript' and @id='configuredScript' and contains(text(), '\"dayAbbreviations\"')]", 1);
		}

	}
}
