using System;
using System.Security.Cryptography;
using System.Text;
using Bloom.Collection;
using Bloom.Publish;
using Bloom.web;

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
		private WiFiAdvertiser _advertiser;
		private BloomReaderUDPListener m_listener;
		private WebSocketProgress _progress;

		public PublishToAndroidApi(CollectionSettings collectionSettings, BloomWebSocketServer bloomWebSocketServer)
		{
			_webSocketServer = bloomWebSocketServer;
			_progress = new WebSocketProgress(_webSocketServer);
			_bloomReaderPublisher = new BloomReaderPublisher(_progress);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "connectUsb/start", ConnectUsbStartHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "connectWiFi/start", request => ConnectWiFiStartHandler(server, request), true);
			server.RegisterEndpointHandler(kApiUrlPart + "connect/cancel", ConnectCancelHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "sendBook/start", request =>
			{
				_webSocketServer.Send(kWebsocketStateId, "Sending");
				request.Succeeded();
				if (_bloomReaderPublisher.SendBook(server.CurrentBook))
					_webSocketServer.Send(kWebsocketStateId, "ReadyToSend");
				else
					_webSocketServer.Send(kWebsocketStateId, "ReadyToConnect");
			}, true);
		}

		private void ConnectUsbStartHandler(ApiRequest request)
		{
			_webSocketServer.Send(kWebsocketStateId, "TryingToConnect");
			_bloomReaderPublisher.Connected += OnConnected;
			_bloomReaderPublisher.ConnectionFailed += OnConnectionFailed;
			_bloomReaderPublisher.Connect();
			request.SucceededDoNotNavigate();
		}

		private void ConnectWiFiStartHandler(EnhancedImageServer server, ApiRequest request)
		{
			if (_advertiser != null)
				return; // repeat clicks do nothing.

			_webSocketServer.Send(kWebsocketStateId, "ServingOnWifi"); // this will change, I suspect, but at least it keeps you from clicking repeatedly

			// This listens for a BloomReader to request a book.
			// It requires a firewall hole allowing Bloom to receive messages on _portToListen.
			// We initialize it before starting the Advertiser to avoid any chance of a race condition
			// where a BloomReader manages to request an advertised book before we start the listener.
			m_listener = new BloomReaderUDPListener();
			m_listener.NewMessageReceived += (sender, args) =>
			{
				var androidIpAddress = Encoding.UTF8.GetString(args.data);
				SendBookTo(server.CurrentBook, androidIpAddress);
			};
			_advertiser = new WiFiAdvertiser(_progress);
			_advertiser.BookTitle = server.CurrentBook.Title;
			_advertiser.TitleLanguage = server.CurrentBook.CollectionSettings.Language1Iso639Code;
			// Review: not sure this is what we want for a version. Basically, it allows the Android (by saving it) to avoid downloading
			// a book that is exactly what it has already...with the risk that it might miss binary changes to images, if nothing changes
			// in the HTML. However, this doesn't prevent overwriting a newer book with an older one. Another option would be to
			// send the file modify time (as well or instead). Or we can institute some system of versioning books...
			_advertiser.BookVersion = Convert.ToBase64String(SHA256Managed.Create().ComputeHash(Encoding.UTF8.GetBytes(server.CurrentBook.RawDom.OuterXml)));
			_advertiser.Start();

			request.SucceededDoNotNavigate();
		}

		private void SendBookTo(Book.Book book, string androidIpAddress)
		{
			_bloomReaderPublisher.SendBookToClientOnLocalSubNet(book, androidIpAddress);
		}

		private void ConnectCancelHandler(ApiRequest request)
		{
			if (_advertiser == null)
			{
				// either closed without pressing any connect button, or used the USB one.
				_bloomReaderPublisher.CancelConnect();
			}
			else {
				// Enhance: should we do something if a transfer is in progress? Here or in Dispose?
				_advertiser.Stop();
				_advertiser.Dispose();
				_advertiser = null;
				m_listener.StopListener();
				m_listener = null;
			}
			request.Succeeded();
		}

		private void OnConnectionFailed(object sender, EventArgs args)
		{
			_bloomReaderPublisher.ConnectionFailed -= OnConnectionFailed;
			_webSocketServer.Send(kWebsocketStateId, "ReadyToConnect");
		}

		private void OnConnected(object sender, EventArgs args)
		{
			_bloomReaderPublisher.Connected -= OnConnected;
			_webSocketServer.Send(kWebsocketStateId, "ReadyToSend");
		}
	}
}
