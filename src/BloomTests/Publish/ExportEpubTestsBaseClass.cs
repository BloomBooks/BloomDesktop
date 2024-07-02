using System;
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
using Bloom.Publish.Epub;
using Bloom.SafeXml;
using Bloom.web;
using BloomTemp;
using BloomTests.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.Extensions;
using SIL.Progress;

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
        protected static BloomServer s_testServer;
        protected static BookSelection s_bookSelection;
        protected static CollectionSettings s_collectionSettings;
        protected BookServer _bookServer;
        protected string _defaultSourceValue;

        protected const string kAudioSlash = EpubMaker.kAudioFolder + "^slash^";
        protected const string kCssSlash = EpubMaker.kCssFolder + "^slash^";
        protected const string kFontsSlash = EpubMaker.kFontsFolder + "^slash^";
        protected const string kImagesSlash = EpubMaker.kImagesFolder + "^slash^";
        protected const string kVideoSlash = EpubMaker.kVideoFolder + "^slash^";

#if __MonoCS__
        protected const int kMakeEpubTrials = 2; // try twice: that should be enough for the tests.
#else
        protected const int kMakeEpubTrials = 1; // try only once
#endif

        [OneTimeSetUp]
        public virtual void OneTimeSetup()
        {
            s_collectionSettings = new CollectionSettings();
            s_testServer = GetTestServer();
        }

        [OneTimeTearDown]
        public virtual void OneTimeTearDown()
        {
            s_testServer.Dispose();
        }

        internal static BloomServer GetTestServer()
        {
            var server = new BloomServer(
                new RuntimeImageProcessor(new BookRenamedEvent()),
                GetTestBookSelection(),
                s_collectionSettings,
                GetTestFileLocator()
            );
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
            return new BloomFileLocator(
                s_collectionSettings,
                new XMatterPackFinder(new[] { BloomFileLocator.GetFactoryXMatterDirectory() }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
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
            return ExportEpubTestsBaseClass.GetZipContent(
                _epub,
                Path.GetDirectoryName(_manifestFile) + "/" + fileName
            );
        }

        protected void VerifyThatPageNDoesNotExist(int n)
        {
            var path = String.Format("{0}/{1}.xhtml", Path.GetDirectoryName(_manifestFile), n);
            var entry = _epub.GetEntry(path);
            Assert.That(entry, Is.Null, "Should not have found entry at " + path);
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
            return doc.Root
                .Element(ns + "rootfiles")
                .Element(ns + "rootfile")
                .Attribute("full-path")
                .Value;
        }

        /// <summary>
        /// Make an ePUB out of the specified book. Sets up several instance variables with commonly useful parts of the results.
        /// </summary>
        /// <returns></returns>
        protected virtual ZipFile MakeEpub(
            string mainFileName,
            string folderName,
            Bloom.Book.Book book,
            BookInfo.HowToPublishImageDescriptions howToPublishImageDescriptions =
                BookInfo.HowToPublishImageDescriptions.None,
            string branding = "Default",
            Action<EpubMaker> extraInit = null,
            bool unpaginated = true
        )
        {
            book.CollectionSettings.BrandingProjectKey = branding;

            // BringBookUpToDate is done on entering the Publish tab, outside the scope of these tests.
            // But note that it must be done AFTER setting the branding (which in Bloom will happen well before
            // entering the Publish tab).
            book.BringBookUpToDate(new NullProgress());
            var epubFolder = new TemporaryFolder(folderName);
            var epubName = mainFileName + ".epub";
            var epubPath = Path.Combine(epubFolder.FolderPath, epubName);
            using (var maker = CreateEpubMaker(book))
            {
                maker.Unpaginated = unpaginated;
                maker.PublishImageDescriptions = howToPublishImageDescriptions;
                extraInit?.Invoke(maker);
                maker.SaveEpub(epubPath, new NullWebSocketProgress());
            }
            Assert.That(File.Exists(epubPath));
            _epub = new ZipFile(epubPath);
            _manifestFile = ExportEpubTestsBaseClass.GetManifestFile(_epub);
            _manifestContent = StripXmlHeader(GetZipContent(_epub, _manifestFile));
            _manifestDoc = XDocument.Parse(_manifestContent);
            _defaultSourceValue =
                $"created from Bloom book on {DateTime.Now:yyyy-MM-dd} with page size A5 Portrait";
            return _epub;
        }

        /// <summary>
        /// Try the number of times given to make the ePUB before letting an ApplicationException be thrown.
        /// </summary>
        /// <remarks>
        /// The ExportEpubTests (including ExportEpubWithVideoTests and EpubValidAndAccessible) fail rather
        /// consistently (but not always) on Linux even though they always succeed on Windows.  The failure
        /// mode is always having an ApplicationException thrown from inside PublishHelper.IsDisplayed
        /// while making the ePUB with the message "Failure to completely load visibility document in
        /// RemoveUnwantedContent".  Converting all three subclasses to single class removed this failure
        /// mode on Linux, but introduced a failure mode on Windows where every test run reported two
        /// failures in this set of tests.  Various attempts with lock on Mutex on the MakeEpub method
        /// had little to no effect.  Implementing this retry method is the only solution that I've come
        /// up with that appears to work on both platforms.  A test run of 20 times through all the export
        /// ePUB tests showed that no more than two tries were ever needed to succeed, but two tries were
        /// needed at least once per run on average.
        /// </remarks>
        protected ZipFile MakeEpubWithRetries(
            int maxRetry,
            string mainFileName,
            string folderName,
            Bloom.Book.Book book,
            BookInfo.HowToPublishImageDescriptions howToPublishImageDescriptions =
                BookInfo.HowToPublishImageDescriptions.None,
            string branding = "Default",
            Action<EpubMaker> extraInit = null
        )
        {
            ZipFile zipFile = null;
            for (int i = 1; i <= maxRetry; ++i)
            {
                try
                {
                    zipFile = MakeEpub(
                        mainFileName,
                        folderName,
                        book,
                        howToPublishImageDescriptions,
                        branding,
                        extraInit
                    );
                    if (i > 1)
                        Console.WriteLine(
                            $"MakeEpub(\"{mainFileName}\",\"{folderName}\",...) succeeded on try number {i} to load the visibility document."
                        );
                    break;
                }
                catch (ApplicationException e)
                {
                    if (
                        e.Message
                        == "Failure to completely load visibility document in RemoveUnwantedContent"
                    )
                    {
                        if (i < maxRetry)
                            continue;
                        Console.WriteLine(
                            $"MakeEpub(\"{mainFileName}\",\"{folderName}\",...) failed {maxRetry} times to complete loading the visibility document."
                        );
                    }
                    throw;
                }
            }
            return zipFile;
        }

        public string FixContentForXPathValueSlash(string content)
        {
            // xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
            return content
                .Replace("application/", "application^slash^")
                .Replace(EpubMaker.kCssFolder + "/", kCssSlash)
                .Replace(EpubMaker.kFontsFolder + "/", kFontsSlash)
                .Replace(EpubMaker.kImagesFolder + "/", kImagesSlash)
                .Replace(EpubMaker.kAudioFolder + "/", kAudioSlash)
                .Replace(EpubMaker.kVideoFolder + "/", kVideoSlash);
        }

        private EpubMakerAdjusted CreateEpubMaker(Bloom.Book.Book book)
        {
            return new EpubMakerAdjusted(
                book,
                new BookThumbNailer(_thumbnailer.Object),
                _bookServer
            );
        }

        internal static string GetZipContent(ZipFile zip, string path)
        {
            var entry = GetZipEntry(zip, path);
            var buffer = new byte[entry.Size];
            var stream = zip.GetInputStream((ZipEntry)entry);
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

        protected void MakeVideoFiles(Bloom.Book.Book book, params string[] videos)
        {
            foreach (var video in videos)
            {
                var path = Path.Combine(book.FolderPath, "video", video + ".mp4");
                Directory.CreateDirectory(book.FolderPath.CombineForPath("video"));
                // first 16 bytes from an actual mp4 file.
                File.WriteAllBytes(
                    path,
                    new byte[]
                    {
                        0x00,
                        0x00,
                        0x00,
                        0x18,
                        0x66,
                        0x74,
                        0x79,
                        0x70,
                        0x69,
                        0x73,
                        0x6f,
                        0x6d,
                        0x00,
                        0x00,
                        0x00,
                        0x00
                    }
                );
            }
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
        protected Bloom.Book.Book SetupBookLong(
            string text,
            string lang,
            string extraPageClass = " numberedPage' data-page-number='1",
            string extraContent = "",
            string extraContentOutsideTranslationGroup = "",
            string parentDivId = "somewrapper",
            string extraPages = "",
            string[] images = null,
            string extraEditGroupClasses = "",
            string extraEditDivClasses = "",
            string defaultLanguages = "auto",
            bool createPhysicalFile = false,
            string optionalDataDiv = "",
            string[] imageDescriptions = null,
            string extraHeadContent = ""
        )
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

                imageDivs += MakeImageContainer(
                    image,
                    imageDescription: imageDescription,
                    descriptionLang: lang
                );
            }

            var body =
                optionalDataDiv
                + $@"<div class='bloom-page{extraPageClass}'>
						<div id='{parentDivId}' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs {extraEditGroupClasses}' lang='' data-default-languages='{defaultLanguages}'>
								<div class='bloom-editable {extraEditDivClasses}' lang='{lang}' contenteditable='true'>
									{text}
								</div>
								{extraContent}
								<div lang = '*'>more text</div>
							</div>
							{imageDivs}
							{extraContentOutsideTranslationGroup}
						</div>
					</div>
					{extraPages}";

            return CreateTestBook(body, createPhysicalFile, extraHeadContent);
        }

        protected Bloom.Book.Book CreateTestBook(
            string body,
            bool createPhysicalFile = false,
            string extraHeadContent = ""
        )
        {
            Bloom.Book.Book book;
            string head =
                @"<meta charset='UTF-8'/>
				<link rel='stylesheet' href='defaultLangStyles.css'/>
                <link rel='stylesheet' href='appearance.css' type='text/css'/>
				<link rel='stylesheet' href='basePage.css' type='text/css'/>
				<link rel='stylesheet' href='customCollectionStyles.css' type='text/css'/>
				<link rel='stylesheet' href='Basic Book.css' type='text/css'></link>
				<link rel='stylesheet' href='Device-XMatter.css' type='text/css'></link>
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

            book.CollectionSettings.XMatterPackName = "Device"; // give us predictable xmatter with content on page 2

            return book;
        }

        // Make a standard image container div with the specified source. If a description and language are
        // provided, include a standard image description with content in that language.
        protected static string MakeImageContainer(
            string src,
            string imageDescription = null,
            string descriptionLang = null
        )
        {
            var imgDesc = "";
            if (imageDescription != null)
            {
                imgDesc =
                    @"<div class='bloom-translationGroup bloom-imageDescription'>"
                    + "<div class='bloom-editable bloom-content1' lang='"
                    + descriptionLang
                    + "'>"
                    + imageDescription
                    + "</div>"
                    + "</div>";
            }

            var imageContainer =
                "<div class='bloom-imageContainer'><img src='"
                + src
                + ".png'></img>"
                + imgDesc
                + "</div>\n";
            return imageContainer;
        }

        // Set up some typical CSS files we DO want to include, even in 'unpaginated' mode
        private static void CreateCommonCssFiles(Bloom.Book.Book book)
        {
            var settingsCollectionPath = Path.Combine(book.FolderPath, "defaultLangStyles.css");
            File.WriteAllText(settingsCollectionPath, "body:{font-family: 'Andika';}");
            var customCollectionPath = Path.Combine(book.FolderPath, "customCollectionStyles.css");
            File.WriteAllText(customCollectionPath, "body:{font-family: 'Andika';}");
            var customBookPath = Path.Combine(book.FolderPath, "customBookStyles.css");
            File.WriteAllText(customBookPath, "body:{font-family: 'Andika';}");
        }

        protected override Bloom.Book.Book CreateBook(bool bringBookUpToDate = false)
        {
            var book = base.CreateBook(bringBookUpToDate);
            // Export requires us to have a thumbnail.
            MakeSamplePngImageWithMetadata(book.FolderPath.CombineForPath("thumbnail-256.png"));
            return book;
        }

        protected void CheckBasicsInGivenPage(int pageNumber, params string[] images)
        {
            var pageData = pageNumber == 1 ? _page1Data : GetPageNData(pageNumber);
            AssertThatXmlIn
                .String(pageData)
                .HasNoMatchForXpath("//*[@aria-describedby and not(@id)]");
            // Not sure why we sometimes have these, but validator doesn't like them.
            AssertThatXmlIn.String(pageData).HasNoMatchForXpath("//*[@lang='']");
            AssertThatXmlIn.String(pageData).HasNoMatchForXpath("//xhtml:script", _ns);
            AssertThatXmlIn.String(pageData).HasNoMatchForXpath("//*[@lang='*']");
            AssertThatXmlIn
                .String(pageData)
                .HasNoMatchForXpath("//xhtml:div[@contenteditable]", _ns);

            foreach (var image in images)
                AssertThatXmlIn
                    .String(pageData)
                    .HasAtLeastOneMatchForXpath("//img[@src='" + kImagesSlash + image + ".png']");
            AssertThatXmlIn
                .String(pageData)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:link[@rel='stylesheet' and @href='"
                        + kCssSlash
                        + "defaultLangStyles.css']",
                    _ns
                );
            AssertThatXmlIn
                .String(pageData)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:link[@rel='stylesheet' and @href='"
                        + kCssSlash
                        + "customCollectionStyles.css']",
                    _ns
                );
            AssertThatXmlIn
                .String(pageData)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:link[@rel='stylesheet' and @href='"
                        + kCssSlash
                        + "customBookStyles.css']",
                    _ns
                );
            AssertThatXmlIn
                .String(pageData)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:link[@rel='stylesheet' and @href='" + kCssSlash + "fonts.css']",
                    _ns
                );

            AssertThatXmlIn
                .String(pageData)
                .HasAtLeastOneMatchForXpath("//xhtml:body/*[@role]", _ns);
            AssertThatXmlIn
                .String(pageData)
                .HasAtLeastOneMatchForXpath("//xhtml:body/*[@aria-label]", _ns);
        }

        protected void CheckBasicsInPage(params string[] images)
        {
            CheckBasicsInGivenPage(1, images);
        }

        protected void CheckBasicsInManifest(params string[] imageFiles)
        {
            VerifyThatFilesInManifestArePresent();
            var assertThatManifest = AssertThatXmlIn.String(
                FixContentForXPathValueSlash(_manifestContent)
            );
            assertThatManifest.HasAtLeastOneMatchForXpath("package[@version='3.0']");
            assertThatManifest.HasAtLeastOneMatchForXpath("package[@unique-identifier]");
            assertThatManifest.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:title", _ns);
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/dc:language",
                _ns
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/dc:identifier",
                _ns
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/dc:source",
                _ns
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/metadata/meta[@property='dcterms:modified']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/opf:meta[@property='schema:accessMode']",
                _ns
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/opf:meta[@property='schema:accessModeSufficient']",
                _ns
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/opf:meta[@property='schema:accessibilityFeature']",
                _ns
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/opf:meta[@property='schema:accessibilityHazard']",
                _ns
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "opf:package/opf:metadata/opf:meta[@property='schema:accessibilitySummary']",
                _ns
            );

            // This is not absolutely required, but it's true for all our test cases and the way we generate books.
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f1' and @href='1.xhtml']"
            );
            // And that one page must be in the spine
            assertThatManifest.HasAtLeastOneMatchForXpath("package/spine/itemref[@idref='f1']");
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@properties='nav']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@properties='cover-image']"
            );

            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='defaultLangStyles' and @href='"
                    + kCssSlash
                    + "defaultLangStyles.css']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='customCollectionStyles' and @href='"
                    + kCssSlash
                    + "customCollectionStyles.css']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='customBookStyles' and @href='"
                    + kCssSlash
                    + "customBookStyles.css']"
            );

            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='Andika-Regular' and @href='"
                    + kFontsSlash
                    + "Andika-Regular.woff2' and @media-type='application^slash^font-woff2']"
            );
            // This used to be a test that it DOES include the bold (along with italic and BI) variants. But we decided not to...see BL-4202 and comments in EpubMaker.EmbedFonts().
            // So this is now a negative to check that they don't creep back in (unless we change our minds).
            assertThatManifest.HasNoMatchForXpath(
                "package/manifest/item[@id='Andika-Bold' and @href='"
                    + kFontsSlash
                    + "Andika-Bold.woff2' and @media-type='application^slash^font-woff2']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='fonts' and @href='" + kCssSlash + "fonts.css']"
            );

            foreach (var image in imageFiles)
                assertThatManifest.HasAtLeastOneMatchForXpath(
                    "package/manifest/item[@id='"
                        + image
                        + "' and @href='"
                        + EpubMaker.kImagesFolder
                        + "^slash^"
                        + image
                        + ".png']"
                );
        }

        /// <summary>
        /// Check that all the files referenced in the manifest are actually present in the zip.
        /// </summary>
        void VerifyThatFilesInManifestArePresent()
        {
            XNamespace opf = "http://www.idpf.org/2007/opf";
            var files = _manifestDoc.Root
                .Element(opf + "manifest")
                .Elements(opf + "item")
                .Select(item => item.Attribute("href").Value);
            foreach (var file in files)
            {
                ExportEpubTestsBaseClass.GetZipEntry(
                    _epub,
                    Path.GetDirectoryName(_manifestFile) + "/" + file
                );
            }
        }

        protected void CheckAccessibilityInManifest(
            bool hasAudio,
            bool hasImages,
            bool hasVideo,
            string desiredSource,
            bool hasFullAudio = false
        )
        {
            var xdoc = SafeXmlDocument.Create();
            xdoc.LoadXml(_manifestContent);
            var source = xdoc.SelectSingleNode("opf:package/opf:metadata/dc:source", _ns);
            Assert.AreEqual(desiredSource, source.InnerXml);

            // Check accessMode section
            var foundTextual = false;
            var foundAudio = false;
            var foundVisual = false;
            var foundOther = false;
            foreach (
                var node in xdoc.SafeSelectNodes(
                    "opf:package/opf:metadata/opf:meta[@property='schema:accessMode']",
                    _ns
                )
            )
            {
                switch (node.InnerXml)
                {
                    case "textual":
                        foundTextual = true;
                        break;
                    case "auditory":
                        foundAudio = true;
                        break;
                    case "visual":
                        foundVisual = true;
                        break;
                    default:
                        foundOther = true;
                        break;
                }
            }
            Assert.IsFalse(foundOther, "Unrecognized accessMode in manifest");
            Assert.IsTrue(foundTextual, "Failed to find expected 'textual' accessMode in manifest");
            Assert.AreEqual(
                hasAudio,
                foundAudio,
                hasAudio
                    ? "Failed to find expected 'auditory' accessMode in manifest"
                    : "Unexpected 'auditory' accessMode in manifest"
            );
            Assert.AreEqual(
                hasImages || hasVideo,
                foundVisual,
                hasImages || hasVideo
                    ? "Failed to find expected 'visual' accessMode in manifest"
                    : "Unexpected 'visual' accessMode in manifest"
            );

            foreach (
                var node in xdoc.SafeSelectNodes(
                    "opf:package/opf:metadata/opf:meta[@property='schema:accessModeSufficient']",
                    _ns
                )
            )
            {
                foundTextual = foundAudio = foundVisual = foundOther = false;
                var modes = node.InnerXml.Split(',');
                foreach (var mode in modes)
                {
                    switch (mode)
                    {
                        case "textual":
                            foundTextual = true;
                            break;
                        case "auditory":
                            foundAudio = true;
                            break;
                        case "visual":
                            foundVisual = true;
                            break;
                        default:
                            foundOther = true;
                            break;
                    }
                }
                Assert.IsFalse(foundOther, "Unrecognized mode in accessModeSufficient in manifest");
                if (!hasFullAudio)
                    // If hasFullAudio is false, then every accessModeSufficient must contain textual.
                    Assert.IsTrue(
                        foundTextual,
                        "Failed to find expected 'textual' in accessModeSufficient in manifest"
                    );
                else
                    // If hasFullAudio is true, then every accessModeSufficient must contain either textual or auditory (or both).
                    Assert.IsTrue(
                        foundTextual || foundAudio,
                        "Failed to find either 'textual' or 'auditory' in accessModeSufficient in manifest"
                    );
                if (!hasAudio)
                    Assert.IsFalse(
                        foundAudio,
                        "Unexpected 'auditory' in accessModeSufficient in manifest"
                    );
                if (!hasImages)
                    Assert.IsFalse(
                        foundVisual,
                        "Unexpected 'visual' in accessModeSufficient in manifest"
                    );
            }

            // Check accessibilityFeature section
            foundOther = false;
            var foundSynchronizedAudio = false;
            var foundDisplayTransformability = false;
            var foundPageNumbers = false;
            var foundUnlocked = false;
            var foundReadingOrder = false;
            var foundTableOfContents = false;

            foreach (
                var node in xdoc.SafeSelectNodes(
                    "opf:package/opf:metadata/opf:meta[@property='schema:accessibilityFeature']",
                    _ns
                )
            )
            {
                switch (node.InnerXml)
                {
                    case "synchronizedAudioText":
                        foundSynchronizedAudio = true;
                        break;
                    case "displayTransformability":
                        foundDisplayTransformability = true;
                        break;
                    case "printPageNumbers":
                        foundPageNumbers = true;
                        break;
                    case "unlocked":
                        foundUnlocked = true;
                        break;
                    case "readingOrder":
                        foundReadingOrder = true;
                        break;
                    case "tableOfContents":
                        foundTableOfContents = true;
                        break;

                    // Other accessbilityFeature values that we recognize, but don't need to process in this test
                    case "signLanguage":
                    case "alternativeText":
                        break;

                    default:
                        foundOther = true;
                        break;
                }
            }
            Assert.IsFalse(foundOther, "Unrecognized accessibilityFeature value in manifest");
            Assert.AreEqual(
                hasAudio,
                foundSynchronizedAudio,
                "Bloom Audio is synchronized iff it exists (which it does{0}) [manifest accessibilityFeature]",
                hasAudio ? "" : " not"
            );
            Assert.IsTrue(
                foundDisplayTransformability,
                "Bloom text should always be displayTransformability [manifest accessibilityFeature]"
            );
            Assert.IsTrue(
                foundPageNumbers,
                "Bloom books provide page number mapping to the print edition [manifest accessibilityFeature]"
            );
            Assert.IsTrue(
                foundUnlocked,
                "Bloom books are always unlocked [manifest accessibilityFeature]"
            );
            Assert.IsTrue(
                foundReadingOrder,
                "Bloom books have simple formats that are always in reading order [manifest accessibilityFeature]"
            );
            Assert.IsTrue(
                foundTableOfContents,
                "Bloom books have a trivial table of contents [manifest accessibilityFeature]"
            );

            // Check accessibilityHazard section
            foundOther = false;
            var foundNone = false;
            var foundMotionHazard = false;
            var foundFlashingHazard = false;
            var foundSoundHazard = false;
            var foundNoMotionHazard = false;
            var foundNoFlashingHazard = false;
            var foundNoSoundHazard = false;
            var foundUnknown = false;
            foreach (
                var node in xdoc.SafeSelectNodes(
                    "opf:package/opf:metadata/opf:meta[@property='schema:accessibilityHazard']",
                    _ns
                )
            )
            {
                switch (node.InnerXml)
                {
                    case "none":
                        foundNone = true;
                        break;
                    case "motionSimulationHazard":
                        foundMotionHazard = true;
                        break;
                    case "flashingHazard":
                        foundFlashingHazard = true;
                        break;
                    case "soundHazard":
                        foundSoundHazard = true;
                        break;
                    case "noMotionSimulationHazard":
                        foundNoMotionHazard = true;
                        break;
                    case "noFlashingHazard":
                        foundNoFlashingHazard = true;
                        break;
                    case "noSoundHazard":
                        foundNoSoundHazard = true;
                        break;
                    case "unknown":
                        foundUnknown = true;
                        break;
                    default:
                        foundOther = true;
                        break;
                }
            }
            Assert.IsFalse(foundOther, "Unrecognized accessibilityHazard value in manifest");
            // Not much to check here, since we want the hazard manifest based entirely on user input.
            Assert.IsFalse(
                foundNoSoundHazard && foundNoFlashingHazard && foundNoMotionHazard,
                "'none' is recommended instead of listing all 3 noXXXHazard values separately"
            );
            Assert.IsFalse(
                foundSoundHazard || foundNoSoundHazard,
                "Sound hazards are not checked at present."
            );
            var definite =
                foundMotionHazard
                || foundNoMotionHazard
                || foundFlashingHazard
                || foundNoFlashingHazard;
            Assert.IsTrue(
                foundUnknown || foundNone || definite,
                "Something should be stated about hazards"
            );
            Assert.IsFalse(
                foundUnknown && foundNone,
                "Cannot have both unknown and none for hazards"
            );
            Assert.IsFalse(
                foundUnknown && definite,
                "Cannot have both unknown and a definite hazard"
            );
            Assert.IsFalse(foundNone && definite, "Cannot have both none and a definite hazard");

            var summaryCount = 0;
            foreach (
                var unused in xdoc.SafeSelectNodes(
                    "opf:package/opf:metadata/opf:meta[@property='schema:accessibilitySummary']",
                    _ns
                )
            )
            {
                ++summaryCount;
            }
            Assert.AreEqual(
                1,
                summaryCount,
                "Expected a single accessibilitySummary item in the manifest, but found {0}",
                summaryCount
            );
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
                            Assert.AreEqual(EpubMaker.kVideoFolder, path[1]);
                        break;
                    case ".ttf":
                    case ".woff":
                    case ".woff2":
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
