using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Utils;
using L10NSharp;
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
		private Browser _content;
		private Process _ffmpegProcess;
		private StringBuilder _errorData;
		private DateTime _startTime;
		private string _videoOnlyPath;
		private string _ffmpegPath;
		private TempFile _initialVideo;
		private TempFile _finalVideo;
		private bool _recording = true;
		private bool _saveReceived;
		private string _pathToRealBook;
		private BloomWebSocketServer _webSocketServer;
		private int _videoHeight = 720; // default, facebook
		private int _videoWidth = 1280;
		private Codec _codec = Codec.H264;
		private string _pageReadTime = "3.0"; // default for pages without narration
		private string _videoSettingsFromPreview;

		// H.263, at least in its original revision, only supports certain specific resolutions, e.g. CIF = 352x288
		// Notably, it is necessary for it to be 352x288, not the inverse 288x352. (Revision 2 supposedly allows flexible resolutions)
		// If false, then we just do the simple thing: make the window 352x288, put a portrait book in the middle (with big blank sidebars on each side), and call it a day
		// If true, this can make the book appear bigger by making the window portrait-sized 288x352, then recording a rotated video.
		//   The pro is that the book is bigger. The con is that any video playing software will play it sideways. But if you just turn your device sideways, then you're golden.
		// I tried having portrait books target H.263+ (-vcodec h263p), but it says its invalid for the container. I tried all the other variations I could think to try;
		// this approach (targeting a newer H263) seems like a dead end.
		private const bool _rotatePortraitH263Videos = false;

		public RecordVideoWindow(BloomWebSocketServer webSocketServer)
		{
			InitializeComponent();
			_webSocketServer = webSocketServer;
			_content = new Browser();
			_content.Dock = DockStyle.Fill;
			Controls.Add(_content);
		}

		/// <summary>
		/// As we show the window, we immediately load Bloom player into the browser and start
		/// navigating to it.
		/// </summary>
		public void Show(string bookUrl, string pathToRealBook)
		{
			_pathToRealBook = pathToRealBook;
			// We don't need to spend any time on pages that have no narration if we're not
			// recording video. (Review: I suppose its technically possible that something
			// really important is happening in the 'background music' stream. If that becomes
			// a problem, we may have to somehow encourage, but not enforce, zero pageReadTime
			// for audio-only output.
			var pageReadTime = (_codec == Codec.MP3 ? "0" : _pageReadTime);
			var url = BloomServer.ServerUrlWithBloomPrefixEndingInSlash
			          + "bloom-player/dist/bloomplayer.htm?centerVertically=true&reportSoundLog=true&initiallyShowAppBar=false&autoplay=yes&hideNavButtons=true&url="
			          + bookUrl
			          + $"&independent=false&host=bloomdesktop&defaultDuration={pageReadTime}&skipActivities=true";
			// The user can make choices in the preview instance of BloomPlayer...currently language and
			// whether to play image descriptions...that need to be communicated to the recording window.
			// If we received any, pass them on.
			if (_videoSettingsFromPreview != null)
			{
				url += $"&videoSettings={_videoSettingsFromPreview}";
			}

			_content.Navigate(url, false);
			// Extra space we need around the recordable content for title bars etc.
			var deltaV = this.Height - _content.Height;
			var deltaH = this.Width - _content.Width;
			// Make the window an appropriate size so the content area gives the resolution we want.
			// (We've already made an adjustment if the screen isn't that big.)
			Height = _videoHeight + deltaV;
			Width = _videoWidth + deltaH;
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

		// As window is loaded, we let Bloom Player know we are ready to start recording.
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			_webSocketServer.SendString("recordVideo", "ready", "false");
			_initialVideo = TempFile.WithExtension(_codec.ToExtension());
			_videoOnlyPath = _initialVideo.Path;
			RobustFile.Delete(_videoOnlyPath);
		}

		/// <summary>
		/// We get a notification through the API when bloom player has loaded the first page content
		/// and is in a good state for us to start recording video from the window content.
		/// </summary>
		public void StartFfmpeg()
		{
			// We do these steps unconditionally because they are used later when we run
			// ffmpeg (for the second time, if recording video).
			_errorData = new StringBuilder();
			_ffmpegPath = MiscUtils.FindFfmpegProgram();
			// Enhance: what on earth should we do if it's not found??

			// If we're doing audio there's no more to do just now; we will play the book
			// visually, but we don't need to record what happens.
			if (_codec == Codec.MP3)
			{
				_startTime = DateTime.Now;
				return;
			}

			string videoArgs;
			if (_codec == Codec.H263)
			{
				bool landscape = _videoWidth >= _videoHeight;
				videoArgs = $"-vcodec h263 -vf \"{(landscape || !_rotatePortraitH263Videos ? "" : "transpose=1,")} scale=352:288\" ";
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
			}

			// Configure ffmpeg to record the video.
			// Todo Linux (BL-11011): gdigrab is Windows-only, we'll need to find something else.
			// I believe ffmpeg has an option to capture the content of an XWindow.
			var args =
				"-f gdigrab " // basic command for using a window (in a Windows OS) as a video input stream
				+ "-framerate 30 " // frames per second to capture (30fps is standard for SD video)
				+ "-draw_mouse 0 " // don't capture any mouse movement over the window
				+ $"-i title=\"{Text}\" " // identifies the window for gdigrab
				
				+ videoArgs
				+ _videoOnlyPath; // the intermediate output file for the recording.
			//Debug.WriteLine("ffmpeg capture args: " + args);
			RunFfmpeg(args);
			_startTime = DateTime.Now;
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
		public void StopRecording(string soundLogJson)
		{
			StopRecordingInternal(soundLogJson);
			// Many early returns in this method, but we always want to close this.
			Close();
		}

		private void StopRecordingInternal(string soundLogJson)
		{
			var haveVideo = _codec != Codec.MP3;
			if (haveVideo)
			{
				// Leaving this in temporarily since there have been reports that the window sometimes doesn't close.
				Debug.WriteLine("Telling ffmpeg to quit");
				_ffmpegProcess.StandardInput.WriteLine("q");
				_ffmpegProcess.WaitForExit();
				// Enhance: if something goes wrong, it may be useful to capture this somehow.
				//Debug.WriteLine("full ffmpeg error log: " + _errorData.ToString());
				var errors = _errorData.ToString();
				if (!File.Exists(_videoOnlyPath) || new FileInfo(_videoOnlyPath).Length < 100)
				{
					ErrorReport.NotifyUserOfProblem(new ApplicationException(errors), "Video capture failed");
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

				sound.startOffset = sound.startTime - _startTime;

				soundLog[i] = sound;
			}

			_finalVideo = TempFile.WithExtension(_codec.ToExtension());
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
				return;
			}

			// configure ffmpeg to merge everything.
			// each sound file becomes an input by prefixing the path with -i.
			var inputs = string.Join(" ", soundLog.Select(item => $"-i \"{item.src}\" "));

			// arguments to configure 'filters' ahead of audio mixer which will combine the sounds into a single stream.
			var audioFilters = string.Join(" ", soundLog.Select((item, index) =>
			{
				// for each input,...
				var result = $"[{index}:a]"; // start with a label which refers to the relevant input stream
				// if we stopped playback early, we need an argument that will truncate it in the mixer.
				if (item.endTime != default(DateTime))
				{
					var duration = (item.endTime - item.startTime);


					result += $"atrim=end={duration.TotalSeconds},";
				}

				// Add instructions to delay it by the right amount
				var delay = item.startTime - _startTime;
				// all=1: in case the input is stereo, all channels of it will be delayed.
				// We shouldn't get negative delays, since startTime
				// is recorded during a method that completes before we return
				Debug.Assert(delay.TotalMilliseconds >= 0);
				result += $"adelay={Math.Max(delay.TotalMilliseconds, 0)}:all=1";

				// possibly reduce its volume.
				if (item.volume != 1.0)
				{
					result += $",volume={item.volume}";
				}

				// add another label by which the mixer will refer to this stream
				result += $"[a{index}]; ";
				return result;
			}));

			// labels of all the audio filter outputs provide the input to the mixer
			// (We're only really doing mixing if there's background music; the other audio
			// streams never overlap. There may be a possible enhancement that would improve
			// quality somewhat if we could avoid decoding and re-encoding when there is no
			// overlap.)
			var mixInputs = string.Join("", soundLog.Select((item, index) => $"[a{index}]"));
			// the video, if any, will be one more input after the audio ones and will be at this index
			// in the inputs.
			var videoIndex = soundLog.Length;

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
					audioArgs = "-acodec libmp3lame ";
					break;
				default:
					throw new NotImplementedException();
			}

			var args = ""
			           + inputs // the audio files are inputs, which may be referred to as [1:a], [2:a], etc.
			           + (haveVideo ? $"-i \"{_videoOnlyPath}\" " : "") // last input (videoIndex) is the original video (if any)
			           + "-filter_complex \""// the next bit specifies a filter with multiple inputs
			           + audioFilters // specifies the inputs to the mixer
			           // mix those inputs to a single stream called out. Note that, because most of our audio
					   // streams don't overlap, and the background music volume is presumed to have already
					   // been suitably adjusted, we do NOT want the default behavior of 'normalizing' volume
					   // by reducing it to 1/n where n is the total number of input streams.
			           + mixInputs + $"amix=inputs={soundLog.Length}:normalize=0[out]\" "
			           // copy the video channel (of input videoIndex) unchanged (if we have video).
					   // (here 'copy' is a pseudo codec...instead of encoding it in some particular way,
					   // we just copy the original.
					   + (haveVideo ? $"-map {videoIndex}:v -vcodec copy " : "")
					   
			           + audioArgs
			           + "-map [out] " // send the output of the audio mix to the output
			           + finalOutputPath; //and this is where we send it (until the user saves it elsewhere).
			// Debug.WriteLine("ffmpeg merge args: " + args);
			RunFfmpeg(args);
			_ffmpegProcess.WaitForExit();
			var mergeErrors = _errorData.ToString();
			if (!File.Exists(_finalVideo.Path) || new FileInfo(_finalVideo.Path).Length < 100)
			{
				ErrorReport.NotifyUserOfProblem(new ApplicationException(mergeErrors), "Merging audio and video failed");
				_recording = false;
				return;
			}
			//Debug.WriteLine("ffmpeg merge errors:");
			//Debug.WriteLine( _errorData.ToString());
			_recording = false;
			GotFullRecording = true;
			// Allows the Check and Save buttons to be enabled, now we have something we can play or save.
			_webSocketServer.SendString("recordVideo","ready", "true");
			// Don't think this ever happens now...if we allowed the user to click Save before the recording
			// was complete, this would be the time to proceed with saving.
			if (_saveReceived)
				SaveVideo(); // now we really can.
		}

		private void RunFfmpeg(string args)
		{
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
			_errorData.Clear(); // no longer need any errors from first ffmpeg run
			// Configure for async capture of stderror. See comment below.
			_ffmpegProcess.ErrorDataReceived += (o, receivedEventArgs) => { _errorData.AppendLine(receivedEventArgs.Data); };
			_ffmpegProcess.Start();
			// Nothing seems to come over the output stream, but it seems to be important to
			// have something running that will accept input on these streams, otherwise the 'q'
			// that we send on standard input is not received. A comment I saw elsewhere indicated
			// that a deadlock in ffmpeg might be involved.
			// We may not need this in the merge rn, since we're not using 'q' to force an early exit,
			// but it's harmless (and necessary if were using ErrorDataReceived as above)
			_ffmpegProcess.BeginOutputReadLine();
			_ffmpegProcess.BeginErrorReadLine();
		}

		public bool GotFullRecording { get; private set; }

		protected override void OnClosed(EventArgs e)
		{
			_saveReceived = false;
			base.OnClosed(e);
			if (_recording && _ffmpegProcess != null)
			{
				_ffmpegProcess.StandardInput.WriteLine("q"); // stop it asap
			}

			_initialVideo?.Dispose();
			_initialVideo = null;
		}

		// When the window is closed we will automatically be Disposed. But we might still be asked to
		// Save the final recording. So we can't get rid of that in Dispose. This will be called
		// when we're sure we need it no more.
		public void Cleanup()
		{
			_finalVideo?.Dispose();
			_finalVideo = null;
			_initialVideo?.Dispose();
			_initialVideo = null;
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
			Process.Start(_finalVideo.Path);
		}

		// Note: this method is normally called after the window is closed and therefore disposed.
		public void SaveVideo()
		{
			_saveReceived = true;
			if (!GotFullRecording)
				return; // nothing to save, this shouldn't have happened.
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				
				var extension = _codec.ToExtension();
				string suggestedName = string.Format($"{Path.GetFileName(_pathToRealBook)}{extension}");
				dlg.FileName = suggestedName;
				var outputFileLabel = L10NSharp.LocalizationManager.GetString(@"PublishTab.VideoFile",
					"Video File",
					@"displayed as file type for Save File dialog.");
				if (_codec == Codec.MP3)
				{
					outputFileLabel = L10NSharp.LocalizationManager.GetString(@"PublishTab.AudioFile",
						"Audio File",
						@"displayed as file type for Save File dialog.");
				}

				outputFileLabel = outputFileLabel.Replace("|", "");
				dlg.Filter = String.Format("{0}|*{1}", outputFileLabel, extension);
				dlg.OverwritePrompt = true;
				if (DialogResult.OK == dlg.ShowDialog())
				{
					RobustFile.Copy(_finalVideo.Path, dlg.FileName, true);
				}
			}
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
		public static string GetDataForFormat(string format, bool landscape, 
			out int actualWidth, out int actualHeight, out Codec codec)
		{
			bool tooBigForScreen = false;
			int desiredWidth;
			int desiredHeight;
			switch (format)
			{
				default:
				case "facebook":
					desiredHeight = landscape ? 720 : 1280;
					desiredWidth = landscape ? 1280 : 720;
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

			actualWidth = desiredWidth;
			actualHeight = desiredHeight;

			var mainWindow = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			if (mainWindow != null)
			{
				var bounds = Screen.FromControl(mainWindow).Bounds;
				var proto = new RecordVideoWindow(null);
				// Enhance: can we improve on this? We seem to be getting numbers just a bit bigger than
				// we need, so the output doesn't quite fill the screen.
				var deltaV = proto.Height - proto._content.Height;
				var deltaH = proto.Width - proto._content.Width;
				var maxHeight = bounds.Height - deltaV;
				var maxWidth = bounds.Width - deltaH;
				if (actualHeight > maxHeight)
				{
					tooBigForScreen = true;
					actualHeight = (maxHeight / 2) * 2; // round down to even, ffmpeg dies on odd sizes
					actualWidth = (actualHeight * desiredWidth / desiredHeight / 2) * 2;
				}

				if (actualWidth > maxWidth)
				{
					tooBigForScreen = true;
					actualWidth = (maxWidth / 2) * 2;
					actualHeight = (actualWidth * desiredHeight / desiredWidth / 2) * 2;
				}
				// Review/enhance: can we really just use any size we want like this, if the ideal won't fit?
				// Or should we step down to the largest available of some standard set?
			}

			if (tooBigForScreen)
			{
				var frame = LocalizationManager.GetString("Publish.RecordVideo.ScreenTooBig",
					"Ideally, this video target should be {0}. However that is larger than your screen, so Bloom will produce a video that is {1}.");
				return string.Format(frame, $"{desiredWidth} x {desiredHeight}", $"{actualWidth} x {actualHeight}");
			}

			return "";
		}

		public void SetFormat(string format, bool landscape)
		{
			GetDataForFormat(format, landscape, out _videoWidth, out _videoHeight, out _codec);
		}

		public void SetPageReadTime(string pageReadTime)
		{
			_pageReadTime = pageReadTime;
		}

		public void SetVideoSettingsFromPreview(string videoSettings)
		{
			_videoSettingsFromPreview = videoSettings;
		}
	}

	class SoundLogItem
	{
		public string src;
		public double volume;
		public DateTime startTime;
		public TimeSpan startOffset;
		public DateTime endTime; // if not set, play the whole sound
	}
}
