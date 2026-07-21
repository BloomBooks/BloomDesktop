using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish;
using NUnit.Framework;

namespace BloomTests.Publish
{
    /// <summary>
    /// Mechanics test for OffScreenBrowser: proves a real WebView2Browser initializes on its own dedicated
    /// thread and runs javascript when driven by blocking synchronous calls from THIS thread — the pattern
    /// that retires RunJavascriptWithStringResult_Sync_Dangerous in PublishHelper.
    ///
    /// Full navigation against real books (which needs a running BloomServer to resolve CSS/fonts/relative
    /// paths) is covered by the existing epub/bloompub publish suites, which drive RemoveUnwantedContent
    /// against real books. That is also where the BL-15292 "real fonts, not empty/garbage" risk is
    /// validated. This file uses a minimal BloomServer (no book folder needed) just to prove several
    /// OffScreenBrowser instances can navigate concurrently without interfering with each other.
    /// </summary>
    [TestFixture]
    public class OffScreenBrowserTests
    {
        private static BloomServer s_bloomServer;

        /// <summary>
        /// A running BloomServer is required for Navigate() (it serves the in-memory HTML). No book/file
        /// locator setup is needed here since the test pages don't reference any files.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            s_bloomServer = new BloomServer(new BookSelection());
            s_bloomServer.EnsureListening();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            s_bloomServer.Dispose();
        }

        [Test]
        public void CanRunJavascriptOnDedicatedThread_WithoutPumpingCallingThread()
        {
            var callingThreadId = Thread.CurrentThread.ManagedThreadId;

            using (var host = new OffScreenBrowser())
            {
                // SANITY: the browser must be on a DIFFERENT thread than the one we block on, otherwise the
                // whole premise (block safely because someone else pumps) is void.
                Assert.That(
                    host.BrowserThreadId,
                    Is.Not.EqualTo(callingThreadId),
                    "browser should run on its own thread, not the calling thread"
                );

                // This blocking call is the crux: the calling thread does NOT pump a message loop, yet the
                // script completes because the dedicated thread services the WebView2 callbacks. If the
                // premise were wrong this would deadlock (and the test would time out) rather than fail.
                var result = host.RunJavascript("(1 + 2).toString()");

                Assert.That(
                    result,
                    Is.EqualTo("3"),
                    "javascript executed on the dedicated-thread browser should return its result"
                );
            }
        }

        [Test]
        public void MultipleInstances_CanNavigateConcurrently_WithoutCrossContamination()
        {
            const int kBrowserCount = 3;
            var browsers = Enumerable
                .Range(0, kBrowserCount)
                .Select(_ => new OffScreenBrowser())
                .ToArray();
            try
            {
                var results = new string[kBrowserCount];

                // Drive each browser's Navigate+RunJavascript round-trip from its own task, all started
                // together, so the dedicated threads/WebView2s are genuinely active at the same time rather
                // than one finishing before the next starts.
                var tasks = Enumerable
                    .Range(0, kBrowserCount)
                    .Select(i =>
                        Task.Run(() =>
                        {
                            var dom = new HtmlDom(
                                $"<html><head></head><body><div id='marker'>browser-{i}</div></body></html>"
                            );
                            var navigated = browsers[i].Navigate(dom, 10000, null);
                            Assert.That(
                                navigated,
                                Is.True,
                                $"browser {i} should have navigated successfully"
                            );
                            results[i] = browsers[i]
                                .RunJavascript("document.getElementById('marker').textContent");
                        })
                    )
                    .ToArray();

                Assert.That(
                    Task.WaitAll(tasks, 20000),
                    Is.True,
                    "all browsers should finish navigating within the timeout"
                );

                for (var i = 0; i < kBrowserCount; i++)
                {
                    Assert.That(
                        results[i],
                        Is.EqualTo($"browser-{i}"),
                        $"browser {i} should see only the content it was navigated to, not another browser's"
                    );
                }
            }
            finally
            {
                foreach (var browser in browsers)
                    browser.Dispose();
            }
        }
    }
}
