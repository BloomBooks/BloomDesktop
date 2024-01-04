using Bloom.Api;
using Bloom.Publish;
using Bloom.Publish.BloomLibrary;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using SIL.Progress;
using SIL.Reporting;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
        private const string kWebSocketEventId_loginSuccessful = "loginSuccessful"; // must match what is in LibraryPublishSteps.tsx

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

            ExternalApi.LoginSuccessful += (sender, args) =>
            {
                Logger.WriteEvent("External login successful. Sending message to js-land.");
                _webSocketServer.SendString(
                    kWebSocketContext,
                    kWebSocketEventId_loginSuccessful,
                    Model?.WebUserId
                );
            };
        }

        private string CurrentSignLanguageName
        {
            get { return Model.Book.CollectionSettings.SignLanguage.Name; }
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("libraryPublish/upload", HandleUpload, true);
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
            apiHandler.RegisterEndpointHandler("libraryPublish/cancel", HandleCancel, true);
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/getUploadCollisionInfo",
                HandleGetUploadCollisionInfo,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/uploadAfterChangingBookId",
                HandleUploadAfterChangingBookId,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/checkForLoggedInUser",
                HandleCheckForLoggedInUser,
                true
            );
            apiHandler.RegisterEndpointHandler("libraryPublish/login", HandleLogin, true);
            apiHandler.RegisterEndpointHandler("libraryPublish/logout", HandleLogout, true);
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/agreementsAccepted",
                HandleAgreementsAccepted,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "libraryPublish/goToEditBookCover",
                HandleGoToEditBookCover,
                true
            );
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
                isTitleOKToPublish = Model.IsTitleOKToPublish
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

        private void HandleCancel(ApiRequest request)
        {
            _progress.CancelRequested = true;
            request.PostSucceeded();
        }

        private void HandleUpload(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Get)
                return;

            _progress.CancelRequested = false;

            try
            {
                _webSocketProgress.Message(
                    "CheckingVersionEligibility",
                    "Checking Bloom version eligibility..."
                );
                if (!Model.IsThisVersionAllowedToUpload)
                {
                    _webSocketProgress.Message(
                        "OldVersion",
                        "Sorry, this version of Bloom Desktop is not compatible with the current version of BloomLibrary.org. Please upgrade to a newer version.",
                        ProgressKind.Error
                    );
                    _webSocketProgress.Message("Cancelled", "Upload was cancelled");
                    request.PostSucceeded();
                    return;
                }

                UploadBook();
            }
            catch (Exception)
            {
                ReportTryAgainDuringUpload();
            }
            request.PostSucceeded();
        }

        private void UploadBook()
        {
            _webSocketProgress.Message("Common.Starting", "Starting...");

            var worker = new BackgroundWorker();
            worker.DoWork += BackgroundUpload;
            worker.RunWorkerCompleted += (_, completedEvent) =>
            {
                // Return all controls to normal state. (Do this first, just in case we get some further exception somehow.)
                // I believe the event is guaranteed to be raised, even if something in the worker thread throws,
                // so there should be no way to get stuck in the state where the tabs etc. are disabled.
                SetParentControlsState(true);

                if (_progress.CancelRequested)
                {
                    _webSocketProgress.Message(
                        "Cancelled",
                        "Upload was cancelled",
                        ProgressKind.Error
                    );
                    _webSocketServer.SendEvent(kWebSocketContext, kWebSocketEventId_uploadCanceled);
                    return;
                }

                if (completedEvent.Error != null)
                {
                    ReportBasicErrorDuringUpload();
                    _webSocketProgress.Exception(completedEvent.Error);
                    return;
                }

                // the book objectId if successful
                // or "quiet" to suppress more failure messages
                // or empty if otherwise failed
                var uploadResult = (string)completedEvent.Result;

                if (uploadResult == "quiet")
                {
                    // no more reporting, sufficient message already given.
                }
                else if (string.IsNullOrEmpty(uploadResult))
                {
                    // Something went wrong, possibly already reported.
                    // If the book has sign language videos, we don't create a PDF, so we don't want to report a PDF generation failure.
                    // Somewhere in 5.5, we lost setting PdfGenerationSucceeded; so I'm just commenting this out for now.
                    //if (Model.PdfGenerationSucceeded || Model.Book.HasSignLanguageVideos())
                    ReportTryAgainDuringUpload();
                    //else
                    //	ReportPdfGenerationFailed();
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
            };
            SetParentControlsState(false); // Last thing we do before launching the worker, so we can't get stuck in this state.
            worker.RunWorkerAsync(Model);
        }

        void BackgroundUpload(object _, DoWorkEventArgs e)
        {
            var checkerResult = Model.CheckBookBeforeUpload();
            if (checkerResult != null)
            {
                _webSocketProgress.MessageWithoutLocalizing(checkerResult, ProgressKind.Error);
                e.Result = "quiet"; // suppress other completion/fail messages
                return;
            }

            Model.UpdateBookMetadataFeatures(
                Model.Book.BookInfo.PublishSettings.BloomLibrary.AudioLangs.Any(),
                ModelIndicatesSignLanguageChecked
            );

            // We currently have no way to turn this off. This is by design, we don't think it is a needed complication.
            var includeBackgroundMusic = true;

            var bookObjectId = Model.UploadOneBook(
                Model.Book,
                _progress,
                _publishModel,
                !includeBackgroundMusic,
                _existingBookObjectIdOrNull
            );

            e.Result = bookObjectId;
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
            GetWorkspaceView()?.SetStateOfNonPublishTabs(enable);
        }

        private WorkspaceView GetWorkspaceView()
        {
            var parent = _publishView.Parent;
            while (parent != null && !(parent is WorkspaceView))
                parent = parent.Parent;
            return (WorkspaceView)parent;
        }

        private void HandleUploadCollection(ApiRequest request)
        {
            if (!ValidateBookshelfBeforeBulkUpload())
            {
                request.PostSucceeded();
                return;
            }

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

            dynamic collisionDialogInfo;
            try
            {
                collisionDialogInfo = Model.GetUploadCollisionDialogProps(
                    Model.TextLanguagesToAdvertiseOnBloomLibrary,
                    ModelIndicatesSignLanguageChecked
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

        private dynamic CollisionDialogInfoForErrorCondition =>
            new
            {
                error = true, // Inform the client there was an error. Don't continue with the upload.
                shouldShow = false // Don't show the dialog. (Currently this is ignored if error is true; in that case, we never show the dialog.)
            };

        private void HandleUploadAfterChangingBookId(ApiRequest request)
        {
            Model.ChangeBookId(_progress);
            HandleUpload(request);
        }

        private void HandleCheckForLoggedInUser(ApiRequest request)
        {
            // Why not just reply with the WebUserId instead?
            // Because we already have this event hooked up for the user-initiated log in process.
            // So it simplifies the client to just reuse this web socket event.
            if (Model.LoggedIn)
            {
                Logger.WriteEvent("User already logged in. Sending message to js-land.");
                _webSocketServer.SendString(
                    kWebSocketContext,
                    kWebSocketEventId_loginSuccessful,
                    Model?.WebUserId
                );
            }
            request.PostSucceeded();
        }

        private void HandleLogin(ApiRequest request)
        {
            Model.LogIn();
            Logger.WriteEvent("User attempting to login to bloomlibrary.org.");
            request.PostSucceeded();
        }

        private void HandleLogout(ApiRequest request)
        {
            Model.LogOut();
            request.PostSucceeded();
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

        private void HandleGoToEditBookCover(ApiRequest request)
        {
            // 0 is the index of the first page, the front cover.
            Model.Book.UserPrefs.MostRecentPage = 0;
            GetWorkspaceView()?.ChangeTab(WorkspaceTab.edit);
            request.PostSucceeded();
        }
    }
}
