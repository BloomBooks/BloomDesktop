using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// A WebView2 can only initialize on an STA thread with a message loop. This fixture covers what
    /// happens when it is created somewhere it cannot possibly initialize — the situation bulk upload
    /// drifted into in BL-16767, where the code had migrated onto an MTA thread-pool thread.
    ///
    /// The point is not that creating a browser there should work; it can't. The point is that the
    /// failure must be visible and immediate. Before the fix, the constructor kicked initialization
    /// off as "_ = InitWebView()", so the exception was discarded without a trace and the browser was
    /// simply left forever not-ready: callers spun out their whole timeout and then failed with the
    /// baffling "The instance of CoreWebView2 is uninitialized", far from the real cause.
    /// </summary>
    [TestFixture]
    public class WebView2BrowserInitFailureTests
    {
        /// <summary>
        /// Runs the given action on a thread-pool thread (which is always MTA) and blocks for it.
        /// </summary>
        private static void RunOnThreadPoolThread(Action action)
        {
            Task.Run(action).GetAwaiter().GetResult();
        }

        [Test]
        public void BrowserCreatedWhereItCannotInitialize_ReportsTheFailure()
        {
            NonFatalProblem.LastNonFatalProblemReported = null;
            RunOnThreadPoolThread(() =>
            {
                Assert.That(
                    Thread.CurrentThread.GetApartmentState(),
                    Is.EqualTo(ApartmentState.MTA),
                    "Sanity check: a thread-pool thread should be MTA, which is what WebView2 cannot initialize on."
                );
                var browser = new WebView2Browser();
                try
                {
                    // Initialization is asynchronous, so give it a moment to fail.
                    var timer = Stopwatch.StartNew();
                    while (
                        NonFatalProblem.LastNonFatalProblemReported == null
                        && timer.ElapsedMilliseconds < 20000
                    )
                        Thread.Sleep(20);

                    Assert.That(
                        NonFatalProblem.LastNonFatalProblemReported,
                        Is.Not.Null,
                        "Initialization failed but nothing was reported, so the failure is invisible again."
                    );
                    Assert.That(
                        NonFatalProblem.LastNonFatalProblemReported,
                        Does.Contain("WebView2"),
                        "The report should say what failed."
                    );
                    Assert.That(
                        browser.IsReadyToNavigate,
                        Is.False,
                        "Sanity check: the browser cannot have become ready to navigate."
                    );
                    // Callers that run their own readiness wait (OffScreenBrowser does) can only give up
                    // early if the failure is visible to them, so it has to be exposed, not just logged.
                    Assert.That(
                        browser.InitializationError,
                        Is.Not.Null,
                        "The failure must be readable from outside, so a caller spinning on "
                            + "IsReadyToNavigate can stop instead of waiting out its own timeout."
                    );
                }
                finally
                {
                    browser.Dispose();
                }
            });
        }

        [Test]
        public void NavigateAndWaitTillDone_AfterInitFailure_ThrowsAtOnceWithTheRealCause()
        {
            RunOnThreadPoolThread(() =>
            {
                var browser = new WebView2Browser();
                try
                {
                    const int timeLimit = 30000;
                    var dom = new HtmlDom(
                        "<html><body><div class='bloom-page'>hello</div></body></html>"
                    );
                    var timer = Stopwatch.StartNew();
                    var exception = Assert.Throws<ApplicationException>(
                        () =>
                            browser.NavigateAndWaitTillDone(
                                dom,
                                timeLimit,
                                InMemoryHtmlFileSource.JustCheckingPage,
                                () => false,
                                throwOnTimeout: true
                            ),
                        "A browser that failed to initialize should refuse to navigate rather than time out."
                    );
                    // The old code waited out the whole limit twice over (once for readiness, once for
                    // the navigation that was never started). It must now give up as soon as it knows.
                    Assert.That(
                        timer.ElapsedMilliseconds,
                        Is.LessThan(timeLimit),
                        "It should fail as soon as initialization fails, not spin out the timeout."
                    );
                    Assert.That(
                        exception.InnerException,
                        Is.Not.Null,
                        "The real cause of the initialization failure should be passed on, so that whoever "
                            + "reads the log can see why the browser never came up."
                    );
                }
                finally
                {
                    browser.Dispose();
                }
            });
        }
    }
}
