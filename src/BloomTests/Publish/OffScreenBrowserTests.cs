using System.Diagnostics;
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

        /// <summary>
        /// The point of the backstop timeout (BL-16612): a browser that stops making progress must fail the
        /// caller rather than block it forever. Blocking forever used to wedge the calling thread, and when
        /// that thread was a BloomServer worker it took publishing down with it for the rest of the session.
        /// </summary>
        [Test]
        public void RunJavascript_WhenTheBrowserDoesNotComeBackInTime_ThrowsRatherThanBlockingForever()
        {
            using (var host = new OffScreenBrowser())
            {
                // SANITY: a normal script works on this browser, so the failure below is really about the
                // timeout and not about a browser that never worked in the first place.
                Assert.That(
                    host.RunJavascript("(1 + 2).toString()"),
                    Is.EqualTo("3"),
                    "setup: the browser should run a trivial script before we test the timeout"
                );

                const int kBackstopMs = 500;
                const int kScriptMs = 5000;
                host.DefaultBlockTimeoutMsForTests = kBackstopMs;

                // A script that keeps the renderer busy for far longer than the backstop. It does finish on
                // its own, so we leave no spinning renderer behind for the rest of the suite.
                var timer = Stopwatch.StartNew();
                Assert.That(
                    () =>
                        host.RunJavascript(
                            $"const end = Date.now() + {kScriptMs}; while (Date.now() < end) {{}} 'done'"
                        ),
                    Throws.TypeOf<OffScreenBrowserTimeoutException>(),
                    "a browser that does not come back must fail the caller, not block it forever"
                );
                Assert.That(
                    timer.ElapsedMilliseconds,
                    Is.LessThan(kScriptMs),
                    "should have given up at the backstop rather than waiting out the whole script"
                );
            }
        }

        /// <summary>
        /// The diagnostics we log when something is stuck waiting on the server have to work at exactly the
        /// moment things are going wrong, so at least prove they report real counts rather than throwing.
        /// </summary>
        [Test]
        public void GetWorkerPoolDiagnostics_WithAServerRunning_ReportsWorkerCounts()
        {
            var diagnostics = BloomServer.GetWorkerPoolDiagnostics();

            Assert.That(
                diagnostics,
                Does.Contain("workers="),
                "diagnostics should report the worker count"
            );
            Assert.That(
                diagnostics,
                Does.Contain("blocked="),
                "diagnostics should report how many workers are blocked, the key number for a starvation hang"
            );
            Assert.That(
                diagnostics,
                Does.Not.Contain("none running"),
                "the fixture's BloomServer should have been found"
            );
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
