using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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

		public PublishToVideoApi(BloomWebSocketServer bloomWebSocketServer, PublishToAndroidApi publishToAndroidApi)
		{
			_webSocketServer = bloomWebSocketServer;
			_publishToAndroidApi = publishToAndroidApi;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "recordVideo", request =>
			{
				RecordVideo(request);
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "soundLog", request =>
			{
				var soundLog = request.RequiredPostJson();
				_recordVideoWindow.StopRecording(soundLog);
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "playVideo", request =>
			{
				_recordVideoWindow.PlayVideo();
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "pageTurnDelay", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithText((request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PageTurnDelayDouble).ToString());
				}
				else
				{
					request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PageTurnDelayDouble = double.Parse(request.RequiredPostString());
					request.CurrentBook.BookInfo.SavePublishSettings();
					_recordVideoWindow?.SetPageReadTime(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PageTurnDelayDouble.ToString());
					request.PostSucceeded();
				}
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "videoSettings", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithText(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PlayerSettings ?? "");
				}
				else
				{
					request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PlayerSettings = request.RequiredPostString();
					request.CurrentBook.BookInfo.SavePublishSettings();
					request.PostSucceeded();
				}
			}, true, false);

			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "hasActivities",
				request =>
				{
					return request.CurrentBook.HasActivities;
				},
				null, // no write action
				false,
				true); // we don't really know, just safe default

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "format", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithText(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format);
				}
				else
				{
					request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format = request.RequiredPostString();
					request.CurrentBook.BookInfo.SavePublishSettings();
					_recordVideoWindow?.SetFormat(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format,
						request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape);

					request.PostSucceeded();
				}
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "startRecording", request =>
			{
				_recordVideoWindow?.StartFfmpeg();
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "tooBigForScreenMsg",
				request =>
				{
					request.ReplyWithText(RecordVideoWindow.GetDataForFormat(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format,
						request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape,
						out _, out _, out _));
				},
				 true, // has to be on UI thread because it uses Bloom's main window to find the right screen
				false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "saveVideo", request =>
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


			apiHandler.RegisterEndpointHandler(kApiUrlPart + "updatePreview", request => { UpdatePreview(request); }, false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "displaySettings", request =>
			{
				Process.Start("desk.cpl");
				request.PostSucceeded();
			}, false);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "isScalingActive",
				request => IsScalingActive(),
			null, true);

			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "motionBookMode",
				readRequest =>
				{
					// If the user has taken off all possible motion, force not having motion in the
					// Bloom Reader book.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7680.
					if (!readRequest.CurrentBook.HasMotionPages)
						readRequest.CurrentBook.BookInfo.PublishSettings.AudioVideo.Motion = false;
					return readRequest.CurrentBook.BookInfo.PublishSettings.AudioVideo.Motion;
				},
				(writeRequest, value) =>
				{
					writeRequest.CurrentBook.BookInfo.PublishSettings.AudioVideo.Motion = value;
					writeRequest.CurrentBook.BookInfo.SavePublishSettings();
					_webSocketServer.SendEvent("publish", "motionChanged");
					UpdatePreview(writeRequest);
				}
				, true, allowWriteActionToPostResult: true);
		}

		private void UpdatePreview(ApiRequest request)
		{
			_publishToAndroidApi.MakeBloompubPreview(request, true);
		}

		private bool IsScalingActive()
		{
			// There may be something comparable to do on Linux, but if so, it certainly won't use the
			// Windows DLL external methods this function uses.
			if (SIL.PlatformUtilities.Platform.IsLinux)
				return false;
			// If we can't use this function, we just won't bother with a warning about scaling.
			// Hopefully not many older systems have high-DPI monitors.
			if (!CanUseSetThreadDpiAwarenesPerMonitorV2())
				return false;

			var scaledWidth = Screen.PrimaryScreen.Bounds.Width;
			int bloomScaledWidth = scaledWidth;
			var mainWindow = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			if (mainWindow != null)
			{
				bloomScaledWidth = Screen.FromControl(mainWindow).Bounds.Width;
			}

			var originalAwareness = SetThreadDpiAwarenessContext(ThreadDpiAwareContext.PerMonitorAwareV2);
			try
			{
				// In my testing, this did NOT give the real width, but the scaledWidth.
				// Leaving it in in case there may be some combination of monitor settings
				// where it indicates a difference, because I think we may well have a problem
				// if the main monitor is scaled, even if the one Bloom is on is not.
				// If we determine that we definitely need to check this screen as well as the
				// one where the Bloom Window is, it may work to make a dummy window while in
				// this thread mode, put it on that screen, and then use Screen.FromControl on that.
				// Yet another approach would be to maximize the dummy window and then get its size.
				if (Screen.PrimaryScreen.Bounds.Width != scaledWidth)
					return true;
				// We definitely have a problem if the screen that the preview will be on,
				// the same one as Bloom, is scaled.
				if (mainWindow != null && Screen.FromControl(mainWindow).Bounds.Width != bloomScaledWidth)
					return true;
			}
			finally
			{
				SetThreadDpiAwarenessContext(originalAwareness);
			}

			return false;
		}

		private static bool CanUseSetThreadDpiAwarenesPerMonitorV2()
		{
			// Create a reference to the OS version of Windows 10 Creators Update.
			// This is the first version of Windows that can use SetThreadDpiAwarenessContext
			Version OsMinVersion = new Version(10, 0, 15063, 0);
			return Environment.OSVersion.Version.CompareTo(OsMinVersion) >= 0;
		}

		// Possible values for SetThreadDpiAwarenessContext
		enum ThreadDpiAwareContext : int
		{
			Invalid = 0,
			Unaware = -1,
			SystemAware = -2,
			PerMonitorAware = -3,
			/* Fails if used before Creators Update. */
			PerMonitorAwareV2 = -4
		}

		// Use with care...Windows only! And the option we want to use only works after the 'creators update'
		[DllImport("user32.dll")]
		static extern ThreadDpiAwareContext SetThreadDpiAwarenessContext(PublishToVideoApi.ThreadDpiAwareContext newContext);

		private void RecordVideo(ApiRequest request)
		{
			_recordVideoWindow = RecordVideoWindow.Create(_webSocketServer);
			_recordVideoWindow.SetFormat(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format,
				request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape);
			_recordVideoWindow.SetPageReadTime(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PageTurnDelayDouble.ToString());
			_recordVideoWindow.SetVideoSettingsFromPreview(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PlayerSettings);
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
