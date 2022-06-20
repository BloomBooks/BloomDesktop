using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.Publish.Android;
using L10NSharp;
using SIL.PlatformUtilities;

namespace Bloom.Publish.Video
{
	/// <summary>
	/// API calls starting with publish/av, used in the Publish panel for Video
	/// </summary>
	public class PublishAudioVideoAPI
	{
		private readonly BloomWebSocketServer _webSocketServer;
		// It's slightly weird that we use one of these, but the video is done by means
		// of an android-type book preview, and this class knows all about how to make one.
		private PublishToAndroidApi _publishToAndroidApi;
		private const string kApiUrlPart = "publish/av/";
		private RecordVideoWindow _recordVideoWindow;

		public PublishAudioVideoAPI(BloomWebSocketServer bloomWebSocketServer, PublishToAndroidApi publishToAndroidApi)
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

			// This is sent directly from BloomPlayer when it gets to the end of making the recording.
			// The player gives Bloom a list of all the sounds it played and their timings so we can
			// merge them into the captured video.
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

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "settings", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					var settings = request.CurrentBook.BookInfo.PublishSettings.AudioVideo;
					var lastPageIndex = request.CurrentBook.GetPages().Count() - 1;
					// this default might be too high but I don't think it will ever be too low.
					// The HTML side will handle it being too high.
					var pageRange = new[] { 0, lastPageIndex };
					if (settings.PageRange!= null && settings.PageRange.Length == 2)
					{
						// I wanted to do something like this as validation, in case the book changed
						// since we saved. But we don't have an accurate page count of the modified version
						// of the book that the range applies to yet. (e.g., switch to device xmatter, strip
						// activities,...
						//pageRange[1] = Math.Min(settings.PageRange[1], lastPageIndex); // not more than the pages we have
						//pageRange[0] = Math.Min(settings.PageRange[0], pageRange[1] - 1); // at least one less than lim
						//if (pageRange[0] < 0)
						//	pageRange[0] = 0;
						//if (pageRange[1] < pageRange[0] + 1)
						//	pageRange[1] = Math.Min(pageRange[0] + 1, lastPageIndex);
						pageRange = settings.PageRange;
					}
					request.ReplyWithJson(new {
						format= settings.Format,
						pageTurnDelay = settings.PageTurnDelayDouble,
						motion = settings.Motion,
						pageRange
					});
				}
				else
				{
					var data = DynamicJson.Parse(request.RequiredPostJson());
					var settings = request.CurrentBook.BookInfo.PublishSettings.AudioVideo;
					var oldMotion = settings.Motion;
					settings.Format = data.format;
					settings.PageTurnDelayDouble = data.pageTurnDelay;
					settings.Motion = data.motion;
					// pageRange also comes in as doubles. "as double[]" does not work, so have to do them individually.
					settings.PageRange = new int[0];
					// Typescript passes an empty array if all pages are selected.
					// This allows us naturally to keep everything selected until the user explicitly changes something.
					if (data.pageRange.IsArray && data.pageRange.Count == 2)
					{
						var start = (int)data.pageRange[0];
						var end = (int)data.pageRange[1];
						settings.PageRange = new[] { start, end };
					}

					request.CurrentBook.BookInfo.SavePublishSettings();

					_recordVideoWindow?.SetPageReadTime(settings.PageTurnDelayDouble.ToString());
					_recordVideoWindow?.SetFormat(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format,
						ShouldRecordAsLandscape(request.CurrentBook), request.CurrentBook.GetLayout());
					if (settings.Motion != oldMotion)
					{
						UpdatePreview(request); // does its own success/fail
					}
					else
					{
						request.PostSucceeded();
					}
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
					request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PlayerSettings =
						request.RequiredPostString();
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

			// Returns true if publish to MP3 is supported for this book, false otherwise.
			// To be eligible to publish to MP3, the book must contain narration audio.
			// (There's not any point to an MP3 if the book has absolutely no audio...
			// Even a book with background music but no narration, there's not too much point to an MP3 either)
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "isMP3FormatSupported", request =>
			{
				// ENHANCE: If desired, you can make it only consider languages in the book that are currently relevant,
				// instead of any language in the book.
				var narrationAudioLangs = request.CurrentBook.GetLanguagesWithAudio();
				return narrationAudioLangs.Any();
			}, null, false, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "startRecording", request =>
			{
				var videoList = request.GetPostStringOrNull();
				var anyVideoHasAudio = _recordVideoWindow?.AnyVideoHasAudio(videoList, request.CurrentBook.FolderPath) ?? false;
				if (anyVideoHasAudio)
				{
					var messageBoxButtons = new[]
					{
						new MessageBoxButton() { Text = LocalizationManager.GetString("Common.Continue","Continue"), Id = "continue" },
						new MessageBoxButton() { Text = LocalizationManager.GetString("Common.Cancel", "Cancel"), Id = "cancel", Default = true }
					};
					if (BloomMessageBox.Show(null,
						    LocalizationManager.GetString("PublishTab.RecordVideo.NoAudioInVideo",
							"Currently, Bloom does not support including audio from Sign Language videos in video output."),
						    messageBoxButtons) == "cancel")
					{
						request.Failed("canceled");
						_recordVideoWindow.Close();
						return;
					}
				}
				_recordVideoWindow?.StartFfmpegForVideoCapture();
				request.PostSucceeded();
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "shouldUseOriginalPageSize",
				request =>
				{
					RecordVideoWindow.GetDataForFormat(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format,
						ShouldRecordAsLandscape(request.CurrentBook), request.CurrentBook.GetLayout(),
						out _, out _, out _, out bool useOriginalPageSize);
					request.ReplyWithBoolean(useOriginalPageSize);
				},
				 true, // has to be on UI thread because it uses Bloom's main window to find the right screen
				false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "tooBigForScreenMsg",
				request =>
				{
					request.ReplyWithText(RecordVideoWindow.GetDataForFormat(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format,
						ShouldRecordAsLandscape(request.CurrentBook), request.CurrentBook.GetLayout(),
						out _, out _, out _, out _));
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

				_recordVideoWindow?.SetBook(request.CurrentBook);

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
		}

		private void UpdatePreview(ApiRequest request)
		{
			_publishToAndroidApi.MakeBloompubPreview(request, true);
		}


		/// <summary>
		/// Return true if the book should be published in landscape mode, that is,
		/// either it is created as a landscape book, or it is a motion book configured
		/// to play in motion mode.
		/// </summary>
		/// <param name="book"></param>
		/// <returns></returns>
		public static bool ShouldRecordAsLandscape(Book.Book book)
		{
			return book.BookInfo.PublishSettings.AudioVideo.Motion || (book.GetLayout().SizeAndOrientation.IsLandScape && !book.GetLayout().SizeAndOrientation.IsSquare);
		}

		private bool IsScalingActive()
		{
			// There may be something comparable to do on Linux, but if so, it certainly won't use the
			// Windows DLL external methods this function uses.
			if (Platform.IsLinux)
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
		static extern ThreadDpiAwareContext SetThreadDpiAwarenessContext(PublishAudioVideoAPI.ThreadDpiAwareContext newContext);

		private void RecordVideo(ApiRequest request)
		{
			_recordVideoWindow = RecordVideoWindow.Create(_webSocketServer);
			_recordVideoWindow.SetFormat(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.Format,
				ShouldRecordAsLandscape(request.CurrentBook), request.CurrentBook.GetLayout());
			_recordVideoWindow.SetPageReadTime(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PageTurnDelayDouble.ToString());
			_recordVideoWindow.SetVideoSettingsFromPreview(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PlayerSettings);
			_recordVideoWindow.SetPageRange(request.CurrentBook.BookInfo.PublishSettings.AudioVideo.PageRange);
			_recordVideoWindow.FinishedProcessingRecording += (sender, args) =>
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
