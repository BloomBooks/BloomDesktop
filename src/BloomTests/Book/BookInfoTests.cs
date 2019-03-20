using System.Collections.Generic;
using System.IO;
using Bloom.Book;
using Bloom.Edit;
using NUnit.Framework;
using SIL.TestUtilities;
using SIL.Reflection;

namespace BloomTests.Book
{
	[TestFixture]
	public class BookInfoTests
	{
		private TemporaryFolder _fixtureFolder;
		private TemporaryFolder _folder;
		[SetUp]
		public void Setup()
		{
			_fixtureFolder = new TemporaryFolder("BloomBookStorageTest");
			_folder = new TemporaryFolder(_fixtureFolder, "theBook");
		}

		[TearDown]
		public void TearDown()
		{
			_fixtureFolder.Dispose();
		}

		[Test]
		public void Constructor_LoadsMetaDataFromJson()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{'folio':'true','experimental':'true','suitableForMakingShells':'true'}");
			var bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio);
			Assert.That(bi.IsSuitableForMakingShells);
		}

		[Test]
		public void InstallFreshGuid_works()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			const string originalGuid = "3988218f-e01c-4a7a-b27d-4a31dd632ccb";
			const string metaData = @"{'bookInstanceId':'" + originalGuid + @"','experimental':'false','suitableForMakingShells':'true'}";
			File.WriteAllText(jsonPath, metaData);
			var bookOrderPath = BookInfo.BookOrderPath(_folder.Path);
			File.WriteAllText(bookOrderPath, metaData);
			Assert.That(File.Exists(bookOrderPath), Is.True);

			// SUT
			BookInfo.InstallFreshInstanceGuid(_folder.Path);

			// Verification
			var bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.Id, Is.Not.EqualTo(originalGuid));
			Assert.That(bi.IsExperimental, Is.False);
			Assert.That(bi.IsSuitableForMakingShells);
			Assert.That(File.Exists(bookOrderPath), Is.False);
			Assert.That(File.Exists(Path.Combine(_folder.Path, "meta.bak")), Is.False);
		}

		[Test]
		public void Constructor_FallsBackToTags()
		{
			var tagsPath = Path.Combine(_folder.Path, "tags.txt");
			File.WriteAllText(tagsPath, @"folio\nexperimental\nsuitableForMakingShells\n");
			var bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio);
			// BL-2163, we are no longer migrating suitableForMakingShells
			Assert.That(bi.IsSuitableForMakingShells, Is.False);

			// Check that json takes precedence
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{'folio':'false','experimental':'true','suitableForMakingShells':'false'}");
			bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio, Is.False);
			Assert.That(bi.IsSuitableForMakingShells, Is.False);
		}

		[Test]
		public void TitleSetter_FixesTitleWithXml()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath,
				"{'title':'<span class=\"sentence-too-long\" data-segment=\"sentence\">Book on &lt;span&gt;s\r\n</span>'}");
			var bi = new BookInfo(_folder.Path, true); // loads metadata, but doesn't use Title setter
			// SUT
			bi.Title = bi.Title; // exercises setter
			Assert.AreEqual("Book on <span>s\r\n", bi.Title);
		}

		[Test]
		public void RoundTrips_AllowUploading()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{'allowUploadingToBloomLibrary':'false'}");
			var bi = new BookInfo(_folder.Path, true);
			Assert.False(bi.AllowUploading, "CHECK YOUR FixBloomMetaInfo ENV variable! Initial Read Failed to get false. Contents: " + File.ReadAllText(jsonPath));
			bi.Save();
			var bi2 = new BookInfo(_folder.Path, true);
			Assert.False(bi2.AllowUploading, "Read after Save() Failed  to get false. Contents: " + File.ReadAllText(jsonPath));

			File.WriteAllText(jsonPath, @"{'allowUploadingToBloomLibrary':'true'}");
			var bi3 = new BookInfo(_folder.Path, true);
			Assert.That(bi3.AllowUploading,  "Initial Read Failed to get true. Contents: " + File.ReadAllText(jsonPath));
			bi3.Save();
			var bi4 = new BookInfo(_folder.Path, true);
			Assert.That(File.ReadAllText(jsonPath).Contains("allowUploadingToBloomLibrary"), "The file doesn't contain 'allowUploadingToBloomLibrary'");
			Assert.That(bi4.AllowUploading, "Read after Save() Failed  to get true. Contents: " + File.ReadAllText(jsonPath));
		}

		[Test]
		public void WebDataJson_IncludesCorrectFields()
		{
			var meta = new BookMetaData()
			{
				Id = "myId",
				IsSuitableForMakingShells = true,
				IsSuitableForVernacularLibrary = false,
				IsExperimental = true,
				Title = "myTitle",
				AllTitles = "abc,\"def",
				BaseUrl = "http://some/unlikely/url",
				Isbn = "123-456-78-9",
				DownloadSource = "http://some/amazon/url",
				License = "ccby",
				FormatVersion = "1.0",
				Credits = "JohnT",
				Summary = "A very nice book\\ in a very nice nook",
				Tags= new []{"Animals"},
				CurrentTool = "mytool",
				BookletMakingIsAppropriate = false, PageCount=7,
				LanguageTableReferences = new [] {new ParseDotComObjectPointer() { ClassName = "Language", ObjectId = "23456" }},
				Uploader = new ParseDotComObjectPointer() { ClassName="User", ObjectId = "12345"},
				Tools = new List<ToolboxToolState>(new [] {ToolboxToolState.CreateFromToolId("decodableReader")}),
				AllowUploadingToBloomLibrary = false,
				CountryName = "InTheBush",
				ProvinceName = "Provence",
				DistrictName = "Ocean"
			};
			var result = meta.WebDataJson;
			var meta2 = BookMetaData.FromString(result);
			Assert.That(meta2.Id, Is.EqualTo("myId"));
			Assert.That(meta2.IsSuitableForMakingShells, Is.True);
			Assert.That(meta2.IsSuitableForVernacularLibrary, Is.False);
			Assert.That(meta2.IsExperimental, Is.True);
			Assert.That(meta2.Title, Is.EqualTo("myTitle"));
			Assert.That(meta2.AllTitles, Is.EqualTo("abc,\"def"));
			Assert.That(meta2.BaseUrl, Is.EqualTo("http://some/unlikely/url"));
			Assert.That(meta2.Isbn, Is.EqualTo("123-456-78-9"));

			Assert.That(meta2.License, Is.EqualTo("ccby"));
			Assert.That(meta2.FormatVersion, Is.EqualTo("1.0"));
			Assert.That(meta2.Credits, Is.EqualTo("JohnT"));
			Assert.That(meta2.Tags, Has.Length.EqualTo(1));
			Assert.That(meta2.Summary, Is.EqualTo("A very nice book\\ in a very nice nook"));
			Assert.That(meta2.PageCount, Is.EqualTo(7));
			Assert.That(meta2.LanguageTableReferences, Has.Length.EqualTo(1));
			Assert.That(meta2.LanguageTableReferences[0].ObjectId, Is.EqualTo("23456"));
			Assert.That(meta2.Uploader, Is.Not.Null);
			Assert.That(meta2.Uploader.ObjectId, Is.EqualTo("12345"));

			// These properties (and various others) should not be in the serialization data.
			// Since AllowUploadingToBloomLibrary defaults true, that should be its value if not set by json
			Assert.That(meta2.AllowUploadingToBloomLibrary, Is.True, "AllowUploadingtoBloomLibrary was unexpectedly serialized");
			Assert.That(meta2.DownloadSource, Is.Null);
			Assert.That(meta2.CurrentTool, Is.Null);
			Assert.That(meta2.Tools, Is.Null);
			Assert.That(meta2.CountryName, Is.EqualTo("InTheBush"));
			Assert.That(meta2.ProvinceName, Is.EqualTo("Provence"));
			Assert.That(meta2.DistrictName, Is.EqualTo("Ocean"));
			Assert.That(meta2.BookletMakingIsAppropriate, Is.True); // default value
		}

		[TestCase("'Fiction'", "Fiction")]
		[TestCase("'Math', 'Fiction'", "Math, Fiction")]
		[TestCase("'topic:Fiction'", "Fiction")]
		[TestCase("'Fiction','media:audio'", "Fiction")]
		[TestCase("'media:audio','Fiction'", "Fiction")]
		[TestCase("'topic:Math','Fiction'", "Math, Fiction")]
		public void TopicsList_GetsTopicsAndOnlyTopicsFromTagsList(string jsonTagsList, string expectedTopicsList)
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{'tags':[" + jsonTagsList + @"]}");
			var bi = new BookInfo(_folder.Path, true);
			Assert.AreEqual(expectedTopicsList, bi.TopicsList);
		}

		[TestCase("'topic:Fiction'", "", "", new string[0])]
		[TestCase("'media:audio'", "", "", new[] { "media:audio" })]
		[TestCase("'media:audio', 'topic:Fiction'", "", "", new[] { "media:audio" })]
		[TestCase("'Fiction'", "Math", "Math", new []{ "topic:Math" })]
		[TestCase("'Fiction','Math'", "Math", "Math", new[] { "topic:Math" })]
		[TestCase("'Fiction','Math','media:audio'", "Math", "Math", new[] { "media:audio", "topic:Math" })]
		[TestCase("'media:audio'", "topic:Math", "Math", new[] { "media:audio", "topic:Math" })]
		[TestCase("'media:audio','region:Asia'", "topic:Math,topic:Fiction", "Math, Fiction", new[] { "media:audio", "region:Asia", "topic:Math", "topic:Fiction" })]
		[TestCase("'topic:Science'", "topic:Math,topic:Fiction", "Math, Fiction", new[] { "topic:Math", "topic:Fiction" })]
		public void TopicsList_SetsTopicsWhileLeavingOtherTagsIntact(string jsonTagsList, string topicsListToSet, string expectedTopicsList, string[] expectedTags)
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{'tags':[" + jsonTagsList + @"]}");
			var bi = new BookInfo(_folder.Path, true);

			//SUT
			bi.TopicsList = topicsListToSet;

			Assert.AreEqual(expectedTopicsList, bi.TopicsList);

			BookMetaData metadata = (BookMetaData) ReflectionHelper.GetField(bi, "_metadata");
			Assert.AreEqual(expectedTags, metadata.Tags);
		}
	}
}
