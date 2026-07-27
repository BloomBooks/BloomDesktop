using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Microsoft.Web.WebView2.Core;

namespace Bloom.Publish
{
    /// <summary>
    /// A WebView2 browser for off-screen work: navigate to a page and run javascript against the real,
    /// laid-out DOM (so CSS, fonts, and layout are actually resolved) without ever putting the browser on
    /// screen or letting the user interact with it. Callers drive it with blocking, synchronous calls —
    /// <see cref="Navigate"/> and <see cref="RunJavascript"/> — that return the result to the calling thread.
    ///
    /// The first use is PublishHelper's "page checks" (which elements are visible, what fonts are used), but
    /// nothing here is specific to that; it suits any task that needs to ask a real browser questions about a
    /// document off-screen.
    ///
    /// One instance can manage a SERIES of inner browsers over its lifetime, all sharing a single
    /// CoreWebView2Environment: call <see cref="StartFreshBrowser"/> to discard the current browser and
    /// continue with a clean one (a fresh renderer with no residual page state) while keeping that
    /// environment — so the browser process, user-data folder, and HTTP cache stay warm across the series.
    /// The environment is created lazily with the first browser and belongs to this instance alone (it is
    /// deliberately not static/shared between instances, so instances on different threads never contend).
    /// A caller that only needs one browser simply never calls StartFreshBrowser.
    ///
    /// How it stays safe: the browser is owned by a private, dedicated STA thread with its own Windows Forms
    /// message loop, and THAT thread — not the caller — services the browser's callbacks. So a caller can
    /// simply block for a result. It never has to pump the MAIN UI message loop the way
    /// Browser.RunJavascriptWithStringResult_Sync_Dangerous does (Application.DoEvents), which lets unrelated
    /// user commands run in the middle of the call stack — the reentrancy blamed for BL-12614 / BL-13120.
    /// Async continuations for the browser's operations run on the owning thread, so the WebView2 is only
    /// ever touched by that thread.
    ///
    /// Blocking here cannot deadlock on the message loop, since the thread that blocks is never the thread
    /// that must pump. But it is NOT immune to deadlock in general: navigation asks BloomServer for the page,
    /// so a caller that blocks while holding the last free BloomServer worker would be waiting for a page
    /// that nobody is left to serve. Two things guard against that (BL-16612): we register the block with
    /// BloomServer so it can add a worker (see RunAndBlock), and every blocking call is bounded by a backstop
    /// timeout and throws <see cref="OffScreenBrowserTimeoutException"/> rather than waiting forever.
    ///
    /// The browser is a real <see cref="WebView2Browser"/> (not a bare WebView2 control) so navigation goes
    /// through Bloom's BloomServer/in-memory-file plumbing and resolves CSS, fonts, and relative paths.
    /// </summary>
    public sealed class OffScreenBrowser : IDisposable
    {
        private readonly Thread _thread;
        private WebView2Browser _browser;

        // The one CoreWebView2Environment (browser process + user-data folder + HTTP cache) shared across
        // every inner browser this instance creates. Captured from the first browser and reused for each
        // fresh browser (see StartFreshBrowser), so we don't pay environment creation each time.
        private CoreWebView2Environment _environment;

        // The message loop we run on the dedicated thread; ExitThread() on it ends the loop at Dispose.
        private ApplicationContext _appContext;

        // The dedicated thread's Windows Forms synchronization context. Posting to it marshals work onto that
        // thread; awaits inside that work resume there too.
        private SynchronizationContext _ctx;

        // Signaled once the dedicated thread has finished (or failed) browser initialization.
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private Exception _startupError;

        private const int kInitTimeoutMs = 20000;

        // Longest we will block a caller waiting for work we posted to the dedicated thread. This is a
        // BACKSTOP, not a normal budget: the operations that can legitimately take a while carry their own
        // timeout (Navigate, and the browser creation inside StartFreshBrowser) and pass it plus a margin,
        // so the only way to hit this is a WebView2 — or the message loop that services it — that has
        // stopped making progress altogether. We used to block forever in that case, which wedged the
        // calling thread permanently; when the caller was a BloomServer worker that wedged publishing with
        // it, and the process never recovered (BL-16612).
        private const int kDefaultBlockTimeoutMs = 30000;

        // Added to an operation's own timeout to get the backstop for waiting on it, so a normal timeout
        // (which makes the operation return by itself) always wins over the backstop.
        private const int kBlockTimeoutMarginMs = 15000;

        /// <summary>
        /// Test hook: shortens the backstop of <see cref="kDefaultBlockTimeoutMs"/> so a test can prove it
        /// fires without having to wedge a real browser for 30 seconds. Never set in production.
        /// </summary>
        internal int? DefaultBlockTimeoutMsForTests;

        /// <summary>
        /// Starts the dedicated thread and blocks until its WebView2 is initialized and ready to navigate.
        /// </summary>
        public OffScreenBrowser()
        {
            _thread = new Thread(ThreadMain) { IsBackground = true, Name = "OffScreenBrowser" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            // Bounded for the same reason as RunAndBlock: InitializeAsync enforces kInitTimeoutMs itself,
            // but only once its message loop is actually pumping. If the thread never gets that far, _ready
            // is never set, and an unbounded wait here would hang whoever asked for the browser.
            if (!_ready.Wait(kInitTimeoutMs + kBlockTimeoutMarginMs))
            {
                Dispose();
                throw new OffScreenBrowserTimeoutException(
                    $"The off-screen browser's thread did not finish initializing within "
                        + $"{kInitTimeoutMs + kBlockTimeoutMarginMs}ms."
                );
            }
            if (_startupError != null)
            {
                // Initialization failed (e.g. the WebView2 readiness timeout). The dedicated thread may still be
                // pumping its message loop with a live WebView2 (and its CoreWebView2 process) attached, so tear
                // that down before we throw; otherwise the thread and the browser process leak.
                Dispose();
                throw new ApplicationException(
                    "OffScreenBrowser failed to initialize",
                    _startupError
                );
            }
        }

        /// <summary>
        /// The managed id of the thread that owns the browser. Used by tests to prove the browser runs on a
        /// different thread than the one that blocks for results.
        /// </summary>
        public int BrowserThreadId => _thread.ManagedThreadId;

        // Runs on the dedicated thread: establishes a message loop, creates the browser, then pumps messages
        // until Dispose() ends the loop via the ApplicationContext.
        private void ThreadMain()
        {
            try
            {
                // Give this thread a Windows Forms message loop + synchronization context, so the browser's
                // async continuations (and cross-thread Posts from callers) run here.
                var ctx = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(ctx);
                _ctx = ctx;

                // Create the browser and wait for it to become ready once the loop is pumping (its async
                // CoreWebView2 initialization completes via that loop), then signal the constructor.
                ctx.Post(_ => InitializeAsync(), null);

                _appContext = new ApplicationContext();
                Application.Run(_appContext); // pump until the context's loop is ended in Dispose
            }
            catch (Exception e)
            {
                _startupError = e;
                _ready.Set();
            }
        }

        private async void InitializeAsync()
        {
            try
            {
                await CreateInnerBrowserAndWaitReadyAsync();
                // Capture the environment the first browser created, so each later fresh browser can reuse it
                // (see StartFreshBrowser) instead of paying environment creation again.
                _environment = _browser.CoreEnvironment;
            }
            catch (Exception e)
            {
                _startupError = e;
            }
            finally
            {
                _ready.Set();
            }
        }

        // Creates the inner browser — reusing our shared environment once we have captured one — and waits
        // until it is ready to navigate. Runs on the dedicated thread.
        private async Task CreateInnerBrowserAndWaitReadyAsync()
        {
            _browser =
                _environment == null
                    ? new WebView2Browser()
                    : WebView2Browser.CreateWithInjectedEnvironment(_environment);
            // Realize the HWND now; CoreWebView2 initialization can only complete once the control has a
            // window handle, and nothing else (no parent Form) will create it for us. Same trick as
            // BookProcessor's off-screen browser.
            _browser.CreateControl();

            var timer = Stopwatch.StartNew();
            while (!_browser.IsReadyToNavigate)
            {
                if (timer.ElapsedMilliseconds > kInitTimeoutMs)
                    throw new ApplicationException(
                        "Timed out initializing the off-screen WebView2."
                    );
                await Task.Delay(20);
            }
        }

        /// <summary>
        /// Navigates the browser to the given DOM (served via BloomServer) and blocks the calling thread until
        /// navigation completes, times out, or is cancelled. Returns true on successful navigation, false on
        /// timeout or cancellation — matching NavigateAndWaitTillDone's contract for the caller. The source
        /// controls how BloomServer serves the page (e.g. JustCheckingPage swaps videos for placeholders,
        /// Frame serves the page as-is for the editing bundle).
        ///
        /// Throws <see cref="OffScreenBrowserTimeoutException"/> in the separate case where the browser stops
        /// responding altogether, so it cannot even tell us it timed out.
        /// </summary>
        public bool Navigate(
            HtmlDom htmlDom,
            int timeoutMs,
            Func<bool> cancelCheck,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.JustCheckingPage
        )
        {
            // NavigateAsync enforces timeoutMs and returns false by itself, so the backstop only has to be
            // comfortably larger than that.
            return RunAndBlock(
                () => NavigateAsync(htmlDom, timeoutMs, cancelCheck, source),
                timeoutMs + kBlockTimeoutMarginMs
            );
        }

        /// <summary>
        /// Starts navigating to the given DOM but does NOT wait for the navigation-completed ('load') event;
        /// blocks only until the navigation has been dispatched. Use this for full editing pages whose 'load'
        /// is unreliable off-screen (document.readyState can stick at "interactive"); the caller instead polls
        /// a window flag the page's own script sets when it is actually ready (see BookProcessor).
        /// </summary>
        public void NavigateWithoutWaitingForLoad(HtmlDom htmlDom, InMemoryHtmlFileSource source)
        {
            RunAndBlock(() =>
            {
                _browser.Navigate(htmlDom, source: source);
                return Task.FromResult(true);
            });
        }

        // Async navigation using only the public Browser API (DocumentCompleted is raised on the WebView2's
        // NavigationCompleted). No Application.DoEvents: we await the completion event, polling for timeout and
        // caller-requested cancellation. Runs on the dedicated thread.
        private async Task<bool> NavigateAsync(
            HtmlDom htmlDom,
            int timeoutMs,
            Func<bool> cancelCheck,
            InMemoryHtmlFileSource source
        )
        {
            var navigated = new TaskCompletionSource<bool>();
            void Handler(object sender, EventArgs e) => navigated.TrySetResult(true);
            _browser.DocumentCompleted += Handler;
            try
            {
                _browser.Navigate(htmlDom, source: source);
                var timer = Stopwatch.StartNew();
                while (!navigated.Task.IsCompleted)
                {
                    if (cancelCheck != null && cancelCheck())
                        return false;
                    if (timer.ElapsedMilliseconds > timeoutMs)
                        return false;
                    await Task.WhenAny(navigated.Task, Task.Delay(50));
                }
                return true;
            }
            finally
            {
                _browser.DocumentCompleted -= Handler;
            }
        }

        /// <summary>
        /// Runs the given script and blocks the calling thread until it returns the result (already JSON-decoded
        /// to a plain string by GetStringFromJavascriptAsync). This is the safe replacement for
        /// RunJavascriptWithStringResult_Sync_Dangerous: it blocks the caller instead of pumping the main loop.
        /// </summary>
        public string RunJavascript(string script)
        {
            return RunAndBlock(() => _browser.GetStringFromJavascriptAsync(script));
        }

        /// <summary>
        /// Runs the given script without waiting for it to finish (beyond its synchronous kickoff). Use this
        /// for scripts that start asynchronous work and stash their result on a window global that the caller
        /// then polls for (via <see cref="RunJavascript"/>). Blocks only until the script has been dispatched
        /// on the browser's thread.
        /// </summary>
        public void RunJavascriptFireAndForget(string script)
        {
            RunAndBlock(() =>
            {
                _browser.RunJavascriptFireAndForget(script);
                return Task.FromResult(true);
            });
        }

        /// <summary>
        /// Discards the current inner browser and continues with a fresh one — a clean renderer with no
        /// residual page state — reusing the same environment, then blocks until it is ready. Use this
        /// whenever you need a clean browser (for example, to isolate one navigation from the previous one's
        /// leftover state) without paying to recreate the environment: the browser process, user-data folder,
        /// and HTTP cache stay warm across the series.
        /// </summary>
        public void StartFreshBrowser()
        {
            RunAndBlock(
                async () =>
                {
                    _browser?.Dispose();
                    await CreateInnerBrowserAndWaitReadyAsync();
                    return true;
                },
                // CreateInnerBrowserAndWaitReadyAsync enforces kInitTimeoutMs itself.
                kInitTimeoutMs + kBlockTimeoutMarginMs
            );
        }

        // Marshals an async function onto the dedicated thread and BLOCKS the calling thread for its result.
        // Blocking is safe here precisely because the dedicated thread — not the caller — pumps the messages
        // that let the awaited WebView2 operations complete.
        //
        // timeoutMs is the backstop for the wait (see kDefaultBlockTimeoutMs): callers whose operation has
        // its own timeout pass that plus kBlockTimeoutMarginMs, so the operation's own timeout normally
        // wins and this only fires when the browser has stopped making progress entirely.
        private T RunAndBlock<T>(Func<Task<T>> asyncFunc, int? timeoutMs = null)
        {
            var effectiveTimeoutMs =
                timeoutMs ?? DefaultBlockTimeoutMsForTests ?? kDefaultBlockTimeoutMs;
            var tcs = new TaskCompletionSource<T>();
            _ctx.Post(
                async _ =>
                {
                    try
                    {
                        tcs.SetResult(await asyncFunc());
                    }
                    catch (Exception e)
                    {
                        tcs.SetException(e);
                    }
                },
                null
            );

            // Tell BloomServer that this thread is about to block, in case it is one of its workers.
            // BloomServer does NOT work that out for itself (see RegisterThreadBlocking): a worker that
            // blocks without registering is invisible to the logic in QueueRequest that adds a worker when
            // every existing one is blocked. That matters a great deal here, because what we are waiting
            // for is a browser navigating to a page that BloomServer itself has to serve. If every worker
            // is blocked and none is left to serve that page, the navigation can never complete and we
            // would be waiting for something that cannot happen (BL-16612). Registering lets BloomServer
            // spin up the worker that breaks the cycle.
            var server = BloomServer._theOneInstance;
            server?.RegisterThreadBlocking();
            try
            {
                bool completed;
                try
                {
                    completed = tcs.Task.Wait(effectiveTimeoutMs);
                }
                catch (AggregateException)
                {
                    // The work threw. Fall through to GetResult() below, which rethrows the original
                    // exception rather than the AggregateException that Wait wraps it in.
                    completed = true;
                }
                if (!completed)
                {
                    // The posted work is still queued or running on the dedicated thread and may yet touch
                    // the browser, so the browser is now in an unknown state: the caller must discard it
                    // (StartFreshBrowser) or stop using this instance.
                    throw new OffScreenBrowserTimeoutException(
                        $"The off-screen browser did not respond within {effectiveTimeoutMs}ms. "
                            + BloomServer.GetWorkerPoolDiagnostics()
                    );
                }
                return tcs.Task.GetAwaiter().GetResult();
            }
            finally
            {
                server?.RegisterThreadUnblocked();
            }
        }

        /// <summary>
        /// Disposes the WebView2 (on its owning thread) and shuts down the dedicated thread's message loop.
        /// </summary>
        public void Dispose()
        {
            var ctx = _ctx;
            if (ctx != null)
            {
                ctx.Post(
                    _ =>
                    {
                        // Always end the loop, even if disposing the browser throws — otherwise the loop keeps
                        // running and Join below only returns after its 5s timeout, with the thread still alive.
                        try
                        {
                            _browser?.Dispose();
                        }
                        finally
                        {
                            _appContext?.ExitThread();
                        }
                    },
                    null
                );
            }
            _thread.Join(5000);
        }
    }

    /// <summary>
    /// Thrown when the off-screen browser stops making progress: it did not finish initializing, or work we
    /// posted to its thread did not come back within the backstop timeout. Distinct from a plain
    /// ApplicationException so callers can tell "the browser is wedged" (retry on a fresh one, or fail the
    /// operation) from an error thrown by the script or navigation itself.
    /// </summary>
    public class OffScreenBrowserTimeoutException : ApplicationException
    {
        public OffScreenBrowserTimeoutException(string message)
            : base(message) { }
    }
}
