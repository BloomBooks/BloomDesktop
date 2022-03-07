using System;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Publish.Android;

namespace Bloom.Publish.Video
{
	/// <summary>
	/// API calls starting with publish/video, used in the Publish panel for Video
	/// </summary>
	public class PublishToVideoApi
	{
		private readonly BloomWebSocketServer _webSocketServer;
		// It's slightly weird that we use one of these, but the video is done by means
		// of an android-type book preview, and this class knows all about how to make one.
		private PublishToAndroidApi _publishToAndroidApi;
		private const string kApiUrlPart = "publish/video/";
		private RecordVideoWindow _recordVideoWindow;
		private string _videoFormat = "facebook";
		string _pageReadTime = "3.0";
		private string _settingsFromPreview;

		public PublishToVideoApi(BloomWebSocketServer bloomWebSocketServer, PublishToAndroidApi publishToAndroidApi)
		{
			_webSocketServer = bloomWebSocketServer;
			_publishToAndroidApi = publishToAndroidApi;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "recordVideo", request =>
			{
				RecordVideo(request);
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "soundLog", request =>
			{
				var soundLog = request.RequiredPostJson();
				_recordVideoWindow.StopRecording(soundLog);
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "playVideo", request =>
			{
				_recordVideoWindow.PlayVideo();
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "pageReadTime", request =>
			{
				_pageReadTime = request.RequiredPostString();
				_recordVideoWindow?.SetPageReadTime(_pageReadTime);
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "videoSettings", request =>
			{
				_settingsFromPreview = request.RequiredPostString();
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterBooleanEndpointHandlerExact(kApiUrlPart + "hasActivities",
				request =>
				{
					return request.CurrentBook.HasActivities;
				},
				null, // no write action
				false,
				true); // we don't really know, just safe default

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "format", request =>
			{
				_videoFormat = request.RequiredPostString();
				_recordVideoWindow?.SetFormat(_videoFormat, request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape);
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "startRecording", request =>
			{
				_recordVideoWindow?.StartFfmpeg();
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "tooBigForScreenMsg",
				request =>
				{
					request.ReplyWithText(RecordVideoWindow.GetDataForFormat(_videoFormat, request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape,
						out _, out _, out _));
				},
				 true, // has to be on UI thread because it uses Bloom's main window to find the right screen
				false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "saveVideo", request =>
			{
				if (_recordVideoWindow == null)
				{
					// This shouldn't be possible, but just in case, we'll kick off the recording now.
					RecordVideo(request);
				}
				// If we bring up the dialog inside this API call, it may time out.
				Application.Idle += SaveVideoOnIdle;
				request.PostSucceeded();
			}, true, false);


			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "updatePreview", request =>
			{
				_publishToAndroidApi.MakeBloompubPreview(request, true);
			}, false);
		}

		private void RecordVideo(ApiRequest request)
		{
			_recordVideoWindow = new RecordVideoWindow(_webSocketServer);
			_recordVideoWindow.SetFormat(_videoFormat, request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape);
			_recordVideoWindow.SetPageReadTime(_pageReadTime);
			_recordVideoWindow.SetVideoSettingsFromPreview(_settingsFromPreview);
			_recordVideoWindow.Closed += (sender, args) =>
			{
				if (!_recordVideoWindow.GotFullRecording)
				{
					_recordVideoWindow.Cleanup();
					_recordVideoWindow = null;
				}
			};
			_recordVideoWindow.Show(PublishToAndroidApi.PreviewUrl, request.CurrentBook.FolderPath);
		}

		public void AbortMakingVideo()
		{
			if (_recordVideoWindow != null)
			{
				_recordVideoWindow.Close();
				_recordVideoWindow.Cleanup();
				_recordVideoWindow = null;
			}
		}

		private void SaveVideoOnIdle(object sender, EventArgs e)
		{
			Application.Idle -= SaveVideoOnIdle;
			_recordVideoWindow.SaveVideo();
		}
	}
}
