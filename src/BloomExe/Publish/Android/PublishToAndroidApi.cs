using System.Linq;
using Bloom.Api;
using Bloom.Properties;
using Bloom.Publish.Android.wifi;
using Bloom.web;

namespace Bloom.Publish.Android
{
	/// <summary>
	/// Handles api request dealing with the publishing of books to an Android device
	/// </summary>
	public class PublishToAndroidApi
	{
		private const string kApiUrlPart = "publish/android/";
		private const string kWebsocketStateId = "publish/android/state";
		private readonly WiFiPublisher _wifiPublisher;
		private readonly UsbPublisher _usbPublisher;
		private readonly BloomWebSocketServer _webSocketServer;

		public PublishToAndroidApi(BloomWebSocketServer bloomWebSocketServer)
		{
			_webSocketServer = bloomWebSocketServer;
			var progress = new WebSocketProgress(_webSocketServer);
			_wifiPublisher = new WiFiPublisher(progress);
			_usbPublisher = new UsbPublisher(progress)
			{
				Stopped = () => SetState("stopped")
			};
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			// This is just for storing the user preference of method
			// If we had a couple of these, we could just have a generic preferences api
			// that browser-side code could use.
			server.RegisterEndpointHandler(kApiUrlPart + "method", request =>
			{
				if(request.HttpMethod == HttpMethods.Get)
				{
					var method = Settings.Default.PublishAndroidMethod;
					if(!new string[]{"wifi", "usb", "file"}.Contains(method))
					{
						method = "wifi";
					}
					request.ReplyWithText(method);
				}
				else // post
				{
					Settings.Default.PublishAndroidMethod = request.RequiredPostString();
					request.SucceededDoNotNavigate();
				}
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "usb/start", request =>
			{
				SetState("UsbStarted");
				_usbPublisher.Connect();
				request.SucceededDoNotNavigate();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "usb/stop", request =>
			{
				_usbPublisher.Stop();
				SetState("stopped");
				request.Succeeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "wifi/start", request =>
			{
				_wifiPublisher.Start(request.CurrentBook, request.CurrentCollectionSettings);
				SetState("ServingOnWifi");
				request.SucceededDoNotNavigate();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "wifi/stop", request =>
			{
				_wifiPublisher.Stop();
				SetState("stopped");
				request.Succeeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "file/save", request =>
			{
				FilePublisher.Save(request.CurrentBook);
				SetState("stopped");
				request.Succeeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "cleanup", request =>
			{
				_usbPublisher.Stop();
				_wifiPublisher.Stop();
				SetState("stopped");
				request.Succeeded();
			}, true);
		}

		private void SetState(string state)
		{
			_webSocketServer.Send(kWebsocketStateId, state);
		}
	}
}
