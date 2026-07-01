using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Edit;
using Bloom.ToPalaso;
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
        ///
        /// When <paramref name="fitImageTextSplits"/> is true, simple two-pane origami pages with a
        /// single illustration in the first pane and a single text block in the second pane have their
        /// split auto-fit. This currently covers both image-above-text and image-left-of-text layouts.
        /// The image pane is grown (and the text pane shrunk) as far as it can without making the text
        /// overflow, but no further than the image filling the relevant page dimension. This uses the
        /// real off-screen browser layout (no font/text estimation); see fitImageOverTextSplits() in
        /// bloomEditing.ts.
        /// </summary>
        public static int ProcessBook(Book book, bool fitImageTextSplits = false)
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

            // Creating the off-screen WebView2 controls below can yank the OS foreground onto Bloom,
            // popping it in front of whatever the user (or the external tool that invoked
            // process-book, e.g. BloomBridge) was looking at. This is a background processing step, so
            // it has no business stealing focus. Remember who held the foreground up front and hand it
            // back whenever a browser steals it (see RestoreForeground in the per-page loop and below).
            // We capture this ONCE: every RestoreForeground call aims at this same original window, so
            // if Bloom grabs the foreground again on a later page we still hand it back to where it
            // started. (Re-capturing mid-batch would be wrong: by then Bloom itself often holds the
            // foreground, so we'd "restore" to Bloom's own window.)
            var priorForeground = ProcessExtra.GetForegroundWindow();

            book.BringBookUpToDate(new NullProgress());

            // 2. Per-page browser fix-up.
            var pages = book.GetPages().Where(p => p != null).ToList();
            Log($"starting per-page fix-up of {pages.Count} pages (ckeditor stripped off-screen)");

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
                        browser = BrowserMaker.MakeBrowser();
                        // Force the control's window handle into existence. The WebView2's CoreWebView2
                        // initialization (kicked off unawaited in the browser constructor) can only complete
                        // once the control has a realized HWND; until it does, _readyToNavigate never becomes
                        // true and navigation just times out. HtmlThumbNailer does the same thing for its
                        // off-screen browser. (We never add this browser to a Form, so nothing else would
                        // create the handle for us.)
                        browser.CreateControl();

                        // If realizing the off-screen browser pulled Bloom to the front, put the
                        // previous window back so we keep processing quietly in the background.
                        RestoreForeground(priorForeground);

                        ProcessOnePage(book, browser, page, fitImageTextSplits);

                        Log($"page {pageIndex}/{pages.Count} [{page.Id}] done");
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
                // Final safety net: make sure we leave the foreground where we found it, even if a
                // page threw partway through the batch.
                RestoreForeground(priorForeground);
            }

            // 3. One full save now that every page's in-memory DOM has been updated.
            book.Save();

            Log($"DONE: {pages.Count} pages");
            return pages.Count;
        }

        /// <summary>
        /// Hand the OS foreground back to <paramref name="priorForeground"/> if the off-screen browser
        /// stole it for Bloom. No-op if we never knew the prior window or it still holds the foreground,
        /// so we don't gratuitously flip windows around. Critically, we only restore when Bloom itself
        /// currently holds the foreground: if some other application now holds it, the user has switched
        /// away on purpose and we must not yank focus back out from under them.
        /// </summary>
        private static void RestoreForeground(IntPtr priorForeground)
        {
            if (priorForeground == IntPtr.Zero)
                return;
            var currentForeground = ProcessExtra.GetForegroundWindow();
            if (currentForeground == priorForeground)
                return; // already where we want it
            if (!ProcessExtra.IsWindowInCurrentProcess(currentForeground))
                return; // user has moved on to another app; leave their choice alone
            ProcessExtra.SetForegroundWindow(priorForeground);
        }

        // Write a process-book diagnostic line to BOTH the terminal (Bloom's stdout is piped to the
        // `go` dev terminal) and the Bloom log file, so progress is visible live and after the fact.
        private static void Log(string message)
        {
            var line = "[process-book] " + message;
            Console.WriteLine(line);
            SIL.Reporting.Logger.WriteEvent(line);
        }

        private static void ProcessOnePage(
            Book book,
            Browser browser,
            IPage page,
            bool fitImageTextSplits
        )
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
            // Navigate() blocks internally until the control is ready to navigate, so we pre-wait here.
            // With the shared environment the heavy browser-process/cache warm-up is paid once for the
            // batch, so after the first page this mostly waits on the control's handle/CoreWebView2
            // initialization.
            var readyTimer = Stopwatch.StartNew();
            while (!browser.IsReadyToNavigate)
            {
                if (readyTimer.ElapsedMilliseconds >= kReadyTimeoutMs)
                    throw new ApplicationException(
                        $"process-book: timed out waiting for the browser to become ready to navigate on page {page.Id}."
                    );
                Application.DoEvents();
                Thread.Sleep(5);
            }

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

            // Ask the bundle to gather the (now browser-processed) page content. It stashes the
            // result on window for us to poll, rather than posting to the live editView API (which
            // would feed the live EditingModel and corrupt the live editor's state).
            // The boolean tells the bundle whether to auto-fit simple image/text origami splits before
            // capturing (see fitImageOverTextSplits in bloomEditing.ts).
            // Fire-and-forget: capture is asynchronous (it stashes onto window.__bloomExternalPageContent
            // when it finishes) and we poll for that below, so there is no result to wait for here. The
            // sync RunJavascriptWithStringResult_Sync_Dangerous would only block until the script's
            // synchronous kickoff returned, which buys us nothing.
            browser.RunJavascriptFireAndForget(
                $"window.editablePageBundle.captureContentForExternalProcessing({(fitImageTextSplits ? "true" : "false")})"
            );

            var pageContent = WaitForJavascriptResult(
                browser,
                "window.__bloomExternalPageContent || ''",
                "the page content to be captured",
                page
            );

            if (pageContent.StartsWith("ERROR:", StringComparison.Ordinal))
            {
                throw new ApplicationException(
                    $"process-book: failed to capture page {page.Id}: {pageContent}"
                );
            }

            var editedDom = EditingModel.GetEditedPageDomFromBrowserContent(pageContent);
            // Force a full update so shared/derived data (titles, metadata) is sucked in, matching
            // what the live editor does when leaving a page that changed such data. We delay the
            // actual write to disk until a single Book.Save() after all pages are processed.
            book.UpdateDomFromEditedPage(editedDom, out _, needToDoFullSave: true);
        }

        /// <summary>
        /// Polls a javascript expression (which evaluates to a non-empty string when "ready") until
        /// it is non-empty or we time out, returning the result.
        ///
        /// We deliberately keep this "poll a window global" approach for the off-screen processor
        /// rather than the async editView/pageContent API callback the live editor uses: that callback
        /// pattern needs the live EditingModel and edit WebSocket channel, which a throwaway off-screen
        /// browser doesn't have, and the per-page loop here wants a deterministic in-line result. We are
        /// aware RunJavascriptWithStringResult_Sync_Dangerous is the same family of call implicated in
        /// past page-content deadlocks (BL-13120 etc.); moving this to an async/callback design is
        /// deferred. That method already pumps the message loop while it waits for each script to
        /// return, so we do NOT pump again between polls (just sleep briefly).
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
                Thread.Sleep(20);
            }
            throw new ApplicationException(
                $"process-book: timed out waiting for {whatWeAreWaitingFor} on page {page.Id}."
            );
        }
    }
}
