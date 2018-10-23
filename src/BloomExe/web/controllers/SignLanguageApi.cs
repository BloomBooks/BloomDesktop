using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using DesktopAnalytics;
using Gecko;
using Gecko.DOM;
using L10NSharp;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.FileSystem;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Handles API calls related to the Sign Language tool (mainly to video).
	/// </summary>
	public class SignLanguageApi
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		public EditingView View { get; set; }
		public EditingModel Model { get; set; }

		public SignLanguageApi(BookSelection bookSelection, PageSelection pageSelection)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
		}


		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("signLanguage/recordedVideo", HandleRecordedVideoRequest, true);
			apiHandler.RegisterEndpointHandler("signLanguage/editVideo", HandleEditVideoRequest, true);
			apiHandler.RegisterEndpointHandler("signLanguage/deleteVideo", HandleDeleteVideoRequest, true);
			apiHandler.RegisterEndpointHandler("signLanguage/restoreOriginal", HandleRestoreOriginalRequest, true);
			apiHandler.RegisterEndpointHandler("signLanguage/importVideo", HandleImportVideoRequest, true);
			apiHandler.RegisterEndpointHandler("signLanguage/getStats", HandleVideoStatisticsRequest, true);
		}

		public Book.Book CurrentBook
		{
			get { return _bookSelection.CurrentSelection; }
		}

		// Request from sign language tool, issued when a complete recording has been captured.
		// It is passed as a binary blob that is the actual content that needs to be made into
		// an mp4 file. (At this point we don't try to handle recordings too big for this approach.)
		// We make a file (with an arbitrary guid name) and attempt to make it the recording for the
		// first page element with class bloom-videoContainer.
		private void HandleRecordedVideoRequest(ApiRequest request)
		{
			lock (request)
			{
				var bytes = request.RawPostData;
				var fileName = GetNewVideoFileName();
				var videoFolder = BookStorage.GetVideoDirectoryAndEnsureExistence(CurrentBook.FolderPath);
				var path = Path.Combine(videoFolder, fileName);
				SaveVideoFile(path, bytes);
				var videoContainer = GetSelectedEditableVideoContainer(request, path);
				if (videoContainer == null)
					return;

				// Technically this could fail and we might want to report that the post failed.
				// But currently nothing is using the success/fail status, and we don't expect this to fail.
				SaveChangedVideo(videoContainer, path, "Bloom had a problem including that video");
				request.PostSucceeded();
			}
		}

		private GeckoHtmlElement GetSelectedEditableVideoContainer(ApiRequest request, string path)
		{
			var videoContainer = GetSelectedVideoContainer();
			if (videoContainer == null)
			{
				// Enhance: if we end up needing this it should be localizable. But the current plan is to disable
				// video recording and importing if there is no container on the page.
				var msg = "There's nowhere to put a video on this page." +
					(String.IsNullOrEmpty(path) ? "" : (" " + String.Format("You can find it later at {0}", path)));
				MessageBox.Show(msg);
				request.Failed("nowhere to put video");
				return null;
			}

			if (WarnIfVideoCantChange(videoContainer))
			{
				request.Failed("editing not allowed");
				return null;
			}
			return videoContainer;
		}

		/// <summary>
		/// Save the video file, first processing it with ffmpeg (if possible) to convert the data to a more
		/// common and more compressed format (h264 instead of vp8) and to insert keyframes every half second.
		/// Processing with ffmpeg also has the effect of setting the duration value for the video as a whole,
		/// something which is sadly lacking in the video data coming directly from mozilla browser code.
		/// If ffmpeg is not available, then the file is stored exactly as it comes from the api call.
		/// </summary>
		/// <remarks>
		/// Inserting keyframes, and possibly any ffmeg processing, may not be needed if we use ffmpeg to
		/// trim the output later.  But the smaller size of the h264 format may be attractive if the
		/// quality is still good enough to work from.
		/// </remarks>
		private static void SaveVideoFile(string path, byte [] bytes)
		{
			var ffmpeg = FindFfmpegProgram();
			if (ffmpeg != string.Empty)
			{
				var rawVideo = TempFile.CreateAndGetPathButDontMakeTheFile();
				RobustFile.WriteAllBytes(rawVideo.Path, bytes);
				// -hide_banner = don't write all the version and build information to the console
				// -y = always overwrite output file
				// -v 16 = verbosity level reports only errors, including ones that can be recovered from
				// -i <path> = specify input file
				// -force_key_frames "expr:gte(t,n_forced*0.5)" = insert keyframe every 0.5 seconds in the output file
				var parameters = $"-hide_banner -y -v 16 -i \"{rawVideo.Path}\" -force_key_frames \"expr:gte(t,n_forced*0.5)\" \"{path}\"";
				var result = CommandLineRunner.Run(ffmpeg, parameters, "", 60, new NullProgress());
				var msg = string.Empty;
				if (result.DidTimeOut)
				{
					msg = LocalizationManager.GetString("EditTab.Toolbox.SignLanguage.Timeout",
						"The initial processing of the video file timed out after one minute.  The raw video output will be stored.");
				}
				else
				{
					var output = result.StandardError;
					if (!string.IsNullOrWhiteSpace(output))
					{
						// Even though it may be possible to recover from the error, we'll just notify the user and use the raw vp8 output.
						var format = LocalizationManager.GetString("EditTab.Toolbox.SignLanguage.VideoProcessingError",
							"Error output from ffmpeg trying to produce {0}: {1}{2}The raw video output will be stored.",
							"{0} is the path to the video file, {1} is the error message from the ffmpeg program, and {2} is a newline character");
						msg = string.Format(format, path, output, Environment.NewLine);
					}
				}
				if (!string.IsNullOrEmpty(msg))
				{
					Logger.WriteEvent(msg);
					ErrorReport.NotifyUserOfProblem(msg);
					RobustFile.WriteAllBytes(path, bytes);     // use the original, hoping it's better than nothing.
				}
			}
			else
			{
				RobustFile.WriteAllBytes(path, bytes);
			}
		}

		private static string FindFfmpegProgram()
		{
			var ffmpeg = "/usr/bin/ffmpeg";     // standard Linux location
			if (SIL.PlatformUtilities.Platform.IsWindows)
				ffmpeg = Path.Combine(BloomFileLocator.GetCodeBaseFolder(), "ffmpeg.exe");
			return RobustFile.Exists(ffmpeg) ? ffmpeg : string.Empty;
		}

		// Request from sign language tool to restore the original video.
		private void HandleRestoreOriginalRequest(ApiRequest request)
		{
			lock (request)
			{
				var videoContainer = GetSelectedVideoContainer();
				string videoPath;
				if (!GetFilePathFromVideoContainer(request, videoContainer, out videoPath))
					return; // request.Failed was called inside the above method
				var originalPath = Path.ChangeExtension(videoPath, "orig");
				if (!RobustFile.Exists(originalPath))
				{
					request.Failed("no original");
					return;
				}
				var newVideoPath = Path.Combine(BookStorage.GetVideoDirectoryAndEnsureExistence(CurrentBook.FolderPath), GetNewVideoFileName()); // Use a new name to defeat caching.
				var newOriginalPath = Path.ChangeExtension(newVideoPath, "orig");
				RobustFile.Move(originalPath, newOriginalPath); // Keep old original associated with new name
				RobustFile.Copy(newOriginalPath, newVideoPath);
				// I'm not absolutely sure we need to get the Video container again on the UI thread, but have had some problems
				// with COM interfaces in a similar situation so it seems safest.
				View.Invoke((Action)(() => SaveChangedVideo(GetSelectedVideoContainer(), newVideoPath, "Bloom had a problem updating that video")));
				request.PostSucceeded();
			}
		}

		// Request from sign language tool to edit the selected video.
		private void HandleEditVideoRequest(ApiRequest request)
		{
			lock (request)
			{
				var videoContainer = GetSelectedVideoContainer();
				string videoPath;
				if (!GetFilePathFromVideoContainer(request, videoContainer, out videoPath))
					return; // request.Failed was called inside the above method
				var originalPath = Path.ChangeExtension(videoPath, "orig");
				if (!RobustFile.Exists(videoPath))
				{
					if (RobustFile.Exists(originalPath))
					{
						RobustFile.Copy(originalPath, videoPath);
					}
					else
					{
						request.Failed("missing video");
						return;
					}
				}

				var proc = new Process()
				{
					StartInfo = new ProcessStartInfo()
					{
						FileName = videoPath,
						UseShellExecute = true
					},
					EnableRaisingEvents = true
				};
				var begin = DateTime.Now;
				proc.Exited += (sender, args) =>
				{
					var videoFolderPath = BookStorage.GetVideoDirectoryAndEnsureExistence(CurrentBook.FolderPath);
					var lastModifiedFile = new DirectoryInfo(videoFolderPath)
						.GetFiles("*.mp4")
						.OrderByDescending(f => GetRealLastModifiedTime(f))
						.FirstOrDefault();
					if (lastModifiedFile != null && GetRealLastModifiedTime(lastModifiedFile) > begin)
					{
						var newVideoPath = Path.Combine(videoFolderPath, GetNewVideoFileName()); // Use a new name to defeat caching; prefer our standard type of name.
						RobustFile.Move(Path.Combine(videoFolderPath, lastModifiedFile.Name), newVideoPath);
						var newOriginalPath = Path.ChangeExtension(newVideoPath, "orig");
						if (RobustFile.Exists(originalPath))
						{
							RobustFile.Move(originalPath, newOriginalPath); // Keep old original associated with new name
							RobustFile.Delete(videoPath);
						}
						else
						{
							RobustFile.Move(videoPath, newOriginalPath); // Set up original for the first time.
						}
						// I'm not sure why it fails if we use the videoContainer variable we set above,
						// but somehow QueryInterface on the underlying COM object fails. It's probably something to
						// do with the COM threading model that forbids using it on a thread other than the
						// one that created it.
						View.Invoke((Action)(() => SaveChangedVideo(GetSelectedVideoContainer(), newVideoPath, "Bloom had a problem updating that video")));
						//_view.Invoke((Action)(()=> RethinkPageAndReloadIt()));
					}
				};
				proc.Start();
				request.PostSucceeded();
			}
		}

		// Request from sign language tool to import a video.
		private void HandleImportVideoRequest (ApiRequest request)
		{
			var videoContainer = GetSelectedEditableVideoContainer(request, null);
			if (videoContainer == null)
				return;
			string path = null;
			View.Invoke((Action)(() => {
				var videoFiles = LocalizationManager.GetString("EditTab.Toolbox.SignLanguage.FileDialogVideoFiles", "Video files");
				var dlg = new DialogAdapters.OpenFileDialogAdapter
				{
					Multiselect = false,
					CheckFileExists = true,
					Filter = String.Format("{0} (*.mp4)|*.mp4", videoFiles)
				};
				var result = dlg.ShowDialog();
				if (result == DialogResult.OK)
					path = dlg.FileName;
			}));
			if (!string.IsNullOrEmpty(path))
			{
				var newVideoPath = Path.Combine(BookStorage.GetVideoDirectoryAndEnsureExistence(CurrentBook.FolderPath), GetNewVideoFileName()); // Use a new name to defeat caching.
				RobustFile.Copy(path, newVideoPath);
				SaveChangedVideo(videoContainer, newVideoPath, "Bloom had a problem including that video");
			}
			// If the user canceled, we didn't exactly succeed, but having the user cancel is such a normal
			// event that posting a failure, which is a nuisance to ignore, is not warranted.
			request.PostSucceeded();
		}

		// Request from sign language tool to delete the selected video.
		private void HandleDeleteVideoRequest(ApiRequest request)
		{
			lock (request)
			{
				var videoContainer = GetSelectedVideoContainer();
				string videoPath;
				if (!GetFilePathFromVideoContainer(request, videoContainer, out videoPath))
					return; // request.Failed was called inside the above method
				var originalPath = Path.ChangeExtension(videoPath, "orig");
				var label = LocalizationManager.GetString("EditTab.Toolbox.SignLanguage.SelectedVideo", "The selected video", "Appears in the contest \"X will be moved to the recycle bin\"");
				var didDelete = ConfirmRecycleDialog.ConfirmThenRecycle(label, videoPath);
				if (!didDelete)
				{
					// We didn't exactly succeed, but having the user cancel is such a normal
					// event that posting a failure, which is a nuisance to ignore, is not warranted.
					request.PostSucceeded();
					return;

				}
				ConfirmRecycleDialog.Recycle(originalPath);
				View.Invoke((Action)(() => {
					var video = videoContainer.GetElementsByTagName("video").First(); // should be one, since got a path from it above.
					video.ParentNode.RemoveChild(video);
					// BL-6136 add back in the class that shows the placeholder
					videoContainer.ClassName += " bloom-noVideoSelected";
					Model.SaveNow();
					View.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
					View.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
				}));
				request.PostSucceeded();
			}
		}

		private void HandleVideoStatisticsRequest(ApiRequest request)
		{
			if (request.HttpMethod != HttpMethods.Get)
				throw new ApplicationException(request.LocalPath() + " only implements 'get'");
			lock (request)
			{
				string videoFilePath;
				if (!GetFilePathFromVideoContainer(request, GetSelectedVideoContainer(), out videoFilePath))
					return; // request.Failed was called inside the above method
				if (!RobustFile.Exists(videoFilePath))
				{
					request.Failed("Cannot find video file ("+videoFilePath+")");
					return;
				}

				var ffmpeg = FindFfmpegProgram();
				if (ffmpeg == string.Empty)
				{
					request.Failed("Cannot find FFMpeg program");
					return;
				}

				var fileInfo = new FileInfo(videoFilePath);
				var sizeInBytes = fileInfo.Length;

				// Run FFMpeg on the file to get statistics.
				// -hide_banner = don't write all the version and build information to the console
				// -i <path> = specify input file
				var parameters = $"-hide_banner -i \"{videoFilePath}\"";
				var result = CommandLineRunner.Run(ffmpeg, parameters, "", 60, new NullProgress());
				if (result.DidTimeOut)
				{
					request.Failed("FFMpeg timed out getting video statistics");
					return;
				}

				var output = result.StandardError;
				if (string.IsNullOrWhiteSpace(output))
				{
					request.Failed("FFMpeg failed to get video statistics");
					return;
				}

				var statistics = ParseFfmpegStatistics(output, sizeInBytes);
				request.ReplyWithJson(statistics);
			}
		}

		private static Dictionary<string, object> ParseFfmpegStatistics(string output, long sizeInBytes)
		{
			// The RegExes in the individual ParseX methods are mostly from:
			// https://jasonjano.wordpress.com/2010/02/09/a-simple-c-wrapper-for-ffmpeg/

			var statistics = new Dictionary<string, object>();

			ParseDuration(output, statistics);
			ParseFileSize(sizeInBytes, statistics);
			ParseFrameSize(output, statistics);
			ParseFramesPerSecond(output, statistics);
			ParseFileFormat(output, statistics);

			return statistics;
		}

		private static void ParseDuration(string output, IDictionary<string, object> statistics)
		{
			var re = new Regex("[D|d]uration:.((\\d|:|\\.)*)");
			var match = re.Match(output);
			if (!match.Success)
				return;
			var duration = match.Groups[1].Value;
			// put out MM:SS or (if it's really long) HH:MM:SS
			statistics.Add("duration",
				duration.Substring(0, 3) == "00:" ? duration.Substring(3, 5) : duration.Substring(0, 8));
		}

		private static void ParseFileSize(long sizeInBytes, IDictionary<string, object> statistics)
		{
			var sizeInMb = ConvertBytesToMegabyteString(sizeInBytes);
			if (sizeInMb != string.Empty)
			{
				statistics.Add("fileSize", sizeInMb + " MB");
			}
		}

		private static string ConvertBytesToMegabyteString(long sizeInBytes)
		{
			const decimal mbConversion = 1048576M; // 1024 x 1024
			var sizeInMb = sizeInBytes / mbConversion;
			return sizeInMb.ToString("F1", CultureInfo.CurrentUICulture);
		}

		private static void ParseFrameSize(string output, IDictionary<string, object> statistics)
		{
			var re = new Regex("(\\d{2,3})x(\\d{2,3})");
			var match = re.Match(output);
			if (!match.Success)
				return;
			int width, height;
			int.TryParse(match.Groups[1].Value, out width);
			int.TryParse(match.Groups[2].Value, out height);
			// put out www x hhh
			statistics.Add("frameSize", $"{width} x {height}");
		}

		private static void ParseFramesPerSecond(string output, IDictionary<string, object> statistics)
		{
			var re = new Regex(", (\\d{2,3})(\\.\\d{2,3}){0,1} fps");
			var match = re.Match(output);
			if (!match.Success)
				return;
			var fps = match.Value;
			statistics.Add("framesPerSecond", fps.Substring(2).ToUpper(CultureInfo.CurrentUICulture));
		}

		private static void ParseFileFormat(string output, IDictionary<string, object> statistics)
		{
			var re = new Regex("[V|v]ideo: [A-Za-z0-9]* ");
			var match = re.Match(output);
			if (!match.Success)
				return;
			statistics.Add("fileFormat", match.Value.Substring(7).ToUpper(CultureInfo.CurrentUICulture));
		}

		internal void OnChangeVideo(DomEventArgs ge)
		{
			var target = (GeckoHtmlElement)ge.Target.CastToGeckoElement();
			var videoContainer = target.Parent;
			if (videoContainer == null)
				return; // should never happen
			if (WarnIfVideoCantChange(videoContainer))
				return;

			var videoFiles = LocalizationManager.GetString("EditTab.FileDialogVideoFiles", "Video files");
			using (var dlg = new DialogAdapters.OpenFileDialogAdapter
			{
				Multiselect = false,
				CheckFileExists = true,
				// rather restrictive, but the only type that works in all browsers.
				Filter = String.Format("{0} (*.mp4)|*.mp4", videoFiles)
			})
			{
				var result = dlg.ShowDialog();
				if (result == DialogResult.OK)
				{
					// Check memory for the benefit of developers.  The user won't see anything.
					SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "video chosen or canceled", false);
					if (DialogResult.OK == result)
					{
						// var path = MakePngOrJpgTempFileForImage(dlg.ImageInfo.Image);
						SaveChangedVideo(videoContainer, dlg.FileName, "Bloom had a problem including that video");
						// Warn the user if we're starting to use too much memory.
						SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "video chosen and saved", true);
					}
				}
			}

			Logger.WriteMinorEvent("Changed Video");
		}

		internal bool WarnIfVideoCantChange(GeckoHtmlElement videoContainer)
		{
			if (!Model.CanChangeImages())
			{
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.CantPasteImageLocked",
						"Sorry, this book is locked down so that images cannot be changed.")); // Is it worth another LM string so we can say "videos can't be changed?
				return true;
			}
			string currentPath = HtmlDom.GetVideoElementUrl(videoContainer).NotEncoded;

			if (!View.CheckIfLockedAndWarn(currentPath))
				return true;
			return false;
		}

		private bool GetFilePathFromVideoContainer(ApiRequest request, GeckoHtmlElement videoContainer, out string videoFilePath)
		{
			videoFilePath = null;
			if (videoContainer == null)
			{
				// Enhance: if we end up needing this it should be localizable. But the current plan is that the button should be
				// disabled if we don't have a recording to edit.
				request.Failed("no video container");
				return false;
			}

			if (WarnIfVideoCantChange(videoContainer))
			{
				request.Failed("editing not allowed");
				return false;
			}

			var videos = videoContainer.GetElementsByTagName("video");
			if (videos.Length == 0)
			{
				request.Failed("no existing video to edit");
				return false;
			}

			var sources = videos[0].GetElementsByTagName("source");
			if (sources.Length == 0 || string.IsNullOrWhiteSpace(sources[0].GetAttribute("src")))
			{
				request.Failed("current video has no source");
				return false;
			}

			var fileName = sources[0].GetAttribute("src");
			videoFilePath = Path.Combine(CurrentBook.FolderPath, fileName);
			// Some callers need this file to exist, others don't, but decoding is required for all.
			if (!RobustFile.Exists(videoFilePath) && Regex.IsMatch(fileName, "%[0-9A-Fa-f][0-9A-Fa-f]"))
			{
				videoFilePath = Path.Combine(CurrentBook.FolderPath,
					UrlPathString.CreateFromUrlEncodedString(fileName).NotEncoded);
			}
			return true;
		}

		private GeckoHtmlElement GetSelectedVideoContainer()
		{
			var root = View.Browser.WebBrowser.Document;
			var page = root.GetElementById("page") as GeckoIFrameElement;
			var pageDoc = page.ContentWindow.Document;
			var videoContainer =
				pageDoc.GetElementsByClassName("bloom-videoContainer bloom-selected").FirstOrDefault() as GeckoHtmlElement;
			return videoContainer;
		}

		DateTime GetRealLastModifiedTime(FileInfo info)
		{
			if (info.LastWriteTime > info.CreationTime)
				return info.LastWriteTime;
			else
				return info.CreationTime;
		}

		public static string GetNewVideoFileName()
		{
			return Guid.NewGuid().ToString() + ".mp4";
		}

		internal void SaveChangedVideo(GeckoHtmlElement videoElement, string videoPath, string exceptionMsg)
		{
			try
			{
				ChangeVideo(videoElement, videoPath, new NullProgress());
			}
			catch (System.IO.IOException error)
			{
				ErrorReport.NotifyUserOfProblem(error, error.Message);
			}
			catch (ApplicationException error)
			{
				ErrorReport.NotifyUserOfProblem(error, error.Message);
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error, exceptionMsg);
			}
		}

		public void ChangeVideo(GeckoHtmlElement videoContainer, string videoPath, IProgress progress)
		{
			try
			{
				Logger.WriteMinorEvent("Starting ChangeVideo {0}...", videoPath);
				var editor = new PageEditingModel();
				editor.ChangeVideo(CurrentBook.FolderPath, new ElementProxy(videoContainer), videoPath, progress);

				// We need to save so that when asked by the thumbnailer, the book will give the proper image
				Model.SaveNow();

				// At some point we might clean up unused videos here. If so, be careful about
				// losing license info if we ever put video information without it in the clipboard.
				// Compare the parallel situation for images, BL-3717

				// But after saving, we need the non-cleaned version back there
				View.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

				View.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
				Analytics.Track("Change Video");
				Logger.WriteEvent("ChangeVideo {0}...", videoPath);

			}
			catch (Exception e)
			{
				var msg = LocalizationManager.GetString("Errors.ProblemImportingVideo", "Bloom had a problem importing this video.");
				ErrorReport.NotifyUserOfProblem(e, msg + Environment.NewLine + e.Message);
			}
		}
	}
}
