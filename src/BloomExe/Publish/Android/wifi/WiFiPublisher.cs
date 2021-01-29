using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Bloom.Book;
using Bloom.Collection;
using Bloom.web;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.Publish.Android.wifi
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
		// This is the web client we use in StartSendBookToClientOnLocalSubNet() to send a book to an android.
		// It is non-null only for the duration of a send, being destroyed in its own UploadDataCompleted
		// event. Thus, its non-null status indicates a transfer is in progress, and we won't start any
		// others. One reason for this is that depending on various network latencies, it is possible
		// for us to get another request from the same device to which we are already sending.
		// Trying to send the same thing to the same device twice at the same time does not work well.
		private WebClient _wifiSender;

		public WiFiPublisher(WebSocketProgress progress, BookServer bookServer)
		{
			_bookServer = bookServer;
			_progress = progress.WithL10NPrefix("PublishTab.Android.Wifi.Progress.");
		}

		public void Start(Book.Book book, CollectionSettings collectionSettings, Color backColor, AndroidPublishSettings publishSettings = null)
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
					var androidIpAddress = (string) settings.deviceAddress;

					var androidName = (string) settings.deviceName;
					// This prevents the device (or other devices) from queuing up requests while we're busy with this one.
					// In effect, the Android is only allowed to request a retry after we've given up this try at sending.
					// Of course, there are async effects from network latency. But if we do get another request while
					// handling this one, we will ignore it, since StartSendBook checks for a transfer in progress.
					_wifiAdvertiser.Paused = true;
					StartSendBookOverWiFi(book, androidIpAddress, androidName, backColor, publishSettings);
					// Returns immediately. But we don't resume advertisements until the async send completes.
				}
				// If there's something wrong with the JSON (maybe an obsolete or newer version of reader?)
				// just ignore the request.
				catch (Exception ex) when (ex is JsonReaderException || ex is JsonSerializationException)
				{
					_progress.Message(idSuffix: "BadBookRequest",
						message: "Got a book request we could not process. Possibly the device is running an incompatible version of BloomReader?",
						kind:MessageKind.Error);

					//this is too technical/hard to translate
					_progress.MessageWithoutLocalizing($" Request contains {json}; trying to interpret as JSON we got {ex.Message}", kind: MessageKind.Error);
				}
			};

			var pathHtmlFile = book.GetPathHtmlFile();
			_wifiAdvertiser = new WiFiAdvertiser(_progress)
			{
				BookTitle = BookStorage.SanitizeNameForFileSystem(book.Title), // must be the exact same name as the file we will send if requested
				TitleLanguage = book.BookData.Language1.Iso639Code,
				BookVersion = Book.Book.MakeVersionCode(File.ReadAllText(pathHtmlFile), pathHtmlFile)
			};

			PublishToAndroidApi.CheckBookLayout(book, _progress);
			_wifiAdvertiser.Start();

			var part1 = LocalizationManager.GetDynamicString(appId: "Bloom", id: "PublishTab.Android.Wifi.Progress.WifiInstructions1",
				englishText: "On the Android, run Bloom Reader, open the menu and choose 'Receive Books from computer'.");
			var part2 = LocalizationManager.GetDynamicString(appId: "Bloom", id: "PublishTab.Android.Wifi.Progress.WifiInstructions2",
				englishText: "You can do this on as many devices as you like. Make sure each device is connected to the same network as this computer.");

			// can only have one instruction up at a time, so we concatenate these
			_progress.MessageWithoutLocalizing(part1+" "+part2, MessageKind.Instruction);

		}

		public void Stop()
		{
			// Locked to avoid contention with code in the thread that reports a transfer complete,
			// which disposes of _wifiSender and tries to restart the advertiser.
			lock (this)
			{
				if (_wifiAdvertiser != null)
				{
					_wifiAdvertiser.Stop();
					_wifiAdvertiser.Dispose();
					_wifiAdvertiser = null;
				}
				if (_wifiSender != null)
				{
					_wifiSender.CancelAsync();
					Debug.WriteLine("attempting async cancel send");
				}
				if (_uploadTimer != null)
				{
					_uploadTimer.Stop();
					_uploadTimer.Dispose();
					_uploadTimer = null;
				}
			}
			// To avoid leaving a thread around when quitting, try to wait for the sender to cancel or complete.
			// We expect another thread to set _wifiSender to null in the UploadDataCompleted event
			// (which is supposed to be triggered also by canceling).
			for (int i = 0; i < 30 && _wifiSender != null; i++)
			{
				Thread.Sleep(100);
			}
			lock (this)
			{
				if (_wifiSender != null)
				{
					// If it's still null we give up on the Cancel and try to shut it down any way we can.
					// Note that if the cancelAsync didn't work, as it generally seems not to, this could
					// cancel a file transfer rather abruptly. But the alternative is to leave the thread
					// running, possibly after Bloom has otherwise exited, causing problems like BL-5272.
					// (In practice even aborting this thread doesn't seem to force the file transfer to
					// stop, nor does anything else I've tried, so we just do our best to make sure
					// the thread won't outlive the application by much. Not allowing requests to queue
					// up is one thing that helps. At worst there's only one in progress that either
					// finishes or aborts before too long.)
					_wifiSender.Dispose();
					_wifiSender = null;
					Debug.WriteLine("had to force dispose sender");
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

		private System.Timers.Timer _uploadTimer;

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
		private void StartSendBookToClientOnLocalSubNet(Book.Book book, string androidIpAddress, string androidName, Color backColor, AndroidPublishSettings settings = null)
		{
			// Locked in case more than one thread at a time can handle incoming packets, though I don't think
			// this is true. Also, Stop() on the main thread cares whether _wifiSender is null.
			lock (this)
			{
				// We only support one send at a time. If we somehow get more than one request, we ignore the other.
				// The device will retry soon if still listening and we are still advertising.
				if (_wifiSender != null) // indicates transfer in progress
					return;
				// now THIS transfer is 'in progress' as far as any thread checking this is concerned.
				_wifiSender = new WebClient();
			}
			_wifiSender.UploadDataCompleted += WifiSenderUploadCompleted;
			// Now we actually start the send...but using an async API, so there's no long delay here.
			PublishToAndroidApi.SendBook(book, _bookServer,
				null, (publishedFileName, bloomDPath) =>
				{
					var androidHttpAddress = "http://" + androidIpAddress + ":5914"; // must match BloomReader SyncServer._serverPort.
					_wifiSender.UploadDataAsync(new Uri(androidHttpAddress + "/putfile?path=" + Uri.EscapeDataString(publishedFileName)), File.ReadAllBytes(bloomDPath));
					Debug.WriteLine($"upload started to http://{androidIpAddress}:5914 ({androidName}) for {publishedFileName}");
				},
				_progress,
				(publishedFileName, bookTitle) => _progress.GetMessageWithParams(idSuffix: "Sending",
					comment: "{0} is the name of the book, {1} is the name of the device",
					message: "Sending \"{0}\" to device {1}",
					parameters: new object[] { bookTitle, androidName }),
				null,
				backColor,
				settings:settings);
			// Occasionally preparing a book for sending will, despite our best efforts, result in a different sha.
			// For example, it might change missing or out-of-date mp3 files. In case the sha we just computed
			// is different from the one we're advertising, update the advertisement, so at least subsequent
			// advertisements will conform to the version the device just got.
			_wifiAdvertiser.BookVersion = BookCompressor.LastVersionCode;
			lock (this)
			{
				// The UploadDataCompleted event handler quit working at Bloom 4.6.1238 Alpha (Windows test build).
				// The data upload still works, but the event handler is *NEVER* called.  Trying to revise the upload
				// by using UploadDataTaskAsync with async/await  did not work any better: the await never happened.
				// To get around this bug, we introduce a timer that periodically checks the IsBusy flag of the
				// _wifiSender object.  It's a hack, but I haven't come up with anything better in two days of
				// looking at this problem.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-7227 for details.
				if (_uploadTimer == null)
				{
					_uploadTimer = new System.Timers.Timer
					{
						Interval = 500.0,
						Enabled = false
					};
					_uploadTimer.Elapsed += (sender, args) =>
					{
						if (_wifiSender != null && _wifiSender.IsBusy)
							return;
						_uploadTimer.Stop();
						Debug.WriteLine("upload timed out, appears to be finished");
						WifiSenderUploadCompleted(_uploadTimer, null);
					};
				}
				_uploadTimer.Start();
			}
			PublishToAndroidApi.ReportAnalytics("wifi", book);
		}

		private void WifiSenderUploadCompleted(object sender, UploadDataCompletedEventArgs args)
		{
			// Runs on the async transfer thread after the transfer initiated above.  (Or on the async
			// timer thread if the completion event handler is not called, as seems to be happening
			// since Bloom 4.6.1238 Alpha according to BL-7227)
			if (args?.Error != null)
			{
				ReportException(args.Error);
			}
			// Should we report if canceled? Thinking not, we typically only cancel while shutting down,
			// it's probably too late for a useful report.

			// To avoid contention with Stop(), which may try to cancel the send if it finds
			// an existing wifiSender, and may destroy the advertiser we are trying to restart.
			lock (this)
			{
				Debug.WriteLine($"upload completed, sender is {sender}, cancelled is {args?.Cancelled}");
				if (_wifiSender != null) // should be null only in a desperate abort-the-thread situation.
				{
					_wifiSender.Dispose();
					_wifiSender = null;
				}
				if (_wifiAdvertiser != null)
					_wifiAdvertiser.Paused = false;
				if (_uploadTimer != null)
				{
					_uploadTimer.Stop();
					_uploadTimer.Dispose();
					_uploadTimer = null;
				}
			}
		}

		private void StartSendBookOverWiFi(Book.Book book, string androidIpAddress, string androidName, Color backColor, AndroidPublishSettings settings = null)
		{
			try
			{
				StartSendBookToClientOnLocalSubNet(book, androidIpAddress, androidName, backColor, settings);
			}
			catch (Exception e)
			{
				ReportException(e);
			}
		}

		private void ReportException(Exception e)
		{
			// If this happens while _wifiSender is null, it can only be because Stop() tried to abort a transfer
			// and CancelAsync didn't work (as usual). At this point the exception is being reported on an orphan thread
			// very possibly after Bloom has closed down and the localization manager is disposed.
			// Certainly the _progress thing is no longer visible. So no point in trying to send something
			// there, it will just cause exceptions.
			if (_wifiSender != null)
			{
				// This method is called on a background thread in response to receiving a request from Bloom Reader.
				// Exceptions somehow get discarded, so there is no point in letting them propagate further.
				_progress.Message(idSuffix: "Failed",
					message: "There was an error while sending the book. Possibly the device was disconnected? If you can't see a "
					         + "reason for this the following may be helpful to report to the developers:",
					kind: MessageKind.Error);
				_progress.Exception(e);
			}
			Debug.Fail("got exception " + e.Message + " sending book");
		}
	}
}
