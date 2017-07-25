using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
		private UDPListener m_listener;

		public PublishToAndroidApi(CollectionSettings collectionSettings, BloomWebSocketServer bloomWebSocketServer)
		{
			_webSocketServer = bloomWebSocketServer;
			var progress = new WebSocketProgress(_webSocketServer);
			_bloomReaderPublisher = new BloomReaderPublisher(progress);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "connectUsb/start", ConnectUsbStartHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "connectWiFi/start", request => ConnectWiFiStartHandler(server, request), true);
			server.RegisterEndpointHandler(kApiUrlPart + "connect/cancel", ConnectCancelHandler, true);
			// Not yet
			//server.RegisterEndpointHandler(kApiUrlPart + "connectWifi/start", ConnectWiFiStartHandler, true);
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
			// This listens for a BloomReader to request a book.
			// It requires a firewall hole allowing Bloom to receive messages on _portToListen.
			// We initialize it before starting the Advertiser to avoid any chance of a race condition
			// where a BloomReader manages to request an advertised book before we start the listener.
			m_listener = new UDPListener();
			m_listener.NewMessageReceived += (sender, args) =>
			{
				var androidIpAddress = Encoding.UTF8.GetString(args.data);
				SendBookTo(server.CurrentBook, androidIpAddress);
			};
			_advertiser = new WiFiAdvertiser();
			_advertiser.BookTitle = server.CurrentBook.Title;
			// Review: not sure this is what we want for a version. Basically, it allows the Android (by saving it) to avoid downloading
			// a book that is exactly what it has already...with the risk that it might miss binary changes to images, if nothing changes
			// in the HTML. However, this doesn't prevent overwriting a newer book with an older one. Another option would be to
			// send the file modify time (as well or instead). Or we can institute some system of versioning books...
			_advertiser.BookVersion = Convert.ToBase64String(SHA256Managed.Create().ComputeHash(Encoding.UTF8.GetBytes(server.CurrentBook.RawDom.OuterXml)));
			_advertiser.Start();

			request.Succeeded();
		}

		private void SendBookTo(Book.Book book, string androidIpAddress)
		{
			_bloomReaderPublisher.SendBookToWiFi(book, androidIpAddress);
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

	/// <summary>
	/// Helper class to listen for a single packet from the Android. Construct an instance to start
	/// listening (on another thread); hook NewMessageReceived to receive a packet each time a client sends it.
	/// </summary>
	class UDPListener
	{
		// must match BloomReader.NewBookListenerService.desktopPort
		// and be different from WiFiAdvertiser.Port and port in BloomReaderPublisher.SendBookToWiFi
		private int _portToListen = 5915;
		Thread _ListeningThread;
		public event EventHandler<MyMessageArgs> NewMessageReceived;
		UdpClient _listener = null;
		private bool _listening;

		//constructor: starts listening.
		public UDPListener()
		{
			_ListeningThread = new Thread(ListenForUDPPackages);
			_ListeningThread.IsBackground = true;
			_ListeningThread.Start();
			_listening = true;
		}

		/// <summary>
		/// Run on a background thread; returns only when done listening.
		/// </summary>
		public void ListenForUDPPackages()
		{
			try
			{
				_listener = new UdpClient(_portToListen);
			}
			catch (SocketException e)
			{
				//do nothing
			}

			if (_listener != null)
			{
				IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 0);

				while (_listening)
				{
					try
					{
						byte[] bytes = _listener.Receive(ref groupEP); // waits for packet from Android.

						//raise event
						NewMessageReceived?.Invoke(this, new MyMessageArgs(bytes));
					}
					catch (Exception e)
					{
						Console.WriteLine(e.ToString());
					}
				}
			}

		}
		public void StopListener()
		{
			if (_listening)
			{
				_listening = false;
				_listener.Close(); // forcibly end communication
			}
		}
	}

	/// <summary>
	/// Helper class to hold the data we got from the Android, for the NewMessageReceived event of UDPListener
	/// </summary>
	class MyMessageArgs : EventArgs
	{
		public byte[] data { get; set; }

		public MyMessageArgs(byte[] newData)
		{
			data = newData;
		}
	}
}
