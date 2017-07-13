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
		private readonly ReaderBookPublisher _readerBookPublisher;
		private readonly BloomWebSocketServer _webSocketServer;

		public PublishToAndroidApi(CollectionSettings collectionSettings, BloomWebSocketServer bloomWebSocketServer)
		{
			_webSocketServer = bloomWebSocketServer;
			var progress = new WebSocketProgress(_webSocketServer);
			_readerBookPublisher = new ReaderBookPublisher(progress);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart, request =>
			{
				var pathTail = request.LocalPath().Replace("api/" + kApiUrlPart, "");
				switch (pathTail)
				{
					case "connectUsb":
						_webSocketServer.Send(kWebsocketStateId, "TryingToConnect");
						_readerBookPublisher.Connected += OnConnected;
						_readerBookPublisher.ConnectionFailed += OnConnectionFailed;
						_readerBookPublisher.Connect();
						request.SucceededDoNotNavigate();
						break;

					case "connectUsb/cancel":
						_readerBookPublisher.CancelConnect();
						request.Succeeded();
						break;

					case "connectWifi":
						// not implemented
						break;

					case "sendBook":
						_webSocketServer.Send(kWebsocketStateId, "Sending");
						request.Succeeded();
						if (_readerBookPublisher.SendBook(server.CurrentBook))
							_webSocketServer.Send(kWebsocketStateId, "ReadyToSend");
						else
							_webSocketServer.Send(kWebsocketStateId, "ReadyToConnect");
						break;
				}
			}, true);
		}

		private void OnConnectionFailed(object sender, EventArgs args)
		{
			_readerBookPublisher.ConnectionFailed -= OnConnectionFailed;
			_webSocketServer.Send(kWebsocketStateId, "ReadyToConnect");
		}

		private void OnConnected(object sender, EventArgs args)
		{
			_readerBookPublisher.Connected -= OnConnected;
			_webSocketServer.Send(kWebsocketStateId, "ReadyToSend");
		}
	}
}
