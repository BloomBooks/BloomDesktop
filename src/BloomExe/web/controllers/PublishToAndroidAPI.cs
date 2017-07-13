using Bloom.Collection;
using Bloom.web;

namespace Bloom.Api
{
	/// <summary>
	/// </summary>
	class PublishToAndroidApi
	{
		private const string kApiUrlPart = "publish/android/";
		private readonly CollectionSettings _collectionSettings;
		private readonly WebSocketProgress _progress;

		public PublishToAndroidApi(CollectionSettings collectionSettings, BloomWebSocketServer bloomWebSocketServer)
		{
			_collectionSettings = collectionSettings;
			_progress = new WebSocketProgress(bloomWebSocketServer);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart+"connectUsb", request =>
			{
				_progress.WriteMessage("Attempting Wifi connection...");
				_progress.WriteMessage("Connected.");
				request.ReplyWithText("ReadyToSend");
			}, true);
			server.RegisterEndpointHandler(kApiUrlPart + "connectWifi", request =>
			{
				for(int i=0;i<10;i++)
					_progress.WriteMessage("I think I can.");
				_progress.WriteMessage("I cannot.");
				_progress.WriteError("Could not connect via network.");
				request.ReplyWithText("ReadyToConnect");
			}, true);
			server.RegisterEndpointHandler(kApiUrlPart + "sendBook/enabled", request =>
			{
				request.ReplyWithText("false");
			}, true);
			server.RegisterEndpointHandler(kApiUrlPart + "sendBook", request =>
			{
				_progress.WriteMessage("Sending...");
				_progress.WriteMessage("You should now see your book in Bloom Reader.");
				request.Succeeded();
			}, true);
		}
	}
}
