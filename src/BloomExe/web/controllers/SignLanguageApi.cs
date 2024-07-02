using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.SafeXml;
using Bloom.ToPalaso;
using Bloom.Utils;
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
        private bool _importedVideoIntoBloom;
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
            apiHandler
                .RegisterEndpointHandler(
                    "signLanguage/recordedVideo",
                    HandleRecordedVideoRequest,
                    true
                )
                .Measureable("Process recorded video");
            apiHandler
                .RegisterEndpointHandler("signLanguage/deleteVideo", HandleDeleteVideoRequest, true)
                .Measureable("Delete video");
            ;
            apiHandler.RegisterEndpointHandler(
                "signLanguage/importVideo",
                HandleImportVideoRequest,
                true
            ); // has dialog, so measure internally after the dialog.
            apiHandler.RegisterEndpointHandler(
                "signLanguage/getStats",
                HandleVideoStatisticsRequest,
                true
            );
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
                var videoFolder = BookStorage.GetVideoDirectoryAndEnsureExistence(
                    CurrentBook.FolderPath
                );
                var fileName = GetNewVideoFileName();
                var path = Path.Combine(videoFolder, fileName);
                using (var rawVideo = TempFile.CreateAndGetPathButDontMakeTheFile())
                {
                    using (
                        var rawVideoOutput = RobustIO.GetFileStream(rawVideo.Path, FileMode.Create)
                    )
                    {
                        // Do NOT just get RawPostData and try to write it to a file; this
                        // typically runs out of memory for anything more than about 2min of video.
                        // (It write to a MemoryStream, and this seems to manage memory very badly.
                        // 66MB should not be a huge problem, but somehow it is.)
                        using (var rawVideoInput = request.RawPostStream)
                        {
                            rawVideoInput.CopyTo(rawVideoOutput);
                        }
                    }
                    // The output stream should be closed before trying to access the newly written file.
                    SaveVideoFile(path, rawVideo.Path);
                }

                var relativePath = BookStorage.GetVideoFolderName + Path.GetFileName(path);
                request.ReplyWithText(
                    UrlPathString.CreateFromUnencodedString(relativePath).UrlEncodedForHttpPath
                );
            }
        }

        /// <summary>
        /// Save the video file, first processing it with ffmpeg (if possible) to convert the data to a more
        /// common and more compressed format (h264 instead of vp8) and to insert keyframes every half second.
        /// Processing with ffmpeg also has the effect of setting the duration value for the video as a whole,
        /// something which is sadly lacking in the video data coming directly from mozilla browser code.
        /// If ffmpeg is not available, then the file is stored exactly as it comes from the api call.
        /// </summary>
        /// <remarks>
        /// Explicitly setting the frame rate to 30 fps is needed for Geckofx60.  The raw file that comes
        /// directly from the javascript objects claims to be at 1000fps.  This is perhaps due to the high
        /// speed video cameras that claim up to that frame rate.  I can't figure out how to change the frame
        /// rate in javascript.  The video track already claims to be at 30 fps even though the raw file
        /// claims to be at 1000fps.  Interestingly, setting the frame rate explicitly speeds up ffmpeg
        /// processing to what it was with Geckofx45 instead of being much slower.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-7934.
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
                // -r 30 = set frame rate to 30 fps
                // -force_key_frames "expr:gte(t,n_forced*0.5)" = insert keyframe every 0.5 seconds in the output file
                var parameters =
                    $"-hide_banner -y -v 16 -i \"{rawVideoPath}\" -r 30 -force_key_frames \"expr:gte(t,n_forced*0.5)\" \"{path}\"";
                // On slowish machines, this compression seems to take about 1/5 as long as the video took to record.
                // Allowing for some possibly being slower still, we're basing the timeout on half the length of the video,
                // plus a minute to be sure.
                var result = CommandLineRunnerExtra.RunWithInvariantCulture(
                    FfmpegProgram,
                    parameters,
                    "",
                    seconds / 2 + 60,
                    new NullProgress()
                );
                var msg = string.Empty;
                if (result.DidTimeOut)
                {
                    msg = LocalizationManager.GetString(
                        "EditTab.Toolbox.SignLanguage.Timeout",
                        "The initial processing of the video file timed out after one minute.  The raw video output will be stored."
                    );
                }
                else
                {
                    var output = result.StandardError;
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        // Even though it may be possible to recover from the error, we'll just notify the user and use the raw vp8 output.
                        var format = LocalizationManager.GetString(
                            "EditTab.Toolbox.SignLanguage.VideoProcessingError",
                            "Error output from ffmpeg trying to produce {0}: {1}{2}The raw video output will be stored.",
                            "{0} is the path to the video file, {1} is the error message from the ffmpeg program, and {2} is a newline character"
                        );
                        msg = string.Format(format, path, output, Environment.NewLine);
                    }
                }
                if (!string.IsNullOrEmpty(msg))
                {
                    Logger.WriteEvent(msg);
                    ErrorReport.NotifyUserOfProblem(msg);
                    RobustFile.Copy(rawVideoPath, path, true); // use the original, hoping it's better than nothing.
                }
            }
            else
            {
                RobustFile.Copy(rawVideoPath, path, true);
            }
        }

        public static string FfmpegProgram
        {
            get
            {
                if (string.IsNullOrEmpty(_ffmpeg))
                {
                    _ffmpeg = MiscUtils.FindFfmpegProgram();
                }

                return _ffmpeg;
            }
        }

        // Request from sign language tool to import a video.
        private void HandleImportVideoRequest(ApiRequest request)
        {
            string path = null;
            View.Invoke(
                (Action)(
                    () =>
                    {
                        var videoFiles = LocalizationManager.GetString(
                            "EditTab.Toolbox.SignLanguage.FileDialogVideoFiles",
                            "Video files"
                        );
                        var dlg = new DialogAdapters.OpenFileDialogAdapter
                        {
                            Multiselect = false,
                            CheckFileExists = true,
                            // If this filter ever changes, make sure we update BookCompressor.VideoFileExtensions.
                            Filter = $"{videoFiles} (*.mp4)|*.mp4"
                        };
                        var result = dlg.ShowDialog();
                        if (result == DialogResult.OK)
                            path = dlg.FileName;
                    }
                )
            );
            if (!string.IsNullOrEmpty(path))
            {
                using (PerformanceMeasurement.Global.Measure("Import Video", path))
                {
                    _importedVideoIntoBloom = true;
                    var newVideoPath = Path.Combine(
                        BookStorage.GetVideoDirectoryAndEnsureExistence(CurrentBook.FolderPath),
                        GetNewVideoFileName()
                    ); // Use a new name to defeat caching.
                    RobustFile.Copy(path, newVideoPath);
                    var relativePath =
                        BookStorage.GetVideoFolderName + Path.GetFileName(newVideoPath);
                    request.ReplyWithText(
                        UrlPathString.CreateFromUnencodedString(relativePath).UrlEncodedForHttpPath
                    );
                }
            }
            else
            {
                // If the user canceled, we didn't exactly succeed, but having the user cancel is such a normal
                // event that posting a failure, which is a nuisance to ignore, is not warranted.
                request.ReplyWithText("");
            }
        }

        // Request from sign language tool to delete the selected video.
        private void HandleDeleteVideoRequest(ApiRequest request)
        {
            lock (request)
            {
                string videoPath;
                decimal[] dummy;
                if (!GetVideoDetailsFromRequest(request, true, out videoPath, out dummy))
                    return; // request.Failed was called inside the above method

                var label = LocalizationManager.GetString(
                    "EditTab.Toolbox.SignLanguage.SelectedVideo",
                    "The selected video",
                    "Appears in the context \"X will be moved to the recycle bin\""
                );
                if (!ConfirmRecycleDialog.JustConfirm(label))
                {
                    request.ReplyWithText("canceled");
                    return;
                }

                ConfirmRecycleDialog.Recycle(videoPath);
                request.ReplyWithText("deleted");
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
                if (!GetVideoDetailsFromRequest(request, false, out videoFilePath, out timings))
                    return; // request.Failed was called inside the above method

                if (!RobustFile.Exists(videoFilePath))
                {
                    request.Failed("Cannot find video file (" + videoFilePath + ")");
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
        private static string RunFfmpegOnVideoToGetStatistics(
            ApiRequest request,
            string videoFilePath
        )
        {
            // -hide_banner = don't write all the version and build information to the console
            // -i <path> = specify input file
            var parameters = $"-hide_banner -i \"{videoFilePath}\"";
            var result = CommandLineRunnerExtra.RunWithInvariantCulture(
                FfmpegProgram,
                parameters,
                "",
                60,
                new NullProgress()
            );
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

        private static Dictionary<string, object> ParseFfmpegStatistics(
            string output,
            long sizeInBytes
        )
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
            var re = new Regex("[D|d]uration:.((\\d|:|\\.)+)");
            var match = re.Match(output);
            if (!match.Success)
            {
                statistics.Add("duration", "unknown");
                return;
            }
            var duration = match.Groups[1].Value;
            // put out MM:SS.T or (if it's really long) HH:MM:SS.T
            statistics.Add(
                "duration",
                duration.Substring(0, 3) == "00:"
                    ? duration.Substring(3, 7)
                    : duration.Substring(0, 10)
            );
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
            int width,
                height;
            int.TryParse(match.Groups[1].Value, out width);
            int.TryParse(match.Groups[2].Value, out height);
            // put out www x hhh
            statistics.Add("frameSize", $"{width} x {height}");
        }

        private static void ParseFramesPerSecond(
            string output,
            IDictionary<string, object> statistics
        )
        {
            var re = new Regex(", (\\d{2,3})(\\.\\d{2,3}){0,1} fps");
            var match = re.Match(output);
            if (!match.Success)
                return;
            var fps = match.Value;
            statistics.Add(
                "framesPerSecond",
                fps.Substring(2).ToUpper(CultureInfo.CurrentUICulture)
            );
        }

        private static void ParseFileFormat(string output, IDictionary<string, object> statistics)
        {
            var re = new Regex("[V|v]ideo: [A-Za-z0-9]* ");
            var match = re.Match(output);
            if (!match.Success)
                return;
            statistics.Add(
                "fileFormat",
                match.Value.Substring(7).ToUpper(CultureInfo.CurrentUICulture)
            );
        }

        internal bool WarnIfVideoCantChange(string videoFilePath)
        {
            if (!View.CheckIfLockedAndWarn(videoFilePath))
                return true;
            return false;
        }

        private bool GetVideoDetailsFromRequest(
            ApiRequest request,
            bool forEditing,
            out string videoFilePath,
            out decimal[] timings
        )
        {
            var fileName = request.Parameters.Get("source");
            timings = new[] { 0.0m, 0.0m };

            string rawTimings = UrlPathString
                .CreateFromUrlEncodedString(request.Parameters.Get("timings"))
                .NotEncoded; // UrlPathString used only for decoding

            videoFilePath = Path.Combine(CurrentBook.FolderPath, fileName);
            // Some callers need this file to exist, others don't, but decoding is required for all.
            if (
                !RobustFile.Exists(videoFilePath)
                && Regex.IsMatch(fileName, "%[0-9A-Fa-f][0-9A-Fa-f]")
            )
            {
                videoFilePath = Path.Combine(
                    CurrentBook.FolderPath,
                    UrlPathString.CreateFromUrlEncodedString(fileName).NotEncoded
                ); // UrlPathString used only for decoding
            }

            if (forEditing && WarnIfVideoCantChange(videoFilePath))
            {
                request?.Failed("editing not allowed");
                return false;
            }

            ConvertRawTimingsToDecimalArray(rawTimings, timings);
            return true;
        }

        private static void ConvertRawTimingsToDecimalArray(string rawTimings, decimal[] timings)
        {
            if (string.IsNullOrEmpty(rawTimings))
                return; // do nothing. timings array will hold default values
            if (rawTimings.StartsWith("t="))
                rawTimings = rawTimings.Substring(2);
            var timingArray = rawTimings.Split(',');
            timings[0] = Convert.ToDecimal(timingArray[0], CultureInfo.InvariantCulture);
            if (timingArray.Length > 1)
            {
                timings[1] = Convert.ToDecimal(timingArray[1], CultureInfo.InvariantCulture);
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

            // On Linux, this method interferes with successfully referencing a newly imported video file.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-6723.  Ignoring just the one call to
            // this method suffices for things  to work.  (On Windows, the sequence of events differs, but
            // this change is safe.)
            if (_importedVideoIntoBloom)
            {
                _importedVideoIntoBloom = false;
                return;
            }
            var videoFolderPath = BookStorage.GetVideoDirectoryAndEnsureExistence(
                CurrentBook.FolderPath
            );
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
            Model.SaveThen(
                () =>
                {
                    foreach (var videoPath in filesModifiedSinceDeactivate)
                    {
                        var expectedSrcAttr = UrlPathString.CreateFromUnencodedString(
                            BookStorage.GetVideoFolderName + Path.GetFileName(videoPath)
                        );
                        var videoElts = CurrentBook.RawDom.SafeSelectNodes(
                            $"//video/source[contains(@src,'{expectedSrcAttr.UrlEncodedForHttpPath}')]"
                        );
                        if (videoElts.Length == 0)
                            continue; // not used in book, ignore

                        // OK, the user has modified the file outside of Bloom. Something is determined to cache video.
                        // Defeat it by setting a fake param.
                        // Note that doing this will discard any fragment in the existing URL, typically trimming.
                        // I think this is good...if the user has edited the video, we should start over assuming he
                        // wants all of it.

                        var newSrcAttr = UrlPathString.CreateFromUnencodedString(
                            BookStorage.GetVideoFolderName + Path.GetFileName(videoPath)
                        );
                        HtmlDom.SetSrcOfVideoElement(
                            newSrcAttr,
                            (SafeXmlElement)videoElts[0],
                            true,
                            "?now=" + DateTime.Now.Ticks
                        );
                    }

                    // Likewise, this is probably overkill, but it's a probably-rare case.
                    View.UpdateAllThumbnails();
                    return _pageSelection.CurrentSelection.Id;
                },
                () => { } // wrong state, do nothing
            );
        }

        /// <summary>
        /// When publishing videos in any form but PDF, we want to trim the actual video to just the part that
        /// the user wants to see and add the controls attribute, so that the video controls are visible.
        /// </summary>
        /// <param name="videoContainerElement">bloom-videoContainer element from copied DOM</param>
        /// <param name="sourceBookFolder">This is assumed to be a staging folder, we may replace videos here!</param>
        /// <returns>the new filepath if a video file exists and was copied, empty string if no video file was found</returns>
        public static string PrepareVideoForPublishing(
            SafeXmlElement videoContainerElement,
            string sourceBookFolder,
            bool videoControls
        )
        {
            var videoFolder = Path.Combine(sourceBookFolder, "video");

            var videoElement = videoContainerElement.SelectSingleNode("video") as SafeXmlElement;
            if (videoElement == null)
                return string.Empty;

            // In each valid video element, we remove any timings in the 'src' attribute of the source element.
            var sourceElement = videoElement.SelectSingleNode("source") as SafeXmlElement;
            var srcAttrVal = sourceElement?.GetAttribute("src");
            if (string.IsNullOrEmpty(srcAttrVal))
                return string.Empty;

            string timings;
            var videoUrl = StripTimingFromVideoUrl(srcAttrVal, out timings);

            // Check for valid video file to match url
            var urlWithoutPrefix = UrlPathString.CreateFromUrlEncodedString(videoUrl.Substring(6)); // grab everything after 'video/'
            var originalVideoFilePath = Path.Combine(videoFolder, urlWithoutPrefix.NotEncoded); // any query already removed
            if (!RobustFile.Exists(originalVideoFilePath))
                return string.Empty;

            var tempName = originalVideoFilePath;
            if (
                !string.IsNullOrEmpty(FfmpegProgram)
                && !string.IsNullOrEmpty(timings)
                && IsVideoMarkedForTrimming(sourceBookFolder, videoUrl, timings)
            )
            {
                tempName = Path.Combine(videoFolder, GetNewVideoFileName());
                var successful = TrimVideoUsingFfmpeg(originalVideoFilePath, tempName, timings);
                if (successful)
                {
                    RobustFile.Delete(originalVideoFilePath);
                    var trimmedFileName =
                        BookStorage.GetVideoFolderName + Path.GetFileName(tempName);
                    HtmlDom.SetVideoElementUrl(
                        videoContainerElement,
                        UrlPathString.CreateFromUnencodedString(trimmedFileName, true),
                        false
                    );
                }
                else
                {
                    // probably doesn't exist, but if it does we don't need it.
                    // RobustFile.Delete does not throw if the file doesn't exist.
                    RobustFile.Delete(tempName);
                    tempName = originalVideoFilePath;
                }
            }

            if (videoControls)
            {
                // Add playback controls needed for videos to work in Readium and possibly other epub readers.
                videoElement.SetAttribute("controls", string.Empty);
            }

            return tempName;
        }

        /// <summary>
        /// Loops through all the videoContainers and prepares them for publishing. This includes trimming.
        /// EpubMaker has different requirements and uses a slightly different process [in CopyVideos()],
        /// but BookCompressor.CompressDirectory() for BloomPUB and BloomS3Client.UploadBook() for Upload use this method.
        /// </summary>
        public static void ProcessVideos(
            IEnumerable<SafeXmlElement> videoContainerElements,
            string sourceFolder
        )
        {
            if (videoContainerElements == null) // probably a test
                return;
            // We are counting on this method processing the videos before
            // the recursive CompressDirectory() method gets to the video subdirectory.
            // We are also assuming that 'sourceFolder' is a staging folder (so we can delete modified videos).
            foreach (var videoContainerElement in videoContainerElements)
            {
                PrepareVideoForPublishing(
                    videoContainerElement,
                    sourceFolder,
                    videoControls: false
                );
            }
        }

        private static bool IsVideoMarkedForTrimming(
            string sourceBookFolder,
            string videoUrl,
            string rawTimings
        )
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

        private static bool TrimVideoUsingFfmpeg(
            string sourceVideoFilePath,
            string destinationPath,
            string timings
        )
        {
            Guard.Against(
                string.IsNullOrEmpty(FfmpegProgram),
                "Caller should have verified 'ffmpeg' existence."
            );
            var timingArray = timings.Split(',');
            var startTiming = ConvertSecondsToHhMmSsString(timingArray[0]);
            var endTiming = ConvertSecondsToHhMmSsString(timingArray[1]);
            // Run ffmpeg on the file to trim it down using the timings.
            // -hide_banner = don't write all the version and build information to the console
            // -i <path> = specify input file
            // -ss HH:MM:SS.T = trim to this start time
            // -to HH:MM:SS.T = trim to this end time
            // -c:v copy -c:a copy = copy video (and audio) streams with no codec modification
            var parameters =
                $"-hide_banner -i \"{sourceVideoFilePath}\" -ss {startTiming} -to {endTiming} -c:v copy -c:a copy \"{destinationPath}\"";
            var result = CommandLineRunnerExtra.RunWithInvariantCulture(
                FfmpegProgram,
                parameters,
                "",
                60,
                new NullProgress()
            );
            if (result.DidTimeOut)
            {
                Logger.WriteEvent("ffmpeg timed out trimming video for publication");
                return false;
            }

            var output = result.StandardError;
            if (
                string.IsNullOrWhiteSpace(output)
                || output.Contains("Invalid data found when processing input")
            )
            {
                Logger.WriteEvent("ffmpeg did not return normal output");
                return false;
            }

            return true;
        }

        private static string ConvertSecondsToHhMmSsString(string seconds)
        {
            var time = TimeSpan.FromSeconds(double.Parse(seconds, CultureInfo.InvariantCulture));
            return time.ToString(@"hh\:mm\:ss\.f");
        }

        private static decimal ConvertHhMmSsStringToSeconds(string hhmmss)
        {
            var formatString = hhmmss.Length > 7 ? "HH:mm:ss.f" : "mm:ss.f";
            var dt = DateTime.ParseExact(hhmmss, formatString, CultureInfo.InvariantCulture);
            return dt.Hour * 3600m + dt.Minute * 60m + dt.Second + dt.Millisecond / 1000m;
        }
    }
}
