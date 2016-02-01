using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using L10NSharp;
#if __MonoCS__
#else
using SIL.Media.Naudio;
#endif
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.Widgets;
// Note: it is for the benefit of this component that Bloom references NAudio. We don't use it directly,
// but Palaso.Media does, and we need to make sure it gets copied to our output.

namespace Bloom.Edit
{
	/// <summary>
	/// Manages the process of making an audio recording.
	/// Adapted from HearThis class AudioButtonsControl; however, in Bloom the UI is in HTML
	/// (and so is the whole Playback logic).
	/// Interacts with the logic (and currently embedded HTML) in audioRecording.ts.
	/// See the comment at the start of that file for various needed enhancements.
	/// </summary>
	class AudioRecording
	{
		private AudioRecorder _recorder;
		/// <summary>
		/// The file we want to record to
		/// </summary>
		public string Path { get; set; }
#if __MonoCS__
#else
		// Palaso component to do the actual recording.
		private AudioRecorder Recorder
		{
			get
			{
				// We postpone actually creating a recorder until something uses audio.
				// Typically it is created when the talking book tool requests AudioDevicesJson
				// to update the icon. At that point we start really sending volume requests.
				if (_recorder == null)
				{
					Form.ActiveForm.Invoke((Action) (() =>
					{
						_recorder = new AudioRecorder(1);
						_recorder.PeakLevelChanged += ((s, e) => SetPeakLevel(e));
						BeginMonitoring(); // will call this recursively; make sure _recorder has been set by now!
					}));
				}
				return _recorder;
			}
		}
#endif
		private readonly string _backupPath; // If we are about to replace a recording, save the old one here; a temp file.
		private DateTime _startRecording; // For tracking recording length.
#if __MonoCS__
#else
		public event EventHandler<PeakLevelEventArgs> PeakLevelChanged;
#endif
		LameEncoder _mp3Encoder = new LameEncoder();
		/// <summary>
		/// This timer introduces a brief delay from the mouse click to actually starting to record.
		/// Based on HearThis behavior, I think the purpose is to avoid recording the click,
		/// and perhaps also experience indicates the user typically pauses slightly between clicking and actually talking.
		/// HearThis uses a system timer rather than this normal form timer because with the latter, when the button "captured" the mouse, the timer refused to fire.
		/// I don't think we can capture the mouse (at least not attempting it yet) so Bloom does not have this problem  and uses a regular Windows.Forms timer.
		/// </summary>
		private readonly Timer _startRecordingTimer;

		// This is a bit of a kludge. The server needs to be able to retrieve the data from AudioDevicesJson.
		// It would be quite messy to give the image server access to the EditingModel which owns the instance of AudioRecording.
		// However in practice (and very likely we would preserve this even if we had more than one book open at a time)
		// there is only one current AudioRecording object supporting the one EditingModel. This variable keeps track
		// of the one most recently created and uses it in the AudioDevicesJson method, which the server can therefore
		// call directly since it is static.
		private static AudioRecording CurrentRecording { get; set; }

		public AudioRecording()
		{
			_startRecordingTimer = new Timer();
			_startRecordingTimer.Interval = 300; //  ms from click to actual recording
			_startRecordingTimer.Tick += OnStartRecordingTimer_Elapsed;
			_backupPath = System.IO.Path.GetTempFileName();
			CurrentRecording = this;
		}

		/// <summary>
		/// Returns a json string like {"devices":["microphone", "Logitech Headset"], "productName":"Logitech Headset", "genericName":"Headset"},
		/// except that in practice currrently the generic and product names are the same and not as helpful as the above.
		/// Devices is a list of product names (of available recording devices), the productName and genericName refer to the
		/// current selection (or will be null, if no current device).
		/// </summary>
		public static string AudioDevicesJson
		{
			get
			{
#if __MonoCS__
				return String.Empty;
#else
				var sb = new StringBuilder("{\"devices\":[");
				bool first = true;
				foreach (var device in RecordingDevice.Devices)
				{
					if (first)
					{
						first = false;
					}
					else
					{
						sb.Append(",");
					}
					sb.Append("\"" + device.ProductName + "\"");
				}
				sb.Append("],\"productName\":");
				if (CurrentRecording.RecordingDevice != null)
					sb.Append("\"" + CurrentRecording.RecordingDevice.ProductName + "\"");
				else
					sb.Append("null");

				sb.Append(",\"genericName\":");
				if (CurrentRecording.RecordingDevice != null)
					sb.Append("\"" + CurrentRecording.RecordingDevice.GenericName + "\"");
				else
					sb.Append("null");

				sb.Append("}");
				return sb.ToString();
#endif
			}
		}

		/// <summary>
		/// Used to initiate sending the PeakLevelChanged notifications.
		/// Currently this typically happens when the Recorder instance is created,
		/// which is usually when the talking book tool asks for the AudioDevicesJson.
		/// This is not very intuitive, but it's the most easily detectable event
		/// that indicates that the talking book tool is actually active.
		/// </summary>
		public void BeginMonitoring()
		{
#if __MonoCS__
#else
			if (!RecordingDevice.Devices.Contains(RecordingDevice))
			{
				RecordingDevice = RecordingDevice.Devices.FirstOrDefault();
			}
			if (RecordingDevice != null)
			{
				Recorder.BeginMonitoring();
			}
#endif
		}

#if __MonoCS__
#else
		private void SetPeakLevel(PeakLevelEventArgs args)
		{
			if (PeakLevelChanged != null)
				PeakLevelChanged(this, args);
		}
#endif

		public void StopRecording()
		{
#if __MonoCS__
#else
			if (Recorder.RecordingState != RecordingState.Recording)
			{
				WarnPressTooShort();
				UpdateDisplay();
				return;
			}
			try
			{
				Debug.WriteLine("Stop recording");
				Recorder.Stop(); //.StopRecordingAndSaveAsWav();
				//ReportSuccessfulRecordingAnalytics();
			}
			catch (Exception)
			{
				//swallow it. One reason (based on HearThis comment) is that they didn't hold it down long enough, we detect this below.
			}
			if (DateTime.Now - _startRecording < TimeSpan.FromSeconds(0.5))
				WarnPressTooShort();
			else
			{
				_mp3Encoder.Encode(Path, Path.Substring(0, Path.Length - 4), new NullProgress());
				// Note: we need to keep the .wav file as well as the mp3 one. The mp3 format (or alternative mp4)
				// is required for epub. The wav file is a better permanent record of the recording; also,
				// it is used for playback.
			}
#endif
		}

		public void StartRecording()
		{
			TryStartRecord();
		}

		/// <summary>
		/// Start the recording
		/// </summary>
		/// <returns>true if the recording started successfully</returns>
		private bool TryStartRecord()
		{
#if __MonoCS__
			MessageBox.Show("Recording does not yet work on Linux", "Cannot record");
			return false;
#else
			if (Recorder.RecordingState == RecordingState.RequestedStop)
			{
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.AudioControl.BadState",
						"Bloom recording is in an unusual state, possibly caused by unplugging a microphone. You will need to restart."),
					LocalizationManager.GetString("EditTab.AudioControl.BadStateCaption", "Cannot record"));
			}
			//if (!_recordButton.Enabled)
			//	return false; //could be fired by keyboard

			// If someone unplugged the microphone we were planning to use switch to another.
			// This also triggers selecting the first one initially.
			if (!RecordingDevice.Devices.Contains(RecordingDevice))
			{
				RecordingDevice = RecordingDevice.Devices.FirstOrDefault();
			}
			if (RecordingDevice == null)
			{
				ReportNoMicrophone();
				return false;
			}

			if (Recording)
				return false;

			if (File.Exists(Path))
			{
				try
				{
					File.Copy(Path, _backupPath, true);
					File.Delete(Path);
					//DesktopAnalytics.Analytics.Track("Re-recorded a clip", ContextForAnalytics);
				}
				catch (Exception err)
				{
					ErrorReport.NotifyUserOfProblem(err,
						"Sigh. The old copy of that file is locked up, so we can't record over it at the moment. Yes, this problem will need to be fixed.");
					return false;
				}
			}
			else
			{
				File.Delete(_backupPath);
				//DesktopAnalytics.Analytics.Track("Recording clip", ContextForAnalytics);
			}
			_startRecording = DateTime.Now;
			_startRecordingTimer.Start();
			UpdateDisplay();
			return true;
#endif
		}

		public bool Recording
		{
			get
			{
#if __MonoCS__
				return false;
#else
				return Recorder.RecordingState == RecordingState.Recording ||
					   Recorder.RecordingState == RecordingState.RequestedStop;
#endif
			}
		}

		/// <summary>
		/// This is a placeholder method in case we need to communicate further with HTML to update the display during recording
		/// </summary>
		public void UpdateDisplay()
		{
		}

		private void OnStartRecordingTimer_Elapsed(object sender, EventArgs e)
		{
#if __MonoCS__
#else
			_startRecordingTimer.Stop();
			Debug.WriteLine("Start recording");
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)); // make sure audio directory exists
			Recorder.BeginRecording(Path);
#endif
		}

		private void WarnPressTooShort()
		{
			MessageBox.Show(null, LocalizationManager.GetString("EditTab.AudioControl.PleaseHold",
				"Please hold the record button down until you have finished recording", "Appears when the button is pressed very briefly"),
				 LocalizationManager.GetString("EditTab.AudioControl.PressToRecord", "Press to record", "Caption for PleaseHold message"));
			// If we had a prior recording, restore it...button press may have been a mistake.
			if (File.Exists(_backupPath))
			{
				try
				{
					File.Copy(_backupPath, Path, true);
				}
				catch (IOException)
				{
					// if we can't restore it we can't. Review: are there other exception types we should ignore? Should we bother the user?
				}
			}
		}

#if __MonoCS__
#else
		public RecordingDevice RecordingDevice
		{
			get { return Recorder.SelectedDevice; }
			set { Recorder.SelectedDevice = value; }
		}
#endif

		internal void ReportNoMicrophone()
		{
			MessageBox.Show(null,
				LocalizationManager.GetString("EditTab.AudioControl.NoMic", "This computer appears to have no sound recording device available. You will need one to record audio for a talking book."),
				LocalizationManager.GetString("EditTab.AudioControl.NoInput", "No input device"));
		}

		public void ChangeRecordingDevice(string deviceName)
		{
#if __MonoCS__
#else
			foreach (var dev in RecordingDevice.Devices)
			{
				if (dev.ProductName == deviceName)
				{
					RecordingDevice = dev;
				}
			}
#endif
		}
	}
}
