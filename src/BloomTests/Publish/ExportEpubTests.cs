using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish;
using Bloom.Publish.Epub;
using BloomTemp;
using BloomTests.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.Extensions;
using SIL.PlatformUtilities;

namespace BloomTests.Publish
{
	[TestFixture]
	public class ExportEpubTests : ExportEpubTestsBaseClass
	{
		public override void Setup()
		{
			base.Setup();
			_ns = ExportEpubTestsBaseClass.GetNamespaceManager();
			_bookServer = CreateBookServer();
		}

		[Test]
		public void HandlesWhiteSpaceInImageNames()
		{
#if __MonoCS__
			// This tests only the whitespace difference on Linux because filenames differing by case are different!
			var book = SetupBook("This is some text", "en", "my_Image", "my%20Image");
			MakeImageFiles(book, "my_Image", "my Image");
#else
			// These two names (my_Image and "my image") are especially interesting because they differ by case and also white space.
			// The case difference is not important to the Windows file system.
			// The white space must be removed to make an XML ID.
			var book = SetupBook("This is some text", "en", "my_Image", "my%20image");
			MakeImageFiles(book, "my_Image", "my image");
#endif
			MakeEpub("output", "HandlesWhiteSpaceInImageNames", book);
#if __MonoCS__
			CheckBasicsInManifest("my_Image", "my_Image1");
			CheckBasicsInPage("my_Image", "my_Image1");
#else
			CheckBasicsInManifest("my_Image", "my_image1");
			CheckBasicsInPage("my_Image", "my_image1");
#endif
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();
		}

		[Test]
		public void HandlesNonRomanFileNames()
		{
			// This mysterious string is the filename that will be actually used in the ePUB.
			// It comes from UrlEncoding the original name and then replacing % with _ so the reader
			// doesn't have to be smart about decoding the href.
			string outputImageName = "_e0_b8_9b_e0_b8_b9_e0_b8_81_e0_b8_b1_e0_b8_9a_e0_b8_a1_e0_b8_94";
			var book = SetupBook("This is some text", "en", "ปูกับมด", "my%20image");
			MakeImageFiles(book, "ปูกับมด", "my image");
			MakeEpub("ปูกับมด", "HandlesNonRomanFileNames", book);
			CheckBasicsInManifest(outputImageName, "my_image");
			CheckBasicsInPage(outputImageName, "my_image");
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();
		}

		Bloom.Book.Book SetupBook(string text, string lang, params string[] images)
		{
			return SetupBookLong(text, lang, images: images);
		}

		[Test]
		public void ImageDescriptions_ImageDescriptionPublishingNone_AreRemoved()
		{
			var book = SetupBookLong("This is a simple page", "xyz", images: new[] {"image1"},
				imageDescriptions: new[] {"This describes image 1"});
			MakeEpub("output", "ImageDescriptions_PublishInPageNone_AreRemoved", book);
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//xhtml:div[contains(@class,'bloom-imageDescription')]", _ns);
		}

		[Test]
		public void ImageDescriptions_ImageDescriptionPublishingOnPage_ConvertedToAsides()
		{
			var book = SetupBookLong("This is a simple page", "xyz", images: new[] { "image1" },
				imageDescriptions: new[] { "This describes image 1" });
			MakeEpub("output", "ImageDescriptions_PublishInPageOnPage_ConvertedToAsides", book, EpubMaker.ImageDescriptionPublishing.OnPage);
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//xhtml:div[contains(@class,'bloom-imageDescription')]", _ns);
			AssertThatXmlIn.String(_page1Data).HasSpecifiedNumberOfMatchesForXpath("//xhtml:div[@class='marginBox']/xhtml:aside[.='This describes image 1']", _ns, 1);
		}

		private string CheckNavPage()
		{
			XNamespace opf = "http://www.idpf.org/2007/opf";

			var navPage = _manifestDoc.Root.Element(opf + "manifest").Elements(opf + "item").Last().Attribute("href").Value;
			var navPageData = StripXmlHeader(ExportEpubTestsBaseClass.GetZipContent(_epub, Path.GetDirectoryName(_manifestFile) + "/" + navPage));
			AssertThatXmlIn.String(navPageData)
				.HasAtLeastOneMatchForXpath(
					"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='1.xhtml']", _ns);
			return navPageData;	// in case the test wants more extensive checking
		}

		private void CheckFontStylesheet()
		{
			var fontCssData = ExportEpubTestsBaseClass.GetZipContent(_epub, "content/fonts.css");
			Assert.That(fontCssData,
				Is.StringContaining(
					"@font-face {font-family:'Andika New Basic'; font-weight:normal; font-style:normal; src:url(AndikaNewBasic-R.ttf) format('opentype');}"));
			// Currently we're not embedding bold and italic fonts (BL-4202)
			//Assert.That(fontCssData,
			//	Is.StringContaining(
			//		"@font-face {font-family:'Andika New Basic'; font-weight:bold; font-style:normal; src:url(AndikaNewBasic-B.ttf) format('opentype');}"));
			//Assert.That(fontCssData,
			//	Is.StringContaining(
			//		"@font-face {font-family:'Andika New Basic'; font-weight:normal; font-style:italic; src:url(AndikaNewBasic-I.ttf) format('opentype');}"));
			//Assert.That(fontCssData,
			//	Is.StringContaining(
			//		"@font-face {font-family:'Andika New Basic'; font-weight:bold; font-style:italic; src:url(AndikaNewBasic-BI.ttf) format('opentype');}"));
		}

		private void CheckPageBreakMarker(string pageData, string pageId="pg1", string pageLabel="1")
		{
			AssertThatXmlIn.String(pageData).HasSpecifiedNumberOfMatchesForXpath("//div/span[@role='doc-pagebreak' and @id='"+pageId+"' and @aria-label='"+pageLabel+"']", 1);
		}

		private void CheckEpubTypeAttributes(string currentPage, string pageType, params string[] otherEpubTypeValues)
		{
			if (String.IsNullOrEmpty(pageType))
				AssertThatXmlIn.String(currentPage).HasSpecifiedNumberOfMatchesForXpath("//xhtml:body/xhtml:section", _ns, 0);
			else
				AssertThatXmlIn.String(currentPage).HasSpecifiedNumberOfMatchesForXpath("//xhtml:body/xhtml:section[@epub:type='"+pageType+"']", _ns, 1);

			AssertThatXmlIn.String(currentPage).HasSpecifiedNumberOfMatchesForXpath("//xhtml:div[@epub:type]", _ns, otherEpubTypeValues.Count());
			foreach (var val in otherEpubTypeValues)
				AssertThatXmlIn.String(currentPage).HasSpecifiedNumberOfMatchesForXpath("//xhtml:div[@epub:type='"+val+"']", _ns, 1);
		}

		/// <summary>
		/// Make an ePUB out of the specified book. Sets up several instance variables with commonly useful parts of the results.
		/// </summary>
		/// <param name="mainFileName"></param>
		/// <param name="folderName"></param>
		/// <param name="book"></param>
		/// <returns></returns>
		protected override ZipFile MakeEpub(string mainFileName, string folderName, Bloom.Book.Book book,
			EpubMaker.ImageDescriptionPublishing howToPublishImageDescriptions = EpubMaker.ImageDescriptionPublishing.None,
			Action<EpubMaker> extraInit = null)
		{
			var result = base.MakeEpub(mainFileName, folderName, book, howToPublishImageDescriptions, extraInit);
			GetPageOneData();
			return result;
		}

		[Test]
		public void Missing_Audio_Ignored()
		{
			// Similar input as the basic SaveAudio, (also verifies that IDs are really adjusted), but this time we don't create one of the expected audio files.
			var book = SetupBook("<p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2c'>This is some text.</span><span id='i0d8e9910-dfa3-4376-9373-a869e109b763'>Another sentence</span></p>",
				"xyz", "1my$Image", "my%20image");
			MakeImageFiles(book, "my image");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp4"));
			// But don't make a fake audio file for the second span
			MakeEpub("output", "Missing_Audio_Ignored", book);
			CheckBasicsInManifest("my_image");
			CheckBasicsInPage("my_image");
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();

			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil"));
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");

			var smilData = StripXmlHeader(ExportEpubTestsBaseClass.GetZipContent(_epub, "content/1_overlay.smil"));
			var mgr = new XmlNamespaceManager(new NameTable());
			mgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
			mgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");
			var assertSmil = AssertThatXmlIn.String(smilData);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3']", mgr);

			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4");
		}

		[Test]
		public void BookSwitchedToDeviceXMatter()
		{
			var book = SetupBookLong("This is some text", "en", createPhysicalFile: true);
			MakeEpub("output", "BookSwitchedToDeviceXMatter", book);
			// This is a rather crude way to test that it is switched to device XMatter, but we aren't even sure we
			// really want to do that yet. This at least gets something in there which should fail if we somehow
			// lose that fix, although currently the failure that happens if I take out the conversion is not
			// a simple failure of this assert...something goes wrong before that making a real physical file
			// for the unit test.
			var currentPage = String.Empty;
			int pageCount = 0;
			for (int i = 1; ; ++i)
			{
				var entryPath = "content/" + i + ".xhtml";
				var entry = _epub.GetEntry(entryPath);
				if (entry == null)
					break;
				++pageCount;
				currentPage = ExportEpubTestsBaseClass.GetZipContent(_epub, entryPath);
				switch (i)
				{
				case 1:
					CheckPageBreakMarker(currentPage, "pgFrontCover", "Front Cover");
					break;
				case 2:
					CheckPageBreakMarker(currentPage, "pg1", "1");
					CheckEpubTypeAttributes(currentPage, null);
					break;
				case 3:
					CheckPageBreakMarker(currentPage, "pgTitlePage", "Title Page");
					CheckEpubTypeAttributes(currentPage, null, "titlepage");
					break;
				case 4:
					CheckPageBreakMarker(currentPage, "pgCreditsPage", "Credits Page");
					break;
				case 5:
					CheckPageBreakMarker(currentPage, "pgTheEnd", "The End");
					CheckEpubTypeAttributes(currentPage, null);
					break;
				default:
					// We should never get here!
					Assert.IsTrue(i > 0 && i < 6, "unexpected page number {0} should be between 1 and 5 inclusive", i);
					break;
				}
			}
			// device xmatter currently has 1 front and a default of 3 back pages, so we should have exactly five pages.
			Assert.AreEqual(5, pageCount);
			AssertThatXmlIn.String(currentPage).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'theEndPage')]", 1);
			var navPageData = CheckNavPage();
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='2.xhtml']", _ns, 1);
			AssertThatXmlIn.String(navPageData).HasNoMatchForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='3.xhtml']", _ns);
			AssertThatXmlIn.String(navPageData).HasNoMatchForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='4.xhtml']", _ns);
			AssertThatXmlIn.String(navPageData).HasNoMatchForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='5.xhtml']", _ns);
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='1.xhtml#pgFrontCover']", _ns, 1);
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='2.xhtml#pg1']", _ns, 1);
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='3.xhtml#pgTitlePage']", _ns, 1);
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='4.xhtml#pgCreditsPage']", _ns, 1);
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='5.xhtml#pgTheEnd']", _ns, 1);
		}

		[Test]
		public void Missing_Audio_CreatedFromWav()
		{
			// Similar input as the basic Missing_Audio_Ignored, (also verifies that IDs are really adjusted), but this time we don't create one of the expected audio files.
			var book = SetupBook("<p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2c'>This is some text.</span><span id='i0d8e9910-dfa3-4376-9373-a869e109b763'>Another sentence</span></p>",
				"xyz", "1my$Image", "my%20image");
			MakeImageFiles(book, "my image");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3"));
			// But don't make a fake audio file for the second span
			MakeEpub("output", "Missing_Audio_CreatedFromWav", book);
			CheckBasicsInManifest("my_image");
			CheckAccessibilityInManifest(true, true, _defaultSourceValue);	// both sound and image files
			CheckBasicsInPage("my_image");
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();

			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil"));
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");

			var smilData = StripXmlHeader(ExportEpubTestsBaseClass.GetZipContent(_epub, "content/1_overlay.smil"));
			var mgr = new XmlNamespaceManager(new NameTable());
			mgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
			mgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");
			var assertSmil = AssertThatXmlIn.String(smilData);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp3']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3']", mgr);

			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp3");
		}

		/// <summary>
		/// Motivated by "El Nino" from bloom library, which (to defeat caching?) has such a query param in one of its src attrs.
		/// </summary>
		[Test]
		public void ImageSrcQuery_IsIgnored()
		{
			var book = SetupBook("This is some text",
				"en", "myImage.png?1023456");
			MakeImageFiles(book, "myImage");
			MakeEpub("output", "ImageSrcQuery_IsIgnored", book);
			CheckBasicsInManifest("myImage");
			CheckAccessibilityInManifest(false, true, _defaultSourceValue);		// no sound files, but a nontrivial image file
			CheckBasicsInPage("myImage");
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
		}

		[Test]
		public void HandlesMultiplePages()
		{
			var book = SetupBookLong("This is some text",
				"en", extraPages: @"<div class='bloom-page numberedPage' data-page-number='2'>
						<div id='anotherId' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
									Page two text
								</div>
								<div lang = '*'>more text</div>
							</div>
						</div>
					</div>");
			MakeEpub("output", "HandlesMultiplePages", book);
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(false, false, _defaultSourceValue);	// neither sound nor image files
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);

			var page2Data = ExportEpubTestsBaseClass.GetZipContent(_epub, Path.GetDirectoryName(_manifestFile) + "/" + "2.xhtml");
			AssertThatXmlIn.String(page2Data).HasAtLeastOneMatchForXpath("//xhtml:div[@id='anotherId']", _ns);
			CheckPageBreakMarker(page2Data, "pg2", "2");
			CheckEpubTypeAttributes(page2Data, null);
			var navPageData = CheckNavPage();
			AssertThatXmlIn.String(navPageData).HasNoMatchForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='2.xhtml']", _ns);
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='1.xhtml#pg1']", _ns, 1);
			AssertThatXmlIn.String(navPageData).HasSpecifiedNumberOfMatchesForXpath(
				"xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='2.xhtml#pg2']", _ns, 1);
		}

		[Test]
		public void OmitsNonPrintingPages()
		{
			var book = SetupBookLong("This is some text",
				"en", extraPages: @"<div class='bloom-page bloom-nonprinting'>
						<div id='anotherId' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
									Page two text
								</div>
								<div lang = '*'>more text</div>
							</div>
						</div>
					</div>");
			MakeEpub("output", "OmitsNonPrintingPages", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);

			var page2entry = _epub.GetEntry(Path.GetDirectoryName(_manifestFile) + "/" + "2.xhtml");
			Assert.That(page2entry, Is.Null, "nonprinting page should be omitted");
		}

		/// <summary>
		/// Motivated by "Look in the sky. What do you see?" from bloom library, if we can't find an image,
		/// remove the element. Also exercises some other edge cases of missing or empty src attrs for images.
		/// </summary>
		[Test]
		public void ImageMissing_IsRemoved()
		{
			var book = SetupBookLong("This is some text", "en",
				extraContentOutsideTranslationGroup: @"<div><img></img></div>
							<div><img src=''></img></div>
							<div><img src='?1023456'></img></div>",
				images:new [] {"myImage.png?1023456"});
			// Purposely do NOT create any images.
			MakeEpub("output", "ImageMissing_IsRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);

			AssertThatXmlIn.String(_manifestContent).HasNoMatchForXpath("package/manifest/item[@id='fmyImage' and @href='myImage.png']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//img");
		}

		[Test]
		public void LeftToRight_SpineDoesNotDeclareDirection()
		{
			var book = SetupBook("This is some text", "xyz");
			book.CollectionSettings.IsLanguage1Rtl = false;
			MakeEpub("output", "SpineDoesNotDeclareDirection", book);
			AssertThatXmlIn.String(_manifestContent).HasSpecifiedNumberOfMatchesForXpath("//spine[not(@page-progression-direction)]", 1);
			;
		}

		[Test]
		public void RightToLeft_SpineDeclaresRtlDirection()
		{
			var book = SetupBook("This is some text", "xyz");
			book.CollectionSettings.IsLanguage1Rtl = true;
			MakeEpub("output", "SpineDeclaresRtlDirection", book);
			AssertThatXmlIn.String(_manifestContent).HasSpecifiedNumberOfMatchesForXpath("//spine[@page-progression-direction='rtl']", 1);;
		}

		/// <summary>
		/// Motivated by "Look in the sky. What do you see?" from bloom library, if elements with class bloom-ui have
		/// somehow been left in the book, don't put them in the ePUB.
		/// </summary>
		[Test]
		public void BloomUi_IsRemoved()
		{
			var book = SetupBookLong("This is some text", "en",
				extraContent: "<div class='bloom-ui rubbish'><img src='myImage.png?1023456'></img></div>",
				images: new [] {"myImage.png?1023456"});
			MakeImageFiles(book, "myImage"); // Even though the image exists we should not use it.
			MakeEpub("output", "BloomUi_IsRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);

			AssertThatXmlIn.String(_manifestContent).HasNoMatchForXpath("package/manifest/item[@id='fmyImage' and @href='myImage.png']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//img[@src='myImage.png']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//div[@class='bloom-ui rubbish']");
		}

		[Test]
		public void StandardStyleSheets_AreRemoved()
		{
			var book = SetupBookLong("Some text", "en");
			MakeEpub("output", "StandardStyleSheets_AreRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();
			// Check that the standard stylesheet, not wanted in the ePUB, is removed.
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//xhtml:head/xhtml:link[@href='basePage.css']", _ns); // standard stylesheet should be removed.
			Assert.That(_page1Data, Is.Not.StringContaining("basePage.css")); // make sure it's stripped completely
			Assert.That(_epub.GetEntry("content/basePage.ss"),Is.Null);
		}

		/// <summary>
		/// Test that content that shouldn't show up in the ePUB gets removed.
		/// -- display: none
		/// -- pageDescription
		/// -- pageLabel
		/// -- label elements
		/// -- bubbles
		/// </summary>
		[Test]
		public void InvisibleAndUnwantedContentRemoved()
		{
			var book = SetupBookLong("Page with a picture on top and a large, centered word below.", "en",
				extraContent: @"<div class='bloom-editable' lang='xyz'><label class='bubble'>Book title in {lang} should be removed</label>vernacular text should always display</div>
					<div class='bloom-editable' lang='fr'>French text should only display if configured</div>
					<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
				extraContentOutsideTranslationGroup: @"
					<div class='split-pane'>
						<div class='split-pane-component'>
							<div class='split-pane-component-inner'>
								<div id='testSecondTextBox' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
									<div class='bloom-editable' lang='xyz'>2nd text box vernacular</div>
									<div class='bloom-editable' lang='en'>Second text box</div>
								</div>
							</div>
						</div>
						<div class='split-pane-component'>
							<div class='split-pane'>
								<div class='split-pane-component'>
									<div class='split-pane-component-inner'>
										<div id='testThirdTextBox' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
											<div class='bloom-editable' lang='xyz'>3rd text box vernacular</div>
											<div class='bloom-editable' lang='en'>Third text box</div>
										</div>
									</div>
								</div>
								<div class='split-pane-divider'></div>
								<div class='split-pane-component'>
									<div class='split-pane-component-inner'>
<!-- class 'box-header-off' here pointed out a flaw in our deletion code in InvisibleAndUnwantedContentRemoved() -->
										<div id='testFourthTextBox' class='box-header-off bloom-translationGroup bloom-requiresParagraphs' lang=''>
											<div class='bloom-editable' lang='xyz'><p></p></div>
											<div class='bloom-editable' lang='en'><p></p></div>
										</div>
										<div id='testFifthTextBox' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
											<div class='bloom-editable' lang='xyz'>5th text box vernacular</div>
											<div class='bloom-editable' lang='en'>Fifth text box</div>
										</div>
									</div>
								</div>
							</div>
						</div>
					</div>",
				defaultLanguages: "V");

			MakeEpub("output", "InvisibleAndUnwantedContentRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();

			var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
			assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='pageDescription']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='en']", _ns); // one language by default
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:script", _ns);
		}

		/// <summary>
		/// Content whose display properties resolves to display:None should be removed.
		/// This should not include National1 in XMatter.
		/// </summary>
		[Test]
		public void National1_InXMatter_IsNotRemoved()
		{
			// We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
			var book = SetupBookLong("English text (first national language) should display in title.", "en",
				extraPageClass: " bloom-frontMatter frontCover' data-page='required singleton",
				extraContent:
					@"<div class='bloom-editable' lang='xyz'><label class='bubble'>Book title in {lang} should be removed</label>vernacular text (content1) should always display</div>
							<div class='bloom-editable' lang='fr'>French text (second national language) should not display</div>
							<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
				extraEditGroupClasses: "bookTitle",
				defaultLanguages: "V,N1");

			MakeEpub("output", "National1_InXMatter_IsNotRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data, "pgFrontCover", "Front Cover");

			var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
			assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
			assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
		}

		/// <summary>
		/// The critical point here is that passing defaultLanguages:N1 makes N1 the value of the data-default-languages attribute
		/// of the translation group, which makes only N1 (English) visible. We want to verify that this results in only that
		/// language being even PRESENT in the epub.
		/// </summary>
		[Test]
		public void UserSpecifiedNoVernacular_VernacularRemoved()
		{
			// We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
			var book = SetupBookLong("English text (first national language) should display but vernacular shouldn't.", "en",
				extraContent:
					@"<div class='bloom-editable' lang='xyz'>vernacular text should not display in this case because the user turned it off</div>
							<div class='bloom-editable' lang='fr'>French text (second national language) should not display</div>
							<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
				defaultLanguages: "N1");

			MakeEpub("output", "UserSpecifiedNoVernacular_VernacularRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);

			var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
			assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
		}

		/// <summary>
		/// Content whose display properties resolves to display:None should be removed.
		/// The default rules on a credits page show original acknowledgments only in national language.
		/// </summary>
		[Test]
		public void OriginalAcknowledgments_InCreditsPage_InVernacular_IsRemoved()
		{
			// We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
			var book = SetupBookLong("Acknowledgments should only show in national 1.", "en",
				extraPageClass: " bloom-frontMatter credits' data-page='required singleton",
				extraContent:
					@"<div class='bloom-editable' lang='xyz'><label class='bubble'>Book title in {lang} should be removed</label>acknowledgments in vernacular not displayed</div>
							<div class='bloom-editable' lang='fr'>National 2 should not be displayed</div>
							<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
				extraEditGroupClasses: "originalAcknowledgments",
				defaultLanguages: "N1");

			MakeEpub("output", "OriginalAcknowledgments_InCreditsPage_InVernacular_IsRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data, "pgCreditsPage", "Credits Page");
			CheckEpubTypeAttributes(_page1Data, null);
			//Thread.Sleep(20000);

			//Console.WriteLine(XElement.Parse(_page1Data).ToString());

			var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
			assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
		}

		/// <summary>
		/// Content whose display properties resolves to display:none should be removed.
		/// </summary>
		[Test]
		public void InCreditsPage_LicenseUrlAndISBN_AreRemoved()
		{
			// We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
			var book = SetupBookLong("License url and ISBN should be removed from the epub", "en",
				extraPageClass: " bloom-frontMatter credits' data-page='required singleton",
				extraContentOutsideTranslationGroup:
					@"<div class=""bloom-metaData licenseAndCopyrightBlock"" data-functiononhintclick=""bookMetadataEditor"" data-hint=""Click to Edit Copyright &amp; License"">
						<div class=""copyright Credits-Page-style"" data-derived=""copyright"" lang=""*"">
							Copyright © 2017, me
						</div>
						<div class=""licenseBlock"">
							<img class=""licenseImage"" src=""license.png"" data-derived=""licenseImage""></img>
							<div class=""licenseUrl"" data-derived=""licenseUrl"" lang=""*"">
								http://creativecommons.org/licenses/by/4.0/
							</div>
							<div class=""licenseDescription Credits-Page-style"" data-derived=""licenseDescription"" lang=""en"">
								http://creativecommons.org/licenses/by/4.0/<br></br>
								You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
							</div>
							<div class=""licenseNotes Credits-Page-style"" data-derived=""licenseNotes""></div>
						</div>
					</div>
					<div class=""ISBNContainer"" data-hint=""International Standard Book Number. Leave blank if you don't have one of these."">
						<span class=""bloom-doNotPublishIfParentOtherwiseEmpty Credits-Page-style"">ISBN</span>
						<div class=""bloom-translationGroup"" data-default-languages=""*"">
							<div role=""textbox"" spellcheck=""true"" tabindex=""0"" class=""bloom-editable Credits-Page-style cke_editable cke_editable_inline cke_contents_ltr bloom-content1"" data-book=""ISBN"" contenteditable=""true"" lang=""enc"">
								<p></p>
							</div>
							<div class=""bloom-editable Credits-Page-style"" data-book=""ISBN"" contenteditable=""true"" lang=""*"">
								ABCDEFG
							</div>
							<div role=""textbox"" spellcheck=""true"" tabindex=""0"" class=""bloom-editable Credits-Page-style cke_editable cke_editable_inline cke_contents_ltr bloom-contentNational1"" data-book=""ISBN"" contenteditable=""true"" lang=""en"">
								<p></p>
							</div>
						</div>
					</div>",
				optionalDataDiv: @"<div id=""bloomDataDiv"">
						<div data-book=""contentLanguage1"" lang=""*"">en</div>
						<div data-book=""ISBN"" lang=""*"">ABCDEFG</div>
						<div data-book=""ISBN"" lang=""en"">ABCDEFG-en</div>
					</div>");
			MakeImageFiles(book, "license");

			MakeEpub("output", "InCreditsPage_LicenseUrlAndISBN_AreRemoved", book);
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(false, false, "urn:isbn:ABCDEFG");	// no sound or nontrivial image files
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data, "pgCreditsPage", "Credits Page");
			CheckEpubTypeAttributes(_page1Data, null, "copyright-page");

			var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='ISBNContainer']", _ns);
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='licenseUrl']", _ns);
			assertThatPage1.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'licenseDescription')]", 1);
			assertThatPage1.HasSpecifiedNumberOfMatchesForXpath("//img[@class='licenseImage']", 1);
			// These temp Ids are added and removed during the creation process
			assertThatPage1.HasNoMatchForXpath("//xhtml:div[contains(@id, '" + EpubMaker.kTempIdMarker + "')]", _ns);
		}

		[Test]
		public void FindFontsUsedInCss_FindsSimpleFontFamily()
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family:Arial}", results, true);
			Assert.That(results, Has.Count.EqualTo(1));
			Assert.That(results.Contains("Arial"));
		}

		[Test]
		public void FindFontsUsedInCss_FindsQuotedFontFamily()
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family:'Times New Roman'}", results, true);
			HtmlDom.FindFontsUsedInCss("body {font-family:\"Andika New Basic\"}", results, true);
			Assert.That(results, Has.Count.EqualTo(2));
			Assert.That(results.Contains("Times New Roman"));
			Assert.That(results.Contains("Andika New Basic"));
		}

		[Test]
		public void FindFontsUsedInCss_IncludeFallbackFontsTrue_FindsMultipleFontFamilies()
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family: 'Times New Roman', Arial,\"Andika New Basic\";}", results, true);
			Assert.That(results, Has.Count.EqualTo(3));
			Assert.That(results.Contains("Times New Roman"));
			Assert.That(results.Contains("Andika New Basic"));
			Assert.That(results.Contains("Arial"));
		}

		[Test]
		public void FindFontsUsedInCss_IncludeFallbackFontsFalse_FindsFirstFontInEachList()
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family: 'Times New Roman', Arial,\"Andika New Basic\";} " +
			                           "div {font-family: Font1, \"Font2\";} " +
			                           "p {font-family: \"Font3\";}", results, false);
			Assert.That(results, Has.Count.EqualTo(3));
			Assert.That(results.Contains("Times New Roman"));
			Assert.That(results.Contains("Font1"));
			Assert.That(results.Contains("Font3"));
		}

		[TestCase("Arial !important")]
		[TestCase("Arial ! important")]
		[TestCase("Arial ! important ")]
		[TestCase("Arial  ! important , Times New Roman")]
		public void FindFontsUsedInCss_RemovesBangImportant(string fontFamily)
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family:" + fontFamily + "}", results, false);
			Assert.That(results, Has.Count.EqualTo(1));
			Assert.That(results.Contains("Arial"));
		}

		[Test]
		public void FindFontsUsedInCss_IgnoresInherit()
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family:inherit}", results, false);
			Assert.That(results, Has.Count.EqualTo(0));
		}

		[TestCase("Segoe UI", true)]
		[TestCase("Segoe UI", false)]
		[TestCase("segoe ui,Arial", false)]
		public void FindFontsUsedInCss_IgnoresSegoeUi(string fontFamily, bool includeFallbackFonts)
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family:" + fontFamily + "}", results, includeFallbackFonts);
			Assert.That(results, Has.Count.EqualTo(0));
		}

		[TestCase("segoe ui,Arial")]
		public void FindFontsUsedInCss_IgnoresSegoeUi(string fontFamily)
		{
			var results = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss("body {font-family:" + fontFamily + "}", results, true);
			Assert.That(results, Has.Count.EqualTo(1));
			Assert.That(results.Contains("Arial"));
		}

		[TestCase("A5Portrait", 297.0 / 2.0)]
		[TestCase("HalfLetterLandscape", 8.5 * 25.4)]
		[Test]
		public void ImageStyles_ConvertedToPercent(string sizeClass, double pageWidthMm)
		{
			var book = SetupBookLong("This is some text", "en",
				extraPageClass: " " + sizeClass + " numberedPage' data-page-number='5",
				extraContentOutsideTranslationGroup: @"<div><img src='image1.png' width='334' height='220' style='width:334px; height:220px; margin-left: 34px; margin-top: 0px;'></img></div>
							<div><img src='image2.png' width='330' height='220' style='width:330px; height: 220px; margin-left: 33px; margin-top: 0px;'></img></div>");
			MakeImageFiles(book, "image1", "image2");
			MakeEpub("output", "ImageStyles_ConvertedToPercent" + sizeClass, book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data, "pg5", "5");
			CheckEpubTypeAttributes(_page1Data, null);

			// A5Portrait page is 297/2 mm wide
			// Percent size however is relative to containing block, typically the marginBox,
			// which is inset 40mm from page
			// a px in a printed book is exactly 1/96 in.
			// 25.4mm.in
			var marginboxInches = (pageWidthMm-40)/25.4;
			var picWidthInches = 334/96.0;
			var widthPercent = Math.Round(picWidthInches/marginboxInches*1000)/10;
			var picIndentInches = 34/96.0;
			var picIndentPercent = Math.Round(picIndentInches / marginboxInches * 1000) / 10;
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:img[@style='width:" + widthPercent.ToString("F1")
				+ "%; height:auto; margin-left: " + picIndentPercent.ToString("F1") + "%; margin-top: 0px;']", _ns);

			picWidthInches = 330 / 96.0;
			widthPercent = Math.Round(picWidthInches / marginboxInches * 1000) / 10;
			picIndentInches = 33 / 96.0;
			picIndentPercent = Math.Round(picIndentInches / marginboxInches * 1000) / 10;
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:img[@style='width:" + widthPercent.ToString("F1")
				+ "%; height:auto; margin-left: " + picIndentPercent.ToString("F1") + "%; margin-top: 0px;']", _ns);
		}

		[Test]
		public void ImageStyles_ConvertedToPercent_SpecialCases()
		{
			// First image triggers special case for missing height
			// Second image triggers special cases for no width at all.
			var book = SetupBookLong("This is some text", "en",
				extraPageClass: " A5Portrait numberedPage' data-page-number='3",
				extraContentOutsideTranslationGroup: @"<div><img src='image1.png' width='334' height='220' style='width:334px; margin-left: 34px; margin-top: 0px;'></img></div>
							<div><img src='image2.png' width='330' height='220' style='margin-top: 0px;'></img></div>");
			MakeImageFiles(book, "image1", "image2");
			MakeEpub("output", "ImageStyles_ConvertedToPercent_SpecialCases", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data, "pg3", "3");
			CheckEpubTypeAttributes(_page1Data, null);

			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:img[@style='margin-top: 0px;']", _ns);
			var marginboxInches = (297.0/2.0 - 40) / 25.4;
			var picWidthInches = 334 / 96.0;
			var widthPercent = Math.Round(picWidthInches / marginboxInches * 1000) / 10;
			var picIndentInches = 34 / 96.0;
			var picIndentPercent = Math.Round(picIndentInches / marginboxInches * 1000) / 10;
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:img[@style='height:auto; width:" + widthPercent.ToString("F1")
				+ "%; margin-left: " + picIndentPercent.ToString("F1") + "%; margin-top: 0px;']", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:img[@style='margin-top: 0px;']", _ns);
		}

		[Test]
		public void ImageStyles_PercentsAdjustForContainingPercentDivs()
		{
			var book = SetupBookLong("This is some text", "en",
				extraPageClass: " A5Portrait numberedPage' data-page-number='99",
				extraContentOutsideTranslationGroup: @"<div id='anotherWrapper' style='width:80%'>
								<div id='innerrWrapper' style='width:50%'>
									<div><img src='image1.png' width='40' height='220' style='width:40px; height:220px; margin-left: 14px; margin-top: 0px;'></img></div>
								</div>
							</div>");
			MakeImageFiles(book, "image1");
			MakeEpub("output", "ImageStyles_PercentsAdjustForContainingPercentDivs", book);
			CheckBasicsInManifest("image1");
			CheckBasicsInPage("image1");
			CheckPageBreakMarker(_page1Data, "pg99", "99");
			CheckEpubTypeAttributes(_page1Data, null);

			// A5Portrait page is 297/2 mm wide
			// Percent size however is relative to containing block,
			// which in this case is 50% of 80% of the marginBox,
			// which is inset 40mm from page
			// a px in a printed book is exactly 1/96 in.
			// 25.4mm.in
			var marginboxInches = (297.0 / 2.0 - 40) / 25.4;
			var picWidthInches = 40 / 96.0;
			var parentWidthInches = marginboxInches*0.8*0.5;
			var widthPercent = Math.Round(picWidthInches / parentWidthInches * 1000) / 10;
			var picIndentInches = 14 / 96.0;
			var picIndentPercent = Math.Round(picIndentInches / parentWidthInches * 1000) / 10;
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:img[@style='width:" + widthPercent.ToString("F1")
				+ "%; height:auto; margin-left: " + picIndentPercent.ToString("F1") + "%; margin-top: 0px;']", _ns);
		}

		[Test]
		public void BookWithAudio_ProducesOverlay_OmitsInvalidAttrs()
		{
			var book = SetupBook("<span id='a123' recordingmd5='undefined'>This is some text.</span><span id='a23'>Another sentence</span>", "xyz");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a123.mp4"));
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a23.mp3"));
			MakeEpub("output", "BookWithAudio_ProducesOverlay", book);
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(true, false, _defaultSourceValue);	// sound files but no image files
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();

			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertThatManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil").Replace("audio/", "audio^slash^"));
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='audio_2fa23' and @href='audio_2fa23.mp3' and @media-type='audio^slash^mpeg']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='audio_2fa123' and @href='audio_2fa123.mp4' and @media-type='audio^slash^mp4']");

			var smilData = StripXmlHeader(ExportEpubTestsBaseClass.GetZipContent(_epub, "content/1_overlay.smil"));
			var assertThatSmil = AssertThatXmlIn.String(smilData);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#a123']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#a23']", _ns);
			if (Platform.IsLinux)
			{
				// Approximate audio time calculations on Linux don't work very well, but are at least consistent.
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fa123.mp4' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.534']", _ns);
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fa23.mp3' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.534']", _ns);
			}
			else
			{
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fa123.mp4' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.700']", _ns);
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fa23.mp3' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.700']", _ns);
			}

			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//span[@id='a123' and not(@recordingmd5)]");

			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/audio_2fa123.mp4");
			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/audio_2fa23.mp3");
		}

		[Test]
		public void BookWithAudio_OneAudioPerPage_ProducesOneMp3()
		{
			var book = SetupBook("<span id='a123' recordingmd5='undefined'>This is some text.</span><span id='a23'>Another sentence</span>", "xyz");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a123.mp4"));
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a23.mp3"));
			MakeEpub("output", "BookWithAudio_MergeAudio_ProducesOneMp3", book,
				extraInit: maker => maker.OneAudioPerPage = true);

			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertThatManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil").Replace("audio/", "audio^slash^"));
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='audio_page1' and @href='audio_page1.mp3' and @media-type='audio^slash^mpeg']");

			var smilData = StripXmlHeader(ExportEpubTestsBaseClass.GetZipContent(_epub, "content/1_overlay.smil"));
			var assertThatSmil = AssertThatXmlIn.String(smilData);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#a123']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#a23']", _ns);
			if (Platform.IsLinux)
			{
				// Approximate audio time calculations on Linux don't work very well, but are at least consistent.
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_page1.mp3' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.534']", _ns);
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_page1.mp3' and @clipBegin='0:00:01.534' and @clipEnd='0:00:03.069']", _ns);
			}
			else
			{
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_page1.mp3' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.700']", _ns);
				assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_page1.mp3' and @clipBegin='0:00:01.700' and @clipEnd='0:00:03.400']", _ns);
			}
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//span[@id='a123' and not(@recordingmd5)]");

			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/audio_page1.mp3");
		}

		/// <summary>
		/// There's some special-case code for Ids that start with digits that we test here.
		/// This test has been extended to verify that we get media:duration metadata
		/// </summary>
		[Test]
		public void AudioWithParagraphsAndRealGuids_ProducesOverlay()
		{
			var book = SetupBook("<p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2c'>This is some text.</span><span id='i0d8e9910-dfa3-4376-9373-a869e109b763'>Another sentence</span></p>", "xyz");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp4"));
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "i0d8e9910-dfa3-4376-9373-a869e109b763.mp3"));
			MakeEpub("output", "AudioWithParagraphsAndRealGuids_ProducesOverlay", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();

			var assertManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil"));
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");
			if (Platform.IsLinux)
			{
				// Approximate audio time calculations on Linux don't work very well, but are at least consistent.
				assertManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='media:duration' and not(@refines) and text()='00:00:03.0692130']");
				assertManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='media:duration' and @refines='#f1_overlay' and text()='00:00:03.0692130']");
			}
			else
			{
				// We don't much care how many decimals follow the 03.4 but this is what the default TimeSpan.ToString currently does.
				assertManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='media:duration' and not(@refines) and text()='00:00:03.4000000']");
				assertManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='media:duration' and @refines='#f1_overlay' and text()='00:00:03.4000000']");
			}
			var smilData = StripXmlHeader(ExportEpubTestsBaseClass.GetZipContent(_epub, "content/1_overlay.smil"));
			var assertSmil = AssertThatXmlIn.String(smilData);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3']", _ns);

			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4");
			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3");
		}

		// Sometimes the tests were failing on TeamCity because the file was in use by another process
		// (I'm assuming that was another test since a few use the same path).
		private static readonly object s_thisLock = new object();
		protected void MakeFakeAudio(string path)
		{
			lock (s_thisLock)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				// Bloom is going to try to figure its duration, so put a real audio file there.
				// Some of the paths are for mp4s, but it doesn't hurt to use an mp3.
				var src = SIL.IO.FileLocator.GetFileDistributedWithApplication("src/BloomTests/Publish/sample_audio.mp3");
				File.Copy(src, path);
				var wavSrc = Path.ChangeExtension(src, ".wav");
				File.Copy(wavSrc, Path.ChangeExtension(path, "wav"), true);
			}
		}

		[TestCase("abc", ExpectedResult = "abc")]
		[TestCase("123", ExpectedResult = "f123")]
		[TestCase("123 abc", ExpectedResult = "f123abc")]
		[TestCase("x*y", ExpectedResult = "x_y")]
		[TestCase("x:y", ExpectedResult = "x:y")]
		[TestCase("*edf", ExpectedResult = "_edf")]
		[TestCase("a\u0300z", ExpectedResult = "a\u0300z")] // valid mid character
		[TestCase("\u0300z", ExpectedResult = "f\u0300z")] // invalid start character
		[TestCase("a\u037Ez", ExpectedResult = "a_z")] // high-range invalid
		[TestCase("-abc", ExpectedResult = "f-abc")]
		[Test]
		public string ToValidXmlId(string input)
		{
			return EpubMaker.ToValidXmlId(input);
		}

		[Test]
		public void IllegalIds_AreFixed()
		{
			var book = SetupBookLong("This is some text", "xyz", parentDivId:"12");
			MakeEpub("output", "IllegalIds_AreFixed", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);

			// This is possibly too strong; see comment where we remove them.
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//div[@id='i12']");
		}

		[Test]
		public void ConflictingIds_AreNotConfused()
		{
			// Files called 12.png and f12.png are quite safe and distinct as files, but they will generate the same ID,
			// so one must be fixed. The second occurrence of f12, however, should not cause another file to be generated.
			var book = SetupBook("This is some text", "xyz", "12", "f12", "f12");
			MakeImageFiles(book, "12", "f12");
			MakeEpub("output", "ConflictingIds_AreNotConfused", book);
			CheckBasicsInManifest(); // don't check the file stuff here, we're looking at special cases
			CheckBasicsInPage();
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();

			var assertThatManifest = AssertThatXmlIn.String(_manifestContent);
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f12' and @href='12.png']");
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/manifest/item[@id='f121' and @href='f12.png']", 1);
			assertThatManifest.HasNoMatchForXpath("package/manifest/item[@href='f121.png']"); // What it would typically generate if it made another copy.

			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//img[@src='12.png']");
			AssertThatXmlIn.String(_page1Data).HasSpecifiedNumberOfMatchesForXpath("//img[@src='f12.png']", 2);

			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/12.png");
			ExportEpubTestsBaseClass.GetZipEntry(_epub, "content/f12.png");
		}

		/// <summary>
		/// Also tests various odd characters that might be in file names. The &amp; is not technically
		/// valid in an href but it's what Bloom currently does for & in file name.
		/// </summary>
		[Test]
		public void FilesThatMapToSameSafeName_AreNotConfused()
		{
#if __MonoCS__
			// The first two image src values used here will both initially attempt to save as my_3dImage.png, so one must be renamed.
			// The latter two image src values both try to save as my_image.png, so one must be renamed.
			var book = SetupBook("This is some text", "en", "my%3Dimage", "my_3dimage", "my%2bimage", "my&amp;image");
			MakeImageFiles(book, "my=image", "my_3dimage", "my+image", "my&image");
#else
			// The two image src values used here will both initially attempt to save as my_3DImage.png, so one will have to be renamed
			var book = SetupBook("This is some text", "en", "my%3Dimage", "my_3Dimage", "my%2bimage", "my&amp;image");
			MakeImageFiles(book, "my=image", "my_3Dimage", "my+image", "my&image");
#endif
			MakeEpub("output", "FilesThatMapToSameSafeName_AreNotConfused", book);
#if __MonoCS__
			CheckBasicsInManifest("my_3dimage", "my_3dimage1", "my_image", "my_image1");
			CheckBasicsInPage("my_3dimage", "my_3dimage1", "my_image", "my_image1");
#else
			CheckBasicsInManifest("my_3dimage", "my_3Dimage1", "my_image", "my_image1");
			CheckBasicsInPage("my_3dimage", "my_3Dimage1", "my_image", "my_image1");
#endif
			CheckPageBreakMarker(_page1Data);
			CheckEpubTypeAttributes(_page1Data, null);
			CheckNavPage();
			CheckFontStylesheet();
		}

		[Test]
		public void AddFontFace_WithNullPath_AddsNothing()
		{
			var sb = new StringBuilder();
			Assert.DoesNotThrow(() => EpubMaker.AddFontFace(sb, "myFont", "bold", "italic", null));
			Assert.That(sb.Length, Is.EqualTo(0));
		}

		[Test]
		public void HasFullAudioCoverage_ReturnsTrue()
		{
			var book = SetupBook("\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2d' class='audio-sentence'>This is some text.</span> <span id='i0d8e9910-dfa3-4376-9373-a869e109b764' class='audio-sentence'>Another sentence</span></p>  \r\n", "xyz");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2d.mp4"));
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "i0d8e9910-dfa3-4376-9373-a869e109b764.mp3"));
			var hasFullAudio = EpubMaker.HasFullAudioCoverage(book);
			Assert.IsTrue(hasFullAudio, "HasFullAudioCoverage should return true when appropriate");
			MakeEpub("output", "HasFullAudioCoverage_ReturnsTrue", book);
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(true, false, _defaultSourceValue, hasFullAudio);	// sound files, but no image files
		}

		[Test]
		public void HasFullAudioCoverage_ReturnsFalseForNoAudio()
		{
			var book = SetupBook("\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2f' class='audio-sentence'>This is some text.</span> Another sentence</p>  \r\n", "xyz");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2f.mp3"));
			var hasFullAudio = EpubMaker.HasFullAudioCoverage(book);
			Assert.IsFalse(hasFullAudio, "HasFullAudioCoverage should return false when not all text is covered by an audio span.");
			MakeEpub("output", "HasFullAudioCoverage_ReturnsFalseForNoAudio", book);
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(true, false, _defaultSourceValue, hasFullAudio);	// sound files, but no image files
		}

		[Test]
		public void HasFullAudioCoverage_ReturnsFalseForMissingAudioFile()
		{
			var book = SetupBook("\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2e' class='audio-sentence'>This is some text.</span> <span id='i0d8e9910-dfa3-4376-9373-a869e109b765' class='audio-sentence'>Another sentence</span></p>  \r\n", "xyz");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "i0d8e9910-dfa3-4376-9373-a869e109b765.mp3"));
			var hasFullAudio = EpubMaker.HasFullAudioCoverage(book);
			Assert.IsFalse(hasFullAudio, "HasFullAudioCoverage should return false when an audio file is missing.");
			MakeEpub("output", "HasFullAudioCoverage_ReturnsFalseForMissingAudioFile", book);
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(true, false, _defaultSourceValue, hasFullAudio);	// sound files, but no image files
		}

		[Test]
		public void HasFullAudioCoverage_IgnoresOtherLanguage()
		{
			var book = SetupBookLong("\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2f' class='audio-sentence'>This is some text.</span> <span id='i0d8e9910-dfa3-4376-9373-a869e109b764' class='audio-sentence'>Another sentence</span></p>  \r\n", "xyz",
							extraContent: @"
					<div class='bloom-editable' lang='xyz' contenteditable='true'>
						<p><span id='e993d14a-0ec3-4316-840b-ac9143d59a20' class='audio-sentence'>More text with audio</span></p>
					</div>
					<div class='bloom-editable' lang='de' contenteditable='true'>German should never display in this collection, and audio shouldn't be required either!</div>");
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2f.mp3"));
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a20.mp3"));
			MakeFakeAudio(book.FolderPath.CombineForPath("audio", "i0d8e9910-dfa3-4376-9373-a869e109b764.mp3"));
			var hasFullAudio = EpubMaker.HasFullAudioCoverage(book);
			Assert.IsTrue(hasFullAudio, "HasFullAudioCoverage should return true, ignoring text in other languages");
			MakeEpub("output", "HasFullAudioCoverage_IgnoresOtherLanguage", book);
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(true, false, _defaultSourceValue, hasFullAudio);	// sound files, but no image files
		}
	}

	class EpubMakerAdjusted : EpubMaker
	{
		// Todo: at least some test involving a real BookServer which validates the device xmatter behavior.
		// The minimal bookServer created by CreateBookServer fails to copy mock books because
		// GetPathHtmlFile() return an empty string, I think because we don't make a real book file with mocked books.
		// So I think all the tests are currently passing null for the bookserver, which disables the
		// device xmatter code.
		public EpubMakerAdjusted(Bloom.Book.Book book, BookThumbNailer thumbNailer, BookServer bookServer) :
			base(thumbNailer, NavigationIsolator.GetOrCreateTheOneNavigationIsolator(),
				string.IsNullOrEmpty(book.GetPathHtmlFile())? null : bookServer)
		{
			this.Book = book;
			AudioProcessor._compressorMethod = EpubMakerAdjusted.PretendMakeCompressedAudio;
		}

		internal override void CopyFile(string srcPath, string dstPath, bool reduceImageIfPossible=false, bool needsTransparentBackground=false)
		{
			if (srcPath.Contains("notareallocation"))
			{
				File.WriteAllText(dstPath, "This is a test fake");
				return;
			}
			base.CopyFile(srcPath, dstPath, reduceImageIfPossible, needsTransparentBackground);
		}

		// We can't test real compression because (a) the wave file is fake to begin with
		// and (b) we can't assume the machine running the tests has LAME installed.
		internal static string PretendMakeCompressedAudio(string wavPath)
		{
			var output = Path.ChangeExtension(wavPath, "mp3");
			File.Copy(wavPath, output);
			return output;
		}
	}
}
