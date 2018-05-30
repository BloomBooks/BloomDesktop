using System;
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
			return GetFileData(n + ".xhtml");
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
			EpubMaker.ImageDescriptionPublishing howToPublishImageDescriptions = EpubMaker.ImageDescriptionPublishing.None, Action<EpubMaker> extraInit = null)
		{
			var epubFolder = new TemporaryFolder(folderName);
			var epubName = mainFileName + ".epub";
			var epubPath = Path.Combine(epubFolder.FolderPath, epubName);
			using (var maker = CreateEpubMaker(book))
			{
				maker.Unpaginated = true; // Currently we always make unpaginated epubs.
				maker.PublishImageDescriptions = howToPublishImageDescriptions;
				extraInit?.Invoke(maker);
				maker.SaveEpub(epubPath);
			}
			Assert.That(File.Exists(epubPath));
			_epub = new ZipFile(epubPath);
			_manifestFile = ExportEpubTestsBaseClass.GetManifestFile(_epub);
			_manifestContent = StripXmlHeader(GetZipContent(_epub, _manifestFile));
			_manifestDoc = XDocument.Parse(_manifestContent);
			_defaultSourceValue = String.Format("created from Bloom book on {0} with page size A5 Portrait", DateTime.Now.ToString("yyyy-MM-dd"));
			return _epub;
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

		public void VerifyEntryExists(string fileName)
		{
			GetZipEntry(_epub, Path.GetDirectoryName(_manifestFile) + "/" + fileName);
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
			string optionalDataDiv = "", string[] imageDescriptions = null)
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
				<link rel='stylesheet' href='customBookStyles.css'/>";
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
	}
}
