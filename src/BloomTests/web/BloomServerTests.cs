// Copyright (c) 2014-2018 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.web.controllers;
using L10NSharp;
using L10NSharp.Windows.Forms;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using TemporaryFolder = SIL.TestUtilities.TemporaryFolder;

namespace BloomTests.web
{
    [TestFixture]
    public class BloomServerTests
    {
        private TemporaryFolder _folder;
        private BloomFileLocator _fileLocator;
        private string _collectionPath;
        private ILocalizationManager _localizationManager;

        [SetUp]
        public void Setup()
        {
            Logger.Init();
            _folder = new TemporaryFolder("BloomServerTests");
            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationDirectory =
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization");
            _localizationManager = LocalizationManagerWinforms.Create(
                "fr",
                "Bloom",
                "Bloom",
                "1.0.0",
                localizationDirectory,
                "SIL/Bloom",
                null,
                new string[] { }
            );

            ErrorReport.IsOkToInteractWithUser = false;
            _collectionPath = Path.Combine(_folder.Path, "TestCollection");
            var cs = new CollectionSettings(
                Path.Combine(_folder.Path, "TestCollection.bloomCollection")
            );
            _fileLocator = new BloomFileLocator(
                cs,
                new XMatterPackFinder(
                    new string[] { BloomFileLocator.GetFactoryXMatterDirectory() }
                ),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
        }

        [TearDown]
        public void TearDown()
        {
            _localizationManager.Dispose();
            LocalizationManager.ForgetDisposedManagers();
            _folder.Dispose();
            Logger.ShutDown();
        }

        [Test]
        public void CanGetImage()
        {
            // Setup
            using (var server = CreateBloomServer())
            using (var file = MakeTempImage())
            {
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + file.Path
                );

                // Execute
                server.MakeReply(transaction);

                // Verify
                Assert.IsTrue(transaction.ReplyImagePath.Contains(".png"));
            }
        }

        /// <summary>
        /// Tests retrieving an image that is not at the root of book-preview, but rather inside a subdirectory.
        /// </summary>
        [Test]
        public void CanGetActivityImage()
        {
            // Setup //

            // Mock up server.CurrentBook.FolderPath
            var testRootDir = Path.Combine(_folder.Path, "CanGetActivityImage");
            var mockBook = new Mock<Bloom.Book.Book>();
            mockBook.Setup(m => m.FolderPath).Returns(testRootDir);

            using (var server = CreateBloomServer(mockBook.Object))
            using (var file = MakeTempActivityImage(testRootDir))
            {
                var path = "book-preview/resources/image.png";
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + path
                );

                // Execute //
                server.MakeReply(transaction);

                // Verify //
                Assert.AreEqual(0, transaction.StatusCode); // or 200 would be ideal, but currently the code surprisingly returns 0.  404 would definitely be a failure though!
            }
        }

        [Test]
        public void CanGetPdf()
        {
            // Setup
            using (var server = CreateBloomServer())
            using (var file = TempFile.WithExtension(".pdf"))
            {
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + file.Path
                );

                // Execute
                server.MakeReply(transaction);

                // Verify
                Assert.IsTrue(transaction.ReplyImagePath.Contains(".pdf"));
            }
        }

        [Test]
        public void CanOpenConsecutivePorts_ReturnsFalseWhenAnyPortInRangeIsBlocked()
        {
            var startingPort = Enumerable
                .Range(38089, 2000)
                .FirstOrDefault(port => BloomServer.CanOpenConsecutivePorts(port, 2));

            Assert.That(
                startingPort,
                Is.GreaterThan(0),
                "Could not find a free consecutive port range for this test."
            );

            Assert.That(BloomServer.CanOpenConsecutivePorts(startingPort, 2), Is.True);

            using (var blocker = new TcpListener(IPAddress.Loopback, startingPort + 1))
            {
                blocker.Start();

                Assert.That(BloomServer.CanOpenConsecutivePorts(startingPort, 2), Is.False);
            }
        }

        /// <summary>
        /// A bundle we inject into a page at the server root imports its sibling chunks by bare
        /// name, so the server is regularly asked for files that sit directly in the browser folder
        /// with no directory in the request at all (BL-16577).
        ///
        /// It must resolve those to a full path. Finding a file that ships with Bloom may not depend
        /// on the process's current working directory: Windows File Explorer sets that to the folder
        /// of a file the user double-clicked, and any Open/Save dialog can move it mid-session.
        /// We deliberately do NOT move the current directory here to prove that - it is
        /// process-wide state, and doing so makes other tests in this assembly fail
        /// unpredictably - so instead we assert the property that a current-directory-relative
        /// lookup would violate: the path we resolved to and served is rooted.
        /// </summary>
        [Test]
        public void CanGetJavascriptDirectlyInBrowserRoot_AndResolvesItToAFullPath()
        {
            // Named like a Vite chunk (which is what the real requests are), but unique so we
            // neither collide with nor depend on any particular chunk of the current build.
            var chunkName = "BloomServerTestsChunk" + Guid.NewGuid().ToString("N") + ".js";
            var chunkPath = Path.Combine(BloomFileLocator.AbsoluteBrowserRoot, chunkName);
            using (var server = CreateBloomServer())
            {
                try
                {
                    RobustFile.WriteAllText(chunkPath, "// pretend chunk");

                    var transaction = MakeJavascriptRequest(server, chunkName);

                    Assert.That(transaction.StatusCode, Is.Not.EqualTo(404));
                    Assert.That(transaction.ReplyContents, Is.EqualTo("// pretend chunk"));
                    Assert.That(
                        Path.IsPathRooted(transaction.ReplyImagePath),
                        Is.True,
                        $"Served {transaction.ReplyImagePath}, which is not a full path, so finding it depended on the current working directory."
                    );
                    Assert.That(transaction.ReplyImagePath, Is.EqualTo(chunkPath));
                }
                finally
                {
                    RobustFile.Delete(chunkPath);
                }
            }
        }

        /// <summary>
        /// Requests a JS file by bare name. The assetv query parameter is what the server itself
        /// redirects JS requests to (see ProcessRequestAsync); supplying it up front keeps this
        /// test on the file-serving path instead of getting the redirect.
        /// </summary>
        private PretendRequestInfo MakeJavascriptRequest(BloomServer server, string fileName)
        {
            var transaction = new PretendRequestInfo(
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash + fileName + "?assetv=test"
            );
            server.MakeReply(transaction);
            return transaction;
        }

        [Test]
        public void ReportsMissingFile()
        {
            // Setup
            using (var server = CreateBloomServer())
            {
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "/non-existing-file.pdf"
                );

                // Execute
                server.MakeReply(transaction);

                // Verify
                Assert.That(transaction.StatusCode, Is.EqualTo(404));
                Assert.That(
                    Logger.LogText,
                    Contains.Substring("**BloomServer: File Missing: /non-existing-file.pdf")
                );
            }
        }

        /// <summary>
        /// A url containing the literal text "undefined" means some front-end code built it out of a
        /// JavaScript value it did not have yet - always our bug, never a file the user is missing.
        /// The referrer is what tells us which page did it, so it has to reach the report. BL-16666.
        /// </summary>
        [Test]
        public void ReportsMissingFile_UndefinedInPath_LogsTheReferringPage()
        {
            using (var server = CreateBloomServer())
            {
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "audio/undefined",
                    referer: "http://localhost:8089/bloom/SomeBook/SomePage.htm"
                );

                server.MakeReply(transaction);

                Assert.That(
                    Logger.LogText,
                    Contains.Substring("SomeBook/SomePage.htm"),
                    "The referring page should be in the report; without it we cannot tell which of our pages built the bad url."
                );
                Assert.That(
                    Logger.LogText,
                    Contains.Substring("JavaScript value turned into text")
                );
            }
        }

        /// <summary>
        /// The referrer header is optional, so the diagnostics must still say something useful (and
        /// not crash) when the browser doesn't send one.
        /// </summary>
        [Test]
        public void ReportsMissingFile_UndefinedInPathAndNoReferer_StillReports()
        {
            using (var server = CreateBloomServer())
            {
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "undefined"
                );

                server.MakeReply(transaction);

                Assert.That(Logger.LogText, Contains.Substring("the browser did not say"));
            }
        }

        [TestCase("audio/undefined", true)]
        [TestCase("undefined", true)]
        [TestCase("C:/Users/joe/AppData/Local/Temp/undefined", true)]
        [TestCase("images/null", true)]
        [TestCase("pages/NaN.htm", false, Description = "NaN.htm is a filename, not a bare value")]
        [TestCase("undefined.png", false, Description = "so is undefined.png")]
        [TestCase(
            "audio/Undefined",
            false,
            Description = "JavaScript never produces this spelling"
        )]
        [TestCase("undefinedThings/x.png", false)]
        [TestCase("myBook/audio/abc123.mp3", false)]
        [TestCase("", false)]
        public void LooksLikeAJavascriptValueInAUrl_Works(string localPath, bool expected)
        {
            Assert.That(
                BloomServer.LooksLikeAJavascriptValueInAUrl(localPath),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void SupportsHandlerInjection()
        {
            // Setup
            using (var server = CreateBloomServer())
            {
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                        + "api/thisWontWorkWithoutInjectionButWillWithIt"
                );
                EndpointHandler testFunc = (request) =>
                {
                    Assert.That(
                        request.LocalPath(),
                        Does.Contain("thisWontWorkWithoutInjectionButWillWithIt")
                    );
                    Assert.That(
                        request.CurrentCollectionSettings,
                        Is.EqualTo(server.CurrentCollectionSettings)
                    );
                    request.ReplyWithText("Did It!");
                };
                server.ApiHandler.RegisterEndpointHandler(
                    "thisWontWorkWithoutInjectionButWillWithIt",
                    testFunc,
                    true
                );

                // Execute
                server.MakeReply(transaction);

                // Verify
                Assert.That(transaction.ReplyContents, Is.EqualTo("Did It!"));
            }
        }

        [Test]
        public async Task MissingLegacyBrandingApiEndpoint_DoesNotReportNonFatalProblem()
        {
            using (var server = CreateBloomServer())
            {
                server.ApiHandler.RegisterEndpointHandler(
                    "existingProjectEndpoint",
                    request => request.ReplyWithText("ok"),
                    true
                );
                NonFatalProblem.LastNonFatalProblemReported = null;
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                        + "api/branding/image?id=back-cover-outside.png"
                );

                await server.ApiHandler.ProcessRequestAsync(transaction, "api/branding/image");

                Assert.That(transaction.StatusCode, Is.EqualTo(404));
                Assert.That(transaction.StatusDescription, Is.EqualTo("API endpoint not found"));
                Assert.That(NonFatalProblem.LastNonFatalProblemReported, Is.Null);
            }
        }

        [Test]
        public async Task MissingNonLegacyApiEndpoint_ReportsNonFatalProblem()
        {
            using (var server = CreateBloomServer())
            {
                server.ApiHandler.RegisterEndpointHandler(
                    "existingProjectEndpoint",
                    request => request.ReplyWithText("ok"),
                    true
                );
                NonFatalProblem.LastNonFatalProblemReported = null;
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/notARealEndpoint"
                );

                await server.ApiHandler.ProcessRequestAsync(transaction, "api/notARealEndpoint");

                Assert.That(transaction.StatusCode, Is.EqualTo(404));
                Assert.That(transaction.StatusDescription, Is.EqualTo("API endpoint not found"));
                Assert.That(
                    NonFatalProblem.LastNonFatalProblemReported,
                    Does.Contain(
                        "Server could not find an API endpoint for /bloom/api/notARealEndpoint"
                    )
                );
            }
        }

        [Test]
        public void RegisterBoolEndpointHandler_Works()
        {
            // Setup
            using (var server = CreateBloomServer())
            {
                server.SetCollectionSettingsDuringInitialization(new CollectionSettings());

                // set boolean handler
                server.ApiHandler.RegisterBooleanEndpointHandler(
                    "allowNewBooks",
                    // get action
                    request => request.CurrentCollectionSettings.AllowDeleteBooks,
                    // post action
                    (request, myBoolean) =>
                        request.CurrentCollectionSettings.AllowNewBooks = myBoolean,
                    true
                );

                // Get
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/allowNewBooks"
                );
                server.CurrentCollectionSettings.AllowNewBooks = true;

                // Execute get
                server.MakeReply(transaction);

                // Verify get
                Assert.That(transaction.ReplyContents, Is.EqualTo("true"));

                // Make sure
                server.CurrentCollectionSettings.AllowNewBooks = false;
                server.MakeReply(transaction);
                Assert.That(transaction.ReplyContents, Is.EqualTo("false"));

                // Post
                transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/allowNewBooks",
                    HttpMethods.Post
                );
                transaction.SetPostJson("true");

                // Execute post
                server.MakeReply(transaction);

                // Verify post
                Assert.That(transaction.ReplyContents, Is.EqualTo("OK"));
                Assert.That(server.CurrentCollectionSettings.AllowNewBooks, Is.True);

                // Make sure
                transaction.SetPostJson("false");
                server.MakeReply(transaction);
                Assert.That(transaction.ReplyContents, Is.EqualTo("OK"));
                Assert.That(server.CurrentCollectionSettings.AllowNewBooks, Is.False);
            }
        }

        [Test]
        public void RegisterEnumEndpointHandler_Works()
        {
            // Setup
            var info = new BookInfo("", true);
            using (var server = CreateBloomServer(info))
            {
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/imageDesc"
                );
                server.CurrentBook.BookInfo.PublishSettings.Epub.HowToPublishImageDescriptions =
                    BookInfo.HowToPublishImageDescriptions.None;
                // set enum handler
                server.ApiHandler.RegisterEnumEndpointHandler(
                    "imageDesc",
                    // get action
                    request =>
                        request
                            .CurrentBook
                            .BookInfo
                            .PublishSettings
                            .Epub
                            .HowToPublishImageDescriptions,
                    // post action
                    (request, myEnum) =>
                        request
                            .CurrentBook
                            .BookInfo
                            .PublishSettings
                            .Epub
                            .HowToPublishImageDescriptions = myEnum,
                    true
                );

                // Execute get
                server.MakeReply(transaction);

                // Verify get
                Assert.That(transaction.ReplyContents, Is.EqualTo("None"));

                // HowToPublishImageDescriptions.Links was removed in Bloom 4.6
                // Try another
                server.CurrentBook.BookInfo.PublishSettings.Epub.HowToPublishImageDescriptions =
                    BookInfo.HowToPublishImageDescriptions.OnPage;
                server.MakeReply(transaction);
                Assert.That(transaction.ReplyContents, Is.EqualTo("OnPage"));

                // Post
                transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/imageDesc",
                    HttpMethods.Post
                );
                transaction.SetPostJson("OnPage");

                // Execute post
                server.MakeReply(transaction);

                // Verify post
                Assert.That(transaction.ReplyContents, Is.EqualTo("OK"));
                Assert.That(
                    server.CurrentBook.BookInfo.PublishSettings.Epub.HowToPublishImageDescriptions,
                    Is.EqualTo(BookInfo.HowToPublishImageDescriptions.OnPage)
                );

                // Try another
                transaction.SetPostJson("None");
                server.MakeReply(transaction);
                Assert.That(transaction.ReplyContents, Is.EqualTo("OK"));
                Assert.That(
                    server.CurrentBook.BookInfo.PublishSettings.Epub.HowToPublishImageDescriptions,
                    Is.EqualTo(BookInfo.HowToPublishImageDescriptions.None)
                );
            }
        }

        [Test]
        public void Topics_ReturnsFrenchFor_NoTopic_()
        {
            var topics = QueryServerForJson("api/editView/topics").Topics;
            var topicToTest = topics.First(t => t.Contains("No Topic"));
            var topicObject = JsonConvert.DeserializeObject<Topic>(topicToTest);
            Assert.AreEqual("Aucun thème", topicObject.translated);
        }

        [Test]
        public void Topics_ReturnsFrenchFor_Dictionary_()
        {
            var topics = QueryServerForJson("api/editView/topics").Topics;
            var topicToTest = topics.First(t => t.Contains("Dictionary"));
            var topicObject = JsonConvert.DeserializeObject<Topic>(topicToTest);
            Assert.AreEqual("Dictionnaire", topicObject.translated);
        }

        private TopicInfo QueryServerForJson(string query)
        {
            using (var server = CreateBloomServer())
            {
                var editingViewApi = new EditingViewApi();
                editingViewApi.RegisterWithApiHandler(server.ApiHandler);
                var transaction = new PretendRequestInfo(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + query
                );
                server.MakeReply(transaction);
                Debug.WriteLine(transaction.ReplyContents);
                return JsonConvert.DeserializeObject<TopicInfo>(transaction.ReplyContents);
            }
        }

        private class TopicInfo
        {
            [JsonProperty]
            internal string Current { get; set; }

            [JsonProperty]
            internal string[] Topics { get; set; }
        }

        private class Topic
        {
            [JsonProperty]
            internal string englishKey { get; set; }

            [JsonProperty]
            internal string translated { get; set; }
        }

        private BloomServer CreateBloomServer(BookInfo info = null) =>
            CreateBloomServer(new Bloom.Book.Book(info));

        private BloomServer CreateBloomServer(Bloom.Book.Book book)
        {
            var bookSelection = new BookSelection();
            bookSelection.SelectBook(book);
            return new BloomServer(
                new RuntimeImageProcessor(new BookRenamedEvent()),
                bookSelection,
                _fileLocator
            );
        }

        private TempFile MakeTempImage()
        {
            var file = TempFile.WithExtension(".png");
            File.Delete(file.Path);
            using (var x = new Bitmap(100, 100))
            {
                x.Save(file.Path, ImageFormat.Png);
            }
            return file;
        }

        private TempFile MakeTempActivityImage(string testRootDir)
        {
            var directory = Path.Combine(testRootDir, "resources");
            Directory.CreateDirectory(directory);
            var tempFileRelativePath = Path.Combine(directory, "image.png");
            // This must be a real image, and the file must be closed once written. It used to be
            // RobustFile.Create(path), which left the returned stream open (so the file was locked)
            // and the file empty (not a valid PNG); the server's image processing then burned
            // ~20 seconds in RetryUtility retry loops before giving up on the unreadable image.
            using (var x = new Bitmap(100, 100))
            {
                x.Save(tempFileRelativePath, ImageFormat.Png);
            }
            return TempFile.TrackExisting(tempFileRelativePath);
        }

        [Test]
        public void CanRetrieveContentOfFakeTempFile_ButOnlyUntilDisposed()
        {
            using (var server = CreateBloomServer())
            {
                var html = @"<html ><head></head><body>here it is</body></html>";
                var dom = new HtmlDom(html);
                dom.BaseForRelativePaths = _folder.Path.ToLocalhost();
                string url;
                using (var fakeTempFile = BloomServer.MakeInMemoryHtmlFileInBookFolder(dom))
                {
                    url = fakeTempFile.Key;
                    var transaction = new PretendRequestInfo(url);

                    // Execute
                    server.MakeReply(transaction);

                    // Verify
                    // Whitespace inserted by CreateHtml5StringFromXml seems to vary across versions and platforms.
                    // I would rather verify the actual output, but don't want this test to be fragile, and the
                    // main point is that we get a file with the DOM content.
                    Assert.That(
                        transaction.ReplyContents,
                        Is.EqualTo(dom.getHtmlStringDisplayOnly())
                    );
                }
                server.DoIdleTasksIfNoActivity();
                var transactionFail = new PretendRequestInfo(url);

                // Execute
                server.MakeReply(transactionFail);

                // Verify
                Assert.That(transactionFail.StatusCode, Is.EqualTo(404));
            }
        }

        [Test]
        [TestCase(InMemoryHtmlFileSource.JustCheckingPage, false)]
        [TestCase(InMemoryHtmlFileSource.Frame, true)]
        [TestCase(InMemoryHtmlFileSource.Nav, true)]
        [TestCase(InMemoryHtmlFileSource.Normal, true)]
        [TestCase(InMemoryHtmlFileSource.Pagelist, false)]
        [TestCase(InMemoryHtmlFileSource.Preview, true)]
        [TestCase(InMemoryHtmlFileSource.Pub, true)]
        [TestCase(InMemoryHtmlFileSource.Thumb, false)]
        public void ServerKnowsDifferenceBetweenRealAndThumbVideos(
            InMemoryHtmlFileSource source,
            bool expectVideo
        )
        {
            using (var server = CreateBloomServer())
            {
                const string html =
                    @"<html ><head></head><body>
						<div class='bloom-page'>
							<div id='1' class='bloom-videoContainer bloom-noVideoSelected bloom-leadingElement bloom-selected'>
								<video>
									<source src='video/randommp4filename.mp4#t=0.0,4.6'>
									</source>
								</video>
							</div>
							<div class='otherStuff'>
							</div>
							<div id='2' class='bloom-videoContainer'>
								<video>
									<source src='video/otherrandomfilename.mp4'>
									</source>
								</video>
							</div>
							<div id='3' class='bloom-videoContainer bloom-noVideoSelected bloom-leadingElement bloom-selected'>
							</div>
						</div>
						<div class='afterStuff'>
						</div>
					</body></html>";
                var dom = new HtmlDom(html) { BaseForRelativePaths = _folder.Path.ToLocalhost() };
                using (
                    var fakeTempFile = BloomServer.MakeInMemoryHtmlFileInBookFolder(
                        dom,
                        true,
                        true,
                        source
                    )
                )
                {
                    var url = fakeTempFile.Key;
                    var transaction = new PretendRequestInfo(url);

                    // Execute
                    server.MakeReply(transaction);

                    // Verify
                    var contents = transaction.ReplyContents;
                    if (expectVideo)
                    {
                        AssertThatXmlIn
                            .String(contents)
                            .HasSpecifiedNumberOfMatchesForXpath(
                                "//div[contains(@class,'bloom-videoContainer')]",
                                3
                            );
                        AssertThatXmlIn
                            .String(contents)
                            .HasNoMatchForXpath("//div[contains(@class,'bloom-imageContainer')]");
                    }
                    else
                    {
                        AssertThatXmlIn
                            .String(contents)
                            .HasSpecifiedNumberOfMatchesForXpath(
                                "//div[contains(@class,'bloom-imageContainer')]",
                                3
                            );
                        AssertThatXmlIn
                            .String(contents)
                            .HasNoMatchForXpath("//div[contains(@class,'bloom-videoContainer')]");
                    }
                }
            }
        }

        [Test]
        public void CanRetrieveContentOfFakeTempFile_WhenFolderContainsAmpersand_ViaJavaScript()
        {
            var dom = SetupDomWithAmpersandInTitle();
            // the 'true' parameter simulates calling BloomServer via JavaScript
            var transaction = CreateServerMakeSimPageMakeReply(dom, true);
            // Verify
            // Whitespace inserted by CreateHtml5StringFromXml seems to vary across versions and platforms.
            // I would rather verify the actual output, but don't want this test to be fragile, and the
            // main point is that we get a file with the DOM content.
            Assert.That(transaction.ReplyContents, Is.EqualTo(dom.getHtmlStringDisplayOnly()));
        }

        [Test]
        public void CanRetrieveContentOfFakeTempFile_WhenFolderContainsAmpersand_NotViaJavaScript()
        {
            var dom = SetupDomWithAmpersandInTitle();
            var transaction = CreateServerMakeSimPageMakeReply(dom);
            // Verify
            // Whitespace inserted by CreateHtml5StringFromXml seems to vary across versions and platforms.
            // I would rather verify the actual output, but don't want this test to be fragile, and the
            // main point is that we get a file with the DOM content.
            Assert.That(transaction.ReplyContents, Is.EqualTo(dom.getHtmlStringDisplayOnly()));
        }

        private HtmlDom SetupDomWithAmpersandInTitle()
        {
            var ampSubfolder = Path.Combine(_folder.Path, "Using &lt;, &gt;, & &amp; in HTML");
            Directory.CreateDirectory(ampSubfolder);
            var html =
                @"<html ><head><title>Using &lt;lt;, &gt;gt;, &amp; &amp;amp; in HTML</title></head><body>here it is</body></html>";
            var dom = new HtmlDom(html);
            dom.BaseForRelativePaths = ampSubfolder.ToLocalhost();
            return dom;
        }

        private PretendRequestInfo CreateServerMakeSimPageMakeReply(
            HtmlDom dom,
            bool simulateCallingFromJavascript = false
        )
        {
            PretendRequestInfo transaction;
            using (var server = CreateBloomServer())
            {
                using (
                    var fakeTempFile = BloomServer.MakeInMemoryHtmlFileInBookFolder(
                        dom,
                        simulateCallingFromJavascript
                    )
                )
                {
                    var url = fakeTempFile.Key;
                    transaction = new PretendRequestInfo(
                        url,
                        forPrinting: false,
                        forSrcAttr: simulateCallingFromJavascript
                    );

                    // Execute
                    server.MakeReply(transaction);
                }
            }
            return transaction;
        }

        private void SetupCssTests()
        {
            // create collection directory
            Directory.CreateDirectory(_collectionPath);

            // customCollectionStyles.css
            var cssFile = Path.Combine(_collectionPath, "customCollectionStyles.css");
            RobustFile.WriteAllText(cssFile, @".customCollectionStylesCssTest{}");

            // create book directory
            var bookPath = Path.Combine(_collectionPath, "TestBook");
            Directory.CreateDirectory(bookPath);

            // defaultLangStyles.css
            cssFile = Path.Combine(bookPath, "defaultLangStyles.css");
            RobustFile.WriteAllText(cssFile, @".defaultLangStylesCssTest{}");

            cssFile = Path.Combine(bookPath, "customCollectionStyles.css");
            RobustFile.WriteAllText(cssFile, @".customCollectionStylesCssTest{}");

            cssFile = Path.Combine(bookPath, "ForUnitTest-XMatter.css");
            RobustFile.WriteAllText(cssFile, @"This is the one in the book");

            // Factory-XMatter.css
            cssFile = Path.Combine(bookPath, "Factory-XMatter.css");
            RobustFile.WriteAllText(cssFile, @".factoryXmatterCssTest{}");

            // customBookStyles.css
            cssFile = Path.Combine(bookPath, "customBookStyles.css");
            RobustFile.WriteAllText(cssFile, @".customBookStylesCssTest{}");

            // miscStyles.css - a file name not distributed with or created by Bloom
            cssFile = Path.Combine(bookPath, "miscStyles.css");
            RobustFile.WriteAllText(cssFile, @".miscStylesCssTest{}");
        }

        [Test]
        public void GetCorrect_SettingsCollectionStylesCss()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                // Let's do it the way BookStorage.EnsureHasLinksToStylesheets() does it
                var filePath = "defaultLangStyles.css";
                var cssFile = Path.Combine(_folder.Path, "TestCollection", "TestBook", filePath);

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url);

                server.MakeReply(transaction);

                Assert.That(transaction.ReplyContents, Is.EqualTo(".defaultLangStylesCssTest{}"));
            }
        }

        [Test]
        public void GetCorrect_SettingsCollectionStylesCss_WhenMakingPdf()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                // Let's do it the way BookStorage.EnsureHasLinksToStylesheets() does it
                var filePath = "defaultLangStyles.css";
                var cssFile = Path.Combine(_folder.Path, "TestCollection", "TestBook", filePath);

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url, forPrinting: true);

                server.MakeReply(transaction);

                Assert.That(transaction.ReplyContents, Is.EqualTo(".defaultLangStylesCssTest{}"));
            }
        }

        [Test]
        public void GetCorrect_CustomCollectionStylesCss()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                // Let's do it the way BookStorage.EnsureHasLinksToStylesheets() does it
                var filePath = ".." + Path.DirectorySeparatorChar + "customCollectionStyles.css";
                var cssFile = Path.Combine(_folder.Path, "TestCollection", "TestBook", filePath);

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url);

                server.MakeReply(transaction);

                Assert.That(
                    transaction.ReplyContents,
                    Is.EqualTo(".customCollectionStylesCssTest{}")
                );
            }
        }

        [Test]
        public void GetCorrect_CustomCollectionStylesCss_WhenMakingPdf()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                // Let's do it the way BookStorage.EnsureHasLinksToStylesheets() does it
                var filePath = ".." + Path.DirectorySeparatorChar + "customCollectionStyles.css";
                var cssFile = Path.Combine(_folder.Path, "TestCollection", "TestBook", filePath);

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url, forPrinting: true);

                server.MakeReply(transaction);

                Assert.That(
                    transaction.ReplyContents,
                    Is.EqualTo(".customCollectionStylesCssTest{}")
                );
            }
        }

        [Test]
        public void RequestXMatter_OnlyExistsInBookAndDistFiles_ReturnsTheOneInDistFiles()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                var cssFile = Path.Combine(
                    _folder.Path,
                    "TestCollection",
                    "TestBook",
                    "ForUnitTest-XMatter.css"
                );

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url);

                server.MakeReply(transaction);

                Assert.AreEqual(
                    transaction.ReplyContents.Trim(),
                    "/* This is the one in DistFiles */"
                );
            }
        }

        [Test]
        public void RequestXMatterFiles_IsAProjectSpecificXMatter_FindsFiles()
        {
            using (var server = CreateBloomServer())
            {
                var transaction = new PretendRequestInfo(
                    "unit-test-project-specific-xmatter.css".ToLocalhost()
                );
                server.MakeReply(transaction);
                Assert.Greater(transaction.ReplyContents.Length, 100);

                transaction = new PretendRequestInfo(
                    "unit-test-project-specific-xmatter.html".ToLocalhost()
                );
                server.MakeReply(transaction);
                Assert.Greater(transaction.ReplyContents.Length, 100);
            }
        }

        [Test]
        public void GetCorrect_XmatterStylesCss()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                var cssFile = Path.Combine(
                    _folder.Path,
                    "TestCollection",
                    "TestBook",
                    "Factory-XMatter.css"
                );

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url);

                server.MakeReply(transaction);

                Assert.AreNotEqual(transaction.ReplyContents, ".factoryXmatterCssTest{}");
            }
        }

        [Test]
        public void GetCorrect_CustomBookStylesCss()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                var cssFile = Path.Combine(
                    _folder.Path,
                    "TestCollection",
                    "TestBook",
                    "customBookStyles.css"
                );

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url);

                server.MakeReply(transaction);

                Assert.That(transaction.ReplyContents, Is.EqualTo(".customBookStylesCssTest{}"));
            }
        }

        [Test]
        public void GetCorrect_CustomBookStylesCss_WhenMakingPdf()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                var cssFile = Path.Combine(
                    _folder.Path,
                    "TestCollection",
                    "TestBook",
                    "customBookStyles.css"
                );

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url, forPrinting: true);

                server.MakeReply(transaction);

                Assert.That(transaction.ReplyContents, Is.EqualTo(".customBookStylesCssTest{}"));
            }
        }

        [Test]
        public void GetCorrect_MiscStylesCss()
        {
            using (var server = CreateBloomServer())
            {
                SetupCssTests();
                var cssFile = Path.Combine(
                    _folder.Path,
                    "TestCollection",
                    "TestBook",
                    "miscStyles.css"
                );

                var url = cssFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url);

                server.MakeReply(transaction);

                Assert.That(transaction.ReplyContents, Is.EqualTo(".miscStylesCssTest{}"));
            }
        }

        [Test]
        public void HandleDoubleEncodedUrls()
        {
            // https://silbloom.myjetbrains.com/youtrack/issue/BL-3835 describes a problem that can occur when
            // Url encoded filenames are stored for the coverImage data.  One of the uploaded books
            // in the library has coverImage data stored as
            // <div data-book="coverImage" lang="*">
            //     The%20Moon%20and%20The%20Cap_Cover.png
            // </div>
            // and the image file was not being found by the server because a second level of encoding was
            // applied before requesting the file.  So this test arbitrarily applies a double level of Url
            // encoding (the third time) to ensure that the server can handle it.
            using (var server = CreateBloomServer())
            {
                Directory.CreateDirectory(_collectionPath);
                var txtFile = Path.Combine(_collectionPath, "File With Spaces.txt");
                const string testData = @"This is a test!\r\n";
                File.WriteAllText(txtFile, testData);

                // no Url encoding of spaces fed to server
                var url = txtFile.ToLocalhost();
                var transaction = new PretendRequestInfo(url);
                server.MakeReply(transaction);
                Assert.That(transaction.ReplyContents, Is.EqualTo(testData));

                // single level of Url encoding fed to server.
                // The Replace is a no-op and the comment that used to be here ("ToLocalHost() does
                // partial encoding, but not for spaces") was wrong: ToLocalhost escapes each path
                // component with Uri.EscapeDataString, which does encode a space, so by this point
                // there is no literal space left to replace. That makes this case identical to the
                // one above; kept because it costs nothing and states the intent explicitly.
                var encUrl = txtFile.ToLocalhost().Replace(" ", "%20");
                var encTransaction = new PretendRequestInfo(encUrl);
                Assert.That(encTransaction.RawUrl.Contains("%20"), Is.True);
                server.MakeReply(encTransaction);
                Assert.That(encTransaction.ReplyContents, Is.EqualTo(testData));

                // double level of Url encoding fed to server.
                // This works because ProcessAnyFileContent decodes a second time and keeps that
                // result only when it is what finds the file. Worth knowing: it is NOT that the
                // server decodes twice unconditionally -- a file whose name really contains "%41"
                // relies on the same guard to be left alone. See
                // FileNameContainingPercentThenHexDigits_IsFound below for the other side of it.
                var enc2TxtFile = txtFile.Replace(" ", "%20"); // encodes spaces
                var enc2Url = enc2TxtFile.ToLocalhost(); // double encodes spaces
                var enc2Transaction = new PretendRequestInfo(enc2Url);
                Assert.That(enc2Transaction.RawUrl.Contains("%2520"), Is.True);
                server.MakeReply(enc2Transaction);
                Assert.That(enc2Transaction.ReplyContents, Is.EqualTo(testData));
            }
        }

        /// <summary>
        /// BL-16669: a file name may itself contain a '%' followed by two hex digits, which looks
        /// exactly like an escape. Encoded for the url that '%' becomes "%25", and the server has
        /// to end up asking the disk for the real name rather than for the name you get by
        /// decoding one time too many.
        ///
        /// Note how this coexists with HandleDoubleEncodedUrls above, which needs the opposite:
        /// ProcessAnyFileContent decodes a second time but keeps the result ONLY if that is what
        /// finds the file (BloomServer.cs, "if (RobustFileExistsWithCaseCheck(tempPath))"). So the
        /// two cases are distinguished by what is actually on disk, not by guessing -- which is
        /// the same approach this branch adopted elsewhere.
        /// </summary>
        [Test]
        public void FileNameContainingPercentThenHexDigits_IsFound()
        {
            using (var server = CreateBloomServer())
            {
                Directory.CreateDirectory(_collectionPath);
                var txtFile = Path.Combine(_collectionPath, "photo%41.txt");
                const string testData = @"This is a test!\r\n";
                File.WriteAllText(txtFile, testData);
                // Sanity check: nothing exists under the wrongly-decoded name, so a pass here can
                // only mean the server found the file we actually made.
                Assert.That(
                    File.Exists(Path.Combine(_collectionPath, "photoA.txt")),
                    Is.False,
                    "test setup: the wrongly-decoded name must not exist"
                );

                var transaction = new PretendRequestInfo(txtFile.ToLocalhost());
                Assert.That(
                    transaction.RawUrl.Contains("photo%2541.txt"),
                    Is.True,
                    "test setup: the '%' in the name should have been encoded as %25"
                );

                server.MakeReply(transaction);

                Assert.That(transaction.ReplyContents, Is.EqualTo(testData));
            }
        }

        [Test]
        public void GetLocalPathRoot_UrlToFileInRoot_ReturnsDirectoryAsRoot()
        {
            string inputPath = "book-preview/image.png";

            var result =
                SIL.Reflection.ReflectionHelper.GetResult(
                    typeof(BloomServer),
                    "GetLocalPathRoot",
                    inputPath
                ) as string;

            Assert.That(result, Is.EqualTo("book-preview"));
        }

        [Test]
        public void GetLocalPathRoot_UrlWithSubdirectories_ReturnsFirstDirectoryAsRoot()
        {
            string inputPath = "book-preview/activities/Title/resources/image.png";

            var result =
                SIL.Reflection.ReflectionHelper.GetResult(
                    typeof(BloomServer),
                    "GetLocalPathRoot",
                    inputPath
                ) as string;

            Assert.That(result, Is.EqualTo("book-preview"));
        }

        [Test]
        public void GetLocalPathAfterRoot_UrlToFileInRoot_ReturnsFile()
        {
            string inputPath = "book-preview/image.png";

            var result =
                SIL.Reflection.ReflectionHelper.GetResult(
                    typeof(BloomServer),
                    "GetLocalPathAfterRoot",
                    inputPath
                ) as string;

            Assert.That(result, Is.EqualTo("image.png"));
        }

        [Test]
        public void GetLocalPathAfterRoot_UrlWithSubdirectories_ReturnsFirstDirectoryAsRoot()
        {
            string inputPath = "book-preview/activities/Title/resources/image.png";

            var result =
                SIL.Reflection.ReflectionHelper.GetResult(
                    typeof(BloomServer),
                    "GetLocalPathAfterRoot",
                    inputPath
                ) as string;

            Assert.That(result, Is.EqualTo("activities/Title/resources/image.png"));
        }

        [Test]
        [Platform(Exclude = "Linux", Reason = "Linux will just report a missing file.")]
        [TestCase("customcollectionstyles.css", true, true)] // exists, but case error
        [TestCase("customCollectionStyles.css", true, false)] // exact match
        [TestCase("xxyyzz.css", false, false)] // doesn't exist
        [TestCase("TestBook/customcollectionstyles.css", true, true)] // forward slash; case error
        [TestCase("TestBook\\customcollectionstyles.css", true, true)] // backward slash; case error
        [TestCase("Testbook/customCollectionStyles.css", true, true)] // directory case error
        [TestCase("TestBook/../customcollectionstyles.css", true, true)] // subdir parent case; case error
        [TestCase("TestBook/../customCollectionStyles.css", true, false)] // subdir parent case; exact match
        public void RobustFileExistsWithCaseCheck_Works(
            string requestedFile,
            bool fileExists,
            bool caseErrorLogged
        )
        {
            SetupCssTests(); // Creates all the above files and more, except for the 'xxyyzz.css'.

            var requestedFilePath = Path.Combine(_collectionPath, requestedFile);

            // SUT
            var result = BloomServer.RobustFileExistsWithCaseCheck(requestedFilePath);

            // Verification
            var latestLog = Logger.LogText;
            var fullRequestedPath = Path.GetFullPath(requestedFilePath); // The case check will apply GetFullPath().

            Assert.That(
                result,
                Is.EqualTo(fileExists),
                fileExists
                    ? "RobustFile.Exists() call failed."
                    : "RobustFile.Exists() incorrectly passes"
            );
#if DEBUG // Case check only happens in DEBUG mode.
            Assert.That(
                latestLog.Contains($"*** Case error occurred. {fullRequestedPath} does not match"),
                Is.EqualTo(caseErrorLogged),
                caseErrorLogged ? "No case error logged." : "Case error logged in error!"
            );
#endif
        }
    }
}
