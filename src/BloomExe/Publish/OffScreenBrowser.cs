using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;

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
                throw new ApplicationException(
                    "OffScreenBrowser failed to initialize",
                    _startupError
                );
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

                // The constructor kicks off CoreWebView2 initialization unawaited; it completes via the message
                // loop we start below.
                _browser = new WebView2Browser();
                // Realize the HWND now; CoreWebView2 initialization can only complete once the control has a
                // window handle, and nothing else (no parent Form) will create it for us. Same trick as
                // BookProcessor's off-screen browser.
                _browser.CreateControl();

                // Poll for readiness once the loop is pumping, then signal the constructor.
                ctx.Post(_ => WaitUntilReadyThenSignal(), null);

                _appContext = new ApplicationContext();
                Application.Run(_appContext); // pump until the context's loop is ended in Dispose
            }
            catch (Exception e)
            {
                _startupError = e;
                _ready.Set();
            }
        }

        private async void WaitUntilReadyThenSignal()
        {
            try
            {
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
            catch (Exception e)
            {
                _startupError = e;
            }
            finally
            {
                _ready.Set();
            }
        }

        /// <summary>
        /// Navigates the browser to the given DOM (served via BloomServer) and blocks the calling thread until
        /// navigation completes, times out, or is cancelled. Returns true on successful navigation, false on
        /// timeout or cancellation — matching NavigateAndWaitTillDone's contract for the caller.
        /// </summary>
        public bool Navigate(HtmlDom htmlDom, int timeoutMs, Func<bool> cancelCheck)
        {
            return RunAndBlock(() => NavigateAsync(htmlDom, timeoutMs, cancelCheck));
        }

        // Async navigation using only the public Browser API (DocumentCompleted is raised on the WebView2's
        // NavigationCompleted). No Application.DoEvents: we await the completion event, polling for timeout and
        // caller-requested cancellation. Runs on the dedicated thread.
        private async Task<bool> NavigateAsync(
            HtmlDom htmlDom,
            int timeoutMs,
            Func<bool> cancelCheck
        )
        {
            var navigated = new TaskCompletionSource<bool>();
            void Handler(object sender, EventArgs e) => navigated.TrySetResult(true);
            _browser.DocumentCompleted += Handler;
            try
            {
                _browser.Navigate(htmlDom, source: InMemoryHtmlFileSource.JustCheckingPage);
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
                        _browser?.Dispose();
                        _appContext?.ExitThread();
                    },
                    null
                );
            }
            _thread.Join(5000);
        }
    }
}
