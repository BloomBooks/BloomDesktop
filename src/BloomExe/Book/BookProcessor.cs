using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Bloom.Api;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.MiscUI;
using Bloom.Publish;
using Bloom.ToPalaso;
using Bloom.web;
using L10NSharp;
using SIL.Progress;

namespace Bloom.Book
{
    /// <summary>
    /// Runs, off-screen, the same per-page "fix-up" that a user gets by opening a book in the Edit
    /// tab and visiting every page, but without disturbing the live UI. It is driven by the
    /// external/process-book API and used by BloomBridge to finish a freshly-generated
    /// book whose raw HTML "isn't quite right" yet. Before the per-page work it also shrinks any
    /// oversized images BloomBridge wrote straight to the book folder, since those never passed
    /// through Bloom's normal image import.
    ///
    /// Why a real browser is needed: when an editable page loads, the editing JavaScript
    /// (bloomEditing.ts bootstrap/SetupElements) measures and mutates the DOM — image sizing,
    /// canvas-element layout, font auto-fit, etc. We load each page into an off-screen WebView2, let
    /// that initialization run, pull the resulting DOM back out, and save it through the normal
    /// editing save path (Book.UpdateDomFromEditedPage), which strips editing markup and extracts
    /// metadata. This mirrors the publish tab's off-screen page-check browser
    /// (PublishHelper.GetOrCreatePageChecksBrowser).
    /// </summary>
    /// <remarks>
    /// Besides BloomBridge (via the external/process-book API), this is what the Collection tab's
    /// "Update Book" command runs (CollectionModel.BringBookUpToDate): the whole-book migrations
    /// alone leave undone everything the editing JavaScript does to a page, so users used to be told
    /// to "go to the Edit tab and click on each page" (BL-16595). Doing that here, off-screen, is
    /// also safer than driving the live editor through the pages: the capture below waits for the
    /// page's asynchronous fix-ups to finish and captures on a later timer tick, so it can never save
    /// a page mid-fix-up. The live save path, triggered the instant a page loaded, could (BL-16870).
    /// </remarks>
    public static class BookProcessor
    {
        // Generous per-page limit; this is a background automation step, not interactive editing.
        // Must stay comfortably above kExternalCaptureMaxWaitMs in pageContentCapturePolicy.ts (the
        // browser's own cap on waiting for a page's async fix-ups before it captures or gives up),
        // or we would time out on a slow page just before the browser reported it.
        private const int kReadyTimeoutMs = 60000;

        // A <meta> in the book's HTML recording the Bloom version (major.minor.build, e.g. "6.5.0")
        // whose per-page browser fix-up was last applied to this book, and one recording the page
        // size/orientation (e.g. "A5Portrait") it was applied at. Together they let us tell whether
        // the fix-up still needs (re-)running for editing or publishing: see NeedsPerPageFixup.
        internal const string kPerPageFixupVersionMeta = "perPageFixupBloomVersion";
        internal const string kPerPageFixupLayoutMeta = "perPageFixupLayout";

        // Books for which the automatic per-page fix-up (EnsurePerPageFixupIfNeeded) was tried this
        // session and threw. Since a failed run stamps nothing, NeedsPerPageFixup would keep saying
        // "yes" and we would re-prompt on every tab switch; remembering the failure lets us stop
        // pestering until Bloom is restarted (by when the cause may be gone). Keyed by book id.
        private static readonly HashSet<string> s_perPageFixupFailedThisSession =
            new HashSet<string>();

        /// <summary>
        /// Shrink any oversized images sitting in the book folder, bring the book structurally up to
        /// date (xmatter/layout migrations; this also ensures the needed CSS file links are present,
        /// and on save the actual CSS files), then run the per-page browser fix-up over every page and
        /// save the result to disk. Returns the number of pages processed.
        ///
        /// All-or-nothing for the HTML: a failure on any page (capture error or timeout) throws and
        /// the save at the end is skipped, so none of the page fix-up is persisted. The caller
        /// (external/process-book) surfaces this as an error so BloomBridge can re-run, rather than
        /// leaving a half-processed book on disk. (The up-front image shrink is the one exception:
        /// it rewrites image files in place before the page loop, and a later failure does not undo
        /// it -- harmless, since a re-run simply finds those images already small enough.)
        ///
        /// Can run on any thread (and the caller runs it on a background thread so the UI stays responsive):
        /// the WebView2 it drives lives on the OffScreenBrowser's own dedicated thread, and this method just
        /// blocks on it. The book, however, must not be touched by another thread while we process it.
        ///
        /// When <paramref name="fitImageTextSplits"/> is true, origami image/text pages have their
        /// split auto-fit: two-pane pages with one illustration in the first pane and one text block in
        /// the second (both image-above-text and image-left-of-text), plus top-to-bottom stacks of
        /// three or more panes holding one illustration and text in the rest -- text above / picture /
        /// text below and the like. Each divider moves to the size the content actually calls for, in
        /// either direction, but never so far that text overflows, and never past the point where the
        /// illustration already fills the relevant page dimension (beyond which a bigger pane is only
        /// whitespace). This uses the real off-screen browser layout (no font/text estimation); see
        /// fitImageOverTextSplits() in bloomEditing.ts.
        ///
        /// <paramref name="progress"/>, if given, receives the whole-book update's status messages,
        /// plus the percent done on its indicator (if it has one) as the per-page pass advances, so a
        /// determinate progress dialog can show where we are. (It deliberately gets no per-page text
        /// message: the bar already shows that, and a line per page just floods the log.) It may be
        /// called on whatever thread this runs on; the progress objects we use marshal for themselves.
        /// </summary>
        public static int ProcessBook(
            Book book,
            bool fitImageTextSplits = false,
            IProgress progress = null
        )
        {
            progress = progress ?? new NullProgress();
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

            // BloomBridge writes book folders straight to disk, bypassing the normal add-image import
            // path that would otherwise have already shrunk any oversized image. The usual catch-all,
            // BookStorage.MigrateToMediaLevel1ShrinkLargeImages, won't help here: the bridge HTML
            // already carries a modern maintenance level, so BringBookUpToDate below treats that
            // migration as already done and skips it. So we do the shrink ourselves, unconditionally
            // (not gated by mediaMaintenanceLevel), and it must come BEFORE BringBookUpToDate so that
            // the off-screen per-page fix-up measures and lays out against the final, already-shrunk
            // images. (This used to have a second reason -- keeping the migration from creating its
            // modal progress dialog on this background thread, which WinForms forbids. Since BL-16646
            // the migration only creates that dialog when it is already on the UI thread, and reports
            // through the caller's IProgress otherwise, so it could no longer do that here anyway and
            // only the layout reason remains.)
            if (ImageUtils.NeedToShrinkImages(book.FolderPath))
            {
                Log("shrinking oversized images in the book folder");
                var shrinkTimer = Stopwatch.StartNew();
                ImageUtils.FixSizeAndTransparencyOfImagesInFolder(
                    book.FolderPath,
                    new List<string>(),
                    new NullProgress()
                );
                Log($"done shrinking images ({shrinkTimer.ElapsedMilliseconds}ms)");
            }

            book.BringBookUpToDate(progress);

            // 2. Per-page browser fix-up.
            // A book with structural errors cannot be shown for editing (the Edit tab displays an error
            // page instead), so there is nothing meaningful the per-page pass could do for it; the
            // whole-book update above (which has already saved) is all it gets.
            var errors = book.CheckForErrors();
            if (!string.IsNullOrEmpty(errors))
            {
                Log($"skipping the per-page fix-up because the book has errors: {errors}");
                return 0;
            }
            var pages = book.GetPages().Where(p => p != null).ToList();
            Log($"starting per-page fix-up of {pages.Count} pages (ckeditor stripped off-screen)");

            var pageIndex = 0;

            // Process every page with an off-screen browser that lives on its own dedicated thread, so we can
            // drive it with blocking calls that never pump the main UI message loop.
            //
            // Fresh renderer per page: we must NOT reuse a single browser control. Each editing page is a full
            // live-edit page (it opens the edit WebSocket channel and fires editView/* API calls on load);
            // that residual state wedges the next top-level navigation in the same control, which hangs or
            // crashes Bloom on the 2nd page. StartFreshBrowser() gives each page a clean renderer, so every
            // page behaves like the always-working first one.
            //
            // Shared environment: OffScreenBrowser keeps ONE CoreWebView2Environment (one browser process,
            // user-data folder, and HTTP cache) alive across those fresh renderers, so we don't pay
            // environment creation per page and later pages navigate against a warm cache. (Measured, with the
            // previous shared-environment approach, ~31s -> ~18s for a 22-page book.)
            using (var browser = new OffScreenBrowser())
            {
                try
                {
                    // If realizing the off-screen browser pulled Bloom to the front, put the previous window
                    // back so we keep processing quietly in the background.
                    RestoreForeground(priorForeground);

                    foreach (var page in pages)
                    {
                        pageIndex++;
                        // We deliberately do NOT write a per-page status message here. The progress
                        // dialog is determinate, so the percent bar below already shows how far we
                        // are; a "Updating page N of M" line per page just fills the log with dozens
                        // of near-identical lines that duplicate the bar (BL-16852).
                        if (progress.ProgressIndicator != null)
                            progress.ProgressIndicator.PercentCompleted =
                                (pageIndex - 1) * 100 / pages.Count;
                        if (pageIndex > 1)
                        {
                            // Fresh renderer for this page (see above); the shared environment stays warm.
                            browser.StartFreshBrowser();
                            RestoreForeground(priorForeground);
                        }

                        ProcessOnePage(book, browser, page, fitImageTextSplits);

                        Log($"page {pageIndex}/{pages.Count} [{page.Id}] done");
                    }
                }
                finally
                {
                    // Final safety net: make sure we leave the foreground where we found it, even if a
                    // page threw partway through the batch.
                    RestoreForeground(priorForeground);
                }
            }

            // Record that this Bloom version has applied the per-page fix-up to this book at its
            // current page size, so NeedsPerPageFixup can tell it need not be done again unless a
            // newer Bloom or a page-size change makes it stale. Only reached when every page
            // succeeded (a failure throws before here), so we never claim a half-done book is done.
            StampPerPageFixupDone(book);

            // 3. One full save now that every page's in-memory DOM (and the stamp above) has been updated.
            book.Save();
            if (progress.ProgressIndicator != null)
                progress.ProgressIndicator.PercentCompleted = 100;

            Log($"DONE: {pages.Count} pages");
            return pages.Count;
        }

        /// <summary>
        /// True if the per-page browser fix-up (ProcessBook's off-screen page pass) should be run on
        /// this book before it is edited or published. A book carries the fix-up automatically once it
        /// has been opened for editing in the current Bloom at its current page size; this catches the
        /// books that have NOT — old books, and books whose page size changed since it was last done.
        ///
        /// It is "needed" when any of these holds:
        ///  - the book has never recorded a fix-up (an old book, or one made by a Bloom without this);
        ///  - the recorded version is older than this Bloom (a newer Bloom's DOM work is not yet applied);
        ///  - the recorded page size/orientation differs from the book's current one (the layout-derived
        ///    measurements — image sizing, canvas-element geometry — need recomputing).
        ///
        /// Returns false for a book we could not usefully process anyway: one we cannot save (e.g. a
        /// Team Collection book not checked out — EnsureUpToDate would refuse it too), or one with
        /// structural errors (it would show an error page rather than editable pages).
        /// </summary>
        public static bool NeedsPerPageFixup(Book book)
        {
            if (book == null || !book.IsSaveable)
                return false;
            if (!string.IsNullOrEmpty(book.CheckForErrors()))
                return false;

            var dom = book.OurHtmlDom;
            var stampedVersionString = dom.GetMetaValue(kPerPageFixupVersionMeta, "");
            if (!Version.TryParse(stampedVersionString, out var stampedVersion))
                return true; // never done, or an unreadable stamp we should redo

            if (stampedVersion < GetRunningBloomVersion())
                return true; // last done by an older Bloom

            // Same or newer Bloom did it; the only remaining reason to redo is a page-size change.
            var stampedLayout = dom.GetMetaValue(kPerPageFixupLayoutMeta, "");
            return stampedLayout != GetLayoutStamp(book);
        }

        /// <summary>
        /// Run the per-page browser fix-up on <paramref name="book"/> if NeedsPerPageFixup says it is
        /// due, behind a modal progress dialog, and return true if it actually ran. Called before a
        /// book is edited (EditingModel.OnBecomeVisible), before it is published (PublishView.Activate),
        /// and after a page-size change (EditingModel.SetLayout). No-op (returns false) when the book
        /// does not need it, or when a run already failed for this book this session (so we don't
        /// re-prompt on every tab switch).
        ///
        /// Must be called on the UI thread: it shows a modal dialog. The heavy work runs on the
        /// dialog's background worker (ProcessBook drives its own off-screen browser thread and the
        /// pages it loads call back into Bloom's API server, so it must not run on the UI thread),
        /// exactly like the "Update Book" command it shares ProcessBook with.
        /// </summary>
        public static bool EnsurePerPageFixupIfNeeded(
            Book book,
            BloomWebSocketServer webSocketServer
        )
        {
            if (!NeedsPerPageFixup(book))
                return false;
            if (s_perPageFixupFailedThisSession.Contains(book.ID))
                return false;

            // Reuse the "Update Book" label: to the user this is the same operation, applied for them
            // automatically rather than on request.
            var title = LocalizationManager.GetString(
                "CollectionTab.BookMenu.UpdateFrontMatterToolStrip",
                "Update Book"
            );
            BrowserProgressDialog.DoWorkWithProgressDialog(
                webSocketServer,
                () =>
                {
                    var dlg = new ReactDialog(
                        "progressDialogBundle",
                        new
                        {
                            title,
                            titleColor = "white",
                            titleBackgroundColor = Palette.kBloomBlueHex,
                            showReportButton = "if-error",
                            determinate = true,
                            linearProgress = true,
                        },
                        title
                    );
                    dlg.SetScaledSize(560, 400);
                    return dlg;
                },
                (progress, worker) =>
                {
                    // Tell the user why Bloom paused to do this; they did not ask for it.
                    progress.MessageWithoutLocalizing(
                        LocalizationManager.GetString(
                            "BookProcessor.AutoUpdateExplanation",
                            "Bloom needs to update the pages of this book so they work well with this version of Bloom. This happens once for each book, and again if you change the page size."
                        ),
                        ProgressKind.Instruction
                    );
                    try
                    {
                        ProcessBook(book, progress: new WebProgressAdapter(progress));
                    }
                    catch (Exception e)
                    {
                        // Don't retry this book until Bloom restarts (see s_perPageFixupFailedThisSession),
                        // and make sure the details reach the log; the dialog shows the message to the user.
                        //
                        // Pages that were processed before the failure stay updated in the book's
                        // in-memory DOM on purpose. A page is replaced only after its capture
                        // succeeded, and each replaced page is a complete, correctly migrated page,
                        // exactly what visiting it in the Edit tab produces, so a later ordinary save
                        // persisting some migrated pages alongside unmigrated ones loses nothing: that
                        // mixture is just the state every book was in before this feature. And since
                        // the stamp is written only when every page succeeded, NeedsPerPageFixup stays
                        // true and a later run finishes the rest. (BloomBridge's process-book gets its
                        // all-or-nothing behavior by reloading its own separate book object; the live
                        // book has no need of that.)
                        s_perPageFixupFailedThisSession.Add(book.ID);
                        SIL.Reporting.Logger.WriteError(
                            "Automatic page update failed for " + book.NameBestForUserDisplay,
                            e
                        );
                        throw;
                    }
                    return false; // no error: close the dialog automatically
                }
            );
            return true;
        }

        // The Bloom version (major.minor.build) whose per-page fix-up is stamped into a processed book.
        // Shell.GetShortVersionInfo() reads it from the running assembly (e.g. "6.5.0") and parses
        // cleanly as a Version, unlike Application.ProductVersion, which can carry a channel suffix.
        private static Version GetRunningBloomVersion()
        {
            return Version.TryParse(Shell.GetShortVersionInfo(), out var v) ? v : new Version(0, 0);
        }

        // The page size + orientation class the book currently uses, e.g. "A5Portrait". This is what
        // governs the layout-derived measurements the per-page fix-up computes, so a change to it is
        // exactly when those measurements need recomputing.
        private static string GetLayoutStamp(Book book)
        {
            return book.GetLayout().SizeAndOrientation.ClassName;
        }

        // Record, in the book's HTML, that this Bloom version applied the per-page fix-up at the
        // current page size. Written just before ProcessBook's final Save so it is persisted with it.
        private static void StampPerPageFixupDone(Book book)
        {
            book.OurHtmlDom.UpdateMetaElement(
                kPerPageFixupVersionMeta,
                GetRunningBloomVersion().ToString()
            );
            book.OurHtmlDom.UpdateMetaElement(kPerPageFixupLayoutMeta, GetLayoutStamp(book));
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
            OffScreenBrowser browser,
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
            // We navigate WITHOUT waiting for the 'load' event, which is unreliable for a full editing page
            // loaded off-screen: the editing bundle opens the edit WebSocket channel and fires editView/* API
            // calls on load, and we have observed document.readyState getting stuck at "interactive" (load
            // never firing) even though bootstrap()/SetupElements() has fully run and there are no pending
            // sub-resources. Waiting for 'load' would just burn the whole timeout. Instead we fire the
            // navigation and then poll for __bloomEditablePageReady, which is the signal we actually care
            // about (the load-time DOM fix-ups are in place). The browser is already ready to navigate (the
            // OffScreenBrowser blocked until it was) so there is no ready-wait to do here.
            browser.NavigateWithoutWaitingForLoad(dom, InMemoryHtmlFileSource.Frame);

            // Wait until bootstrap()/SetupElements() has actually run (signaled by
            // __bloomEditablePageReady), not merely until the bundle's exports exist, so the load-time
            // DOM fix-ups are in place before we capture the page.
            WaitForJavascriptResult(
                browser,
                "(window.__bloomEditablePageReady && window.editablePageBundle) ? 'ready' : ''",
                "the editing bundle to initialize",
                page.Id
            );

            // Ask the bundle to gather the (now browser-processed) page content. It stashes the
            // result on window for us to poll, rather than posting to the live editView API (which
            // would feed the live EditingModel and corrupt the live editor's state).
            // The boolean tells the bundle whether to auto-fit simple image/text origami splits before
            // capturing (see fitImageOverTextSplits in bloomEditing.ts).
            // Fire-and-forget: capture is asynchronous (it stashes onto window.__bloomExternalPageContent
            // when it finishes) and we poll for that below, so there is no result to wait for here.
            browser.RunJavascriptFireAndForget(
                $"window.editablePageBundle.captureContentForExternalProcessing({(fitImageTextSplits ? "true" : "false")})"
            );

            var pageContent = WaitForJavascriptResult(
                browser,
                "window.__bloomExternalPageContent || ''",
                "the page content to be captured",
                page.Id
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
        /// Polls a javascript expression (which evaluates to a non-empty string when "ready") against the
        /// given off-screen browser until it is non-empty or we time out, returning the result.
        ///
        /// We deliberately keep this "poll a window global" approach for the off-screen processor rather than
        /// the async editView/pageContent API callback the live editor uses: that callback pattern needs the
        /// live EditingModel and edit WebSocket channel, which a throwaway off-screen browser doesn't have,
        /// and the per-page loop here wants a deterministic in-line result. Each poll blocks the calling
        /// thread while the browser's OWN thread runs the script, so we never pump the main UI message loop
        /// between polls (just sleep briefly) — this is what replaced the old
        /// RunJavascriptWithStringResult_Sync_Dangerous, which pumped the main loop and risked the reentrancy
        /// behind past page-content deadlocks (BL-13120 etc.).
        /// </summary>
        internal static string WaitForJavascriptResult(
            OffScreenBrowser browser,
            string script,
            string whatWeAreWaitingFor,
            string pageId,
            int timeoutMs = kReadyTimeoutMs
        )
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                var result = browser.RunJavascript(script);
                if (!string.IsNullOrEmpty(result))
                    return result;
                Thread.Sleep(20);
            }
            throw new ApplicationException(
                $"process-book: timed out waiting for {whatWeAreWaitingFor} on page {pageId}."
            );
        }
    }
}
