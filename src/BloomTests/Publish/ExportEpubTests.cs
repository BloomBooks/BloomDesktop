using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish;
using BloomTemp;
using BloomTests.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.Extensions;

namespace BloomTests.Publish
{
	[TestFixture]
	[Platform(Exclude = "Linux", Reason = "Linux code not yet available.")]
	public class ExportEpubTests : BookTestsBase
	{
		private readonly XNamespace _xhtml = "http://www.w3.org/1999/xhtml";
		private ZipFile _epub; // The ePUB that the test created, converted to a zip file.
		private string _manifestFile; // The path in _epub to the main manifest file.
		private string _manifestContent; // the contents of _manifestFile (as a string)
		private XDocument _manifestDoc; // the contents of _manifestFile as an XDocument.
		private string _page1Data; // contents of the file "1.xhtml" (the main content of the test book, typically)
		private XmlNamespaceManager _ns; // Set up with all the namespaces we use (See GetNamespaceManager())

		public override void Setup()
		{
			base.Setup();
			GetNamespaceManager();
		}

		[Test]
		public void HandlesWhiteSpaceInImageNames()
		{
			// These two names (my_Image and "my image") are especially interesting because they differ by case and also white space.
			// The case difference is not important to the Windows file system.
			// The white space must be removed to make an XML ID.
			var book = SetupBook("This is some text", "en", "my_Image", "my%20image");
			MakeImageFiles(book, "my_Image", "my image");
			MakeEpub("output", "HandlesWhiteSpaceInImageNames", book);
			CheckBasicsInManifest("my_Image", "my_image1");
			CheckBasicsInPage("my_Image", "my_image1");
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
			CheckNavPage();
			CheckFontStylesheet();
		}

		Bloom.Book.Book SetupBook(string text, string lang, params string[] images)
		{
			return SetupBookLong(text, lang, images: images);
		}

		/// <summary>
		/// Set up a book with the typical content most of our tests need. It has the standard three stylesheets
		/// (and empty files for them). It has one bloom editable div, in the specified language, with the specified text.
		/// It has a 'lang='*' div which is ignored.
		/// It may have extra content in various places.
		/// It may have an arbitrary number of images, with the specified names.
		/// The image files are not created in this method, because the exact correspondence between the
		/// image names inserted into the HTML and the files created (or sometimes purposely not created)
		/// is an important aspect of many tests.
		/// </summary>
		/// <param name="text"></param>
		/// <param name="lang"></param>
		/// <param name="extraContent"></param>
		/// <param name="extraImages"></param>
		/// <param name="extraStyleSheet"></param>
		/// <param name="extraPages"></param>
		/// <param name="images"></param>
		/// <param name="extraEditGroupClasses"></param>
		/// <param name="extraEditDivClasses"></param>
		/// <param name="parentDivId"></param>
		/// <returns></returns>
		Bloom.Book.Book SetupBookLong(string text, string lang, string extraPageClass = "", string extraContent = "", string extraImages = "",
			string extraStyleSheet= "", string parentDivId = "somewrapper", string extraPages="", string[] images = null,
			string extraEditGroupClasses = "", string extraEditDivClasses = "")
		{
			if (images == null)
				images = new string[0];
			string imageDivs = "";
			foreach (var image in images)
				imageDivs += "<div><img src='" + image + ".png'></img></div>\n";
			var body = string.Format(@"<div class='bloom-page" + extraPageClass + @"'>
						<div id='" + parentDivId + @"' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs {7}' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable {6}' lang='{0}'>
									{1}
								</div>
								{2}
								<div lang = '*'>more text</div>
							</div>
							{3}
							{4}
						</div>
					</div>
					{5}",
				lang, text, extraContent, imageDivs, extraImages, extraPages, extraEditDivClasses, extraEditGroupClasses);
			SetDom(body,
				string.Format(@"<link rel='stylesheet' href='../settingsCollectionStyles.css'/>
							{0}
							<link rel='stylesheet' href='../customCollectionStyles.css'/>
							<link rel='stylesheet' href='customBookStyles.css'/>", extraStyleSheet));
			var book = CreateBook();
			CreateCommonCssFiles(book);
			return book;
		}

		void MakeImageFiles(Bloom.Book.Book book, params string[] images)
		{
			foreach (var image in images)
				MakeSamplePngImageWithMetadata(book.FolderPath.CombineForPath(image + ".png"));
		}

		private void CheckNavPage()
		{
			XNamespace opf = "http://www.idpf.org/2007/opf";

			var navPage = _manifestDoc.Root.Element(opf + "manifest").Elements(opf + "item").Last().Attribute("href").Value;
			var navPageData = StripXmlHeader(GetZipContent(_epub, Path.GetDirectoryName(_manifestFile) + "/" + navPage));
			AssertThatXmlIn.String(navPageData)
				.HasAtLeastOneMatchForXpath(
					"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='1.xhtml']", _ns);
		}

		private void CheckFontStylesheet()
		{
			var fontCssData = GetZipContent(_epub, "content/fonts.css");
			Assert.That(fontCssData,
				Is.StringContaining(
					"@font-face {font-family:'Andika New Basic'; font-weight:normal; font-style:normal; src:url(AndikaNewBasic-R.ttf) format('opentype');}"));
			Assert.That(fontCssData,
				Is.StringContaining(
					"@font-face {font-family:'Andika New Basic'; font-weight:bold; font-style:normal; src:url(AndikaNewBasic-B.ttf) format('opentype');}"));
			Assert.That(fontCssData,
				Is.StringContaining(
					"@font-face {font-family:'Andika New Basic'; font-weight:normal; font-style:italic; src:url(AndikaNewBasic-I.ttf) format('opentype');}"));
			Assert.That(fontCssData,
				Is.StringContaining(
					"@font-face {font-family:'Andika New Basic'; font-weight:bold; font-style:italic; src:url(AndikaNewBasic-BI.ttf) format('opentype');}"));
		}

		private void CheckBasicsInManifest(params string[] imageFiles)
		{
			VerifyThatFilesInManifestArePresent();
			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertThatManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/", "application^slash^"));
			assertThatManifest.HasAtLeastOneMatchForXpath("package[@version='3.0']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package[@unique-identifier]");
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:title", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:language", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:identifier", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='dcterms:modified']");

			// This is not absolutely required, but it's true for all our test cases and the way we generate books.
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml']");
			// And that one page must be in the spine
			assertThatManifest.HasAtLeastOneMatchForXpath("package/spine/itemref[@idref='f1']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@properties='nav']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@properties='cover-image']");

			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='settingsCollectionStyles' and @href='settingsCollectionStyles.css']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='customCollectionStyles' and @href='customCollectionStyles.css']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='customBookStyles' and @href='customBookStyles.css']");

			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='AndikaNewBasic-R' and @href='AndikaNewBasic-R.ttf' and @media-type='application^slash^vnd.ms-opentype']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='AndikaNewBasic-B' and @href='AndikaNewBasic-B.ttf' and @media-type='application^slash^vnd.ms-opentype']");
			// It should include italic and BI too...though eventually it may get smarter and figure they are not used...but I think this is enough to test
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='fonts' and @href='fonts.css']");

			foreach (var image in imageFiles)
				assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='" + image + "' and @href='" + image + ".png']");
		}

		private void CheckBasicsInPage(params string[] images)
		{
			// This is possibly too strong; see comment where we remove them.
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//*[@aria-describedby]");
			// Not sure why we sometimes have these, but validator doesn't like them.
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//*[@lang='']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//xhtml:script", _ns);
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//*[@lang='*']");
			foreach (var image in images)
				AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//img[@src='" +image + ".png']");
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='settingsCollectionStyles.css']", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='customCollectionStyles.css']", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='customBookStyles.css']", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='fonts.css']", _ns);
		}

		/// <summary>
		/// Check that all the files referenced in the manifest are actually present in the zip.
		/// </summary>
		void VerifyThatFilesInManifestArePresent()
		{
			XNamespace opf = "http://www.idpf.org/2007/opf";
			var files = _manifestDoc.Root.Element(opf + "manifest").Elements(opf + "item").Select(item => item.Attribute("href").Value);
			foreach (var file in files)
			{
				GetZipEntry(_epub, Path.GetDirectoryName(_manifestFile) + "/" + file);
			}
		}

		void GetPageOneData()
		{
			_page1Data = GetZipContent(_epub, Path.GetDirectoryName(_manifestFile) + "/" + "1.xhtml");
		}

		private XmlNamespaceManager GetNamespaceManager()
		{
			_ns = new XmlNamespaceManager(new NameTable());
			_ns.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
			_ns.AddNamespace("opf", "http://www.idpf.org/2007/opf");
			_ns.AddNamespace("epub", "http://www.idpf.org/2007/ops");
			_ns.AddNamespace("xhtml", "http://www.w3.org/1999/xhtml");
			_ns.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
			return _ns;
		}

		// Do some basic checks and get a path to the main manifest (open package format) file, typically content.opf
		private string GetManifestFile(ZipFile zip)
		{
			// Every ePUB must have a mimetype at the root
			GetZipContent(zip, "mimetype");

			// Every ePUB must have a "META-INF/container.xml." (case matters). Most things we could check about its content
			// would be redundant with the code that produces it, but we can at least verify that it is valid
			// XML and extract the manifest data.
			var containerData = GetZipContent(zip, "META-INF/container.xml");
			var doc = XDocument.Parse(containerData);
			XNamespace ns = doc.Root.Attribute("xmlns").Value;
			return doc.Root.Element(ns + "rootfiles").Element(ns + "rootfile").Attribute("full-path").Value;
		}

		/// <summary>
		/// Make an ePUB out of the specified book. Sets up several instance variables with commonly useful parts of the results.
		/// </summary>
		/// <param name="mainFileName"></param>
		/// <param name="folderName"></param>
		/// <param name="book"></param>
		/// <returns></returns>
		private ZipFile MakeEpub(string mainFileName, string folderName, Bloom.Book.Book book)
		{
			var epubFolder = new TemporaryFolder(folderName);
			var epubName = mainFileName + ".epub";
			var epubPath = Path.Combine(epubFolder.FolderPath, epubName);
			using (var maker = CreateEpubMaker(book))
			{
				maker.Unpaginated = true; // Currently we always make unpaginated epubs.
				maker.SaveEpub(epubPath);
			}
			Assert.That(File.Exists(epubPath));
			_epub= new ZipFile(epubPath);
			_manifestFile = GetManifestFile(_epub);
			_manifestContent = StripXmlHeader(GetZipContent(_epub, _manifestFile));
			_manifestDoc = XDocument.Parse(_manifestContent);
			GetPageOneData();
			return _epub;
		}

		private EpubMakerAdjusted CreateEpubMaker(Bloom.Book.Book book)
		{
			return new EpubMakerAdjusted(book, new BookThumbNailer(_thumbnailer.Object));
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
			CheckNavPage();
			CheckFontStylesheet();

			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil"));
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");

			var smilData = StripXmlHeader(GetZipContent(_epub, "content/1_overlay.smil"));
			var mgr = new XmlNamespaceManager(new NameTable());
			mgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
			mgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");
			var assertSmil = AssertThatXmlIn.String(smilData);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3']", mgr);

			GetZipEntry(_epub, "content/audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4");
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
			CheckBasicsInPage("my_image");
			CheckNavPage();
			CheckFontStylesheet();

			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil"));
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");

			var smilData = StripXmlHeader(GetZipContent(_epub, "content/1_overlay.smil"));
			var mgr = new XmlNamespaceManager(new NameTable());
			mgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
			mgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");
			var assertSmil = AssertThatXmlIn.String(smilData);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']", mgr);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp3']", mgr);
			assertSmil.HasNoMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3']", mgr);

			GetZipEntry(_epub, "content/audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp3");
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
			CheckBasicsInPage("myImage");
		}

		[Test]
		public void HandlesMultiplePages()
		{
			var book = SetupBookLong("This is some text",
				"en", extraPages: @"<div class='bloom-page'>
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
			CheckBasicsInPage();

			var page2Data = GetZipContent(_epub, Path.GetDirectoryName(_manifestFile) + "/" + "2.xhtml");
			AssertThatXmlIn.String(page2Data).HasAtLeastOneMatchForXpath("//div[@id='anotherId']");
		}

		/// <summary>
		/// Motivated by "Look in the sky. What do you see?" from bloom library, if we can't find an image,
		/// remove the element. Also exercises some other edge cases of missing or empty src attrs for images.
		/// </summary>
		[Test]
		public void ImageMissing_IsRemoved()
		{
			var book = SetupBookLong("This is some text", "en",
				extraImages: @"<div><img></img></div>
							<div><img src=''></img></div>
							<div><img src='?1023456'></img></div>",
				images:new [] {"myImage.png?1023456"});
			// Purposely do NOT create any images.
			MakeEpub("output", "ImageMissing_IsRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();

			AssertThatXmlIn.String(_manifestContent).HasNoMatchForXpath("package/manifest/item[@id='fmyImage' and @href='myImage.png']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//img");
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

			AssertThatXmlIn.String(_manifestContent).HasNoMatchForXpath("package/manifest/item[@id='fmyImage' and @href='myImage.png']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//img[@src='myImage.png']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//div[@class='bloom-ui rubbish']");
		}

		// Set up some typical CSS files we DO want to include, even in 'unpaginated' mode
		private static void CreateCommonCssFiles(Bloom.Book.Book book)
		{
			var collectionFolder = Path.GetDirectoryName(book.FolderPath);
			var settingsCollectionPath = Path.Combine(collectionFolder, "settingsCollectionStyles.css");
			File.WriteAllText(settingsCollectionPath, "body:{font-family: 'Andika New Basic';}");
			var customCollectionPath = Path.Combine(collectionFolder, "customCollectionStyles.css");
			File.WriteAllText(customCollectionPath, "body:{font-family: 'Andika New Basic';}");
			var customBookPath = Path.Combine(book.FolderPath, "customBookStyles.css");
			File.WriteAllText(customBookPath, "body:{font-family: 'Andika New Basic';}");
		}

		protected override Bloom.Book.Book CreateBook()
		{
			var book = base.CreateBook();
			// Export requires us to have a thumbnail.
			MakeSamplePngImageWithMetadata(book.FolderPath.CombineForPath("thumbnail-256.png"));
			return book;
		}

		[Test]
		public void StandardStyleSheets_AreRemoved()
		{
			var book = SetupBookLong("Some text", "en", extraStyleSheet: "<link rel='stylesheet' href='basePage.css'/>");
			MakeEpub("output", "StandardStyleSheets_AreRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
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
								<div class='bloom-editable' lang='de'>German should never display in this collection</div>");
			MakeEpub("output", "InvisibleAndUnwantedContentRemoved", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();
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
			// This test does some real navigation so needs the server to be running.
			using (GetTestServer())
			{
				// We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
				var book = SetupBookLong("English text (bloom-contentNational1) should display in title.", "en",
					extraPageClass: " bloom-frontMatter frontCover",
					extraContent:
						@"<div class='bloom-editable bloom-content1' lang='xyz'><label class='bubble'>Book title in {lang} should be removed</label>vernacular text (content1) should always display</div>
								<div class='bloom-editable bloom-contentNational2' lang='fr'>French text (national2) should not display</div>
								<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
					extraStyleSheet: "<link rel='stylesheet' href='basePage.css' type='text/css'></link><link rel='stylesheet' href='Factory-XMatter/Factory-XMatter.css' type='text/css'></link>",
					extraEditGroupClasses: "bookTitle",
					extraEditDivClasses: "bloom-contentNational1");
				//CopyFactoryXMatter(server, book);
				MakeEpub("output", "National1_InXMatter_IsNotRemoved", book);
				CheckBasicsInManifest();
				CheckBasicsInPage();

				var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
				assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
				assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
				assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
				assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
				assertThatPage1.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
				assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
			}
		}

		private static EnhancedImageServer GetTestServer()
		{
			var server = new EnhancedImageServer(new RuntimeImageProcessor(new BookRenamedEvent()), null, new BookSelection(), GetTestFileLocator());
			server.StartListening();
			return server;
		}

		private static BloomFileLocator GetTestFileLocator()
		{
			return new BloomFileLocator(new CollectionSettings(), new XMatterPackFinder(new [] { BloomFileLocator.GetInstalledXMatterDirectory() }), ProjectContext.GetFactoryFileLocations(),
				ProjectContext.GetFoundFileLocations(), ProjectContext.GetAfterXMatterFileLocations());
		}

		/// <summary>
		/// Content whose display properties resolves to display:None should be removed.
		/// The default rules on a credits page show original acknowledgements only in national language.
		/// </summary>
		[Test]
		public void OriginalAcknowledgents_InCreditsPage_InVernacular_IsRemoved()
		{
			// This test does some real navigation so needs the server to be running.
			using (GetTestServer())
			{
				// We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
				var book = SetupBookLong("Acknowledgements should only show in national 1.", "en",
					extraPageClass: " bloom-frontMatter credits",
					extraContent:
						@"<div class='bloom-editable bloom-content1' lang='xyz'><label class='bubble'>Book title in {lang} should be removed</label>acknowledgements in vernacular not displayed</div>
								<div class='bloom-editable bloom-contentNational2 bloom-content2' lang='fr'>National 2 should not be displayed</div>
								<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
					extraStyleSheet:
						"<link rel='stylesheet' href='basePage.css' type='text/css'></link><link rel='stylesheet' href='Factory-XMatter/Factory-XMatter.css' type='text/css'></link>",
					extraEditGroupClasses: "originalAcknowledgments",
					extraEditDivClasses: "bloom-contentNational1");
				MakeEpub("output", "OriginalAcknowledgents_InCreditsPage_InVernacular_IsRemoved", book);
				CheckBasicsInManifest();
				CheckBasicsInPage();
				//Thread.Sleep(20000);

				var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
				assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
				assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
				assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
				assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
				assertThatPage1.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
				assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
			}
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

		[TestCase("A5Portrait", 297.0 / 2.0)]
		[TestCase("HalfLetterLandscape", 8.5 * 25.4)]
		[Test]
		public void ImageStyles_ConvertedToPercent(string sizeClass, double pageWidthMm)
		{
			var book = SetupBookLong("This is some text", "en",
				extraPageClass: " " + sizeClass,
				extraImages: @"<div><img src='image1.png' width='334' height='220' style='width:334px; height:220px; margin-left: 34px; margin-top: 0px;'></img></div>
							<div><img src='image2.png' width='330' height='220' style='width:330px; height: 220px; margin-left: 33px; margin-top: 0px;'></img></div>");
			MakeImageFiles(book, "image1", "image2");
			MakeEpub("output", "ImageStyles_ConvertedToPercent" + sizeClass, book);
			CheckBasicsInManifest();
			CheckBasicsInPage();

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
				extraPageClass: " A5Portrait",
				extraImages: @"<div><img src='image1.png' width='334' height='220' style='width:334px; margin-left: 34px; margin-top: 0px;'></img></div>
							<div><img src='image2.png' width='330' height='220' style='margin-top: 0px;'></img></div>");
			MakeImageFiles(book, "image1", "image2");
			MakeEpub("output", "ImageStyles_ConvertedToPercent_SpecialCases", book);
			CheckBasicsInManifest();
			CheckBasicsInPage();

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
				extraPageClass: " A5Portrait",
				extraImages: @"<div id='anotherWrapper' style='width:80%'>
								<div id='innerrWrapper' style='width:50%'>
									<div><img src='image1.png' width='40' height='220' style='width:40px; height:220px; margin-left: 14px; margin-top: 0px;'></img></div>
								</div>
							</div>");
			MakeImageFiles(book, "image1");
			MakeEpub("output", "ImageStyles_PercentsAdjustForContainingPercentDivs", book);
			CheckBasicsInManifest("image1");
			CheckBasicsInPage("image1");

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
			CheckBasicsInPage();
			CheckNavPage();
			CheckFontStylesheet();

			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			var assertThatManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil").Replace("audio/", "audio^slash^"));
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='audio_2fa23' and @href='audio_2fa23.mp3' and @media-type='audio^slash^mpeg']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='audio_2fa123' and @href='audio_2fa123.mp4' and @media-type='audio^slash^mp4']");

			var smilData = StripXmlHeader(GetZipContent(_epub, "content/1_overlay.smil"));
			var assertThatSmil = AssertThatXmlIn.String(smilData);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#a123']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#a23']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fa123.mp4']", _ns);
			assertThatSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fa23.mp3']", _ns);

			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//span[@id='a123' and not(@recordingmd5)]");

			GetZipEntry(_epub, "content/audio_2fa123.mp4");
			GetZipEntry(_epub, "content/audio_2fa23.mp3");
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
			CheckNavPage();
			CheckFontStylesheet();

			var assertManifest = AssertThatXmlIn.String(_manifestContent.Replace("application/smil", "application^slash^smil"));
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml' and @media-overlay='f1_overlay']");
			assertManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1_overlay' and @href='1_overlay.smil' and @media-type='application^slash^smil+xml']");
			// We don't much care how many decimals follow the 03.4 but this is what the default TimeSpan.ToString currently does.
			assertManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='media:duration' and not(@refines) and text()='00:00:03.4000000']");
			assertManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='media:duration' and @refines='#f1_overlay' and text()='00:00:03.4000000']");

			var smilData = StripXmlHeader(GetZipContent(_epub, "content/1_overlay.smil"));
			var assertSmil = AssertThatXmlIn.String(smilData);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq[@epub:textref='1.xhtml' and @epub:type='bodymatter chapter']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='1.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='1.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4']", _ns);
			assertSmil.HasAtLeastOneMatchForXpath("smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3']", _ns);

			GetZipEntry(_epub, "content/audio_2fe993d14a-0ec3-4316-840b-ac9143d59a2c.mp4");
			GetZipEntry(_epub, "content/audio_2fi0d8e9910-dfa3-4376-9373-a869e109b763.mp3");
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

		private string GetZipContent(ZipFile zip, string path)
		{
			var entry = GetZipEntry(zip, path);
			var buffer = new byte[entry.Size];
			var stream = zip.GetInputStream(entry);
			stream.Read(buffer, 0, (int) entry.Size);
			return Encoding.UTF8.GetString(buffer);
		}

		private static ZipEntry GetZipEntry(ZipFile zip, string path)
		{
			var entry = zip.GetEntry(path);
			Assert.That(entry, Is.Not.Null, "Should have found entry at " + path);
			Assert.That(entry.Name, Is.EqualTo(path), "Expected entry has wrong case");
			return entry;
		}

		private string StripXmlHeader(string data)
		{
			var index = data.IndexOf("?>");
			if (index > 0)
				return data.Substring(index + 2);
			return data;
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
			CheckNavPage();
			CheckFontStylesheet();

			var assertThatManifest = AssertThatXmlIn.String(_manifestContent);
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f12' and @href='12.png']");
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/manifest/item[@id='f121' and @href='f12.png']", 1);
			assertThatManifest.HasNoMatchForXpath("package/manifest/item[@href='f121.png']"); // What it would typically generate if it made another copy.

			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//img[@src='12.png']");
			AssertThatXmlIn.String(_page1Data).HasSpecifiedNumberOfMatchesForXpath("//img[@src='f12.png']", 2);

			GetZipEntry(_epub, "content/12.png");
			GetZipEntry(_epub, "content/f12.png");
		}

		/// <summary>
		/// Also tests various odd characters that might be in file names. The &amp; is not technically
		/// valid in an href but it's what Bloom currently does for & in file name.
		/// </summary>
		[Test]
		public void FilesThatMapToSameSafeName_AreNotConfused()
		{
			// The two image src values used here will both initially attempt to save as my_3DImage.png, so one will have to be renamed
			var book = SetupBook("This is some text", "en", "my%3Dimage", "my_3Dimage", "my%2bimage", "my&amp;image");
			MakeImageFiles(book, "my=image", "my_3Dimage", "my+image", "my&image");
			MakeEpub("output", "FilesThatMapToSameSafeName_AreNotConfused", book);
			CheckBasicsInManifest("my_3dimage", "my_3Dimage1", "my_image", "my_image1");
			CheckBasicsInPage("my_3dimage", "my_3Dimage1", "my_image", "my_image1");
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
	}

	class EpubMakerAdjusted : EpubMaker
	{
		public EpubMakerAdjusted(Bloom.Book.Book book, BookThumbNailer thumbNailer) : base(thumbNailer, new NavigationIsolator())
		{
			this.Book = book;
		}

		internal override void CopyFile(string srcPath, string dstPath)
		{
			if (srcPath.Contains("notareallocation"))
			{
				File.WriteAllText(dstPath, "This is a test fake");
				return;
			}
			base.CopyFile(srcPath, dstPath);
		}

		// We can't test real compression because (a) the wave file is fake to begin with
		// and (b) we can't assume the machine running the tests has LAME installed.
		internal override string MakeCompressedAudio(string wavPath)
		{
			var output = Path.ChangeExtension(wavPath, "mp3");
			File.Copy(wavPath, output);
			return output;
		}
	}
}
