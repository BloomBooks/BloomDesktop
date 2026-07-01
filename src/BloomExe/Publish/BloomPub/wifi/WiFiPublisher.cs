using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using Bloom.web;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// Runs a service on the local net that advertises a book and then delivers it to Androids that request it
    /// </summary>
    public class WiFiPublisher
    {
        private readonly BookServer _bookServer;
        private readonly WebSocketProgress _progress;
        private WiFiAdvertiser _wifiAdvertiser;
        private BloomReaderUDPListener _wifiListener;
        public const string ProtocolVersion = "2.0";

        // A single shared HttpClient is the recommended pattern; reusing it avoids socket exhaustion.
        // We give it no timeout (rely on cancellation instead), since sending a book over local WiFi
        // can take a while and we don't want to abort a legitimate, slow transfer.
        private static readonly HttpClient s_defaultHttpClient = new HttpClient
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        // Defaults to the shared client; tests substitute one backed by a fake handler.
        private HttpClient _httpClient = s_defaultHttpClient;

        // This indicates a send (in StartSendBookToClientOnLocalSubNet) is in progress: it is non-null
        // only for the duration of a send, being cleared when the send completes (or is canceled).
        // Its non-null status means we won't start another send. One reason for this is that depending
        // on various network latencies, it is possible for us to get another request from the same
        // device to which we are already sending. Trying to send the same thing to the same device
        // twice at the same time does not work well. Canceling this token aborts an in-progress send.
        private CancellationTokenSource _wifiSendCancellation;

        // Test seam: lets tests inject an HttpClient with a fake handler so the upload can be
        // exercised without a real device.
        internal void SetHttpClientForTests(HttpClient client)
        {
            _httpClient = client;
        }

        // Test seam: lets tests set/inspect the in-progress indicator without going through SendBook.
        // Locked so the seam follows the same discipline as every production access of the field.
        internal CancellationTokenSource WifiSendCancellationForTests
        {
            get
            {
                lock (this)
                    return _wifiSendCancellation;
            }
            set
            {
                lock (this)
                    _wifiSendCancellation = value;
            }
        }

        // Test seam: lets tests set/inspect the advertiser so they can verify Paused handling
        // without starting a real advertisement.
        internal WiFiAdvertiser WifiAdvertiserForTests
        {
            get { return _wifiAdvertiser; }
            set { _wifiAdvertiser = value; }
        }

        public WiFiPublisher(WebSocketProgress progress, BookServer bookServer)
        {
            _bookServer = bookServer;
            _progress = progress.WithL10NPrefix("PublishTab.Android.Wifi.Progress.");
        }

        public void Start(
            Book.Book book,
            CollectionSettings collectionSettings,
            Color backColor,
            BloomPubPublishSettings publishSettings = null
        )
        {
            if (_wifiAdvertiser != null)
            {
                Stop();
            }

            // This listens for a BloomReader to request a book.
            // It requires a firewall hole allowing Bloom to receive messages on _portToListen.
            // We initialize it before starting the Advertiser to avoid any chance of a race condition
            // where a BloomReader manages to request an advertised book before we start the listener.
            _wifiListener = new BloomReaderUDPListener();
            _wifiListener.NewMessageReceived += (sender, args) =>
            {
                var json = Encoding.UTF8.GetString(args.Data);
                try
                {
                    dynamic settings = JsonConvert.DeserializeObject(json);
                    // The property names used here must match the ones in BloomReader, doInBackground method of SendMessage,
                    // a private class of NewBookListenerService.
                    var androidIpAddress = (string)settings.deviceAddress;

                    var androidName = (string)settings.deviceName;
                    // This prevents the device (or other devices) from queuing up requests while we're busy with this one.
                    // In effect, the Android is only allowed to request a retry after we've given up this try at sending.
                    // Of course, there are async effects from network latency. But if we do get another request while
                    // handling this one, we will ignore it, since StartSendBook checks for a transfer in progress.
                    _wifiAdvertiser.Paused = true;
                    StartSendBookToClientOnLocalSubNet(
                        book,
                        androidIpAddress,
                        androidName,
                        backColor,
                        publishSettings
                    );
                    // Returns immediately. But we don't resume advertisements until the async send completes.
                }
                // If there's something wrong with the JSON (maybe an obsolete or newer version of reader?)
                // just ignore the request.
                catch (Exception ex)
                    when (ex is JsonReaderException || ex is JsonSerializationException)
                {
                    _progress.Message(
                        idSuffix: "BadBookRequest",
                        message: "Got a book request we could not process. Possibly the device is running an incompatible version of BloomReader?",
                        progressKind: ProgressKind.Error
                    );

                    //this is too technical/hard to translate
                    _progress.MessageWithoutLocalizing(
                        $" Request contains {json}; trying to interpret as JSON we got {ex.Message}",
                        kind: ProgressKind.Error
                    );
                }
            };

            var pathHtmlFile = book.GetPathHtmlFile();
            _wifiAdvertiser = new WiFiAdvertiser(_progress)
            {
                BookTitle = BookStorage.SanitizeNameForFileSystem(book.Title), // must be the exact same name as the file we will send if requested
                TitleLanguage = book.BookData.Language1.Tag,
                BookVersion = Book.Book.MakeVersionCode(
                    RobustFile.ReadAllText(pathHtmlFile),
                    pathHtmlFile
                ),
            };

            PublishToBloomPubApi.CheckBookLayout(book, _progress);
            _wifiAdvertiser.Start();

            var part1 = LocalizationManager.GetDynamicString(
                appId: "Bloom",
                id: "PublishTab.Android.Wifi.Progress.WifiInstructions1",
                englishText: "On the Android, run Bloom Reader, open the menu and choose 'Receive Books via WiFi'."
            );
            var part2 = LocalizationManager.GetDynamicString(
                appId: "Bloom",
                id: "PublishTab.Android.Wifi.Progress.WifiInstructions2",
                englishText: "You can do this on as many devices as you like. Make sure each device is connected to the same network as this computer."
            );

            // can only have one instruction up at a time, so we concatenate these
            _progress.MessageWithoutLocalizing(part1 + " " + part2, ProgressKind.Instruction);
        }

        public void Stop()
        {
            // Locked to avoid contention with code in the thread that reports a transfer complete,
            // which clears _wifiSendCancellation and tries to restart the advertiser.
            lock (this)
            {
                if (_wifiAdvertiser != null)
                {
                    _wifiAdvertiser.Stop();
                    _wifiAdvertiser.Dispose();
                    _wifiAdvertiser = null;
                }
                if (_wifiSendCancellation != null)
                {
                    _wifiSendCancellation.Cancel();
                    Debug.WriteLine("attempting to cancel send");
                }
            }
            // To avoid leaving a thread around when quitting, try to wait for the sender to cancel or complete.
            // We expect the send's completion continuation to clear _wifiSendCancellation (the cancel above
            // triggers that completion).
            for (int i = 0; i < 30 && _wifiSendCancellation != null; i++)
            {
                Thread.Sleep(100);
            }
            lock (this)
            {
                if (_wifiSendCancellation != null)
                {
                    // The completion continuation didn't run within our wait. We've already canceled the
                    // token (which aborts the underlying transfer), so stop tracking this send to avoid
                    // leaving a thread that could outlive the application (see BL-5272). We deliberately
                    // do NOT Dispose the CancellationTokenSource here: the continuation owns disposal, and
                    // the request may still hold a registration on the token, so disposing now could race.
                    // Once we null the field, a late continuation will see it's no longer current
                    // (ReferenceEquals fails) and will not disturb any newer send.
                    _wifiSendCancellation = null;
                    Debug.WriteLine("had to force-abandon sender");
                }
            }
            if (_wifiListener != null)
            {
                {
                    _wifiListener.StopListener();
                    _wifiListener = null;
                }
            }
        }

        /// <summary>
        /// Send the book to a client over local network, typically WiFi (at least on Android end).
        /// This is currently called on the UDPListener thread.
        /// Enhance: if we spin off another thread to do the transfer, especially if we create the file
        /// and read it into memory once and share the content, we can probably serve multiple
        /// requesting devices much faster. Currently, we are only handling one request at a time,
        /// since we pause advertising while sending and ignore requests that come in during sending.
        /// If the user switches away from the Android tab while a transfer
        /// is in progress, the thread will continue and complete the request. Quitting Bloom
        /// is likely to leave the transfer incomplete.
        /// </summary>
        private void StartSendBookToClientOnLocalSubNet(
            Book.Book book,
            string androidIpAddress,
            string androidName,
            Color backColor,
            BloomPubPublishSettings settings = null
        )
        {
            CancellationTokenSource cancellation;
            // Locked in case more than one thread at a time can handle incoming packets, though I don't think
            // this is true. Also, Stop() on the main thread cares whether _wifiSendCancellation is null.
            lock (this)
            {
                // We only support one send at a time. If we somehow get more than one request, we ignore the other.
                // The device will retry soon if still listening and we are still advertising.
                if (_wifiSendCancellation != null) // indicates transfer in progress
                    return;
                // now THIS transfer is 'in progress' as far as any thread checking this is concerned.
                cancellation = _wifiSendCancellation = new CancellationTokenSource();
            }
            // Tracks whether the async upload was actually started (and thus a completion continuation
            // attached). It is set inside the synchronous sendAction callback below.
            var uploadStarted = false;
            try
            {
                // Now we actually start the send...but using an async API, so there's no long delay here.
                PublishToBloomPubApi.SendBook(
                    book,
                    _bookServer,
                    destFileName: null,
                    sendAction: (publishedFileName, bloomDPath) =>
                    {
                        UploadToDevice(
                            androidIpAddress,
                            publishedFileName,
                            RobustFile.ReadAllBytes(bloomDPath),
                            cancellation
                        );
                        // From here on, the upload's completion continuation owns clearing the
                        // in-progress state and disposing the CancellationTokenSource.
                        uploadStarted = true;
                        Debug.WriteLine(
                            $"upload started to http://{androidIpAddress}:5914 ({androidName}) for {publishedFileName}"
                        );
                    },
                    _progress,
                    startingMessageFunction: (publishedFileName, bookTitle) =>
                        _progress.GetMessageWithParams(
                            idSuffix: "Sending",
                            comment: "{0} is the name of the book, {1} is the name of the device",
                            message: "Sending \"{0}\" to device {1}",
                            parameters: new object[] { bookTitle, androidName }
                        ),
                    confirmFunction: null,
                    backColor,
                    settings
                );
                // Occasionally preparing a book for sending will, despite our best efforts, result in a different sha.
                // For example, it might change missing or out-of-date mp3 files. In case the sha we just computed
                // is different from the one we're advertising, update the advertisement, so at least subsequent
                // advertisements will conform to the version the device just got.
                _wifiAdvertiser.BookVersion = BloomPubMaker.HashOfMostRecentlyCreatedBook;
                PublishToBloomPubApi.ReportAnalytics("wifi", book);
            }
            catch (Exception e)
            {
                HandleSendSetupFailure(e, cancellation, uploadStarted);
            }
        }

        /// <summary>
        /// Handles an exception thrown while setting up/starting a send. Reports it, and—only if the
        /// upload never actually started—clears the in-progress state and resumes advertising. If the
        /// upload had started, its completion continuation owns the CancellationTokenSource and will do
        /// that cleanup; clearing/disposing here would clobber an in-flight transfer and could let a
        /// second send start concurrently.
        /// </summary>
        internal void HandleSendSetupFailure(
            Exception e,
            CancellationTokenSource cancellation,
            bool uploadStarted
        )
        {
            // Report while the send still counts as 'in progress' (ReportException suppresses itself
            // once the in-progress state has been cleared).
            ReportException(e);
            if (uploadStarted)
                return;
            lock (this)
            {
                if (ReferenceEquals(_wifiSendCancellation, cancellation))
                {
                    ClearWifiSend();
                    if (_wifiAdvertiser != null)
                        _wifiAdvertiser.Paused = false;
                }
                else
                {
                    // This send was abandoned (Stop() gave up waiting for it) and a newer send may be
                    // in progress, so leave the shared state and the advertiser's Paused status alone.
                    // Since the upload never started, no completion continuation exists to dispose our
                    // CancellationTokenSource, so dispose it here.
                    cancellation.Dispose();
                }
            }
        }

        /// <summary>
        /// POSTs the given book bytes to the device's putfile endpoint and wires up completion
        /// handling. The continuation (which runs on a thread-pool thread when the transfer finishes,
        /// faults, or is canceled) reports any error and clears the in-progress state. Unlike the old
        /// WebClient.UploadDataCompleted event, an HttpClient continuation fires reliably, so the
        /// BL-7227 IsBusy/timer hack is no longer needed.
        /// Returns the completion task; production code ignores it, but tests await it.
        /// </summary>
        internal Task UploadToDevice(
            string androidIpAddress,
            string publishedFileName,
            byte[] bytes,
            CancellationTokenSource cancellation
        )
        {
            var androidHttpAddress = "http://" + androidIpAddress + ":5914"; // must match BloomReader SyncServer._serverPort.
            var uri = new Uri(
                androidHttpAddress + "/putfile?path=" + Uri.EscapeDataString(publishedFileName)
            );
            var content = new ByteArrayContent(bytes);
            // The old WebClient sent application/octet-stream by default; set it explicitly so the
            // bytes-on-the-wire to the BloomReader device are guaranteed unchanged by the HttpClient
            // migration (ByteArrayContent sends no Content-Type on its own).
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/octet-stream"
            );
            return _httpClient
                .PostAsync(uri, content, cancellation.Token)
                .ContinueWith(task => WifiSendCompleted(task, content, cancellation));
        }

        // Runs on a thread-pool thread when the upload started above finishes, faults, or is canceled.
        // 'cancellation' is the CancellationTokenSource that belongs to *this* send.
        private void WifiSendCompleted(
            Task<HttpResponseMessage> task,
            ByteArrayContent content,
            CancellationTokenSource cancellation
        )
        {
            content.Dispose();
            // The finally guarantees the state cleanup below runs even if reporting the outcome throws
            // (e.g. the progress channel is being torn down); otherwise the in-progress flag would stay
            // set and the advertiser paused, silently blocking all future sends. This continuation is
            // fire-and-forget in production, so a throw here would not even be observed.
            try
            {
                if (task.IsFaulted)
                {
                    var error = task.Exception?.GetBaseException();
                    // Don't report cancellation: we typically only cancel while shutting down, when it's
                    // too late for a useful report.
                    if (error != null && !(error is OperationCanceledException))
                        ReportException(error);
                }
                else if (!task.IsCanceled)
                {
                    using (var response = task.Result)
                    {
                        if (!response.IsSuccessStatusCode)
                            ReportException(
                                new HttpRequestException(
                                    $"Device returned {(int)response.StatusCode} {response.ReasonPhrase}"
                                )
                            );
                    }
                }
            }
            finally
            {
                // To avoid contention with Stop(), which may try to cancel the send if it finds
                // an existing transfer, and may destroy the advertiser we are trying to restart.
                lock (this)
                {
                    Debug.WriteLine($"upload completed, cancelled is {task.IsCanceled}");
                    // A newer send may have started (e.g. after Stop() abandoned this one) while this
                    // continuation was still pending. Only clear the shared in-progress state and resume
                    // advertising if we are still the current send; otherwise we would wrongly cancel the
                    // newer send's "in progress" status and resume advertising during its transfer.
                    if (ReferenceEquals(_wifiSendCancellation, cancellation))
                    {
                        _wifiSendCancellation = null;
                        if (_wifiAdvertiser != null)
                            _wifiAdvertiser.Paused = false;
                    }
                }
                // This continuation owns its CancellationTokenSource. The request has finished using the
                // token by the time the continuation runs, so disposing here is safe (and is the single
                // place the CTS gets disposed).
                cancellation.Dispose();
            }
        }

        // Disposes and clears the in-progress send. Callers must hold the lock on 'this'.
        // Used only when a send fails during setup, before its completion continuation is attached.
        private void ClearWifiSend()
        {
            if (_wifiSendCancellation != null)
            {
                _wifiSendCancellation.Dispose();
                _wifiSendCancellation = null;
            }
        }

        protected virtual void ReportException(Exception e)
        {
            // If this happens while _wifiSendCancellation is null, it can only be because Stop() tried to
            // abort a transfer. At this point the exception is being reported on an orphan thread, very
            // possibly after Bloom has closed down and the localization manager is disposed.
            // Certainly the _progress thing is no longer visible. So no point in trying to send something
            // there, it will just cause exceptions.
            if (_wifiSendCancellation != null)
            {
                // This method is called on a background thread in response to receiving a request from Bloom Reader.
                // Exceptions somehow get discarded, so there is no point in letting them propagate further.
                _progress.Message(
                    idSuffix: "Failed",
                    message: "There was an error while sending the book. Possibly the device was disconnected? If you can't see a "
                        + "reason for this the following may be helpful to report to the developers:",
                    progressKind: ProgressKind.Error
                );
                _progress.Exception(e);
            }
            Debug.Fail("got exception " + e.Message + " sending book");
        }
    }
}
