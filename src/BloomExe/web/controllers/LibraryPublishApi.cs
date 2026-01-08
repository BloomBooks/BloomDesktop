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
using SIL.Reporting;

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
            apiHandler.RegisterEndpointHandler("libraryPublish/cancel", HandleCancel, true);
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

        private void HandleCancel(ApiRequest request)
        {
            _progress.CancelRequested = true;
            request.PostSucceeded();
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

        private async Task HandleUpload(ApiRequest request, bool changeUploader)
        {
            if (request.HttpMethod == HttpMethods.Get)
                return;
            _changeUploader = changeUploader;

            _progress.CancelRequested = false;

            try
            {
                await UploadBookAsync();
            }
            catch (Exception)
            {
                ReportTryAgainDuringUpload();
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

            if (_progress.CancelRequested)
            {
                _webSocketProgress.Message("Cancelled", "Upload was cancelled", ProgressKind.Error);
                _webSocketServer.SendEvent(kWebSocketContext, kWebSocketEventId_uploadCanceled);
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
