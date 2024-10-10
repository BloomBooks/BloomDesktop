using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Utils;
using Bloom.web;
using Bloom.Workspace;
using DesktopAnalytics;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Publish.Epub
{
    /// <summary>
    /// Handles API requests from the Epub publishing HTML UI
    /// </summary>
    public class PublishEpubApi
    {
        private const string kApiUrlPart = "publish/epub/"; // common prefix of requests this class handles

        // Autofac singletons we need and get through our constructor
        private BookServer _bookServer;
        private BookThumbNailer _thumbNailer;
        private BookSelection _bookSelection;
        private CollectionSettings _collectionSettings;
        private BloomWebSocketServer _webSocketServer;

        // The usual place to report progress, read by the progress panel in the main epub preview window.
        private readonly WebSocketProgress _standardProgress;

        // The progress socket manager that is actually used to report progress with making epubs.
        // Usually _standardProgress, but when the epub is being generated for another purpose
        // besides the preview in the main epub window (e.g., for Daisy checker), we use that
        // window's progress box.
        private WebSocketProgress _progress;

        private string _previewSrc;
        private string _bookVersion;
        private WorkspaceTabSelection _tabSelection;

        // This goes out with our messages and, on the client side (typescript), messages are filtered
        // down to the context (usually a screen) that requested them.
        private const string kWebsocketContext = "publish-epub";

        private const string kWebsocketEventId_epubReady = "newEpubReady";

        // This constant must match the ID used for the useWatchString called by the React component EPUBPublishScreenInternal.
        private const string kWebsocketState_LicenseOK = "publish/licenseOK";

        public EpubMaker EpubMaker { get; private set; }
        public static Control ControlForInvoke { get; set; }

        public PublishEpubApi(
            BookThumbNailer thumbNailer,
            BookServer bookServer,
            BookSelection bookSelection,
            CollectionSettings collectionSettings,
            BloomWebSocketServer webSocketServer,
            WorkspaceTabSelection tabSelection
        )
        {
            _thumbNailer = thumbNailer;
            _bookServer = bookServer;
            _bookSelection = bookSelection;
            _collectionSettings = collectionSettings;
            _webSocketServer = webSocketServer;
            _standardProgress = new WebSocketProgress(_webSocketServer, kWebsocketContext);
            _tabSelection = tabSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "save", HandleEpubSave, false, false);

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "epubMode",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        if (request.CurrentBook.OurHtmlDom.HasOverlayPages())
                            // If we have comic pages (now), we have to use fixed layout, even if flowable was set at some point.
                            request.ReplyWithText("fixed");
                        else
                            request.ReplyWithText(
                                request.CurrentBook.BookInfo.PublishSettings.Epub.Mode
                            );
                    }
                    else
                    {
                        request.CurrentBook.BookInfo.PublishSettings.Epub.Mode =
                            request.RequiredPostString();
                        request.CurrentBook.BookInfo.Save();
                        request.PostSucceeded();
                    }
                },
                false
            );

            // The backend here was written with an enum that had two choices for how to publish descriptions, but we only ever
            // have used one of them so far in the UI. So this is a boolean api that converts to an enum underlying value.
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "imageDescriptionSetting",
                request =>
                    request.CurrentBook.BookInfo.PublishSettings.Epub.HowToPublishImageDescriptions
                    == BookInfo.HowToPublishImageDescriptions.OnPage,
                (request, onPage) =>
                {
                    request
                        .CurrentBook
                        .BookInfo
                        .PublishSettings
                        .Epub
                        .HowToPublishImageDescriptions = onPage
                        ? BookInfo.HowToPublishImageDescriptions.OnPage
                        : BookInfo.HowToPublishImageDescriptions.None;
                    request.CurrentBook.BookInfo.Save();
                },
                false
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "updatePreview",
                request =>
                {
                    RefreshPreview(request.CurrentBook.BookInfo.PublishSettings.Epub);
                    request.PostSucceeded();
                    if (request.CurrentBook?.ActiveLanguages != null)
                    {
                        var message = new LicenseChecker().CheckBook(
                            request.CurrentBook,
                            request.CurrentBook.ActiveLanguages.ToArray()
                        );
                        _webSocketServer.SendString(
                            kWebsocketContext,
                            kWebsocketState_LicenseOK,
                            (message == null) ? "true" : "false"
                        );
                    }
                },
                false
            ); // must not be on the UI thread, because it's a long-running task and the UI thread must be available
            // to update the progress dialog, respond to user actions, etc.

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "abortPreview",
                request =>
                {
                    AbortMakingEpub();

                    request.PostSucceeded();
                },
                false,
                false
            );

            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "landscape",
                request => request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape,
                null,
                false
            );
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "overlays",
                request => request.CurrentBook.OurHtmlDom.HasOverlayPages(),
                null,
                false
            );
        }

        private void HandleEpubSave(ApiRequest request)
        {
            // Will only update the staged ePUB files if needed.  If the license is not valid, we still want
            // enough work done to display the error message to the user.  See BL-12334
            if (
                RefreshPreview(
                    request.CurrentBook.BookInfo.PublishSettings.Epub,
                    forceUpdate: false
                )
            )
            {
                request.PostSucceeded();
                if (request.CurrentBook?.ActiveLanguages != null)
                {
                    var message = new LicenseChecker().CheckBook(
                        request.CurrentBook,
                        request.CurrentBook.ActiveLanguages.ToArray()
                    );
                    _webSocketServer.SendString(
                        kWebsocketContext,
                        kWebsocketState_LicenseOK,
                        (message == null) ? "true" : "false"
                    );
                    if (message != null)
                    {
                        EpubMaker.AbortRequested = true; // cause license error message to be displayed if the user clicks again
                        return;
                    }
                }
                string destPath = null;
                if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
                {
                    ControlForInvoke.Invoke((Action)(() => destPath = getSaveAsPath(request)));
                }
                else
                {
                    destPath = getSaveAsPath(request);
                }
                if (string.IsNullOrEmpty(destPath))
                {
                    // No output path means the user cancelled the file chooser dialog.
                    return;
                }
                // The necessary ePUB files are present
                SaveAsEpub(destPath);
            }
            else
            {
                request.PostSucceeded();
                // Preview generation failed; the ePUB files might not all be there, we should not save.
                // Notify the user gently that updating the ePUB preview failed.
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    "Something went wrong while saving the ePUB."
                );
            }
        }

        private string getSaveAsPath(ApiRequest request)
        {
            var initialPath = OutputFilenames.GetOutputFilePath(
                _bookSelection.CurrentSelection,
                ".epub"
            );
            var destFileName = Utils.MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                initialPath,
                "ePUB files|*.epub"
            );
            if (!string.IsNullOrEmpty(destFileName))
            {
                OutputFilenames.RememberOutputFilePath(
                    _bookSelection.CurrentSelection,
                    ".epub",
                    destFileName
                );
            }
            return destFileName;
        }

        public void AbortMakingEpub()
        {
            if (EpubMaker != null)
            {
                // typically will cause some OTHER thread that is making the epub to wind up quickly.
                EpubMaker.AbortRequested = true;
            }
        }

        private bool RefreshPreview(EpubSettings newSettings, bool forceUpdate = true)
        {
            // We have seen some exceptions thrown during refresh that cause a pretty yellow
            // dialog box pop up informing the user, e.g., that the program couldn't find
            // "api/publish/epub/updatePreview".  Rather than confuse the user, we catch such
            // exceptions here and retry a limited number of times.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-6763.
            Exception exception = null;
            for (int i = 0; i < 3; ++i)
            {
                try
                {
                    if (UpdatePreview(newSettings, forceUpdate))
                    {
                        _webSocketServer.SendString(
                            kWebsocketContext,
                            kWebsocketEventId_epubReady,
                            _previewSrc
                        );
                        return true;
                    }
                }
                catch (Exception e)
                {
                    exception = e; // the original stack trace is rather important for post mortem debugging!
                }
            }
            string detailsMessage = "Updating the ePUB preview failed";
            // If we are here because UpdatePreview repeatedly returned false indicating it failed, we won't have an exception
            if (exception != null)
            {
                detailsMessage = detailsMessage + ": " + exception.Message;
            }
            // Notify the user gently that updating the ePUB preview failed.
            NonFatalProblem.Report(
                ModalIf.None,
                PassiveIf.All,
                "Something went wrong while making the ePUB preview.",
                detailsMessage,
                exception
            );
            return false;
        }

        public void ReportAnalytics(string eventName)
        {
            Analytics.Track(
                eventName,
                new Dictionary<string, string>()
                {
                    { "BookId", _bookSelection.CurrentSelection.ID },
                    { "Country", _collectionSettings.Country }
                }
            );
        }

        internal void PrepareToStageEpub()
        {
            if (EpubMaker != null)
            {
                //it has state that we don't want to reuse, so make a new one
                EpubMaker.Dispose();
                EpubMaker = null;
            }

            EpubMaker = new EpubMaker(_thumbNailer, _bookServer);

            EpubMaker.Book = _bookSelection.CurrentSelection;
            EpubMaker.Unpaginated =
                _bookSelection.CurrentSelection.BookInfo.PublishSettings.Epub.Mode == "flowable";
            EpubMaker.OneAudioPerPage = true;
        }

        internal string StagingDirectory
        {
            get { return EpubMaker.BookInStagingFolder; }
        }

        internal void SaveAsEpub(string destPath)
        {
            EpubMaker.ZipAndSaveEpub(destPath, _progress);
            _bookSelection.CurrentSelection.ReportSimplisticFontAnalytics(
                FontAnalytics.FontEventType.PublishEbook,
                "ePUB"
            );
            ReportAnalytics("Save ePUB");
        }

        public string UpdateEpubControlContent()
        {
            // Enhance: this could be optimized (but it will require changes to EpubMaker, it assumes it only stages once)
            PrepareToStageEpub();
            // Initialize the settings to affect the first epub preview.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7316.
            var settings = _bookSelection.CurrentSelection.BookInfo.PublishSettings.Epub;

            // We can't visibly publish image descriptions in fixed mode.
            EpubMaker.PublishImageDescriptions = (
                settings.Mode == "fixed"
                    ? BookInfo.HowToPublishImageDescriptions.None
                    : settings.HowToPublishImageDescriptions
            );
            // In BL-11499 we decided that 'flowable' epubs should leave font size decisions
            // up to the reader always, while 'fixed' ones must control everything.
            EpubMaker.RemoveFontSizes = settings.Mode == "flowable";
            // (Previous versions of Bloom had a checkbox and API to control this in flowable mode.)
            //EpubMaker.RemoveFontSizes = settings.RemoveFontSizes;

            return SetupEpubControlContent();
        }

        public string SetupEpubControlContent()
        {
            // This gets called on a background thread but one step needs to happen on the UI thread,
            // so the Maker needs a control to Invoke on. An Api class doesn't naturally have one to give it,
            // so we arrange that this class is given the Bloom main window by the PublishView when the
            // publish tab is activated. In production, this is roughly equivalent to just using
            // Form.ActiveForms.Last(), but that fails when debugging; this is more robust.
            EpubMaker.ControlForInvoke = ControlForInvoke;
            EpubMaker.StageEpub(_progress);
            if (StagingDirectory == null)
                return null; // aborted, hopefully already reported.

            var fileLocator = _bookSelection.CurrentSelection.GetFileLocator();
            var root = fileLocator.LocateDirectoryWithThrow("Readium");

            // Not sure if we will need this. The current UI does not appear to have a way to indicate whether
            // we have a talking book, a book without audio, or one that has audio but it is not being published.
            //var audioSituationClass = "noAudioAvailable";
            //if (EpubMaker.PublishWithoutAudio)
            //	audioSituationClass = "haveAudioButNotMakingTalkingBook";
            //else if (BookHasAudio)
            //	audioSituationClass = "isTalkingBook";

            var rtl = _bookSelection.CurrentSelection.BookData.Language1.IsRightToLeft;

            var targetFile = Path.Combine(root, rtl ? "indexRtl.html" : "index.html");
            var manifestPath = ReadiumManifest.MakeReadiumManifest(StagingDirectory);

            var iframeSource =
                targetFile.ToLocalhost()
                + "?url="
                + UrlPathString.CreateFromUnencodedString(manifestPath.ToLocalhost()).UrlEncoded;
            return iframeSource;
        }

        public void UpdateAndSave(
            EpubSettings newSettings,
            string path,
            bool force,
            WebSocketProgress progress = null
        )
        {
            bool succeeded;
            do
            {
                lock (this)
                {
                    succeeded = UpdatePreview(newSettings, force, progress);
                    if (succeeded)
                    {
                        EpubMaker.SaveEpub(path, _progress);
                        _webSocketServer.SendString(
                            kWebsocketContext,
                            kWebsocketEventId_epubReady,
                            _previewSrc
                        );
                    }
                }
            } while (!succeeded && !EpubMaker.AbortRequested); // try until we get a complete epub, not interrupted by user changing something.
        }

        private EpubSettings _lastPreviewSettings = null;

        public bool UpdatePreview(
            EpubSettings newSettings,
            bool force,
            WebSocketProgress progress = null
        )
        {
            _progress = progress ?? _standardProgress.WithL10NPrefix("PublishTab.Epub.");

            if (Program.RunningOnUiThread)
            {
                throw new ApplicationException(@"Must not attempt to make epubs on UI thread");
            }

            // For some unknown reason, if the accessibility window is showing, some of the browser navigation
            // that is needed to accurately determine which content is visible simply doesn't happen.
            // It would be disconcerting if it popped to the top after we close it and reopen it.
            // So, we just close the window if it is showing when we do this. See BL-7807.
            // Except that opening the Ace Checker tab invokes this code path in a way that works without the
            // deadlock (or whatever causes the failure).  This call can be detected by the progress argument not
            // being null.  The Refresh button on the AccessibilityCheckWindow also uses this code path in the
            // same way, so the next two lines also allow that Refresh button to work.  See BL-9341 for why
            // the original fix is inadequate.
            if (progress == null)
                AccessibilityChecker.AccessibilityCheckWindow.StaticClose();

            _webSocketServer.SendString(kWebsocketContext, "startingEbookCreation", _previewSrc);

            var htmlPath = _bookSelection.CurrentSelection.GetPathHtmlFile();
            var newVersion = Book.Book.ComputeHashForAllBookRelatedFiles(htmlPath);
            var previewIsAlreadyCurrent =
                !newSettings.RequiresDifferentPreviewThan(_lastPreviewSettings)
                && newVersion == _bookVersion
                && EpubMaker != null
                && !EpubMaker.AbortRequested
                && !force;

            if (previewIsAlreadyCurrent)
            {
                return true; // preview is already up to date.
            }

            // newSettings is typically the actual settigns object on the book, which could get updated by the UI.
            // To be able to tell later whether it changed, we need a copy.
            _lastPreviewSettings = newSettings.Clone();

            // clear the obsolete preview, if any; this also ensures that when the new one gets done,
            // we will really be changing the src attr in the preview iframe so the display will update.
            _webSocketServer.SendEvent(kWebsocketContext, kWebsocketEventId_epubReady);
            _bookVersion = newVersion;
            _progress.Message("PreparingPreview", "Preparing Preview");

            // This three-tries loop is an attempt to recover from a weird state the system sometimes gets into
            // where a browser won't navigate to a temporary page that the EpubMaker uses. I'm not sure it actually
            // helps, once the system gets into this state even a brand new browser seems to have the same problem.
            // Usually there will be no exception, and the loop breaks at the end of the first iteration.
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (_tabSelection.ActiveTab != WorkspaceTab.publish)
                    {
                        return false;
                    }
                    _previewSrc = UpdateEpubControlContent();
                }
                catch (ApplicationException ex)
                {
                    Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);

                    if (i >= 2)
                        throw;
                    _progress.MessageWithoutLocalizing(
                        "Something went wrong, trying again",
                        ProgressKind.Error
                    );
                    continue;
                }

                break; // normal case, no exception
            }

            if (EpubMaker.AbortRequested)
                return false; // the code that set the abort flag will request a new preview.

            _progress.Message("Done", "Done");
            return true;
        }
    }
}
