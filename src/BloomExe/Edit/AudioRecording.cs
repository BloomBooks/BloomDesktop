﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.web;
using L10NSharp;
#if __MonoCS__
#else
using SIL.Media.Naudio;
#endif
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
	public class AudioRecording
	{
		private readonly BookSelection _bookSelection;
		private AudioRecorder _recorder;
		BloomWebSocketServer _peakLevelWebSocketServer;
		
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

		// This is a bit of a kludge. The server needs to be able to retrieve the data from AudioDevicesJson.
		// It would be quite messy to give the image server access to the EditingModel which owns the instance of AudioRecording.
		// However in practice (and very likely we would preserve this even if we had more than one book open at a time)
		// there is only one current AudioRecording object supporting the one EditingModel. This variable keeps track
		// of the one most recently created and uses it in the AudioDevicesJson method, which the server can therefore
		// call directly since it is static.
		private static AudioRecording CurrentRecording { get; set; }

		public AudioRecording(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
			_startRecordingTimer = new Timer();
			_startRecordingTimer.Interval = 300; //  ms from click to actual recording
			_startRecordingTimer.Tick += OnStartRecordingTimer_Elapsed;
			_backupPath = System.IO.Path.GetTempFileName();
			CurrentRecording = this;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterSimpleHandler("audio/startRecord", HandleStartRecording);
			server.RegisterSimpleHandler("audio/endRecord", HandleEndRecord);
			server.RegisterSimpleHandler("audio/enableListenButton", HandleEnableListenButton);
			server.RegisterSimpleHandler("audio/deleteSegment", HandleDeleteSegment);
			server.RegisterSimpleHandler("audio/setRecordingDevice", HandleSetRecordingDevice);
			server.RegisterSimpleHandler("audio/checkForSegement", HandleCheckForSegment);

			_peakLevelWebSocketServer = new BloomWebSocketServer("8189");//review: we have no dispose (on us or our parent) so this is never disposed
		}

		// does this page have any audio at all? Used enable the Listen page.
		private void HandleEnableListenButton(SimpleHandlerRequest request)
		{
			request.Succeeded("Yes");// enhance: determine if there is any audio for this page
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
			var level = Math.Round(args.Level, 3);
			if(level != _previousLevel)
			{
				_previousLevel = level;
				_peakLevelWebSocketServer.Send(level.ToString(CultureInfo.InvariantCulture));
			}
		}
#endif

		private void HandleEndRecord(SimpleHandlerRequest request)
		{
#if __MonoCS__
#else
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
			try
			{
				Debug.WriteLine("Stop recording");
				Recorder.Stopped += Recorder_Stopped;
				//note, this doesn't actually stop... more like... starts the stopping. It does mark the time
				//we requested to stop. A few seconds later (2, looking at the library code today), it will
				//actually close the file and raise the Stopped event
				Recorder.Stop(); 
				//ReportSuccessfulRecordingAnalytics();
			}
			catch (Exception)
			{
				//swallow it. One reason (based on HearThis comment) is that they didn't hold it down long enough, we detect this below.
			}
			if (TestForTooShortAndSendFailIfSo(request))
				return;


#endif
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
			}
			catch (Exception error)
			{
				Logger.WriteEvent(error.Message);
				File.Copy(PathToTemporaryWav,PathToCurrentAudioSegment, true);
			}

			//We don't actually need the mp3 now, so let people play with recording even without LAME (previously it could crash BL-3159).
			//We could put this off entirely until we make the epub.
			//I'm just gating this for now because maybe the thought was that it's better to do it a little at a time?
			//That's fine so long as it doesn't make the UI unresponsive on slow machines.
			if (LameEncoder.IsAvailable())
			{
				_mp3Encoder.Encode(PathToCurrentAudioSegment, PathToCurrentAudioSegment.Substring(0, PathToCurrentAudioSegment.Length - 4), new NullProgress());
				// Note: we need to keep the .wav file as well as the mp3 one. The mp3 format (or alternative mp4)
				// is required for epub. The wav file is a better permanent record of the recording; also,
				// it is used for playback.
			}
		}

		private bool TestForTooShortAndSendFailIfSo(SimpleHandlerRequest request)
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
		public void HandleStartRecording(SimpleHandlerRequest request)
		{
#if __MonoCS__
						MessageBox.Show("Recording does not yet work on Linux", "Cannot record");
						return false;
#else
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

			if (File.Exists(PathToCurrentAudioSegment))
			{
				//Try to deal with _backPath getting locked (BL-3160)
				try
				{
					File.Delete(_backupPath);
				}
				catch(IOException)
				{
					_backupPath = System.IO.Path.GetTempFileName();
				}
				try
				{
					File.Copy(PathToCurrentAudioSegment, _backupPath, true);
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
					File.Delete(PathToCurrentAudioSegment);
					//DesktopAnalytics.Analytics.Track("Re-recorded a clip", ContextForAnalytics);
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
				File.Delete(_backupPath);
				//DesktopAnalytics.Analytics.Track("Recording clip", ContextForAnalytics);
			}
			_startRecording = DateTime.Now;
			_startRecordingTimer.Start();
			request.Succeeded("starting record soon");
			return;
#endif
		}

		

		private string GetPathToSegment(string segmentId)
		{
			return System.IO.Path.Combine(_bookSelection.CurrentSelection.FolderPath, "audio", segmentId + ".wav");
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

		private void OnStartRecordingTimer_Elapsed(object sender, EventArgs e)
		{
#if __MonoCS__
#else
			_startRecordingTimer.Stop();
			Debug.WriteLine("Start actual recording");
			Recorder.BeginRecording(PathToTemporaryWav);
#endif
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
				File.Delete(PathToCurrentAudioSegment);
			}
			catch (Exception error)
			{
				Logger.WriteError("Audio Recording trying to delete "+PathToCurrentAudioSegment, error);
				Debug.Fail("can't delete the recording even after we stopped:"+error.Message);
			}

			// If we had a prior recording, restore it...button press may have been a mistake.
			if (File.Exists(_backupPath))
			{
				try
				{
					File.Copy(_backupPath, PathToCurrentAudioSegment, true);
				}
				catch (IOException e)
				{
					Logger.WriteError("Audio Recording could not restore backup " + _backupPath, e);
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
				LocalizationManager.GetString("EditTab.Toolbox.TalkingBook.NoMic", "This computer appears to have no sound recording device available. You will need one to record audio for a talking book."),
				LocalizationManager.GetString("EditTab.Toolbox.TalkingBook.NoInput", "No input device"));
		}

		public void HandleSetRecordingDevice(SimpleHandlerRequest request)
		{ 
#if __MonoCS__
#else
			foreach (var dev in RecordingDevice.Devices)
			{
				if (dev.ProductName == request.Parameters["deviceName"])
				{
					RecordingDevice = dev;
					return;
				}
			}
#endif
		}

		private void HandleCheckForSegment(SimpleHandlerRequest request)
		{
			var path = GetPathToSegment(request.RequiredParam("id"));
			request.Succeeded(File.Exists(path) ? "exists" : "not found");
		}


		/// <summary>
		/// Delete a file (typically a recording, as requested by the Clear button in the talking book tool)
		/// </summary>
		/// <param name="fileUrl"></param>
		private void HandleDeleteSegment(SimpleHandlerRequest request)
		{
			var path = GetPathToSegment(request.RequiredParam("id"));
			if(!File.Exists(path))
			{
				request.Succeeded();
			}
			else
			{
				try
				{
					File.Delete(path);
				}
				catch(IOException e)
				{
					var msg =
						string.Format(
							LocalizationManager.GetString("Errors.ProblemDeletingFile", "Bloom had a problem deleting this file: {0}"), path);
					ErrorReport.NotifyUserOfProblem(e, msg + Environment.NewLine + e.Message);
				}
			}
		}


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
					Form.ActiveForm.Invoke((Action)(() =>
					{
						_recorder = new AudioRecorder(1);
						_recorder.PeakLevelChanged += ((s, e) => SetPeakLevel(e));
						BeginMonitoring(); // will call this recursively; make sure _recorder has been set by now!
						Application.ApplicationExit += (sender, args) =>
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
						};
					}));
				}
				return _recorder;
			}
		}
#endif
	}
}
