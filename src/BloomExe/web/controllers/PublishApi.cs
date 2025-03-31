using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish;
using Bloom.Publish.BloomPub;
using Bloom.Publish.BloomLibrary;
using BloomTemp;
using Newtonsoft.Json;
using SIL.IO;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using SIL.Reporting;
using Bloom.CollectionTab;

namespace Bloom.web.controllers
{
    /// <summary>
    /// This class handles the API calls that are common to more than one publishing tab.
    /// (Generally, each tab has its own Api class for its private API.)
    /// </summary>
    public class PublishApi
    {
        private BookUpload _bookTransferrer;
        private PublishModel _publishModel;
        public static BloomLibraryPublishModel Model { get; set; }
        private IBloomWebSocketServer _webSocketServer;
        private readonly BookServer _bookServer;
        private Dictionary<string, bool> _allLanguages;
        private HashSet<string> _languagesWithAudio = new HashSet<string>();
        private Book.Book _bookForLanguagesToPublish = null;
        private object _lockForLanguages = new object();
        internal BloomPubPublishSettings _lastSettings;
        internal Color _thumbnailBackgroundColor = Color.Transparent; // can't be actual book cover color <--- why not?
        private Color _lastThumbnailBackgroundColor;

        // This constant must match the ID that is used for the listener set up in the client
        private const string kWebsocketEventId_Preview = "bloomPubPreview";
        private Book.Book _coverColorSourceBook;
        public const string kStagingFolder = "PlaceForStagingBook";

        // This constant must match the ID used for the useWatchString called by the React component MethodChooser.
        private const string kWebsocketState_LicenseOK = "publish/licenseOK";

        internal const string kWebSocketContext = "publish-bloompub"; // must match client

        private static TemporaryFolder _stagingFolder;

        internal bool LicenseOK;

        private readonly WebSocketProgress _progress;

        public static string PreviewUrl { get; set; }
        private WorkspaceTabSelection _tabSelection;

        /// <summary>
        /// Conceptually, this is where we are currently building a book for preview.
        /// In the current implementation, it is not cleared when we are no longer doing so.
        /// Nor does it include the path to the individual book folder, just the staging
        /// folder. This is not ideal but it serves the current limited purpose of this field.
        /// </summary>
        public static string CurrentPublicationFolder { get; private set; }

        public PublishApi(
            BloomWebSocketServer webSocketServer,
            BookServer bookServer,
            BookUpload bookTransferrer,
            PublishModel model,
            WorkspaceTabSelection tabSelection,
            CollectionModel collectionModel
        )
        {
            _webSocketServer = webSocketServer;
            _bookServer = bookServer;
            _progress = new WebSocketProgress(_webSocketServer, PublishApi.kWebSocketContext);
            _bookTransferrer = bookTransferrer;
            _publishModel = model;
            _tabSelection = tabSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "publish/getInitialPublishTabInfo",
                getInitialPublishTabInfo,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "publish/switchingPublishMode",
                (request) =>
                {
                    // Abort any work we're doing to prepare a preview (at least stop it interfering with other navigation)
                    PublishHelper.Cancel();
                    request.PostSucceeded();
                },
                false
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "publish/signLanguage",
                request => Model.Book.HasSignLanguageVideos() && Model.IsPublishSignLanguage(),
                (request, val) =>
                {
                    if (val)
                    {
                        // If we don't know a sign language to advertise, nothing will happen in
                        // the book metadata. The UI will show a link to let the user select a language.
                        // If the user does not do so, the checked state will not persist.
                        if (!string.IsNullOrEmpty(Model.Book.CollectionSettings.SignLanguageTag))
                            Model.SetOnlySignLanguageToPublish(
                                Model.Book.CollectionSettings.SignLanguageTag
                            );
                    }
                    else
                        Model.ClearSignLanguageToPublish();
                },
                true
            );

            apiHandler.RegisterBooleanEndpointHandler(
                "publish/hasVideo",
                request => Model.Book.HasSignLanguageVideos(),
                null,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "publish/signLanguageName",
                (request) =>
                {
                    request.ReplyWithText(Model.Book.CollectionSettings.SignLanguage?.Name ?? "");
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                "publish/l1Name",
                (request) => request.ReplyWithText(Model.Book.BookData.Language1.Name),
                true
            );
            apiHandler.RegisterEndpointHandler(
                "publish/chooseSignLanguage",
                HandleChooseSignLanguage,
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "publish/hasActivities",
                request => Model.Book.HasActivities,
                null,
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "publish/comicEnabled",
                request => Model.Book.HasComicalOverlays,
                null,
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "publish/comic",
                request =>
                    Model.Book.HasComicalOverlays
                    && Model.Book.BookInfo.PublishSettings.BloomLibrary.Comic,
                (request, val) =>
                {
                    Model.Book.BookInfo.PublishSettings.BloomLibrary.Comic = val;
                    Model.Book.BookInfo.Save();
                },
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "publish/visuallyImpairedEnabled",
                request => Model.Book.OurHtmlDom.HasImageDescriptions,
                null,
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "publish/visuallyImpaired",
                request =>
                    Model.Book.OurHtmlDom.HasImageDescriptions && Model.L1SupportsVisuallyImpaired,
                (request, val) =>
                {
                    Model.L1SupportsVisuallyImpaired = val; // Saves the BookInfo with the new value
                },
                true
            );

            apiHandler.RegisterBooleanEndpointHandler(
                "publish/canHaveMotionMode",
                request =>
                {
                    return request.CurrentBook.HasMotionPages;
                },
                null, // no write action
                false,
                true
            ); // we don't really know, just safe default
            apiHandler.RegisterEndpointHandler(
                "publish/languagesInBook",
                request =>
                {
                    try
                    {
                        InitializeLanguagesInBook(request);

                        Dictionary<string, InclusionSetting> textLangsToPublish = request
                            .CurrentBook
                            .BookInfo
                            .PublishSettings
                            .BloomLibrary
                            .TextLangs;
                        Dictionary<string, InclusionSetting> audioLangsToPublish = request
                            .CurrentBook
                            .BookInfo
                            .PublishSettings
                            .BloomLibrary
                            .AudioLangs;

                        var result =
                            "["
                            + string.Join(
                                ",",
                                _allLanguages.Select(kvp =>
                                {
                                    string langCode = kvp.Key;

                                    bool includeText = false;
                                    if (
                                        textLangsToPublish != null
                                        && textLangsToPublish.TryGetValue(
                                            langCode,
                                            out InclusionSetting includeTextSetting
                                        )
                                    )
                                    {
                                        includeText = includeTextSetting.IsIncluded();
                                    }

                                    bool includeAudio = false;
                                    if (
                                        audioLangsToPublish != null
                                        && audioLangsToPublish.TryGetValue(
                                            langCode,
                                            out InclusionSetting includeAudioSetting
                                        )
                                    )
                                    {
                                        includeAudio = includeAudioSetting.IsIncluded();
                                    }

                                    var required = false;
                                    if (Model.Book.IsRequiredLanguage(kvp.Key))
                                    {
                                        includeText = true;
                                        required = true;
                                    }

                                    var value = new LanguagePublishInfo()
                                    {
                                        code = kvp.Key,
                                        name = request.CurrentBook.PrettyPrintLanguage(langCode),
                                        complete = kvp.Value,
                                        includeText = includeText,
                                        containsAnyAudio = _languagesWithAudio.Contains(langCode),
                                        includeAudio = includeAudio,
                                        required = required
                                    };
                                    var json = JsonConvert.SerializeObject(value);
                                    return json;
                                })
                            )
                            + "]";

                        request.ReplyWithText(result);
                    }
                    catch (Exception e)
                    {
                        request.Failed(
                            "Error while determining languages in book. Message: " + e.Message
                        );
                        NonFatalProblem.Report(
                            ModalIf.Alpha,
                            PassiveIf.All,
                            "Error determining which languages are in the book.",
                            null,
                            e,
                            true
                        );
                    }
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                "publish/thumbnail",
                request =>
                {
                    var coverImage = request.CurrentBook.GetCoverImagePath();
                    if (coverImage == null)
                        request.Failed("no cover image");
                    else
                    {
                        // We don't care as much about making it resized as making its background transparent.
                        using (var thumbnail = TempFile.CreateAndGetPathButDontMakeTheFile())
                        {
                            if (_thumbnailBackgroundColor == Color.Transparent)
                            {
                                ImageUtils.TryCssColorFromString(
                                    request.CurrentBook?.GetCoverColor(),
                                    out _thumbnailBackgroundColor
                                );
                            }
                            RuntimeImageProcessor.GenerateEBookThumbnail(
                                coverImage,
                                thumbnail.Path,
                                256,
                                256,
                                _thumbnailBackgroundColor,
                                padImageToRequestedSize: false
                            );
                            request.ReplyWithImage(thumbnail.Path);
                        }
                    }
                },
                true
            );

            apiHandler.RegisterBooleanEndpointHandler(
                "publish/motionBookMode",
                readRequest =>
                {
                    return readRequest.CurrentBook.HasMotionPages
                        && readRequest
                            .CurrentBook
                            .BookInfo
                            .PublishSettings
                            .BloomPub
                            .PublishAsMotionBookIfApplicable;
                },
                (writeRequest, value) =>
                {
                    writeRequest
                        .CurrentBook
                        .BookInfo
                        .PublishSettings
                        .BloomPub
                        .PublishAsMotionBookIfApplicable = value;
                    writeRequest.CurrentBook.BookInfo.SavePublishSettings();
                    _webSocketServer.SendEvent("publish", "motionChanged");
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                "publish/includeLanguage",
                request =>
                {
                    var langCode = request.RequiredParam("langCode");
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        var includeTextValue = request.GetParamOrNull("includeText");
                        if (includeTextValue != null)
                        {
                            var inclusionSetting =
                                includeTextValue == "true"
                                    ? InclusionSetting.Include
                                    : InclusionSetting.Exclude;
                            request.CurrentBook.BookInfo.PublishSettings.BloomLibrary.TextLangs[
                                langCode
                            ] = inclusionSetting;
                        }

                        var includeAudioValue = request.GetParamOrNull("includeAudio");
                        if (includeAudioValue != null)
                        {
                            var inclusionSetting =
                                includeAudioValue == "true"
                                    ? InclusionSetting.Include
                                    : InclusionSetting.Exclude;
                            request.CurrentBook.BookInfo.PublishSettings.BloomLibrary.AudioLangs[
                                langCode
                            ] = inclusionSetting;
                        }

                        request.CurrentBook.BookInfo.Save(); // We updated the BookInfo, so need to persist the changes. (but only the bookInfo is necessary, not the whole book)
                        request.PostSucceeded();
                    }
                    // We don't currently need a get...it's subsumed in the 'include' value returned from allLanguages...
                    // but if we ever do this is what it would look like.
                    //else
                    //{
                    //	request.ReplyWithText(_languagesToPublish.Contains(langCode) ? "true" : "false");
                    //}
                },
                false
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "publish/markAsDraft",
                readRequest =>
                {
                    return readRequest.CurrentBook.BookInfo.MetaData.Draft;
                },
                (writeRequest, value) =>
                {
                    writeRequest.CurrentBook.BookInfo.MetaData.Draft = value;
                    writeRequest.CurrentBook.BookInfo.Save(); // We updated the BookInfo, so need to persist the changes. (but only the bookInfo is necessary, not the whole book)
                },
                false
            );
        }

        public void getInitialPublishTabInfo(ApiRequest request)
        {
            _publishModel.UpdateModelUponActivation();
            // There should be a current selection by now but just in case:
            if (_publishModel.BookSelection.CurrentSelection == null)
            {
                request.ReplyWithJson(new { });
                return;
            }
            LibraryPublishApi.Model = Model = new BloomLibraryPublishModel(
                _bookTransferrer,
                _publishModel.BookSelection.CurrentSelection,
                _publishModel
            );
            Logger.WriteEvent("Entered Publish Tab");
            var featureStatus = _publishModel.GetFeaturePreventingPublishingOrNull();
            var featureStatusForSerialization = featureStatus?.ForSerialization();
            var resultObject = new
            {
                canUpload = _publishModel.BookSelection.CurrentSelection.BookInfo.AllowUploading,
                cannotPublishWithoutCheckout = _publishModel.CannotPublishWithoutCheckout,
                canDownloadPDF = _publishModel.PdfGenerationSucceeded, // To be used for the context menu
                titleForDisplay = _publishModel
                    .BookSelection
                    .CurrentSelection
                    .TitleBestForUserDisplay,
                featurePreventingPublishing = featureStatusForSerialization
            };

            var result = JsonConvert.SerializeObject(resultObject);
            request.ReplyWithJson(result);
        }

        private void HandleChooseSignLanguage(ApiRequest request)
        {
            Application.Idle += LaunchChooseSignLanguage;
            request.PostSucceeded();
        }

        private void LaunchChooseSignLanguage(object sender, EventArgs e)
        {
            Application.Idle -= LaunchChooseSignLanguage;
            var collectionSettings = Model.Book.CollectionSettings;
            void onLanguageChange(LanguageChangeEventArgs args)
            {
                // How to know if the new sign language name is custom or not!?
                // 1- set the Tag (which also sets the Name to the non-custom default
                // 2- read the Name
                // 3- if it's not the same as DesiredName, the new name is custom
                collectionSettings.SignLanguageTag = args.LanguageTag;
                var slIsCustom = collectionSettings.SignLanguage.Name != args.DesiredName;
                collectionSettings.SignLanguage.SetName(args.DesiredName, slIsCustom);
                collectionSettings.Save();
                Model.UpdateLangDataCache();

                Model.SetOnlySignLanguageToPublish(collectionSettings.SignLanguageTag);
                _webSocketServer.SendString("publish", "signLang", args.DesiredName);
            }

            CollectionSettingsDialog.ChangeLanguage(
                onLanguageChange,
                collectionSettings.SignLanguageTag,
                CurrentSignLanguageName
            );
        }

        private string CurrentSignLanguageName
        {
            get { return Model.Book.CollectionSettings.SignLanguage.Name; }
        }

        /// <summary>
        /// The book language data needs to be initialized before handling updatePreview requests, but the
        /// languagesInBook request comes in after the updatePreview request.  So we call this method in
        /// both places with a lock to prevent stepping on each other.  This results in duplicate
        /// calls for AllLanguages, but is safest since the user could leave the publish tab, change the
        /// languages in the book, and then come back to the publish tab with the same book.
        /// </summary>
        private void InitializeLanguagesInBook(ApiRequest request)
        {
            lock (_lockForLanguages)
            {
                _allLanguages = request.CurrentBook.AllPublishableLanguages(
                    // True up to 5.6. Things are a bit tricky if xmatter contains L2 and possibly L3 data.
                    // We always include that xmatter data if it is needed for the book to be complete.
                    // But if nothing in the book content is in those languages, we don't list them as
                    // book languages, either in Blorg or in Bloom Player. To be consistent, we don't
                    // even want to have check boxes for them (they would not have any effect, since there
                    // is nothing in the book in those languages that is optional to include).
                    includeLangsOccurringOnlyInXmatter: false
                );

                // Note that at one point, we had a check that would bypass most of this function if the book hadn't changed.
                // However, one side effect of this is that any settings behind the if guard would not be updated if the book was edited
                // At one point, whenever a check box changed, the whole Publish screen was regenerated (along with languagesInBook being retrieved again),
                // but this is no longer the case.
                // So, we no longer have any bypass... Instead we recompute the values so that they can be updated
                _bookForLanguagesToPublish = request.CurrentBook;

                _languagesWithAudio = request.CurrentBook.GetLanguagesWithAudio();

                // I think this is redundant with initialization of the model we do when switching to
                // publish tab but am not sure enough to leave it out.
                InitializeLanguagesInBook(_bookForLanguagesToPublish);
            }
        }

        // Precondition: If any locking is required, the caller should handle it.
        internal static void InitializeLanguagesInBook(Book.Book book)
        {
            BloomLibraryPublishModel.InitializeLanguages(book);
        }

        /// <summary>
        /// Updates the BloomReader preview. The URL of the BloomReader preview will be sent over the web socket.
        /// The format of the URL is a valid ("single" encoded) URL.
        /// If the caller wants to insert this URL as a query parameter to another URL (e.g. like what is often done with Bloom Player),
        /// it's the caller's responsibility to apply another layer of URL encoding to make the URL suitable to be passed as data inside another URL.
        /// </summary>
        /// <returns>True if the preview was updated successfully, false otherwise.</returns>
        internal bool UpdatePreview(ApiRequest request, bool forVideo)
        {
            _progress.Reset(); // Otherwise errors get carried over between runs of the preview.
            InitializeLanguagesInBook(request);
            _lastSettings = GetSettings();
            _lastThumbnailBackgroundColor = _thumbnailBackgroundColor;
            if (forVideo)
            {
                // We'll put all possible languages in the preview; the user can choose the one wanted
                // with the preview controls if there is more than one. Don't include any that are NOT
                // allowed to be published, or we won't get a preview at all, just an error message.
                // Review: do we need to give some message about why some were not included?
                var licenseChecker = new LicenseChecker();
                var allowedLanguages = licenseChecker.AllowedLanguages(
                    _allLanguages.Keys.ToArray(),
                    request.CurrentBook
                );
                if (!allowedLanguages.Any())
                {
                    allowedLanguages = _allLanguages.Keys; // making preview will fail, but with a helpful message.
                }
                _lastSettings = _lastSettings.WithAllLanguages(allowedLanguages);
            }

            _lastSettings.PublishAsMotionBookIfApplicable = forVideo
                ? request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Motion
                : request
                    .CurrentBook
                    .BookInfo
                    .PublishSettings
                    .BloomPub
                    .PublishAsMotionBookIfApplicable;
            _lastSettings.WantPageLabels = forVideo;
            // BloomPlayer is capable of skipping these, but they confuse the page list we use to populate
            // the page-range control.
            _lastSettings.RemoveInteractivePages = forVideo;
            PreviewUrl = MakeBloomPubForPreview(
                request.CurrentBook,
                _bookServer,
                _progress,
                _thumbnailBackgroundColor,
                _lastSettings
            );
            if (PreviewUrl == null)
            {
                // Tried sending empty string, but SendString() ignores empty messages. "stopPreview" gets interpreted as a command to stop
                // the preview spinner.
                _webSocketServer.SendString(
                    kWebSocketContext,
                    kWebsocketEventId_Preview,
                    "stopPreview"
                );
                return false;
            }
            _webSocketServer.SendString(kWebSocketContext, kWebsocketEventId_Preview, PreviewUrl);
            return true;
        }

        public void MakeBloompubPreview(ApiRequest request, bool forVideo)
        {
            if (request.HttpMethod == HttpMethods.Post)
            {
                // This is already running on a server thread, so there doesn't seem to be any need to kick off
                // another background one and return before the preview is ready. But in case something in C#
                // might one day kick of a new preview, or we find we do need a background thread,
                // I've made it a websocket broadcast when it is ready.
                // If we've already left the publish tab...we can get a few of these requests queued up when
                // a tester rapidly toggles between views...abandon the attempt
                if (_tabSelection.ActiveTab != WorkspaceTab.publish)
                {
                    request.Failed("aborted, no longer in publish tab");
                    return;
                }

                try
                {
                    UpdatePreview(request, forVideo);
                    request.PostSucceeded();
                }
                catch (Exception e)
                {
                    request.Failed("Error while updating preview. Message: " + e.Message);
                    NonFatalProblem.Report(
                        ModalIf.Alpha,
                        PassiveIf.All,
                        "Error while updating preview.",
                        null,
                        e,
                        true
                    );
                }
            }
        }

        /// <summary>
        /// Check whether we are allowed to publish this book in this language (using LicenseChecker)
        /// </summary>
        internal bool IsBookLicenseOK(
            Book.Book book,
            BloomPubPublishSettings settings,
            WebSocketProgress progress
        )
        {
            if (settings?.LanguagesToInclude != null)
            {
                var message = new LicenseChecker().CheckBook(
                    book,
                    settings.LanguagesToInclude.ToArray()
                );
                if (message != null)
                {
                    if (progress != null)
                        progress.MessageWithoutLocalizing(message, ProgressKind.Error);
                    LicenseOK = false;
                    _webSocketServer.SendString(
                        kWebSocketContext,
                        kWebsocketState_LicenseOK,
                        "false"
                    );
                    return false;
                }
            }
            LicenseOK = true;
            return true;
        }

        /// <summary>
        /// Generates an unzipped, staged BloomPUB from the book
        /// </summary>
        /// <returns>A valid, well-formed URL on localhost that points to the staged book's htm file,
        /// or null if we aren't allowed to publish this book in this language (LicenseChecker).</returns>
        public string MakeBloomPubForPreview(
            Book.Book book,
            BookServer bookServer,
            WebSocketProgress progress,
            Color backColor,
            BloomPubPublishSettings settings
        )
        {
            progress.Message("PublishTab.Epub.PreparingPreview", "Preparing Preview"); // message shared with Epub publishing
            if (!IsBookLicenseOK(book, settings, progress))
                return null;
            _webSocketServer.SendString(kWebSocketContext, kWebsocketState_LicenseOK, "true");

            _stagingFolder?.Dispose();
            if (AudioProcessor.IsAnyCompressedAudioMissing(book.FolderPath, book.RawDom))
            {
                progress.Message("CompressingAudio", "Compressing audio files");
                AudioProcessor.TryCompressingAudioAsNeeded(book.FolderPath, book.RawDom);
            }
            // BringBookUpToDate() will already have been done on the original book on entering the Publish tab.

            // We don't use the folder found here, but this method does some checks we want done.
            BookStorage.FindBookHtmlInFolder(book.FolderPath);
            _stagingFolder = new TemporaryFolder(kStagingFolder);
            // I'd prefer this to include the book folder, but we need it before PrepareBookForBloomReader returns.
            // I believe we only ever have one book being made there, so it works.
            CurrentPublicationFolder = _stagingFolder.FolderPath;
            var modifiedBook = BloomPubMaker.PrepareBookForBloomReader(
                settings,
                bookFolderPath: book.FolderPath,
                bookServer: bookServer,
                temp: _stagingFolder,
                progress,
                isTemplateBook: book.IsTemplateBook
            );
            // Compress images for preview as well as actual publish step.  See comments in BL-12130.
            BloomPubMaker.CompressImages(
                modifiedBook.FolderPath,
                settings.ImagePublishSettings,
                modifiedBook.RawDom
            );
            progress.Message(
                "Common.Done",
                "Shown in a list of messages when Bloom has completed a task.",
                "Done"
            );
            if (settings?.WantPageLabels ?? false)
            {
                int pageNum = 0;
                var labelData = modifiedBook
                    .GetPages()
                    .Select(p =>
                    {
                        var caption = p.GetCaptionOrPageNumber(ref pageNum, out string i18nId);
                        if (!string.IsNullOrEmpty(caption))
                            caption = I18NApi.GetTranslationDefaultMayNotBeEnglish(i18nId, caption);
                        return caption;
                    })
                    .ToArray();
                dynamic messageBundle = new DynamicJson();
                messageBundle.labels = labelData;
                // We send these through the websocket rather than getting them through an API because the
                // API request would very likely come before we finish generating the preview so at best we'd
                // have to delay the response. Then the request might time out on a long book. So we'd really
                // need an event anyway to tell the Typescript code it is time to request the labels.
                // And then it would take some nasty spaghetti code to get the labels to the PublishVideoApi
                // class so it could reply to the request. Cleaner just to send it through the socket.
                _webSocketServer.SendBundle("publishPageLabels", "ready", messageBundle);
            }

            return modifiedBook.GetPathHtmlFile().ToLocalhost();
        }

        internal bool UpdatePreviewIfNeeded(ApiRequest request)
        {
            var newSettings = GetSettings();
            if (
                newSettings.Equals(_lastSettings)
                && _thumbnailBackgroundColor == _lastThumbnailBackgroundColor
                && LicenseOK
            )
            {
                return true;
            }
            return UpdatePreview(request, false);
        }

        internal BloomPubPublishSettings GetSettings()
        {
            return BloomPubPublishSettings.FromBookInfo(_bookForLanguagesToPublish.BookInfo);
        }

        private static string ToCssColorString(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public void Dispose()
        {
            _stagingFolder?.Dispose();
        }
    }
}
