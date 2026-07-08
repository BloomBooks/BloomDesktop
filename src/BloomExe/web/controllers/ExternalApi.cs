using System;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.web;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using SIL.Reporting;

namespace Bloom.web.controllers
{
    /// <summary>
    /// API functions which are called from outside of Bloom
    /// </summary>
    public class ExternalApi
    {
        private BloomLibraryBookApiClient _bloomLibraryBookApiClient;
        private readonly CollectionModel _collectionModel;
        private readonly EditingModel _editingModel;
        private readonly WorkspaceTabSelection _tabSelection;
        private readonly BookServer _bookServer;
        private readonly CollectionSettings _collectionSettings;
        private readonly AvatarCache _avatarCache;

        // Re-entrancy guard for process-book. ProcessBook occupies the UI thread but pumps the
        // Windows message loop via Application.DoEvents() (both the pre-loop below and the per-page
        // waits in BookProcessor). Because external/* handlers are dispatched on the UI thread via
        // message posts, a second process-book request that arrives mid-run could be delivered
        // re-entrantly during one of those DoEvents pumps. The shared-environment statics in
        // WebView2Browser (_useSharedEnvironment/_sharedEnvironment) are explicitly NOT designed for
        // overlapping batches, so we reject the re-entrant call rather than let the two corrupt each
        // other. A plain bool is sufficient: everything here is single-threaded on the UI thread, so
        // there is no cross-thread race to lock against.
        private bool _processBookInProgress;

        // The most recent (or in-progress) external/process-book job. process-book replies
        // immediately with a jobId and runs the heavy work asynchronously on the UI thread; the
        // client then polls external/process-book-status until State is "done" or "failed". This
        // replaces a single long-held HTTP response — which a stale/dropped keep-alive socket could
        // silently swallow, leaving the client hung in "Converting…" forever — with a pollable
        // terminal state the client can read with short, retryable requests, so it learns precisely
        // when processing finished. Guarded by _processBookJobLock: the UI thread writes it while
        // server worker threads read it for the status endpoint.
        private readonly object _processBookJobLock = new object();
        private ProcessBookJob _processBookJob;

        private class ProcessBookJob
        {
            public string JobId;
            public string State; // "running" | "done" | "failed"
            public int Processed;
            public string BookFolderPath;
            public string HtmPath;
            public string Error;
        }

        private struct ProcessBookResult
        {
            public int Processed;
            public string BookFolderPath;
            public string HtmPath;
        }

        // Called by autofac, which creates the one instance and registers it with the server.
        public ExternalApi(
            BloomLibraryBookApiClient bloomLibraryBookApiClient,
            CollectionModel collectionModel,
            EditingModel editingModel,
            WorkspaceTabSelection tabSelection,
            BookServer bookServer,
            CollectionSettings collectionSettings,
            AvatarCache avatarCache
        )
        {
            _bloomLibraryBookApiClient = bloomLibraryBookApiClient;
            _collectionModel = collectionModel;
            _editingModel = editingModel;
            _tabSelection = tabSelection;
            _bookServer = bookServer;
            _collectionSettings = collectionSettings;
            _avatarCache = avatarCache;
        }

        /// <summary>
        /// BloomBridge is not supported on Team Collections. Operating on a TC safely would require
        /// honoring checkout state, preserving each book's TeamCollection.status file, and telling the
        /// TC about renames (and recording them in history) — none of which the external write
        /// endpoints do. Rather than silently corrupt a shared collection, the mutating endpoints
        /// (add/update/process-book) fail fast when the open collection is a Team Collection. Returns
        /// true (and fails the request) if we refused; false if it is safe to proceed.
        /// </summary>
        private bool RefuseIfTeamCollection(ApiRequest request, string endpoint)
        {
            if (_collectionModel.IsEditableCollectionATeamCollection)
            {
                request.Failed(
                    endpoint
                        + " is not supported on Team Collections. BloomBridge can only be used with a "
                        + "regular (non-Team) collection."
                );
                return true;
            }
            return false;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // This is called from bloomlibrary.org after a successful login.
            apiHandler.RegisterEndpointHandler(
                "external/login",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        var requestData = DynamicJson.Parse(request.RequiredPostJson());
                        string token = requestData.sessionToken;
                        string email = requestData.email;
                        string userId = requestData.userId;
                        // photoUrl (the user's Google/Firebase profile picture) is OPTIONAL and read
                        // defensively: the website and desktop deploy independently, so an older blorg
                        // build will not send this field at all. A missing/null value is normal and
                        // simply means "no picture" (the avatar falls back to Gravatar/initials). Only
                        // THIS field gets this lenient treatment; the others keep the fail-fast behavior.
                        //
                        // Accept either casing. Firebase's own user property is `photoURL` (capital URL)
                        // while our other fields are camelCase, so a website build that forwards
                        // `user.photoURL` verbatim is an easy and likely mismatch. Tolerating both spellings
                        // (checking IsDefined, which is case-sensitive, for each) means the nicer avatar
                        // lights up regardless of which the website happens to send.
                        string photoUrl =
                            requestData.IsDefined("photoUrl") ? (string)requestData.photoUrl
                            : requestData.IsDefined("photoURL") ? (string)requestData.photoURL
                            : null;
                        //Debug.WriteLine("Got login data " + email + " with token " + token + " and id " + userId);

                        // Register the logged-in user's profile picture (if any) in the person-keyed
                        // avatar cache BEFORE SetLoginData, because SetLoginData broadcasts the new login
                        // state, which prompts the front end to request the avatar. Registering first
                        // guarantees that request resolves to the Google photo rather than racing ahead
                        // and caching Gravatar. When photoUrl is absent/empty this is a no-op and the
                        // avatar falls back to Gravatar/initials.
                        if (!string.IsNullOrEmpty(photoUrl))
                        {
                            _avatarCache.SetKnownPhotoUrl(AvatarCache.Md5OfEmail(email), photoUrl);
                        }

                        _bloomLibraryBookApiClient.SetLoginData(
                            email,
                            userId,
                            token,
                            BookUpload.Destination
                        );

                        request.PostSucceeded();

                        Shell.ComeToFront();
                    }
                    else if (request.HttpMethod == HttpMethods.Options)
                    {
                        // blorg will send an OPTIONS request; if we don't respond successfully, things go badly.
                        request.PostSucceeded();
                    }
                },
                false
            );

            // This is called from outside Bloom (e.g. bloomlibrary.org) to bring the Bloom window to the front.
            apiHandler.RegisterEndpointHandler(
                "external/bringToFront",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        request.PostSucceeded();

                        Shell.ComeToFront();
                    }
                    else if (request.HttpMethod == HttpMethods.Options)
                    {
                        // blorg will send an OPTIONS request; if we don't respond successfully, things go badly.
                        request.PostSucceeded();
                    }
                },
                false
            );

            // Called by an external utility (e.g. BloomBridge) after it has written or
            // overwritten a book folder in this collection on disk. We make the running Bloom show the
            // current state of that book: a brand-new book is added to the collection list; a re-imported
            // existing book has its display refreshed. If the re-imported book happens to be the one open
            // in the Edit tab, we throw away any unsaved edits and reload it from disk.
            //
            // This must run on the UI thread because it can reload the Edit tab's view.
            apiHandler.RegisterEndpointHandler(
                "external/update-book",
                HandleUpdateBook,
                handleOnUiThread: true
            );

            // Called by an external utility to make the running Bloom select a particular book in the
            // collection (the one whose 'id' is supplied). This changes the current selection just as if
            // the user had clicked the book in the collection list.
            //
            // We only honor this when the Collection tab is active. Changing the selection while the user
            // is mid-edit would silently discard their unsaved page edits (EditingModel.OnBookSelectionChanged
            // clears _havePageToSave and tears down the live editor) and could leave the Edit tab in a bad
            // state. So if we're not on the Collection tab, we ignore the request rather than risk havoc.
            //
            // This must run on the UI thread because changing the selection updates the UI.
            apiHandler.RegisterEndpointHandler(
                "external/select-book",
                HandleSelectBook,
                handleOnUiThread: true
            );

            // Called by an external utility (e.g. BloomBridge's "keep this book" flow) to
            // copy a book folder from an arbitrary location on disk into the open collection and select
            // it. The source folder need NOT be in the collection; it is copied in (the source is left
            // untouched), the collection list is reloaded so the new book appears, and it becomes the
            // current selection. The reply includes the new book's 'id' so the caller can later target
            // it with external/select-book or external/update-book.
            //
            // Like external/select-book this reloads the collection and changes the selection, so we only
            // honor it while the Collection tab is active (otherwise we'd risk discarding the user's
            // unsaved edits). This must run on the UI thread because it updates the UI.
            apiHandler.RegisterEndpointHandler(
                "external/add-book",
                HandleAddBook,
                handleOnUiThread: true
            );

            // Called by an external utility (e.g. BloomBridge) to run the full "make it
            // right" pass on a book in this collection (the one whose 'id' is supplied): bring it
            // structurally up to date, then process every page off-screen in a real browser the same
            // way visiting each page in the Edit tab does, and save it to disk. This is the step that
            // applies the browser-only fix-ups (image sizing, canvas-element layout, etc.) that raw
            // generated HTML is missing. The call blocks until processing is complete.
            //
            // This must run on the UI thread because it creates and pumps an off-screen WebView2.
            //
            // requiresSync: false is essential here. The off-screen editable page this handler loads
            // makes its own /bloom/api/* calls during bootstrap (image/info to size images, bubble
            // languages, etc.). If we held the global API sync lock for the whole ~20-30s run, those
            // dependent requests (which also default to requiresSync) would block behind us while we
            // block waiting for them to complete the page — a deadlock. We don't need the global lock
            // anyway: process-book is already serialized against itself by _processBookInProgress, and
            // the user is blocked behind the opaque ExternalBusyOverlay for the duration, so there is no
            // competing user-driven API traffic to race with.
            apiHandler.RegisterEndpointHandler(
                "external/process-book",
                HandleProcessBook,
                handleOnUiThread: true,
                requiresSync: false
            );

            // Poll the status of the most recent external/process-book job (see HandleProcessBook).
            // Read-only, so it does not need the UI thread — and crucially it MUST answer on a server
            // worker thread while the UI thread is busy processing a book, so the client can watch for
            // completion mid-run. requiresSync: false for the same reason process-book uses it: while a
            // book processes, the off-screen WebView2's own /bloom/api/* bootstrap calls hold the global
            // sync lock, and a status poll must not queue behind them — it only reads an in-memory field
            // under its own lock. Returns { state: "unknown" | "running" | "done" | "failed", ... }.
            apiHandler.RegisterEndpointHandler(
                "external/process-book-status",
                HandleProcessBookStatus,
                handleOnUiThread: false,
                requiresSync: false
            );

            // Called by an external utility (e.g. BloomBridge) to discover which languages
            // the open collection is set up for, so it can tag the book content it generates correctly.
            // Returns the collection's L1/L2/L3 language tags; L3Code is null when the collection has no
            // third language. This is read-only, so it does not need the UI thread.
            apiHandler.RegisterEndpointHandler(
                "external/collection-languages",
                HandleCollectionLanguages,
                handleOnUiThread: false
            );
        }

        private void HandleCollectionLanguages(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the other external endpoints do).
                request.PostSucceeded();
                return;
            }

            // Language1/Language2 are always present; Language3 is optional.
            request.ReplyWithJson(
                new
                {
                    L1Code = _collectionSettings.Language1?.Tag,
                    L2Code = _collectionSettings.Language2?.Tag,
                    L3Code = string.IsNullOrEmpty(_collectionSettings.Language3?.Tag)
                        ? null
                        : _collectionSettings.Language3.Tag,
                }
            );
        }

        private void HandleProcessBook(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the other external endpoints do).
                request.PostSucceeded();
                return;
            }
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed("external/process-book only supports POST");
                return;
            }

            // Reject a process-book that arrives while one is already running (see field comment).
            // BloomBridge sends these sequentially so in practice this never trips, but it makes the
            // re-entrancy contract explicit and protects the shared-environment statics if it ever does.
            if (_processBookInProgress)
            {
                request.Failed(
                    "external/process-book is already processing a book; try again later"
                );
                return;
            }

            string id = null;
            string folderPath = null;
            bool fitImageTextSplits = false;
            try
            {
                var data = Newtonsoft.Json.Linq.JObject.Parse(request.RequiredPostJson());
                id = (string)data["id"];
                folderPath = (string)data["path"];
                // Optional: auto-fit simple single-image/single-text origami pages (currently both
                // image-above-text and image-left-of-text; grow the image pane to fill the space the
                // text doesn't need). Defaults to false.
                fitImageTextSplits = (bool?)data["fitImageTextSplits"] ?? false;
                if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(folderPath))
                {
                    request.Failed(
                        "external/process-book requires a book 'path' (preferred) or 'id'"
                    );
                    return;
                }

                // Processing rewrites the book on disk. Only do it from the Collection tab, so we
                // never write a book out from under the live editor or fight its save/navigate state
                // machine.
                if (_tabSelection.ActiveTab != WorkspaceTab.collection)
                {
                    request.Failed(
                        "external/process-book is only allowed while the Collection tab is active"
                    );
                    return;
                }

                if (RefuseIfTeamCollection(request, "external/process-book"))
                    return;
            }
            catch (Exception e)
            {
                // A failure validating/parsing the request (before any work started). Report it the
                // old synchronous way — there is no job to poll yet.
                Logger.WriteError(
                    "external/process-book failed to start for book " + (folderPath ?? id),
                    e
                );
                request.Failed("external/process-book failed: " + e.Message);
                return;
            }

            // The heavy work needs the UI thread (it creates and pumps an off-screen WebView2). We
            // are on the UI thread now, but we must NOT do that work here: holding the HTTP response
            // open for the whole ~20-30s run is exactly what leaves the client hung if the connection
            // is dropped (e.g. a stale keep-alive socket). Instead reply immediately with a jobId and
            // BeginInvoke the work to run after this response is sent — it queues on this same UI
            // thread's message loop and runs once this handler returns. The client polls
            // external/process-book-status for the outcome. _processBookInProgress stays true for the
            // whole async run, so the re-entrancy guard still rejects an overlapping process-book.
            var shell = Shell.GetShellOrNull();
            if (shell == null || shell.IsDisposed)
            {
                request.Failed("external/process-book: Bloom has no main window to process on.");
                return;
            }

            var jobId = Guid.NewGuid().ToString("N");
            _processBookInProgress = true;
            lock (_processBookJobLock)
            {
                _processBookJob = new ProcessBookJob { JobId = jobId, State = "running" };
            }
            request.ReplyWithJson(new { jobId, state = "running" });

            shell.BeginInvoke(
                (Action)(() => RunProcessBookJob(jobId, folderPath, id, fitImageTextSplits))
            );
        }

        /// <summary>
        /// Runs the heavy process-book work on the UI thread. Posted via BeginInvoke from
        /// HandleProcessBook so it executes after that request's reply has already been sent, then
        /// records the outcome on _processBookJob for the client to poll via
        /// external/process-book-status. Never throws to the caller (it is a fire-and-forget UI
        /// message); any failure is captured as the job's "failed" state.
        /// </summary>
        private void RunProcessBookJob(
            string jobId,
            string folderPath,
            string id,
            bool fitImageTextSplits
        )
        {
            try
            {
                // The overlay 'show', the DoEvents/Sleep spin-up loop, and the processing all run
                // inside this try so that an exception anywhere after we raise the overlay still runs
                // the finally and sends 'hide'; otherwise the modal overlay would be stuck opaque
                // until the user navigates away. (Sending 'hide' when 'show' never succeeded is a
                // harmless no-op.)
                try
                {
                    // Let the user know Bloom is busy. The work below pumps the message loop via
                    // Application.DoEvents(), so the main WebView2 keeps painting and this overlay
                    // (with its CSS spinner) stays visible/animated for the whole run. Pump a few
                    // events first so it actually appears before the heavy work ties up the thread.
                    dynamic overlay = new DynamicJson();
                    // Intentionally NOT localized, like the add-book/update-book toasts: this is an
                    // operator-facing message shown only during a BloomBridge-driven processing run.
                    overlay.message = "Bloom is processing a book for BloomBridge, please wait…";
                    BloomWebSocketServer.Instance?.SendBundle(
                        "externalProcessing",
                        "show",
                        overlay
                    );
                    for (var i = 0; i < 10; i++)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        System.Threading.Thread.Sleep(15);
                    }

                    var result = !string.IsNullOrEmpty(folderPath)
                        ? ProcessBookByPath(folderPath, fitImageTextSplits)
                        : ProcessBookById(id, fitImageTextSplits);

                    lock (_processBookJobLock)
                    {
                        if (_processBookJob?.JobId == jobId)
                        {
                            _processBookJob.State = "done";
                            _processBookJob.Processed = result.Processed;
                            _processBookJob.BookFolderPath = result.BookFolderPath;
                            _processBookJob.HtmPath = result.HtmPath;
                        }
                    }
                }
                finally
                {
                    BloomWebSocketServer.Instance?.SendEvent("externalProcessing", "hide");
                }
            }
            catch (Exception e)
            {
                Logger.WriteError("external/process-book failed for book " + (folderPath ?? id), e);
                lock (_processBookJobLock)
                {
                    if (_processBookJob?.JobId == jobId)
                    {
                        _processBookJob.State = "failed";
                        _processBookJob.Error = e.Message;
                    }
                }
            }
            finally
            {
                _processBookInProgress = false;
            }
        }

        /// <summary>
        /// Report the state of the most recent process-book job. The client polls this (with short,
        /// independent requests) until State is a terminal "done"/"failed", rather than waiting on one
        /// long-held process-book response. An optional jobId query param scopes the answer to a
        /// specific job: if it doesn't match the current job we report "unknown" so a stale poll can't
        /// misread a newer job's result.
        /// </summary>
        private void HandleProcessBookStatus(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the other external endpoints do).
                request.PostSucceeded();
                return;
            }

            var jobId = request.GetParamOrNull("jobId");
            lock (_processBookJobLock)
            {
                if (
                    _processBookJob == null
                    || (!string.IsNullOrEmpty(jobId) && _processBookJob.JobId != jobId)
                )
                {
                    request.ReplyWithJson(new { state = "unknown" });
                    return;
                }
                request.ReplyWithJson(
                    new
                    {
                        jobId = _processBookJob.JobId,
                        state = _processBookJob.State,
                        processed = _processBookJob.Processed,
                        bookFolderPath = _processBookJob.BookFolderPath,
                        htmPath = _processBookJob.HtmPath,
                        error = _processBookJob.Error,
                    }
                );
            }
        }

        /// <summary>
        /// Process a book given the path to its folder, which need NOT be a member of the open
        /// collection. The book is processed in place (the fixed-up .htm is written back to the same
        /// folder) using the running project's CollectionSettings (xmatter/branding/languages), and
        /// nothing is added to the open collection. This is what BloomBridge uses, so its staging
        /// books no longer have to be copied into the collection just to be processed.
        /// </summary>
        private ProcessBookResult ProcessBookByPath(string folderPath, bool fitImageTextSplits)
        {
            if (
                !System.IO.Directory.Exists(folderPath)
                || !SIL.IO.RobustFile.Exists(System.IO.Path.Combine(folderPath, "meta.json"))
            )
            {
                throw new ApplicationException(
                    "external/process-book could not find a book folder (with meta.json) at "
                        + folderPath
                );
            }

            // Normally the path points at an off-screen staging folder that is NOT the selected book,
            // so there's no live editor state to reconcile. But a caller could hand us the folder of the
            // book currently open in the Edit tab; capture that now (before processing, which may rename
            // the folder) so we can reload the editor afterward and not let it clobber what we wrote.
            // isInEditableCollection:true + AlwaysEditSaveContext makes Book.IsSaveable true
            // (Book.IsSaveable => IsInEditableCollection && BookInfo.IsSaveable), matching the semantics
            // of the old flow where the book was first copied into the editable collection.
            var selectedBeforeProcessing = _collectionModel.GetSelectedBookOrNull();
            var processingSelectedBook =
                selectedBeforeProcessing != null
                && AreSameFolder(selectedBeforeProcessing.FolderPath, folderPath);

            var bookInfo = new BookInfo(folderPath, true, new AlwaysEditSaveContext());
            var book = _bookServer.GetBookFromBookInfo(bookInfo);
            var pageCount = BookProcessor.ProcessBook(book, fitImageTextSplits);

            // If the folder we just rewrote is the book currently open in the Edit tab, the live
            // EditingModel still holds the pre-processed in-memory DOM; the next time the user leaves the
            // Edit tab, OnTabAboutToChange would Save() it and silently overwrite what we just wrote.
            // Discard those in-memory edits and reload from disk, then refresh the collection's view of
            // it. This mirrors ProcessBookById. It only reconciles in-memory UI state, so a failure
            // here is logged but does NOT turn the job into a failure (the disk output is already correct).
            if (processingSelectedBook)
            {
                try
                {
                    _editingModel.ReloadCurrentBookDiscardingEdits();
                    _collectionModel.ReloadEditableCollection();
                }
                catch (Exception e)
                {
                    Logger.WriteError(
                        "external/process-book: book at "
                            + folderPath
                            + " was processed and saved, but the in-memory UI refresh afterward failed",
                        e
                    );
                }
            }

            // Saving may have renamed the folder to match the book's title, so report the final
            // location the client should read its output from.
            return new ProcessBookResult
            {
                Processed = pageCount,
                BookFolderPath = book.FolderPath,
                HtmPath = book.GetPathHtmlFile(),
            };
        }

        /// <summary>
        /// True if the two paths refer to the same folder on disk (normalized, trailing separators and
        /// case ignored — Windows file systems are case-insensitive). Used to tell whether a path-based
        /// process-book is targeting the book currently open in the Edit tab.
        /// </summary>
        private static bool AreSameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            return string.Equals(
                System
                    .IO.Path.GetFullPath(a)
                    .TrimEnd(
                        System.IO.Path.DirectorySeparatorChar,
                        System.IO.Path.AltDirectorySeparatorChar
                    ),
                System
                    .IO.Path.GetFullPath(b)
                    .TrimEnd(
                        System.IO.Path.DirectorySeparatorChar,
                        System.IO.Path.AltDirectorySeparatorChar
                    ),
                StringComparison.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// Legacy flow: process a book that is a member of the open editable collection, found by its
        /// bookInstanceId.
        /// </summary>
        private ProcessBookResult ProcessBookById(string id, bool fitImageTextSplits)
        {
            var editableCollection = _collectionModel.TheOneEditableCollection;
            var collectionPath = editableCollection.PathToDirectory;
            var bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
            if (bookInfo == null)
            {
                // The book may have just been written to disk and our in-memory collection cache
                // doesn't know about it yet. Rescan from disk and look again before giving up.
                _collectionModel.ReloadEditableCollection();
                bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
            }
            if (bookInfo == null)
            {
                throw new ApplicationException(
                    "external/process-book could not find a book with id " + id
                );
            }

            // Process a fresh Book read from disk rather than any in-memory selection, so we
            // don't disturb the state of the currently-selected book object.
            var book = _bookServer.GetBookFromBookInfo(bookInfo);
            var pageCount = BookProcessor.ProcessBook(book, fitImageTextSplits);

            // The book is now processed and saved on disk: the operation the caller asked for has
            // succeeded. Everything below only reconciles in-memory UI state, so wrap it so a refresh
            // failure is logged but does NOT fail the job — otherwise we'd report failure for a book
            // whose output is already correct on disk, and the caller would discard it.
            try
            {
                // If we just rewrote the book that is currently selected, the in-memory selection now
                // disagrees with disk (we processed a separate Book instance). Discard the stale in-memory
                // copy and reload it from disk so a later trip through the Edit tab can't clobber what we
                // just wrote, then refresh the collection's view of it (list metadata + thumbnail). This
                // mirrors how external/update-book handles re-import of the selected book.
                var selected = _collectionModel.GetSelectedBookOrNull();
                if (selected != null && selected.ID == id)
                {
                    _editingModel.ReloadCurrentBookDiscardingEdits();
                    _collectionModel.ReloadEditableCollection();
                    var refreshedInfo = _collectionModel.BookInfoFromCollectionAndId(
                        collectionPath,
                        id
                    );
                    if (refreshedInfo != null)
                        _collectionModel.UpdateThumbnailAsync(
                            _collectionModel.GetBookFromBookInfo(refreshedInfo)
                        );
                }
            }
            catch (Exception e)
            {
                Logger.WriteError(
                    "external/process-book: book "
                        + id
                        + " was processed and saved, but the in-memory UI refresh afterward failed",
                    e
                );
            }

            return new ProcessBookResult
            {
                Processed = pageCount,
                BookFolderPath = book.FolderPath,
                HtmPath = book.GetPathHtmlFile(),
            };
        }

        private void HandleAddBook(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the other external endpoints do).
                request.PostSucceeded();
                return;
            }
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed("external/add-book only supports POST");
                return;
            }

            string folderPath = null;
            try
            {
                // Parse with Newtonsoft rather than Bloom's DynamicJson because the body contains a
                // Windows path, and DynamicJson's JSON->XML conversion chokes on the backslashes.
                var data = Newtonsoft.Json.Linq.JObject.Parse(request.RequiredPostJson());
                folderPath = (string)data["path"];
                if (string.IsNullOrEmpty(folderPath))
                {
                    request.Failed("external/add-book requires a book 'path'");
                    return;
                }

                // Adding a book reloads the collection and changes the current selection, which would
                // discard the user's unsaved edits if they were mid-edit. Only do it from the Collection
                // tab, matching external/select-book and external/process-book.
                if (_tabSelection.ActiveTab != WorkspaceTab.collection)
                {
                    request.Failed(
                        "external/add-book is only allowed while the Collection tab is active"
                    );
                    return;
                }

                if (RefuseIfTeamCollection(request, "external/add-book"))
                    return;

                var newBook = _collectionModel.AddBookFromFolder(folderPath);
                if (newBook == null)
                {
                    request.Failed(
                        "external/add-book copied the book but could not locate it in the collection afterward"
                    );
                    return;
                }

                // Intentionally NOT localized: operator-facing notification driven by an external tool.
                var timestamp = DateTime.Now.ToString("h:mm:ss tt");
                ToastService.ShowToast(
                    text: $"Added book \"{newBook.Title}\" ({timestamp})",
                    durationSeconds: 180
                );

                // Report the new book's id and final on-disk location so the caller can target it later.
                request.ReplyWithJson(
                    new
                    {
                        id = newBook.ID,
                        bookFolderPath = newBook.FolderPath,
                        htmPath = newBook.GetPathHtmlFile(),
                    }
                );
            }
            catch (Exception e)
            {
                Logger.WriteError("external/add-book failed for path " + folderPath, e);
                request.Failed("external/add-book failed: " + e.Message);
            }
        }

        private void HandleSelectBook(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the other external endpoints do).
                request.PostSucceeded();
                return;
            }
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed("external/select-book only supports POST");
                return;
            }

            string id = null;
            try
            {
                // Parse with Newtonsoft rather than Bloom's DynamicJson for consistency with updateBook
                // (DynamicJson's JSON->XML conversion can choke on Windows paths and other content).
                var data = Newtonsoft.Json.Linq.JObject.Parse(request.RequiredPostJson());
                id = (string)data["id"];
                if (string.IsNullOrEmpty(id))
                {
                    request.Failed("external/select-book requires a book 'id'");
                    return;
                }

                // Only change the selection when the Collection tab is active. If the user is editing
                // or publishing, quietly ignore the request rather than discard their work or disrupt
                // their current tab. We still report success so the caller isn't treated as an error.
                if (_tabSelection.ActiveTab != WorkspaceTab.collection)
                {
                    Logger.WriteEvent(
                        "external/select-book ignored for book id "
                            + id
                            + " because the Collection tab is not active (ActiveTab="
                            + _tabSelection.ActiveTab
                            + ")"
                    );
                    request.PostSucceeded();
                    return;
                }

                var editableCollection = _collectionModel.TheOneEditableCollection;
                var collectionPath = editableCollection.PathToDirectory;
                var bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                if (bookInfo == null)
                {
                    request.Failed("external/select-book could not find a book with id " + id);
                    return;
                }

                _collectionModel.SelectBook(_collectionModel.GetBookFromBookInfo(bookInfo));

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                Logger.WriteError("external/select-book failed for book id " + id, e);
                request.Failed("external/select-book failed: " + e.Message);
            }
        }

        private void HandleUpdateBook(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the login endpoint does).
                request.PostSucceeded();
                return;
            }
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed("external/update-book only supports POST");
                return;
            }

            string id = null;
            try
            {
                // Note: we parse with Newtonsoft rather than Bloom's DynamicJson because the body
                // typically contains a Windows folderPath, and DynamicJson's JSON->XML conversion
                // throws on the backslashes in such paths.
                var data = Newtonsoft.Json.Linq.JObject.Parse(request.RequiredPostJson());
                id = (string)data["id"];
                if (string.IsNullOrEmpty(id))
                {
                    request.Failed("external/update-book requires a book 'id'");
                    return;
                }

                if (RefuseIfTeamCollection(request, "external/update-book"))
                    return;

                var editableCollection = _collectionModel.TheOneEditableCollection;
                var collectionPath = editableCollection.PathToDirectory;
                var bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);

                bool added = bookInfo == null;

                if (added)
                {
                    // A new book appeared on disk. Rescan the collection so it shows up in the list,
                    // then locate it so we can name it in the toast and build its thumbnail.
                    _collectionModel.ReloadEditableCollection();
                    bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                }
                else
                {
                    // The book already existed and has been re-imported/overwritten on disk.
                    var selected = _collectionModel.GetSelectedBookOrNull();
                    if (selected != null && selected.ID == id)
                    {
                        // It's the book currently open in the Edit tab. Discard any unsaved edits to it
                        // and reload it from disk. We do NOT touch the editor for any other book, so a
                        // user editing an unrelated book never loses work.
                        // Note: when the Edit tab is live this schedules an async tab-switch (the actual
                        // switch completes via PostponedWork after the browser returns page content). The
                        // collection reload below is safe to run immediately only because it doesn't read
                        // tab/selection state.
                        _editingModel.ReloadCurrentBookDiscardingEdits();
                    }
                    // Re-read the collection so the list (titles, sort order) reflects the new content,
                    // then refresh the thumbnail below.
                    _collectionModel.ReloadEditableCollection();
                    bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                }

                if (bookInfo == null)
                {
                    // Even after reloading the collection we cannot find a book with this id on disk.
                    // The update did not land, so report failure rather than a false-positive
                    // "Added/Updated" notification that would hide the real problem from the caller.
                    request.Failed("external/update-book could not find a book with id " + id);
                    return;
                }

                string title = bookInfo.Title ?? bookInfo.QuickTitleUserDisplay ?? "";
                // GetBookFromBookInfo returns the current selection (already reloaded above) when this
                // is the selected book, otherwise a fresh Book read from disk; either way the thumbnail
                // reflects the new content.
                _collectionModel.UpdateThumbnailAsync(
                    _collectionModel.GetBookFromBookInfo(bookInfo)
                );

                // Intentionally NOT localized: this is a developer/operator-facing notification driven by
                // an external automation tool. We include a timestamp and keep the toast up for a few
                // minutes so the user can see, after the fact, that (and when) an external update landed.
                var timestamp = DateTime.Now.ToString("h:mm:ss tt");
                var verb = added ? "Added" : "Updated";
                var message = $"{verb} book \"{title}\" ({timestamp})";
                ToastService.ShowToast(text: message, durationSeconds: 180);

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                Logger.WriteError("external/update-book failed for book id " + id, e);
                request.Failed("external/update-book failed: " + e.Message);
            }
        }
    }
}
