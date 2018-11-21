using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using DesktopAnalytics;
using Gecko;
using Gecko.DOM;
using L10NSharp;
using SIL.Code;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.FileSystem;
using SIL.Xml;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Handles API calls related to the Sign Language tool (mainly to video).
	/// </summary>
	public class SignLanguageApi
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private bool _doingEditOutsideBloom;
		private const string noVideoClass = "bloom-noVideoSelected";

		public EditingView View { get; set; }
		public EditingModel Model { get; set; }

		private static string _ffmpeg;

		public SignLanguageApi(BookSelection bookSelection, PageSelection pageSelection)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			DeactivateTime = DateTime.MaxValue; // no action needed on first activate.
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
				var videoFolder = BookStorage.GetVideoDirectoryAndEnsureExistence(CurrentBook.FolderPath);
				var fileName = GetNewVideoFileName();
				var path = Path.Combine(videoFolder, fileName);
				using (var rawVideo = TempFile.CreateAndGetPathButDontMakeTheFile())
				{
					using (var rawVideoOutput = new FileStream(rawVideo.Path, FileMode.Create))
					{
						// Do NOT just get RawPostData and try to write it to a file; this
						// typically runs out of memory for anything more than about 2min of video.
						// (It write to a MemoryStream, and this seems to manage memory very badly.
						// 66MB should not be a huge problem, but somehow it is.)
						using (var rawVideoInput = request.RawPostStream)
						{
							rawVideoInput.CopyTo(rawVideoOutput);
						}

						SaveVideoFile(path, rawVideo.Path);
					}
				}

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
					(string.IsNullOrEmpty(path) ? "" : " " + $"You can find it later at {path}");
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
		private static void SaveVideoFile(string path, string rawVideoPath)
		{
			var length = new FileInfo(rawVideoPath).Length;
			var seconds = (int)(length / 312500); // we're asking for 2.5mpbs, which works out to 312.5K bytes per second.
			if (!string.IsNullOrEmpty(FfmpegProgram))
			{
				// -hide_banner = don't write all the version and build information to the console
				// -y = always overwrite output file
				// -v 16 = verbosity level reports only errors, including ones that can be recovered from
				// -i <path> = specify input file
				// -force_key_frames "expr:gte(t,n_forced*0.5)" = insert keyframe every 0.5 seconds in the output file
				var parameters = $"-hide_banner -y -v 16 -i \"{rawVideoPath}\" -force_key_frames \"expr:gte(t,n_forced*0.5)\" \"{path}\"";
				// On slowish machines, this compression seems to take about 1/5 as long as the video took to record.
				// Allowing for some possibly being slower still, we're basing the timeout on half the length of the video,
				// plus a minute to be sure.
				var result = CommandLineRunner.Run(FfmpegProgram, parameters, "", seconds / 2 + 60, new NullProgress());
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
					RobustFile.Copy(rawVideoPath, path);     // use the original, hoping it's better than nothing.
				}
			}
			else
			{
				RobustFile.Copy(rawVideoPath, path);
			}
		}

		public static string FfmpegProgram
		{
			get
			{
				if (string.IsNullOrEmpty(_ffmpeg))
				{
					_ffmpeg = FindFfmpegProgram();
				}

				return _ffmpeg;
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
				decimal[] timings;
				if (!ParseVideoContainerSourceAttribute(request, videoContainer, true, out videoPath, out timings))
					return; // request.Failed was called inside the above method

				var originalPath = Path.ChangeExtension(videoPath, "orig");
				if (!RobustFile.Exists(originalPath))
				{
					request.Failed("no original video file ("+originalPath+")");
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
				decimal[] timings;
				if (!ParseVideoContainerSourceAttribute(request, videoContainer, true, out videoPath, out timings))
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
						request.Failed("missing video file ("+videoPath+")");
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
				_doingEditOutsideBloom = true;
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
					_doingEditOutsideBloom = false;
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
					Filter = $"{videoFiles} (*.mp4)|*.mp4"
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
				decimal[] dummy;
				if (!ParseVideoContainerSourceAttribute(request, videoContainer, true, out videoPath, out dummy))
					return; // request.Failed was called inside the above method

				var originalPath = Path.ChangeExtension(videoPath, "orig");
				var label = LocalizationManager.GetString("EditTab.Toolbox.SignLanguage.SelectedVideo", "The selected video", "Appears in the contest \"X will be moved to the recycle bin\"");
				if (!ConfirmRecycleDialog.JustConfirm(label))
				{
					// We didn't exactly succeed, but having the user cancel is such a normal
					// event that posting a failure, which is a nuisance to ignore, is not warranted.
					request.PostSucceeded();
					return;
				}

				ConfirmRecycleDialog.Recycle(originalPath);
				View.Invoke((Action)(() =>
				{
					// Other "HandleX" methods have comments here about COM problems re-using the videoContainer variable
					// above. Hopefully this will make deletion more reliable too.
					var container = GetSelectedVideoContainer();
					var video = container.GetElementsByTagName("video").First(); // should be one, since got a path from it above.
					video.ParentNode.RemoveChild(video);
					// BL-6136 add back in the class that shows the placeholder
					container.ClassName += " " + noVideoClass;
					Model.SaveNow();
					View.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
					View.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
				}));
				// After we refresh the page, breaking any state that has the video locked because it's been played,
				// we should be actually able to recycle it. Clearing the cache may help (in case it is holding on to
				// the video somehow).
				Browser.ClearCache();
				ConfirmRecycleDialog.Recycle(videoPath);
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
				decimal[] timings;
				if (!ParseVideoContainerSourceAttribute(request, GetSelectedVideoContainer(), false, out videoFilePath, out timings))
					return; // request.Failed was called inside the above method

				if (!RobustFile.Exists(videoFilePath))
				{
					request.Failed("Cannot find video file ("+videoFilePath+")");
					return;
				}

				if (string.IsNullOrEmpty(FfmpegProgram))
				{
					request.Failed("Cannot find ffmpeg program");
					return;
				}

				var fileInfo = new FileInfo(videoFilePath);
				var sizeInBytes = fileInfo.Length;

				var output = RunFfmpegOnVideoToGetStatistics(request, videoFilePath);

				var statistics = ParseFfmpegStatistics(output, sizeInBytes);
				statistics.Add("startSeconds", timings[0].ToString("F1"));
				statistics.Add("endSeconds", timings[1].ToString("F1"));
				request.ReplyWithJson(statistics);
			}
		}

		// Run ffmpeg on the file to get statistics.
		// request is an optional parameter, the method works fine with it set to null.
		private static string RunFfmpegOnVideoToGetStatistics(ApiRequest request, string videoFilePath)
		{
			// -hide_banner = don't write all the version and build information to the console
			// -i <path> = specify input file
			var parameters = $"-hide_banner -i \"{videoFilePath}\"";
			var result = CommandLineRunner.Run(FfmpegProgram, parameters, "", 60, new NullProgress());
			if (result.DidTimeOut)
			{
				request?.Failed("ffmpeg timed out getting video statistics");
				return string.Empty;
			}

			var output = result.StandardError;
			if (string.IsNullOrWhiteSpace(output))
			{
				request?.Failed("ffmpeg failed to get video statistics");
				return string.Empty;
			}

			return output;
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
			// put out MM:SS.T or (if it's really long) HH:MM:SS.T
			statistics.Add("duration",
				duration.Substring(0, 3) == "00:" ? duration.Substring(3, 7) : duration.Substring(0, 10));
		}

		private static void ParseFileSize(long sizeInBytes, IDictionary<string, object> statistics)
		{
			var sizeInMb = ConvertBytesToMegabyteString(sizeInBytes);
			if (!string.IsNullOrEmpty(sizeInMb))
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

		private bool ParseVideoContainerSourceAttribute(ApiRequest request, GeckoHtmlElement videoContainer, bool forEditing,
			out string videoFilePath, out decimal[] timings)
		{
			videoFilePath = null;
			timings = new[] {0.0m, 0.0m};
			if (videoContainer == null)
			{
				// Enhance: if we end up needing this it should be localizable. But the current plan is that the button should be
				// disabled if we don't have a recording to edit.
				request?.Failed("no video container");
				return false;
			}

			if (forEditing && WarnIfVideoCantChange(videoContainer))
			{
				request?.Failed("editing not allowed");
				return false;
			}

			var videos = videoContainer.GetElementsByTagName("video");
			if (videos.Length == 0)
			{
				request?.Failed("no existing video to edit");
				return false;
			}

			var sources = videos[0].GetElementsByTagName("source");
			if (sources.Length == 0 || string.IsNullOrWhiteSpace(sources[0].GetAttribute("src")))
			{
				request?.Failed("current video has no source");
				return false;
			}

			var fileNameWithTimings = sources[0].GetAttribute("src");
			string rawTimings;
			var fileName = StripTimingFromVideoUrl(fileNameWithTimings, out rawTimings);
			var paramIndex = fileName.IndexOf("?");
			if (paramIndex >= 0)
			{
				fileName = fileName.Substring(0, paramIndex);
			}

			videoFilePath = Path.Combine(CurrentBook.FolderPath, fileName);
			// Some callers need this file to exist, others don't, but decoding is required for all.
			if (!RobustFile.Exists(videoFilePath) && Regex.IsMatch(fileName, "%[0-9A-Fa-f][0-9A-Fa-f]"))
			{
				videoFilePath = Path.Combine(CurrentBook.FolderPath,
					UrlPathString.CreateFromUrlEncodedString(fileName).NotEncoded);
			}
			ConvertRawTimingsToDecimalArray(rawTimings, timings);
			return true;
		}

		private static void ConvertRawTimingsToDecimalArray(string rawTimings, decimal[] timings)
		{
			if (string.IsNullOrEmpty(rawTimings))
				return; // do nothing. timings array will hold default values
			var timingArray = rawTimings.Split(',');
			timings[0] = Convert.ToDecimal(timingArray[0]);
			if (timingArray.Length > 1)
			{
				timings[1] = Convert.ToDecimal(timingArray[1]);
			}
		}

		public static string StripTimingFromVideoUrl(string videoUrl, out string timings)
		{
			var baseUri = new Uri("https://bloomlibrary.org"); // only used to get Uri class to function properly.
			var videoUri = new Uri(baseUri, videoUrl);
			var fragment = videoUri.Fragment; // timing fragments
			timings = fragment.Length > 0 ? fragment.Substring(3) : string.Empty; // strip off timing prefix ('#t=')
			// The next line will strip off the query too (if there is one).
			// Currently we never want the query here, if we do someday, we should use 'videoUri.PathAndQuery'.
			return videoUri.LocalPath.Substring(1); // most callers won't want the initial slash '/', LocalPath ensures no encoding
		}

		private GeckoHtmlElement GetSelectedVideoContainer()
		{
			var root = View.Browser.WebBrowser.Document;
			var page = root.GetElementById("page") as GeckoIFrameElement;
			var pageDoc = page.ContentWindow.Document;
			return
				pageDoc.GetElementsByClassName("bloom-videoContainer bloom-selected").FirstOrDefault() as GeckoHtmlElement;
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
				var msg = LocalizationManager.GetString("Errors.ProblemImportingVideo",
					"Bloom had a problem importing this video.");
				ErrorReport.NotifyUserOfProblem(e, msg + Environment.NewLine + e.Message);
			}
		}

		public DateTime DeactivateTime { get; set; }

		/// <summary>
		/// In case the user has been editing video files outside of Bloom, for example,
		/// after using the Show In Folder command in the sign language toolbox,
		/// we want to update the pages that show those videos to use the new versions.
		/// Unfortunately something, probably the browser itself, seems to be very
		/// persistent about caching videos, even though our server tells it not to cache
		/// things in the book folder (and debug builds don't ever tell it to cache anything).
		/// We prevent cache hits on modified files by adding a fake param to the URI.
		/// (Also we will remove any trimming...see comment below.)
		/// This function is called whenever Bloom is activated in Edit mode.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="eventArgs"></param>
		public void CheckForChangedVideoOnActivate(object sender, EventArgs eventArgs)
		{
			// We're only going to check for video changes on the current book, if any.
			// It's not inconceivable that whatever caches videos will keep a cached one
			// for another book, but I think trying to modify books we don't even have open
			// is to dangerous, as well as quite difficult.
			if (CurrentBook == null)
				return;
			// These two mechanisms are in danger of fighting over the change. If we're in the
			// middle of editing a single file from the Bloom command, don't do anything here.
			// Note: this means we _could_ miss another edit the user did while he was off doing
			// the edit outside. But the race condition between the event handlers for Bloom activated
			// and the end of the edit-outside process is a real one (different threads) and they
			// did really overlap before I put this in. The edit-outside process is looking for the most recently
			// modified video to replace the current one, so things are likely to get confused
			// anyway if the user is trying to use both mechanisms at once. This mechanism can't
			// just replace that one (at least as it stands), because we're expecting the outside
			// program to make a new file, allowing Bloom to save the old one as an original, while
			// this is looking for in-place changes.
			// A downside is that if the user never closes the edit-outside program, this mechanism
			// will stay disabled. But I don't see a better answer, at least if we keep both commands.
			if (_doingEditOutsideBloom)
				return;
			var videoFolderPath = BookStorage.GetVideoDirectoryAndEnsureExistence(CurrentBook.FolderPath);
			var filesModifiedSinceDeactivate = new DirectoryInfo(videoFolderPath)
				.GetFiles("*.mp4")
				.Where(f => GetRealLastModifiedTime(f) > DeactivateTime)
				.Select(f => f.FullName)
				.ToList();

			if (!filesModifiedSinceDeactivate.Any())
				return;

			// We might modify the current page, but the user may also have modified it
			// without doing anything to cause a Save before the deactivate. So save their
			// changes before we go to work on it.
			Model.SaveNow();

			foreach (var videoPath in filesModifiedSinceDeactivate)
			{
				var expectedSrcAttr = UrlPathString.CreateFromUnencodedString(BookStorage.GetVideoFolderName + Path.GetFileName(videoPath));
				var videoElts = CurrentBook.RawDom.SafeSelectNodes($"//video/source[contains(@src,'{expectedSrcAttr.UrlEncodedForHttpPath}')]");
				if (videoElts.Count == 0)
					continue; // not used in book, ignore

				// OK, the user has modified the file outside of Bloom. Something is determined to cache video.
				// Defeat it by setting a fake param.
				// Note that doing this will discard any fragment in the existing URL, typically trimming.
				// I think this is good...if the user has edited the video, we should start over assuming he
				// wants all of it.

				var newSrcAttr = UrlPathString.CreateFromUnencodedString(BookStorage.GetVideoFolderName + Path.GetFileName(videoPath));
				HtmlDom.SetSrcOfVideoElement(newSrcAttr, new ElementProxy((XmlElement)videoElts[0]), true, "?now=" + DateTime.Now.Ticks);
			}

			// We could try to figure out whether one of the modified videos is on the current page.
			// But that's the most likely video to be modified, and it doesn't take long to reload,
			// and this only happens in the very special case that the user has modified a video outside
			// of Bloom.
			View.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

			// Likewise, this is probably overkill, but it's a probably-rare case. 
			View.UpdateAllThumbnails();
		}

		/// <summary>
		/// When publishing videos in any form but PDF, we want to trim the actual video to just the part that
		/// the user wants to see and add the controls attribute, so that the video controls are visible.
		/// </summary>
		/// <param name="videoContainerElement">bloom-videoContainer element from copied DOM</param>
		/// <param name="sourceBookFolder">This is assumed to be a staging folder, we may replace videos here!</param>
		/// <returns>the new filepath if a video file exists and was copied, empty string if no video file was found</returns>
		public static string PrepareVideoForPublishing(XmlElement videoContainerElement,
			string sourceBookFolder)
		{
			var videoFolder = Path.Combine(sourceBookFolder, "video");

			var videoElement = videoContainerElement.SelectSingleNode("video") as XmlElement;
			if (videoElement == null)
				return string.Empty;

			// In each valid video element, we remove any timings in the 'src' attribute of the source element.
			var sourceElement = videoElement.SelectSingleNode("source") as XmlElement;
			var srcAttrVal = sourceElement?.Attributes["src"]?.Value;
			if (srcAttrVal == null)
				return string.Empty;

			string timings;
			var videoUrl = StripTimingFromVideoUrl(srcAttrVal, out timings);

			// Check for valid video file to match url
			var urlWithoutPrefix = UrlPathString.CreateFromUrlEncodedString(videoUrl.Substring(6)); // grab everything after 'video/'
			var originalVideoFilePath = Path.Combine(videoFolder, urlWithoutPrefix.NotEncoded);
			if (!RobustFile.Exists(originalVideoFilePath))
				return string.Empty;

			var tempName = originalVideoFilePath;
			if (!string.IsNullOrEmpty(FfmpegProgram) && !string.IsNullOrEmpty(timings) &&
			    IsVideoMarkedForTrimming(sourceBookFolder, videoUrl, timings))
			{
				tempName = Path.Combine(videoFolder, GetNewVideoFileName());
				var successful = TrimVideoUsingFfmpeg(originalVideoFilePath, tempName, timings);
				if (successful)
				{
					RobustFile.Delete(originalVideoFilePath);
					var trimmedFileName = "video/" + Path.GetFileName(tempName); // we never want backslash here...
					HtmlDom.SetVideoElementUrl(new ElementProxy(videoContainerElement), UrlPathString.CreateFromUnencodedString(trimmedFileName, true), false);
				}
				else
				{
					// probably doesn't exist, but if it does we don't need it.
					// File.Delete (underneath RobustFile.Delete) does not throw if the file doesn't exist.
					RobustFile.Delete(tempName);
					tempName = originalVideoFilePath;
				}
			}
			// Add attributes needed for videos to work in Readium and possibly other readers.
			// Existence of the 'controls' attribute is enough to trigger controls
			videoElement.SetAttribute("controls", string.Empty);
			videoElement.SetAttribute("width", "100%");
			return tempName;
		}

		private static bool IsVideoMarkedForTrimming(string sourceBookFolder, string videoUrl, string rawTimings)
		{
			var timings = new[] { 0.0m, 0.0m };
			ConvertRawTimingsToDecimalArray(rawTimings, timings);
			var startTrimPoint = timings[0];
			var endTrimPoint = timings[1];
			if (startTrimPoint > 0)
				return true;

			var stats = new Dictionary<string, object>();
			var videoFilePath = Path.Combine(sourceBookFolder, videoUrl);
			var output = RunFfmpegOnVideoToGetStatistics(null, videoFilePath);
			ParseDuration(output, stats);
			var durStr = stats["duration"] as string;
			var duration = ConvertHhMmSsStringToSeconds(durStr);
			// if our trim setting is less than 1/10 second from the end of the whole video, don't bother trimming the video.
			// duration in seconds is equal to the endpoint of the (untrimmed) video.
			return duration - endTrimPoint > 0.1m;
		}

		private static bool TrimVideoUsingFfmpeg(string sourceVideoFilePath,
			string destinationPath,
			string timings)
		{
			Guard.Against(string.IsNullOrEmpty(FfmpegProgram), "Caller should have verified 'ffmpeg' existence.");
			var timingArray = timings.Split(',');
			var startTiming = ConvertSecondsToHhMmSsString(timingArray[0]);
			var endTiming = ConvertSecondsToHhMmSsString(timingArray[1]);
			// Run ffmpeg on the file to trim it down using the timings.
			// -hide_banner = don't write all the version and build information to the console
			// -i <path> = specify input file
			// -ss HH:MM:SS.T = trim to this start time
			// -to HH:MM:SS.T = trim to this end time
			// -c:v copy -c:a copy = copy video (and audio) streams with no codec modification
			var parameters = $"-hide_banner -i \"{sourceVideoFilePath}\" -ss {startTiming} -to {endTiming} -c:v copy -c:a copy \"{destinationPath}\"";
			var result = CommandLineRunner.Run(FfmpegProgram, parameters, "", 60, new NullProgress());
			if (result.DidTimeOut)
			{
				Logger.WriteEvent("ffmpeg timed out trimming video for publication");
				return false;
			}

			var output = result.StandardError;
			if (string.IsNullOrWhiteSpace(output) || output.Contains("Invalid data found when processing input"))
			{
				Logger.WriteEvent("ffmpeg did not return normal output");
				return false;
			}

			return true;
		}

		private static string ConvertSecondsToHhMmSsString(string seconds)
		{
			var time = TimeSpan.FromSeconds(double.Parse(seconds));
			return time.ToString(@"hh\:mm\:ss\.f");
		}

		private static decimal ConvertHhMmSsStringToSeconds(string hhmmss)
		{
			var formatString = hhmmss.Length > 7 ? "HH:mm:ss.f" : "mm:ss.f";
			var dt = DateTime.ParseExact(hhmmss, formatString, CultureInfo.CurrentUICulture);
			return dt.Hour * 3600m + dt.Minute * 60m + dt.Second + dt.Millisecond / 1000m;
		}
	}
}
