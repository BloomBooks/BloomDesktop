using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Bloom.Book;
using Bloom.Edit;
using Bloom.Publish;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.IO;
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
			File.WriteAllText(jsonPath, @"{""folio"":""true"",""experimental"":""true"",""suitableForMakingShells"":""true""}");
			var playerSettingsPath = Path.Combine(_folder.Path, BookInfo.PublishSettingsFileName);
			File.WriteAllText(playerSettingsPath, @"{""audioVideo"": {""motion"":true, ""pageTurnDelay"":3500,
						""format"":""feature"",
					""playerSettings"": ""{\""lang\"":\""qaa\"",\""imageDescriptions\"":false}""},
					""bloomPUB"": {""motion"": true},
					""bloomLibrary"": {""textLangs"": {""de"":""ExcludeByDefault"",""en"":""Exclude""}}}");
			var bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio);
			Assert.That(bi.IsSuitableForMakingShells);
			var ps = bi.PublishSettings;
			Assert.That(ps.AudioVideo.Format, Is.EqualTo("feature"));
			Assert.That(ps.AudioVideo.Motion, Is.True);
			Assert.That(ps.AudioVideo.PageTurnDelay, Is.EqualTo(3500));
			Assert.That(ps.AudioVideo.PlayerSettings, Is.EqualTo("{\"lang\":\"qaa\",\"imageDescriptions\":false}"));
			Assert.That(ps.BloomPub.PublishAsMotionBookIfApplicable, Is.True);
			Assert.That(ps.BloomLibrary.TextLangs["de"], Is.EqualTo(InclusionSetting.ExcludeByDefault));
			Assert.That(ps.BloomLibrary.TextLangs["en"], Is.EqualTo(InclusionSetting.Exclude));
		}

		[Test]
		public void Constructor_OldJson_HasDefaultPublishProps()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{""folio"":""true"",""experimental"":""true"",""suitableForMakingShells"":""true""}");
			var bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio);
			Assert.That(bi.IsSuitableForMakingShells);
			Assert.That(bi.PublishSettings.AudioVideo.Format, Is.EqualTo("facebook"));
			Assert.That(bi.PublishSettings.AudioVideo.Motion, Is.False);
			Assert.That(bi.PublishSettings.AudioVideo.PageTurnDelay, Is.EqualTo(3000));
			Assert.That(bi.PublishSettings.AudioVideo.PlayerSettings, Is.EqualTo(""));
			Assert.That(bi.PublishSettings.BloomPub.PublishAsMotionBookIfApplicable, Is.True);
			Assert.That(bi.PublishSettings.BloomLibrary.TextLangs, Is.Not.Null);
			Assert.That(bi.PublishSettings.BloomPub.TextLangs, Is.Not.Null);
			Assert.That(bi.PublishSettings.BloomLibrary.AudioLangs, Is.Not.Null);
			Assert.That(bi.PublishSettings.BloomPub.AudioLangs, Is.Not.Null);
			Assert.That(bi.PublishSettings.BloomLibrary.SignLangs, Is.Not.Null);
			Assert.That(bi.PublishSettings.BloomPub.SignLangs, Is.Not.Null);
		}

		[Test]
		public void UpdateOneSingletonTag_GivenNoTags_AddsWithoutException()
		{
			BookInfo bi = new BookInfo(_folder.Path, true);

			// SUT
			bi.UpdateOneSingletonTag("bookshelf", "value1");

			// Verification
			CollectionAssert.AreEquivalent(new string[] {"bookshelf:value1" }, bi.MetaData.Tags);
		}
		[Test]
		public void LoadPublishSettings_NullsInBloomPubReplaceWithDefault()
		{
			var input =
				@"{'bloomPUB': {'motion': true, 'textLangs': null,
						'audioLangs':null,
						'signLangs': null,
						'imageSettings':null},
			}";
			var ps = PublishSettings.FromString(input);
			Assert.That(ps.BloomPub.TextLangs.Count, Is.EqualTo(0));
			Assert.That(ps.BloomPub.AudioLangs.Count, Is.EqualTo(0));
			Assert.That(ps.BloomPub.SignLangs.Count, Is.EqualTo(0));
			Assert.That(ps.BloomPub.ImageSettings, Is.Not.Null);
		}

			[Test]
		public void LoadPublishSettings_AllSettings_Works()
		{
			var input =
				@"{""audioVideo"": {""motion"":true, ""pageTurnDelay"":2500,
					""format"":""feature"",
					""playerSettings"": ""{\""lang\"":\""fr\"",\""imageDescriptions\"":true}""},
					""bloomPUB"": {""motion"": true, ""textLangs"": {""baa"":""Include"",""es"":""Exclude""},
						""audioLangs"": { ""baa"":""IncludeByDefault"",""es"":""ExcludeByDefault""},
						""signLangs"": { ""asl"":""Include""}},
					""epub"": {""howToPublishImageDescriptions"":1, ""removeFontSizes"":true},
					""bloomLibrary"": {""textLangs"": {""def"":""Include"",""xyz"":""Exclude"", ""abc"":""ExcludeByDefault""},
						""audioLangs"": { ""def"":""IncludeByDefault"",""xyz"":""ExcludeByDefault""},
						""signLangs"": { ""asl"":""Include""}}}";
			var ps = PublishSettings.FromString(input);

			Assert.That(ps.AudioVideo.Format, Is.EqualTo("feature"));
			Assert.That(ps.AudioVideo.Motion, Is.True);
			Assert.That(ps.AudioVideo.PageTurnDelay, Is.EqualTo(2500));
			Assert.That(ps.AudioVideo.PlayerSettings, Is.EqualTo("{\"lang\":\"fr\",\"imageDescriptions\":true}"));

			Assert.That(ps.BloomPub.PublishAsMotionBookIfApplicable, Is.True);
			Assert.That(ps.BloomPub.TextLangs["baa"], Is.EqualTo(InclusionSetting.Include));
			Assert.That(ps.BloomPub.TextLangs["es"], Is.EqualTo(InclusionSetting.Exclude));
			Assert.That(ps.BloomPub.AudioLangs["baa"], Is.EqualTo(InclusionSetting.IncludeByDefault));
			Assert.That(ps.BloomPub.AudioLangs["es"], Is.EqualTo(InclusionSetting.ExcludeByDefault));
			Assert.That(ps.BloomPub.SignLangs["asl"], Is.EqualTo(InclusionSetting.Include));

			Assert.That(ps.Epub.HowToPublishImageDescriptions, Is.EqualTo(BookInfo.HowToPublishImageDescriptions.OnPage));
			Assert.That(ps.Epub.RemoveFontSizes, Is.True);

			Assert.That(ps.BloomLibrary.TextLangs["def"], Is.EqualTo(InclusionSetting.Include));
			Assert.That(ps.BloomLibrary.TextLangs["xyz"], Is.EqualTo(InclusionSetting.Exclude));
			Assert.That(ps.BloomLibrary.TextLangs["abc"], Is.EqualTo(InclusionSetting.ExcludeByDefault));
			Assert.That(ps.BloomLibrary.AudioLangs["def"], Is.EqualTo(InclusionSetting.IncludeByDefault));
			Assert.That(ps.BloomLibrary.AudioLangs["xyz"], Is.EqualTo(InclusionSetting.ExcludeByDefault));
			Assert.That(ps.BloomLibrary.SignLangs["asl"], Is.EqualTo(InclusionSetting.Include));
		}

		[Test]
		public void PublishSettings_RoundTrip_Works()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{""suitableForMakingShells"":""true""}");
			var original = new BookInfo(_folder.Path, true);
			original.PublishSettings.AudioVideo.PlayerSettings = "{\"lang\":\"fr\",\"imageDescriptions\":true}";
			original.Save();
			var restored = new BookInfo(_folder.Path, true);
			Assert.That(restored.PublishSettings.AudioVideo.PlayerSettings, Is.EqualTo(original.PublishSettings.AudioVideo.PlayerSettings));
		}

		[Test]
		public void PublishSettings_MigrateFromOldSettings()
		{
			// Todo: when I have all the old settings migrating, Save the meta.json that this writes,
			// use it to create the input here, and get rid of them from BookMetaData.
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{""suitableForMakingShells"":""true"", ""epub_HowToPublishImageDescriptions"":0}");
			var original = new BookInfo(_folder.Path, true);

			// Reading the file we just wrote will have attempted a migration. It should get all default values.
			Assert.That(original.PublishSettings.AudioVideo.Motion, Is.False);
			Assert.That(original.PublishSettings.Epub.HowToPublishImageDescriptions, Is.EqualTo(BookInfo.HowToPublishImageDescriptions.None));
			Assert.That(original.PublishSettings.Epub.RemoveFontSizes, Is.False);
			Assert.That(original.PublishSettings.BloomPub.TextLangs, Is.Not.Null);
			Assert.That(original.PublishSettings.BloomLibrary.TextLangs, Is.Not.Null);
			Assert.That(original.PublishSettings.BloomPub.AudioLangs, Is.Not.Null);
			Assert.That(original.PublishSettings.BloomLibrary.AudioLangs, Is.Not.Null);
			Assert.That(original.PublishSettings.BloomPub.SignLangs, Is.Not.Null);
			Assert.That(original.PublishSettings.BloomLibrary.SignLangs, Is.Not.Null);

			// oldMetaJson was produced by the following code, which depends on the obsolete properties:
			//original.MetaData.Feature_Motion = true;
			//original.MetaData.A11y_NoEssentialInfoByColor = true;
			//original.MetaData.A11y_NoTextIncludedInAnyImages = true;
			//original.MetaData.Epub_HowToPublishImageDescriptions = BookInfo.HowToPublishImageDescriptions.OnPage;
			//original.MetaData.Epub_RemoveFontSizes = true;

			//original.MetaData.TextLangsToPublish = new LangsToPublishSetting()
			//	{ForBloomLibrary = new Dictionary<string, InclusionSetting>(),ForBloomPUB = new Dictionary<string, InclusionSetting>()};
			//original.MetaData.TextLangsToPublish.ForBloomLibrary["en"] = InclusionSetting.Include;
			//original.MetaData.TextLangsToPublish.ForBloomLibrary["fr"] = InclusionSetting.Exclude;
			//original.MetaData.TextLangsToPublish.ForBloomLibrary["tpi"] = InclusionSetting.IncludeByDefault;
			//original.MetaData.TextLangsToPublish.ForBloomPUB["tpi"] = InclusionSetting.ExcludeByDefault;
			//original.MetaData.TextLangsToPublish.ForBloomPUB["en"] = InclusionSetting.Exclude;

			//original.MetaData.AudioLangsToPublish = new LangsToPublishSetting()
			//	{ ForBloomLibrary = new Dictionary<string, InclusionSetting>(), ForBloomPUB = new Dictionary<string, InclusionSetting>() };
			//original.MetaData.AudioLangsToPublish.ForBloomLibrary["en"] = InclusionSetting.ExcludeByDefault;
			//original.MetaData.AudioLangsToPublish.ForBloomPUB["es"] = InclusionSetting.IncludeByDefault;

			//original.MetaData.SignLangsToPublish = new LangsToPublishSetting()
			//	{ ForBloomLibrary = new Dictionary<string, InclusionSetting>(), ForBloomPUB = new Dictionary<string, InclusionSetting>() };
			//original.MetaData.SignLangsToPublish.ForBloomLibrary["qaa"] = InclusionSetting.Include;
			//original.MetaData.SignLangsToPublish.ForBloomPUB["qed"] = InclusionSetting.Exclude;
			//original.Save();

			var oldMetaJson =
				@"{""a11y_NoEssentialInfoByColor"":true,""a11y_NoTextIncludedInAnyImages"":true,""epub_HowToPublishImageDescriptions"":1,
				""epub_RemoveFontStyles"":true,""bookInstanceId"":""738cf8ea-357d-4c50-88d2-60774ffbd32b"",""suitableForMakingShells"":true,
				""suitableForMakingTemplates"":false,""suitableForVernacularLibrary"":true,""bloomdVersion"":0,""experimental"":false,
				""brandingProjectName"":null,""nameLocked"":false,""folio"":false,""isRtl"":false,""title"":"""",""allTitles"":null,
				""originalTitle"":null,""baseUrl"":null,""bookOrder"":null,""isbn"":null,""bookLineage"":"""",""downloadSource"":null,
				""license"":null,""formatVersion"":null,""licenseNotes"":null,""copyright"":null,""credits"":null,""tags"":null,
				""pageCount"":0,""languages"":[],""langPointers"":null,""summary"":null,""allowUploadingToBloomLibrary"":true,
				""bookletMakingIsAppropriate"":true,""textLangsToPublish"":{""bloomPUB"":{""tpi"":""ExcludeByDefault"",""en"":""Exclude""},
				""bloomLibrary"":{""en"":""Include"",""fr"":""Exclude"",""tpi"":""IncludeByDefault""}},
				""audioLangsToPublish"":{""bloomPUB"":{""es"":""IncludeByDefault""},""bloomLibrary"":{""en"":""ExcludeByDefault""}},
				""signLangsToPublish"":{""bloomPUB"":{""qed"":""Exclude""},""bloomLibrary"":{""qaa"":""Include""}},""country"":null,
				""province"":null,""district"":null,""uploader"":null,""tools"":null,""toolboxIsOpen"":false,""author"":null,
				""publisher"":null,""originalPublisher"":null,""subjects"":null,""hazards"":null,""a11yFeatures"":null,
				""a11yLevel"":null,""a11yCertifier"":null,""readingLevelDescription"":null,""typicalAgeRange"":null,
				""features"":[""motion""],""page-number-style"":null,""language-display-names"":null,""internetLimits"":null,
				""use-original-copyright"":false,""imported-book-source-url"":null,""phashOfFirstContentImage"":null}";
			File.WriteAllText(jsonPath, oldMetaJson);

			var settingsPath = PublishSettings.PublishSettingsPath(_folder.Path);
			RobustFile.Delete(settingsPath);
			var restored = new BookInfo(_folder.Path, true);
			Assert.That(restored.PublishSettings.AudioVideo.Motion, Is.True);
			Assert.That(restored.PublishSettings.Epub.HowToPublishImageDescriptions, Is.EqualTo(BookInfo.HowToPublishImageDescriptions.OnPage));
			Assert.That(restored.PublishSettings.Epub.RemoveFontSizes, Is.True);

			Assert.That(restored.PublishSettings.BloomLibrary.TextLangs["en"], Is.EqualTo(InclusionSetting.Include));
			Assert.That(restored.PublishSettings.BloomLibrary.TextLangs["fr"], Is.EqualTo(InclusionSetting.Exclude));
			Assert.That(restored.PublishSettings.BloomLibrary.TextLangs["tpi"], Is.EqualTo(InclusionSetting.IncludeByDefault));
			Assert.That(restored.PublishSettings.BloomPub.TextLangs["tpi"], Is.EqualTo(InclusionSetting.ExcludeByDefault));
			Assert.That(restored.PublishSettings.BloomPub.TextLangs["en"], Is.EqualTo(InclusionSetting.Exclude));

			Assert.That(restored.PublishSettings.BloomLibrary.AudioLangs["en"], Is.EqualTo(InclusionSetting.ExcludeByDefault));
			Assert.That(restored.PublishSettings.BloomPub.AudioLangs["es"], Is.EqualTo(InclusionSetting.IncludeByDefault));
			Assert.That(restored.PublishSettings.BloomLibrary.SignLangs["qaa"], Is.EqualTo(InclusionSetting.Include));
			Assert.That(restored.PublishSettings.BloomPub.SignLangs["qed"], Is.EqualTo(InclusionSetting.Exclude));
		}

		[TestCase(new object[] { "bookshelf:oldValue" })]
		[TestCase(new object[] { "bookshelf:oldValue1", "bookshelf:oldValue2" })]	// Not quite sure how you get in this state, but the function is supposed to remove all of the previous ones
		public void UpdateOneSingletonTag_GivenATag_WhenNewTagAdded_OldValueReplaced(object[] existingContents)
		{
			BookInfo bi = new BookInfo(_folder.Path, true);
			// Converts from object[] to string[], because TestCase attribute doesn't support string[] directly
			bi.MetaData.Tags = existingContents.Select(x => x.ToString()).ToArray();	

			// SUT
			bi.UpdateOneSingletonTag("bookshelf", "newValue");

			// Verification
			CollectionAssert.AreEquivalent(new string[] {"bookshelf:newValue" }, bi.MetaData.Tags);
		}

		[Test]
		public void UpdateOneSingletonTag_GivenATag_WhenUnrelatedKeyAdded_OldKeyUntouched()
		{
			BookInfo bi = new BookInfo(_folder.Path, true);
			bi.MetaData.Tags = new string[] { "key1:value1" } ;

			// SUT
			bi.UpdateOneSingletonTag("key2", "value2");

			// Verification
			CollectionAssert.AreEquivalent(new string[] {"key1:value1", "key2:value2" }, bi.MetaData.Tags);
		}

		[TestCase("")]
		[TestCase(null)]
		[TestCase("   ")]	// all whitespace should be removed too
		public void UpdateOneSingletonTag_GivenATag_WhenValueUpdatedToNullOrWhiteSpace_TagIsRemoved(string input)
		{
			BookInfo bi = new BookInfo(_folder.Path, true);
			bi.MetaData.Tags = new string[] { "bookshelf:value" } ;

			// SUT
			bi.UpdateOneSingletonTag("bookshelf", input);

			// Verification
			CollectionAssert.AreEquivalent(new string[] { }, bi.MetaData.Tags);
		}

		[Test]
		public void InstallFreshGuid_works()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			const string originalGuid = "3988218f-e01c-4a7a-b27d-4a31dd632ccb";
			const string metaData = @"{""bookInstanceId"":""" + originalGuid + @""",""experimental"":""false"",""suitableForMakingShells"":""true""}";
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
			File.WriteAllText(jsonPath, @"{""folio"":""false"",""experimental"":""true"",""suitableForMakingShells"":""false""}");
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
				@"{""title"":""<span class='sentence-too-long' data-segment='sentence'>Book on &lt;span&gt;s\r\n</span>""}");
			var bi = new BookInfo(_folder.Path, true); // loads metadata, but doesn't use Title setter
			// SUT
			bi.Title = bi.Title; // exercises setter
			Assert.AreEqual("Book on <span>s\r\n", bi.Title);
		}

		[Test]
		public void RoundTrips_AllowUploading()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{""allowUploadingToBloomLibrary"":""false""}");
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
				ToolStates = new List<ToolboxToolState>(new [] {ToolboxToolState.CreateFromToolId("decodableReader")}),
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
			Assert.That(meta2.ToolStates, Is.Null);
			Assert.That(meta2.CountryName, Is.EqualTo("InTheBush"));
			Assert.That(meta2.ProvinceName, Is.EqualTo("Provence"));
			Assert.That(meta2.DistrictName, Is.EqualTo("Ocean"));
			Assert.That(meta2.BookletMakingIsAppropriate, Is.True); // default value
		}

		[Test]
		public void GetRepairedMetaDataWithIdOnly_FindsId()
		{
			using (var tempFile =
			       new TempFile("junk rubbish nonsense \"bookInstanceId\":\"abcdefg-1234\" lots more junk"))
			{
				Assert.That(BookMetaData.GetRepairedMetaDataWithIdOnly(tempFile.Path).Id,
					Is.EqualTo("abcdefg-1234"));
			}
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

		[TestCase(@"""Fiction""", "Fiction")]
		[TestCase(@"""Math"", ""Fiction""", "Math, Fiction")]
		[TestCase(@"""topic:Fiction""", "Fiction")]
		[TestCase(@"""Fiction"",""media:audio""", "Fiction")]
		[TestCase(@"""media:audio"",""Fiction""", "Fiction")]
		[TestCase(@"""topic:Math"",""Fiction""", "Math, Fiction")]
		public void TopicsList_GetsTopicsAndOnlyTopicsFromTagsList(string jsonTagsList, string expectedTopicsList)
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{""tags"":[" + jsonTagsList + @"]}");
			var bi = new BookInfo(_folder.Path, true);
			Assert.AreEqual(expectedTopicsList, bi.TopicsList);
		}

		[TestCase(@"""topic:Fiction""", "", "", new string[0])]
		[TestCase(@"""media:audio""", "", "", new[] { "media:audio" })]
		[TestCase(@"""media:audio"", ""topic:Fiction""", "", "", new[] { "media:audio" })]
		[TestCase(@"""Fiction""", "Math", "Math", new []{ "topic:Math" })]
		[TestCase(@"""Fiction"",""Math""", "Math", "Math", new[] { "topic:Math" })]
		[TestCase(@"""Fiction"",""Math"",""media:audio""", "Math", "Math", new[] { "media:audio", "topic:Math" })]
		[TestCase(@"""media:audio""", "topic:Math", "Math", new[] { "media:audio", "topic:Math" })]
		[TestCase(@"""media:audio"",""region:Asia""", "topic:Math,topic:Fiction", "Math, Fiction", new[] { "media:audio", "region:Asia", "topic:Math", "topic:Fiction" })]
		[TestCase(@"""topic:Science""", "topic:Math,topic:Fiction", "Math, Fiction", new[] { "topic:Math", "topic:Fiction" })]
		public void TopicsList_SetsTopicsWhileLeavingOtherTagsIntact(string jsonTagsList, string topicsListToSet, string expectedTopicsList, string[] expectedTags)
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{""tags"":[" + jsonTagsList + @"]}");
			var bi = new BookInfo(_folder.Path, true);

			//SUT
			bi.TopicsList = topicsListToSet;

			Assert.AreEqual(expectedTopicsList, bi.TopicsList);

			BookMetaData metadata = (BookMetaData) ReflectionHelper.GetField(bi, "_metadata");
			Assert.AreEqual(expectedTags, metadata.Tags);
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
		[TestCase(false)]
		[TestCase(true)]
		public void FeaturesGetter_SimpleDomChoice(bool containsSimpleDomChoice)
		{
			var metadata = new BookMetaData();
			metadata.Feature_SimpleDomChoice = containsSimpleDomChoice;
			Assert.AreEqual(containsSimpleDomChoice, metadata.Features.Contains("simple-dom-choice"));
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
		public void RuntimeInformationInjector_PullInCollectionLanguagesDisplayNames_GetsItRight()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{""language-display-names"":{""sok"":""Sokoro"",""en"":""English"",""de"":""Custom German Name"",""fr"":""French"",""tza"":""Tanzanian Sign Language""}}");
			var bookInfo = new BookInfo(_folder.Path, true);
			var d = new Dictionary<string, string>();

			// SUT
			RuntimeInformationInjector.PullInCollectionLanguagesDisplayNames(d, bookInfo);

			// Verification
			Assert.AreEqual(d["en"], "English");
			Assert.AreEqual(d["fr"], "French");
			Assert.AreEqual(d["de"], "Custom German Name");
			Assert.AreEqual(d["sok"], "Sokoro");
		}
	}
}
