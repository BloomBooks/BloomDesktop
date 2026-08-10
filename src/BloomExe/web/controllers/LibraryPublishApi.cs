using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Publish;
using Bloom.Publish.BloomLibrary;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using L10NSharp;
using SIL.Progress;

namespace Bloom.web.controllers
{
    /// <summary>
    /// APIs related to the Library (Web) Publish screen.
    /// </summary>
    class LibraryPublishApi
    {
        public static BloomLibraryPublishModel Model { get; set; }

        // This goes out with our messages and, on the client side (typescript), messages are filtered
        // down to the context (usualy a screen) that requested them.
        private const string kWebSocketContext = "libraryPublish"; // must match what is in LibraryPublishScreen.tsx

        private const string kWebSocketEventId_uploadSuccessful = "uploadSuccessful"; // must match what is in LibraryPublishSteps.tsx
        private const string kWebSocketEventId_uploadCanceled = "uploadCanceled"; // must match what is in LibraryPublishSteps.tsx

        private PublishView _publishView;
        private PublishModel _publishModel;
        private IBloomWebSocketServer _webSocketServer;
        private WebSocketProgress _webSocketProgress;
        private IProgress _progress;

        private string _existingBookObjectIdOrNull;

        public LibraryPublishApi(
            BloomWebSocketServer webSocketServer,
            PublishView publishView,
            PublishModel publishModel
        )
        {
            _publishView = publishView;
            _publishModel = publishModel;
            Debug.Assert(publishModel == publishView._model);

            _webSocketServer = webSocketServer;
            var progress = new WebSocketProgress(_webSocketServer, kWebSocketContext);
            _webSocketProgress = progress.WithL10NPrefix("PublishTab.Upload.");
            _webSocketProgress.LogAllMessages = true;
            _progress = new WebProgressAdapter(_webSocketProgress);
        }

        private string CurrentSignLanguageName
        {
            get { return Model.Book.CollectionSettings.SignLanguage.Name; }
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Review: Why is requiresSync false, and is it safe?
            // We seem to get into trouble releasing the lock when using an async method,
            // apparently because the continuation of the method, after the stack unwinds when
            // doing the await, runs on a different thread, which does not own the lock it is
            // trying to release. It appears, in fact, that you can't reliably claim a lock in an
            // async method and release it after awaiting something. I find it hard to believe
            // that there isn't a way around that, but there doesn't seem to be.
            // There's also the consideration that we are now loading the document into a browser
            // in order to evaluate what fonts are really used, and it may be that doing so will
            // trigger calls to API methods. So we're safer from deadlocks and releasing the lock
            // on the wrong thread if we just don't lock.
            // So, could there be any data that these handlers manipulate that needs locking
            // when other API calls are running? I can't think of any, but don't know how to
            // prove that there is not.
            apiHandler.RegisterAsyncEndpointHandler(
                "libraryPublish/upload",
                HandleUpload,
                true,
                false
            );
            apiHandler.RegisterAsyncEndpointHandler(
                "libraryPublish/uploadWithNewUploader",
                HandleUploadWithNewUploader,
                true,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/uploadCollection",
                HandleUploadCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/uploadFolderOfCollections",
                HandleUploadFolderOfCollections,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/getBookInfo",
                HandleGetBookInfo,
                true
            );
            apiHandler.RegisterEndpointHandler("libraryPublish/setSummary", HandleSetSummary, true);
            apiHandler.RegisterEndpointHandler("libraryPublish/useSandbox", HandleUseSandbox, true);
            // Deliberately handled off the UI thread and without the api sync lock. All this
            // handler does is set a flag, but its whole purpose is to interrupt an upload which
            // may be monopolizing both the UI thread (offscreen-browser thumbnailing) and the
            // lock for minutes at a time. If the cancel had to queue behind the very work it is
            // trying to stop, the flag could get set only after the upload had passed its last
            // cancellation check: the upload would run to completion and the user would be left
            // with a Cancel that did nothing (BL-16340). Compare progress/cancel and
            // signLanguage/cancelImportVideo, which are registered this way for the same reason.
            apiHandler.RegisterEndpointHandler("libraryPublish/cancel", HandleCancel, false, false);
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/getUploadCollisionInfo",
                HandleGetUploadCollisionInfo,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/checkSubscriptionMatch",
                HandleCheckSubscriptionMatch,
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                "libraryPublish/uploadAfterChangingBookId",
                HandleUploadAfterChangingBookId,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/agreementsAccepted",
                HandleAgreementsAccepted,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/goToEditBookTitle",
                HandleGoToEditBookTitle,
                true
            );
            apiHandler.RegisterEndpointHandler("libraryPublish/topic", HandleTopic, true);
        }

        private static bool ModelIndicatesSignLanguageChecked =>
            Model.Book.HasSignLanguageVideos() && Model.IsPublishSignLanguage();

        private void HandleGetBookInfo(ApiRequest request)
        {
            Model.EnsureUpToDateLicense();
            dynamic bookInfo = new
            {
                title = Model.Title,
                summary = Model.Summary,
                copyright = Model.Copyright,
                licenseType = Model.LicenseType.ToString(),
                licenseToken = Model.LicenseToken,
                licenseRights = Model.LicenseRights,
                isTemplate = Model.IsTemplate,
                isTitleOKToPublish = Model.IsTitleOKToPublish,
            };
            request.ReplyWithJson(bookInfo);
        }

        private void HandleSetSummary(ApiRequest request)
        {
            Model.Summary = request.GetPostStringOrNull();
            request.PostSucceeded();
        }

        private void HandleUseSandbox(ApiRequest request)
        {
            request.ReplyWithBoolean(BookUpload.UseSandbox);
        }

        /// <summary>
        /// Ask the upload to stop. All we do is set the flag that the upload polls between its
        /// stages; it can take a while to notice, since we don't interrupt a stage (notably PDF
        /// creation) that is already under way.
        /// </summary>
        private void HandleCancel(ApiRequest request)
        {
            // If the user clicked Cancel before the upload itself got under way, no upload is
            // running to notice the flag and report on it. That window is real and not small:
            // the client shows Cancel from the moment the user commits, but two API round trips
            // (the subscription check and the "existing copy on server" query) happen before
            // libraryPublish/upload even arrives. Nothing else would ever tell the client the
            // cancel took effect -- and the client only ever leaves its Cancel state on that
            // report -- so make the report here. HandleUpload takes the same lock and declines
            // to start. (BL-16340)
            bool nobodyElseWillReportIt;
            lock (_uploadStateLock)
            {
                _progress.CancelRequested = true;
                nobodyElseWillReportIt = !_uploadInProgress;
            }

            // Outside the lock: this sends a websocket message, and the lock exists only to make
            // the flag pair above indivisible.
            if (nobodyElseWillReportIt)
                ReportUploadCanceled();

            request.PostSucceeded();
        }

        /// <summary>
        /// Tell the user, and the publish screen, that the upload was cancelled. Sending the
        /// event matters as much as showing the message: the screen greys out UPLOAD BOOK as
        /// soon as the user clicks Cancel, and this event is the only thing that brings it back.
        /// Pass false for withMessage when a message has already been given, to avoid a second
        /// one -- the event still has to go out.
        /// </summary>
        private void ReportUploadCanceled(bool withMessage = true)
        {
            if (withMessage)
                _webSocketProgress.Message("Cancelled", "Upload was cancelled", ProgressKind.Error);
            _webSocketServer.SendEvent(kWebSocketContext, kWebSocketEventId_uploadCanceled);
        }

        private async Task HandleUpload(ApiRequest request)
        {
            await HandleUpload(request, false);
        }

        private async Task HandleUploadWithNewUploader(ApiRequest request)
        {
            await HandleUpload(request, true);
        }

        private bool _changeUploader = false;

        // True from the moment we take charge of an upload until we have reported its outcome.
        // HandleCancel uses it to work out whether anyone else is going to report the cancel.
        private bool _uploadInProgress;

        // Makes "is a cancel already pending?" and "an upload is now running" one indivisible
        // step, shared with HandleCancel. Without it the two could interleave such that
        // HandleCancel reported the cancel AND the upload started anyway, so the cancel got
        // reported twice. Only ever held across these two field reads/writes -- never across
        // the upload, or a websocket send.
        private readonly object _uploadStateLock = new object();

        private async Task HandleUpload(ApiRequest request, bool changeUploader)
        {
            if (request.HttpMethod == HttpMethods.Get)
                return;
            _changeUploader = changeUploader;

            // Note that we deliberately do NOT clear CancelRequested here. The user can click
            // Cancel before this request arrives (see HandleCancel), and clearing it here would
            // throw that cancel away and upload the book anyway (BL-16340). It is cleared in
            // HandleCheckSubscriptionMatch, which the client calls at the start of every attempt.
            bool alreadyCancelled;
            lock (_uploadStateLock)
            {
                alreadyCancelled = _progress.CancelRequested;
                if (!alreadyCancelled)
                    _uploadInProgress = true;
            }
            if (alreadyCancelled)
            {
                // A cancel arrived during the pre-upload handshake. HandleCancel has already
                // told the user, so all that is left to do is not upload.
                request.PostSucceeded();
                return;
            }
            try
            {
                await UploadBookAsync();
            }
            catch (Exception)
            {
                ReportTryAgainDuringUpload();
            }
            finally
            {
                lock (_uploadStateLock)
                    _uploadInProgress = false;
            }
            request.PostSucceeded();
        }

        private async Task UploadBookAsync()
        {
            _webSocketProgress.Message("Common.Starting", "Starting...");
            SetParentControlsState(false); // Disable UI

            string uploadResult = null;
            Exception caughtException = null;

            try
            {
                uploadResult = await Task.Run(async () =>
                {
                    var checkerResult = Model.CheckBookBeforeUpload();
                    if (checkerResult != null)
                    {
                        _webSocketProgress.MessageWithoutLocalizing(
                            checkerResult,
                            ProgressKind.Error
                        );
                        return "quiet"; // suppress other completion/fail messages
                    }

                    Model.UpdateBookMetadataFeatures(
                        Model.Book.BookInfo.PublishSettings.BloomLibrary.AudioLangs.Any(),
                        ModelIndicatesSignLanguageChecked
                    );

                    // We currently have no way to turn this off. This is by design, we don't think it is a needed complication.
                    var includeBackgroundMusic = true;

                    var bookObjectId = await Model.UploadOneBook(
                        Model.Book,
                        _progress,
                        _publishModel,
                        !includeBackgroundMusic,
                        _existingBookObjectIdOrNull,
                        _changeUploader
                    );

                    return bookObjectId;
                });
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
            finally
            {
                SetParentControlsState(true); // Re-enable UI
            }

            // A cancel that arrived too late to stop anything must not be reported as though it
            // had worked: if we got a book id back, the book really is on BloomLibrary now, and
            // telling the user it was cancelled is a falsehood they might act on. uploadResult is
            // empty when the upload was abandoned (including by the cancellation checks inside
            // BookUpload) or failed, and "quiet" when a message has already been given.
            var uploadActuallyCompleted =
                !string.IsNullOrEmpty(uploadResult) && uploadResult != "quiet";
            if (_progress.CancelRequested && !uploadActuallyCompleted)
            {
                // Send the event on EVERY cancelled ending, including the "quiet" one. The
                // screen's Cancel state is cleared only by this event, and making it depend
                // instead on some other path happening to emit an error line is how it gets
                // left permanently greyed out -- the whole of BL-16340. For "quiet" a message
                // has already been given, so send the event without adding a second one.
                ReportUploadCanceled(withMessage: uploadResult != "quiet");
                return;
            }

            if (caughtException != null)
            {
                ReportBasicErrorDuringUpload();
                _webSocketProgress.Exception(caughtException);
                return;
            }

            if (uploadResult == "quiet")
            {
                // no more reporting, sufficient message already given.
            }
            else if (string.IsNullOrEmpty(uploadResult))
            {
                // Something went wrong, possibly already reported.
                ReportTryAgainDuringUpload();
            }
            else
            {
                var url = BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(
                    bookId: uploadResult,
                    true
                );
                Model.AddHistoryRecordForLibraryUpload(url);
                dynamic result = new DynamicJson();
                result.bookId = Model.Book.BookInfo.Id;
                result.url = url;
                _webSocketServer.SendBundle(
                    kWebSocketContext,
                    kWebSocketEventId_uploadSuccessful,
                    result
                );
            }
        }

        private void ReportBasicErrorDuringUpload()
        {
            _webSocketProgress.MessageUsingTitle(
                "ErrorUploading",
                "Sorry, there was a problem uploading {0}. Some details follow. You may need technical help.",
                Model.Title,
                ProgressKind.Error
            );
        }

        private void ReportPdfGenerationFailed()
        {
            ReportBasicErrorDuringUpload();
            _webSocketProgress.Message(
                "BadPdfShort",
                "Bloom had a problem making a PDF of this book.",
                ProgressKind.Error
            );
        }

        private void ReportTryAgainDuringUpload()
        {
            _webSocketProgress.MessageUsingTitle(
                "FinalUploadFailureNotice",
                "Sorry, \"{0}\" was not successfully uploaded. Sometimes this is caused by temporary problems with the servers we use. It's worth trying again in an hour or two. If you regularly get this problem please report it to us.",
                Model.Title,
                ProgressKind.Error
            );
        }

        private void SetParentControlsState(bool enable)
        {
            GetWorkspaceView()?.SetTabsEnabled(enable);
        }

        private WorkspaceView GetWorkspaceView()
        {
            return _publishView.WorkspaceView;
        }

        private void HandleUploadCollection(ApiRequest request)
        {
            if (!ValidateBookshelfBeforeBulkUpload())
            {
                request.PostSucceeded();
                return;
            }

            // Bulk upload shares _progress with single-book upload but has no Cancel of its own,
            // so a cancellation left over from an earlier single-book upload would silently stop
            // it before it started.
            _progress.CancelRequested = false;

            Model.BulkUpload(Model.Book.CollectionSettings.FolderPath, _progress);
            request.PostSucceeded();
        }

        private void HandleUploadFolderOfCollections(ApiRequest request)
        {
            if (!ValidateBookshelfBeforeBulkUpload())
            {
                request.PostSucceeded();
                return;
            }

            // See the note in HandleUploadCollection about why this is cleared here.
            _progress.CancelRequested = false;

            var folderPath = request.RequiredPostString();
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                Model.BulkUpload(folderPath, _progress);

            request.PostSucceeded();
        }

        private bool ValidateBookshelfBeforeBulkUpload()
        {
            // for now, we're limiting this to projects that have set up a default bookshelf
            // so that all their books go to the correct place.
            if (string.IsNullOrEmpty(Model.Book.CollectionSettings.DefaultBookshelf))
            {
                // Intentionally not localized ( because it's complicated, rare, and generally advanced )
                _webSocketProgress.MessageWithoutLocalizing(
                    "Before sending all of your books to BloomLibrary.org, you probably want to tell Bloom which bookshelf this collection belongs in. Please go to Collection Tab : Settings : Book Making and set the \"Bloom Library Bookshelf\".",
                    ProgressKind.Error
                );

                return false;
            }
            return true;
        }

        private void HandleGetUploadCollisionInfo(ApiRequest request)
        {
            _webSocketProgress.Message(
                "CheckingExistingCopy",
                "Checking for existing copy on server..."
            );

            _existingBookObjectIdOrNull = null;

            var index = Int32.Parse(request.RequiredParam("index"));

            dynamic collisionDialogInfo;
            try
            {
                collisionDialogInfo = Model.GetUploadCollisionDialogProps(
                    Model.TextLanguagesToAdvertiseOnBloomLibrary,
                    ModelIndicatesSignLanguageChecked,
                    index
                );
            }
            catch
            {
                // This should be pretty rare. We can't get this far unless we already verified the user is logged in.
                _webSocketProgress.MessageWithoutLocalizing(
                    "Unable to check for existing copy on server. Please try again in a minute or two.",
                    ProgressKind.Error
                );
                request.ReplyWithJson(CollisionDialogInfoForErrorCondition);
                return;
            }

            if (collisionDialogInfo.shouldShow)
                _existingBookObjectIdOrNull = collisionDialogInfo.existingBookObjectId.ToString();

            request.ReplyWithJson(collisionDialogInfo);
        }

        private void HandleCheckSubscriptionMatch(ApiRequest request)
        {
            // This is the first thing the client asks for once the user commits to an upload, so
            // as far as C# is concerned it is where a new attempt begins -- and therefore where
            // any cancellation left over from an earlier attempt is cleared. Deliberately not in
            // HandleUpload, which arrives too late to clear it safely; see the note there.
            _progress.CancelRequested = false;

            var subscriptionMatch = Model.CheckSubscriptionMatchBeforeUpload();
            if (subscriptionMatch != null)
            {
                _webSocketProgress.MessageWithoutLocalizing(subscriptionMatch, ProgressKind.Error);
                request.ReplyWithJson(new { error = true });
            }
            else
            {
                request.ReplyWithJson(new { error = false });
            }
        }

        private dynamic CollisionDialogInfoForErrorCondition =>
            new
            {
                error = true, // Inform the client there was an error. Don't continue with the upload.
                shouldShow = false, // Don't show the dialog. (Currently this is ignored if error is true; in that case, we never show the dialog.)
            };

        private async Task HandleUploadAfterChangingBookId(ApiRequest request)
        {
            if (!Model.ChangeBookInstanceId(_progress))
            {
                request.Failed("Can't fix ID because in TC");
                return;
            }

            // We're treating this upload as a new book; if we keep this around, it will
            // attempt an overwrite.
            _existingBookObjectIdOrNull = null;
            await HandleUpload(request);
        }

        private void HandleAgreementsAccepted(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Get)
                request.ReplyWithBoolean(Model.Book.UserPrefs.UploadAgreementsAccepted);
            else
            {
                Model.Book.UserPrefs.UploadAgreementsAccepted = request.RequiredPostBooleanAsJson();
                request.PostSucceeded();
            }
        }

        private void HandleGoToEditBookTitle(ApiRequest request)
        {
            // 0 is the index of the first page, the front cover.
            // A template book does not have a title on the front cover, but it
            // can be edited on the second (title) page.
            Model.Book.UserPrefs.MostRecentPage = Model.Book.IsTemplateBook ? 1 : 0;
            GetWorkspaceView()?.ChangeTab(WorkspaceTab.edit);
            request.PostSucceeded();
        }

        private void HandleTopic(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Get)
            {
                var currentTopicKey = Model
                    .Book.BookData.GetVariableOrNull("topic", "en")
                    .Unencoded;
                string result;
                if (string.IsNullOrEmpty(currentTopicKey))
                    result = "Missing";
                else
                {
                    result = LocalizationManager.GetDynamicString(
                        "Bloom",
                        "Topics." + currentTopicKey,
                        currentTopicKey
                    );
                }

                request.ReplyWithJson(result);
            }
            else if (request.HttpMethod == HttpMethods.Post)
            {
                var topicKey = request.RequiredPostString();
                // RequiredPostString cannot be empty, so we use a substitute value for empty.
                if (topicKey == "<NONE>")
                    topicKey = "";
                Model.Book.SetTopic(topicKey);
                Model.Book.Save();

                // Used by the Publish tab to refresh the UI when the data is saved.
                _webSocketServer.SendString("publish", "topicChanged", null);

                request.PostSucceeded();
            }
        }
    }
}
