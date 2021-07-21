using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Bloom.Book;
using Bloom.Edit;
using Newtonsoft.Json.Linq;
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
				LanguageTableReferences = new [] {new ParseServerObjectPointer { ClassName = "Language", ObjectId = "23456" }},
				Uploader = new ParseServerObjectPointer { ClassName="User", ObjectId = "12345"},
				Tools = new List<ToolboxToolState>(new [] {ToolboxToolState.CreateFromToolId("decodableReader")}),
				AllowUploadingToBloomLibrary = false,
				CountryName = "InTheBush",
				ProvinceName = "Provence",
				DistrictName = "Ocean"
			};
			var result = meta.WebDataJson;

			AssertNonBookMetaDataFieldsAreValid(result);

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

		private void AssertNonBookMetaDataFieldsAreValid(string webDataJsonString)
		{
			// These two fields (updateSource & lastUploaded) are only sent to parse server.
			// They are not part of BookMetaData.
			
			dynamic jsonResult = JObject.Parse(webDataJsonString);

			// updateSource
			Assert.True(jsonResult.updateSource.Value.StartsWith("BloomDesktop "), $"{webDataJsonString}\n\nis expected to contain a proper updateSource");

			// lastUploaded.__type
			Assert.That(jsonResult.lastUploaded.__type.Value, Is.EqualTo("Date"));

			// lastUploaded.iso
			DateTime lastUploadedDateTime = jsonResult.lastUploaded.iso.Value;
			var differenceBetweenNowAndCreationOfJson = DateTime.UtcNow - lastUploadedDateTime;
			Assert.That(differenceBetweenNowAndCreationOfJson, Is.GreaterThan(TimeSpan.FromSeconds(0)), "lastUploaded should be a valid date representing now-ish");
			Assert.That(differenceBetweenNowAndCreationOfJson, Is.LessThan(TimeSpan.FromSeconds(5)), "lastUploaded should be a valid date representing now-ish");
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

		[TestCase(null, new string[0], TestName="FeaturesGetter_BlindLangCodesNull_NoException")]
		[TestCase(new string[0], new string[0], TestName = "FeaturesGetter_BlindLangCodesEmpty_Empty")]
		[TestCase(new string[] { "en", "es" }, new string[] { "blind", "blind:en", "blind:es" }, TestName = "FeaturesGetter_BlindLangCodesMultiple_OverallAndLangSpecificFeatures")]
		public void FeaturesGetter_Blind(IEnumerable<string> langCodes, string[] featuresExpected)
		{
			var metadata = new BookMetaData();
			metadata.Feature_Blind_LangCodes = langCodes;

			// System under test
			string[] featuresResult = metadata.Features;
			bool featureBlindResult = metadata.Feature_Blind;

			Assert.AreEqual(featuresExpected, featuresResult, "Features");
			Assert.AreEqual(featuresExpected.Any(), featureBlindResult, "Feature_Blind");
		}

		[TestCase(null, new string[0], TestName = "FeaturesGetter_TalkingBookLangCodesNull_NoException")]
		[TestCase(new string[0], new string[0], TestName = "FeaturesGetter_TalkingBookLangCodesEmpty_Empty")]
		[TestCase(new string[] { "en", "es" }, new string[] { "talkingBook", "talkingBook:en", "talkingBook:es" }, TestName = "FeaturesGetter_TalkingBookLangCodesMultiple_OverallAndLangSpecificFeatures")]
		public void FeaturesGetter_TalkingBook(IEnumerable<string> langCodes, string[] featuresExpected)
		{
			var metadata = new BookMetaData();
			metadata.Feature_TalkingBook_LangCodes = langCodes;

			// System under test
			string[] result = metadata.Features;
			bool featureTalkingBookResult = metadata.Feature_TalkingBook;

			Assert.AreEqual(featuresExpected, result, "Features");
			Assert.AreEqual(featuresExpected.Any(), featureTalkingBookResult, "Feature_TalkingBook");
		}

		[TestCase(null, new string[0], TestName = "FeaturesGetter_SignLanguageLangCodesNull_NoException")]
		[TestCase(new string[0], new string[0], TestName = "FeaturesGetter_SignLanguageLangCodesEmptyArray_Empty")]
		[TestCase(new string[] { "" }, new string[] { "signLanguage" }, TestName = "FeaturesGetter_SignLanguageLangCodesEmptyString_OverallOnly")]
		[TestCase(new string[] { "ase" }, new string[] { "signLanguage", "signLanguage:ase" }, TestName = "FeaturesGetter_SignLanguageLangCodeSet_OverallAndLangSpecificFeatures")]
		public void FeaturesGetter_SignLanguage(IEnumerable<string> langCodes, string[] featuresExpected)
		{
			var metadata = new BookMetaData();
			metadata.Feature_SignLanguage_LangCodes = langCodes;

			// System under test
			string[] result = metadata.Features;
			bool featureSignLanguageResult = metadata.Feature_SignLanguage;

			Assert.AreEqual(featuresExpected, result, "Features");
			Assert.AreEqual(featuresExpected.Any(), featureSignLanguageResult, "Feature_SignLanguage");
		}

		[TestCase(false)]
		[TestCase(true)]
		public void FeaturesGetter_Quiz(bool containsQuiz)
		{
			var metadata = new BookMetaData();
			metadata.Feature_Quiz = containsQuiz;

			// System under test
			string[] result = metadata.Features;

			bool expectedResult = containsQuiz;
			Assert.AreEqual(expectedResult, result.Contains("quiz"));
		}

		[TestCase(false)]
		[TestCase(true)]
		public void FeaturesGetter_Widget(bool containsWidget)
		{
			var metadata = new BookMetaData();
			metadata.Feature_Widget = containsWidget;

			// System under test
			string[] result = metadata.Features;

			bool expectedResult = containsWidget;
			Assert.AreEqual(expectedResult, result.Contains("widget"));
		}

		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public void FeaturesGetter_IfQuizOrWidgetSet_ThenActivityIsTrue(bool containsQuiz, bool containsWidget)
		{
			var metadata = new BookMetaData();
			metadata.Feature_Quiz = containsQuiz;
			metadata.Feature_Widget = containsWidget;

			// System under test
			string[] result = metadata.Features;

			Assert.IsTrue(result.Contains("activity"));
		}

		public void FeaturesGetter_NeitherQuizNorWidgetSet_ThenActivityIsFalse()
		{
			var metadata = new BookMetaData();
			metadata.Feature_Quiz = metadata.Feature_Widget = false;

			// System under test
			string[] result = metadata.Features;

			Assert.IsFalse(result.Contains("activity"));
		}

		[TestCase(false)]
		[TestCase(true)]
		public void FeaturesGetter_Motion(bool containsMotion)
		{
			var metadata = new BookMetaData();
			metadata.Feature_Motion = containsMotion;

			// System under test
			string[] result = metadata.Features;

			string[] expectedResult = containsMotion ? new string[] { "motion" } : new string[0];
			Assert.AreEqual(expectedResult, result);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void FeaturesGetter_Comic(bool containsComic)
		{
			var metadata = new BookMetaData();
			metadata.Feature_Comic = containsComic;

			// System under test
			string[] result = metadata.Features;

			string[] expectedResult = containsComic ? new string[] { "comic" } : new string[0];
			Assert.AreEqual(expectedResult, result);
		}

		[Test]
		public void FeaturesSetter_OverallFeaturesOnly_ConvertBackGetsSameResult()
		{
			var input = new string[] { "blind", "talkingBook", "signLanguage", "quiz", "motion", "comic", "activity", "widget" };
			var metadata = new BookMetaData();

			// System under test
			metadata.Features = input;  // Run the setter
			string[] convertBackResult = metadata.Features;	// Run the getter

			// Verify that converting back gets the same result (We don't care about the order they're in, though))
			CollectionAssert.AreEqual(input.OrderBy(x => x), convertBackResult.OrderBy(x => x));

			// Verify individual other properties too
			Assert.AreEqual(true, metadata.Feature_Blind, "Blind");
			Assert.AreEqual(true, metadata.Feature_TalkingBook, "TalkingBook");
			Assert.AreEqual(true, metadata.Feature_SignLanguage, "SignLanguage");
			Assert.AreEqual(true, metadata.Feature_Quiz, "Quiz");
			Assert.AreEqual(true, metadata.Feature_Motion, "Motion");
			Assert.AreEqual(true, metadata.Feature_Comic, "Comic");
			Assert.AreEqual(true, metadata.Feature_Activity, "Activity");
			Assert.AreEqual(true, metadata.Feature_Widget, "Widget");

			string[] expectedResult = new string[] { "" };
			CollectionAssert.AreEqual(expectedResult, metadata.Feature_Blind_LangCodes, "Blind Language Codes");
			CollectionAssert.AreEqual(expectedResult, metadata.Feature_TalkingBook_LangCodes, "TB Language Codes");
			CollectionAssert.AreEqual(expectedResult, metadata.Feature_SignLanguage_LangCodes, "SL Language Codes");
		}

		[Test]
		public void AudioLangsToPublishForBloomReader_GivenNonDefaultJson_DeserializesProperly()
		{
			var json = "{ \"audioLangsToPublish\": { \"bloomReader\": { \"en\": \"Include\" } } }";
			var metadata = BookMetaData.FromString(json);

			var expected = new Dictionary<string, Bloom.Publish.LangToPublishCheckboxValue>();
			expected.Add("en", Bloom.Publish.LangToPublishCheckboxValue.Include);
			CollectionAssert.AreEquivalent(expected, metadata.AudioLangsToPublish.ForBloomReader);
		}
	}
}
