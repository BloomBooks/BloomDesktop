using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.MiscUI;
using Bloom.Publish;
using Bloom.SafeXml;
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

        // The share of the progress bar given to the whole-book update in ProcessBook; the pages
        // get the rest.
        private const int kWholeBookPercent = 5;

        // A <meta> in the book's HTML that earlier versions used to record, for the whole book, the
        // BookStorage.kBrowserMaintenanceLevel it had been brought to. Pages now record that
        // themselves (kPageLevelAttribute); we no longer write this, and read it only so that a
        // level above ours is still brought down when we save (ClampBrowserMaintenanceLevelToOurs).
        internal const string kBrowserMaintenanceLevelMeta = "browserMaintenanceLevel";

        // Attributes on each .bloom-page recording the BookStorage.kBrowserMaintenanceLevel the page
        // was last saved at with its load-time fix-ups finished, and the page size/orientation (e.g.
        // "A5Portrait") it was laid out at. The editing JavaScript writes them (stampPageAsUpdated in
        // bloomEditing.ts), because only the page knows when that work has finished; they reach the
        // book through HtmlDom.ProcessPageAfterEditing. A page is due for the fix-up when either is
        // missing or does not match: see PageNeedsFixup.
        internal const string kPageLevelAttribute = "data-browser-maintenance-level";
        internal const string kPageLayoutAttribute = "data-browser-maintenance-layout";

        // Attributes C# puts on the <body> of every editable page (AddStampTargetToEditablePage),
        // telling the JavaScript what to write into the two above. The body is not saved.
        internal const string kTargetLevelAttribute = "data-target-browser-maintenance-level";
        internal const string kTargetLayoutAttribute = "data-target-browser-maintenance-layout";

        // Books for which the automatic per-page fix-up (EnsurePerPageFixupIfNeededThen) was tried this
        // session and either threw or left some page unstamped. Either way NeedsPerPageFixup would
        // keep saying "yes" and we would re-prompt every time; remembering it lets us stop pestering
        // until Bloom is restarted (by when the cause may be gone). Keyed by book id.
        // Guarded by s_automaticFixupLock.
        private static readonly HashSet<string> s_perPageFixupFailedThisSession =
            new HashSet<string>();

        // Books whose automatic per-page fix-up has been started and has not yet finished. While a
        // book is here, its pass owns it: the pass rebuilds the book on a background thread, and
        // anything else that brought the book up to date meanwhile would do so at the same time.
        // Keyed by book id. Guarded by s_automaticFixupLock.
        private static readonly HashSet<string> s_automaticFixupUnderway = new HashSet<string>();
        private static readonly object s_automaticFixupLock = new object();

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
        /// each WebView2 it drives lives on its OffScreenBrowser's own dedicated thread, and this method just
        /// blocks on them. The book, however, must not be touched by another thread while we process it.
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
        /// <paramref name="progress"/>, if given, receives the percent done on its indicator (if it
        /// has one) as the passes advance, so a determinate progress dialog can show where we are,
        /// and any warning or error. It deliberately gets none of the status text the whole-book
        /// update and its per-image passes write ("Updating pages...", one line per image), nor a
        /// per-page message: the bar already shows how far along we are, and those lines just fill
        /// the dialog's log (BL-16893). A caller that wants the dialog to say what is happening
        /// writes that itself before calling (see EnsurePerPageFixupIfNeededThen). It may be called on
        /// whatever thread this runs on; the progress objects we use marshal for themselves.
        ///
        /// With <paramref name="onlyPagesNeedingFixup"/>, only the pages PageNeedsFixup picks are
        /// loaded; the others were already saved with their fix-ups done, whether by an earlier run
        /// or by being visited in the Edit tab. The whole-book update and the save still happen.
        /// </summary>
        public static int ProcessBook(
            Book book,
            bool fitImageTextSplits = false,
            IProgress progress = null,
            bool onlyPagesNeedingFixup = false
        )
        {
            // Drop the status lines (see the summary); the percent, warnings and errors still get
            // through. A NullProgress is left as it is: wrapping it would make it look like
            // somewhere to report to (MigrateToMediaLevel1ShrinkLargeImages checks for exactly that),
            // and a caller's own instance may carry state we must not lose (e.g. CancelRequested on
            // PdfMaker.CancellableNullProgress).
            if (progress == null)
                progress = new NullProgress();
            else if (!(progress is NullProgress))
                progress = new QuietStatusProgress(progress);
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

            // The whole-book update reports its own 0-100 (its image passes each run the bar to near
            // the end), so give it only the start of the bar and the pages the rest; otherwise the bar
            // shoots up and then drops back when the pages start.
            book.BringBookUpToDate(
                progress is NullProgress
                    ? progress
                    : new QuietStatusProgress(progress, 0, kWholeBookPercent)
            );

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
            // Chosen after the whole-book update, which can replace pages (xmatter, for one).
            var pages = onlyPagesNeedingFixup
                ? PagesNeedingFixup(book)
                : book.GetPages().Where(p => p != null).ToList();
            Log($"starting per-page fix-up of {pages.Count} pages (ckeditor stripped off-screen)");

            var pageIndex = 0;

            // Process the pages with an off-screen browser that lives on its own dedicated thread, so we
            // can drive it with blocking calls that never pump the main UI message loop.
            //
            // The browser loads the pages one after another into the same control, so its renderer keeps
            // the compiled editing bundle from one page to the next; a new renderer for every page roughly
            // doubles the time per page. See ProcessOnePage for what that requires.
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
                        ProcessOnePage(book, browser, page, fitImageTextSplits);

                        // We deliberately do NOT write a per-page status message here. The progress
                        // dialog is determinate, so the percent bar already shows how far we are; a
                        // "Updating page N of M" line per page just fills the log with dozens of
                        // near-identical lines that duplicate the bar (BL-16852).
                        if (progress.ProgressIndicator != null)
                            progress.ProgressIndicator.PercentCompleted =
                                kWholeBookPercent
                                + pageIndex * (100 - kWholeBookPercent) / pages.Count;
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

            // 3. One full save now that every page's in-memory DOM has been updated.
            book.Save();
            if (progress.ProgressIndicator != null)
                progress.ProgressIndicator.PercentCompleted = 100;

            Log($"DONE: {pages.Count} pages");
            return pages.Count;
        }

        /// <summary>
        /// True if the per-page browser fix-up (ProcessBook's off-screen page pass) should be run on
        /// this book before it is edited or published: some page of it is due (see PageNeedsFixup).
        ///
        /// Note that this deliberately does NOT compare Bloom versions. Version numbers are not
        /// comparable across channels (release, alpha and BetaInternal use different sequences), and
        /// keying off them would reprocess every book on every build instead of only when we change
        /// something that matters.
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
            return PagesNeedingFixup(book).Count > 0;
        }

        /// <summary>
        /// True if Bloom should, of its own accord, run the per-page fix-up on this book: it is due
        /// (NeedsPerPageFixup), and it has not already failed or left pages unstamped this session.
        /// </summary>
        public static bool AutomaticFixupIsDue(Book book)
        {
            lock (s_automaticFixupLock)
            {
                if (s_perPageFixupFailedThisSession.Contains(book.ID))
                    return false;
            }
            return NeedsPerPageFixup(book);
        }

        private static void NoteAutomaticFixupFailed(Book book)
        {
            lock (s_automaticFixupLock)
                s_perPageFixupFailedThisSession.Add(book.ID);
        }

        private static void NoteAutomaticFixupFinished(Book book)
        {
            lock (s_automaticFixupLock)
                s_automaticFixupUnderway.Remove(book.ID);
        }

        /// <summary>
        /// The pages of <paramref name="book"/> that PageNeedsFixup says are due, in book order.
        /// </summary>
        public static List<IPage> PagesNeedingFixup(Book book)
        {
            var layout = GetLayoutStamp(book);
            return book.GetPages()
                .Where(p => p != null && PageNeedsFixup(p.GetDivNodeForThisPage(), layout))
                .ToList();
        }

        /// <summary>
        /// True if <paramref name="pageDiv"/> is due for the per-page fix-up at page size
        /// <paramref name="layout"/>. It is due when any of these holds:
        ///  - it records no browser maintenance level (a new page, a page from an older Bloom, or one
        ///    never saved with its fix-ups finished), or one we cannot read;
        ///  - the recorded level is below BookStorage.kBrowserMaintenanceLevel (we have since added
        ///    fix-ups this page has not been through);
        ///  - the recorded page size differs from the book's (the layout-derived measurements, such as
        ///    image sizing and canvas-element geometry, are relative to the page).
        /// </summary>
        internal static bool PageNeedsFixup(SafeXmlElement pageDiv, string layout)
        {
            if (!int.TryParse(pageDiv.GetAttribute(kPageLevelAttribute), out var level))
                return true;
            if (level < BookStorage.kBrowserMaintenanceLevel)
                return true;
            return pageDiv.GetAttribute(kPageLayoutAttribute) != layout;
        }

        /// <summary>
        /// Tell the editing JavaScript in <paramref name="pageDom"/> what to stamp the page with once
        /// its load-time fix-ups are done (see kTargetLevelAttribute). Called for every editable page,
        /// live or off-screen (Book.GetEditableHtmlDomForPage).
        /// </summary>
        internal static void AddStampTargetToEditablePage(Book book, HtmlDom pageDom)
        {
            pageDom.Body.SetAttribute(
                kTargetLevelAttribute,
                BookStorage.kBrowserMaintenanceLevel.ToString(CultureInfo.InvariantCulture)
            );
            pageDom.Body.SetAttribute(kTargetLayoutAttribute, GetLayoutStamp(book));
        }

        /// <summary>
        /// Carry the stamp from a page coming back from the editor into the book's copy of it
        /// (HtmlDom.ProcessPageAfterEditing). A page that comes back without one keeps whatever the
        /// book had: the editor only omits it when the page was saved before its load-time work had
        /// finished, which says nothing about earlier visits. A level above ours is brought down to
        /// ours, since this Bloom has just written the page (see ClampBrowserMaintenanceLevelToOurs).
        /// </summary>
        internal static void CopyPageStamp(
            SafeXmlElement destinationPageDiv,
            SafeXmlElement editedPageDiv
        )
        {
            if (!editedPageDiv.HasAttribute(kPageLevelAttribute))
                return;
            var level = editedPageDiv.GetAttribute(kPageLevelAttribute);
            if (int.TryParse(level, out var n) && n > BookStorage.kBrowserMaintenanceLevel)
                level = BookStorage.kBrowserMaintenanceLevel.ToString(CultureInfo.InvariantCulture);
            destinationPageDiv.SetAttribute(kPageLevelAttribute, level);
            destinationPageDiv.SetAttribute(
                kPageLayoutAttribute,
                editedPageDiv.GetAttribute(kPageLayoutAttribute)
            );
        }

        /// <summary>
        /// Remove the stamp from a page that is being copied into a book from somewhere else (a
        /// template page, or the pages of a shell a new book is made from): whatever fix-ups it had
        /// were done for another book, with other languages and settings.
        /// </summary>
        internal static void ClearPageStamp(SafeXmlElement pageDiv)
        {
            pageDiv.RemoveAttribute(kPageLevelAttribute);
            pageDiv.RemoveAttribute(kPageLayoutAttribute);
        }

        /// <summary>
        /// True if the book, or any page of it, records a browser maintenance level higher than this
        /// Bloom knows how to produce. A missing or unreadable level is not "above ours"; PageNeedsFixup
        /// already treats that as never done, which is the safe answer.
        /// </summary>
        internal static bool RecordsBrowserMaintenanceLevelAboveOurs(HtmlDom dom)
        {
            return IsAboveOurs(dom.GetMetaValue(kBrowserMaintenanceLevelMeta, ""))
                || PagesAboveOurs(dom).Any();
        }

        private static bool IsAboveOurs(string recorded) =>
            int.TryParse(recorded, out var level) && level > BookStorage.kBrowserMaintenanceLevel;

        private static IEnumerable<SafeXmlElement> PagesAboveOurs(HtmlDom dom) =>
            dom.SafeSelectNodes($"//div[contains(@class,'bloom-page')][@{kPageLevelAttribute}]")
                .Cast<SafeXmlElement>()
                .Where(p => IsAboveOurs(p.GetAttribute(kPageLevelAttribute)));

        /// <summary>
        /// Wherever the book or a page records a browser maintenance level HIGHER than this Bloom knows
        /// how to produce, bring it down to ours. Called as we save a book (BookStorage.Save). Returns
        /// an action that puts back what it changed, for when the write then fails.
        /// </summary>
        /// <remarks>
        /// A newer Bloom may have taken pages past what our editing JavaScript does. The moment we
        /// write the book ourselves we may have changed pages that its extra fix-ups would have
        /// handled, but the recorded level would tell the newer Bloom there was nothing to do, and the
        /// pages we touched would stay behind for good. Recording our own level instead makes that
        /// Bloom see them as due and run its pass again. Deliberately one-way: a level at or below ours
        /// is left alone, because raising it would claim work we never did.
        /// </remarks>
        internal static Action ClampBrowserMaintenanceLevelToOurs(HtmlDom dom)
        {
            var ours = BookStorage.kBrowserMaintenanceLevel.ToString(CultureInfo.InvariantCulture);
            var undo = new List<Action>();
            var bookLevel = dom.GetMetaValue(kBrowserMaintenanceLevelMeta, null);
            if (IsAboveOurs(bookLevel))
            {
                dom.UpdateMetaElement(kBrowserMaintenanceLevelMeta, ours);
                undo.Add(() => dom.UpdateMetaElement(kBrowserMaintenanceLevelMeta, bookLevel));
            }
            foreach (var page in PagesAboveOurs(dom).ToList())
            {
                var pageLevel = page.GetAttribute(kPageLevelAttribute);
                page.SetAttribute(kPageLevelAttribute, ours);
                undo.Add(() => page.SetAttribute(kPageLevelAttribute, pageLevel));
            }
            return () => undo.ForEach(a => a());
        }

        /// <summary>
        /// The one sentence the progress dialog shows while a book is being brought up to date,
        /// whether the user asked for it ("Update Book") or Bloom decided it was due. It is all the
        /// user needs: the bar above it says how far along we are, and what the individual passes
        /// are called is of no interest to anyone but us (BL-16893).
        /// </summary>
        public static string HousekeepingMessage =>
            LocalizationManager.GetString(
                "BookProcessor.HousekeepingMessage",
                "Please wait while Bloom does some housekeeping on your book..."
            );

        /// <summary>
        /// Names the single EmbeddedSimpleProgressDialog that App.tsx renders at the top level of
        /// Bloom's UI. Both the ways of bringing a book up to date open that one dialog: it has to
        /// live above the tabs because the Edit tab empties its own page while the work runs, and
        /// being there also means its backdrop covers the whole of Bloom.
        /// </summary>
        private const string kUpdateBookProgressDialogId = "updateBook";

        /// <summary>
        /// The props that open that dialog. Shared so that the automatic update and the Collection
        /// tab's "Update Book" command cannot drift apart: to the user they are the same operation,
        /// one asked for and one not.
        /// </summary>
        public static DynamicJson MakeUpdateBookProgressProps()
        {
            var props = new DynamicJson();
            dynamic props1 = props;
            props1.which = kUpdateBookProgressDialogId;
            // Reuse the "Update Book" label for both.
            props1.title = LocalizationManager.GetString(
                "CollectionTab.BookMenu.UpdateFrontMatterToolStrip",
                "Update Book"
            );
            props1.titleColor = "white";
            props1.titleBackgroundColor = Palette.kBloomBlueHex;
            props1.message = HousekeepingMessage;
            return props;
        }

        /// <summary>
        /// Run the per-page browser fix-up on <paramref name="book"/> if NeedsPerPageFixup says it is
        /// due, behind the top-level progress dialog, and then run <paramref name="doAfter"/>. Called
        /// when the AI image editor is launched (EditingModel.BringBookToCurrentBrowserLevelThen) and
        /// when a Publish tool is chosen (PublishApi, publish/switchingPublishMode).
        ///
        /// <paramref name="doAfter"/> runs whether or not there was anything to do -- the caller has
        /// a page to get back to either way. The one exception is a call made while a pass on this
        /// book is already running: that call does nothing, and only the running pass's doAfter
        /// runs. When the pass does run, doAfter runs when the dialog closes, which is on one of the
        /// API server's threads: a caller that touches the UI must marshal for itself (EditingModel
        /// does that with RunOffTheApiLock). When there was nothing to do it runs immediately, on
        /// whatever thread called us.
        ///
        /// Returns true if a pass on this book is now running, whether this call started it or an
        /// earlier one did; false if there was nothing to do (and doAfter has already run).
        ///
        /// Nothing here blocks. The heavy work is on the dialog's background worker, because
        /// ProcessBook drives its own off-screen browser thread and the pages it loads call back
        /// into Bloom's API server, so it must not run on the UI thread; this is exactly what the
        /// "Update Book" command it shares ProcessBook with does.
        /// </summary>
        public static bool EnsurePerPageFixupIfNeededThen(
            Book book,
            BloomWebSocketServer webSocketServer,
            Action doAfter
        )
        {
            // A pass on this book is already running (a second click on a Publish tool, say). Leave
            // it to that pass, whose own doAfter brings its caller back when it is done. Check this
            // before anything reads the book: that pass is rewriting it on another thread.
            lock (s_automaticFixupLock)
            {
                if (s_automaticFixupUnderway.Contains(book.ID))
                    return true;
            }
            // Nothing to do, or a run already failed for this book this session (so we don't
            // re-prompt every time). Either way the caller still has its page to get back to.
            if (!AutomaticFixupIsDue(book))
            {
                doAfter();
                return false;
            }
            lock (s_automaticFixupLock)
            {
                if (!s_automaticFixupUnderway.Add(book.ID))
                    return true;
            }

            // Deliberately not awaited: this returns as soon as the dialog is open, not when the
            // work is done, so awaiting it would tell us nothing. doAfter is how we learn it finished.
            //
            // We do have to watch it fail, though. That method is `async Task` with no await in its
            // own body, so if opening the dialog throws -- the websocket send -- the exception lands
            // in the returned task instead of being thrown here, and simply discarding the task would
            // swallow it. Our callers have already emptied the editor and doAfter is the only thing
            // that puts a page back, so losing it would leave the user looking at a blank Edit tab
            // with no dialog and no error: the very thing ReturnToPageAfterFixup exists to avoid.
            BrowserProgressDialog
                .DoWorkWithProgressDialogAsync(
                    webSocketServer,
                    MakeUpdateBookProgressProps(),
                    (progress, worker) =>
                    {
                        try
                        {
                            ProcessBook(
                                book,
                                progress: new WebProgressAdapter(progress),
                                onlyPagesNeedingFixup: true
                            );
                            // A page captured before its load-time work finished comes back unstamped, so
                            // the book would still be due and we would run again every time it is asked
                            // for. Treat that like a failure: note it and stop asking this session.
                            if (NeedsPerPageFixup(book))
                            {
                                NoteAutomaticFixupFailed(book);
                                Log(
                                    $"{PagesNeedingFixup(book).Count} page(s) still unstamped after the automatic update of {book.NameBestForUserDisplay}"
                                );
                            }
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
                            // mixture is just the state every book was in before this feature. And the
                            // pages that did not get that far are still unstamped, so a later run finishes
                            // the rest. (BloomBridge's process-book gets its
                            // all-or-nothing behavior by reloading its own separate book object; the live
                            // book has no need of that.)
                            NoteAutomaticFixupFailed(book);
                            SIL.Reporting.Logger.WriteError(
                                "Automatic page update failed for " + book.NameBestForUserDisplay,
                                e
                            );
                            throw;
                        }
                        finally
                        {
                            NoteAutomaticFixupFinished(book);
                        }
                        // A warning or error can reach the dialog as a message, without stopping the run
                        // (HaveProblemsBeenReported covers Warning, Error and Fatal alike). Nothing on
                        // this path does that today, but the dialog shows such a message if it comes, and
                        // returning false here would close the dialog the instant the work finished -- so
                        // the user would never get to read it. Keep the dialog up instead.
                        return Task.FromResult(progress.HaveProblemsBeenReported);
                    },
                    doWhenDialogCloses: doAfter
                )
                .ContinueWith(
                    t =>
                    {
                        SIL.Reporting.Logger.WriteError(
                            "Could not show the update dialog for " + book.NameBestForUserDisplay,
                            t.Exception
                        );
                        // Don't keep trying on a Bloom that cannot show it, and get the user's
                        // page back rather than leaving the editor empty.
                        NoteAutomaticFixupFailed(book);
                        NoteAutomaticFixupFinished(book);
                        doAfter();
                    },
                    TaskContinuationOptions.OnlyOnFaulted
                );
            return true;
        }

        // The page size + orientation class the book currently uses, e.g. "A5Portrait". This is what
        // governs the layout-derived measurements the per-page fix-up computes, so a change to it is
        // exactly when those measurements need recomputing.
        private static string GetLayoutStamp(Book book)
        {
            return book.GetLayout().SizeAndOrientation.ClassName;
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
            //
            // The browser may still be showing the previous page, and the navigation call returns before
            // the new document replaces it, so polling straight away would find that page's handshake
            // globals already set and capture it a second time. Clear them first. Nothing on that old
            // page sets them again: the ready flag is set once, as it loads, and the content only when
            // asked to capture.
            browser.RunJavascript(
                "window.__bloomEditablePageReady = false; window.__bloomExternalPageContent = ''; window.editablePageBundle = undefined; 'cleared'"
            );
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
