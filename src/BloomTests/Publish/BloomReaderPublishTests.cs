using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.FontProcessing;
using Bloom.ImageProcessing;
using Bloom.Publish;
using Bloom.Publish.BloomPub;
using Bloom.SafeXml;
using Bloom.web;
using BloomTests.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.TestUtilities;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace BloomTests.Publish
{
    class BloomReaderPublishTests : BookTestsBase
    {
        private BookServer _bookServer;
        protected BloomServer s_bloomServer;

        [OneTimeSetUp]
        public virtual void OneTimeSetup()
        {
            var settings = new CollectionSettings();
            var locator = new BloomFileLocator(
                settings,
                new XMatterPackFinder(new[] { BloomFileLocator.GetFactoryXMatterDirectory() }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            s_bloomServer = new BloomServer(
                new RuntimeImageProcessor(new BookRenamedEvent()),
                new BookSelection(),
                settings,
                locator
            );
            s_bloomServer.StartListening();
        }

        [OneTimeTearDown]
        public virtual void OneTimeTearDown()
        {
            s_bloomServer.Dispose();
        }

        [SetUp]
        public void SetupTest()
        {
            _bookServer = CreateBookServer();
        }

        private const string kMinimumValidBookHtml =
            @"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css'></link></head><body>
					<div class='bloom-page' id='guid1'></div>
			</body></html>";

        [Test]
        public void CompressBookForDevice_FileNameIsCorrect()
        {
            var testBook = CreateBookWithPhysicalFile(
                kMinimumValidBookHtml,
                bringBookUpToDate: true
            );

            using (
                var bloomdTempFile = TempFile.WithFilenameInTempFolder(
                    testBook.Title + BloomPubMaker.BloomPubExtensionWithDot
                )
            )
            {
                BloomPubMaker.CreateBloomPub(
                    new BloomPubPublishSettings(),
                    bloomdTempFile.Path,
                    testBook,
                    _bookServer,
                    new NullWebSocketProgress()
                );
                Assert.AreEqual(
                    testBook.Title + BloomPubMaker.BloomPubExtensionWithDot,
                    Path.GetFileName(bloomdTempFile.Path)
                );
            }
        }

        [Test]
        public void CompressBookForDevice_IncludesWantedFiles()
        {
            var wantedFiles = new List<string>()
            {
                "thumbnail.png", // should be left alone
                "previewMode.css",
                "meta.json", // should be left alone
                "readerStyles.css", // gets added
                "Device-XMatter.css", // added when we apply this xmatter
                "customCollectionStyles.css", // should be copied from parent directory
                "defaultLangStyles.css"
            };
            string collectionStylesPath = null;

            TestHtmlAfterCompression(
                kMinimumValidBookHtml,
                actionsOnFolderBeforeCompressing: folderPath =>
                {
                    // These png files have to be real; just putting some text in it leads to out-of-memory failures when Bloom
                    // tries to make its background transparent.
                    File.Copy(
                        SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                            _pathToTestImages,
                            "shirt.png"
                        ),
                        Path.Combine(folderPath, "thumbnail.png")
                    );
                    File.WriteAllText(
                        Path.Combine(folderPath, "previewMode.css"),
                        @"This is wanted"
                    );
                    collectionStylesPath = Path.Combine(
                        Path.GetDirectoryName(folderPath),
                        "customCollectionStyles.css"
                    );
                    File.WriteAllText(collectionStylesPath, "color: red");
                },
                assertionsOnResultingHtmlString: html =>
                {
                    // These two files get moved/created into the book folder, the links must get fixed
                    Assert.That(html, Does.Contain("href=\"customCollectionStyles.css\""));
                    Assert.That(html, Does.Contain("href=\"defaultLangStyles.css\""));
                    // The parent folder doesn't go with the book, so we shouldn't be referencing anything there
                    Assert.That(html, Does.Not.Contain("href=\"../"));
                },
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    foreach (var name in wantedFiles)
                    {
                        Assert.AreNotEqual(
                            -1,
                            zip.FindEntry(Path.GetFileName(name), true),
                            "expected " + name + " to be part of .bloompub zip"
                        );
                    }
                    // A convenient place to check defaults on meta.json
                    var meta = BookMetaData.FromString(GetEntryContents(zip, "meta.json"));
                    Assert.That(meta.Feature_SignLanguage, Is.False);
                    Assert.That(meta.Feature_TalkingBook, Is.False);
                    Assert.That(meta.Feature_Motion, Is.False);
                    Assert.That(meta.BloomdVersion, Is.EqualTo(1));
                }
            );
            RobustFile.Delete(collectionStylesPath);
        }

        [Test]
        public void CompressBookForDevice_OmitsUnwantedFiles()
        {
            // some files we don't want copied into the .bloompub
            var unwantedFiles = new List<string>
            {
                "book.BloomBookOrder",
                "book.pdf",
                "thumbnail-256.png",
                "thumbnail-70.png", // these are artifacts of uploading book to BloomLibrary.org
                "Traditional-XMatter.css" // since we're adding Device-XMatter.css, this is no longer needed
            };

            TestHtmlAfterCompression(
                kMinimumValidBookHtml,
                actionsOnFolderBeforeCompressing: folderPath =>
                {
                    // The png files have to be real; just putting some text in them leads to out-of-memory failures when Bloom
                    // tries to make their background transparent.
                    File.Copy(
                        SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                            _pathToTestImages,
                            "shirt.png"
                        ),
                        Path.Combine(folderPath, "thumbnail.png")
                    );
                    File.WriteAllText(
                        Path.Combine(folderPath, "previewMode.css"),
                        @"This is wanted"
                    );

                    // now some files we expect to be omitted from the .bloompub archive
                    File.WriteAllText(
                        Path.Combine(folderPath, "book.BloomBookOrder"),
                        @"This is unwanted"
                    );
                    File.WriteAllText(Path.Combine(folderPath, "book.pdf"), @"This is unwanted");
                    File.Copy(
                        SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                            _pathToTestImages,
                            "shirt.png"
                        ),
                        Path.Combine(folderPath, "thumbnail-256.png")
                    );
                    File.Copy(
                        SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                            _pathToTestImages,
                            "shirt.png"
                        ),
                        Path.Combine(folderPath, "thumbnail-70.png")
                    );
                },
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    foreach (var name in unwantedFiles)
                    {
                        Assert.AreEqual(
                            -1,
                            zip.FindEntry(Path.GetFileName(name), true),
                            "expected " + name + " to not be part of .bloompub zip"
                        );
                    }
                }
            );
        }

        // Also verifies that images that DO exist are NOT removed (even if src attr includes params like ?optional=true)
        // Since this is one of the few tests that makes a real HTML file we use it also to check
        // the the HTML file is at the root of the zip.
        [Test]
        public void CompressBookForDevice_RemovesImgElementsWithMissingSrc_AndContentEditable()
        {
            const string imgsToRemove = "<img src='nonsence.svg'/><img src=\"rubbish\"/>";
            var htmlTemplate =
                @"<html>
									<body>
										<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
											<div class='pageLabel' lang='en'>
												Outside Back Cover
											</div>
											<div class='pageDescription' lang='en'></div>

											<div class='marginBox'>
											<div class='bloom-translationGroup' data-default-languages='N1'>
												<div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-contentNational1 bloom-visibility-code-on' lang='fr' contenteditable='false' data-book='outsideBackCover'>
													<label class='bubble'>If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.</label>
												</div>

												<div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-contentNational2' lang='de'contenteditable='true' data-book='outsideBackCover'></div>

												<div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-content1' lang='ksf' contenteditable='true' data-book='outsideBackCover'></div>
											</div>
											{0}
											</div>
										</div>
									</body>
									</html>";
            var htmlOriginal = string.Format(htmlTemplate, imgsToRemove);
            var testBook = CreateBookWithPhysicalFile(htmlOriginal, bringBookUpToDate: true);

            TestHtmlAfterCompression(
                htmlOriginal,
                actionsOnFolderBeforeCompressing: bookFolderPath => // Simulate the typical situation where we have the regular but not the wide svg
                    File.WriteAllText(
                        Path.Combine(bookFolderPath, "somelogo.svg"),
                        @"this is a fake for testing"
                    ),
                assertionsOnResultingHtmlString: html =>
                {
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    AssertThatXmlIn.Dom(htmlDom).HasNoMatchForXpath("//div[@contenteditable]");
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath("//img[@src='nonsence.svg']", 0);
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath("//img[@src='rubbish']", 0);
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath("//img[@src='license.png']", 1);
                }
            );
        }

        [Test]
        public void CompressBookForDevice_RemovesUnwantedLanguages()
        {
            var htmlTemplate =
                @"<html>
									<body>
										<div class='bloom-page A5Portrait' ' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'
											<div class='marginBox'>
											<div class='bloom-translationGroup' data-default-languages='N1' id='test1'>

												<div class='bloom-editable bloom-contentNational2' lang='de'contenteditable='true'>German content</div>

												<div class='bloom-editable bloom-content1' lang='ksf' contenteditable='true' >ksf Content</div>

												<div class='bloom-editable' lang='fr' contenteditable='true' >French content</div>
											</div>
											</div>
										</div>
									</body>
									</html>";

            TestHtmlAfterCompression(
                htmlTemplate,
                assertionsOnResultingHtmlString: html =>
                {
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//div[@id='test1']/div[@lang='de']",
                            1
                        );
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//div[@id='test1']/div[@lang='fr']",
                            0
                        );
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//div[@id='test1']/div[@lang='ksf']",
                            1
                        );
                },
                languagesToInclude: new HashSet<string>(new[] { "de", "ksf" })
            );
        }

        /// <summary>
        /// We test in IncludesWantedFiles that by default all metadata features are off.
        /// We test in HandlsVideosAndModifiesSrcAttribute that Feature_SignLanguage is set when appropriate.
        /// This method needs to handle motion, image descriptions, and talking book.
        /// </summary>
        [Test]
        public void CompressBookForDevice_SetsExpectedFeaturesAndAttributes()
        {
            const string imgsToRemove = "<img src='nonsence.svg'/><img src=\"rubbish\"/>";
            var htmlTemplate =
                @"
<html>
<body>
	<div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual' data-page='' id='3dba74c5-adc9-4c0d-9934-e8484fb6e2e2' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' data-page-number='4' lang=''>
		<div class='marginBox'>
            <div style='min-height: 42px;' class='split-pane horizontal-percent'>
                <div class='split-pane-component position-top' style='bottom: 50%'>
                    <div class='split-pane-component-inner'>
                        <div title='aor_BRD11.png 41.36 KB 1500 x 581 716 DPI (should be 300-600) Bit Depth: 32' class='bloom-imageContainer bloom-leadingElement'
							data-initialrect='0.0024509803921568627 0.002967359050445104 0.75 0.7507418397626113' data-finalrect='0.37745098039215685 0.12759643916913946 0.5 0.5014836795252225'>
							< img data-license='cc-by-sa' data-creator='' data-copyright='Copyright SIL International 2009' src='aor_BRD11.png' alt='Two birds on a branch with beak tips touching'></img>

                            <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                                <div data-audiorecordingmode='Sentence' data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
                                    <p><span data-duration='3.984739' id='aace3497-02e1-46e7-9af3-52bb74010fcc' class='audio-sentence' recordingmd5='undefined'>Two birds on a branch with beak tips touching</span></p>
                                </div>

                                <div data-languagetipcontent='German, Standard' style='' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-contentNational2' contenteditable='true' lang='de'>
                                    <p></p>
                                </div>

                                <div data-languagetipcontent='español' style='' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-contentNational1' contenteditable='true' lang='es'>
                                    <p></p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <div class='split-pane-divider horizontal-divider' style='bottom: 50%'></div>

                <div class='split-pane-component position-bottom' style='height: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                            <div data-audiorecordingmode='Sentence' data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 36px;' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
                                <p><span data-duration='3.070453' id='i2335f5ae-2cff-4029-a85c-951cc33256a4' class='audio-sentence' recordingmd5='undefined'>These two birds look very affectionate.</span></p>
                            </div>

                            <div data-languagetipcontent='German, Standard' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 36px;' class='bloom-editable normal-style bloom-contentNational2' contenteditable='true' lang='de'>
                                <p></p>
                            </div>

                            <div data-languagetipcontent='español' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable normal-style bloom-contentNational1' contenteditable='true' lang='es'>
                                <p></p>
                            </div>

                            <div style='' class='bloom-editable normal-style' contenteditable='true' lang='z'>
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
	</div>
</body>
</html>";
            var htmlOriginal = string.Format(htmlTemplate, imgsToRemove);
            var testBook = CreateBookWithPhysicalFile(htmlOriginal, bringBookUpToDate: true);

            TestHtmlAfterCompression(
                htmlOriginal,
                actionsOnFolderBeforeCompressing: bookFolderPath =>
                {
                    // The page above expects these two audio files to exist. Their content doesn't matter.
                    var audioFolder = Path.Combine(bookFolderPath, "audio");
                    Directory.CreateDirectory(audioFolder);
                    File.WriteAllText(
                        Path.Combine(audioFolder, "aace3497-02e1-46e7-9af3-52bb74010fcc.mp3"),
                        @"this is a fake for testing"
                    );
                    File.WriteAllText(
                        Path.Combine(audioFolder, "i2335f5ae-2cff-4029-a85c-951cc33256a4.mp3"),
                        @"this is a fake for testing"
                    );
                    testBook.BookInfo.PublishSettings.BloomPub.PublishAsMotionBookIfApplicable =
                        true;
                    testBook.BookInfo.Save();
                },
                assertionsOnResultingHtmlString: html =>
                {
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    var body = htmlDom.DocumentElement.GetElementsByTagName("body")[0];
                    Assert.That(
                        body.GetAttribute("data-bfautoadvance"),
                        Is.EqualTo("landscape;bloomReader")
                    );
                    Assert.That(
                        body.GetAttribute("data-bfcanrotate"),
                        Is.EqualTo("allOrientations;bloomReader")
                    );
                    Assert.That(
                        body.GetAttribute("data-bfplayanimations"),
                        Is.EqualTo("landscape;bloomReader")
                    );
                    Assert.That(
                        body.GetAttribute("data-bfplaymusic"),
                        Is.EqualTo("landscape;bloomReader")
                    );
                    Assert.That(
                        body.GetAttribute("data-bfplaynarration"),
                        Is.EqualTo("landscape;bloomReader")
                    );
                    Assert.That(
                        body.GetAttribute("data-bffullscreenpicture"),
                        Is.EqualTo("landscape;bloomReader")
                    );
                },
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    var meta = BookMetaData.FromString(GetEntryContents(zip, "meta.json"));
                    Assert.That(meta.Feature_TalkingBook, Is.True);
                    Assert.That(meta.Feature_Motion, Is.True);
                },
                languagesToInclude: new HashSet<string>(new[] { "xyz", "de", "es" })
            );
        }

        [Test]
        public void CompressBookForDevice_ImgInImageContainer_ConvertedToBackground()
        {
            // The odd file names here are an important part of the test; they need to get converted to proper URL syntax.
            const string bookHtml =
                @"<html>
										<body>
											<div class='bloom-page' id='blah'>
												<div class='marginBox'>
													<div class='bloom-imageContainer bloom-leadingElement'>"
                + "	<img src=\"HL00'14 1.svg\"/>"
                + @"</div>
													<div class='bloom-imageContainer bloom-leadingElement'>"
                + "<img src=\"HL00'14 1.svg\"/>"
                + @"</div>
											</div>
										</body>
									</html>";

            TestHtmlAfterCompression(
                bookHtml,
                actionsOnFolderBeforeCompressing: bookFolderPath =>
                    File.WriteAllText(
                        Path.Combine(bookFolderPath, "HL00'14 1.svg"),
                        @"this is a fake for testing"
                    ),
                assertionsOnResultingHtmlString: changedHtml =>
                {
                    // The imgs should be replaced with something like this:
                    //		"<div class='bloom-imageContainer bloom-leadingElement bloom-backgroundImage' style='background-image:url('HL00%2714%201.svg.svg')'</div>
                    //	Note that normally there would also be data-creator, data-license, etc. If we put those in the html, they will be stripped because
                    // the code will actually look at our fake image and, finding now metadata will remove these. This is not a problem for our
                    // testing here, because we're not trying to test the functioning of that function here. The bit we can test, that the image became a
                    // background image, is sufficient to know the function was run.

                    // Oct 2017 jh: I added this bloom-imageContainer/ because the code that does the conversion is limited to these,
                    // presumably because that is the only img's that were giving us problems (ones that need to be sized at display time).
                    // But Xmatter has other img's, for license & branding.
                    var changedDom = XmlHtmlConverter.GetXmlDomFromHtml(changedHtml);
                    AssertThatXmlIn
                        .Dom(changedDom)
                        .HasNoMatchForXpath("//bloom-imageContainer/img"); // should be merged into parent

                    //Note: things like  @data-creator='Anis', @data-license='cc-by' and @data-copyright='1996 SIL PNG' are not going to be there by now,
                    //because they aren't actually supported by the image file, so they get stripped.
                    AssertThatXmlIn
                        .Dom(changedDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//div[@class='bloom-imageContainer bloom-leadingElement bloom-backgroundImage' and @style=\"background-image:url('HL00%2714%201.svg')\"]",
                            2
                        );
                }
            );
        }

        [Test]
        public void CompressBookForDevice_IncludesVersionFileAndStyleSheet()
        {
            // This requires a real book file (which a mocked book usually doesn't have).
            // It's also important that the book contains something like contenteditable that will be removed when
            // sending the book. The sha is based on the actual file contents of the book, not the
            // content actually embedded in the bloompub.
            var bookHtml =
                @"<html>
								<head>
									<meta charset='UTF-8'></meta>
									<link rel='stylesheet' href='defaultLangStyles.css' type='text/css'></link>
									<link rel='stylesheet' href='customCollectionStyles.css' type='text/css'></link>
								</head>
								<body>
									<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
										<div  contenteditable='true'>something</div>
									</div>
								</body>
							</html>";

            string entryContents = null;

            TestHtmlAfterCompression(
                bookHtml,
                actionsOnFolderBeforeCompressing: bookFolderPath => // Simulate the typical situation where we have the regular but not the wide svg
                    File.WriteAllText(
                        Path.Combine(bookFolderPath, "back-cover-outside.svg"),
                        @"this is a fake for testing"
                    ),
                assertionsOnResultingHtmlString: html =>
                {
                    AssertThatXmlIn
                        .Dom(XmlHtmlConverter.GetXmlDomFromHtml(html))
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//html/head/link[@rel='stylesheet' and @href='readerStyles.css' and @type='text/css']",
                            1
                        );
                },
                assertionsOnZipArchive: paramObj =>
                // This test worked when we didn't have to modify the book before making the .bloompub.
                // Now that we do I haven't figured out a reasonable way to rewrite it to test this value again...
                // Assert.That(GetEntryContents(zip, "version.txt"), Is.EqualTo(Bloom.Book.Book.MakeVersionCode(html, bookPath)));
                // ... so for now we just make sure that it was added and looks like a hash code
                {
                    var zip = paramObj.ZipFile;
                    entryContents = GetEntryContents(zip, "version.txt");
                    Assert.AreEqual(44, entryContents.Length);
                },
                assertionsOnRepeat: zip =>
                {
                    Assert.That(GetEntryContents(zip, "version.txt"), Is.EqualTo(entryContents));
                }
            );
        }

        [TestCase(BloomPubMaker.kCreatorBloom, BloomPubMaker.kDistributionBloomDirect)]
        [TestCase(BloomPubMaker.kCreatorHarvester, BloomPubMaker.kDistributionBloomWeb)]
        public void CompressBookForDevice_IncludesDistributionFile(
            string creator,
            string expectedContent
        )
        {
            TestHtmlAfterCompression(
                kMinimumValidBookHtml,
                assertionsOnZipArchive: zipHtmlObj =>
                {
                    Assert.AreEqual(
                        expectedContent,
                        GetEntryContents(zipHtmlObj.ZipFile, BloomPubMaker.kDistributionFileName)
                    );
                },
                creator: creator
            );
        }

        const string kQuestionPagesHtml =
            @"<html>
<head>
	<meta charset='UTF-8'></meta>
	<link rel='stylesheet' href='defaultLangStyles.css' type='text/css'></link>
	<link rel='stylesheet' href='customCollectionStyles.css' type='text/css'></link>
</head>
<body>
	<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
		<div  contenteditable='true'>This page should make it into the book</div>
	</div>
    <div class='bloom-page customPage enterprise questions bloom-nonprinting Device16x9Portrait layout-style-Default side-left bloom-monolingual' id='86574a93-a50f-42da-b78f-574ef790c481' data-page='' data-pagelineage='4140d100-e4c3-49c4-af05-dda5789e019b' data-page-number='1' lang=''>
        <div class='pageLabel' lang='en'>
            Comprehension Questions
        </div>
        <div class='pageDescription' lang='en'></div>

        <div class='marginBox'>
            <div style='min-width: 0px;' class='split-pane vertical-percent'>
                <div class='split-pane-component position-left'>
                    <div class='split-pane-component-inner'>
                        <div class='ccLabel'>
                            <p>Enter your comprehension questions for this book..., see <a href='https://docs.google.com/document/d/1LV0_OtjH1BTJl7wqdth0bZXQxduTqD7WenX4AsksVGs/edit#heading=h.lxe9k6qcvzwb'>Bloom Enterprise Service</a></p>

							...

                            <p>*Appeared to wear the cap</p>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider vertical-divider'></div>

                <div class='split-pane-component position-right'>
                    <div class='split-pane-component-inner adding'>
                        <div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
                            <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr cc-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
                                <p>Where do questions belong?</p>

                                <p>* At the end</p>

                                <p>At the start</p>

                                <p>In the middle</p>

                                <p></p>
                            </div>
                            <div class='bloom-editable' contenteditable='true' lang='fr'>
                                <p>Where do French questions belong?</p>

                                <p> *At the end of the French</p>

                                <p>At the start of the French</p>

                                <p>In the middle of the French</p>

                            </div>
                            <div class='bloom-editable' contenteditable='true' lang='z'></div>

                            <div aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-contentNational1' contenteditable='true' lang='es'>
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page customPage enterprise questions bloom-nonprinting Device16x9Portrait layout-style-Default side-left bloom-monolingual' id='299c0b20-56f7-4a0f-a6d4-08f1ec01f1e6' data-page='' data-pagelineage='4140d100-e4c3-49c4-af05-dda5789e019b' data-page-number='2' lang=''>
        <div class='pageLabel' lang='en'>
            Comprehension Questions
        </div>

        <div class='pageDescription' lang='en'></div>

        <div class='marginBox'>
            <div style='min-width: 0px;' class='split-pane vertical-percent'>
                <div class='split-pane-component position-left'>
                    <div class='split-pane-component-inner'>
                        <div class='ccLabel'>
                            <p>Enter your ..., see <a href='https://docs.google.com/document/d/1LV0_OtjH1BTJl7wqdth0bZXQxduTqD7WenX4AsksVGs/edit#heading=h.lxe9k6qcvzwb'>Bloom Enterprise Service</a></p>

                            <p></p>
								...

                            <p>*Appeared to wear the cap</p>
                        </div>
                    </div>
                </div>

                <div class='split-pane-divider vertical-divider'></div>

                <div class='split-pane-component position-right'>
                    <div class='split-pane-component-inner adding'>
                        <div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
                            <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr cc-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
                                <p>Where is the USA?<br></br>
                                South America<br></br>
                                *North America<br></br>
                                Europe<br></br>
                                Asia</p>

                                <p></p>

                                <p>Where does the platypus come from?<br></br>
                                *Australia<br></br>
                                Papua New Guinea<br></br>
                                Africa<br></br>
                                Peru</p>

                                <p></p>

                                <p>What is an Emu?<br></br>
                                A fish<br></br>
                                An insect<br></br>
                                A spider<br></br>
                                * A bird</p>

                                <p></p>

                                <p>Where do emus live?<br></br>
                                New Zealand<br></br>
                                * Farms in the USA<br></br>
                                England<br></br>
                                Wherever</p>

                                <p></p>
                            </div>

                            <div class='bloom-editable' contenteditable='true' lang='z'></div>

                            <div aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-contentNational1' contenteditable='true' lang='es'>
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";

        private void LegacyQuestionsPagesAssertionsOnResultingHtmlString(string html)
        {
            // The questions pages should be removed.
            var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            AssertThatXmlIn
                .Dom(htmlDom)
                .HasNoMatchForXpath(
                    "//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'questions')]"
                );
        }

        [Test]
        public void CompressBookForDevice_NotBloomEnterprise_LegacyQuestionsPages_PagesAreRemoved()
        {
            TestHtmlAfterCompression(
                kQuestionPagesHtml,
                assertionsOnResultingHtmlString: LegacyQuestionsPagesAssertionsOnResultingHtmlString,
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    Assert.That(
                        zip.FindEntry(BloomPubMaker.kQuestionFileName, false),
                        Is.EqualTo(-1)
                    );
                },
                branding: "Default"
            );
        }

        [Test]
        public void CompressBookForDevice_BloomEnterprise_LegacyQuestionsPages_ConvertsToJson()
        {
            // This requires a real book file (which a mocked book usually doesn't have).
            // Test data reflects a number of important conditions, including presence or absence of
            // white space before and after asterisk, paragraphs broken up with br.
            // As yet does not cover questions with no answers (currently will be excluded),
            // questions with no right answer (currently will be included)
            // questions with more than one right answer (currently will be included)
            // questions with only one answer (currently will be included),
            // since I'm not sure what the desired behavior is.
            // If we want to test corner cases it might be easier to test BloomReaderFileMaker.ExtractQuestionGroups directly.

            TestHtmlAfterCompression(
                kQuestionPagesHtml,
                assertionsOnResultingHtmlString: LegacyQuestionsPagesAssertionsOnResultingHtmlString,
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    var json = GetEntryContents(zip, BloomPubMaker.kQuestionFileName);
                    var groups = QuestionGroup.FromJson(json);
                    // Two (non-z-language) groups in first question page, one in second.
                    Assert.That(groups, Has.Length.EqualTo(3));
                    Assert.That(groups[0].questions, Has.Length.EqualTo(1));
                    Assert.That(groups[1].questions, Has.Length.EqualTo(1));
                    Assert.That(groups[2].questions, Has.Length.EqualTo(4));

                    Assert.That(groups[0].lang, Is.EqualTo("en"));
                    Assert.That(groups[1].lang, Is.EqualTo("fr"));

                    Assert.That(groups[0].onlyForBloomReader1, Is.False);

                    Assert.That(
                        groups[0].questions[0].question,
                        Is.EqualTo("Where do questions belong?")
                    );
                    Assert.That(groups[0].questions[0].answers, Has.Length.EqualTo(3));
                    Assert.That(groups[0].questions[0].answers[0].text, Is.EqualTo("At the end"));
                    Assert.That(groups[0].questions[0].answers[0].correct, Is.True);
                    Assert.That(groups[0].questions[0].answers[1].text, Is.EqualTo("At the start"));
                    Assert.That(groups[0].questions[0].answers[1].correct, Is.False);

                    Assert.That(
                        groups[1].questions[0].question,
                        Is.EqualTo("Where do French questions belong?")
                    );
                    Assert.That(groups[1].questions[0].answers, Has.Length.EqualTo(3));
                    Assert.That(
                        groups[1].questions[0].answers[0].text,
                        Is.EqualTo("At the end of the French")
                    );
                    Assert.That(groups[1].questions[0].answers[0].correct, Is.True);
                    Assert.That(
                        groups[1].questions[0].answers[1].text,
                        Is.EqualTo("At the start of the French")
                    );
                    Assert.That(groups[1].questions[0].answers[1].correct, Is.False);

                    Assert.That(groups[2].questions[0].question, Is.EqualTo("Where is the USA?"));
                    Assert.That(groups[2].questions[0].answers, Has.Length.EqualTo(4));
                    Assert.That(groups[2].questions[0].answers[3].text, Is.EqualTo("Asia"));
                    Assert.That(groups[2].questions[0].answers[3].correct, Is.False);
                    Assert.That(
                        groups[2].questions[0].answers[1].text,
                        Is.EqualTo("North America")
                    );
                    Assert.That(groups[2].questions[0].answers[1].correct, Is.True);

                    Assert.That(groups[2].questions[2].question, Is.EqualTo("What is an Emu?"));
                    Assert.That(groups[2].questions[2].answers, Has.Length.EqualTo(4));
                    Assert.That(groups[2].questions[2].answers[0].text, Is.EqualTo("A fish"));
                    Assert.That(groups[2].questions[2].answers[0].correct, Is.False);
                    Assert.That(groups[2].questions[2].answers[3].text, Is.EqualTo("A bird"));
                    Assert.That(groups[2].questions[2].answers[3].correct, Is.True);

                    // Make sure we don't miss the last answer of the last question.
                    Assert.That(groups[2].questions[3].answers[3].text, Is.EqualTo("Wherever"));
                },
                branding: "Test"
            );
        }

        private const string kNewQuizPageTestsHtml =
            @"<html>
<head>
	<meta charset='UTF-8'></meta>
	<link rel='stylesheet' href='defaultLangStyles.css' type='text/css'></link>
	<link rel='stylesheet' href='customCollectionStyles.css' type='text/css'></link>
</head>
<body>
	<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
		<div  contenteditable='true'>This page should make it into the book</div>
	</div>
    <div class='bloom-page simple-comprehension-quiz bloom-interactive-page enterprise-only Device16x9Portrait side-right bloom-monolingual' id='86574a93-a50f-42da-b88f-574ef790c481' data-page='' data-pagelineage='F125A8B6-EA15-4FB7-9F8D-271D7B3C8D4D' data-page-number='1' lang=''>
        <div class='pageLabel' lang='en'>
            Quiz Page
        </div>
        <div class='pageDescription' lang='en'></div>

        <div class='marginBox'>
            <div class='quiz'>
                <div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
                    <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable QuizQuestion-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
                        <p>Where do questions belong?</p>
					</div>
				</div>
				<div class='checkbox-and-textbox-choice'>
					<div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
						<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable QuizAnswer-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
							<p>At the end</p>
						</div>
					</div>
				</div>
				<div class='checkbox-and-textbox-choice'>
					<div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
						<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable QuizAnswer-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
							<p>At the start</p>
						</div>
					</div>
				</div>
				<div class='checkbox-and-textbox-choice correct-answer'>
					<div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
						<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable QuizAnswer-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
							<p>In the middle</p>
						</div>
					</div>
				</div>
				<script src='simpleComprehensionQuiz.js' />
            </div>
        </div>
    </div>
</body>
</html>";

        private void NewQuizTestActionsOnFolderBeforeCompressing(string bookFolderPath)
        {
            // This file gets placed in the real book's folder after adding a quiz in edit mode, so we mock that process.
            // But the code which puts an epub into a real browser to determine visibility of elements will actually
            // try to run this, so it needs to be something which won't throw a javascript error.
            RobustFile.WriteAllText(
                Path.Combine(bookFolderPath, PublishHelper.kSimpleComprehensionQuizJs),
                "//not the real file's contents"
            );
        }

        [Test]
        public void CompressBookForDevice_BloomEnterprise_ConvertsNewQuizPagesToJson_AndKeepsThem()
        {
            TestHtmlAfterCompression(
                kNewQuizPageTestsHtml,
                actionsOnFolderBeforeCompressing: NewQuizTestActionsOnFolderBeforeCompressing,
                assertionsOnResultingHtmlString: html =>
                {
                    // The quiz pages should not be removed.
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'simple-comprehension-quiz')]",
                            1
                        );
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            $"//script[@src='{PublishHelper.kSimpleComprehensionQuizJs}']",
                            1
                        );
                },
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    Assert.AreNotEqual(
                        -1,
                        zip.FindEntry(PublishHelper.kSimpleComprehensionQuizJs, false)
                    );
                    var json = GetEntryContents(zip, BloomPubMaker.kQuestionFileName);
                    var groups = QuestionGroup.FromJson(json);
                    Assert.That(groups, Has.Length.EqualTo(1));
                    Assert.That(groups[0].lang, Is.EqualTo("xyz"));
                    Assert.That(groups[0].questions, Has.Length.EqualTo(1));
                    Assert.That(groups[0].onlyForBloomReader1, Is.True);

                    var question = groups[0].questions[0];
                    Assert.That(question.question, Is.EqualTo("Where do questions belong?"));

                    var answers = question.answers;
                    Assert.That(answers.Length, Is.EqualTo(3));
                    Assert.That(answers[0].text, Is.EqualTo("At the end"));
                    Assert.That(answers[1].text, Is.EqualTo("At the start"));
                    Assert.That(answers[2].text, Is.EqualTo("In the middle"));
                    Assert.That(answers[0].correct, Is.False);
                    Assert.That(answers[1].correct, Is.False);
                    Assert.That(answers[2].correct, Is.True);
                },
                branding: "Test"
            );
        }

        [Test]
        public void CompressBookForDevice_NotBloomEnterprise_QuizPagesAreRemovedAndQuestionsFileNotGenerated()
        {
            TestHtmlAfterCompression(
                kNewQuizPageTestsHtml,
                actionsOnFolderBeforeCompressing: NewQuizTestActionsOnFolderBeforeCompressing,
                assertionsOnResultingHtmlString: html =>
                {
                    // The quiz pages should be removed.
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasNoMatchForXpath(
                            "//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'simple-comprehension-quiz')]"
                        );
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasNoMatchForXpath(
                            $"//script[@src='{PublishHelper.kSimpleComprehensionQuizJs}']"
                        );
                },
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    Assert.AreEqual(-1, zip.FindEntry(BloomPubMaker.kQuestionFileName, false));
                    Assert.AreEqual(
                        -1,
                        zip.FindEntry(PublishHelper.kSimpleComprehensionQuizJs, false)
                    );
                },
                branding: "Default"
            );
        }

        [Test]
        public void CompressBookForDevice_MakesThumbnailFromCoverPicture()
        {
            // This requires a real book file (which a mocked book usually doesn't have).
            var bookHtml =
                @"<html>
								<head>
									<meta charset='UTF-8'></meta>
									<link rel='stylesheet' href='defaultLangStyles.css' type='text/css'></link>
									<link rel='stylesheet' href='customCollectionStyles.css' type='text/css'></link>
								</head>
								<body>
									<div id='bloomDataDiv'>
										<div data-book='coverImage' lang='*'>
											Listen to My Body_Cover.png
										</div>
									</div>
									<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
										<div class='marginBox'>"
                + "<div class=\"bloom-imageContainer bloom-backgroundImage\" data-book=\"coverImage\" style=\"background-image:url('Listen%20to%20My%20Body_Cover.png')\"></div>"
                + @"</div>
									</div>
								</body>
							</html>";

            TestHtmlAfterCompression(
                bookHtml,
                actionsOnFolderBeforeCompressing: bookFolderPath =>
                {
                    File.Copy(
                        SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                            _pathToTestImages,
                            "shirt.png"
                        ),
                        Path.Combine(bookFolderPath, "Listen to My Body_Cover.png")
                    );
                },
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    using (var thumbStream = GetEntryContentsStream(zip, "thumbnail.png"))
                    {
                        using (var thumbImage = Image.FromStream(thumbStream))
                        {
                            // I don't know how to verify that it's made from shirt.png, but this at least verifies
                            // that some shrinking was done and that it considers height as well as width, since
                            // the shirt.png image happens to be higher than it is wide.
                            // It would make sense to test that it works for jpg images, too, but it's rather a slow
                            // test and jpg doesn't involve a different path through the new code.
                            Assert.That(thumbImage.Width, Is.LessThanOrEqualTo(256));
                            Assert.That(thumbImage.Height, Is.LessThanOrEqualTo(256));
                        }
                    }
                }
            );
        }

        private const string kPathToTestVideos = "src/BloomTests/videos";
        private const string kVideoTestHtml =
            @"<html>
					<head>
						<meta charset='UTF-8'></meta>
						<link rel='stylesheet' href='Basic Book.css' type='text/css'></link>
					</head>
					<body>
						<div class='bloom-page A5Portrait' data-page='required singleton' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
							<div class='marginBox'>
								<div class='someSplitContainerStuff'>
									<div class='bloom-videoContainer'>
										<video>
											<source src='video/Five%20count.mp4'></source>
										</video>
									</div>
								</div>
								<div class='someSplitContainerStuff'>
									<div class='bloom-videoContainer bloom-noVideoSelected'>
									</div>
								</div>
								<div class='someSplitContainerStuff'>
									<div class='bloom-videoContainer'>
										<video>
											<source src='video/Crow.mp4#t=1.0,3.4'></source>
										</video>
									</div>
								</div>
							</div>
						</div>
					</body>
				</html>";

        private void VideoTestActionsOnFolderBeforeCompressing(string folderPath)
        {
            // These video files have to be real.
            var videoFolderPath = Path.Combine(folderPath, "video");
            Directory.CreateDirectory(videoFolderPath);
            RobustFile.Copy(
                FileLocationUtilities.GetFileDistributedWithApplication(
                    kPathToTestVideos,
                    "Crow.mp4"
                ),
                Path.Combine(videoFolderPath, "Crow.mp4")
            );
            RobustFile.Copy(
                FileLocationUtilities.GetFileDistributedWithApplication(
                    kPathToTestVideos,
                    "Five count.mp4"
                ),
                Path.Combine(videoFolderPath, "Five count.mp4")
            );
        }

        [Test]
        public void CompressBookForDevice_BloomEnterprise_HandlesVideosAndModifiesSrcAttribute()
        {
            // This requires a real book file (which a mocked book usually doesn't have).
            TestHtmlAfterCompression(
                kVideoTestHtml,
                actionsOnFolderBeforeCompressing: VideoTestActionsOnFolderBeforeCompressing,
                assertionsOnZipArchive: paramObj =>
                {
                    const int originalCrowSize = 330000; // roughly equivalent to 324Kb
                    var zip = paramObj.ZipFile;
                    Assert.AreNotEqual(
                        -1,
                        zip.FindEntry("video/Five count.mp4", false),
                        "Expected to find untrimmed video"
                    );
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(paramObj.Html);
                    var secondSource = htmlDom.SafeSelectNodes("//source")[1] as SafeXmlElement;
                    var srcAttr = secondSource.GetAttribute("src");
                    Assert.AreNotEqual(
                        -1,
                        zip.FindEntry(srcAttr, false),
                        "Expected to find a new filename for trimmed 'Crow.mp4'"
                    );
                    var entry = zip.GetEntry(srcAttr);
                    // 100000 here is a semi-random quantification of 'considerably'.
                    Assert.Less(
                        entry.Size,
                        originalCrowSize - 100000,
                        "Should have trimmed the file considerably."
                    );
                    var meta = BookMetaData.FromString(GetEntryContents(zip, "meta.json"));
                    Assert.That(meta.Feature_SignLanguage, Is.False);
                    Assert.That(meta.Feature_Video, Is.True);
                },
                assertionsOnResultingHtmlString: html =>
                {
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath("//video[@controls]", 0); //BL-7083
                    // Crow.mp4 should have been trimmed and had a name change
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasNoMatchForXpath("//source[@src='video/Crow.mp4']");
                    // Five count.mp4 was not trimmed and should have kept its name
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//source[@src='video/Five%20count.mp4']",
                            1
                        );
                },
                branding: "Test"
            );
        }

        // Originally, video was an enterprise-only feature, so the logic was reversed.
        // Now we want to be sure we are keeping video even if enterprise is off.
        [Test]
        public void CompressBookForDevice_NotBloomEnterprise_DoesNotRemoveVideoPages()
        {
            // This requires a real book file (which a mocked book usually doesn't have).
            TestHtmlAfterCompression(
                kVideoTestHtml,
                actionsOnFolderBeforeCompressing: VideoTestActionsOnFolderBeforeCompressing,
                assertionsOnZipArchive: paramObj =>
                {
                    var zip = paramObj.ZipFile;
                    var zipEnumerator = zip.GetEnumerator();
                    var foundVideo = false;
                    while (zipEnumerator.MoveNext())
                    {
                        var zipEntry = zipEnumerator.Current as ZipEntry;
                        if (zipEntry == null)
                            Assert.Fail();
                        if (zipEntry.Name.Contains("video"))
                            foundVideo = true;
                    }
                    if (!foundVideo)
                        Assert.Fail("Expected to find video folder and files");
                    var meta = BookMetaData.FromString(GetEntryContents(zip, "meta.json"));
                },
                assertionsOnResultingHtmlString: html =>
                {
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    AssertThatXmlIn.Dom(htmlDom).HasAtLeastOneMatchForXpath("//video");
                },
                branding: "Default"
            );
        }

        private Stream GetEntryContentsStream(ZipFile zip, string name, bool exact = false)
        {
            Func<ZipEntry, bool> predicate;
            if (exact)
                predicate = n => n.Name.Equals(name);
            else
                predicate = n => n.Name.EndsWith(name);

            var ze = (from ZipEntry entry in zip select entry).FirstOrDefault(predicate);
            Assert.That(ze, Is.Not.Null);

            return zip.GetInputStream(ze);
        }

        private string GetEntryContents(ZipFile zip, string name, bool exact = false)
        {
            var buffer = new byte[4096];

            using (var instream = GetEntryContentsStream(zip, name, exact))
            using (var writer = new MemoryStream())
            {
                ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
                writer.Position = 0;
                using (var reader = new StreamReader(writer))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        // re-use the images from another test (added LakePendOreille.jpg for these tests)
        private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

        [Test]
        public void GetBytesOfReducedImage_SmallPngImageMadeTransparent()
        {
            // bird.png:                   PNG image data, 274 x 300, 8-bit/color RGBA, non-interlaced

            var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "bird.png"
            );
            byte[] originalBytes = File.ReadAllBytes(path);
            byte[] reducedBytes = BookCompressor.GetImageBytesForElectronicPub(
                path,
                true,
                new ImagePublishSettings()
            );
            Assert.That(reducedBytes, Is.Not.EqualTo(originalBytes)); // no easy way to check it was made transparent, but should be changed.
            // Size should not change much.
            if (Platform.IsLinux)
            {
                // Linux graphics code isn't as sophisticated as Windows: the file size seems to
                // grow a lot more.
                Assert.That(reducedBytes.Length, Is.LessThan(originalBytes.Length * 2));
            }
            else
            {
                Assert.That(reducedBytes.Length, Is.LessThan(originalBytes.Length * 11 / 10));
            }
            Assert.That(reducedBytes.Length, Is.GreaterThan(originalBytes.Length * 9 / 10));
            using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
            {
                var oldMetadata = Metadata.FromFile(path);
                RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
                var newMetadata = Metadata.FromFile(tempFile.Path);
                if (oldMetadata.IsEmpty)
                {
                    Assert.IsTrue(newMetadata.IsEmpty);
                }
                else
                {
                    Assert.IsFalse(newMetadata.IsEmpty);
                    Assert.AreEqual(
                        oldMetadata.CopyrightNotice,
                        newMetadata.CopyrightNotice,
                        "copyright preserved for bird.png"
                    );
                    Assert.AreEqual(
                        oldMetadata.Creator,
                        newMetadata.Creator,
                        "creator preserved for bird.png"
                    );
                    Assert.AreEqual(
                        oldMetadata.License.ToString(),
                        newMetadata.License.ToString(),
                        "license preserved for bird.png"
                    );
                }
            }
        }

        [Test]
        public void GetBytesOfReducedImage_SmallJpgImageStaysSame()
        {
            // man.jpg:                    JPEG image data, JFIF standard 1.01, ..., precision 8, 118x154, frames 3

            var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.jpg"
            );
            var originalBytes = File.ReadAllBytes(path);
            var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(
                path,
                true,
                new ImagePublishSettings()
            );
            Assert.AreEqual(
                originalBytes,
                reducedBytes,
                "man.jpg is already small enough (118x154)"
            );
            using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
            {
                var oldMetadata = Metadata.FromFile(path);
                RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
                var newMetadata = Metadata.FromFile(tempFile.Path);
                if (oldMetadata.IsEmpty)
                {
                    Assert.IsTrue(newMetadata.IsEmpty);
                }
                else
                {
                    Assert.IsFalse(newMetadata.IsEmpty);
                    Assert.AreEqual(
                        oldMetadata.CopyrightNotice,
                        newMetadata.CopyrightNotice,
                        "copyright preserved for man.jpg"
                    );
                    Assert.AreEqual(
                        oldMetadata.Creator,
                        newMetadata.Creator,
                        "creator preserved for man.jpg"
                    );
                    Assert.AreEqual(
                        oldMetadata.License.ToString(),
                        newMetadata.License.ToString(),
                        "license preserved for man.jpg"
                    );
                }
            }
        }

        [Test]
        public void GetBytesOfReducedImage_LargePngImageReduced()
        {
            // shirtWithTransparentBg.png: PNG image data, 2208 x 2400, 8-bit/color RGBA, non-interlaced

            var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "shirt.png"
            );
            var originalBytes = File.ReadAllBytes(path);
            var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(
                path,
                true,
                new ImagePublishSettings()
            );
            Assert.Greater(
                originalBytes.Length,
                reducedBytes.Length,
                "shirt.png is reduced from 2208x2400"
            );
            using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
            {
                var oldMetadata = Metadata.FromFile(path);
                RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
                var newMetadata = Metadata.FromFile(tempFile.Path);
                if (oldMetadata.IsEmpty)
                {
                    Assert.IsTrue(newMetadata.IsEmpty);
                }
                else
                {
                    Assert.IsFalse(newMetadata.IsEmpty);
                    Assert.AreEqual(
                        oldMetadata.CopyrightNotice,
                        newMetadata.CopyrightNotice,
                        "copyright preserved for shirt.png"
                    );
                    Assert.AreEqual(
                        oldMetadata.Creator,
                        newMetadata.Creator,
                        "creator preserved for shirt.png"
                    );
                    Assert.AreEqual(
                        oldMetadata.License.ToString(),
                        newMetadata.License.ToString(),
                        "license preserved for shirt.png"
                    );
                }
            }
        }

        [Test]
        public void GetBytesOfReducedImage_LargishPngImageNotReduced_AlreadyTransparent()
        {
            // levels.png: PNG image data, 643 x 963, mostly transparent
            // non-transparent pixels are white (and need to stay that way!)
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "levels.png"
            );
            var originalBytes = File.ReadAllBytes(path);
            var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(
                path,
                true,
                new ImagePublishSettings()
            );
            Assert.AreEqual(originalBytes.Length, reducedBytes.Length, "levels.png is not reduced");
        }

        [Test]
        public void GetBytesOfReducedImage_LargeJpgImageReduced()
        {
            // LakePendOreille.jpg:        JPEG image data, JFIF standard 1.01, ... precision 8, 3264x2448, frames 3

            var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "LakePendOreille.jpg"
            );
            var originalBytes = File.ReadAllBytes(path);
            var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(
                path,
                true,
                new ImagePublishSettings()
            );
            Assert.Greater(
                originalBytes.Length,
                reducedBytes.Length,
                "LakePendOreille.jpg is reduced from 3264x2448"
            );
            using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
            {
                var oldMetadata = Metadata.FromFile(path);
                RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
                var newMetadata = Metadata.FromFile(tempFile.Path);
                if (oldMetadata.IsEmpty)
                {
                    Assert.IsTrue(newMetadata.IsEmpty);
                }
                else
                {
                    Assert.IsFalse(newMetadata.IsEmpty);
                    Assert.AreEqual(
                        oldMetadata.CopyrightNotice,
                        newMetadata.CopyrightNotice,
                        "copyright preserved for LakePendOreille.jpg"
                    );
                    Assert.AreEqual(
                        oldMetadata.Creator,
                        newMetadata.Creator,
                        "creator preserved for LakePendOreille.jpg"
                    );
                    Assert.AreEqual(
                        oldMetadata.License.ToString(),
                        newMetadata.License.ToString(),
                        "license preserved for LakePendOreille.jpg"
                    );
                }
            }
        }

        [Test]
        public void GetBytesOfReducedImage_LargePng24bImageReduced()
        {
            // lady24b.png:        PNG image data, 24bit depth, 3632w x 3872h

            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "lady24b.png"
            );
            var originalBytes = File.ReadAllBytes(path);
            var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(
                path,
                true,
                new ImagePublishSettings()
            );
            // Is it reduced, even tho' we switched from 24bit depth to 32bit depth?
            Assert.Greater(
                originalBytes.Length,
                reducedBytes.Length,
                "lady24b.png is reduced from 3632x3872"
            );
            using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
            {
                RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
                using (var newImage = PalasoImage.FromFileRobustly(tempFile.Path))
                    Assert.AreEqual(
                        PixelFormat.Format32bppArgb,
                        newImage.Image.PixelFormat,
                        "should have switched to 32bit depth"
                    );
            }
        }

        [Test]
        public void CompressBookForDevice_PointsAtDeviceXMatter()
        {
            var bookHtml =
                @"<html><head>
						<link rel='stylesheet' href='Basic Book.css' type='text/css'></link>
						<link rel='stylesheet' href='Traditional-XMatter.css' type='text/css'></link>
					</head><body>
					<div class='bloom-page' id='guid1'></div>
			</body></html>";
            TestHtmlAfterCompression(
                bookHtml,
                assertionsOnResultingHtmlString: html =>
                {
                    var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//html/head/link[@rel='stylesheet' and @href='Device-XMatter.css' and @type='text/css']",
                            1
                        );
                    AssertThatXmlIn
                        .Dom(htmlDom)
                        .HasNoMatchForXpath(
                            "//html/head/link[@rel='stylesheet' and @href='Traditional-XMatter.css' and @type='text/css']"
                        );
                }
            );
        }

        class StubProgress : WebSocketProgress
        {
            public readonly List<string> MessagesNotLocalized = new List<string>();

            public override void MessageWithoutLocalizing(string message, ProgressKind kind)
            {
                MessagesNotLocalized.Add(message);
            }

            public readonly List<string> ErrorsNotLocalized = new List<string>();

            public override void Message(
                string idSuffix,
                string comment,
                string message,
                ProgressKind progressKind,
                bool useL10nIdPrefix = true
            )
            {
                MessagesNotLocalized.Add(string.Format(message));
            }

            public override void MessageWithParams(
                string id,
                string comment,
                string message,
                ProgressKind kind,
                params object[] parameters
            )
            {
                MessagesNotLocalized.Add(string.Format(message, parameters));
            }
        }

        class StubFontFinder : IFontFinder
        {
            public StubFontFinder()
            {
                FontsWeCantInstall = new HashSet<string>();
            }

            public Dictionary<string, string> FilesForFont = new Dictionary<string, string>();

            public string GetFileForFont(string fontName, string fontStyle, string fontWeight)
            {
                FilesForFont.TryGetValue(fontName, out string result);
                return result;
            }

            public bool NoteFontsWeCantInstall { get; set; }
            public HashSet<string> FontsWeCantInstall { get; }
            public Dictionary<string, FontGroup> FontGroups = new Dictionary<string, FontGroup>();

            public FontGroup GetGroupForFont(string fontName)
            {
                FontGroup result;
                FontGroups.TryGetValue(fontName, out result);
                return result;
            }
        }

        [Test]
        public void EmbedFonts_EmbedsExpectedFontsAndReportsOthers()
        {
            var bookHtml =
                @"<html><head>
						<link rel='stylesheet' href='Basic Book.css' type='text/css'></link>
						<link rel='stylesheet' href='Traditional-XMatter.css' type='text/css'></link>
						<link rel='stylesheet' href='CustomBookStyles.css' type='text/css'></link>
						<style type='text/css' title='userModifiedStyles'>
							/*<![CDATA[*/
							.Times-style[lang='tpi'] { font-family: Times New Roman ! important; font-size: 12pt  }
							.Times-style[lang='zh'] { font-family: Wen Yei ! important; font-size: 12pt  }
							/*]]>*/
						</style>
					</head><body>
					<div class='bloom-page' id='guid1'></div>
			</body></html>";
            var testBook = CreateBookWithPhysicalFile(bookHtml, bringBookUpToDate: false);
            var fontFileFinder = new StubFontFinder();
            FontsApi.AvailableFontMetadataDictionary.Clear();
            using (
                var tempFontFolder = new TemporaryFolder(
                    "EmbedFonts_EmbedsExpectedFontsAndReportsOthers"
                )
            )
            {
                fontFileFinder.NoteFontsWeCantInstall = true;

                // Fonts called for in HTML
                var timesNewRomanFileName = "Times New Roman R.ttf";
                var tnrPath = Path.Combine(tempFontFolder.Path, timesNewRomanFileName);
                File.WriteAllText(tnrPath, "This is phony TNR");
                var tnrGroup = new FontGroup { Normal = tnrPath };
                var timesMeta = new FontMetadata("Times New Roman", tnrGroup);
                timesMeta.SetSuitabilityForTest(FontMetadata.kOK);
                FontsApi.AvailableFontMetadataDictionary.Add("Times New Roman", timesMeta);

                var wenYeiFileName = "Wen Yei.ttc";
                var wenYeiPath = Path.Combine(tempFontFolder.Path, wenYeiFileName);
                File.WriteAllText(wenYeiPath, "This is phony Wen Yei font collection");
                var wenYeiGroup = new FontGroup { Normal = wenYeiPath };
                var wenYeiMeta = new FontMetadata("Wen Yei", wenYeiGroup);
                // Code marks invalid based on .ttc filename extension
                FontsApi.AvailableFontMetadataDictionary.Add("Wen Yei", wenYeiMeta);

                // Font called for in custom styles CSS
                var calibreFileName = "Calibre R.ttf";
                var calibrePath = Path.Combine(tempFontFolder.Path, calibreFileName);
                File.WriteAllBytes(calibrePath, new byte[200008]); // we want something with a size greater than zero in megs
                var calibreGroup = new FontGroup { Normal = calibrePath };
                var calibreMeta = new FontMetadata("Calibre", calibreGroup);
                calibreMeta.SetSuitabilityForTest(FontMetadata.kOK);
                FontsApi.AvailableFontMetadataDictionary.Add("Calibre", calibreMeta);

                fontFileFinder.FontGroups["Times New Roman"] = tnrGroup;
                fontFileFinder.FilesForFont["Times New Roman"] = tnrPath;
                fontFileFinder.FontGroups["Wen Yei"] = wenYeiGroup;
                fontFileFinder.FilesForFont["Wen Yei"] = wenYeiPath;
                fontFileFinder.FontGroups["Calibre"] = calibreGroup;
                fontFileFinder.FilesForFont["Calibre"] = calibrePath;
                fontFileFinder.FontsWeCantInstall.Add("NotAllowed");
                // And "NotFound" just doesn't get a mention anywhere.
                PublishHelper.ClearFontMetadataMapForTests();

                var stubProgress = new StubProgress();

                var customStylesPath = Path.Combine(
                    testBook.FolderPath,
                    "customCollectionStyles.css"
                );
                File.WriteAllText(
                    customStylesPath,
                    ".someStyle {font-family:'Calibre';} .otherStyle {font-family: 'NotFound';} .yetAnother {font-family:'NotAllowed';}"
                );

                HashSet<PublishHelper.FontInfo> fontsWanted = new HashSet<PublishHelper.FontInfo>();
                fontsWanted.Add(
                    new PublishHelper.FontInfo
                    {
                        fontName = "Times New Roman",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                );
                fontsWanted.Add(
                    new PublishHelper.FontInfo
                    {
                        fontName = "Wen Yei",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                );
                fontsWanted.Add(
                    new PublishHelper.FontInfo
                    {
                        fontName = "Calibre",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                );
                fontsWanted.Add(
                    new PublishHelper.FontInfo
                    {
                        fontName = "NotAllowed",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                );
                fontsWanted.Add(
                    new PublishHelper.FontInfo
                    {
                        fontName = "NotFound",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ); // probably wouldn't happen with new approach for fonts, but leave in the test

                BloomPubMaker.EmbedFonts(testBook, stubProgress, fontsWanted, fontFileFinder);

                Assert.That(File.Exists(Path.Combine(testBook.FolderPath, timesNewRomanFileName)));
                Assert.That(File.Exists(Path.Combine(testBook.FolderPath, calibreFileName)));
                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member("Checking Times New Roman font: License OK for embedding.")
                );
                // 0.00 megs is culture-specific; ignore that part.
                Assert.That(
                    stubProgress.MessagesNotLocalized.Any(
                        s =>
                            s.StartsWith("Embedding font Times New Roman at a cost of 0")
                            && s.EndsWith("00 megs")
                    )
                );
                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member(
                        "This book has text in a font named \"Wen Yei\". Bloom cannot publish this font's format (.ttc)."
                    )
                );
                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member("Bloom will substitute \"Andika\" instead.")
                );
                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member("Checking Calibre font: License OK for embedding.")
                );
                // 0.20 megs is culture-specific.
                Assert.That(
                    stubProgress.MessagesNotLocalized.Any(
                        s =>
                            s.StartsWith("Embedding font Calibre at a cost of 0")
                            && s.EndsWith("20 megs")
                    )
                );

                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member(
                        "This book has text in a font named \"NotAllowed\". The license for \"NotAllowed\" does not permit Bloom to embed the font in the book."
                    )
                );
                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member("Bloom will substitute \"Andika\" instead.")
                );
                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member(
                        "This book has text in a font named \"NotFound\", but Bloom could not find that font on this computer."
                    )
                );
                Assert.That(
                    stubProgress.MessagesNotLocalized,
                    Has.Member("Bloom will substitute \"Andika\" instead.")
                );

                var fontSourceRulesPath = Path.Combine(testBook.FolderPath, "fonts.css");
                var fontSource = RobustFile.ReadAllText(fontSourceRulesPath);
                // We're OK with these in either order.
                string lineTimes =
                    "@font-face {font-family:'Times New Roman'; font-weight:normal; font-style:normal; src:url('Times New Roman R.ttf') format('opentype');}"
                    + Environment.NewLine;
                string lineCalibre =
                    "@font-face {font-family:'Calibre'; font-weight:normal; font-style:normal; src:url('Calibre R.ttf') format('opentype');}"
                    + Environment.NewLine;
                Assert.That(
                    fontSource,
                    Is.EqualTo(lineTimes + lineCalibre).Or.EqualTo(lineCalibre + lineTimes)
                );
                AssertThatXmlIn
                    .Dom(testBook.RawDom)
                    .HasSpecifiedNumberOfMatchesForXpath("//link[@href='fonts.css']", 1);

                var styleNode = testBook.OurHtmlDom.SelectSingleNode(
                    "//head/style[@type='text/css' and @title='userModifiedStyles']"
                );
                Assert.That(styleNode, Is.Not.Null);
                var styleText = styleNode.InnerXml;
                Assert.That(
                    styleText.Contains(
                        ".Times-style[lang='tpi'] { font-family: Times New Roman ! important; font-size: 12pt  }"
                    ),
                    Is.True,
                    "Times New Roman reference unchanged"
                );
                Assert.That(
                    styleText.Contains("Wen Yei"),
                    Is.False,
                    "Wen Yei reference has been removed"
                );
                Assert.That(
                    styleText.Contains(
                        ".Times-style[lang='zh'] { font-family: Andika !important; font-size: 12pt  }"
                    ),
                    Is.True,
                    "Wen Yei reference replaced with Andika"
                );

                var customCss = File.ReadAllText(customStylesPath);
                Assert.That(
                    customCss.Contains(".someStyle {font-family:'Calibre';}"),
                    Is.True,
                    "Calibre reference unchanged"
                );
                Assert.That(
                    customCss.Contains("NotFound"),
                    Is.False,
                    "NotFound reference has been removed"
                );
                Assert.That(
                    customCss.Contains(".otherStyle {font-family: 'Andika';}"),
                    Is.True,
                    "NotFound reference replaced with Andika"
                );
                Assert.That(
                    customCss.Contains("NotAllowed"),
                    Is.False,
                    "NotAllowed reference has been removed"
                );
                Assert.That(
                    customCss.Contains(".yetAnother {font-family: 'Andika';}"),
                    Is.True,
                    "NotAllowed reference replaced with Andika"
                );
            }
        }

        private class ZipHtmlObj
        {
            internal ZipHtmlObj(ZipFile zip, string html)
            {
                ZipFile = zip;
                Html = html;
            }

            internal ZipFile ZipFile { get; }
            internal string Html { get; }
        }

        private void TestHtmlAfterCompression(
            string originalBookHtml,
            Action<string> actionsOnFolderBeforeCompressing = null,
            Action<string> assertionsOnResultingHtmlString = null,
            Action<ZipHtmlObj> assertionsOnZipArchive = null,
            Action<ZipFile> assertionsOnRepeat = null,
            string branding = "Default",
            HashSet<string> languagesToInclude = null,
            string creator = BloomPubMaker.kCreatorBloom
        )
        {
            var testBook = CreateBookWithPhysicalFile(originalBookHtml, bringBookUpToDate: true);

            // Branding must be something other than "Default" or all the Enterprise-only features get stripped
            testBook.CollectionSettings.BrandingProjectKey = branding;

            var bookFileName = Path.GetFileName(testBook.GetPathHtmlFile());

            actionsOnFolderBeforeCompressing?.Invoke(testBook.FolderPath);

            using (
                var bloomdTempFile = TempFile.WithFilenameInTempFolder(
                    testBook.Title + BloomPubMaker.BloomPubExtensionWithDot
                )
            )
            {
                var bloomPubPublishSettings = new BloomPubPublishSettings()
                {
                    PublishAsMotionBookIfApplicable = testBook
                        .BookInfo
                        .PublishSettings
                        .BloomPub
                        .PublishAsMotionBookIfApplicable
                };
                if (languagesToInclude != null)
                    bloomPubPublishSettings.LanguagesToInclude = languagesToInclude;
                BloomPubMaker.CreateBloomPub(
                    settings: bloomPubPublishSettings,
                    outputPath: bloomdTempFile.Path,
                    bookFolderPath: testBook.FolderPath,
                    bookServer: _bookServer,
                    progress: new NullWebSocketProgress(),
                    isTemplateBook: false,
                    creator: creator
                );
                var zip = new ZipFile(bloomdTempFile.Path);
                var newHtml = GetEntryContents(zip, bookFileName);
                var paramObj = new ZipHtmlObj(zip, newHtml);
                assertionsOnZipArchive?.Invoke(paramObj); // send in html in case we need to compare it with the zip contents
                assertionsOnResultingHtmlString?.Invoke(newHtml);
                if (assertionsOnRepeat != null)
                {
                    // compress it again! Used for checking important repeatable results
                    using (
                        var extraTempFile = TempFile.WithFilenameInTempFolder(
                            testBook.Title + "2" + BloomPubMaker.BloomPubExtensionWithDot
                        )
                    )
                    {
                        BloomPubMaker.CreateBloomPub(
                            bloomPubPublishSettings,
                            extraTempFile.Path,
                            testBook,
                            _bookServer,
                            new NullWebSocketProgress()
                        );
                        zip = new ZipFile(extraTempFile.Path);
                        assertionsOnRepeat(zip);
                    }
                }
            }
        }
    }
}
