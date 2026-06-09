using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Edit;
using SIL.Progress;

namespace Bloom.Book
{
    /// <summary>
    /// Runs, off-screen, the same per-page "fix-up" that a user gets by opening a book in the Edit
    /// tab and visiting every page, but without disturbing the live UI. It is driven by the
    /// external/process-book API and used by BloomBridge to finish a freshly-generated
    /// book whose raw HTML "isn't quite right" yet.
    ///
    /// Why a real browser is needed: when an editable page loads, the editing JavaScript
    /// (bloomEditing.ts bootstrap/SetupElements) measures and mutates the DOM — image sizing,
    /// canvas-element layout, font auto-fit, etc. We load each page into an off-screen WebView2, let
    /// that initialization run, pull the resulting DOM back out, and save it through the normal
    /// editing save path (Book.UpdateDomFromEditedPage), which strips editing markup and extracts
    /// metadata. This mirrors the publish tab's off-screen page-check browser
    /// (PublishHelper.BrowserForPageChecks).
    /// </summary>
    public static class BookProcessor
    {
        // Generous per-page limit; this is a background automation step, not interactive editing.
        private const int kReadyTimeoutMs = 30000;

        /// <summary>
        /// Bring the book structurally up to date (xmatter/layout migrations; this also ensures the
        /// needed CSS file links are present, and on save the actual CSS files), then run the
        /// per-page browser fix-up over every page and save the result to disk. Returns the number
        /// of pages processed.
        ///
        /// All-or-nothing: a failure on any page (capture error or timeout) throws, the save at the
        /// end is skipped, and nothing is persisted. The caller (external/process-book) surfaces this
        /// as an error so BloomBridge can re-run, rather than leaving a half-processed book on disk.
        ///
        /// Must be called on the UI thread: it creates and pumps a WebView2 browser.
        /// </summary>
        public static int ProcessBook(Book book)
        {
            Debug.Assert(
                Program.RunningOnUiThread || Program.RunningUnitTests,
                "BookProcessor.ProcessBook must run on the UI thread (it drives a WebView2)."
            );

            // 1. Structural "make it right" pass. Besides migrations, this ensures stylesheet links
            //    (and, when we Save below, the actual CSS files) that BloomBridge's raw HTML may
            //    be missing. See BookStorage.EnsureHasLinksToStylesheets.
            // Log the book's identity so a developer can replay this exact run without BloomBridge,
            // e.g.  POST http://localhost:<port>/bloom/api/external/process-book  {"id":"<id>"}
            // (Bloom must be on the Collection tab.) The id is the book's bookInstanceId.
            Log(
                $"book id={book.ID} folder=\"{book.FolderPath}\" title=\"{book.NameBestForUserDisplay}\""
            );

            var totalTimer = Stopwatch.StartNew();
            var upToDateTimer = Stopwatch.StartNew();
            book.BringBookUpToDate(new NullProgress());
            upToDateTimer.Stop();
            Log($"BringBookUpToDate: {upToDateTimer.ElapsedMilliseconds} ms");

            // 2. Per-page browser fix-up.
            var pages = book.GetPages().Where(p => p != null).ToList();
            Log($"starting per-page fix-up of {pages.Count} pages (ckeditor stripped off-screen)");

            // Per-phase timing accumulators, summed for the DONE line at the end (see Log calls below).
            long totalBrowserMs = 0,
                totalInitMs = 0,
                totalNavReadyMs = 0,
                totalCaptureMs = 0,
                totalUpdateMs = 0;
            var pageIndex = 0;

            // Create a FRESH WebView2 control per page, but share ONE CoreWebView2Environment across them.
            //
            // Fresh control per page: we must NOT reuse a single control. Each editing page is a full
            // live-edit page (it opens the edit WebSocket channel and fires editView/* API calls on load);
            // that residual state wedges the next top-level navigation in the same control, which hangs or
            // crashes Bloom on the 2nd page. A fresh control gives each page a clean renderer, torn down on
            // dispose, so every page behaves like the always-working first one.
            //
            // Shared environment: left to itself, each control would also create its OWN environment — a new
            // browser process and a cold HTTP cache — costing ~300ms+ per page. Sharing one environment
            // (one process, one user-data folder, one cache) across the batch removes that per-page cost and
            // warms the cache so later pages navigate faster. Measured ~31s -> ~18s for a 22-page book
            // (per-page init ~315ms -> ~100ms, nav ~940ms -> ~550ms after page 1).
            WebView2Browser.BeginSharedEnvironmentBatch();
            try
            {
                foreach (var page in pages)
                {
                    pageIndex++;
                    Browser browser = null;
                    try
                    {
                        var browserTimer = Stopwatch.StartNew();
                        browser = BrowserMaker.MakeBrowser();
                        // Force the control's window handle into existence. The WebView2's CoreWebView2
                        // initialization (kicked off unawaited in the browser constructor) can only complete
                        // once the control has a realized HWND; until it does, _readyToNavigate never becomes
                        // true and navigation just times out. HtmlThumbNailer does the same thing for its
                        // off-screen browser. (We never add this browser to a Form, so nothing else would
                        // create the handle for us.)
                        browser.CreateControl();
                        browserTimer.Stop();

                        var timings = ProcessOnePage(book, browser, page);

                        totalBrowserMs += browserTimer.ElapsedMilliseconds;
                        totalInitMs += timings.InitMs;
                        totalNavReadyMs += timings.NavReadyMs;
                        totalCaptureMs += timings.CaptureMs;
                        totalUpdateMs += timings.UpdateMs;

                        var pageTotal =
                            browserTimer.ElapsedMilliseconds
                            + timings.InitMs
                            + timings.NavReadyMs
                            + timings.CaptureMs
                            + timings.UpdateMs;
                        Log(
                            $"page {pageIndex}/{pages.Count} [{page.Id}]: "
                                + $"browser={browserTimer.ElapsedMilliseconds}ms "
                                + $"init={timings.InitMs}ms "
                                + $"nav={timings.NavReadyMs}ms "
                                + $"capture={timings.CaptureMs}ms "
                                + $"update={timings.UpdateMs}ms "
                                + $"total={pageTotal}ms"
                        );
                    }
                    finally
                    {
                        browser?.Dispose();
                    }
                }
            }
            finally
            {
                WebView2Browser.EndSharedEnvironmentBatch();
            }

            // 3. One full save now that every page's in-memory DOM has been updated.
            var saveTimer = Stopwatch.StartNew();
            book.Save();
            saveTimer.Stop();

            totalTimer.Stop();
            var avgPerPage = pages.Count > 0 ? totalTimer.ElapsedMilliseconds / pages.Count : 0;
            Log(
                $"DONE: {pages.Count} pages in {totalTimer.ElapsedMilliseconds} ms ({avgPerPage} ms/page avg). "
                    + $"Breakdown: upToDate={upToDateTimer.ElapsedMilliseconds}ms "
                    + $"browserCreate={totalBrowserMs}ms init={totalInitMs}ms nav={totalNavReadyMs}ms "
                    + $"capture={totalCaptureMs}ms update={totalUpdateMs}ms save={saveTimer.ElapsedMilliseconds}ms"
            );
            return pages.Count;
        }

        /// <summary>Per-page phase timings (milliseconds), for the diagnostic logging in ProcessBook.</summary>
        private struct PageTimings
        {
            public long InitMs;
            public long NavReadyMs;
            public long CaptureMs;
            public long UpdateMs;
        }

        // Write a process-book diagnostic line to BOTH the terminal (Bloom's stdout is piped to the
        // `go` dev terminal) and the Bloom log file, so timing is visible live and after the fact.
        private static void Log(string message)
        {
            var line = "[process-book] " + message;
            Console.WriteLine(line);
            SIL.Reporting.Logger.WriteEvent(line);
        }

        private static PageTimings ProcessOnePage(Book book, Browser browser, IPage page)
        {
            var dom = book.GetEditableHtmlDomForPage(page);
            // So the page's relative links (images, css) resolve against the book folder.
            dom.BaseForRelativePaths = book.FolderPath;

            // Off-screen processing never types into the page, so CKEditor (the rich-text editor
            // that AddJavaScriptForEditing injects for the live editor) is pure dead weight here:
            // ~346KB of script to download/parse into each fresh renderer, plus an editor instance
            // attached to every editable field during bootstrap(). None of it affects the load-time
            // DOM fix-ups we capture. Strip the ckeditor <script> so it never loads; bootstrap()'s
            // existing `typeof CKEDITOR === "undefined"` guard then skips all the attachment work.
            var ckeditorScripts = dom.SafeSelectNodes("//script[contains(@src,'ckeditor')]");
            foreach (var script in ckeditorScripts)
                script.ParentNode?.RemoveChild(script);

            // Frame == "Editing View is updating single displayed page": serves the page as-is.
            // (Unlike JustCheckingPage, it does not swap videos for placeholder images.)
            //
            // We deliberately do NOT use NavigateAndWaitTillDone here. That waits for the WebView2
            // NavigationCompleted event (the window 'load' event), which is unreliable for a full
            // editing page loaded off-screen: the editing bundle opens the edit WebSocket channel and
            // fires editView/* API calls on load, and we have observed document.readyState getting
            // stuck at "interactive" (load never firing) even though bootstrap()/SetupElements() has
            // fully run and there are no pending sub-resources. Waiting for 'load' just burns the whole
            // timeout. Instead we fire the navigation and then poll for __bloomEditablePageReady, which
            // is the signal we actually care about (the load-time DOM fix-ups are in place).
            //
            // Navigate() blocks internally until the control is ready to navigate, so we pre-wait here
            // only to attribute that readiness cost to its own timer (init) instead of folding it into
            // nav time. With the shared environment the heavy browser-process/cache warm-up is paid once
            // for the batch, so after the first page this mostly measures the control's handle/CoreWebView2
            // initialization.
            var initTimer = Stopwatch.StartNew();
            while (!browser.IsReadyToNavigate)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            initTimer.Stop();

            var navReadyTimer = Stopwatch.StartNew();
            browser.Navigate(dom, source: InMemoryHtmlFileSource.Frame);

            // Wait until bootstrap()/SetupElements() has actually run (signaled by
            // __bloomEditablePageReady), not merely until the bundle's exports exist, so the load-time
            // DOM fix-ups are in place before we capture the page.
            WaitForJavascriptResult(
                browser,
                "(window.__bloomEditablePageReady && window.editablePageBundle) ? 'ready' : ''",
                "the editing bundle to initialize",
                page
            );
            navReadyTimer.Stop();

            // Ask the bundle to gather the (now browser-processed) page content. It stashes the
            // result on window for us to poll, rather than posting to the live editView API (which
            // would feed the live EditingModel and corrupt the live editor's state).
            var captureTimer = Stopwatch.StartNew();
            browser.RunJavascriptWithStringResult_Sync_Dangerous(
                "window.editablePageBundle.captureContentForExternalProcessing(); ''"
            );

            var pageContent = WaitForJavascriptResult(
                browser,
                "window.__bloomExternalPageContent || ''",
                "the page content to be captured",
                page
            );
            captureTimer.Stop();

            if (pageContent.StartsWith("ERROR:", StringComparison.Ordinal))
            {
                throw new ApplicationException(
                    $"process-book: failed to capture page {page.Id}: {pageContent}"
                );
            }

            var updateTimer = Stopwatch.StartNew();
            var editedDom = EditingModel.GetEditedPageDomFromBrowserContent(pageContent);
            // Force a full update so shared/derived data (titles, metadata) is sucked in, matching
            // what the live editor does when leaving a page that changed such data. We delay the
            // actual write to disk until a single Book.Save() after all pages are processed.
            book.UpdateDomFromEditedPage(editedDom, out _, needToDoFullSave: true);
            updateTimer.Stop();

            return new PageTimings
            {
                InitMs = initTimer.ElapsedMilliseconds,
                NavReadyMs = navReadyTimer.ElapsedMilliseconds,
                CaptureMs = captureTimer.ElapsedMilliseconds,
                UpdateMs = updateTimer.ElapsedMilliseconds,
            };
        }

        /// <summary>
        /// Polls a javascript expression (which evaluates to a non-empty string when "ready") until
        /// it is non-empty or we time out, returning the result. RunJavascriptWithStringResult_Sync_Dangerous
        /// already pumps the message loop while it waits for each script to return.
        /// </summary>
        private static string WaitForJavascriptResult(
            Browser browser,
            string script,
            string whatWeAreWaitingFor,
            IPage page
        )
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < kReadyTimeoutMs)
            {
                var result = browser.RunJavascriptWithStringResult_Sync_Dangerous(script);
                if (!string.IsNullOrEmpty(result))
                    return result;
                Application.DoEvents();
                Thread.Sleep(20);
            }
            throw new ApplicationException(
                $"process-book: timed out waiting for {whatWeAreWaitingFor} on page {page.Id}."
            );
        }
    }
}
