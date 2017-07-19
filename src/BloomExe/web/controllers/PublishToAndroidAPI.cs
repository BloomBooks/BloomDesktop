using System;
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

		public PublishToAndroidApi(CollectionSettings collectionSettings, BloomWebSocketServer bloomWebSocketServer)
		{
			_webSocketServer = bloomWebSocketServer;
			var progress = new WebSocketProgress(_webSocketServer);
			_bloomReaderPublisher = new BloomReaderPublisher(progress);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "connectUsb/start", ConnectUsbStartHandler, true);
			server.RegisterEndpointHandler(kApiUrlPart + "connectUsb/cancel", ConnectUsbCancelHandler, true);
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

		private void ConnectUsbCancelHandler(ApiRequest request)
		{
			_bloomReaderPublisher.CancelConnect();
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
