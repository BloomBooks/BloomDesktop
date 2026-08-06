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
    /// user commands run in the middle of the call stack — the reentrancy blamed for BL-12614 / BL-13120. And
    /// it never deadlocks, because the thread that blocks is never the thread that must pump. Async
    /// continuations for the browser's operations run on the owning thread, so the WebView2 is only ever
    /// touched by that thread.
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

        /// <summary>
        /// Starts the dedicated thread and blocks until its WebView2 is initialized and ready to navigate.
        /// </summary>
        public OffScreenBrowser()
        {
            _thread = new Thread(ThreadMain) { IsBackground = true, Name = "OffScreenBrowser" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
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
        /// </summary>
        public bool Navigate(
            HtmlDom htmlDom,
            int timeoutMs,
            Func<bool> cancelCheck,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.JustCheckingPage
        )
        {
            return RunAndBlock(() => NavigateAsync(htmlDom, timeoutMs, cancelCheck, source));
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
            RunAndBlock(async () =>
            {
                _browser?.Dispose();
                await CreateInnerBrowserAndWaitReadyAsync();
                return true;
            });
        }

        // Marshals an async function onto the dedicated thread and BLOCKS the calling thread for its result.
        // Blocking is safe here precisely because the dedicated thread — not the caller — pumps the messages
        // that let the awaited WebView2 operations complete.
        private T RunAndBlock<T>(Func<Task<T>> asyncFunc)
        {
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
            return tcs.Task.GetAwaiter().GetResult();
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
}
