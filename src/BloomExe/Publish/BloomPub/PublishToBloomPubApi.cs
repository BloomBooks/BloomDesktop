using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.Publish.BloomPub.file;
using SIL.Windows.Forms.Miscellaneous;

#if !__MonoCS__
using Bloom.Publish.BloomPub.usb;
#endif
using Bloom.Publish.BloomPub.wifi;
using Bloom.web;
using Bloom.web.controllers;
using DesktopAnalytics;
using SIL.IO;
using Newtonsoft.Json;
using SIL.Reporting;

namespace Bloom.Publish.BloomPub
{
    /// <summary>
    /// Handles api request dealing with the publishing of BloomPUBs
    /// </summary>
    public class PublishToBloomPubApi
    {
        private const string kApiUrlPart = "publish/bloompub/";
        private const string kWebsocketState_EventId = "publish/bloompub/state";
        private readonly WiFiPublisher _wifiPublisher;
#if !__MonoCS__
        private readonly UsbPublisher _usbPublisher;
#endif
        private readonly CollectionSettings _collectionSettings;
        private readonly BloomWebSocketServer _webSocketServer;
        private readonly BookServer _bookServer;
        private readonly BulkBloomPubCreator _bulkBloomPubCreator;
        private readonly WebSocketProgress _progress;

        private PublishApi _publishApi;

        public PublishToBloomPubApi(
            CollectionSettings collectionSettings,
            BloomWebSocketServer bloomWebSocketServer,
            BookServer bookServer,
            BulkBloomPubCreator bulkBloomPubCreator,
            PublishApi publishApi
        )
        {
            _collectionSettings = collectionSettings;
            _webSocketServer = bloomWebSocketServer;
            _bookServer = bookServer;
            _bulkBloomPubCreator = bulkBloomPubCreator;
            _publishApi = publishApi;
            _progress = new WebSocketProgress(_webSocketServer, PublishApi.kWebSocketContext);
            _wifiPublisher = new WiFiPublisher(_progress, _bookServer);
#if !__MonoCS__
            _usbPublisher = new UsbPublisher(_progress, _bookServer)
            {
                Stopped = () => SetState("stopped")
            };
#endif
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // This is just for storing the user preference of method
            // If we had a couple of these, we could just have a generic preferences api
            // that browser-side code could use.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "method",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        var method = Settings.Default.PublishAndroidMethod;
                        if (!new string[] { "wifi", "usb", "file" }.Contains(method))
                        {
                            method = "file";
                        }
                        request.ReplyWithText(method);
                    }
                    else // post
                    {
                        Settings.Default.PublishAndroidMethod = request.RequiredPostString();
#if __MonoCS__
                        if (Settings.Default.PublishAndroidMethod == "usb")
                        {
                            _progress.MessageWithoutLocalizing(
                                "Sorry, this method is not available on Linux yet."
                            );
                        }
#endif
                        request.PostSucceeded();
                    }
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "updatePreview",
                request =>
                {
                    _publishApi.MakeBloompubPreview(request, false);
                },
                false
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "usb/start",
                request =>
                {
#if !__MonoCS__

                    SetState("UsbStarted");
                    var publishSettings = _publishApi.GetSettings();
                    if (
                        _publishApi.IsBookLicenseOK(request.CurrentBook, publishSettings, _progress)
                    )
                    {
                        _usbPublisher.Connect(
                            request.CurrentBook,
                            _publishApi._thumbnailBackgroundColor,
                            publishSettings
                        );
                    }
                    else
                    {
                        SetState("stopped");
                    }
#endif
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "usb/stop",
                request =>
                {
#if !__MonoCS__
                    _usbPublisher.Stop(disposing: false);
                    SetState("stopped");
#endif
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "wifi/start",
                request =>
                {
                    SetState("ServingOnWifi");
                    var publishSettings = _publishApi.GetSettings();
                    if (
                        _publishApi.IsBookLicenseOK(request.CurrentBook, publishSettings, _progress)
                    )
                    {
                        _wifiPublisher.Start(
                            request.CurrentBook,
                            request.CurrentCollectionSettings,
                            _publishApi._thumbnailBackgroundColor,
                            publishSettings
                        );
                    }
                    else
                    {
                        SetState("stopped");
                    }

                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "wifi/stop",
                request =>
                {
                    _wifiPublisher.Stop();
                    SetState("stopped");
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "file/save",
                request =>
                {
                    SetState("SavingFile");
                    var publishSettings = _publishApi.GetSettings();
                    if (
                        _publishApi.IsBookLicenseOK(request.CurrentBook, publishSettings, _progress)
                    )
                    {
                        FilePublisher.Save(
                            request.CurrentBook,
                            _bookServer,
                            _publishApi._thumbnailBackgroundColor,
                            _progress,
                            publishSettings
                        );
                    }
                    SetState("stopped");
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "file/bulkSaveBloomPubsParams",
                request =>
                {
                    request.ReplyWithJson(
                        JsonConvert.SerializeObject(_collectionSettings.BulkPublishBloomPubSettings)
                    );
                },
                true
            );

            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "file/bulkSaveBloomPubs",
                async request =>
                {
                    // update what's in the collection so that we remember for next time
                    _collectionSettings.BulkPublishBloomPubSettings =
                        request.RequiredPostObject<BulkBloomPubPublishSettings>();
                    _collectionSettings.Save();

                    await _bulkBloomPubCreator.PublishAllBooksAsync(
                        _collectionSettings.BulkPublishBloomPubSettings
                    );
                    SetState("stopped");
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "textToClipboard",
                request =>
                {
                    PortableClipboard.SetText(request.RequiredPostString());
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "canRotate",
                request =>
                {
                    return request
                            .CurrentBook
                            .BookInfo
                            .PublishSettings
                            .BloomPub
                            .PublishAsMotionBookIfApplicable && request.CurrentBook.HasMotionPages;
                },
                null, // no write action
                false,
                true
            ); // we don't really know, just safe default

            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "defaultLandscape",
                request =>
                {
                    return request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape;
                },
                null, // no write action
                false,
                true
            ); // we don't really know, just safe default
        }

        public void Dispose()
        {
#if !__MonoCS__
            _usbPublisher.Stop(disposing: true);
#endif
            _wifiPublisher.Stop();
        }

        private void SetState(string state)
        {
            _webSocketServer.SendString(
                PublishApi.kWebSocketContext,
                kWebsocketState_EventId,
                state
            );
        }

        public static void ReportAnalytics(string mode, Book.Book book)
        {
            Analytics.Track(
                "Publish Android",
                new Dictionary<string, string>()
                {
                    { "mode", mode },
                    { "BookId", book.ID },
                    { "Country", book.CollectionSettings.Country },
                    { "Language", book.BookData.Language1.Tag }
                }
            );
            book.ReportSimplisticFontAnalytics(
                FontAnalytics.FontEventType.PublishEbook,
                "bloomPUB, " + mode
            );
        }

        /// <summary>
        /// This is the core of sending a book to a device. We need a book and a bookServer in order to come up
        /// with the .bloompub file.
        /// We are either simply saving the .bloompub to destFileName, or else we will make a temporary .bloompub file and
        /// actually send it using sendAction.
        /// We report important progress on the progress control. This includes reporting that we are starting
        /// the actual transmission using startingMessageAction, which is passed the safe file name (for checking pre-existence
        /// in UsbPublisher) and the book title (typically inserted into the message).
        /// If a confirmAction is passed (currently only by UsbPublisher), we use it check for a successful transfer
        /// before reporting completion (except for file save, where the current message is inappropriate).
        /// This is an awkward case where the three ways of publishing are similar enough that
        /// it's annoying and dangerous to have three entirely separate methods but somewhat awkward to combine them.
        /// Possibly we could eventually make them more similar, e.g., it would simplify things if they all said
        /// "Sending X to Y", though I'm not sure that would be good i18n if Y is sometimes a device name
        /// and sometimes a path.
        /// </summary>
        /// <param name="book"></param>
        /// <param name="destFileName"></param>
        /// <param name="sendAction"></param>
        /// <param name="progress"></param>
        /// <param name="bookServer"></param>
        /// <param name="startingMessageFunction"></param>
        public static void SendBook(
            Book.Book book,
            BookServer bookServer,
            string destFileName,
            Action<string, string> sendAction,
            WebSocketProgress progress,
            Func<string, string, string> startingMessageFunction,
            Func<string, bool> confirmFunction,
            Color backColor,
            BloomPubPublishSettings settings = null
        )
        {
            var bookTitle = book.Title;
            progress.MessageUsingTitle(
                "PackagingBook",
                "Packaging \"{0}\" for use with Bloom Reader...",
                bookTitle,
                ProgressKind.Progress
            );

            // REVIEW: Why is this here in this method? We do a bunch of things to convert a book, but this one thing, audio, was
            // put here instead in BloomReaderFileMaker along with all the other operations.


            // Compress audio if needed, with progress message
            if (AudioProcessor.IsAnyCompressedAudioMissing(book.FolderPath, book.RawDom))
            {
                progress.Message("CompressingAudio", "Compressing audio files");
                AudioProcessor.TryCompressingAudioAsNeeded(book.FolderPath, book.RawDom);
            }
            var publishedFileName =
                Path.GetFileName(book.FolderPath) + BloomPubMaker.BloomPubExtensionWithDot;
            if (startingMessageFunction != null)
                progress.MessageWithoutLocalizing(
                    startingMessageFunction(publishedFileName, bookTitle)
                );
            if (destFileName == null)
            {
                // wifi or usb...make the .bloompub in a temp folder.
                using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
                {
                    BloomPubMaker.CreateBloomPub(
                        settings,
                        bloomdTempFile.Path,
                        book,
                        bookServer,
                        progress
                    );
                    sendAction(publishedFileName, bloomdTempFile.Path);
                    if (confirmFunction != null && !confirmFunction(publishedFileName))
                        throw new ApplicationException(
                            "Book does not exist after write operation."
                        );
                    progress.MessageUsingTitle(
                        "BookSent",
                        "You can now read \"{0}\" in Bloom Reader!",
                        bookTitle,
                        ProgressKind.Note
                    );
                }
            }
            else
            {
                // save file...user has supplied name, there is no further action.
                Debug.Assert(
                    sendAction == null,
                    "further actions are not supported when passing a path name"
                );
                BloomPubMaker.CreateBloomPub(settings, destFileName, book, bookServer, progress);
                progress.Message("PublishTab.Epub.Done", "Done", useL10nIdPrefix: false); // share message string with epub publishing
            }
        }

        /// <summary>
        /// Check for either "Device16x9Portrait" or "Device16x9Landscape" layout.
        /// Complain to the user if another layout is currently chosen.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-5274.
        /// </remarks>
        public static void CheckBookLayout(Bloom.Book.Book book, WebSocketProgress progress)
        {
            var layout = book.GetLayout();
            var desiredLayoutSize = "Device16x9";
            // Books with overlays don't get their layout switched, because it would mess them up too badly
            // So this warning is not appropriate for comics or other overlays. We might one day consider a
            // milder warning along the lines that legibility might suffer, especially if there is
            // a large difference in page size.
            if (
                layout.SizeAndOrientation.PageSizeName != desiredLayoutSize && !book.HasComicalOverlays
            )
            {
                // The progress object has been initialized to use an id prefix.  So we'll access L10NSharp explicitly here.  We also want to make the string blue,
                // which requires a special argument.
                //				var msgFormat = L10NSharp.LocalizationManager.GetString("Common.Note",
                //					"Note", "A heading shown above some messages.");
                //				progress.MessageWithoutLocalizing(msgFormat, ProgressKind.Note);
                var msgFormat = L10NSharp.LocalizationManager.GetString(
                    "PublishTab.Android.WrongLayout.Message",
                    "The layout of this book is currently \"{0}\". Bloom Reader will display it using \"{1}\", which may cause text to scroll. To see if anything needs adjusting, go back to the Edit Tab and change the layout to \"{1}\".",
                    "{0} and {1} are book layout tags."
                );
                var desiredLayout = desiredLayoutSize + layout.SizeAndOrientation.OrientationName;
                var msg = String.Format(
                    msgFormat,
                    layout.SizeAndOrientation.ToString(),
                    desiredLayout,
                    Environment.NewLine
                );
                progress.MessageWithoutLocalizing(msg, ProgressKind.Warning);
            }
        }
    }
}
