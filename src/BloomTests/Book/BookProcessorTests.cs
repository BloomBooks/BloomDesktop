using System;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish;
using NUnit.Framework;
using TemporaryFolder = SIL.TestUtilities.TemporaryFolder;

namespace BloomTests.Book
{
    /// <summary>
    /// Tests for BookProcessor.WaitForJavascriptResult — the polling primitive that used to sit on top of
    /// Browser.RunJavascriptWithStringResult_Sync_Dangerous and now drives an OffScreenBrowser instead.
    ///
    /// It exercises the two things the off-screen page processor actually relies on: (1) waiting for a value
    /// that a page-load script sets on window, and (2) waiting for a value that a fire-and-forget script sets
    /// asynchronously some time later — plus the timeout path when the value never appears. These are the
    /// behaviors WaitForJavascriptResult provides to ProcessOnePage; the full ProcessBook path (which needs the
    /// whole editing bundle to initialize off-screen) is exercised by a manual process-book run.
    /// </summary>
    [TestFixture]
    [Category("SkipOnTeamCity")]
    public class BookProcessorTests
    {
        private TemporaryFolder _folder;
        private BloomServer _server;
        private OffScreenBrowser _browser;

        [SetUp]
        public void SetUp()
        {
            _folder = new TemporaryFolder("BookProcessorTests");
            var collectionSettings = new CollectionSettings();
            var fileLocator = new BloomFileLocator(
                collectionSettings,
                new XMatterPackFinder(new[] { BloomFileLocator.GetFactoryXMatterDirectory() }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            // A listening server so the OffScreenBrowser can actually fetch the in-memory page over http.
            _server = new BloomServer(
                new RuntimeImageProcessor(new BookRenamedEvent()),
                new BookSelection(),
                fileLocator
            );
            _server.EnsureListening();

            // Created after the server is listening, so navigation URLs resolve to it.
            _browser = new OffScreenBrowser();
        }

        [TearDown]
        public void TearDown()
        {
            _browser?.Dispose();
            _server?.Dispose();
            _folder?.Dispose();
        }

        // Builds a minimal page (served via BloomServer) whose body is the given markup.
        private HtmlDom MakePageDom(string bodyInner)
        {
            var dom = new HtmlDom($"<html><head></head><body>{bodyInner}</body></html>");
            dom.BaseForRelativePaths = _folder.Path;
            return dom;
        }

        private void NavigateTo(HtmlDom dom)
        {
            var ok = _browser.Navigate(dom, 10000, () => false, InMemoryHtmlFileSource.Frame);
            Assert.That(ok, Is.True, "SANITY: navigation to the test page should succeed");
        }

        [Test]
        public void WaitForJavascriptResult_ReturnsValueSetByPageLoadScript()
        {
            // A script that runs as the page loads and sets a window variable — the shape of the
            // __bloomEditablePageReady handshake ProcessOnePage waits on.
            NavigateTo(
                MakePageDom("<div>hello</div><script>window.__ready = 'readyValue';</script>")
            );

            var result = BookProcessor.WaitForJavascriptResult(
                _browser,
                "window.__ready || ''",
                "the page-load flag",
                "testPage"
            );

            Assert.That(result, Is.EqualTo("readyValue"));
        }

        [Test]
        public void WaitForJavascriptResult_ReturnsValueSetByFireAndForgetAfterDelay()
        {
            NavigateTo(MakePageDom("<div>hello</div>"));

            // SANITY: the value we're about to wait for is not set yet, so a non-empty result later
            // can only come from the fire-and-forget script.
            Assert.That(
                _browser.RunJavascript("window.__delayed || ''"),
                Is.Empty,
                "SANITY: the delayed value should not be set before we kick off the script"
            );

            // Fire-and-forget: kicks off work that finishes (and sets the window variable) only later,
            // mirroring captureContentForExternalProcessing stashing onto __bloomExternalPageContent.
            _browser.RunJavascriptFireAndForget(
                "setTimeout(function() { window.__delayed = 'delayedValue'; }, 300);"
            );

            var result = BookProcessor.WaitForJavascriptResult(
                _browser,
                "window.__delayed || ''",
                "the delayed value",
                "testPage"
            );

            Assert.That(result, Is.EqualTo("delayedValue"));
        }

        [Test]
        public void StartFreshBrowser_GivesFreshRendererButKeepsNavigating()
        {
            // Page 1 sets a marker on window.
            NavigateTo(MakePageDom("<div>page1</div><script>window.__marker = 'page1';</script>"));
            Assert.That(
                _browser.RunJavascript("window.__marker || ''"),
                Is.EqualTo("page1"),
                "SANITY: the first page should have set its marker"
            );

            // Fresh renderer for the next page (what BookProcessor does between pages).
            _browser.StartFreshBrowser();

            // The fresh browser is a clean renderer: the previous page's window state is gone.
            Assert.That(
                _browser.RunJavascript("window.__marker || ''"),
                Is.Empty,
                "a fresh browser should be a clean renderer with no residual window state"
            );

            // And it can still navigate and run script, proving the shared environment survives the switch.
            NavigateTo(MakePageDom("<div>page2</div><script>window.__marker = 'page2';</script>"));
            Assert.That(_browser.RunJavascript("window.__marker || ''"), Is.EqualTo("page2"));
        }

        [Test]
        public void WaitForJavascriptResult_ThrowsOnTimeout_WhenValueNeverSet()
        {
            NavigateTo(MakePageDom("<div>hello</div>"));

            // SANITY: confirm the variable really is absent, so a timeout is because it is never set,
            // not because we are looking at the wrong thing.
            Assert.That(_browser.RunJavascript("window.__never || ''"), Is.Empty);

            // Short timeout so the test doesn't wait the full production budget.
            var ex = Assert.Throws<ApplicationException>(() =>
                BookProcessor.WaitForJavascriptResult(
                    _browser,
                    "window.__never || ''",
                    "the value that never appears",
                    "testPage",
                    timeoutMs: 1000
                )
            );
            Assert.That(ex.Message, Does.Contain("the value that never appears"));
        }
    }
}
