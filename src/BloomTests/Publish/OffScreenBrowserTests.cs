using System.Threading;
using Bloom.Publish;
using NUnit.Framework;

namespace BloomTests.Publish
{
    /// <summary>
    /// Mechanics test for OffScreenBrowser: proves a real WebView2Browser initializes on its own dedicated
    /// thread and runs javascript when driven by blocking synchronous calls from THIS thread — the pattern
    /// that retires RunJavascriptWithStringResult_Sync_Dangerous in PublishHelper.
    ///
    /// Full navigation (which needs a running BloomServer to resolve CSS/fonts/relative paths) is covered by
    /// the existing epub/bloompub publish suites, which drive RemoveUnwantedContent against real books. That
    /// is also where the BL-15292 "real fonts, not empty/garbage" risk is validated.
    /// </summary>
    [TestFixture]
    [Category("SkipOnTeamCity")]
    public class OffScreenBrowserTests
    {
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
    }
}
