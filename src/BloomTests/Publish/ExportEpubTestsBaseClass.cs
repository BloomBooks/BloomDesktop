﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish.Epub;
using Bloom.web;
using BloomTemp;
using BloomTests.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.Extensions;

namespace BloomTests.Publish
{
	/// <summary>
	/// Contains a chunk of common code useful for testing export of epubs.
	/// Most of this code was extracted unchanged from ExportEpubTests (except for making various private things
	/// protected).
	/// The main purpose is to allow classes like ExportEpubWithLinksTests, where the class is set up to
	/// do one export, but has many unit tests corresponding to the many things we want to verify about the output.
	/// </summary>
	public class ExportEpubTestsBaseClass : BookTestsBase
	{
		protected readonly XNamespace _xhtml = "http://www.w3.org/1999/xhtml";
		protected ZipFile _epub; // The ePUB that the test created, converted to a zip file.
		protected string _manifestFile; // The path in _epub to the main manifest file.
		protected string _manifestContent; // the contents of _manifestFile (as a string)
		protected string _page1Data; // contents of the file "1.xhtml" (the main content of the test book, typically)
		protected XDocument _manifestDoc; // the contents of _manifestFile as an XDocument.
		protected XmlNamespaceManager _ns; // Set up with all the namespaces we use (See GetNamespaceManager())
		protected static EnhancedImageServer s_testServer;
		protected static BookSelection s_bookSelection;
		protected BookServer _bookServer;
		protected string _defaultSourceValue;

		protected const string kAudioSlash = EpubMaker.kAudioFolder+"^slash^";
		protected const string kCssSlash = EpubMaker.kCssFolder+"^slash^";
		protected const string kFontsSlash = EpubMaker.kFontsFolder+"^slash^";
		protected const string kImagesSlash = EpubMaker.kImagesFolder+"^slash^";

		[OneTimeSetUp]
		public virtual void OneTimeSetup()
		{
			s_testServer = GetTestServer();
		}

		[OneTimeTearDown]
		public virtual void OneTimeTearDown()
		{
			s_testServer.Dispose();
		}


		internal static EnhancedImageServer GetTestServer()
		{
			var server = new EnhancedImageServer(new RuntimeImageProcessor(new BookRenamedEvent()), null, GetTestBookSelection(), GetTestFileLocator());
			server.StartListening();
			return server;
		}


		private static BookSelection GetTestBookSelection()
		{
			s_bookSelection = new BookSelection();
			return s_bookSelection;
		}

		private static BloomFileLocator GetTestFileLocator()
		{
			return new BloomFileLocator(new CollectionSettings(), new XMatterPackFinder(new[] { BloomFileLocator.GetInstalledXMatterDirectory() }), ProjectContext.GetFactoryFileLocations(),
				ProjectContext.GetFoundFileLocations(), ProjectContext.GetAfterXMatterFileLocations());
		}


		protected void GetPageOneData()
		{
			_page1Data = GetPageNData(1);
		}

		protected string GetPageNData(int n)
		{
			var data = GetFileData(n + ".xhtml");
			return FixContentForXPathValueSlash(data);
		}

		protected string GetFileData(string fileName)
		{
			return ExportEpubTestsBaseClass.GetZipContent(_epub, Path.GetDirectoryName(_manifestFile) + "/" + fileName);
		}

		internal static XmlNamespaceManager GetNamespaceManager()
		{
			var ns = new XmlNamespaceManager(new NameTable());
			ns.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
			ns.AddNamespace("opf", "http://www.idpf.org/2007/opf");
			ns.AddNamespace("epub", "http://www.idpf.org/2007/ops");
			ns.AddNamespace("xhtml", "http://www.w3.org/1999/xhtml");
			ns.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
			return ns;
		}

		// Do some basic checks and get a path to the main manifest (open package format) file, typically content.opf
		internal static string GetManifestFile(ZipFile zip)
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
		protected virtual ZipFile MakeEpub(string mainFileName, string folderName, Bloom.Book.Book book,
			BookInfo.HowToPublishImageDescriptions howToPublishImageDescriptions = BookInfo.HowToPublishImageDescriptions.None, Action<EpubMaker> extraInit = null)
		{
			var epubFolder = new TemporaryFolder(folderName);
			var epubName = mainFileName + ".epub";
			var epubPath = Path.Combine(epubFolder.FolderPath, epubName);
			using (var maker = CreateEpubMaker(book))
			{
				maker.Unpaginated = true; // Currently we always make unpaginated epubs.
				maker.PublishImageDescriptions = howToPublishImageDescriptions;
				extraInit?.Invoke(maker);
				maker.SaveEpub(epubPath, new NullWebSocketProgress());
			}
			Assert.That(File.Exists(epubPath));
			_epub = new ZipFile(epubPath);
			_manifestFile = ExportEpubTestsBaseClass.GetManifestFile(_epub);
			_manifestContent = StripXmlHeader(GetZipContent(_epub, _manifestFile));
			_manifestDoc = XDocument.Parse(_manifestContent);
			_defaultSourceValue = String.Format("created from Bloom book on {0} with page size A5 Portrait", DateTime.Now.ToString("yyyy-MM-dd"));
			return _epub;
		}

		public string FixContentForXPathValueSlash(string content)
		{
			// xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
			return content.Replace("application/", "application^slash^")
							.Replace(EpubMaker.kCssFolder+"/", kCssSlash)
							.Replace(EpubMaker.kFontsFolder+"/", kFontsSlash)
							.Replace(EpubMaker.kImagesFolder+"/", kImagesSlash)
							.Replace(EpubMaker.kAudioFolder+"/", kAudioSlash);
		}

		private EpubMakerAdjusted CreateEpubMaker(Bloom.Book.Book book)
		{
			return new EpubMakerAdjusted(book, new BookThumbNailer(_thumbnailer.Object), _bookServer);
		}

		internal static string GetZipContent(ZipFile zip, string path)
		{
			var entry = GetZipEntry(zip, path);
			var buffer = new byte[entry.Size];
			var stream = zip.GetInputStream((ZipEntry) entry);
			stream.Read(buffer, 0, (int)entry.Size);
			return Encoding.UTF8.GetString(buffer);
		}

		public static ZipEntry GetZipEntry(ZipFile zip, string path)
		{
			var entry = zip.GetEntry(path);
			Assert.That(entry, Is.Not.Null, "Should have found entry at " + path);
			Assert.That(entry.Name, Is.EqualTo(path), "Expected entry has wrong case");
			return entry;
		}

		public void VerifyEpubItemExists(string path)
		{
			GetZipEntry(_epub, path);
		}

		public void VerifyEpubItemDoesNotExist(string path)
		{
			var entry = _epub.GetEntry(path);
			Assert.That(entry, Is.Null, "Should not have found entry at " + path);
		}

		internal static string StripXmlHeader(string data)
		{
			var index = data.IndexOf("?>");
			if (index > 0)
				return data.Substring(index + 2);
			return data;
		}

		protected void MakeImageFiles(Bloom.Book.Book book, params string[] images)
		{
			foreach (var image in images)
				MakeSamplePngImageWithMetadata(book.FolderPath.CombineForPath(image + ".png"));
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
		/// <param name="extraPageClass"></param>
		/// <param name="extraContent"></param>
		/// <param name="extraContentOutsideTranslationGroup"></param>
		/// <param name="parentDivId"></param>
		/// <param name="extraPages"></param>
		/// <param name="images"></param>
		/// <param name="extraEditGroupClasses"></param>
		/// <param name="extraEditDivClasses"></param>
		/// <param name="defaultLanguages"></param>
		/// <param name="createPhysicalFile"></param>
		/// <param name="optionalDataDiv"></param>
		/// <returns></returns>
		protected Bloom.Book.Book SetupBookLong(string text, string lang, string extraPageClass = " numberedPage' data-page-number='1", string extraContent = "", string extraContentOutsideTranslationGroup = "",
			string parentDivId = "somewrapper", string extraPages = "", string[] images = null,
			string extraEditGroupClasses = "", string extraEditDivClasses = "", string defaultLanguages = "auto", bool createPhysicalFile = false,
			string optionalDataDiv = "", string[] imageDescriptions = null, string extraHeadContent = "")
		{
			if (images == null)
				images = new string[0];
			string imageDivs = "";
			int imgIndex = -1;
			foreach (var image in images)
			{
				++imgIndex;

				string imageDescription = null;
				if (imageDescriptions != null && imgIndex < imageDescriptions.Length)
				{
					imageDescription = imageDescriptions[imgIndex];
				}

				imageDivs += MakeImageContainer(image, imageDescription: imageDescription, descriptionLang: lang);
			}

			string containedImageDivs = "";
			var body = optionalDataDiv + String.Format(@"<div class='bloom-page" + extraPageClass + @"'>
						<div id='" + parentDivId + @"' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs {7}' lang='' data-default-languages='{8}'>
								<div class='bloom-editable {6}' lang='{0}' contenteditable='true'>
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
				lang, text, extraContent, imageDivs, extraContentOutsideTranslationGroup, extraPages, extraEditDivClasses, extraEditGroupClasses, defaultLanguages);
			Bloom.Book.Book book;
			string head = @"<meta charset='UTF-8'/>
				<link rel='stylesheet' href='../settingsCollectionStyles.css'/>
				<link rel='stylesheet' href='basePage.css' type='text/css'/>
				<link rel='stylesheet' href='languageDisplay.css' type='text/css'/>
				<link rel='stylesheet' href='../customCollectionStyles.css'/>
				<link rel='stylesheet' href='customBookStyles.css'/>" + extraHeadContent;
			if (createPhysicalFile)
			{
				book = CreateBookWithPhysicalFile(MakeBookHtml(body, head));
			}
			else
			{
				SetDom(body, head);
				book = CreateBook();
			}
			CreateCommonCssFiles(book);
			s_bookSelection.SelectBook(book);

			// Set up the visibility classes correctly
			book.UpdateEditableAreasOfElement(book.OurHtmlDom);

			return book;
		}

		// Make a standard image container div with the specified source. If a description and language are
		// provided, include a standard image description with content in that language.
		protected static string MakeImageContainer(string src, string imageDescription = null, string descriptionLang = null)
		{
			var imgDesc = "";
			if (imageDescription != null)
			{
				imgDesc = @"<div class='bloom-translationGroup bloom-imageDescription'>"
				          + "<div class='bloom-editable bloom-content1' lang='" + descriptionLang + "'>" + imageDescription + "</div>"
				          + "</div>";
			}

			var imageContainer = "<div class='bloom-imageContainer'><img src='" + src + ".png'></img>" + imgDesc + "</div>\n";
			return imageContainer;
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

		protected override Bloom.Book.Book CreateBook(bool bringBookUpToDate = false)
		{
			var book = base.CreateBook(bringBookUpToDate);
			// Export requires us to have a thumbnail.
			MakeSamplePngImageWithMetadata(book.FolderPath.CombineForPath("thumbnail-256.png"));
			return book;
		}

		protected void CheckBasicsInPage(params string[] images)
		{
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//*[@aria-describedby and not(@id)]");
			// Not sure why we sometimes have these, but validator doesn't like them.
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//*[@lang='']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//xhtml:script", _ns);
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//*[@lang='*']");
			AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//xhtml:div[@contenteditable]", _ns);

			foreach (var image in images)
				AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//img[@src='"+kImagesSlash + image + ".png']");
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='"+kCssSlash+"settingsCollectionStyles.css']", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='"+kCssSlash+"customCollectionStyles.css']", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='"+kCssSlash+"customBookStyles.css']", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:link[@rel='stylesheet' and @href='"+kCssSlash+"fonts.css']", _ns);

			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:body/*[@role]", _ns);
			AssertThatXmlIn.String(_page1Data).HasAtLeastOneMatchForXpath("//xhtml:body/*[@aria-label]", _ns);
		}

		protected void CheckBasicsInManifest(params string[] imageFiles)
		{
			VerifyThatFilesInManifestArePresent();
			var assertThatManifest = AssertThatXmlIn.String(FixContentForXPathValueSlash(_manifestContent));
			assertThatManifest.HasAtLeastOneMatchForXpath("package[@version='3.0']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package[@unique-identifier]");
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:title", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:language", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:identifier", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:source", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='dcterms:modified']");
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/opf:meta[@property='schema:accessMode']", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/opf:meta[@property='schema:accessModeSufficient']", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/opf:meta[@property='schema:accessibilityFeature']", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/opf:meta[@property='schema:accessibilityHazard']", _ns);
			assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/opf:meta[@property='schema:accessibilitySummary']", _ns);

			// This is not absolutely required, but it's true for all our test cases and the way we generate books.
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml']");
			// And that one page must be in the spine
			assertThatManifest.HasAtLeastOneMatchForXpath("package/spine/itemref[@idref='f1']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@properties='nav']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@properties='cover-image']");

			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='settingsCollectionStyles' and @href='"+kCssSlash+"settingsCollectionStyles.css']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='customCollectionStyles' and @href='"+kCssSlash+"customCollectionStyles.css']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='customBookStyles' and @href='"+kCssSlash+"customBookStyles.css']");

			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='AndikaNewBasic-R' and @href='"+kFontsSlash+"AndikaNewBasic-R.ttf' and @media-type='application^slash^vnd.ms-opentype']");
			// This used to be a test that it DOES include the bold (along with italic and BI) variants. But we decided not to...see BL-4202 and comments in EpubMaker.EmbedFonts().
			// So this is now a negative to check that they don't creep back in (unless we change our minds).
			assertThatManifest.HasNoMatchForXpath("package/manifest/item[@id='AndikaNewBasic-B' and @href='"+kFontsSlash+"AndikaNewBasic-B.ttf' and @media-type='application^slash^vnd.ms-opentype']");
			assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='fonts' and @href='"+kCssSlash+"fonts.css']");

			foreach (var image in imageFiles)
				assertThatManifest.HasAtLeastOneMatchForXpath("package/manifest/item[@id='" + image + "' and @href='" + EpubMaker.kImagesFolder+ "^slash^"+ image + ".png']");
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
				ExportEpubTestsBaseClass.GetZipEntry(_epub, Path.GetDirectoryName(_manifestFile) + "/" + file);
			}
		}

		protected void CheckAccessibilityInManifest(bool hasAudio, bool hasImages, string desiredSource, bool hasFullAudio = false)
		{
			var xdoc = new XmlDocument();
			xdoc.LoadXml(_manifestContent);
			var source = xdoc.SelectSingleNode("opf:package/opf:metadata/dc:source", _ns);
			Assert.AreEqual(desiredSource, source.InnerXml);

			var foundTextual = false;
			var foundAudio = false;
			var foundVisual = false;
			var foundOther = false;
			foreach (XmlNode node in xdoc.SelectNodes("opf:package/opf:metadata/opf:meta[@property='schema:accessMode']", _ns))
			{
				switch (node.InnerXml)
				{
				case "textual":		foundTextual = true;	break;
				case "auditory":	foundAudio = true;		break;
				case "visual":		foundVisual = true;		break;
				default:			foundOther = true;		break;
				}
			}
			Assert.IsFalse(foundOther, "Unrecognized accessMode in manifest");
			Assert.IsTrue(foundTextual, "Failed to find expected 'textual' accessMode in manifest");
			Assert.AreEqual(hasAudio, foundAudio, (hasAudio ? "Failed to find expected 'auditory' accessMode in manifest" : "Unexpected 'auditory' accessMode in manifest"));
			Assert.AreEqual(hasImages, foundVisual, (hasImages ? "Failed to find expected 'visual' accessMode in manifest" : "Unexpected 'visual' accessMode in manifest"));

			foreach (XmlNode node in xdoc.SelectNodes("opf:package/opf:metadata/opf:meta[@property='schema:accessModeSufficient']", _ns))
			{
				foundTextual = foundAudio = foundVisual = foundOther = false;
				var modes =  node.InnerXml.Split(',');
				foreach (var mode in modes)
				{
					switch (mode)
					{
					case "textual":		foundTextual = true;	break;
					case "auditory":	foundAudio = true;		break;
					case "visual":		foundVisual = true;		break;
					default:			foundOther = true;		break;
					}
				}
				Assert.IsFalse(foundOther, "Unrecognized mode in accessModeSufficient in manifest");
				if (!hasFullAudio)
					// If hasFullAudio is false, then every accessModeSufficient must contain textual.
					Assert.IsTrue(foundTextual, "Failed to find expected 'textual' in accessModeSufficient in manifest");
				else
					// If hasFullAudio is true, then every accessModeSufficient must contain either textual or auditory (or both).
					Assert.IsTrue(foundTextual || foundAudio, "Failed to find either 'textual' or 'auditory' in accessModeSufficient in manifest");
				if (!hasAudio)
					Assert.IsFalse(foundAudio, "Unexpected 'auditory' in accessModeSufficient in manifest");
				if (!hasImages)
					Assert.IsFalse(foundVisual, "Unexpected 'visual' in accessModeSufficient in manifest");
			}

			foundOther = false;
			bool foundSynchronizedAudio = false;
			bool foundResizeText = false;
			bool foundPageNumbers = false;
			bool foundUnlocked = false;
			bool foundReadingOrder = false;
			bool foundTableOfContents = false;
			foreach (XmlNode node in xdoc.SelectNodes("opf:package/opf:metadata/opf:meta[@property='schema:accessibilityFeature']", _ns))
			{
				switch (node.InnerXml)
				{
				case "synchronizedAudioText":				foundSynchronizedAudio = true;	break;
				case "displayTransformability/resizeText":	foundResizeText = true;			break;
				case "printPageNumbers":					foundPageNumbers = true;		break;
				case "unlocked":							foundUnlocked = true;			break;
				case "readingOrder":						foundReadingOrder = true;		break;
				case "tableOfContents":						foundTableOfContents = true;	break;
				default:									foundOther = true;				break;
				}
			}
			Assert.IsFalse(foundOther, "Unrecognized accessibilityFeature value in manifest");
			Assert.AreEqual(hasAudio, foundSynchronizedAudio, "Bloom Audio is synchronized iff it exists (which it does{0}) [manifest accessibilityFeature]", hasAudio ? "" : " not");
			Assert.IsTrue(foundResizeText, "Bloom text should always be resizable [manifest accessibilityFeature]");
			Assert.IsTrue(foundPageNumbers, "Bloom books provide page number mapping to the print edition [manifest accessibilityFeature]");
			Assert.IsTrue(foundUnlocked, "Bloom books are always unlocked [manifest accessibilityFeature]");
			Assert.IsTrue(foundReadingOrder, "Bloom books have simple formats that are always in reading order [manifest accessibilityFeature]");
			Assert.IsTrue(foundTableOfContents, "Bloom books have a trivial table of contents [manifest accessibilityFeature]");

			foundOther = false;
			bool foundNone = false;
			bool foundNoMotionHazard = false;
			bool foundNoFlashingHazard = false;
			foreach (XmlNode node in xdoc.SelectNodes("opf:package/opf:metadata/opf:meta[@property='schema:accessibilityHazard']", _ns))
			{
				switch (node.InnerXml)
				{
				case "none":						foundNone = true;				break;
				case "noMotionSimulationHazard":	foundNoMotionHazard = true;		break;
				case "noFlashingHazard":			foundNoFlashingHazard = true;	break;
				default:							foundOther = true;				break;
				}
			}
			Assert.IsFalse(foundOther, "Unrecognized accessibilityHazard value in manifest");
			if (hasAudio)
			{
				Assert.IsFalse(foundNone, "If we have Audio, then accessibilityHazard is not 'none' in manifest");
				Assert.IsTrue(foundNoMotionHazard, "If we have Audio, then accessibilityHazard includes 'noMotionHazard' in manifest");
				Assert.IsTrue(foundNoFlashingHazard, "If we have Audio, then accessibilityHazard includes 'noFlashingHazard' in manifest");
			}
			else
			{
				Assert.IsTrue(foundNone, "If we do not have Audio, then accessibilityHazard is 'none' in manifest");
				Assert.IsFalse(foundNoMotionHazard, "If we do not have Audio, then accessibilityHazard is 'none', not 'noMotionHazard' in manifest");
				Assert.IsFalse(foundNoFlashingHazard, "If we do not have Audio, then accessibilityHazard is 'none', not 'noFlashingHazard' in manifest");
			}

			int summaryCount = 0;
			foreach (XmlNode node in xdoc.SelectNodes("opf:package/opf:metadata/opf:meta[@property='schema:accessibilitySummary']", _ns))
			{
				++summaryCount;
			}
			Assert.AreEqual(1, summaryCount, "Expected a single accessibilitySummary item in the manifest, but found {0}", summaryCount);
		}

		protected void CheckFolderStructure()
		{
			var zipped = _epub.GetEnumerator();
			while (zipped.MoveNext())
			{
				var entry = zipped.Current as ZipEntry;
				Assert.IsNotNull(entry);
				var path = entry.Name.Split('/');
				var ext = Path.GetExtension(entry.Name);
				switch (ext.ToLowerInvariant())
				{
				case ".css":
					Assert.AreEqual(3, path.Length);
					Assert.AreEqual("content", path[0]);
					Assert.AreEqual(EpubMaker.kCssFolder, path[1]);
					break;
				case ".png":
				case ".svg":
				case ".jpg":
					Assert.AreEqual(3, path.Length);
					Assert.AreEqual("content", path[0]);
					Assert.AreEqual(EpubMaker.kImagesFolder, path[1]);
					break;
				case ".mp3":
					Assert.AreEqual(3, path.Length);
					Assert.AreEqual("content", path[0]);
					Assert.AreEqual(EpubMaker.kAudioFolder, path[1]);
					break;
				case ".mp4":
					Assert.AreEqual(3, path.Length);
					Assert.AreEqual("content", path[0]);
					if (path[1] != EpubMaker.kAudioFolder)
						Assert.AreEqual("video", path[1]);
					break;
				case ".ttf":
					Assert.AreEqual(3, path.Length);
					Assert.AreEqual("content", path[0]);
					Assert.AreEqual(EpubMaker.kFontsFolder, path[1]);
					break;
				case ".xhtml":
				case ".smil":
				case ".opf":
					Assert.AreEqual(2, path.Length);
					Assert.AreEqual("content", path[0]);
					break;
				case ".xml":
					Assert.AreEqual(2, path.Length);
					Assert.AreEqual("META-INF", path[0]);
					Assert.AreEqual("container.xml", path[1]);
					break;
				default:
					Assert.AreEqual("", ext);
					Assert.AreEqual(1, path.Length);
					Assert.AreEqual("mimetype", path[0]);
					break;
				}
			}
		}
	}
}
