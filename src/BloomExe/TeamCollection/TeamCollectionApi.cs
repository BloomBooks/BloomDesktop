using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.History;
using Bloom.MiscUI;
using Bloom.Registration;
using Bloom.Utils;
using Bloom.web;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using Sentry;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.TeamCollection
{
    // Implements functions used by the HTML/Typescript parts of the Team Collection code.
    // Review: should this be in web/controllers with all the other API classes, or here with all the other sharing code?
    public class TeamCollectionApi
    {
        private ITeamCollectionManager _tcManager;
        private BookSelection _bookSelection; // configured by autofac, tells us what book is selected
        private BookServer _bookServer;
        private string CurrentUser => TeamCollectionManager.CurrentUser;
        private BloomWebSocketServer _socketServer;
        private readonly CurrentEditableCollectionSelection _currentBookCollectionSelection;
        private CollectionSettings _settings;
        private CollectionModel _collectionModel;

        public static TeamCollectionApi TheOneInstance { get; private set; }

        // Called by autofac, which creates the one instance and registers it with the server.
        public TeamCollectionApi(
            CurrentEditableCollectionSelection currentBookCollectionSelection,
            CollectionSettings settings,
            BookSelection bookSelection,
            ITeamCollectionManager tcManager,
            BookServer bookServer,
            BloomWebSocketServer socketServer,
            CollectionModel collectionModel
        )
        {
            _currentBookCollectionSelection = currentBookCollectionSelection;
            _settings = settings;
            _tcManager = tcManager;
            _tcManager.CurrentCollection?.SetupMonitoringBehavior();
            _bookSelection = bookSelection;
            _socketServer = socketServer;
            _bookServer = bookServer;
            _collectionModel = collectionModel;
            TheOneInstance = this;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "teamCollection/repoFolderPath",
                HandleRepoFolderPath,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/isTeamCollectionEnabled",
                HandleIsTeamCollectionEnabled,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/bookStatus",
                HandleBookStatus,
                false,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/selectedBookStatus",
                HandleSelectedBookStatus,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/attemptLockOfCurrentBook",
                HandleAttemptLockOfCurrentBook,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/checkInCurrentBook",
                HandleCheckInCurrentBook,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/forgetChangesInSelectedBook",
                HandleForgetChangesInSelectedBook,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/chooseFolderLocation",
                HandleChooseFolderLocation,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/createTeamCollection",
                HandleCreateTeamCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/joinTeamCollection",
                HandleJoinTeamCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/checkInAllBooks",
                HandleCheckInAllBooks,
                true
            );
            apiHandler.RegisterEndpointHandler("teamCollection/getLog", HandleGetLog, false);
            apiHandler.RegisterEndpointHandler(
                "teamCollection/getCollectionName",
                HandleGetCollectionName,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/showCreateTeamCollectionDialog",
                HandleShowCreateTeamCollectionDialog,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/reportBadZip",
                HandleReportBadZip,
                true,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/showRegistrationDialog",
                HandleShowRegistrationDialog,
                true,
                false
            );
            apiHandler.RegisterEndpointHandler("teamCollection/getHistory", HandleGetHistory, true);
            apiHandler.RegisterEndpointHandler(
                "teamCollection/checkinMessage",
                HandleCheckinMessage,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "teamCollection/forceUnlock",
                HandleForceUnlock,
                false
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "teamCollection/logImportant",
                HandleLogImportant,
                null,
                false
            );
        }

        private bool HandleLogImportant(ApiRequest arg)
        {
            if (_tcManager?.MessageLog == null)
                return false;
            return _tcManager.MessageLog.Messages.Any(m => m.Important);
        }

        private void HandleForceUnlock(ApiRequest request)
        {
            if (!_tcManager.CheckConnection())
            {
                request.Failed();
                return;
            }

            try
            {
                var bookStatus = _tcManager.CurrentCollection.GetStatus(BookFolderName);
                var lockedBy = bookStatus.lockedByFirstName;
                if (string.IsNullOrEmpty(lockedBy))
                    lockedBy = bookStatus.lockedBy;
                // Could be a problem if there's no current book or it's not in the collection folder.
                // But in that case, we don't show the UI that leads to this being called.
                _tcManager.CurrentCollection.ForceUnlock(BookFolderName);
                BookHistory.AddEvent(
                    _bookSelection.CurrentSelection,
                    BookHistoryEventType.ForcedUnlock,
                    $"Admin force-unlocked while checked out to {lockedBy}."
                );

                UpdateUiForBook();

                Analytics.Track(
                    "TeamCollectionRevertOtherCheckout",
                    new Dictionary<string, string>()
                    {
                        { "CollectionId", _settings?.CollectionId },
                        { "CollectionName", _settings?.CollectionName },
                        { "Backend", _tcManager?.CurrentCollection?.GetBackendType() },
                        { "User", CurrentUser },
                        { "BookId", _bookSelection?.CurrentSelection?.ID },
                        { "BookName", _bookSelection?.CurrentSelection?.Title }
                    }
                );

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.All,
                    "Could not force unlock",
                    null,
                    e,
                    true
                );
                request.Failed("could not unlock");
            }
        }

        /// <summary>
        /// When the user edits the pending checkin message, save it away in the book history database.
        /// </summary>
        /// <param name="request"></param>
        private void HandleCheckinMessage(ApiRequest request)
        {
            var message = request.GetPostStringOrNull() ?? "";
            BookHistory.SetPendingCheckinMessage(request.CurrentBook, message);
            request.PostSucceeded();
        }

        public static string BadZipPath;

        /// <summary>
        /// Report a bad zip file to the user, allowing a report back to the developers.
        /// </summary>
        /// <remarks>
        /// This method is run without sync and should not touch anything that is not private to the thread.
        /// It needs to be without sync because it launches a modal dialog that needs to access the server
        /// and could otherwise cause deadlocks.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-12278 for what happens if the method is
        /// run with sync.
        /// </remarks>
        private void HandleReportBadZip(ApiRequest request)
        {
            var fileEncoded = request.Parameters["file"];
            var file = UrlPathString.CreateFromUrlEncodedString(fileEncoded).NotEncoded;
            request.PostSucceeded(); // this should come before a modal dialog
            NonFatalProblem.Report(
                ModalIf.All,
                PassiveIf.All,
                (_tcManager.CurrentCollection as FolderTeamCollection).GetSimpleBadZipFileMessage(
                    Path.GetFileNameWithoutExtension(file)
                ),
                additionalFilesToInclude: new[] { file }
            );
        }

        private void HandleShowRegistrationDialog(ApiRequest request)
        {
            using (var dlg = new RegistrationDialog(false, _tcManager.UserMayChangeEmail))
            {
                dlg.ShowDialog();
            }
            request.PostSucceeded();
        }

        private void HandleShowCreateTeamCollectionDialog(ApiRequest request)
        {
            ReactDialog.ShowOnIdle(
                "createTeamCollectionDialogBundle",
                new { defaultRepoFolder = DropboxUtils.GetDropboxFolderPath() },
                600,
                580,
                null,
                null,
                "Create Team Collection"
            );
            request.PostSucceeded();
        }

        private void HandleGetCollectionName(ApiRequest request)
        {
            request.ReplyWithText(_settings.CollectionName);
        }

        private void HandleGetLog(ApiRequest request)
        {
            /* keeping this around as a comment to make it easier to work on the display


            _tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "blah blah blah blah");
            _tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "Another message. I just simplified this English, but the surrounding code would lead me to think. I just simplified this English, but the surrounding code would lead me to think.");
            _tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.Error, "", "An error of some sort. I just simplified this English, but the surrounding code would lead me to think. I just simplified this English, but the surrounding code would lead me to think.");
            _tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.Error, "", "An error of some sort. I just simplified this English, but the surrounding code would lead me to think. I just simplified this English, but the surrounding code would lead me to think.");
            _tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "Another message.");
            _tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.NewStuff, "", "a new stuff message.");
            _tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "Another message.");
            */
            try
            {
                if (_tcManager.MessageLog == null)
                {
                    request.Failed();
                    return;
                }

                request.ReplyWithJson(
                    JsonConvert.SerializeObject(_tcManager.MessageLog.GetProgressMessages())
                );
            }
            catch (Exception e)
            {
                // Not sure what to do here: getting the log should never crash.
                Logger.WriteError("TeamCollectionApi.HandleGetLog() crashed", e);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );
                request.Failed("get log failed");
            }
        }

        public void HandleRepoFolderPath(ApiRequest request)
        {
            try
            {
                Debug.Assert(
                    request.HttpMethod == HttpMethods.Get,
                    "only get is implemented for the teamCollection/repoFolderPath api endpoint"
                );
                request.ReplyWithText(
                    _tcManager.CurrentCollectionEvenIfDisconnected?.RepoDescription ?? ""
                );
            }
            catch (Exception e)
            {
                // Not sure what to do here: getting the repo's folder path should never crash.
                Logger.WriteError("TeamCollectionApi.HandleRepoFolderPath() crashed", e);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );
                request.Failed("get repo folder path failed");
            }
        }

        private void HandleJoinTeamCollection(ApiRequest request)
        {
            try
            {
                var joinType = FolderTeamCollection.JoinCollectionTeam();
                ReactDialog.CloseCurrentModal();

                Analytics.Track(
                    "TeamCollectionJoin",
                    new Dictionary<string, string>()
                    {
                        { "CollectionId", _settings?.CollectionId },
                        { "CollectionName", _settings?.CollectionName },
                        { "Backend", _tcManager?.CurrentCollection?.GetBackendType() },
                        { "User", CurrentUser },
                        { "JoinType", joinType } // create, open, or merge
                    }
                );

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                // Not sure what to do here: joining the collection crashed.
                Logger.WriteError("TeamCollectionApi.HandleJoinTeamCollection() crashed", e);
                var msg = LocalizationManager.GetString(
                    "TeamCollection.ErrorJoining",
                    "Could not join Team Collection"
                );
                ErrorReport.NotifyUserOfProblem(e, msg);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );

                // Since we have already informed the user above, it is better to just report a success here.
                // Otherwise, they will also get a toast.
                request.PostSucceeded();
            }
        }

        public void HandleIsTeamCollectionEnabled(ApiRequest request)
        {
            try
            {
                // We don't need any of the Sharing UI if the selected book isn't in the editable
                // collection (or if the collection doesn't have a Team Collection at all).
                request.ReplyWithBoolean(
                    _tcManager.CurrentCollectionEvenIfDisconnected != null
                        && (
                            _bookSelection.CurrentSelection == null
                            || _bookSelection.CurrentSelection.IsInEditableCollection
                        )
                );
            }
            catch (Exception e)
            {
                // Not sure what to do here: checking whether TeamCollection is enabled should never crash.
                Logger.WriteError("TeamCollectionApi.HandleIsTeamCollectionEnabled() crashed", e);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );
                request.Failed("checking if Team Collections are enabled failed");
            }
        }

        // needs to be thread-safe
        public void HandleBookStatus(ApiRequest request)
        {
            try
            {
                if (!TeamCollectionManager.IsRegistrationSufficient())
                {
                    request.Failed(HttpStatusCode.ServiceUnavailable, "Team Collection not active");
                    return;
                }

                var bookFolderName = Path.GetFileName(request.RequiredParam("folderName"));
                request.ReplyWithJson(GetBookStatusJson(bookFolderName, null));
            }
            catch (Exception e)
            {
                // Not sure what to do here: getting the current book status crashed.
                Logger.WriteError("TeamCollectionApi.HandleCurrentBookStatus() crashed", e);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );
                request.Failed("getting the book status failed");
            }
        }

        // Needs to be thread-safe
        private string GetBookStatusJson(string bookFolderName, Book.Book book)
        {
            string whoHasBookLocked = null;
            DateTime whenLocked = DateTime.MaxValue;
            // bookFolderName may be null when no book is selected, e.g., after deleting one.
            var status =
                bookFolderName == null
                    ? null
                    : _tcManager.CurrentCollection?.GetStatus(bookFolderName);
            // At this level, we know this is the path to the .bloom file in the repo
            // (though if we implement another backend, we'll have to generalize the notion somehow).
            // For the Javascript, it's just an argument to pass to
            // CommonMessages.GetPleaseClickHereForHelpMessage(). It's only used if hasInvalidRepoData is non-empty.
            string clickHereArg = "";
            var folderTC = _tcManager.CurrentCollection as FolderTeamCollection;
            if (folderTC != null && bookFolderName != null)
            {
                clickHereArg = UrlPathString
                    .CreateFromUnencodedString(folderTC.GetPathToBookFileInRepo(bookFolderName))
                    .UrlEncoded;
            }

            string invalidRepoDataErrorMsg =
                (status?.hasInvalidRepoData ?? false)
                    ? (folderTC)?.GetCouldNotOpenCorruptZipMessage()
                    : "";

            if (bookFolderName == null)
            {
                return JsonConvert.SerializeObject(
                    new
                    {
                        // Keep this in sync with IBookTeamCollectionStatus defined in TeamCollectionApi.tsx
                        who = "",
                        whoFirstName = "",
                        whoSurname = "",
                        when = DateTime.Now.ToShortDateString(),
                        where = "",
                        currentUser = CurrentUser,
                        currentUserName = TeamCollectionManager.CurrentUserFirstName,
                        currentMachine = TeamCollectionManager.CurrentMachine,
                        hasAProblem = false,
                        invalidRepoDataErrorMsg = "",
                        clickHereArg = "",
                        isChangedRemotely = false,
                        isDisconnected = false,
                        isNewLocalBook = true,
                        checkinMessage = "",
                        isUserAdmin = _tcManager.OkToEditCollectionSettings
                    }
                );
            }

            bool isNewLocalBook = false;
            bool hasConflictingChange = false;
            try
            {
                whoHasBookLocked = _tcManager.CurrentCollectionEvenIfDisconnected?.WhoHasBookLocked(
                    bookFolderName
                );
                // It's debatable whether to use CurrentCollectionEvenIfDisconnected everywhere. For now, I've only changed
                // it for the two bits of information actually needed by the status panel when disconnected.
                whenLocked =
                    _tcManager.CurrentCollection?.WhenWasBookLocked(bookFolderName)
                    ?? DateTime.MaxValue;
                if (whoHasBookLocked == TeamCollection.FakeUserIndicatingNewBook)
                {
                    // This situation comes about from two different scenarios:
                    // 1) The user is creating a new book and TeamCollection status doesn't matter
                    // 2) The user is trying to check out an existing book and TeamCollectionManager
                    //    discovers [through CheckConnection()] that it is suddenly in a disconnected
                    //    state.
                    // In both cases, the current selected book is in view. The only way to tell
                    // these two situations apart is that in (1) book.IsSaveable is true
                    // and in (2) it is not.
                    // Or, book may be null because we're just getting a status to show in the list
                    // of all books. In that case, book is null, but it's fairly safe to assume it's a new local book.
                    if (book?.IsSaveable ?? true)
                    {
                        whoHasBookLocked = CurrentUser;
                        isNewLocalBook = true;
                    }
                    else
                    {
                        whoHasBookLocked = null;
                    }
                }
                hasConflictingChange =
                    _tcManager.CurrentCollection?.HasLocalChangesThatMustBeClobbered(bookFolderName)
                    ?? false;
            }
            catch (Exception e)
                when (e is ICSharpCode.SharpZipLib.Zip.ZipException || e is IOException)
            {
                invalidRepoDataErrorMsg = (
                    _tcManager.CurrentCollection as FolderTeamCollection
                )?.GetCouldNotOpenCorruptZipMessage();
            }

            // If the request asked for the book by name, we don't have an actual Book object.
            // However, it happens that those requests don't need the checkinMessage.
            var checkinMessage = book == null ? "" : BookHistory.GetPendingCheckinMessage(book);
            return JsonConvert.SerializeObject(
                new
                {
                    // Keep this in sync with IBookTeamCollectionStatus defined in TeamCollectionApi.tsx
                    who = whoHasBookLocked,
                    whoFirstName = _tcManager.CurrentCollectionEvenIfDisconnected?.WhoHasBookLockedFirstName(
                        bookFolderName
                    ),
                    whoSurname = _tcManager.CurrentCollectionEvenIfDisconnected?.WhoHasBookLockedSurname(
                        bookFolderName
                    ),
                    when = whenLocked.ToLocalTime().ToShortDateString(),
                    where = _tcManager.CurrentCollectionEvenIfDisconnected?.WhatComputerHasBookLocked(
                        bookFolderName
                    ),
                    currentUser = CurrentUser,
                    currentUserName = TeamCollectionManager.CurrentUserFirstName,
                    currentMachine = TeamCollectionManager.CurrentMachine,
                    hasConflictingChange,
                    invalidRepoDataErrorMsg,
                    clickHereArg,
                    isChangedRemotely = _tcManager.CurrentCollection?.HasBeenChangedRemotely(
                        bookFolderName
                    ),
                    isDisconnected = _tcManager.CurrentCollectionEvenIfDisconnected?.IsDisconnected,
                    isNewLocalBook,
                    checkinMessage,
                    isUserAdmin = _tcManager.OkToEditCollectionSettings
                }
            );
        }

        public void HandleSelectedBookStatus(ApiRequest request)
        {
            try
            {
                if (!TeamCollectionManager.IsRegistrationSufficient())
                {
                    request.Failed("not registered");
                    return;
                }
                request.ReplyWithJson(
                    GetBookStatusJson(BookFolderName, _bookSelection.CurrentSelection)
                );
            }
            catch (Exception e)
            {
                // Not sure what to do here: getting the current book status crashed.
                Logger.WriteError("TeamCollectionApi.HandleSelectedBookStatus() crashed", e);
                SentrySdk.AddBreadcrumb(
                    string.Format("Something went wrong for {0}", request.LocalPath())
                );
                SentrySdk.CaptureException(e);
                request.Failed("getting the current book status failed");
            }
        }

        public void HandleGetHistory(ApiRequest request)
        {
            var currentBookOnly = request.GetParamOrNull("currentBookOnly");
            IEnumerable<BookHistoryEvent> events;
            if (currentBookOnly == "true")
            {
                events = CollectionHistory.GetBookEvents(request.CurrentBook.BookInfo);
            }
            else
            {
                events = CollectionHistory.GetAllEvents(
                    _currentBookCollectionSelection.CurrentSelection
                );
            }
            var result = events.OrderByDescending(b => b.When).ToArray();
            request.ReplyWithJson(JsonConvert.SerializeObject(result));
        }

        private string BookFolderName =>
            Path.GetFileName(_bookSelection.CurrentSelection?.FolderPath);

        public void HandleAttemptLockOfCurrentBook(ApiRequest request)
        {
            if (!_tcManager.CheckConnection())
            {
                request.Failed();
                return;
            }

            try
            {
                // Could be a problem if there's no current book or it's not in the collection folder.
                // But in that case, we don't show the UI that leads to this being called.
                var success = _tcManager.CurrentCollection.AttemptLock(BookFolderName);
                if (success)
                {
                    UpdateUiForBook();

                    Analytics.Track(
                        "TeamCollectionCheckoutBook",
                        new Dictionary<string, string>()
                        {
                            { "CollectionId", _settings?.CollectionId },
                            { "CollectionName", _settings?.CollectionName },
                            { "Backend", _tcManager?.CurrentCollection?.GetBackendType() },
                            { "User", CurrentUser },
                            { "BookId", _bookSelection?.CurrentSelection?.ID },
                            { "BookName", _bookSelection?.CurrentSelection?.Title }
                        }
                    );

                    BookHistory.AddEvent(
                        _bookSelection.CurrentSelection,
                        BookHistoryEventType.CheckOut
                    );
                }

                request.ReplyWithBoolean(success);
            }
            catch (Exception e)
            {
                var msg = MakeLockFailedMessageFromException(e, BookFolderName);
                // Pushing an error into the log will show the Reload Collection button. It's not obvious this
                // is useful here, since we don't know exactly what went wrong. However, it at least gives the user
                // the option to try it.
                var log = _tcManager?.CurrentCollection?.MessageLog;
                if (log != null)
                    log.WriteMessage(msg);
                Logger.WriteError(msg.TextForDisplay, e);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );
                request.Failed("lock failed");
            }
        }

        // internal, and taking bookFolder (which is always this.BookFolderName in production) for ease of testing.
        internal TeamCollectionMessage MakeLockFailedMessageFromException(
            Exception e,
            string bookFolder
        )
        {
            var msgId = "TeamCollection.CheckoutError";
            var msgEnglish = "Bloom was not able to check out \"{0}\".";
            var syncAgent = ""; // becomes SyncAgent for longer versions of message that need it
            if (e is FolderTeamCollection.CannotLockException cannotLockException)
            {
                var msgTryAgain = LocalizationManager.GetString(
                    "Common.TryAgainOrRestart",
                    "Please try again later. If the problem continues, restart your computer."
                );
                msgId = null; // this branch uses a 3-part message which can't be relocalized later.
                string part2;
                if (cannotLockException.SyncAgent != "Unknown")
                {
                    part2 = string.Format(
                        LocalizationManager.GetString(
                            "TeamCollection.AgentSynchronizing",
                            "Some other program may be busy with it. This may just be {0} synchronizing the file."
                        ),
                        cannotLockException.SyncAgent
                    );
                }
                else
                {
                    part2 = LocalizationManager.GetString(
                        "TeamCollection.SomethingSynchronizing",
                        "Some other program may be busy with it. This may just be something synchronizing the file."
                    );
                }
                msgEnglish += " " + part2 + " " + msgTryAgain;
            }

            var msg = new TeamCollectionMessage(
                MessageAndMilestoneType.Error,
                msgId,
                msgEnglish,
                Path.GetFileName(bookFolder),
                syncAgent
            );
            return msg;
        }

        public void HandleForgetChangesInSelectedBook(ApiRequest request)
        {
            try
            {
                if (!_tcManager.CheckConnection())
                {
                    request.Failed();
                    return;
                }

                // Enhance: do we need progress here?
                var bookName = Path.GetFileName(_bookSelection.CurrentSelection.FolderPath);
                // Todo before 5.1: forgetting changes might involve undoing a rename.
                // If so, ForgetChanges will return a list of folders affected (up to 3).
                // We need to notify the new collection tab to update its book list
                // and also possibly update the current selection, and in case we undid
                // things in the book, we should update the preview.
                var modifiedBookFolders = _tcManager.CurrentCollection.ForgetChangesCheckin(
                    bookName
                );
                string updatedBookFolder = null;
                var finalBookName = bookName;
                if (modifiedBookFolders.Count > 0)
                {
                    updatedBookFolder = modifiedBookFolders[0];
                    finalBookName = Path.GetFileName(updatedBookFolder);
                }

                if (finalBookName != bookName)
                {
                    _bookSelection.CurrentSelection.Storage.RestoreBookName(finalBookName);
                }

                // We've restored an old meta.json, things might be different...book titles for one.
                // This needs to come AFTER RestoreBookName, which fixes the book's FolderPath
                // so it knows where to load the restored meta.json from. But BEFORE
                // UpdateLabelOfBookInEditableCollection, which wants to use the restored BookInfo
                // to get a name (and fix the one in the Model).
                _bookSelection.CurrentSelection.UpdateBookInfoFromDisk();
                // We need to do this as early as possible so that as notifications start to
                // go to the UI and it starts to request things from our server the answers are
                // up to date.
                _bookSelection.CurrentSelection.ReloadFromDisk(updatedBookFolder);

                if (finalBookName != bookName)
                {
                    _collectionModel.UpdateLabelOfBookInEditableCollection(
                        _bookSelection.CurrentSelection
                    );
                }
                BookHistory.SetPendingCheckinMessage(_bookSelection.CurrentSelection, "");
                UpdateUiForBook(reloadFromDisk: false, renamedTo: updatedBookFolder);
                // We need to do this after updating the rest of the UI, so the button we're
                // looking for has been adjusted.
                _tcManager.CurrentCollection.UpdateBookStatus(finalBookName, true);
                request.PostSucceeded();
            }
            catch (Exception ex)
            {
                var msgId = "TeamCollection.ErrorForgettingChanges";
                var msgEnglish = "Error forgetting changes for {0}: {1}";
                var log = _tcManager?.CurrentCollection?.MessageLog;
                // Pushing an error into the log will show the Reload Collection button. It's not obvious this
                // is useful here, since we don't know exactly what went wrong. However, it at least gives the user
                // the option to try it.
                if (log != null)
                    log.WriteMessage(
                        MessageAndMilestoneType.Error,
                        msgId,
                        msgEnglish,
                        _bookSelection?.CurrentSelection?.FolderPath,
                        ex.Message
                    );
                Logger.WriteError(
                    String.Format(
                        msgEnglish,
                        _bookSelection?.CurrentSelection?.FolderPath,
                        ex.Message
                    ),
                    ex
                );
                request.Failed("forget changes failed");
            }
        }

        public void HandleCheckInCurrentBook(ApiRequest request)
        {
            Action<float> reportCheckinProgress = (fraction) =>
            {
                dynamic messageBundle = new DynamicJson();
                messageBundle.fraction = fraction;
                _socketServer.SendBundle("checkinProgress", "progress", messageBundle);
                // The status panel is supposed to be showing a progress bar in response to getting the bundle,
                // but since we're doing the checkin on the UI thread, it doesn't get painted without this.
                Application.DoEvents();
            };
            // Right before calling this API, the status panel makes a change that
            // should make the progress bar visible. But this method is running on
            // the UI thread so without this call it won't appear until later, when
            // we have Application.DoEvents() as part of reporting progress. We do
            // quite a bit on large books before the first file is written to the
            // zip, so one more DoEvents() here lets the bar appear at once.
            Application.DoEvents();
            _bookSelection.CurrentSelection.Save();

            if (!_tcManager.CheckConnection())
            {
                request.Failed();
                return;
            }
            CheckInOneBook(
                _bookSelection.CurrentSelection.BookInfo,
                request,
                reportCheckinProgress
            );
            request.PostSucceeded();
            // Force a full reload of the book from disk and update the UI to match.
            _bookSelection.SelectBook(_bookSelection.CurrentSelection);
        }

        public void HandleCheckInAllBooks(ApiRequest request)
        {
            // this could take a long time, so...
            request.PostSucceeded();

            var progress = new WebSocketProgress(_socketServer, "teamCollection-status");
            progress.LogAllMessages = true;

            Action<float> reportProgressFraction = (fraction) => {
                //progress.MessageWithoutLocalizing(fraction.ToString() + " ");
            };
            // might not be even saveable. Do we need this for anything? _bookSelection.CurrentSelection?.Save();
            var books = _collectionModel.GetBookCollections()[0].GetBookInfos();
            // limit to books which are checked out

            books = books.Where(b =>
            {
                var status = _tcManager.CurrentCollection.GetStatus(b.FolderName);
                return _tcManager.CurrentCollection.IsCheckedOutHereBy(status);
            });
            foreach (var book in books)
            {
                progress.MessageWithoutLocalizing($"Starting check-in of '{book.Title}'...");
                CheckInOneBook(book, request, reportProgressFraction, progress);
            }
            progress.MessageWithoutLocalizing($"Done checking in books.");
        }

        private void CheckInOneBook(
            BookInfo bookInfo,
            ApiRequest request,
            Action<float> reportProgressFraction,
            IWebSocketProgress progress = null
        )
        {
            try
            {
                var bookName = Path.GetFileName(bookInfo.FolderPath);
                if (_tcManager.CurrentCollection.OkToCheckIn(bookName))
                {
                    // review: not super happy about this being here in the api. Was stymied by
                    // PutBook not knowing about the actual book object, but maybe that could be passed in.
                    // It's important that this is done BEFORE the checkin: we want other users to see the
                    // comment, and NOT see the pending comment as if it was their own if they check out.

                    var book = _collectionModel.GetBookFromBookInfo(bookInfo);
                    var message = BookHistory.GetPendingCheckinMessage(book);
                    BookHistory.AddEvent(book, BookHistoryEventType.CheckIn, message);
                    BookHistory.SetPendingCheckinMessage(book, "");
                    _tcManager.CurrentCollection.PutBook(
                        bookInfo.FolderPath,
                        true,
                        false,
                        reportProgressFraction
                    );
                    progress?.MessageWithoutLocalizing($"Completed check-in of '{bookInfo.Title}'");

                    reportProgressFraction(0); // hides the progress bar (important if a different book has been selected that is still checked out)

                    Analytics.Track(
                        "TeamCollectionCheckinBook",
                        new Dictionary<string, string>()
                        {
                            { "CollectionId", _settings?.CollectionId },
                            { "CollectionName", _settings?.CollectionName },
                            { "Backend", _tcManager?.CurrentCollection?.GetBackendType() },
                            { "User", CurrentUser },
                            { "BookId", bookInfo.Id },
                            { "BookName", bookInfo.Title }
                        }
                    );
                }
                else
                {
                    // We can't check in! The system has broken down...perhaps conflicting checkouts while offline.
                    // Save our version in Lost-and-Found
                    _tcManager.CurrentCollection.PutBook(
                        bookInfo.FolderPath,
                        false,
                        true,
                        reportProgressFraction
                    );
                    reportProgressFraction(0); // cleans up panel for next time
                    // overwrite it with the current repo version.
                    _tcManager.CurrentCollection.CopyBookFromRepoToLocal(
                        bookName,
                        dialogOnError: true
                    );
                    var msgFmt = LocalizationManager.GetString(
                        "TeamCollection.ConflictingEditOrCheckout",
                        "Someone else has edited the book {0} or checked it out even though it was being editing here! Local changes have been saved to Lost and Found"
                    );
                    var msg = string.Format(
                        msgFmt,
                        Path.GetFileNameWithoutExtension(bookInfo.FolderPath)
                    );

                    progress.MessageWithoutLocalizing($"{msgFmt}", ProgressKind.Error);

                    Analytics.Track(
                        "TeamCollectionConflictingEditOrCheckout",
                        new Dictionary<string, string>()
                        {
                            { "CollectionId", _settings?.CollectionId },
                            { "CollectionName", _settings?.CollectionName },
                            { "Backend", _tcManager?.CurrentCollection?.GetBackendType() },
                            { "User", CurrentUser },
                            { "BookId", bookInfo.Id },
                            { "BookName", bookInfo.Title }
                        }
                    );
                    BookHistory.AddEvent(bookInfo, BookHistoryEventType.SyncProblem, msg);
                    BloomMessageBox.ShowInfo(msg);
                }

                UpdateUiForBook();

                Application.Idle += OnIdleConnectionCheck;
            }
            catch (Exception e)
            {
                reportProgressFraction(0); // cleans up panel progress indicator
                var msgId = "TeamCollection.ErrorCheckingBookIn";
                var msgEnglish = "Error checking in {0}: {1}";
                var log = _tcManager?.CurrentCollection?.MessageLog;
                // Pushing an error into the log will show the Reload Collection button. It's not obvious this
                // is useful here, since we don't know exactly what went wrong. However, it at least gives the user
                // the option to try it.
                if (log != null)
                    log.WriteMessage(
                        MessageAndMilestoneType.Error,
                        msgId,
                        msgEnglish,
                        _bookSelection?.CurrentSelection?.FolderPath,
                        e.Message
                    );
                Logger.WriteError(
                    String.Format(
                        msgEnglish,
                        _bookSelection?.CurrentSelection?.FolderPath,
                        e.Message
                    ),
                    e
                );
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()} ({_bookSelection?.CurrentSelection?.FolderPath})"
                );
                request.Failed("checkin failed");
            }
        }

        private void OnIdleConnectionCheck(object sender, EventArgs e)
        {
            Application.Idle -= OnIdleConnectionCheck;

            // BL-10704: In case the Internet went away while we were trying to CheckIn a book...
            // This will at least signal to the user in the Dropbox case, that while his checkin
            // may have succeeded, his colleagues won't know about it until the Internet is up again.
            // If we don't do it "OnIdle", the book status pane doesn't reflect that we actually did
            // (probably, assuming we are on Dropbox, anyway) complete the checkin.
            _tcManager.CheckConnection();
        }

        // Tell the CollectionSettingsDialog that we should reopen the collection now
        private Action _callbackToReopenCollection;

        public void SetCallbackToReopenCollection(Action callback)
        {
            _callbackToReopenCollection = callback;
        }

        public void HandleChooseFolderLocation(ApiRequest request)
        {
            try
            {
                // One of the few places that knows we're using a particular implementation
                // of TeamRepo. But we have to know that to create it. And of course the user
                // has to chose a folder to get things started.
                // We'll need a different API or something similar if we ever want to create
                // some other kind of repo.
                var dropboxFolder = DropboxUtils.GetDropboxFolderPath();
                var description = LocalizationManager.GetString(
                    "TeamCollection.SelectFolder",
                    "Select or create the folder where this collection will be shared"
                );
                var sharedFolder = BloomFolderChooser.ChooseFolder(dropboxFolder, description);
                if (string.IsNullOrEmpty(sharedFolder))
                {
                    request.Failed();
                    return;
                }
                // We send the result through a websocket rather than simply returning it because
                // if the user is very slow (one site said FF times out after 90s) the browser may
                // abandon the request before it completes. The POST result is ignored and the
                // browser simply listens to the socket.
                // We'd prefer this request to return immediately and set a callback to run
                // when the dialog closes and handle the results, but FolderBrowserDialog
                // does not offer such an API. Instead, we just ignore any timeout
                // in our Javascript code.
                dynamic messageBundle = new DynamicJson();
                messageBundle.repoFolderPath = sharedFolder;
                messageBundle.problem = ProblemsWithLocation(sharedFolder);
                // This clientContext must match what is being listened for in CreateTeamCollection.tsx
                _socketServer.SendBundle(
                    "teamCollectionCreate",
                    "shared-folder-path",
                    messageBundle
                );

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                // Not sure what to do here: choosing the collection folder should never crash.
                Logger.WriteError("TeamCollectionApi.HandleChooseFolderLocation() crashed", e);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );
                request.Failed("choose folder location failed");
            }
        }

        internal string ProblemsWithLocation(string sharedFolder)
        {
            // For now we use this generic message, because it's too hard to come up with concise
            // understandable messages explaining why these locations are a problem.
            var defaultMessage = LocalizationManager.GetString(
                "TeamCollection.ProblemLocation",
                "There is a problem with this location"
            );
            try
            {
                if (Directory.EnumerateFiles(sharedFolder, "*.JoinBloomTC").Any())
                {
                    return defaultMessage;
                    //return LocalizationManager.GetString("TeamCollection.AlreadyTC",
                    //	"This folder appears to already be in use as a Team Collection");
                }

                if (Directory.EnumerateFiles(sharedFolder, "*.bloomCollection").Any())
                {
                    return defaultMessage;
                    //return LocalizationManager.GetString("TeamCollection.LocalCollection",
                    //	"This appears to be a local Bloom collection. The Team Collection must be created in a distinct place.");
                }

                if (Directory.Exists(_tcManager.PlannedRepoFolderPath(sharedFolder)))
                {
                    return defaultMessage;
                    //return LocalizationManager.GetString("TeamCollection.TCExists",
                    //	"There is already a Folder in that location with the same name as this collection");
                }

                // We're not in a big hurry here, and the most decisive test that we can actually put things in this
                // folder is to do it.
                var testFolder = Path.Combine(sharedFolder, "test");
                Directory.CreateDirectory(testFolder);
                RobustFile.WriteAllText(Path.Combine(testFolder, "test"), "This is a test");
                SIL.IO.RobustIO.DeleteDirectoryAndContents(testFolder);
            }
            catch (Exception ex)
            {
                // This might also catch errors such as not having permission to enumerate things
                // in the directory.
                return LocalizationManager.GetString(
                    "TeamCollection.NoWriteAccess",
                    "Bloom does not have permission to write to the selected folder. The system reported "
                        + ex.Message
                );
            }

            return "";
        }

        public void HandleCreateTeamCollection(ApiRequest request)
        {
            string repoFolderParentPath = null;
            try
            {
                if (!TeamCollection.PromptForSufficientRegistrationIfNeeded())
                {
                    request.PostSucceeded();
                    return;
                }

                repoFolderParentPath = request.RequiredPostString();

                _tcManager.ConnectToTeamCollection(repoFolderParentPath, _settings.CollectionId);
                _callbackToReopenCollection?.Invoke();

                Analytics.Track(
                    "TeamCollectionCreate",
                    new Dictionary<string, string>()
                    {
                        { "CollectionId", _settings?.CollectionId },
                        { "CollectionName", _settings?.CollectionName },
                        { "Backend", _tcManager?.CurrentCollection?.GetBackendType() },
                        { "User", CurrentUser }
                    }
                );

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                var msgEnglish = "Error creating Team Collection {0}: {1}";
                var msgFmt = LocalizationManager.GetString(
                    "TeamCollection.ErrorCreating",
                    msgEnglish
                );
                ErrorReport.NotifyUserOfProblem(e, msgFmt, repoFolderParentPath, e.Message);
                Logger.WriteError(String.Format(msgEnglish, repoFolderParentPath, e.Message), e);
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Something went wrong for {request.LocalPath()}"
                );

                // Since we have already informed the user above, it is better to just report a success here.
                // Otherwise, they will also get a toast.
                request.PostSucceeded();
            }
        }

        // Called when we cause the book's status to change, so things outside the HTML world, like visibility of the
        // "Edit this book" button, can change appropriately. Pretending the user chose a different book seems to
        // do all the necessary stuff for now.
        private void UpdateUiForBook(bool reloadFromDisk = false, string renamedTo = null)
        {
            // Todo: This is not how we want to do this. Probably the UI should listen for changes to the status of books,
            // whether selected or not, talking to the repo directly.
            if (Form.ActiveForm == null)
            {
                // On Linux (at least for Bionic), Form.ActiveForm can sometimes be null when
                // this executes.  The following loop seems to be as simple a fix as possible.
                foreach (var form in Application.OpenForms)
                {
                    if (form is Shell shell)
                    {
                        shell.Invoke((Action)(() => _bookSelection.InvokeSelectionChanged(false)));
                        return;
                    }
                }
            }
            Form.ActiveForm.Invoke(
                (Action)(
                    () =>
                    {
                        if (reloadFromDisk)
                            _bookSelection.CurrentSelection.ReloadFromDisk(renamedTo);
                        _bookSelection.InvokeSelectionChanged(false);
                    }
                )
            );
        }
    }
}
