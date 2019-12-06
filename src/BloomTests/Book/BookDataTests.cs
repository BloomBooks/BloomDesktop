using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using NUnit.Framework;
using NUnit.Framework.Internal;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;
using SIL.Windows.Forms.ClearShare;
using SIL.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public sealed class BookDataTests
	{
		private CollectionSettings _collectionSettings;
		private ILocalizationManager _localizationManager;
		private ILocalizationManager _palasoLocalizationManager;

		[SetUp]
		public void Setup()
		{
			_collectionSettings = new CollectionSettings(new NewCollectionSettings()
			{
				PathToSettingsFile =
					CollectionSettings.GetPathForNewSettings(new TemporaryFolder("BookDataTests").Path, "test"),
			});
			_collectionSettings.Language1.Iso639Code = "xyz";
			_collectionSettings.Language2.Iso639Code = "en";
			_collectionSettings.Language3.Iso639Code = "fr";
			ErrorReport.IsOkToInteractWithUser = false;

			LocalizationManager.UseLanguageCodeFolders = true;
			var localizationDirectory = FileLocationUtilities.GetDirectoryDistributedWithApplication("localization");
			_localizationManager = LocalizationManager.Create(TranslationMemory.XLiff, "fr", "Bloom", "Bloom", "1.0.0",
				localizationDirectory, "SIL/Bloom",
				null, "");
			_palasoLocalizationManager = LocalizationManager.Create(TranslationMemory.XLiff, "fr", "Palaso", "Palaso",
				"1.0.0", localizationDirectory, "SIL/Palaso",
				null, "");
		}

		[TearDown]
		public void TearDown()
		{
			_localizationManager.Dispose();
			_palasoLocalizationManager.Dispose();
			LocalizationManager.ForgetDisposedManagers();
		}

		[Test]
		public void TextOfInnerHtml_RemovesMarkup()
		{
			var input = "This <em>is</em> the day";
			var output = BookData.TextOfInnerHtml(input);
			Assert.That(output, Is.EqualTo("This is the day"));
		}

		[Test]
		public void TextOfInnerHtml_HandlesSpansProperly()
		{
			var input = "<p><strong><span class='x'>01.</span></strong> <span class='y'>The Creation</span></p>";
			var output = BookData.TextOfInnerHtml(input);
			Assert.That(output, Is.EqualTo("01. The Creation"));
		}

		[Test]
		public void TextOfInnerHtml_HandlesXmlEscapesCorrectly()
		{
			var input =
				"Jack &amp; Jill like xml sequences like &amp;amp; &amp; &amp;lt; &amp; &amp;gt; for characters like &lt;&amp;&gt;";
			var output = BookData.TextOfInnerHtml(input);
			Assert.That(output,
				Is.EqualTo("Jack & Jill like xml sequences like &amp; & &lt; & &gt; for characters like <&>"));
		}

		[Test]
		public void MakeLanguageUploadData_FindsDefaultInfo()
		{
			var results = _collectionSettings.MakeLanguageUploadData(new[] {"en", "tpi", "xy3"});
			Assert.That(results.Length, Is.EqualTo(3), "should get one result per input");
			VerifyLangData(results[0], "en", "English", "eng");
			VerifyLangData(results[1], "tpi", "Tok Pisin", "tpi");
			VerifyLangData(results[2], "xy3", "xy3", "xy3");
		}

		[Test]
		public void MakeLanguageUploadData_FindsOverriddenNames()
		{
			_collectionSettings.Language1.Name = "Cockney";
			// Note: no current way of overriding others; verify they aren't changed.
			var results = _collectionSettings.MakeLanguageUploadData(new[] {"en", "tpi", "xyz"});
			Assert.That(results.Length, Is.EqualTo(3), "should get one result per input");
			VerifyLangData(results[0], "en", "English", "eng");
			VerifyLangData(results[1], "tpi", "Tok Pisin", "tpi");
			VerifyLangData(results[2], "xyz", "Cockney", "xyz");
		}

		void VerifyLangData(LanguageDescriptor lang, string code, string name, string ethCode)
		{
			Assert.That(lang.IsoCode, Is.EqualTo(code));
			Assert.That(lang.Name, Is.EqualTo(name));
			Assert.That(lang.EthnologueCode, Is.EqualTo(ethCode));
		}

		[Test]
		public void SuckInDataFromEditedDom_NoDataDIvTitleChanged_NewTitleInCache()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>original</textarea>
				</div>
			 </body></html>");
			var data = new BookData(bookDom, _collectionSettings, null);
			Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));


			HtmlDom editedPageDom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>changed</textarea>
				</div>
			 </body></html>");

			data.SuckInDataFromEditedDom(editedPageDom);

			Assert.AreEqual("changed", data.GetVariableOrNull("bookTitle", "xyz"));
		}

		/// <summary>
		/// Regression test: the difference between this situation (had a value before) and the one where this is newly discovered was the source of a bug
		/// </summary>
		[Test]
		public void SuckInDataFromEditedDom_HasDataDivWithOldTitleThenTitleChanged_NewTitleInCache()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>original</textarea>
				</div>
			 </body></html>");

			var data = new BookData(bookDom, _collectionSettings, null);
			Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));

			HtmlDom editedPageDom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>changed</textarea>
				</div>
			 </body></html>");

			data.SuckInDataFromEditedDom(editedPageDom);

			Assert.AreEqual("changed", data.GetVariableOrNull("bookTitle", "xyz"));
		}

		// BRANDING-RELATED TESTS

		[Test]
		public void MergeSettings_NoSettings_DoesNothing()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>");

			var data = new BookData(bookDom, _collectionSettings, null);
			Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
			data.MergeBrandingSettings("nonsense");
			Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
		}

		[Test]
		public void MergeSettings_SettingsExistsButIsEmpty_DoesNothing()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>");
			using (var tempFolder = new TemporaryFolder("MergeSettings_SettingsExistsButIsEmpty_DoesNothing"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"), @"{}");

				var data = new BookData(bookDom, _collectionSettings, null);
				Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
				data.MergeBrandingSettings(tempFolder.Path);
				Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
			}
		}

		[Test]
		public void MergeSettings_SettingsExistsButHasBogusJson_DoesNothing()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>");
			using (var tempFolder = new TemporaryFolder("MergeSettings_SettingsExistsButIsEmpty_DoesNothing"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"), "");

				var data = new BookData(bookDom, _collectionSettings, null);
				Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
				data.MergeBrandingSettings(tempFolder.Path);
				Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
			}
		}

		[Test]
		public void MergeSettings_SettingsExistsButLacksCondition_DoesNothing()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>");
			using (var tempFolder = new TemporaryFolder("MergeSettings_SettingsExistsButLacksCondition_DoesNothing"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"), @"{
	""presets"": [{
		""data-book"": ""insideBackCover"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings""
	}, {
		""data-book"": ""insideBackCover"",
		""lang"": ""en"",
		""content"": ""English stuff from settings""
	},  {
		""data-book"": ""title"",
		""lang"": ""xyz"",
		""content"": ""xyz title"",
		""condition"":""someUnknownCondition""
	}]
}");

				var data = new BookData(bookDom, _collectionSettings, null);
				data.MergeBrandingSettings(tempFolder.Path);
				Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
				Assert.That(data.GetVariableOrNull("insideBackCover", "en"), Is.Null);
				Assert.That(data.GetVariableOrNull("title", "xyz"), Is.Null);
			}
		}

		[Test]
		public void MergeSettings_UpdatesEmptyField()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'></div>
				</div>
			 </body></html>");

			using (var tempFolder = new TemporaryFolder("MergeSettings_UpdatesEmptyField"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"),
					// First item tests successful setting;
					// Second tests setting of another language of same property;
					// Third tests setting of another property;
					// Fourth tests overwrite prevented by existing value;
					// Remaining ones should be ignored, present to verify this.
					@"{
	""presets"": [{
		""data-book"": ""insideBackCover"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""insideBackCover"",
		""lang"": ""en"",
		""content"": ""English stuff from settings"",
		""condition"":""ifEmpty""
	},  {
		""data-book"": ""title"",
		""lang"": ""xyz"",
		""content"": ""xyz title"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""bookTitle"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}, {
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""bookTitle"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""bookTitle"",
		""lang"": ""xyz"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": "" "",
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}]
}");
				var data = new BookData(bookDom, _collectionSettings, null);
				data.MergeBrandingSettings(tempFolder.Path);

				Assert.AreEqual("stuff from settings", data.GetVariableOrNull("insideBackCover", "xyz"));
				Assert.AreEqual("English stuff from settings", data.GetVariableOrNull("insideBackCover", "en"));
				Assert.AreEqual("xyz title", data.GetVariableOrNull("title", "xyz"));
				Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
			}
		}

		[Test]
		public void MergeSettings_OverridesIfSpecified()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>");

			using (var tempFolder = new TemporaryFolder("MergeSettings_UpdatesEmptyField"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"),
					// First item tests successful setting;
					// Second tests setting of another language of same property;
					@"{
	""presets"": [{
		""data-book"": ""insideBackCover"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings"",
		""condition"":""always""
}, {
		""data-book"": ""bookTitle"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}]
}");
				var data = new BookData(bookDom, _collectionSettings, null);
				data.MergeBrandingSettings(tempFolder.Path);

				Assert.AreEqual("stuff from settings", data.GetVariableOrNull("insideBackCover", "xyz"));
				Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));
			}
		}

		[Test]
		public void MergeSettings_BrandingHasLicenseAndNotesButNotCopyright_MetadataMatches()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>");
			using (var tempFolder = new TemporaryFolder("MergeSettings_UpdatesEmptyField"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"),
					// First item tests successful setting;
					// Second tests setting of another language of same property;
					@"{
	""presets"": [{
		""data-book"": ""licenseNotes"",
		""lang"": ""*"",
		""content"": ""These are custom notes."",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""licenseUrl"",
		""lang"": ""*"",
		""content"": ""http://creativecommons.org/licenses/by/3.0/igo/"",
		""condition"":""ifEmpty""
	}]
}");
				var data = new BookData(bookDom, _collectionSettings, null);
				data.MergeBrandingSettings(tempFolder.Path);
				var metadata = BookCopyrightAndLicense.GetMetadata(bookDom);
				Assert.AreEqual("http://creativecommons.org/licenses/by/3.0/igo/", metadata.License.Url);
				Assert.AreEqual("These are custom notes.", metadata.License.RightsStatement);
				Assert.That(metadata.CopyrightNotice, Is.Null.Or.Empty);
			}
		}

		[Test]
		public void MergeSettings_HasCopyrightAndLicenseAndLicenseNotes_MetadataMatches()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>");
			using (var tempFolder = new TemporaryFolder("MergeSettings_UpdatesEmptyField"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"),
					// First item tests successful setting;
					// Second tests setting of another language of same property;
					@"{
	""presets"": [{
		""data-book"": ""licenseNotes"",
		""lang"": ""*"",
		""content"": ""These are custom notes."",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""licenseUrl"",
		""lang"": ""*"",
		""content"": ""http://creativecommons.org/licenses/by/3.0/igo/"",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""copyright"",
		""lang"": ""*"",
		""content"": ""Copyright © 2016"",
		""condition"":""ifAllCopyrightEmpty""
	}]
}");
				var data = new BookData(bookDom, _collectionSettings, null);
				data.MergeBrandingSettings(tempFolder.Path);
				var metadata = BookCopyrightAndLicense.GetMetadata(bookDom);

				Assert.AreEqual("http://creativecommons.org/licenses/by/3.0/igo/", metadata.License.Url);
				Assert.AreEqual("These are custom notes.", metadata.License.RightsStatement);
				Assert.AreEqual("Copyright © 2016", metadata.CopyrightNotice);
			}
		}

		// This is actually the only REASONABLE way to specify copyrights; these other tests are kinda
		// bogus in having copyright notices that say things like "Copyright © 2016".... that doesn't
		// even say the org, and would incorrectly insert 2016 regardless of the actual year of the book.
		// The following tests a real use.
		[Test]
		public void MergeSettings_HasJustCopyrightOrg_GetsFullCopyrightForCurrentYear()
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>");
			using (var tempFolder = new TemporaryFolder("MergeSettings_UpdatesEmptyField"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"),
					// First item tests successful setting;
					// Second tests setting of another language of same property;
					@"{
						""presets"": [ {
							""data-book"": ""copyright"",
							""lang"": ""*"",
							""content"": ""Chewtoys International"",
							""condition"":""ifAllCopyrightEmpty""
						}]
					}");
				var data = new BookData(bookDom, _collectionSettings, null);
				data.MergeBrandingSettings(tempFolder.Path);
				var metadata = BookCopyrightAndLicense.GetMetadata(bookDom);

				Assert.AreEqual($"Copyright © {DateTime.Now.Year.ToString()} Chewtoys International",
					metadata.CopyrightNotice);
			}
		}

		// we don't want shell books to get this notice
		[TestCase("copyright", "Copyright © 2012, test")]
		[TestCase("licenseNotes", "Some extra notes")]
		[TestCase("licenseUrl", "http://creativecommons.org/licenses/by-nd/3.0/bynd/")]
		public void MergeSettings_HasCopyrightAlready_CustomBrandingStuffIgnored(string dataDivName,
			string dataDivContent)
		{
			HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='" + dataDivName + "' lang='*'>" + dataDivContent + @"</div>
				</div>
			 </body></html>");
			using (var tempFolder = new TemporaryFolder("MergeSettings_HasCopyrightAlready_CustomBrandingStuffIgnored"))
			{
				File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"),
					@"{
	""presets"": [{
		""data-book"": ""licenseNotes"",
		""lang"": ""*"",
		""content"": ""These are custom notes."",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""licenseUrl"",
		""lang"": ""*"",
		""content"": ""http://creativecommons.org/licenses/by/3.0/igo/"",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""copyright"",
		""lang"": ""*"",
		""content"": ""Copyright © 2016"",
		""condition"":""ifAllCopyrightEmpty""
	}]
}");
				var data = new BookData(bookDom, _collectionSettings, null);
				data.MergeBrandingSettings(tempFolder.Path);
				var metadata = BookCopyrightAndLicense.GetMetadata(bookDom);
				if (dataDivName == "copyright")
					Assert.IsTrue(metadata.CopyrightNotice.Contains("2012")); // unchanged from testcase
				else
					Assert.That(metadata.CopyrightNotice, Is.Null);
				if (dataDivName == "licenseUrl")
				{
					Assert.That(metadata.License, Is.InstanceOf<CreativeCommonsLicense>());
					Assert.That(metadata.License.Url,
						Is.EqualTo("http://creativecommons.org/licenses/by-nd/3.0/bynd/"));
				}
				else if (dataDivName == "copyright")
				{
					Assert.That(metadata.License, Is.InstanceOf<NullLicense>());
				}
				else
				{
					Assert.That(metadata.License is CustomLicense);
				}

				if (dataDivName == "licenseNotes")
					Assert.That(metadata.License.RightsStatement, Is.EqualTo("Some extra notes"));
				else
					Assert.That(metadata.License.RightsStatement, Is.Null.Or.Empty);
			}
		}

		[Test]
		public void
			SuckInDataFromEditedDom_XmatterPageAttributeAddedToXmatterPage_XmatterPageAttributeAddedToBookDataAndDataDiv()
		{
			HtmlDom bookDom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover'>
				</div>
			 </body></html>");

			var data = new BookData(bookDom, _collectionSettings, null);
			Assert.IsNull(data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio"));
			Assert.IsNull(data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume"));

			HtmlDom editedPageDom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
				</div>
			 </body></html>");

			data.SuckInDataFromEditedDom(editedPageDom);

			Assert.AreEqual("SoundTrack0.mp3",
				data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio"));
			Assert.AreEqual("0.21", data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume"));

			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' " +
				"and @data-backgroundaudio='SoundTrack0.mp3' and @data-backgroundaudiovolume='0.21' and not(text())]",
				1);
		}

		//[Test] TODO - this test doesn't work because SuckInDataFromEditedDom first updates the page from the data div before updating the data div from the page.
		// I couldn't get it worked out, but the production code does do things correctly. I attempted to add this test for BL-5409.
		public void
			SuckInDataFromEditedDom_XmatterPageAttributeUpdatedInXmatterPage_XmatterPageAttributeUpdatedInBookDataAndDataDiv()
		{
			HtmlDom bookDom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'>
					<div class='bloom-page frontCover' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
					</div>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
				</div>
			 </body></html>");

			var data = new BookData(bookDom, _collectionSettings, null);
			Assert.AreEqual("SoundTrack0.mp3",
				data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio"));
			Assert.AreEqual("0.21", data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume"));

			HtmlDom editedPageDom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'>
					<div class='bloom-page frontCover' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
					</div>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack2.mp3' data-backgroundaudiovolume='0.99'>
				</div>
			 </body></html>");

			data.SuckInDataFromEditedDom(editedPageDom);

			Assert.AreEqual("SoundTrack2.mp3",
				data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio"));
			Assert.AreEqual("0.99", data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume"));

			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' " +
				"and @data-backgroundaudio='SoundTrack2.mp3' and @data-backgroundaudiovolume='0.99' and not(text())]",
				1);
		}

		[Test]
		public void UpdateFieldsAndVariables_CustomLibraryVariable_CopiedToOtherElement()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id='copyOfVTitle'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='1' data-collection='testLibraryVariable'>aa</textarea>
					   <textarea lang='xyz' id='2'  data-collection='testLibraryVariable'>bb</textarea>
					</p>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.UpdateVariablesAndDataDivThroughDOM();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			Assert.AreEqual("aa", textarea2.InnerText);
		}

		[Test]
		public void UpdateFieldsAndVariables_HasBookTitleTemplateWithVernacularPlaceholder_CreatesTitleForVernacular()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitleTemplate' lang='{V}'>the title</div>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@data-book='bookTitle' and @lang='" + _collectionSettings.Language1Iso639Code +
				"' and text()='the title']", 1);
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_DataBookAttributes_AttributesAddedToDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'>
					<div data-xmatter-page='frontCover' " + HtmlDom.musicAttrName + "='audio/SoundTrack1.mp3' " +
			                      HtmlDom.musicVolumeName + @"='0.17'></div>
				</div>
				<div id='firstPage' class='bloom-page' data-xmatter-page='frontCover'>1st page</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@id='firstPage' and @data-xmatter-page='frontCover' and @"
				                                     + HtmlDom.musicAttrName + "='audio/SoundTrack1.mp3' and @" +
				                                     HtmlDom.musicVolumeName + "='0.17']", 1);
		}

		[Test]
		public void UpdateFieldsAndVariables_VernacularTitleChanged_TitleCopiedToParagraphAnotherPage()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' data-book='bookTitle'>original</textarea>
						</p>
				</div>
				<div class='bloom-page' id='0a99fad3-0a17-4240-a04e-86c2dd1ec3bd'>
						<p class='centered' lang='xyz' data-book='bookTitle' id='P1'>originalButNoExactlyCauseItShouldn'tMatter</p>
				</div>
			 </body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@data-book='bookTitle' and @lang='xyz']");
			textarea1.InnerText = "peace & quiet";
			data.SynchronizeDataItemsThroughoutDOM();
			var paragraph = dom.SelectSingleNodeHonoringDefaultNS("//p[@data-book='bookTitle'  and @lang='xyz']");
			Assert.AreEqual("peace & quiet", paragraph.InnerText);
		}


		[Test]
		public void UpdateFieldsAndVariables_OneDataItemChanges_ItemsWithThatLanguageAlsoUpdated()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>EnglishTitle</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id='3'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
			 </body></html>");
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='EnglishTitle']", 1);
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='xyzTitle']", 1);
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			textarea2.InnerText = "newXyzTitle";
			var data = new BookData(dom, CreateCollection(Language1Iso639Code: "etr"), null);
			data.SynchronizeDataItemsThroughoutDOM();
			var textarea3 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='3']");
			Assert.AreEqual("newXyzTitle", textarea3.InnerText);
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//textarea[@id='1' and text()='EnglishTitle']", 1);
		}

		[Test]
		public void UpdateFieldsAndVariables_EnglishDataItemChanges_VernItemsUntouched()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>EnglishTitle</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id='3'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
			 </body></html>");
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='EnglishTitle']", 1);
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='xyzTitle']", 1);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='1']");
			textarea1.InnerText = "newEnglishTitle";
			var data = new BookData(dom, CreateCollection(Language1Iso639Code: "etr"), null);
			data.SynchronizeDataItemsThroughoutDOM();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			Assert.AreEqual("xyzTitle", textarea2.InnerText);
			var textarea3 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='3']");
			Assert.AreEqual("xyzTitle", textarea3.InnerText);
		}


		[Test]
		public void UpdateFieldsAndVariables_BookTitleInSpanOnSecondPage_UpdatesH2OnFirstWithCurrentNationalLang()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page titlePage'>
						<div class='pageContent'>
							<h2 data-book='bookTitle' lang='N1'>{national book title}</h2>
						</div>
					</div>
				<div class='bloom-page verso'>
					<div class='pageContent'>
						(<span lang='en' data-book='bookTitle'>Vaccinations</span><span lang='tpi' data-book='bookTitle'>Tambu Sut</span>)
						<br />
					</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection(Language1Iso639Code: "etr");
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			XmlElement nationalTitle =
				(XmlElement) dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
			Assert.AreEqual("Vaccinations", nationalTitle.InnerText);

			//now switch the national language to Tok Pisin

			collectionSettings.Language2Iso639Code = "tpi";
			data.SynchronizeDataItemsThroughoutDOM();
			nationalTitle = (XmlElement) dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
			Assert.AreEqual("Tambu Sut", nationalTitle.InnerText);
		}

		[Test]
		public void UpdateFieldsAndVariables_OneLabelPreserved_DuplicatesRemovedNotAdded()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page titlePage'>
						<div id='target' class='bloom-content1' data-book='insideBackCover'>
							<label class='bubble'>Some more space to put things</label><label class='bubble'>Some more space to put things</label>Here is the content
						</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection(Language1Iso639Code: "etr");
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			XmlElement target = (XmlElement) dom.SelectSingleNodeHonoringDefaultNS("//div[@id='target']");

			// It's expected that the surviving label goes at the end.
			Assert.That(target.InnerText, Is.EqualTo("Here is the contentSome more space to put things"));
			XmlElement label = (XmlElement) target.SelectSingleNodeHonoringDefaultNS("//label");
			Assert.That(label.InnerText, Is.EqualTo("Some more space to put things"));
		}

		[Test]
		public void GetMultilingualContentLanguage_ContentLanguageSpecifiedInHtml_ReadsIt()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='contentLanguage2'>fr</div>
						<div data-book='contentLanguage3'>es</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection();
			var data = new BookData(dom, collectionSettings, null);
			Assert.AreEqual("fr", data.MultilingualContentLanguage2);
			Assert.AreEqual("es", data.MultilingualContentLanguage3);
		}

		[Test]
		public void SetMultilingualContentLanguage_ContentLanguageSpecifiedInHtml_ReadsIt()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='contentLanguage2'>fr</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection();
			var data = new BookData(dom, collectionSettings, null);
			data.SetMultilingualContentLanguages("en", "de");
			Assert.AreEqual("en", data.MultilingualContentLanguage2);
			Assert.AreEqual("de", data.MultilingualContentLanguage3);
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_NewLangAdded_AddedToDataDiv()
		{
			var dom = new HtmlDom(
				@"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div></body></html>");

			var e = dom.RawDom.CreateElement("div");
			e.SetAttribute("data-book", "someVariable");
			e.SetAttribute("lang", "fr");
			e.InnerText = "bonjour";
			dom.RawDom.SelectSingleNode("//body").AppendChild(e);
			var data = new BookData(dom, CreateCollection(), null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']",
					1); //NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='en' and text()='hi']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='fr' and text()='bonjour']", 1);
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_HasDataLibraryValues_LibraryValuesNotPutInDataDiv()
		{
			var dom = new HtmlDom(
				@"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div><div data-collection='user' lang='en'>john</div></body></html>");
			var data = new BookData(dom, CreateCollection(), null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='user']");
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-collection]");
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_DoesNotExist_MakesOne()
		{
			var dom = new HtmlDom(@"<html><head></head><body><div data-book='someVariable'>world</div></body></html>");
			var data = new BookData(dom, CreateCollection(), null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']",
					1); //NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath(
					"//div[@id='bloomDataDiv']/div[@data-book='someVariable' and text()='world']", 1);
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_NewXmatterPageAttributeSet_AddedToDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body></body></html>");

			var e = dom.RawDom.CreateElement("div");
			e.SetAttribute("data-xmatter-page", "frontCover");
			e.SetAttribute("data-someattribute", "someValue");
			e.InnerText = "anything";
			dom.RawDom.SelectSingleNode("//body").AppendChild(e);

			var data = new BookData(dom, CreateCollection(), null);
			data.UpdateVariablesAndDataDivThroughDOM();

			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']",
					1); //NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' and @data-someattribute='someValue' and not(text())]",
				1);
		}

		//[Test] TODO - this test doesn't work because UpdateVariablesAndDataDivThroughDOM first updates the page from the data div before updating the data div from the page.
		// I couldn't get it worked out, but the production code does do things correctly. I attempted to add this test for BL-5409.
		public void UpdateVariablesAndDataDivThroughDOM_UpdatedXmatterPageAttributeSet_UpdatedInDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'><div data-xmatter-page='frontCover' data-someattribute='someValue'></div></div>
				<div class='bloom-page' data-xmatter-page='frontCover' data-someattribute='someValue'>anything</div>
				</body></html>");

			var e = (XmlElement) dom.RawDom.SelectSingleNode(
				"//body/div[@class='bloom-page' and @data-xmatter-page='frontCover']");
			e.SetAttribute("data-someattribute", "someOtherValue");

			var data = new BookData(dom, CreateCollection(), null);
			data.UpdateVariablesAndDataDivThroughDOM();

			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']",
					1); //NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' and @data-someattribute='someOtherValue' and not(text())]",
				1);
		}

		//[Test] TODO - this test doesn't work because UpdateVariablesAndDataDivThroughDOM first updates the page from the data div before updating the data div from the page.
		// I couldn't get it worked out, but the production code does do things correctly. I attempted to add this test for BL-5409.
		public void UpdateVariablesAndDataDivThroughDOM_RemovedXmatterPageAttribute_RemovedFromDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'><div data-xmatter-page='frontCover' data-someattribute='someValue'></div></div>
				<div class='bloom-page' data-xmatter-page='frontCover' data-someattribute='someValue'>anything</div>
				</body></html>");

			var e = (XmlElement) dom.RawDom.SelectSingleNode(
				"//body/div[@class='bloom-page' and @data-xmatter-page='frontCover']");
			e.RemoveAttribute("data-someattribute");

			var data = new BookData(dom, CreateCollection(), null);
			data.UpdateVariablesAndDataDivThroughDOM();

			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']",
					1); //NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' and not(@data-someattribute) and not(text())]",
				1);
		}


		[Test]
		public void SetMultilingualContentLanguages_HasTrilingualLanguages_AddsToDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body></body></html>");
			var data = new BookData(dom, CreateCollection(), null);
			data.SetMultilingualContentLanguages("okm", "kbt");
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath(
					"//div[@id='bloomDataDiv']/div[@data-book='contentLanguage2' and text()='okm']", 1);
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath(
					"//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3' and text()='kbt']", 1);
		}

		[Test]
		public void SetMultilingualContentLanguages_ThirdContentLangTurnedOff_RemovedFromDataDiv()
		{
			var dom = new HtmlDom(
				@"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage2'>xyz</div><div data-book='contentLanguage3'>kbt</div></div></head><body></body></html>");
			var data = new BookData(dom, CreateCollection(), null);
			data.SetMultilingualContentLanguages(null, null);
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3']", 0);
		}


		[TestCase("", "", "", null)]
		[TestCase("the country", "", "", "the country")]
		[TestCase("the country", "the province", "", "the province, the country")]
		[TestCase("the country", "the province", "the district", "the district, the province, the country")]
		[TestCase("", "the province", "the district", "the district, the province")]
		[TestCase("", "", "the district", "the district")]
		[TestCase("", "the province", "", "the province")]
		public void Constructor_CollectionSettingsHasVariousLocationFields_LanguageLocationFilledCorrect(string country,
			string province, string district, string expected)
		{
			var dom = new HtmlDom();
			var data = new BookData(dom,
				new CollectionSettings() {Country = country, Province = province, District = district}, null);
			Assert.AreEqual(expected, data.GetVariableOrNull("languageLocation", "*"));
		}

		/*    data.AddLanguageString("*", "nameOfLanguage", collectionSettings.Language1.Name, true);
				data.AddLanguageString("*", "nameOfNationalLanguage1",
									   collectionSettings.Language2.GetNameInLanguage(collectionSettings.Language2Iso639Code), true);
				data.AddLanguageString("*", "nameOfNationalLanguage2",
									   collectionSettings.Language3.GetNameInLanguage(collectionSettings.Language2Iso639Code), true);
				data.AddGenericLanguageString("iso639Code", collectionSettings.Language1Iso639Code, true);*/

		[Test]
		public void Constructor_CollectionSettingsHasISO639Code_iso639CodeFilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, CreateCollection(Language1Iso639Code: "xyz"), null);
			Assert.AreEqual("xyz", data.GetVariableOrNull("iso639Code", "*"));
		}

		[Test]
		public void Constructor_CollectionSettingsHasISO639Code_DataSetContainsProperV()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, CreateCollection(Language1Iso639Code: "xyz"), null);
			Assert.AreEqual("xyz", data.GetWritingSystemCodes()["V"]);
		}

		[Test]
		public void Constructor_CollectionSettingsHasLanguage1Name_LanguagenameOfNationalLanguage1FilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, CreateCollection(Language1Name: "foobar"), null);
			Assert.AreEqual("foobar", data.GetVariableOrNull("nameOfLanguage", "*"));
		}

		//NB: yes, this is confusing, having lang1 = language, lang2 = nationalLang1, lang3 = nationalLang2

		[Test]
		public void Constructor_CollectionSettingsHasLanguage2Iso639Code_nameOfNationalLanguage1FilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, CreateCollection(Language2Iso639Code: "tpi"), null);
			Assert.AreEqual("Tok Pisin", data.GetVariableOrNull("nameOfNationalLanguage1", "*"));
		}

		[Test]
		public void Constructor_CollectionSettingsHasLanguage3Iso639Code_nameOfNationalLanguage2FilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, CreateCollection(Language3Iso639Code: "tpi"), null);
			Assert.AreEqual("Tok Pisin", data.GetVariableOrNull("nameOfNationalLanguage2", "*"));
		}

		[Test]
		public void Set_DidNotHaveForm_Added()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			Assert.AreEqual("one", data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 1);
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			var t = roundTripData.GetVariableOrNull("1", "en");
			Assert.AreEqual("one", t);
		}

		[Test]
		public void Set_AddTwoForms_BothAdded()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			data.Set("1", "uno", "es");
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			Assert.AreEqual("one", roundTripData.GetVariableOrNull("1", "en"));
			Assert.AreEqual("uno", roundTripData.GetVariableOrNull("1", "es"));
		}

		[Test]
		public void Set_DidHaveForm_StillJustOneCopy()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			data.Set("1", "one", "en");
			Assert.AreEqual("one", data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 1);
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			var t = roundTripData.GetVariableOrNull("1", "en");
			Assert.AreEqual("one", t);
		}

		[Test]
		public void Set_EmptyString_Removes()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			data.Set("1", "", "en");
			Assert.AreEqual(null, data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void Set_Null_Removes()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			data.Set("1", null, "en");
			Assert.AreEqual(null, data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void RemoveSingleForm_HasForm_Removed()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			var data2 = new BookData(htmlDom, CreateCollection(), null);
			data2.RemoveSingleForm("1", "en");
			Assert.IsNull(data2.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void RemoveDataDivVariableForOneLanguage_DoesNotHaveForm_OK()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.RemoveSingleForm("1", "en");
			Assert.AreEqual(null, data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void RemoveDataDivVariableForOneLanguage_WasLastForm_WholeElementRemoved()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			roundTripData.RemoveSingleForm("1", "en");
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));

		}


		[Test]
		public void RemoveDataDivVariableForOneLanguage_WasTwoForms_OtherRemains()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			data.Set("1", "uno", "es");
			var roundTripData = new BookData(htmlDom, CreateCollection(), null);
			roundTripData.RemoveSingleForm("1", "en");
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
			Assert.AreEqual("uno", roundTripData.GetVariableOrNull("1", "es"));
		}


		[Test]
		public void Set_CalledTwiceWithDIfferentLangs_HasBoth()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			data.Set("1", "uno", "es");
			Assert.AreEqual(2, data.GetMultiTextVariableOrEmpty("1").Forms.Count());
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_VariableIsNull_DataDivForItRemoved()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, CreateCollection(), null);
			data.Set("1", "one", "en");
			data.Set("1", null, "es");
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@lang='en']", 1);
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@lang='es']", 0);
		}

		[Test]
		public void PrettyPrintLanguage_DoesNotModifyUnknownCodes()
		{
			var htmlDom = new HtmlDom();
			var settingsettings =
				CreateCollection(Language1Iso639Code: "pdc", Language1Name: "German, Kludged");
			var data = new BookData(htmlDom, settingsettings, null);
			Assert.That(data.PrettyPrintLanguage("xyz"), Is.EqualTo("xyz"));
		}

		[Test]
		public void PrettyPrintLanguage_AdjustsLang1()
		{
			var htmlDom = new HtmlDom();
			var settingsettings =
				CreateCollection(Language1Iso639Code: "pdc", Language1Name: "German, Kludged");
			var data = new BookData(htmlDom, settingsettings, null);
			Assert.That(data.PrettyPrintLanguage("pdc"), Is.EqualTo("German, Kludged"));
		}

		[Test]
		public void PrettyPrintLanguage_AdjustsKnownLanguages_German()
		{
			var htmlDom = new HtmlDom();
			var settings = CreateCollection(
				Language1Name: "German, Kludged",
				Language1Iso639Code: "pdc",
				Language2Iso639Code: "de",
				Language3Iso639Code: "fr"
			);
			var data = new BookData(htmlDom, settings, null);
			Assert.That(data.PrettyPrintLanguage("de"), Is.EqualTo("Deutsch"));
			Assert.That(data.PrettyPrintLanguage("fr"), Is.EqualTo("français"));
			Assert.That(data.PrettyPrintLanguage("en"), Is.EqualTo("English"));
			Assert.That(data.PrettyPrintLanguage("es"), Is.EqualTo("español"));
		}

		[Test]
		public void PrettyPrintLanguage_AdjustsKnownLanguages_English()
		{
			var htmlDom = new HtmlDom();
			var settings = CreateCollection(
				Language1Iso639Code: "pdc",
				Language1Name: "German, Kludged",
				Language2Iso639Code: "en",
				Language3Iso639Code: "fr"
			);
			var data = new BookData(htmlDom, settings, null);
			Assert.That(data.PrettyPrintLanguage("de"), Is.EqualTo("German"));
			Assert.That(data.PrettyPrintLanguage("fr"), Is.EqualTo("French"));
			Assert.That(data.PrettyPrintLanguage("en"), Is.EqualTo("English"));
			Assert.That(data.PrettyPrintLanguage("es"), Is.EqualTo("Spanish"));
		}

		[Test]
		public void MigrateData_TopicInTokPisinButNotEnglish_ChangesLangeToEnglish()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='tpi'>health</div>
				</div>
			 </body></html>");

			var data = new BookData(bookDom, _collectionSettings, null);
			Assert.AreEqual("health", data.GetVariableOrNull("topic", "en"));
			Assert.IsNull(data.GetVariableOrNull("topic", "tpi"));
		}

		[Test]
		public void SynchronizeDataItemsThroughoutDOM_VariousTopicScenarios()
		{
			TestTopicHandling("Health", "fr", "Santé", "fr", "en", null, "Should use lang1");
			TestTopicHandling("Health", "fr", "Santé", "x", "fr", null, "Should use lang2");
			TestTopicHandling("Health", "fr", "Santé", "x", "y", "fr", "Should use lang3");
			TestTopicHandling("Health", "en", "Health", "x", "y", "z", "Should use English");
			TestTopicHandling("Health", "en", "Health", "en", "fr", "es", "Should use lang1");
			TestTopicHandling("NoTopic", "", "", "en", "fr", "es", "'No Topic' should give no @lang and no text");
			TestTopicHandling("Bogus", "en", "Bogus", "z", "fr", "es",
				"Unrecognized topic should give topic in English");
		}

		private void TestTopicHandling(string topicKey, string expectedLanguage, string expectedTranslation,
			string lang1, string lang2, string lang3, string description)
		{
			_collectionSettings.Language1.Iso639Code = lang1;
			_collectionSettings.Language2.Iso639Code = lang2;
			_collectionSettings.Language3.Iso639Code = lang3;

			var bookDom = new HtmlDom(@"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'>" + topicKey + @"</div>
				</div>
				<div id='somePage'>
                    <div id='test' data-derived='topic'>
					</div>
                </div>
			 </body></html>");
			var data = new BookData(bookDom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			try
			{
				if (string.IsNullOrEmpty(expectedLanguage))
				{
					AssertThatXmlIn.Dom(bookDom.RawDom)
						.HasSpecifiedNumberOfMatchesForXpath(
							"//div[@id='test' and @data-derived='topic' and not(@lang) and text()='" +
							expectedTranslation + "']", 1);
				}
				else
				{
					AssertThatXmlIn.Dom(bookDom.RawDom)
						.HasSpecifiedNumberOfMatchesForXpath(
							"//div[@id='test' and @data-derived='topic' and @lang='" + expectedLanguage +
							"' and text()='" +
							expectedTranslation + "']", 1);
				}
			}
			catch (Exception)
			{
				Assert.Fail(description);
			}

		}

		/// <summary>
		/// we use English as the one and only "key" language for topics in the datadiv
		/// </summary>
		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasMultipleTopicItems_RemovesAllButEnglish()
		{
			var bookDom = new HtmlDom(@"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'>Health</div>
						<div data-book='topic' lang='fr'>Santé</div>
				</div>
			 </body></html>");
			var data = new BookData(bookDom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			AssertThatXmlIn.Dom(bookDom.RawDom)
				.HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='topic' and @lang='fr']");
		}

		[Test]
		public void SynchronizeDataItemsThroughoutDOM_TopicHasParagraphElement_Removed()
		{
			var bookDom = new HtmlDom(@"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'><p>Health</p></div>
				</div>
			 </body></html>");
			var data = new BookData(bookDom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			AssertThatXmlIn.Dom(bookDom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@data-derived='topic' and @lang='en']/p", 0);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(
				"//div[@id='bloomDataDiv']/div[@data-book='topic' and @lang='en' and text()='Health']", 1);
		}

		private bool AndikaNewBasicIsInstalled()
		{
			const string fontToCheck = "andika new basic";
			return FontFamily.Families.FirstOrDefault(f => f.Name.ToLowerInvariant() == fontToCheck) != null;
		}

		[Test]
		[Category("SkipOnTeamCity")]
		public void AndikaNewBasic_MustBeInstalled()
		{
			Assert.That(AndikaNewBasicIsInstalled());
		}

		[Test]
		public void OneTimeCheckVersionNumber_AndikaNewBasicMigration_alreadyDone()
		{
			var filepath = _collectionSettings.SettingsFilePath;
			WriteSettingsFile(filepath, _postAndikaMigrationCollection);

			// SUT
			_collectionSettings.Load();

			// Verify
			var font1 = _collectionSettings.Language1.FontName;
			var oneTimeCheckVersion = _collectionSettings.OneTimeCheckVersionNumber;
			Assert.That(Convert.ToInt32(oneTimeCheckVersion).Equals(1));
			Assert.That(font1.Equals("Andika New Basic"));
		}

		[Test]
		public void OneTimeCheckVersionNumber_AndikaNewBasicMigration_doneUserReverted()
		{
			var filepath = _collectionSettings.SettingsFilePath;
			WriteSettingsFile(filepath, _postAndikaMigrationCollectionNoANB);

			// SUT
			_collectionSettings.Load();

			// Verify
			var font1 = _collectionSettings.Language1.FontName;
			var oneTimeCheckVersion = _collectionSettings.OneTimeCheckVersionNumber;
			Assert.That(Convert.ToInt32(oneTimeCheckVersion).Equals(1));
			Assert.That(font1.Equals("Andika"));
		}

		private void WriteSettingsFile(string filepath, string xmlString)
		{
			File.WriteAllText(filepath, xmlString);
		}

		#region Collection Settings test data

		private const string _preAndikaMigrationCollection = @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika</DefaultLanguage3FontName>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<IsSourceCollection>False</IsSourceCollection>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

		private const string _postAndikaMigrationCollection = @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika New Basic</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika New Basic</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika New Basic</DefaultLanguage3FontName>
				<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<IsSourceCollection>False</IsSourceCollection>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

		private const string _postAndikaMigrationCollectionNoANB = @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika</DefaultLanguage3FontName>
				<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<IsSourceCollection>False</IsSourceCollection>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

		#endregion

		private static string GetXpathForContributionsInLang(string lang)
		{
			// The old xpath used in most of these tests was getting datadiv results, not bloom-page results,
			// but the messages on most Asserts made it clear it was supposed to be testing the bloom-page contents.
			// This xpath tests the bloom-page contents, not the datadiv
			return "//div[@id='originalContributions']/div[@data-book='originalContributions' and @lang='" + lang +
			       "']";
		}

		[Test]
		public void
			SynchronizeDataItemsThroughoutDOM_HasOnlyEnglishContributorsButEnglishIsLang3_CopiesEnglishIntoNationalLanguageSlot()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='fr'></div>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='en'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection(

				Language1Iso639Code: "etr",
				Language2Iso639Code: "fr"
			);
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var englishContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("en"));
			Assert.AreEqual("the contributions", englishContributions.InnerText,
				"Should copy English into body of course, as normal");
			var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("fr"));
			Assert.AreEqual("the contributions", frenchContributions.InnerText,
				"Should copy English into French Contributions becuase it's better than just showing nothing");
		}



		[Test]
		public void
			SynchronizeDataItemsThroughoutDOM_HasOnlyEnglishContributorsInDataDivButFrenchInBody_DoesNotCopyEnglishIntoFrenchSlot()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div data-book='originalContributions' lang='fr'>les contributeurs</div>
						<div data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection(
				Language1Iso639Code: "etr",
				Language2Iso639Code: "fr"
			);
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("fr"));
			Assert.AreEqual("les contributeurs", frenchContributions.InnerText,
				"Should not touch existing French Contributions");
		}

		[Test]
		public void
			SynchronizeDataItemsThroughoutDOM_HasFrenchAndEnglishContributorsInDataDiv_DoesNotCopyEnglishIntoFrenchSlot()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
					<div data-book='originalContributions' lang='fr'>les contributeurs</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div data-book='originalContributions' lang='fr'></div>
						<div data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection(

				Language1Iso639Code: "xyz",
				Language2Iso639Code: "fr"
			);
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("fr"));
			Assert.AreEqual("les contributeurs", frenchContributions.InnerText,
				"Should use the French, not the English even though the French in the body was empty");
			var vernacularContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("xyz"));
			Assert.AreEqual("", vernacularContributions.InnerText,
				"Should not copy Edolo into Vernacular Contributions. Only national language fields get this treatment");
		}

		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasOnlyEdoloContributors_CopiesItIntoL2ButNotL1()
		{
			// empty french datadiv element, but has self-closing paragraph tag
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='etr'><p>the contributions</p></div>
					 <div data-book='originalContributions' lang='fr'><p /></div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='fr'></div>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection(
				Language1Iso639Code: "xyz",
				Language2Iso639Code: "fr"
			);
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("fr"));
			Assert.AreEqual("the contributions", frenchContributions.InnerText,
				"Should copy Edolo into French Contributions because it's better than just showing nothing");
			var vernacularContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("xyz"));
			Assert.AreEqual("", vernacularContributions.InnerText,
				"Should not copy Edolo into Vernacular Contributions. Only national language fields get this treatment");
		}

		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasOnlyFrenchContributors_CopiesItIntoEnglishIfNoEnglish()
		{
			// empty english datadiv element, but has empty paragraph tag
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='fr'><p>les contributeurs</p></div>
					 <div data-book='originalContributions' lang='en'><p></p></div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='en'><p></p></div>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = CreateCollection(

				Language1Iso639Code: "xyz",
				Language2Iso639Code: "en"
			);
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var englishContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("en"));
			Assert.AreEqual("les contributeurs", englishContributions.InnerText,
				"Should copy French into English Contributions because it's better than just showing nothing");
			var vernacularContributions = dom.SelectSingleNodeHonoringDefaultNS(GetXpathForContributionsInLang("xyz"));
			Assert.AreEqual("", vernacularContributions.InnerText,
				"Should not copy Edolo into Vernacular Contributions. Only national language fields get this treatment");
		}

		[TestCase("", ExpectedResult = true)]
		[TestCase("  \t  ", ExpectedResult = true)]
		[TestCase("<p></p>", ExpectedResult = true)]
		[TestCase("Bob", ExpectedResult = false)]
		[TestCase("<p>Bob</p>", ExpectedResult = false)]
		[TestCase("<p/><p />", ExpectedResult = true)]
		[TestCase("<br></br>", ExpectedResult = true)]
		[TestCase("<br/>Bob", ExpectedResult = false)]
		[TestCase("<br />", ExpectedResult = true)]
		[TestCase("  <p> </p>  ", ExpectedResult = true)]
		//[TestCase("\xFEFF", ExpectedResult = true)] // non-breaking zero-width Unicode character
		public bool StringAlternativeHasNoText_VariousCases(string input)
		{
			return BookData.StringAlternativeHasNoText(input);
		}

		private static string NewLines
		{
			get { return Environment.NewLine + "  " + Environment.NewLine + "  " + Environment.NewLine; }
		}

		// A separate test was required, since I wanted to use Environment.NewLine and the
		// TestCase attribute requires a constant string.
		[Test]
		public void StringAlternativeHasNoText_NewLines()
		{
			Assert.IsTrue(BookData.StringAlternativeHasNoText(NewLines));
		}

		/// <summary>
		/// BL-3078 where when xmatter was injected and updated, the text stored in data-book overwrote
		/// the innerxml of the div, knocking out the <label></label> in there, which lead to losing
		/// the side bubbles explaining what the field was for.
		/// </summary>
		[Test]
		public void SynchronizeDataItemsThroughoutDOM_EditableHasLabelElement_LabelPreserved()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='insideBackCover' lang='en'><p/></div>
				</div>
				<div class='bloom-page'>
					 <div id='foo' class='bloom-content1 bloom-editable' data-book='insideBackCover' lang='en'>
						<label>some label</label>
					</div>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var foo = (XmlElement) dom.SelectSingleNodeHonoringDefaultNS("//*[@id='foo']");
			Assert.That(foo.InnerXml, Contains.Substring("<label>some label</label>"));
		}

		[Test]
		public void GatherDataItemsFromXElement_OmitsDataPageNumber()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-xmatter-page='insideBackCover' data-page='required singleton' data-export='back-matter-inside-back-cover' data-page-number='3'></div>
 					<div data-xmatter-page='outsideBackCover' data-page='required singleton' data-export='back-matter-back-cover' data-page-number=''></div>
				</div>
				<div class='bloom-page'>
					 <div id='foo' class='bloom-content1 bloom-editable' data-book='insideBackCover' lang='en'>
						<label>some label</label>
					</div>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);

			var pageNumber = data.GetXmatterPageDataAttributeValue("insideBackCover", "data-page-number");
			var dataPage = data.GetXmatterPageDataAttributeValue("insideBackCover", "data-page");
			Assert.That(pageNumber, Is.EqualTo(""));
			Assert.That(dataPage, Is.EqualTo("required singleton"));
			pageNumber = data.GetXmatterPageDataAttributeValue("outsideBackCover", "data-page-number");
			dataPage = data.GetXmatterPageDataAttributeValue("outsideBackCover", "data-page");
			Assert.That(pageNumber, Is.EqualTo(""));
			Assert.That(dataPage, Is.EqualTo("required singleton"));
		}


		public static CollectionSettings CreateCollection(string Language1Iso639Code = null,
			string Language1Name = null,
			string Language2Iso639Code = null,
			string Language2Name = null,
			string Language3Iso639Code = null,
			string Language3Name = null
		)
		{
			var c = new CollectionSettings();
			if (Language1Iso639Code != null)
			{
				c.Language1.Iso639Code = Language1Iso639Code;
			}

			if (Language1Name != null)
			{
				c.Language1.Name = Language1Name;
			}

			if (Language2Iso639Code != null)
			{
				c.Language2.Iso639Code = Language2Iso639Code;
			}

			if (Language2Name != null)
			{
				c.Language2.Name = Language2Name;
			}

			if (Language3Iso639Code != null)
			{
				c.Language3.Iso639Code = Language3Iso639Code;
			}

			if (Language3Name != null)
			{
				c.Language3.Name = Language3Name;
			}

			return c;
		}
	}
}
