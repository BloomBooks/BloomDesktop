using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using Sentry;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Publish.Video
{
    /// <summary>
    /// This class manages a window that appears when the 'Record' button is pressed in the
    /// Video sub-pane of the Publish tab. The entire content of the window is (and must remain)
    /// an instance of Browser containing an instance of BloomPlayer configured to autoplay
    /// the book. FFMpeg is configured to create a video of everything that happens in the
    /// window until the autoplay completes. Then we get a notification from the player of
    /// what audio was played during the playback. Ffmpeg is used again to merge this audio into
    /// the video (or just combine them, if audio output is selected).
    /// </summary>
    public partial class RecordVideoWindow : Form
    {
        // We only want to calculate this once per run and cache it.
        private static Resolution s_maxResolution;
        private static Resolution s_screenResolution;

        private UserControl _content;
        private Process _ffmpegProcess;
        private bool _ffmpegExited;
        private StringBuilder _errorData;
        private DateTime _startTimeForVideoCapture;
        private string _videoOnlyPath;
        private string _ffmpegPath;
        private TempFile _htmlFile;
        private TempFile _initialVideo;
        private TempFile _capturedVideo;
        private TempFile _finalVideo;
        private TempFile _ffmpegProgressFile;
        private bool _recording = true;
        private int _numIterationsDone;
        private int _lastIterationPrinted = 0;
        private bool _saveReceived;
        private Book.Book _book;
        private string _pathToRealBook;
        private BloomWebSocketServer _webSocketServer;
        private int _videoHeight = 720; // default, facebook
        private int _videoWidth = 1280;
        private Codec _codec = Codec.H264;
        private string _pageReadTime = "3.0"; // default for pages without narration
        private string _videoSettingsFromPreview;
        private bool _shouldRotateBook = false;
        private bool _shouldUseOriginalPageSize = false;
        private bool _showFullScreen;
        private int[] _pageRange = new int[0];

        // H.263, at least in its original revision, only supports certain specific resolutions, e.g. CIF = 352x288
        // Notably, it is necessary for it to be 352x288, not the inverse 288x352. (Revision 2 supposedly allows flexible resolutions)
        // If false, then we just do the simple thing: make the window 352x288, put a portrait book in the middle (with big blank sidebars on each side), and call it a day
        // If true, this can make the book appear bigger by making the window portrait-sized 288x352, then recording a rotated video.
        //   The pro is that the book is bigger. The con is that any video playing software will play it sideways. But if you just turn your device sideways, then you're golden.
        // I tried having portrait books target H.263+ (-vcodec h263p), but it says its invalid for the container. I tried all the other variations I could think to try;
        // this approach (targeting a newer H263) seems like a dead end.
        private const bool _rotatePortraitH263Videos = false;

        // 30fps is standard for SD video
        private const int kFrameRate = 30;

        public bool LocalAudioNamesMessedUp = false;

        public RecordVideoWindow(BloomWebSocketServer webSocketServer)
        {
            InitializeComponent();
            _webSocketServer = webSocketServer;
            // If we don't have a webSocketServer, we're just creating this to get our max resolution and don't
            // need/want the overhead of creating a browser and cleaning it up. See BL-12164.
            _content = webSocketServer == null ? new UserControl() : BrowserMaker.MakeBrowser();
            _content.Dock = DockStyle.Fill;
            _content.AutoScaleMode = AutoScaleMode.None;
            Controls.Add(_content);
            AutoScaleMode = AutoScaleMode.None;

            // We have to capture a region of the screen (see Gdigrab args below).
            // The following prevents the window being moved, which is good since doing so
            // will mess up the recording; but then you lose the title and close box, so
            // there's no way to cancel.
            //FormBorderStyle = FormBorderStyle.None;
            TopMost = true; // capturing a screen area we really don't want things on top of the window.

            // force handles to be created
            _ = Handle;
            _ = _content.Handle;
        }

        // This override prevents the window from being moved.
        protected override void WndProc(ref Message message)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MOVE = 0xF010;

            switch (message.Msg)
            {
                case WM_SYSCOMMAND:
                    int command = message.WParam.ToInt32() & 0xfff0;
                    if (command == SC_MOVE)
                        return;
                    break;
            }

            base.WndProc(ref message);
        }

        // Used for various kinds of cleanup, previously hooked to the Close event.
        // In normal recording, this is raised when we are done with the post-processing
        // of a recording (which now happens distinctly after this window is closed).
        // It also gets raised if the user aborts the recording by closing the
        // recording window.
        public event EventHandler FinishedProcessingRecording;

        public static RecordVideoWindow Create(BloomWebSocketServer webSocketServer)
        {
            // Creating the window with the thread set to PerMonitorAwareV2 should mean that its
            // size is set in real pixels, and we can record at the resolution we want, even if
            // the display is scaled. I could not get it to work; the window appears to be the
            // correct size, but (at 150%, anyway) ffmpeg thinks it is one pixel smaller, which makes
            // the size odd, and nothing gets saved. Also, when the window is created this way,
            // something weird happens during playback, with a flash of a smaller page showing up
            // before the correct appearance. Keeping this in comments in case we want to try again.
            // However, a more promising approach might be to ship a separate program that is fully
            // DPIAware and does nothing but display the recording window correctly.
            // If you get this working, remember to check for running on Windows 10.
            //var originalAwareness = SetThreadDpiAwarenessContext(ThreadDpiAwareContext.PerMonitorAwareV2);
            var result = new RecordVideoWindow(webSocketServer);

            // These lines go with the DpiAwareness trick. If we ever get it working, we'll want
            // the new window to be on the right screen before we make any measurments.
            //var mainWindow = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
            //if (mainWindow != null)
            //{
            //	result.StartPosition = FormStartPosition.Manual;
            //	var bounds = Screen.FromControl(mainWindow).Bounds;
            //	result.Location = bounds.Location;
            //	var bounds2 = Screen.FromControl(result).Bounds;
            //}

            // SetThreadDpiAwarenessContext(originalAwareness);
            return result;
        }

        public void SetPageRange(int[] range)
        {
            _pageRange = range;
        }

        /// <summary>
        /// As we show the window, we immediately load Bloom player into the browser and start
        /// navigating to it.
        /// </summary>
        public void Show(string bookUrl, string pathToRealBook)
        {
            this.WindowState = FormWindowState.Normal; // should be default, but make sure
            if (_showFullScreen)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.Bounds = new Rectangle(0, 0, _videoWidth, _videoHeight);
                // Right-click closes the window, aborting the recording.  This is a bit of
                // a hack, but it works.  If we brought up the normal context menu instead,
                // that would ruin the recording anyway.
                ((Browser)_content).ReplaceContextMenu = () =>
                {
                    this.Close();
                }; // OnClose() handles cleanup.
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
            }
            _pathToRealBook = pathToRealBook;
            // We don't need to spend any time on pages that have no narration if we're not
            // recording video. (Review: I suppose its technically possible that something
            // really important is happening in the 'background music' stream. If that becomes
            // a problem, we may have to somehow encourage, but not enforce, zero pageReadTime
            // for audio-only output.
            var pageReadTime = (_codec == Codec.MP3 ? "0" : _pageReadTime);
            string pageRangeParams =
                _pageRange.Length == 2
                    ? $"&start-page={_pageRange[0]}&autoplay-count={_pageRange[1] - _pageRange[0] + 1}"
                    : "";

            var bloomPlayerUrl =
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                + "bloom-player/dist/bloomplayer.htm?centerVertically=true&reportSoundLog=true&initiallyShowAppBar=false&autoplay=yes&hideNavButtons=true&url="
                // This is strange. bookUrl is already partially encoded. For example, a title of `a%` is already `a%25`.
                // But what we actually need for `a%` is `a%2525`. This is confusing but matches what we do for the preview in js code:
                // encodeURIComponent(bookUrl) + // Need to apply encoding to the bookUrl again as data to use it as a parameter of another URL
                // See BL-11319.
                + UrlPathString.CreateFromUnencodedString(bookUrl, true).UrlEncoded
                + $"&independent=false&host=bloomdesktop&defaultDuration={pageReadTime}&useOriginalPageSize={_shouldUseOriginalPageSize}&skipActivities=true{pageRangeParams}";
            // The user can make choices in the preview instance of BloomPlayer...currently language and
            // whether to play image descriptions...that need to be communicated to the recording window.
            // If we received any, pass them on.
            if (_videoSettingsFromPreview != null)
            {
                bloomPlayerUrl += $"&videoSettings={_videoSettingsFromPreview}";
            }

            string url;
            if (!_shouldRotateBook)
            {
                this.Text = LocalizationManager.GetString(
                    "PublishTab.RecordVideo.RecordingInProgress",
                    "Recording in Progress..."
                );
                url = bloomPlayerUrl;
            }
            else
            {
                this.Text = LocalizationManager.GetString(
                    "PublishTab.RecordVideo.RecordingInProgressSideways",
                    "Recording in Progress. Showing sideways in order to fit on your screen."
                );
                GenerateRotatedHtml(bloomPlayerUrl);
                url = _htmlFile.Path.ToLocalhost();
            }

            // _content is not defined as Browser in case we don't want to create a real browser.
            // See constructor.
            ((Browser)_content).Navigate(url, false);

            // Couldn't get this to work. See comment in constructor.
            //_originalAwareness = SetThreadDpiAwarenessContext(ThreadDpiAwareContext.PerMonitorAwareV2);
            // Extra space we need around the recordable content for title bars etc.
            var deltaV = this.Height - _content.Height;
            var deltaH = this.Width - _content.Width;
            // Make the window an appropriate size so the content area gives the resolution we want.
            // (We've already made an adjustment if the screen isn't that big.)
            Height = (_shouldRotateBook ? _videoWidth : _videoHeight) + deltaV;
            Width = (_shouldRotateBook ? _videoHeight : _videoWidth) + deltaH;
            // Try to bring the preview up on the same screen as Bloom itself, in the top left so we
            // have all the space available if we need it.
            var mainWindow = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
            if (mainWindow != null)
            {
                StartPosition = FormStartPosition.Manual;
                var bounds = Screen.FromControl(mainWindow).Bounds;
                Location = bounds.Location;
            }

            Show(mainWindow);
        }

        /// <summary>
        /// Generates a temporary html file that instead of displaying the book right side up, displays the book sideways.
        /// The temp file location can be retrieved at _htmlFile
        /// </summary>
        /// <remarks>Same algorithm works for either portrait or landscape books</remarks>
        /// <param name="bloomPlayerUrl"></param>
        private void GenerateRotatedHtml(string bloomPlayerUrl)
        {
            // position:absolute is needed to get rid of the scrollbars
            // (the scrollbars appear because with the rotate transform, assuming a portrait video, despite the VISUAL  element visually appearing
            // to be rotated and now less high, the LITERAL element still occupies the old (very tall) amount of pixels.
            // This is placed in a container that is of short height. So, it's considered to overflow and needs a scrollbar.
            // Position:absolute gets rid of that though.
            //
            // Explanation of transform:
            // Define the transformations (particularly the rotation) to be around the top-right corner (transform-origin). (Note: the default, center, is a lot harder to explain the numbers for)
            // Move the top-right corner into its final position.
            // Rotate 90 degrees counterclockwise (technically, a 270 degree clockwise rotation), pivoting on the top-right corner
            // Voila.
            string htmlContents =
                $@"
<html>
	<head>
		<meta charset=""UTF-8"">
	</head>
	<body style=""margin: 0; width: {_videoHeight}px; height: {_videoWidth}px"">
		<iframe
			src = ""{XmlString.FromUnencoded(bloomPlayerUrl).Xml}""
			style = ""position:absolute; width: {_videoWidth}px; height: {_videoHeight}px; transform-origin: top right; transform: translateX(-{_videoWidth}px) rotate(270deg); border: 0; ""
			allowfullscreen
			allow = ""fullscreen""
		/>
	</body>
</html>";

            // If the assert fails, it's a weird situation but we'll just allow this current function to continue with a brand new file
            // We won't mess with the old file either, so if anything is still running using the old file, it'll still work
            // It would be nice to check out the cause though and see if there's a bug preventing the temp file from getting cleaned up at the expected time.
            Debug.Assert(
                _htmlFile == null,
                "Found existing temporary HTML file which was not properly cleaned up"
            );
            _htmlFile = BloomTemp.TempFileUtils.GetTempFileWithPrettyExtension("html");

            RobustFile.WriteAllText(_htmlFile.Path, htmlContents);
        }

        // As window is loaded, we let Bloom Player know we are ready to start recording.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _webSocketServer.SendString("recordVideo", "recording", "true");
            _webSocketServer.SendString("recordVideo", "ready", "false");
            _initialVideo = BloomTemp.TempFileUtils.GetTempFileWithPrettyExtension(
                _codec.ToExtension()
            );
            _videoOnlyPath = _initialVideo.Path;
            RobustFile.Delete(_videoOnlyPath);
        }

        /// <summary>
        /// We get a notification through the API when bloom player has loaded the first page content
        /// and is in a good state for us to start recording video from the window content.
        /// </summary>
        public void StartFfmpegForVideoCapture()
        {
            // Enhance: what on earth should we do if it's not found??

            // If we're doing audio there's no more to do just now; we will play the book
            // visually, but we don't need to record what happens.
            if (_codec == Codec.MP3)
            {
                _startTimeForVideoCapture = DateTime.Now;
                return;
            }

            string videoArgs;
            if (_codec == Codec.H263)
            {
                bool portrait = _videoWidth < _videoHeight;
                videoArgs =
                    $"-vcodec h263 -vf \"{((_rotatePortraitH263Videos && portrait) || _shouldRotateBook ? "transpose=1," : "")} scale=352:288\" ";
            }
            else
            {
                // libx264 is the standard encoder for H.264, which Wikipedia says is the most used (91%)
                // video compression format. ffmpeg will use H.264 by default, but I think we have to
                // specify the encoder in order to give it parameters. H.264 has a variety of 'profiles',
                // and the one used by default, which I think may be High 4:4:4, is not widely supported.
                // For example, it won't open on Windows with Media Player, Movies and TV, Photos, or
                // Windows Media Player, nor in Firefox, nor in either of the apps suggested for mp4 files
                // on my Android 11 device. The 'main' profile specified here seems to be
                // much better, and opened in everything I tried. However, by default 'main' tries to use
                // 4:4:4 and gives an error message. Some pix_fmt seems to be needed, and this one works
                // in all the above places, though I'm not clear exactly what it does.
                // To sum up, this substring, which needs to come after the inputs and before the output,
                // tells it to make an H.264 compressed video of a type (profile) that most software can work with.
                videoArgs = "-vcodec libx264 -profile:v main -pix_fmt yuv420p ";

                if (_shouldRotateBook)
                {
                    videoArgs += "-vf \"transpose=1\" ";
                }
            }

            // Configure ffmpeg to record the video.
            // Todo Linux (BL-11011): gdigrab is Windows-only, we'll need to find something else.
            // I believe ffmpeg has an option to capture the content of an XWindow.

            var height = _content.Height;
            if (height % 2 > 0)
                height--;
            var width = _content.Width;
            if (width % 2 > 0)
                width--;
            var screenPoint = PointToScreen(new Point(0, 0));
            var offsetY = screenPoint.Y;
            var offsetX = screenPoint.X;
            var areaCaptureArgs =
                $"-video_size {width}x{height} -offset_x {offsetX} -offset_y {offsetY} -i desktop ";
            // This alternative code works with Gecko provided all screens are set to 100% scale.
            // We have to capture a region of the screen instead (using the code above) for WebView2
            // because GdiGrab of Window content doesn't work with WV2, probably because
            // it uses GPU to render.
            // We would rather capture a window content if we could, since it would
            // continue to work even if the user moves the window, and I think even
            // if they cover it with another window. However, even with Gecko,
            // there is a problem with window capture if the primary window scale is
            // not 100%, even though (as we require) the window where Bloom is running
            // and capturing IS at 100%. What seems to happen is that ffmpeg calculates
            // the area to capture by something like multiplying the ClientRectangle
            // of the window by the primary monitor scale. At best, this captures an
            // area that is too large. At worst, the result is an odd number for the width,
            // and video capture fails altogether. And we're moving away from Gecko anyway.
            // So, I think it's best to use the area capture approach everywhere.
            // var areaCaptureArgs = $"-i title=\"{Text}\" "; // rendering with Gecko we can just capture the window.

            var args =
                "-f gdigrab " // basic command for using a window (in a Windows OS) as a video input stream
                + $"-framerate {kFrameRate} " // frames per second to capture
                + "-draw_mouse 0 " // don't capture any mouse movement over the window
                + areaCaptureArgs
                //+ $"-i title=\"desktop\" " // identifies the window for gdigrab

                + videoArgs
                + "\""
                + _videoOnlyPath
                + "\""; // the intermediate output file for the recording.
            //Debug.WriteLine("ffmpeg capture args: " + args);
            RunFfmpeg(args);
            _startTimeForVideoCapture = DateTime.Now;
        }

        // convert urls received in sound log to actual local file paths.
        string UrlToFile(string input)
        {
            var result = input.FromLocalhost();
            // If we added a param to force reloading, remove it.
            var index = result.IndexOf("?");
            if (index >= 0)
                result = result.Substring(0, index);
            return result;
        }

        /// <summary>
        /// Received through API when bloom-player has played the whole book.
        /// Comes with data from BP about sounds played.
        /// We will stop our ffmpeg video capture if one is in progress.
        /// Then we will run another copy of ffmpeg to merge these sounds (and the video,
        /// if any).
        /// </summary>
        /// <param name="soundLogJson"></param>
        public async Task StopRecordingAsync(string soundLogJson)
        {
            // Couldn't get this to work. See comment in constructor. If we do get it working,
            // make sure it gets turned off, however things turn out. Or maybe we can turn it
            // off much sooner, without waiting for the recording to finish? Waiting until this
            // point didn't seem to help.
            //SetThreadDpiAwarenessContext(_originalAwareness);

            // Make sure that OnClosed() won't dispose of anything we need.
            _capturedVideo = _initialVideo; // save so we can dispose eventually
            _initialVideo = null; // prevent automatic dispose in OnClosed

            // ffmpeg hasn't been set up yet for audio-only recording; we only use it during the last phase.
            if (_ffmpegProcess != null)
            {
                // Stop the recording BEFORE we close the window, otherwise, we capture a bit of it fading away.
                Debug.WriteLine("Telling ffmpeg to quit");
                _ffmpegProcess.StandardInput.WriteLine("q");
                _ffmpegProcess.WaitForExit();
            }

            ClearPreventSleepTimer();
            Close();

            await BrowserProgressDialog.DoWorkWithProgressDialogAsync(
                _webSocketServer,
                async (progress, worker) =>
                {
                    StopRecordingInternal(progress, soundLogJson);
                    FinishedProcessingRecording?.Invoke(this, new EventArgs());

                    // we decided to always show this until the user closes it
                    return true;

                    // if we want to close it automatically if there are no errors:
                    //return progress.HaveProblemsBeenReported;
                },
                "avPublish",
                "Processing Video",
                showCancelButton: false
            );
        }

        private void StopRecordingInternal(IWebSocketProgress progress, string soundLogJson)
        {
            var videoDuration = DateTime.Now - _startTimeForVideoCapture;
            var haveVideo = _codec != Codec.MP3;
            if (haveVideo)
            {
                progress.Message(
                    "PublishTab.RecordVideo.FinishingInitialVideoRecording",
                    message: "Finishing initial video recording"
                );

                // Leaving this here in case we decide it is helpful to report to the user.
                // It can also be helpful when debugging certain things.
                //var msg = string.Format(
                //	LocalizationManager.GetDynamicString(
                //		"Bloom",
                //		"PublishTab.RecordVideo.VideoDuration",
                //		"Video duration is {0:hh\\:mm\\:ss}",
                //		"{0:hh\\:mm\\:ss} is a duration in hours, minutes, and seconds"),
                //	videoDuration);
                //progress.MessageWithoutLocalizing($"- {msg}");

                // Enhance: if something goes wrong, it may be useful to capture this somehow.
                //Debug.WriteLine("full ffmpeg error log: " + _errorData.ToString());
                var errors = _errorData.ToString();
                if (!RobustFile.Exists(_videoOnlyPath) || new FileInfo(_videoOnlyPath).Length < 100)
                {
                    Logger.WriteError(new ApplicationException(errors));
                    progress.MessageWithoutLocalizing("Video capture failed", ProgressKind.Error);
                    _recording = false;
                    return;
                }
            }

            //Debug.WriteLine(soundLogJson);
            var soundLogObj = DynamicJson.Parse(soundLogJson);

            // Process the soundLog into C# objects.
            // var soundLog = soundLogObj.Deserialize<SoundLogItem[]>() doesn't work, don't know why.
            int count = soundLogObj.Count;

            var soundLog = new SoundLogItem[count];
            for (int i = 0; i < count; i++)
            {
                var item = soundLogObj[i];
                var sound = new SoundLogItem()
                {
                    src = UrlToFile((string)item.src),
                    volume = item.volume,
                    startTime = DateTime.Parse(item.startTime)
                };
                if (item.IsDefined("endTime"))
                {
                    sound.endTime = DateTime.Parse(item.endTime);
                }

                sound.startOffset = sound.startTime - _startTimeForVideoCapture;

                soundLog[i] = sound;
            }

            _finalVideo = BloomTemp.TempFileUtils.GetTempFileWithPrettyExtension(
                _codec.ToExtension()
            );
            var finalOutputPath = _finalVideo.Path;
            RobustFile.Delete(finalOutputPath);
            if (soundLog.Length == 0)
            {
                if (!haveVideo)
                {
                    return; // can't do anything useful!
                }

                RobustFile.Copy(_videoOnlyPath, finalOutputPath);
                GotFullRecording = true;
                // Allows the Check and Save buttons to be enabled, now we have something we can play or save.
                _webSocketServer.SendString("recordVideo", "ready", "true");
                progress.MessageWithParams(
                    "Common.FinishedAt",
                    "{0:hh:mm tt} is a time",
                    "Finished at {0:hh:mm tt}",
                    ProgressKind.Progress,
                    DateTime.Now
                );
                return;
            }

            // configure ffmpeg to merge everything.

            // There are multiple limits which mean we need to cap how many files we process at a time.
            // One is the command line length limit, which caps us out at around 2900.
            // Apart from that, ffmpeg will have "too many open files" error if there are too many files.
            // This occurs somewhere inbetween 2036 and 2047 in my environment.
            // To be conservative (since I'm not sure if this is consistent from environment to environment),
            // let's just do 1,000 at a time.
            const int batchSize = 1000;
            bool isSuccess = MergeAudioFilesInBatches(
                progress,
                soundLog,
                haveVideo,
                finalOutputPath,
                videoDuration,
                batchSize
            );

            if (!isSuccess)
                return;
            _recording = false;
            GotFullRecording = true;
            // Allows the Check and Save buttons to be enabled, now we have something we can play or save.
            _webSocketServer.SendString("recordVideo", "ready", "true");
            // Don't think this ever happens now...if we allowed the user to click Save before the recording
            // was complete, this would be the time to proceed with saving.
            if (_saveReceived)
            {
                // Reusing id from epub. (not creating a new one or extracting to common at this point as we don't think this is ever called)
                progress.Message("PublishTab.Epub.Saving", message: "Saving");
                SaveVideo(); // now we really can.
            }
            progress.MessageWithParams(
                "Common.FinishedAt",
                "{0:hh:mm tt} is a time",
                "Finished at {0:hh:mm tt}",
                ProgressKind.Progress,
                DateTime.Now
            );
        }

        private bool MergeAudioFilesInBatches(
            IWebSocketProgress progress,
            SoundLogItem[] soundLog,
            bool haveVideo,
            string finalOutputPath,
            TimeSpan videoDuration,
            int batchSize
        )
        {
            progress.Message("PublishTab.RecordVideo.ProcessingAudio", message: "Processing audio");

            bool isSuccess = true;
            _numIterationsDone = 0;

            // In some very complex books, we are running into the 32K limit for the length of the args
            // string that can be passed to Process.Start. We can put the complex_filter into a text file,
            // but there appears to be no way to put the actual list of inputs into one. So the only
            // way to shorten it is to use as little text as possible for each one. By setting a working
            // directory for ffmpeg, we only have to give the file name relative to that directory.
            // By generating a short name for each file, we use as few characters as possible: GetShortName
            // will produce no more than 3-character names for up to about 46,000 files. Since most of
            // them will be three-character names, the string we generate for each works out to be 11 characters.
            // (-i<space>xxx.mp3<space>).
            // Everything else is more-or-less fixed length, so we can cope with about 2900 audio files.
            // If that isn't enough, we could go to multiple passes...but that really complicates
            // the code, especially the code for estimating run time. The pathological picture dictionary
            // in BL-11401 has 1388, so we should still have a good margin.
            // Review: it would save a little time on nearly all books to NOT rename the files unless
            // we need to. (That is, if the command line using the original names is too long.)
            // But it would complicate the code to try to figure this out, and I don't think this is a
            // major part of the time recording typically takes. Also, the rename branch would only get tested
            // when a recording involves more than about 650 files, so we'd be unlikely to catch any
            // new problems with it.
            // Another reviewer noted that it's conceivable some anti-virus will consider wholesale
            // file renaming to be suspicious behavior. Decided to wait and see if this happens.
            var workingDirectory = Path.GetDirectoryName(soundLog[0].src);
            LocalAudioNamesMessedUp = true;
            var renames = new Dictionary<string, string>();
            int shortNameIndex = 0;
            string[] origAudioFileNames = Directory.GetFiles(workingDirectory);

            for (int i = 0; i < soundLog.Length; i++)
            {
                var item = soundLog[i];
                if (Path.GetDirectoryName(item.src) == workingDirectory)
                {
                    string shortName = "";
                    while (
                        shortName == ""
                        || origAudioFileNames.Contains(Path.Combine(workingDirectory, shortName))
                    )
                    {
                        shortName = GetShortName(shortNameIndex) + Path.GetExtension(item.src);
                        shortNameIndex++;
                    }
                    if (renames.TryGetValue(item.src, out string prevShortName))
                    {
                        // If we already saw (and renamed) this item, just use what we already renamed it to.
                        item.shortName = prevShortName;
                    }
                    else
                    {
                        RobustFile.Move(item.src, Path.Combine(workingDirectory, shortName));
                        item.shortName = shortName; // deliberately without the full path, we will set workingDirectory.
                        renames[item.src] = shortName;
                    }
                }
                else
                {
                    item.shortName = "\"" + item.src + "\"";
                }
            }

            // Narration is never truncated, so always has a default endTime.
            // If the last music ends up not being truncated (unlikely), I think we can let
            // it play to its natural end. In this case pathologically we might fade some earlier music.
            // But I think BP will currently put an end time on any music that didn't play to the end and
            // then start repeating.
            var lastMusic = soundLog.LastOrDefault(item => item.endTime != default(DateTime));

            var startTime = DateTime.Now;
            if (haveVideo)
                progress.MessageWithParams(
                    "PublishTab.RecordVideo.MergingAudioVideo",
                    "{0:hh:mm tt} is a time",
                    "Merging audio and video, starting at {0:hh:mm tt}",
                    ProgressKind.Progress,
                    startTime
                );
            else
                progress.Message(
                    "PublishTab.RecordVideo.FinalizingAudio",
                    message: "Finalizing audio"
                );

            int numIterationsNeeded = (int)Math.Ceiling(((float)soundLog.Length) / batchSize);
            _lastIterationPrinted = 0;
            // System.Threading.Timer is the only one which will fire during _ffmpegProcess.WaitForExit()
            using (
                new System.Threading.Timer(
                    (state) =>
                    {
                        OutputProgressEstimate(
                            progress,
                            startTime,
                            numIterationsNeeded,
                            videoDuration
                        );
                    },
                    null,
                    5000, // initially at 5 seconds
                    60000 // then every 1 minute thereafter
                )
            )
            {
                using (var tempOutputs = new Utils.DisposableList<TempFile>())
                {
                    bool videoHasAudio = false;
                    string inputVideoPath = _videoOnlyPath;
                    for (int i = 0; i < soundLog.Length; i += batchSize)
                    {
                        // Prepare a temporary output file to write this iteration
                        string outputPath;
                        bool isFinalIteration = i + batchSize >= soundLog.Length;
                        if (!isFinalIteration)
                        {
                            var tempOutput = BloomTemp.TempFileUtils.GetTempFileWithPrettyExtension(
                                _codec.ToExtension()
                            );
                            tempOutputs.Add(tempOutput);
                            outputPath = tempOutput.Path;
                        }
                        else
                        {
                            // On the final iteration, write directly to finalOutputPath
                            outputPath = finalOutputPath;
                        }
                        RobustFile.Delete(outputPath);

                        // Process one batch of files
                        var soundLogSubset = soundLog.Skip(i).Take(batchSize);
                        isSuccess =
                            isSuccess
                            && MergeAudioFiles(
                                progress,
                                soundLogSubset,
                                lastMusic,
                                haveVideo,
                                videoHasAudio,
                                inputVideoPath,
                                outputPath,
                                workingDirectory
                            );

                        if (isSuccess)
                        {
                            // Success. Prepare for next iteration.
                            inputVideoPath = outputPath;
                            videoHasAudio = true;
                        }
                        else
                            break;
                    }
                }
            }

            if (isSuccess)
            {
                progress.MessageWithoutLocalizing($"- Progress: 100%");
            }

            // Restore original names, since that's what is expected if we preview the book or record again
            // (the html is still referencing the original names...the renaming was solely for the process
            // of running ffmpeg afterwards.)
            // If anything goes wrong so that this doesn't happen, we should automatically rebuild the
            // preview entirely.
            foreach (var kvp in renames)
            {
                RobustFile.Move(Path.Combine(workingDirectory, kvp.Value), kvp.Key);
            }

            LocalAudioNamesMessedUp = false;

            return isSuccess;
        }

        /// <summary>
        /// Merge the specified sound items into the video
        /// </summary>
        /// <returns>True if merging was successful. False otherwise.</returns>
        private bool MergeAudioFiles(
            IWebSocketProgress progress,
            IEnumerable<SoundLogItem> soundLog,
            SoundLogItem lastMusic,
            bool haveVideo,
            bool videoHasAudio,
            string inputVideoPath,
            string outputPath,
            string workingDirectory
        )
        {
            var soundLogCount = soundLog.Count();

            // each sound file becomes an input by prefixing the path with -i.
            var inputs = string.Join(" ", soundLog.Select(item => $"-i {item.shortName} "));

            // arguments to configure 'filters' ahead of audio mixer which will combine the sounds into a single stream.
            var audioFilters = string.Join(
                " ",
                soundLog.Select(
                    (item, index) =>
                    {
                        // for each input,...
                        var result = $"[{index}:a]"; // start with a label which refers to the relevant input stream
                        // if we stopped playback early, we need an argument that will truncate it in the mixer.
                        if (item.endTime != default(DateTime))
                        {
                            var duration = (item.endTime - item.startTime);
                            result +=
                                $"atrim=end={duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)},";
                            if (item == lastMusic)
                            {
                                // Make sure the fadeDuration is at least slightly less than the actual duration,
                                // so the fade start time will be positive. On longer sounds we aim for two seconds.
                                var fadeDuration = Math.Min(2, duration.TotalSeconds - 0.001);
                                // Fades its input (the output of the trim).
                                // - t=out makes it fade out (at the end) rather than in (at the beginning)
                                // - st=x makes the fade start x seconds from the start (and so fadeDuration from the end)
                                // - d=n makes the fade last for n seconds
                                result +=
                                    $"afade=t=out:st={(duration.TotalSeconds - fadeDuration).ToString(CultureInfo.InvariantCulture)}:d={fadeDuration.ToString(CultureInfo.InvariantCulture)},";
                            }
                        }

                        // Add instructions to delay it by the right amount
                        var delay = item.startTime - _startTimeForVideoCapture;
                        // all=1: in case the input is stereo, all channels of it will be delayed.
                        // We shouldn't get negative delays, since startTime
                        // is recorded during a method that completes before we return
                        Debug.Assert(delay.TotalMilliseconds >= 0);
                        result +=
                            $"adelay={Math.Max(delay.TotalMilliseconds, 0).ToString(CultureInfo.InvariantCulture)}:all=1";

                        // possibly reduce its volume.
                        if (item.volume != 1.0)
                        {
                            result +=
                                $",volume={item.volume.ToString(CultureInfo.InvariantCulture)}";
                        }

                        // add another label by which the mixer will refer to this stream
                        result += $"[a{index}]; ";
                        return result;
                    }
                )
            );

            // labels of all the audio filter outputs provide the input to the mixer
            // (We're only really doing mixing if there's background music; the other audio
            // streams never overlap. There may be a possible enhancement that would improve
            // quality somewhat if we could avoid decoding and re-encoding when there is no
            // overlap.)
            var mixInputs = string.Join("", soundLog.Select((item, index) => $"[a{index}]"));
            // the video, if any, will be one more input after the audio ones and will be at this index
            // in the inputs.
            var videoIndex = soundLogCount;

            string audioArgs;
            switch (_codec)
            {
                case Codec.H263:
                    audioArgs = "-acodec aac -ar 8000 ";
                    break;
                case Codec.H264:
                    // For MP4 videos, should we we specify MP3 for the SoundHandler instead of the ffmpeg's default SoundHandler, AAC?
                    // https://www.movavi.com/learning-portal/aac-vs-mp3.html says there is marginally more compatibility for mp3,
                    // but it's talking about audio files, not the audio codec within a mp4.
                    // https://stackoverflow.com/questions/9168954/should-i-use-the-mp3-or-aac-codec-for-a-mp4-file/25718378)
                    // has ambiguous answers, but we decided in favor of AAC (the default).
                    audioArgs = "";
                    break;
                case Codec.MP3:
                    audioArgs = "-acodec libmp3lame " + "-b:a 64k "; // Set the bitrate for the audio stream to 64 kbps
                    // For audio Sample Rate (-ar), I read suggestions to use 44.1 KHz stereo,
                    // unless the input source is 48KHz, in which just use that directly
                    // (for radio-level, you can use 22.05 kHz mono)
                    // I see ffmpeg default produce a lot of 44.1 KHz results, which is perfectly adequate if it keeps up.
                    // Just leaving it as the default unless need shows otherwise
                    break;
                default:
                    throw new NotImplementedException();
            }

            var complexFilter =
                audioFilters // specifies the inputs to the mixer
                // mix those inputs to a single stream called out. Note that, because most of our audio
                // streams don't overlap, and the background music volume is presumed to have already
                // been suitably adjusted, we do NOT want the default behavior of 'normalizing' volume
                // by reducing it to 1/n where n is the total number of input streams.
                //
                // If merging the audio in passes (to avoid the limit on how many inputs we can handle),
                // we also want to pass through the audio from the prior iteration (in the input video already).
                // FFMPEG errors out if the input video doesn't have an audio stream, so check first.
                + mixInputs
                + (videoHasAudio ? $"[{videoIndex}:a]" : "")
                + $"amix=inputs={soundLogCount + (videoHasAudio ? 1 : 0)}:normalize=0[out]";
            using (var filterFile = new TempFile(complexFilter))
            {
                using (_ffmpegProgressFile = TempFile.CreateAndGetPathButDontMakeTheFile())
                {
                    var args =
                        ""
                        + inputs // the audio files are inputs, which may be referred to as [1:a], [2:a], etc.
                        + (haveVideo ? $"-i \"{inputVideoPath}\" " : "") // last input (videoIndex) is the original video (if any)
                        + "-filter_complex_script \"" // the next bit specifies a filter with multiple inputs
                        + filterFile.Path
                        + "\" "
                        // copy the video channel (of input videoIndex) unchanged (if we have video).
                        // (here 'copy' is a pseudo codec...instead of encoding it in some particular way,
                        // we just copy the original.
                        + (haveVideo ? $"-map {videoIndex}:v -vcodec copy " : "")
                        + audioArgs
                        + "-map [out] " // send the output of the audio mix to the output
                        + $"\"{outputPath}\"" //and this is where we send it (until the user saves it elsewhere).
                        + $" -progress \"{_ffmpegProgressFile.Path}\"";
                    // Debug.WriteLine("ffmpeg merge args: " + args);

                    // This magic number is documented at https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.arguments?view=net-6.0
                    if (args.Length >= 32699)
                    {
                        // This API requires a localization ID, but as we hope no one will ever see it, I'm not actually
                        // adding it to the XLF.
                        progress.Message(
                            "PublishTab.RecordVideo.TooManyAudioFiles",
                            "This recording has more audio files than Bloom can handle. Try recording the book in smaller sections. You can then join the sections using some other video software.",
                            ProgressKind.Error
                        );
                    }
                    else
                    {
                        RunFfmpeg(args, workingDirectory);

                        _ffmpegProcess.WaitForExit();

                        ++_numIterationsDone;

                        // Check if the file was created successfully
                        if (
                            _ffmpegProcess.ExitCode != 0
                            || !RobustFile.Exists(outputPath)
                            || new FileInfo(outputPath).Length < 100
                        )
                        {
                            // Failure - Log and abort.
                            var mergeErrors = _errorData.ToString();
                            Logger.WriteError(new ApplicationException(mergeErrors));
                            Logger.WriteEvent(PrettyPrintProcessStartInfo(_ffmpegProcess));
                            Logger.WriteEvent("filter_complex_script contents: " + complexFilter); // Write the temp file contents before it gets disposed
                            Logger.WriteEvent($"FFMPEG exit code: {_ffmpegProcess.ExitCode}");

                            // If you get this error, please check the logs above! You'll find the FFMPEG command, exit code, and output in the logs.
                            progress.MessageWithoutLocalizing(
                                "Merging audio and video failed",
                                ProgressKind.Error
                            );
                            _recording = false;

                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static char[] shortNameChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        internal static string GetShortName(int index)
        {
            var result = "";
            var remainder = index;
            while (remainder >= shortNameChars.Length)
            {
                var next = remainder % shortNameChars.Length;
                result += shortNameChars[next];
                remainder = remainder / shortNameChars.Length;
            }

            result += shortNameChars[remainder];
            return result;
        }

        private void OutputProgressEstimate(
            IWebSocketProgress progress,
            DateTime startTime,
            int totalIterationsNeeded,
            TimeSpan totalDuration
        )
        {
            try
            {
                double currentIterationProgress = GetCurrentIterationProgress(
                    progress,
                    totalDuration
                );
                double iterationsSoFar = _numIterationsDone + currentIterationProgress;

                if (iterationsSoFar == 0)
                    return; // Nothing meaningful to output yet.

                progress.MessageWithoutLocalizing(
                    $"- Progress: {GetProgressMessage(iterationsSoFar, totalIterationsNeeded)}"
                );

                if (_numIterationsDone > _lastIterationPrinted)
                {
                    // Only calculate timeRemainingEstimates when a whole number iteration changes,
                    // because the fractional iteration progress produces highly inaccurate estimates
                    // (off by orders of magnitude, usually overestimating, producing very disheartening initial estimates for the user)
                    //
                    // The progress file seems to be updated pretty frequently by ffmpeg, but the estimates are still really inaccurate.
                    // Using the last modified time of the progress file (rather than DateTime.Now) also doesn't help (since the progress file is updated frequently)
                    // The bigger problem seems to be that working through the frames is extremely non-linear.
                    //    I often observe it'll slowly inch to halfway or to two-thirds of the frames,
                    //    then suddenly on the next iteration it's 99% or 100% of the way through the frames.
                    //    A linear extrapolation based on that produces awful estimates.
                    //
                    // Unfortunately, this now seems to underestimate the time required, but at least it's in a closer ballpark.
                    // Only cases that exceed the batch size see these estimates anyway.

                    var timeSoFar = DateTime.Now - startTime;

                    double timePerIterationSoFar = timeSoFar.TotalMilliseconds / iterationsSoFar;
                    var iterationsRemaining = totalIterationsNeeded - iterationsSoFar;
                    if (iterationsRemaining <= 0)
                        return;

                    var estimatedTimeRemaining = timePerIterationSoFar * iterationsRemaining;
                    //Debug.WriteLine($"totalIterations:{totalIterationsNeeded}, iterationsSoFar:{iterationsSoFar}, iterationsRemaining:{iterationsRemaining}");
                    //Debug.WriteLine($"timeSoFar:{timeSoFar}, timePerIterationSoFar:{timePerIterationSoFar}");
                    //Debug.WriteLine($"estimatedTimeRemaining:{estimatedTimeRemaining}");

                    progress.MessageWithoutLocalizing(
                        $"- {GetEstimateMessageFromMillis(estimatedTimeRemaining)}"
                    );
                }
                _lastIterationPrinted = _numIterationsDone;
            }
            catch (Exception e)
            {
                MiscUtils.SuppressUnusedExceptionVarWarning(e);
            }
        }

        /// <summary>
        /// Returns the progress of the current FFMPEG MergeAudioFiles() iteration, as a proportion between 0 to 1
        /// </summary>
        private double GetCurrentIterationProgress(
            IWebSocketProgress progress,
            TimeSpan totalDuration
        )
        {
            try
            {
                if (_ffmpegExited)
                    return 0;

                var progressFileContents = RobustIO.ReadAllTextFromFileWhichMightGetWrittenTo(
                    _ffmpegProgressFile.Path
                );

                if (string.IsNullOrWhiteSpace(progressFileContents))
                    return 0;

                // If progress=end, the process has finished and there is no point in estimating
                if (
                    GetLastOccurenceOfKeyValue(
                        progressFileContents,
                        "progress",
                        out string progressValue
                    )
                    && progressValue == "end"
                )
                    return 0; // We intentionally return 0 for currentIterationProgress and increment numIterationsDone instead.

                if (
                    !GetLastOccurenceOfKeyValue(
                        progressFileContents,
                        "frame",
                        out string framesSoFarStr
                    )
                )
                    return 0;

                var framesSoFar = int.Parse(framesSoFarStr);
                if (framesSoFar < 1)
                    return 0;

                var framesTotal = totalDuration.TotalSeconds * kFrameRate;

                double iterationProgress = Math.Min(framesSoFar / framesTotal, 1);
                //Debug.WriteLine($"{framesSoFar} / {framesTotal} = {iterationProgress}", ProgressKind.Progress);

                return iterationProgress;
            }
            catch (Exception e)
            {
                MiscUtils.SuppressUnusedExceptionVarWarning(e);
                return 0;
            }
        }

        private string GetProgressMessage(double iterationsSoFar, int totalIterationsNeeded)
        {
            var progressPercent = (int)Math.Round(iterationsSoFar / totalIterationsNeeded * 100);
            if (progressPercent == 0 && iterationsSoFar > 0)
            {
                return "<1%";
            }
            else if (progressPercent == 100 && iterationsSoFar < totalIterationsNeeded)
            {
                return ">99%";
            }
            else
            {
                return $"{progressPercent}%";
            }
        }

        private string GetEstimateMessageFromMillis(double millisRemaining)
        {
            var estimateStr =
                millisRemaining < 60000
                    ? LocalizationManager.GetString(
                        "PublishTab.RecordVideo.AboutOneMinute",
                        "less than one minute"
                    )
                    : string.Format(
                        LocalizationManager.GetString(
                            "PublishTab.RecordVideo.AboutNMinutes",
                            "about {0} minutes",
                            "{0} is a number of minutes"
                        ),
                        Math.Ceiling(millisRemaining / 60000)
                    );
            return string.Format(
                LocalizationManager.GetString(
                    "PublishTab.RecordVideo.EstimatedTimeRemaining",
                    "Estimated time remaining: {0}",
                    "{0} is text describing the amount of time such as 'less than one minute' or 'about 3 minutes'"
                ),
                estimateStr
            );
        }

        private bool GetLastOccurenceOfKeyValue(string content, string key, out string value)
        {
            value = null;

            var match = new Regex($"\\n{key}=(.*)\\n", RegexOptions.RightToLeft).Match(content);
            if (!match.Success)
                return false;

            value = match.Groups[1].Value;

            return !string.IsNullOrEmpty(value);
        }

#if __MonoCS__
        // Todo: Anything for Linux?
#else
        [FlagsAttribute]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
            // Legacy flag, should not be used.
            // ES_USER_PRESENT = 0x00000004
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
#endif

        void PreventSleep()
        {
#if __MonoCS__
            // Todo: Linux
#else
            // Reset system and display idle timers
            // It's probably possible to do something with ES_CONTINUOUS so we don't have to do this
            // repeatedly, but the documentation is less clear, and the danger of preventing sleep
            // permanently seems greater.
            SetThreadExecutionState(
                EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED
            );
#endif
        }

        private Timer _preventSleepTimer;

        private void RunFfmpeg(string args, string workingDirectory = null)
        {
            // This method attaches event handlers to the process and doesn't wait for the process
            // to finish, so it can't use the CommandLineRunner methods.
            var currentCulture = CultureInfo.CurrentCulture;
            var currentUICulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

                if (_errorData == null)
                    _errorData = new StringBuilder();
                if (_ffmpegPath == null)
                    _ffmpegPath = MiscUtils.FindFfmpegProgram();

                _ffmpegProcess = new Process
                {
                    StartInfo =
                    {
                        FileName = _ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false, // enables CreateNoWindow
                        CreateNoWindow = true, // don't need a DOS box
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                    }
                };
                if (workingDirectory != null)
                {
                    _ffmpegProcess.StartInfo.WorkingDirectory = workingDirectory;
                }
                _ffmpegExited = false;
                _ffmpegProcess.EnableRaisingEvents = true;
                _ffmpegProcess.Exited += (object sender, EventArgs e) =>
                {
                    _ffmpegExited = true;
                };
                _errorData.Clear(); // no longer need any errors from first ffmpeg run
                // Configure for async capture of stderror. See comment below.
                _ffmpegProcess.ErrorDataReceived += (o, receivedEventArgs) =>
                {
                    _errorData.AppendLine(receivedEventArgs.Data);
                };
                _ffmpegProcess.Start();
                // Nothing seems to come over the output stream, but it seems to be important to
                // have something running that will accept input on these streams, otherwise the 'q'
                // that we send on standard input is not received. A comment I saw elsewhere indicated
                // that a deadlock in ffmpeg might be involved.
                // We may not need this in the merge rn, since we're not using 'q' to force an early exit,
                // but it's harmless (and necessary if were using ErrorDataReceived as above)
                _ffmpegProcess.BeginOutputReadLine();
                _ffmpegProcess.BeginErrorReadLine();
                _preventSleepTimer = new Timer();
                _preventSleepTimer.Tick += (sender, eventArgs) => PreventSleep();
                // Every 20s should prevent even the most aggressive sleep; the lowest setting in my
                // control panel is 1 minute.
                _preventSleepTimer.Interval = 20000;
                _preventSleepTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.WriteError("Starting ffmpeg failed: command line was " + args, ex);
                throw;
            }
            finally
            {
                CultureInfo.CurrentCulture = currentCulture;
                CultureInfo.CurrentUICulture = currentUICulture;
            }
        }

        private string PrettyPrintProcessStartInfo(Process process) =>
            $"{process.StartInfo.WorkingDirectory}>{process.StartInfo.FileName} {process.StartInfo.Arguments}";

        public bool AnyVideoHasAudio(Book.Book book)
        {
            foreach (var videoPath in book.OurHtmlDom.GetAllVideoPaths())
            {
                if (VideoHasAudio(Path.Combine(book.FolderPath, videoPath)))
                    return true;
            }
            return false;
        }

        internal bool VideoHasAudio(string path)
        {
            try
            {
                // This is a kludge. With a single input and no output, Ffmpeg gives an error;
                // but it also gives its usual report of the streams in the input. If any of them
                // is identified as an audio stream we report a possible problem.
                var args = "-i \"" + path + "\"";
                RunFfmpeg(args);
                _ffmpegProcess.WaitForExit();
                var output = _errorData.ToString();
                return Regex.IsMatch(output, "Stream #.*Audio");
            }
            catch (Exception ex)
            {
                SentrySdk.CaptureException(ex);
                // Not obvious what to do here. Doesn't seem worth bothering the user if something
                // went wrong while we're trying to decide whether to warn them that audio in video
                // won't be captured. Maybe we should return true, ensuring that the warning shows?
                // But if one video has audio, probably others do, too, and it's not very serious
                // if we don't show the warning...it's mainly to prevent too many bug reports!
                return false;
            }
        }

        public bool GotFullRecording { get; private set; }

        protected override void OnClosed(EventArgs e)
        {
            // Careful here! We want to clean up in case the user manually closes the window,
            // which amounts to a cancel. But we also close it when the recording finishes
            // properly, BEFORE we do the next stage of processing the video; don't clean
            // up anything we will need for that. See code at start of StopRecordingAsync,
            // which sets things up so that Close will not mess things up.
            _saveReceived = false;
            _webSocketServer.SendString("recordVideo", "recording", "false");
            base.OnClosed(e);
            if (_recording && _ffmpegProcess != null)
            {
                _ffmpegProcess.StandardInput.WriteLine("q"); // stop it asap
            }

            _htmlFile?.Dispose();
            _htmlFile = null;
            if (_initialVideo != null)
            {
                _initialVideo.Dispose();
                // We haven't exactly finished processing it, but on this path,
                // the user canceled, and we want to allow clients to clean things up.
                FinishedProcessingRecording?.Invoke(this, new EventArgs());
            }

            _initialVideo = null;
            ClearPreventSleepTimer();
        }

        void ClearPreventSleepTimer()
        {
            if (_preventSleepTimer == null)
                return;
            _preventSleepTimer.Stop();
            _preventSleepTimer.Dispose();
            _preventSleepTimer = null;
        }

        // When the window is closed we will automatically be Disposed. But we might still be asked to
        // Save the final recording. So we can't get rid of that in Dispose. This will be called
        // when we're sure we need it no more.
        public void Cleanup()
        {
            ClearPreventSleepTimer();
            _htmlFile?.Dispose();
            _htmlFile = null;
            _finalVideo?.Dispose();
            _finalVideo = null;
            _initialVideo?.Dispose();
            _initialVideo = null;
            _capturedVideo?.Dispose();
            _capturedVideo = null;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
            Icon = Properties.Resources.BloomIcon;
        }

        public void PlayVideo()
        {
            if (!GotFullRecording)
                return;
            ProcessExtra.SafeStartInFront(_finalVideo.Path);
        }

        /// <summary>
        /// Gets the suggested file basename to use in the save file dialog
        /// If possible, this is based on the selected language and corresponding title of the book.
        /// </summary>
        /// <returns>The base name (the filename WITHOUT THE EXTENSION nor the path)</returns>
        public string GetSuggestedSaveFileNameBase(out string langTag)
        {
            // If _videoSettingsFromPreview has been set (e.g. on multilingual books),
            // check the video settings from the Bloom Player preview to see
            // if the user has selected a specific language. If so, use the title in that lang
            if (!String.IsNullOrEmpty(_videoSettingsFromPreview) && _book != null)
            {
                dynamic videoSettings;
                string langCode = null;
                try
                {
                    videoSettings = DynamicJson.Parse(_videoSettingsFromPreview);
                    langCode = videoSettings.lang;
                }
                catch (Exception ex)
                {
                    // If there's an error parsing the videoSettings, just report it if debug
                    // and fallback to the legacy behavior.
                    Debug.Fail(
                        $"Error while parsing _videoSettingsFromPreview JSON (\"{_videoSettingsFromPreview}\"): {ex}\n(Will be ignored in production)"
                    );
                }

                if (
                    !String.IsNullOrEmpty(langCode)
                    && !String.IsNullOrEmpty(_book.BookInfo.AllTitles)
                )
                {
                    // Try to find the title in that language
                    var allTitlesDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                        _book.BookInfo.AllTitles
                    );
                    if (allTitlesDict.TryGetValue(langCode, out string titleForRequestedLangCode))
                    {
                        langTag = langCode;
                        return titleForRequestedLangCode;
                    }
                }

                // In case of any unexpected errors above, just fallback to the legacy way which just uses the path to the book.
            }

            langTag = null;
            // Default method
            return Path.GetFileName(_pathToRealBook);
        }

        // Note: this method is normally called after the window is closed and therefore disposed.
        public void SaveVideo()
        {
            _saveReceived = true;
            if (!GotFullRecording)
                return; // nothing to save, this shouldn't have happened.
            var extension = _codec.ToExtension();
            var filename = GetSuggestedSaveFileNameBase(out string langTag);
            var initialPath = OutputFilenames.GetOutputFilePath(
                _book,
                extension,
                filename,
                langTag
            );
            var outputFileLabel = L10NSharp.LocalizationManager.GetString(
                @"PublishTab.RecordVideo.VideoFile",
                "Video File",
                @"displayed as file type for Save File dialog"
            );
            if (_codec == Codec.MP3)
            {
                outputFileLabel = L10NSharp.LocalizationManager.GetString(
                    @"PublishTab.RecordVideo.AudioFile",
                    "Audio File",
                    @"displayed as file type for Save File dialog"
                );
            }
            outputFileLabel = outputFileLabel.Replace("|", "");
            var filter = String.Format("{0}|*{1}", outputFileLabel, extension);

            var destFileName = MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                initialPath,
                filter
            );
            if (!String.IsNullOrEmpty(destFileName))
            {
                OutputFilenames.RememberOutputFilePath(_book, extension, destFileName, langTag);
                RobustFile.Copy(_finalVideo.Path, destFileName, true);
            }

            Analytics.Track(
                "Publish Audio/Video",
                new Dictionary<string, string>()
                {
                    { "format", _book.BookInfo.PublishSettings.AudioVideo.Format },
                    { "BookId", _book.ID },
                    { "Country", _book.CollectionSettings.Country },
                    { "Language", _book.BookData.Language1.Tag }
                }
            );
        }

        // Some logic I found...combined with increasing the MaximumSize of the window, it allows
        // the window to be substantially bigger than the screen (though probably only on Windows).
        // That looked promising for recording at a bigger-than-screen resolution. But it didn't work;
        // painting in the larger window is clipped to the screen (actually to a rectangle enclosing all
        // screens) so typically it results in big white bars in the recording.
        //[DllImport("user32.dll", EntryPoint = "MoveWindow")]
        //private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        //protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        //{
        //	base.SetBoundsCore(x, y, width, height, specified);
        //	MoveWindow(Handle, x, y, width, height, true);
        //}


        /// <summary>
        /// Given a particular video format that the user has selected, decide how big the
        /// content area for the recording window should be and what codec to use.
        /// </summary>
        /// <returns>A warning message, if we can't make the window big enough to record
        /// the optimum resolution for the format; otherwise, an empty string.</returns>
        public static string GetDataForFormat(
            string format,
            bool landscape,
            Layout pageLayout,
            out Resolution desiredResolution,
            out Resolution actualResolution,
            out Codec codec,
            out bool shouldRotateBook,
            out bool shouldUseOriginalPageSize,
            out bool useFullScreen
        )
        {
            shouldRotateBook = false;
            shouldUseOriginalPageSize = false;
            useFullScreen = false;

            int desiredWidth;
            int desiredHeight;
            switch (format)
            {
                default:
                case "facebook":
                    if (landscape)
                    {
                        // It handles 16:9 videos just fine, so just let it stay 16:9 since Bloom Player will show it as Device 16x9 Landscape layout
                        desiredHeight = 720;
                        desiredWidth = 1280;
                    }
                    else // Portrait
                    {
                        // Portrait videos are trickier.
                        // As of 2022, The FB mobile app doesn't display 9:16 (1:1.77) well, or anything > 2:3 (1:1.5).
                        // On the news feed or the video details page, it shows in up to a 2:3 area,
                        // so the 9:16 videos will be cut off. Page numbers won't show, xmatter at the bottom of the page, etc.
                        // (It shows up okay if the user clicks the Expand icon on the video details page, but that's 3 clicks away)
                        // (FYI, even if the video has accompanying text in the post, the video is still allotted up to the 2:3 area.)
                        //
                        // Note: this is for user-uploaded videos, not ads.
                        //    My guess is that our users will be primarily doing these as uploads, not as ad videos. But that's just a guess.
                        // For ad placements on the news feed, FB recommends 4:5 (1:1.25). If the ad video appears with a card underneath,
                        // this will leave room to fit the card text/link etc to 2:3 (1:1.5) total.
                        // If users want to target FB ads, there are a couple options:
                        //    * We could specifically add 4:5 page layout for them, and fix this if condition to handle layouts other than "A5 Portrait" properly
                        //    * We can add a "format" called "Facebook Ad" (in addition to the existing "Facebook")
                        //    * In the meantime, they can use 13cm Square. Alternatively, Letter (Portrait) is very close to 4:5, just a tiny bit bigger (1.29 instead of 1.25).


                        //////////////
                        // ENHANCE: //
                        //////////////
                        // Actually what this if condition should entail is "any book layout between square (1:1)  and 4:5 (1:1.25) aspect ratio,"
                        // but right now we only support square ones. ("13cm Square"  is the only layout that qualifies currently anyway).
                        if (pageLayout != null && pageLayout.SizeAndOrientation.IsSquare)
                        {
                            // Books with aspect ratio < 4:5 can just use their original page size as is.
                            shouldUseOriginalPageSize = true;
                            desiredHeight = 720; // Enhance: Determine {desiredHeight} programatically for non-square layouts
                            desiredWidth = 720;
                        }
                        else
                        {
                            // 4:5 aspect ratio.
                            // ENHANCE: The video will be this size, but Bloom Player will be 9:16 still, so you will get black bars, which is not ideal.
                            // It would be better if Bloom Player used a page layout with the proper size (we'd need to make a new Device4x5 page layout)
                            desiredHeight = 900;
                            desiredWidth = 720;
                        }
                    }
                    codec = Codec.H264;
                    break;
                case "feature":
                    // Targeting Common Intermediate Format (CIF), one of the resolutions allowed by the original H.263 standard.
                    // Note: I think StoryProducer app makes 176x144 (QCIF), another supported resolution but one step down in quality from CIF.
                    desiredHeight = landscape || !_rotatePortraitH263Videos ? 288 : 352;
                    desiredWidth = landscape || !_rotatePortraitH263Videos ? 352 : 288;
                    codec = Codec.H263;
                    break;
                // more options here? YouTube videos don't have to be HD.
                case "youtube":
                    desiredHeight = landscape ? 1080 : 1920;
                    desiredWidth = landscape ? 1920 : 1080;
                    codec = Codec.H264;
                    break;
                case "mp3":
                    // review: what size video do we want to play? Won't actually be used.
                    desiredHeight = landscape ? 720 : 1280;
                    desiredWidth = landscape ? 1280 : 720;
                    codec = Codec.MP3;
                    break;
            }

            desiredResolution = new Resolution(desiredWidth, desiredHeight);
            actualResolution = desiredResolution;

            var mainWindow = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
            if (mainWindow != null)
            {
                if (s_maxResolution.Equals(default(Resolution)))
                {
                    // Couldn't get this to work. See comment in constructor.
                    //var originalAwareness = SetThreadDpiAwarenessContext(ThreadDpiAwareContext.PerMonitorAwareV2);
                    var bounds = Screen.FromControl(mainWindow).Bounds;
                    var proto = RecordVideoWindow.Create(null);
                    // Enhance: can we improve on this? We seem to be getting numbers just a bit bigger than
                    // we need, so the output doesn't quite fill the screen.
                    var deltaV = proto.Height - proto._content.Height;
                    var deltaH = proto.Width - proto._content.Width;

                    s_maxResolution = new Resolution(bounds.Width - deltaH, bounds.Height - deltaV);
                    s_screenResolution = new Resolution(bounds.Width, bounds.Height);
                }

                actualResolution = GetBestResolutionForFormat(
                    format,
                    desiredResolution,
                    s_maxResolution,
                    landscape
                );
                if (
                    ShouldRotateBookForRecording(
                        format,
                        landscape,
                        actualResolution,
                        desiredResolution,
                        s_maxResolution
                    )
                )
                {
                    shouldRotateBook = true;
                    actualResolution = GetBestResolutionForFormat(
                        format,
                        desiredResolution,
                        s_maxResolution.GetInverse(),
                        landscape
                    );
                }

                // Couldn't get this to work. See comment in constructor.
                //SetThreadDpiAwarenessContext(originalAwareness);
            }

            if (format != "mp3" && IsVideoTooSmall(actualResolution, desiredResolution))
            {
                // Check for either landscape or portait orientation.
                if (
                    (
                        s_screenResolution.Width >= desiredResolution.Width
                        && s_screenResolution.Height >= desiredResolution.Height
                    )
                    || (
                        s_screenResolution.Width >= desiredResolution.Height
                        && s_screenResolution.Height >= desiredResolution.Width
                    )
                )
                {
                    // If the screen actually allows the desired resolution, we can make the window full screen.
                    // (Full screen means borderless window that covers the whole screen in at least one dimension.)
                    useFullScreen = true;
                    actualResolution = desiredResolution;
                    return "";
                }
                // If the screen is too small, we'll have to make the window smaller.
                // This will result in a video that is smaller than the desired resolution.
                // We'll warn the user about this.
                var frame = LocalizationManager.GetString(
                    "PublishTab.RecordVideo.ScreenTooSmall",
                    "Ideally, this video target should be {0}. However that is larger than your screen, so Bloom will produce a video that is {1}."
                );
                return string.Format(
                    frame,
                    $"{desiredResolution.Width} x {desiredResolution.Height}",
                    $"{actualResolution.Width} x {actualResolution.Height}"
                );
            }

            return "";
        }

        /// <summary>
        /// Returns true if the actual resolution of a video is smaller than the desired resolution of the video
        /// </summary>
        private static bool IsVideoTooSmall(
            Resolution actualResolution,
            Resolution desiredResolution
        )
        {
            return actualResolution.Width < desiredResolution.Width
                || actualResolution.Height < desiredResolution.Height;
        }

        /// <summary>
        /// Given a video's resolution, returns true if rotating the video would be beneficial (in the sense that it would reach the desired resolution)
        /// </summary>
        private static bool ShouldRotateBookForRecording(
            string format,
            bool isBookLandscape,
            Resolution actualResolution,
            Resolution desiredResolution,
            Resolution maxResolution
        )
        {
            // Feature phones (which uses H.263 r1) is locked to landscape, so don't rotate.
            // mp3: The final result is audio only, no need to bother rotating the video
            if (format == "feature" || format == "mp3")
            {
                return false;
            }

            bool isScreenLandscape = maxResolution.Width > maxResolution.Height;
            return (
                    !isBookLandscape
                    && isScreenLandscape
                    && IsVideoTooSmall(actualResolution, desiredResolution)
                ) // Portrait books on landscape screen
                || (
                    isBookLandscape
                    && !isScreenLandscape
                    && IsVideoTooSmall(actualResolution, desiredResolution)
                ); // Landscape books on portrait screen
        }

        /// <summary>
        /// Returns the appropriate calculation for best resolution depending on the format selected
        /// </summary>
        private static Resolution GetBestResolutionForFormat(
            string format,
            Resolution desiredResolution,
            Resolution maxResolution,
            bool isBookLandscape
        )
        {
            if (format == "youtube")
            {
                return GetBestYouTubeResolution(maxResolution, isBookLandscape);
            }
            else
            {
                return GetBestArbitraryResolution(desiredResolution, maxResolution);
            }
        }

        // Derived from a combination of https://support.google.com/youtube/answer/6375112?hl=en&co=GENIE.Platform%3DDesktop&oco=1
        // and by uploading high-res videos and checking Stats for Nerds -> Optimal Resolution at each playback quality
        private static readonly Resolution[] youtubeLandscapeResolutionsHighToLow = new Resolution[]
        {
            // 3840 x 2160 (2160p) and 2560x1440 (1440p) are also supported by YouTube, but we decided not to include it.
            // 1440p and 2160p would obviously increases the video size a lot, for a scenario we don't think is a likely need
            new Resolution(1920, 1080), // 1080p HD
            new Resolution(1280, 720), // 720p
            new Resolution(854, 480), // 480p
            new Resolution(640, 360), // 360p
            new Resolution(426, 240), // 240p
            new Resolution(256, 144) // 144p
        };

        private static readonly Resolution[] youtubePortraitResolutionsHighToLow =
            youtubeLandscapeResolutionsHighToLow.Select(r => r.GetInverse()).ToArray();

        /// <summary>
        ///  Gets the largest of YouTube's standard resolutions that will fit on the screen.
        /// </summary>
        /// <param name="maxWidth">The maximum width we can display a window (roughly the screen width)</param>
        /// <param name="maxHeight">The maximum height we can display a window (roughly screen height)</param>
        /// <param name="isBookLandscape">true if the book is landscape, false if portrait</param>
        internal static Resolution GetBestYouTubeResolution(
            Resolution maxResolution,
            bool isBookLandscape
        )
        {
            var youtubeResolutionsHighToLow = isBookLandscape
                ? youtubeLandscapeResolutionsHighToLow
                : youtubePortraitResolutionsHighToLow;

            // Iterate over Youtube's recommended resolutions,
            // from highest resolution to lowest resolution,
            // and find the highest one that fits on the screen.
            foreach (var resolution in youtubeResolutionsHighToLow)
            {
                if (
                    resolution.Width <= maxResolution.Width
                    && resolution.Height <= maxResolution.Height
                )
                {
                    return resolution;
                }
            }

            // Made it through the loop without finding any matches :(
            // Just fallback to the screen size
            return maxResolution;
        }

        /// <summary>
        /// Gets the largest resolution, up to the {desired} resolution and in accordance with the {desired} aspect ratio, that will fit on the screen
        /// </summary>
        /// <param name="desired">The largest desired resolution</param>
        /// <param name="max">The max resolution that can fit on the screen</param>
        private static Resolution GetBestArbitraryResolution(Resolution desired, Resolution max)
        {
            Resolution actual = desired;
            if (actual.Height > max.Height)
            {
                actual.Height = (max.Height / 2) * 2; // round down to even, ffmpeg dies on odd sizes
                actual.Width = (actual.Height * desired.Width / desired.Height / 2) * 2;
            }

            if (actual.Width > max.Width)
            {
                actual.Width = (max.Width / 2) * 2;
                actual.Height = (actual.Width * desired.Height / desired.Width / 2) * 2;
            }

            return actual;
        }

        public void SetFormat(string format, bool landscape, Layout pageLayout)
        {
            GetDataForFormat(
                format,
                landscape,
                pageLayout,
                out _,
                out Resolution videoResolution,
                out _codec,
                out _shouldRotateBook,
                out _shouldUseOriginalPageSize,
                out _showFullScreen
            );
            _videoWidth = videoResolution.Width;
            _videoHeight = videoResolution.Height;
        }

        public void SetPageReadTime(string pageReadTime)
        {
            _pageReadTime = pageReadTime;
        }

        public void SetVideoSettingsFromPreview(string videoSettings)
        {
            _videoSettingsFromPreview = videoSettings;
        }

        public void SetBook(Book.Book book)
        {
            _book = book;
        }
    }

    class SoundLogItem
    {
        public string src;
        public string shortName;
        public double volume;
        public DateTime startTime;
        public TimeSpan startOffset;
        public DateTime endTime; // if not set, play the whole sound
    }
}
