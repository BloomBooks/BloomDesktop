using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using Bloom.Book;
using Bloom.Collection;
using Bloom.web;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Publish.Android.wifi
{
	/// <summary>
	/// Runs a service on the local net that advertises a book and then delivers it to Androids that request it
	/// </summary>
	public class WiFiPublisher
	{
		private readonly WebSocketProgress _progress;
		private WiFiAdvertiser _wifiAdvertiser;
		private BloomReaderUDPListener _wifiListener;
		public const string ProtocolVersion = "1.0";

		public WiFiPublisher(WebSocketProgress progress)
		{
			_progress = progress.WithL10NPrefix("Publish.Android.Wifi.Progress.");
		}

		public void Start(Book.Book book, CollectionSettings collectionSettings)
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
					SendBookOverWiFi(book, androidIpAddress, androidName);
				}
				// If there's something wrong with the JSON (maybe an obsolete or newer version of reader?)
				// just ignore the request.
				catch (Exception ex) when (ex is JsonReaderException || ex is JsonSerializationException)
				{
					_progress.Error(id: "BadBookRequest",
						message: "Got a book request we could not process. Possibly the device is running an incompatible version of BloomReader?");

					//this is too technical/hard to translate
					_progress.ErrorWithoutLocalizing($" Request contains {json}; trying to interpret as JSON we got {ex.Message}");
				}
			};

			_wifiAdvertiser = new WiFiAdvertiser(_progress)
			{
				BookTitle = BookStorage.SanitizeNameForFileSystem(book.Title), // must be the exact same name as the file we will send if requested
				TitleLanguage = collectionSettings.Language1Iso639Code,
				BookVersion = Book.Book.MakeVersionCode(File.ReadAllText(book.GetPathHtmlFile()))
			};

			_wifiAdvertiser.Start();

			_progress.Message(id: "WifiInstructions1",
				message:"On the Android, run Bloom Reader, open the menu and choose 'Receive Books from WiFi'.");
			_progress.Message(id: "WifiInstructions2",
				message:"You can do this on as many devices as you like. Make sure each device is connected to the same network as this computer.");
		}

		// Review: not sure this is what we want for a version. Basically, it allows the Android (by saving it) to avoid downloading
		// a book that is exactly what it has already...with the risk that it might miss binary changes to images, if nothing changes
		// in the HTML. However, this doesn't prevent overwriting a newer book with an older one. Another option would be to
		// send the file modify time (as well or instead). Or we can institute some system of versioning books...

		public void Stop()
		{
			if(_wifiAdvertiser != null)
			{
				_wifiAdvertiser.Stop();
				_wifiAdvertiser.Dispose();
				_wifiAdvertiser = null;
			}
			if(_wifiListener != null)
			{
				_wifiListener.StopListener();
				_wifiListener = null;
			}
		}

		/// <summary>
		/// Send the book to a client over local network, typically WiFi (at least on Android end).
		/// This is currently called on the UDPListener thread.
		/// Enhance: if we spin off another thread to do the transfer, especially if we create the file
		/// and read it into memory once and share the content, we can probably serve multiple
		/// requesting devices much faster. Currently, we are only handling one request at a time,
		/// since we don't return to the listening loop until we finish this request.
		/// Haven't tested what will happen if the user switches away from the Android tab while a transfer
		/// is in progress. I _think_ the thread will continue and complete the request. Quitting Bloom
		/// is likely to leave the transfer incomplete.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="androidIpAddress"></param>
		/// <param name="androidName"></param>
		private void SendBookToClientOnLocalSubNet(Book.Book book, string androidIpAddress, string androidName)
		{
			var androidHttpAddress = "http://" + androidIpAddress + ":5914"; // must match BloomReader SyncServer._serverPort.
			var safeName = BookStorage.SanitizeNameForFileSystem(book.Title);

			_progress.MessageWithParams(id: "Sending",
				comment: "{0} is the name of the book, {1} is the name of the device",
				message: "Sending \"{0}\" to device {1}",
				parameters: new object[] {safeName,androidName});

			var publishedFileName = safeName + BookCompressor.ExtensionForDeviceBloomBook;
			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, book);
				using (WebClient myClient = new WebClient())
				{
					myClient.UploadData(androidHttpAddress + "/putfile?path=" + Uri.EscapeDataString(safeName) +
										BookCompressor.ExtensionForDeviceBloomBook, File.ReadAllBytes(bloomdTempFile.Path));
					myClient.UploadData(androidHttpAddress + "/notify?message=transferComplete", new byte[] { 0 });
				}
			}
			_progress.MessageWithParams(id: "Finished",
				comment: "{0} is the name of the book, {1} is the name of the device",
				message: "Finished sending \"{0}\" to device {1}",
				parameters: new object[] {safeName, androidName});
		}

		private void SendBookOverWiFi(Book.Book book, string androidIpAddress, string androidName)
		{
			try
			{
				SendBookToClientOnLocalSubNet(book, androidIpAddress, androidName);
			}
			catch (Exception e)
			{
				// This method is called on a background thread in response to receiving a request from Bloom Reader.
				// Exceptions somehow get discarded, so there is no point in letting them propagate further.
				_progress.Error(id: "Failed",
					message: "Sending the book failed. Possibly the device was disconnected? If you can't see a "
							+"reason for this the following may be helpful to report to the developers:");
				_progress.Exception(e);
				Debug.Fail("got exception " + e.Message + " sending book");
			}
		}
	}
}
