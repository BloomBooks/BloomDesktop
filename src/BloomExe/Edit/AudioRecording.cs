using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Api;
using L10NSharp;
using SIL.IO;
#if __MonoCS__
using SIL.Media.AlsaAudio;
#endif
using SIL.Media.Naudio;
using SIL.Progress;
using SIL.Reporting;
using Timer = System.Windows.Forms.Timer;

// Note: it is for the benefit of this component that Bloom references NAudio. We don't use it directly,
// but Palaso.Media does, and we need to make sure it gets copied to our output.

namespace Bloom.Edit
{

	public delegate AudioRecording Factory();//autofac uses this

	/// <summary>
	/// This is a clean back-end service that provides recording to files
	/// via some http requests from the server.
	/// It also delivers real time microphone peak level numbers over a WebSocket.
	/// The client can be found at audioRecording.ts.
	/// </summary>
	public class AudioRecording :IDisposable
	{
		private readonly BookSelection _bookSelection;
		private AudioRecorder _recorder;
		private bool _exitHookSet;
		BloomWebSocketServer _webSocketServer;
		private const string kWebsocketContext = "audio-recording"; // must match that found in audioRecording.tsx

		/// <summary>
		/// The file we want to record to
		/// </summary>
		public string PathToTemporaryWav;

		//the ultimate destination, after we've cleaned up the recording
		public string PathToCurrentAudioSegment;

		private string _backupPath; // If we are about to replace a recording, save the old one here; a temp file.
		private DateTime _startRecording; // For tracking recording length.
		LameEncoder _mp3Encoder = new LameEncoder();
		/// <summary>
		/// This timer introduces a brief delay from the mouse click to actually starting to record.
		/// Based on HearThis behavior, I think the purpose is to avoid recording the click,
		/// and perhaps also experience indicates the user typically pauses slightly between clicking and actually talking.
		/// HearThis uses a system timer rather than this normal form timer because with the latter, when the button "captured" the mouse, the timer refused to fire.
		/// I don't think we can capture the mouse (at least not attempting it yet) so Bloom does not have this problem  and uses a regular Windows.Forms timer.
		/// </summary>
		private  Timer _startRecordingTimer;

		private double _previousLevel;
		private bool _disposed;

		// This is a bit of a kludge. The server needs to be able to retrieve the data from AudioDevicesJson.
		// It would be quite messy to give the image server access to the EditingModel which owns the instance of AudioRecording.
		// However in practice (and very likely we would preserve this even if we had more than one book open at a time)
		// there is only one current AudioRecording object supporting the one EditingModel. This variable keeps track
		// of the one most recently created and uses it in the AudioDevicesJson method, which the server can therefore
		// call directly since it is static.
		private static AudioRecording CurrentRecording { get; set; }

		public AudioRecording(BookSelection bookSelection, BloomWebSocketServer bloomWebSocketServer)
		{
			_bookSelection = bookSelection;
			_startRecordingTimer = new Timer();
			_startRecordingTimer.Interval = 300; //  ms from click to actual recording
			_startRecordingTimer.Tick += OnStartRecordingTimer_Elapsed;
			_backupPath = System.IO.Path.GetTempFileName();
			CurrentRecording = this;
			_webSocketServer = bloomWebSocketServer;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			// I don't know for sure that these need to be on the UI thread, but that was the old default so keeping it for safety.
			server.RegisterEndpointHandler("audio/startRecord", HandleStartRecording, true);
			server.RegisterEndpointHandler("audio/endRecord", HandleEndRecord, true);
			server.RegisterEndpointHandler("audio/enableListenButton", HandleEnableListenButton, true);
			server.RegisterEndpointHandler("audio/deleteSegment", HandleDeleteSegment, true);
			server.RegisterEndpointHandler("audio/currentRecordingDevice", HandleCurrentRecordingDevice, true);
			server.RegisterEndpointHandler("audio/checkForSegment", HandleCheckForSegment, true);
			server.RegisterEndpointHandler("audio/devices", HandleAudioDevices, true);

			Debug.Assert(ServerBase.portForHttp > 0,"Need the server to be listening before this can be registered (BL-3337).");
		}

		// Does this page have any audio at all? Used to enable 'Listen to the whole page'.
		private void HandleEnableListenButton(ApiRequest request)
		{
			var ids = request.RequiredParam("ids");
			foreach (var id in ids.Split(','))
			{
				if (RobustFile.Exists(GetPathToSegment(id)))
				{
					request.PostSucceeded();
					return;
				}
			}
			request.Failed("no audio");
		}

		/// <summary>
		/// Returns a json string like {"devices":["microphone", "Logitech Headset"], "productName":"Logitech Headset", "genericName":"Headset"},
		/// except that in practice currrently the generic and product names are the same and not as helpful as the above.
		/// Devices is a list of product names (of available recording devices), the productName and genericName refer to the
		/// current selection (or will be null, if no current device).
		/// </summary>
		public void HandleAudioDevices(ApiRequest request)
		{
			var sb = new StringBuilder("{\"devices\":[");
			sb.Append(string.Join(",", RecordingDevice.Devices.Select(d => "\""+d.ProductName+"\"")));
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
			request.ReplyWithJson(sb.ToString());
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
			if (!RecordingDevice.Devices.Contains(RecordingDevice))
			{
				RecordingDevice = RecordingDevice.Devices.FirstOrDefault();
			}
			if (RecordingDevice != null)
			{
				Recorder.BeginMonitoring();
			}
		}

		private void SetPeakLevel(PeakLevelEventArgs args)
		{
			var level = Math.Round(args.Level, 3);
			if(level != _previousLevel)
			{
				_previousLevel = level;
				_webSocketServer.SendString(kWebsocketContext, "peakAudioLevel", level.ToString(CultureInfo.InvariantCulture));
			}
		}

		private void HandleEndRecord(ApiRequest request)
		{
			if (Recorder.RecordingState != RecordingState.Recording)
			{
				//usually, this is a result of us getting the "end" before we actually started, because it was too quick
				if(TestForTooShortAndSendFailIfSo(request))
				{
					_startRecordingTimer.Enabled = false;//we don't want it firing in a few milliseconds from now
					return;
				}

				//but this would handle it if there was some other reason
				request.Failed("Got endRecording, but was not recording");
				return;
			}
			Exception exceptionCaught = null;
			try
			{
				Debug.WriteLine("Stop recording");
				Recorder.Stopped += Recorder_Stopped;
				//note, this doesn't actually stop... more like... starts the stopping. It does mark the time
				//we requested to stop. A few seconds later (2, looking at the library code today), it will
				//actually close the file and raise the Stopped event
				Recorder.Stop();
			}
			catch (Exception ex)
			{
				// Swallow the exception for now. One reason (based on HearThis comment) is that the user
				// didn't hold the record button down long enough, we detect this below.
				exceptionCaught = ex;
				Recorder.Stopped -= Recorder_Stopped;
				Debug.WriteLine("Error stopping recording: " + ex.Message);
			}
			if (TestForTooShortAndSendFailIfSo(request))
			{
				return;
			}
			else if (exceptionCaught != null)
			{
				ResetRecorderOnError();
				request.Failed("Stopping the recording caught an exception: " + exceptionCaught.Message);
			}
			else
			{
				// Report success now that we're sure we succeeded.
				request.PostSucceeded();
			}
		}

		private void ResetRecorderOnError()
		{
			Debug.WriteLine("Resetting the audio recorder");
			// Try to delete the file we were writing to.
			try
			{
				RobustFile.Delete(PathToCurrentAudioSegment);
			}
			catch (Exception error)
			{
				Logger.WriteError("Audio Recording trying to delete "+PathToCurrentAudioSegment, error);
			}
			// The recorder may well be in a bad state.  Throw it away and get a new one.
			// But maintain the assigned recording device.
			var currentMic = RecordingDevice.ProductName;
			_recorder.Dispose();
			CreateRecorder();
			SetRecordingDevice(currentMic);
		}

		private void Recorder_Stopped(IAudioRecorder arg1, ErrorEventArgs arg2)
		{
			Recorder.Stopped -= Recorder_Stopped;
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathToCurrentAudioSegment)); // make sure audio directory exists
			int millisecondsToTrimFromEndForMouseClick =100;
			try
			{
				var minimum = TimeSpan.FromMilliseconds(300); // this is arbitrary
				AudioRecorder.TrimWavFile(PathToTemporaryWav, PathToCurrentAudioSegment, new TimeSpan(), TimeSpan.FromMilliseconds(millisecondsToTrimFromEndForMouseClick), minimum);
				RobustFile.Delete(PathToTemporaryWav);	// Otherwise, these continue to clutter up the temp directory.
			}
			catch (Exception error)
			{
				Logger.WriteEvent(error.Message);
				RobustFile.Copy(PathToTemporaryWav,PathToCurrentAudioSegment, true);
			}

			//We don't actually need the mp3 now, so let people play with recording even without LAME (previously it could crash BL-3159).
			//We could put this off entirely until we make the ePUB.
			//I'm just gating this for now because maybe the thought was that it's better to do it a little at a time?
			//That's fine so long as it doesn't make the UI unresponsive on slow machines.
			if (LameEncoder.IsAvailable())
			{
				_mp3Encoder.Encode(PathToCurrentAudioSegment, PathToCurrentAudioSegment.Substring(0, PathToCurrentAudioSegment.Length - 4), new NullProgress());
				// Note: we need to keep the .wav file as well as the mp3 one. The mp3 format (or alternative mp4)
				// is required for ePUB. The wav file is a better permanent record of the recording; also,
				// it is used for playback.
			}
		}

		private bool TestForTooShortAndSendFailIfSo(ApiRequest request)
		{
			if ((DateTime.Now - _startRecording) < TimeSpan.FromSeconds(0.5))
			{
				CleanUpAfterPressTooShort();
				var msg = LocalizationManager.GetString("EditTab.Toolbox.TalkingBook.PleaseHoldMessage",
					"Please hold the button down until you have finished recording",
					"Appears when the speak/record button is pressed very briefly");
				request.Failed(msg);
				return true;
			}
			return false;
		}

		/// <returns>true if the recording started successfully</returns>
		public void HandleStartRecording(ApiRequest request)
		{
			if(Recording)
			{
				request.Failed("Already recording");
				return;
			}

			string segmentId = request.RequiredParam("id");
			PathToCurrentAudioSegment = GetPathToSegment(segmentId);
			PathToTemporaryWav = Path.GetTempFileName();

			if (Recorder.RecordingState == RecordingState.RequestedStop)
			{
				request.Failed(LocalizationManager.GetString("EditTab.Toolbox.TalkingBook.BadState",
					"Bloom recording is in an unusual state, possibly caused by unplugging a microphone. You will need to restart.","This is very low priority for translation."));
			}

			// If someone unplugged the microphone we were planning to use switch to another.
			// This also triggers selecting the first one initially.
			if (!RecordingDevice.Devices.Contains(RecordingDevice))
			{
				RecordingDevice = RecordingDevice.Devices.FirstOrDefault();
			}
			if (RecordingDevice == null)
			{
				ReportNoMicrophone();
				request.Failed("No Microphone");
				return ;
			}

			if(Recording)
			{
				request.Failed( "Already Recording");
				return;
			}

			if (RobustFile.Exists(PathToCurrentAudioSegment))
			{
				//Try to deal with _backPath getting locked (BL-3160)
				try
				{
					RobustFile.Delete(_backupPath);
				}
				catch(IOException)
				{
					_backupPath = System.IO.Path.GetTempFileName();
				}
				try
				{
					RobustFile.Copy(PathToCurrentAudioSegment, _backupPath, true);
				}
				catch (Exception err)
				{
					ErrorReport.NotifyUserOfProblem(err,
						"Bloom cold not copy "+PathToCurrentAudioSegment+" to "+_backupPath+" If things remains stuck, you may need to restart your computer.");
					request.Failed( "Problem with backup file");
					return;
				}
				try
				{
					RobustFile.Delete(PathToCurrentAudioSegment);
				}
				catch (Exception err)
				{
					ErrorReport.NotifyUserOfProblem(err,
						"The old copy of the recording at " + PathToCurrentAudioSegment + " is locked up, so Bloom can't record over it at the moment. If it remains stuck, you may need to restart your computer.");
					request.Failed( "Audio file locked");
					return;
				}
			}
			else
			{
				RobustFile.Delete(_backupPath);
			}
			_startRecording = DateTime.Now;
			_startRecordingTimer.Start();
			request.ReplyWithText("starting record soon");
			return;
		}



		private string GetPathToSegment(string segmentId)
		{
			return System.IO.Path.Combine(_bookSelection.CurrentSelection.FolderPath, "audio", segmentId + ".wav");
		}

		public bool Recording
		{
			get
			{
				return Recorder.RecordingState == RecordingState.Recording ||
					   Recorder.RecordingState == RecordingState.RequestedStop;
			}
		}

		private void OnStartRecordingTimer_Elapsed(object sender, EventArgs e)
		{
			_startRecordingTimer.Stop();
			Debug.WriteLine("Start actual recording");
			Recorder.BeginRecording(PathToTemporaryWav);
		}

		private void CleanUpAfterPressTooShort()
		{
			// Seems sometimes on a very short click the recording actually got started while we were informing the user
			// that he didn't click long enough. Before we try to delete the file where the recording is taking place,
			// we have to stop it; otherwise, we will get an exception trying to delete it.
			while (Recording)
			{
				try
				{
					Recorder.Stop();
					Application.DoEvents();
				}
				catch (Exception)
				{
				}
			}
			// Don't kid the user we have a recording for this.
			// Also, the absence of the file is how the UI knows to switch back to the state where 'speak'
			// is the expected action.
			try
			{
				RobustFile.Delete(PathToCurrentAudioSegment);
			}
			catch (Exception error)
			{
				Logger.WriteError("Audio Recording trying to delete "+PathToCurrentAudioSegment, error);
				Debug.Fail("can't delete the recording even after we stopped:"+error.Message);
			}

			// If we had a prior recording, restore it...button press may have been a mistake.
			if (RobustFile.Exists(_backupPath))
			{
				try
				{
					RobustFile.Copy(_backupPath, PathToCurrentAudioSegment, true);
				}
				catch (IOException e)
				{
					Logger.WriteError("Audio Recording could not restore backup " + _backupPath, e);
					// if we can't restore it we can't. Review: are there other exception types we should ignore? Should we bother the user?
				}
			}
		}

		public RecordingDevice RecordingDevice
		{
			get { return Recorder.SelectedDevice; }
			set { Recorder.SelectedDevice = value; }
		}

		internal void ReportNoMicrophone()
		{
			MessageBox.Show(null,
				LocalizationManager.GetString("EditTab.Toolbox.TalkingBook.NoMic", "This computer appears to have no sound recording device available. You will need one to record audio for a talking book."),
				LocalizationManager.GetString("EditTab.Toolbox.TalkingBook.NoInput", "No input device"));
		}

		public void HandleCurrentRecordingDevice(ApiRequest request)
		{
			if(request.HttpMethod == HttpMethods.Post)
			{
				var name = request.RequiredPostString();
				if (SetRecordingDevice(name))
					request.PostSucceeded();
				else
					request.Failed("Could not find the device named " + name);
			}
			else request.Failed("Only Post is currently supported");
		}

		private bool SetRecordingDevice(string micName)
		{
			foreach (var d in RecordingDevice.Devices)
			{
				if (d.ProductName == micName)
				{
					RecordingDevice = d;
					return true;
				}
			}
			return false;
		}

		private void HandleCheckForSegment(ApiRequest request)
		{
			var path = GetPathToSegment(request.RequiredParam("id"));
			request.ReplyWithText(RobustFile.Exists(path) ? "exists" : "not found");
		}


		/// <summary>
		/// Delete a recording segment, as requested by the Clear button in the talking book tool.
		/// The corresponding mp3 should also be deleted.
		/// </summary>
		/// <param name="fileUrl"></param>
		private void HandleDeleteSegment(ApiRequest request)
		{
			var path = GetPathToSegment(request.RequiredParam("id"));
			var mp3Path = Path.ChangeExtension(path, "mp3");
			var success = true;
			if(RobustFile.Exists(path))
				success = DeleteFileReportingAnyProblem(path);
			if (RobustFile.Exists(mp3Path))
				success &= DeleteFileReportingAnyProblem(mp3Path);

			if (success)
			{
				request.PostSucceeded();
			}
			else
			{
				request.Failed("could not delete at least one file");
			}
		}

		private static bool DeleteFileReportingAnyProblem(string path)
		{
			try
			{
				RobustFile.Delete(path);
				return true;
			}
			catch (IOException e)
			{
				var msg =
					string.Format(
						LocalizationManager.GetString("Errors.ProblemDeletingFile", "Bloom had a problem deleting this file: {0}"), path);
				ErrorReport.NotifyUserOfProblem(e, msg + Environment.NewLine + e.Message);
			}
			return false;
		}


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
					var formToInvokeOn = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
					if (formToInvokeOn == null)
					{
						NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom could not find a form on which to start the level monitoring code. Please restart Bloom.");
						return null;
					}
					if(formToInvokeOn.InvokeRequired)
					{
						formToInvokeOn.Invoke((Action)(CreateRecorder));
					}
					else
					{
						CreateRecorder();
					}
				}
				return _recorder;
			}
		}

		private void CreateRecorder()
		{
			_recorder = new AudioRecorder(1);
			_recorder.PeakLevelChanged += ((s, e) => SetPeakLevel(e));
			BeginMonitoring();	// could get here recursively _recorder isn't set by now!
			if (_exitHookSet)
				return;
			// We want to do this only once.
			Application.ApplicationExit += OnApplicationExit;
			_exitHookSet = true;
		}

		private void OnApplicationExit(object sender, EventArgs args)
		{
			if (_recorder != null)
			{
				var temp = _recorder;
				_recorder = null;
				try
				{
					temp.Dispose();
				}
				catch (Exception)
				{
					// Not sure how this can fail, but we don't need to crash if
					// something goes wrong trying to free the audio object.
					Debug.Fail("Something went wrong disposing of AudioRecorder");
				}
			}
		}

		public virtual void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (disposing)
				{
					// dispose-only, i.e. non-finalizable logic
					if (_recorder != null)
					{
						_recorder.Dispose();
						_recorder = null;
						Application.ApplicationExit -= OnApplicationExit;
					}
				}

				// shared (dispose and finalizable) cleanup logic
				_disposed = true;
			}
		}
		~AudioRecording()
		{
			if (!_disposed)
			{
				NonFatalProblem.Report(ModalIf.Alpha,PassiveIf.Alpha,"AudioRecording was not disposed");
			}
		}
	}
}
