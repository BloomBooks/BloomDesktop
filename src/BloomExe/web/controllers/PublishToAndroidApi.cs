using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.Collection;
using Bloom.Publish;
using Bloom.web;
using SIL.Code;

namespace Bloom.Api
{
	/// <summary>
	/// Handles api request dealing with the publishing of books to an Android device
	/// </summary>
	class PublishToAndroidApi
	{
		private const string kApiUrlPart = "publish/android/";
		private const string kWebsocketStateId = "publish/android/state";
		private readonly BloomReaderPublisher _bloomReaderPublisher;
		private readonly BloomWebSocketServer _webSocketServer;
		private readonly WebSocketProgress _progress;
		private WiFiAdvertiser _wifiAdvertiser;
		private BloomReaderUDPListener _wifiListener;
		private Book.Book _book;

		public PublishToAndroidApi(CollectionSettings collectionSettings, BloomWebSocketServer bloomWebSocketServer)
		{
			_webSocketServer = bloomWebSocketServer;
			_progress = new WebSocketProgress(_webSocketServer);
			_bloomReaderPublisher = new BloomReaderPublisher(_progress);
			_bloomReaderPublisher.UsbConnected += OnUsbConnected;
			_bloomReaderPublisher.UsbConnectionFailed += OnUsbConnectionFailed;
			_bloomReaderPublisher.SendBookSucceeded += OnSendBookSucceeded;
			_bloomReaderPublisher.SendBookFailed += OnSendBookFailed;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "usb/start", UsbStartHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "usb/stop", StopHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "wifi/start", WiFiStartHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "wifi/stop", StopHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "cleanup", StopHandler, true);
		}

		private void UsbStartHandler(ApiRequest request)
		{
			_book = request.CurrentBook;
			SetState("UsbStarted");
			_bloomReaderPublisher.Connect();
			request.SucceededDoNotNavigate();
		}

		private void WiFiStartHandler(ApiRequest request)
		{
			if (_wifiAdvertiser != null)
			{
				CloseDownWiFiServing();
			}

			// This listens for a BloomReader to request a book.
			// It requires a firewall hole allowing Bloom to receive messages on _portToListen.
			// We initialize it before starting the Advertiser to avoid any chance of a race condition
			// where a BloomReader manages to request an advertised book before we start the listener.
			_wifiListener = new BloomReaderUDPListener();
			_wifiListener.NewMessageReceived += (sender, args) =>
			{
				var androidIpAddress = Encoding.UTF8.GetString(args.data);
				SendBookOverWiFi(request.CurrentBook, androidIpAddress);
			};

			_wifiAdvertiser = new WiFiAdvertiser(_progress)
			{
				BookTitle = request.CurrentBook.Title,
				TitleLanguage = request.CurrentCollectionSettings.Language1Iso639Code,
				BookVersion = MakeVersionCode(File.ReadAllText(request.CurrentBook.GetPathHtmlFile()))
			};
			// Review: not sure this is what we want for a version. Basically, it allows the Android (by saving it) to avoid downloading
			// a book that is exactly what it has already...with the risk that it might miss binary changes to images, if nothing changes
			// in the HTML. However, this doesn't prevent overwriting a newer book with an older one. Another option would be to
			// send the file modify time (as well or instead). Or we can institute some system of versioning books...

			_wifiAdvertiser.Start();

			SetState("ServingOnWifi");
			request.SucceededDoNotNavigate();

			_progress.WriteMessage(
				"On the Android, run Bloom Reader, open the menu and choose 'Receive Books from WiFi'.");
			_progress.WriteMessage("You can do this on as many devices as you like. Make sure each device is connected to the same network as this computer.");
		}

		private void SetState(string state)
		{
			_webSocketServer.Send(kWebsocketStateId, state);
		}

		public static string MakeVersionCode(string fileContent)
		{
			var simplified = fileContent;
			// In general, whitespace sequences are equivalent to a single space.
			// If the user types multiple spaces all but one will be turned to &nbsp;
			simplified = new Regex(@"\s+").Replace(simplified," ");
			// Between the end of one tag and the start of the next white space doesn't count at all
			simplified = new Regex(@">\s+<").Replace(simplified, "><");
			// Page IDs (actually any element ids) are ignored
			// (the bit before the 'id' matches an opening wedge followed by anything but a closing one,
			// and is transferred to the output by $1. Then we look for an id='whatever', with optional
			// whitespace, where (['\"]) matches either kind of opening quote while \2 matches the same one at the end.
			// The question mark makes sure we end with the first possible closing quote.
			// Then we grab everything up to the closing wedge and transfer that to the output as $3.)
			simplified = new Regex("(<[^>]*)\\s*id\\s*=\\s*(['\"]).*?\\2\\s*([^>]*>)").Replace(simplified, "$1$3");
			var bytes = Encoding.UTF8.GetBytes(simplified);
			return Convert.ToBase64String(SHA256Managed.Create().ComputeHash(bytes));
		}

		private void SendBookOverWiFi(Book.Book book, string androidIpAddress)
		{
			try
			{
				_bloomReaderPublisher.SendBookToClientOnLocalSubNet(book, androidIpAddress);
			}
			catch (Exception e)
			{
				// This method is called on a background thread in response to receiving a request from Bloom Reader.
				// Exceptions somehow get discarded, so there is no point in letting them propagate further.
				_progress.WriteError("Sending the book failed. Possibly the device was disconnected? If you can't see a reason for this the following may be helpful to report to the developers:");
				_progress.WriteError(e.Message);
				_progress.WriteError(e.StackTrace);
				Debug.Fail("got exception " + e.Message + " sending book");
			}
		}

		private void StopHandler(ApiRequest request)
		{
			if (_wifiAdvertiser == null)
			{
				// either closed without pressing any connect button, or used the USB one.
				_bloomReaderPublisher.CancelConnect();
			}
			else
			{
				// Enhance: should we do something if a transfer is in progress? Here or in Dispose?
				CloseDownWiFiServing();
			}
			SetState("stopped");
			request.Succeeded();
		}

		private void CloseDownWiFiServing()
		{
			_wifiAdvertiser.Stop();
			_wifiAdvertiser.Dispose();
			_wifiAdvertiser = null;
			_wifiListener.StopListener();
			_wifiListener = null;
		}

		private void OnUsbConnectionFailed(object sender, EventArgs args)
		{
			SetState("stopped");
		}

		private void OnUsbConnected(object sender, EventArgs args)
		{
			Guard.AgainstNull(_book, "_book");
			_bloomReaderPublisher.SendBookAsync(_book);
		}

		private void OnSendBookSucceeded(object sender, EventArgs args)
		{
			SetState("stopped");
		}

		private void OnSendBookFailed(object sender, EventArgs args)
		{
			SetState("stopped");
		}
	}
}
