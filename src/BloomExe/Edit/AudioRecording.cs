using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Api;
using L10NSharp;
using SIL.IO;
using SIL.Media;
#if __MonoCS__
using SIL.Media.AlsaAudio;
#else
using SIL.Media.Naudio;
#endif
using SIL.Reporting;
using Timer = System.Windows.Forms.Timer;
using System.Collections.Generic;
using SIL.Code;

// Note: it is for the benefit of this component that Bloom references NAudio. We don't use it directly,
// but Palaso.Media does, and we need to make sure it gets copied to our output.

namespace Bloom.Edit
{
    public delegate AudioRecording Factory(); //autofac uses this

    /// <summary>
    /// This is a clean back-end service that provides recording to files
    /// via some http requests from the server.
    /// It also delivers real time microphone peak level numbers over a WebSocket.
    /// The client can be found at audioRecording.ts.
    /// </summary>
    public class AudioRecording : IDisposable
    {
        private readonly BookSelection _bookSelection;
        private AudioRecorder _recorder;
        private bool _exitHookSet;
        BloomWebSocketServer _webSocketServer;
        private const string kWebsocketContext = "audio-recording"; // must match that found in audioRecording.tsx

        public const string kPublishableExtension = "mp3";
        public const string kRecordableExtension = "wav";

        /// <summary>
        /// The file we want to record to
        /// </summary>
        public string PathToTemporaryWav;

        //the ultimate destination, after we've cleaned up the recording
        public string PathToRecordableAudioForCurrentSegment;

        private string _backupPathForRecordableAudio; // If we are about to replace a recording, save the old one here; a temp file.
        private string _backupPathForPublishableAudio;
        private DateTime _startRecording; // For tracking recording length.
        LameEncoder _mp3Encoder = new LameEncoder();

        /// <summary>
        /// This timer introduces a brief delay from the mouse click to actually starting to record.
        /// Based on HearThis behavior, I think the purpose is to avoid recording the click,
        /// and perhaps also experience indicates the user typically pauses slightly between clicking and actually talking.
        /// HearThis uses a system timer rather than this normal form timer because with the latter, when the button "captured" the mouse, the timer refused to fire.
        /// I don't think we can capture the mouse (at least not attempting it yet) so Bloom does not have this problem  and uses a regular Windows.Forms timer.
        /// </summary>
        private Timer _startRecordingTimer;

        private double _previousLevel;
        private bool _disposed;

        // This is a bit of a kludge. The server needs to be able to retrieve the data from AudioDevicesJson.
        // It would be quite messy to give the image server access to the EditingModel which owns the instance of AudioRecording.
        // However in practice (and very likely we would preserve this even if we had more than one book open at a time)
        // there is only one current AudioRecording object supporting the one EditingModel. This variable keeps track
        // of the one most recently created and uses it in the AudioDevicesJson method, which the server can therefore
        // call directly since it is static.
        private static AudioRecording CurrentRecording { get; set; }
        private ManualResetEvent _completingRecording; // Note: For simplicity, recommend that any function needing this lock should just check it regardless of the file path. The file paths get tricky with the multiple extensions possible, sequencing, etc., so for now, we recommend avoiding pre-mature optimization until needed.
        private int _collectionAudioTrimEndMilliseconds;
        private bool _monitoringAudio;

        public AudioRecording(
            BookSelection bookSelection,
            BloomWebSocketServer bloomWebSocketServer
        )
        {
            _bookSelection = bookSelection;
            _startRecordingTimer = new Timer();
            _startRecordingTimer.Interval = 300; //  ms from click to actual recording
            _startRecordingTimer.Tick += OnStartRecordingTimer_Elapsed;
            _backupPathForRecordableAudio = Path.GetTempFileName();
            _backupPathForPublishableAudio = Path.GetTempFileName();
            CurrentRecording = this;
            _webSocketServer = bloomWebSocketServer;
            // We create the ManualResetEvent in the "set" (non-blocking) state initially. The idea is to allow HandleEndRecord() to run,
            // but then block functions like HandleAudioFileRequest() which relies on the contents of the audio folder until Recorder_Stopped() has reported finishing saving the audio file.
            _completingRecording = new ManualResetEvent(true);
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // HandleStartRecording seems to need to be on the UI thread in order for HandleEndRecord() to detect the correct state.
            apiHandler
                .RegisterEndpointHandler("audio/startRecord", HandleStartRecording, true)
                .Measureable();

            // Note: This handler locks and unlocks a shared resource (_completeRecording lock).
            // Any other handlers depending on this resource should not wait on the same thread (i.e. the UI thread) or deadlock can occur.
            apiHandler
                .RegisterEndpointHandler("audio/endRecord", HandleEndRecord, true)
                .Measureable();

            // Any handler which retrieves information from the audio folder SHOULD wait on the _completeRecording lock (call WaitForRecordingToComplete()) to ensure that it sees
            // a consistent state of the audio folder, and therefore should NOT run on the UI thread.
            // Also, explicitly setting requiresSync to true (even tho that's default anyway) to make concurrency less complicated to think about
            apiHandler.RegisterEndpointHandler(
                "audio/checkForAnyRecording",
                HandleCheckForAnyRecording,
                false,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "audio/checkForAllRecording",
                HandleCheckForAllRecording,
                false,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "audio/deleteSegment",
                HandleDeleteSegment,
                false,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "audio/checkForSegment",
                HandleCheckForSegment,
                false,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "audio/wavFile",
                HandleAudioFileRequest,
                false,
                true
            );

            // Doesn't matter whether these are on UI thread or not, so using the old default which was true
            apiHandler.RegisterEndpointHandler(
                "audio/currentRecordingDevice",
                HandleCurrentRecordingDevice,
                true
            );
            apiHandler.RegisterEndpointHandler("audio/devices", HandleAudioDevices, true);

            apiHandler.RegisterEndpointHandler("audio/copyAudioFile", HandleCopyAudioFile, false);
            apiHandler.RegisterEndpointHandler("audio/stopMonitoring", HandleStopMonitoring, true);

            Debug.Assert(
                BloomServer.portForHttp > 0,
                "Need the server to be listening before this can be registered (BL-3337)."
            );
        }

        /// <summary>
        /// Dispose of our Recorder, thus we stop using the microphone.
        /// This is invoked when the talking book tool is deactivated.
        /// </summary>
        /// <remarks>There is not a corresponding API to start monitoring, because it happens
        /// automatically when we create a Recorder, which in turn happens when the talking book
        /// tool is activated and request the list of audio devices.</remarks>
        private void HandleStopMonitoring(ApiRequest request)
        {
            PauseMonitoringAudio(false);
            request.PostSucceeded();
        }

        // Does this page have any audio at all? Used to enable 'Listen to the whole page'.
        private void HandleCheckForAnyRecording(ApiRequest request)
        {
            var ids = request.RequiredParam("ids");
            var idList = ids.Split(',');

            if (idList.Any())
            {
                WaitForRecordingToComplete(); // More straightforward to test for the existence of the files by waiting until all the files have been written.
            }

            foreach (var id in idList)
            {
                if (RobustFile.Exists(GetPathToRecordableAudioForSegment(id)))
                {
                    request.PostSucceeded();
                    return;
                }

                if (RobustFile.Exists(GetPathToPublishableAudioForSegment(id)))
                {
                    request.PostSucceeded();
                    return;
                }
            }
            request.Failed("no audio");
        }

        private void HandleCheckForAllRecording(ApiRequest request)
        {
            var ids = request.RequiredParam("ids");
            var idList = ids.Split(',');

            if (idList.Any())
            {
                WaitForRecordingToComplete(); // More straightforward to test for the existence of the files by waiting until all the files have been written.
            }

            foreach (var id in idList)
            {
                if (
                    !RobustFile.Exists(GetPathToRecordableAudioForSegment(id))
                    && !RobustFile.Exists(GetPathToPublishableAudioForSegment(id))
                )
                {
                    request.ReplyWithBoolean(false);
                    return;
                }
            }
            request.ReplyWithBoolean(true);
        }

        /// <summary>
        /// Returns a json string like {"devices":["microphone", "Logitech Headset"], "productName":"Logitech Headset", "genericName":"Headset"},
        /// except that in practice currrently the generic and product names are the same and not as helpful as the above.
        /// Devices is a list of product names (of available recording devices), the productName and genericName refer to the
        /// current selection (or will be null, if no current device).
        /// </summary>
        public void HandleAudioDevices(ApiRequest request)
        {
            try
            {
                var sb = new StringBuilder("{\"devices\":[");
                sb.Append(
                    string.Join(",", RecordingDevices.Select(d => "\"" + d.ProductName + "\""))
                );
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
            catch (Exception e)
            {
                Logger.WriteError("AudioRecording could not find devices: ", e);
                // BL-7272 shows an exception occurred somewhere, and it may have been here.
                // If so, we just assume no input devices could be found.
                request.ReplyWithJson("{\"devices\":[],\"productName\":null,\"genericName\":null}");
            }
        }

        /// <summary>
        /// lock object for the BeginMonitoring method.
        /// </summary>
        object _beginMonitoringLock = new object();

        /// <summary>
        /// Used to initiate sending the PeakLevelChanged notifications.
        /// Currently this typically happens when the Recorder instance is created,
        /// which is usually when the talking book tool asks for the AudioDevicesJson.
        /// This is not very intuitive, but it's the most easily detectable event
        /// that indicates that the talking book tool is actually active.
        /// </summary>
        public void BeginMonitoring()
        {
            // Multiple crashes have been reported due to calling Recorder.BeginMonitoring() more than once for an
            // instance of Recorder.  Using both a lock and testing the Recorder state should prevent this from happening.
            // See BL-13003.
            lock (_beginMonitoringLock)
            {
                if (!RecordingDevices.Contains(RecordingDevice))
                {
                    RecordingDevice = RecordingDevices.FirstOrDefault();
                }
                if (
                    RecordingDevice != null
                    && /* Don't check _monitoringAudio because we can have a new Recorder after resuming */
                    (
                        Recorder.RecordingState == RecordingState.NotYetStarted
                        || Recorder.RecordingState == RecordingState.Stopped
                    )
                )
                {
                    try
                    {
                        Recorder.BeginMonitoring(catchAndReportExceptions: false);
                        _monitoringAudio = true;
                    }
                    catch (Exception e)
                    {
                        Logger.WriteError("Could not begin monitoring microphone", e);
                        var msg = LocalizationManager.GetString(
                            "EditTab.Toolbox.TalkingBookTool.MicrophoneAccessProblem",
                            "Bloom was not able to access a microphone."
                        );
                        _webSocketServer.SendString(kWebsocketContext, "monitoringStartError", msg);
                    }
                }
            }
        }

        private void SetPeakLevel(PeakLevelEventArgs args)
        {
            var level = Math.Round(args.Level, 3);
            if (level != _previousLevel)
            {
                _previousLevel = level;
                _webSocketServer.SendString(
                    kWebsocketContext,
                    "peakAudioLevel",
                    level.ToString(CultureInfo.InvariantCulture)
                );
            }
        }

        private void HandleEndRecord(ApiRequest request)
        {
            if (Recorder.RecordingState != RecordingState.Recording)
            {
                //usually, this is a result of us getting the "end" before we actually started, because it was too quick
                if (TestForTooShortAndSendFailIfSo(request))
                {
                    _startRecordingTimer.Enabled = false; //we don't want it firing in a few milliseconds from now
                    return;
                }
                if (
                    RecordingDevice == null
                    && Recorder.RecordingState == RecordingState.NotYetStarted
                )
                {
                    // We've already complained about no recording device, no need to complain about not recording.
                    request.PostSucceeded();
                    return;
                }

                //but this would handle it if there was some other reason
                request.Failed("Got endRecording, but was not recording");
                return;
            }

            Exception exceptionCaught = null;
            try
            {
                // We never want this thread blocked, but we want to block HandleAudioFileRequest()
                // until Recorder_Stopped() succeeds.
                _completingRecording.Reset();
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
                _completingRecording.Set(); // not saving a recording, so don't block HandleAudioFileRequest
                return;
            }
            else if (exceptionCaught != null)
            {
                ResetRecorderOnError();
                _completingRecording.Set(); // not saving a recording, so don't block HandleAudioFileRequest
                request.Failed(
                    "Stopping the recording caught an exception: " + exceptionCaught.Message
                );
            }
            else
            {
                // Report success now that we're sure we succeeded.
                request.PostSucceeded();
            }
        }

        /// <summary>
        /// If bloom is currently using the microphone (typically to monitor volume), stop.
        /// If autoResume is true, Bloom should start monitoring again when activated,
        /// iff it is currently monitoring.
        /// </summary>
        public void PauseMonitoringAudio(bool autoResume)
        {
            if (_recorder != null)
            {
                _recorder.Dispose();
                _recorder = null;
            }

            if (!autoResume)
                _monitoringAudio = false;
        }

        public void ResumeMonitoringAudio()
        {
            // In most cases, when the talking book tool is activated, it initializes a Recorder
            // and monitoring starts (a side effect of building a list of possible audio devices).
            // The one current exception is when Bloom is deactivated while the talking book tool
            // is visible. In that case, we need to force it to be re-created, since the talking
            // book tool has no awareness of the app being activated.
            if (_monitoringAudio)
            {
                var dummy = Recorder;
            }
        }

        private void ResetRecorderOnError()
        {
            Debug.WriteLine("Resetting the audio recorder");
            // Try to delete the file we were writing to.
            try
            {
                RobustFile.Delete(PathToRecordableAudioForCurrentSegment);
            }
            catch (Exception error)
            {
                Logger.WriteError(
                    "Audio Recording trying to delete " + PathToRecordableAudioForCurrentSegment,
                    error
                );
            }
            // The recorder may well be in a bad state.  Throw it away and get a new one.
            // But maintain the assigned recording device.
            var currentMic = RecordingDevice.ProductName;
            _recorder.Dispose();
            CreateRecorder();
            SetRecordingDevice(currentMic);
        }

        static HashSet<Type> retryMp3Exceptions = new HashSet<Type>
        {
            Type.GetType("System.IO.IOException"),
            Type.GetType("System.ApplicationException")
        };

        private void Recorder_Stopped(IAudioRecorder arg1, ErrorEventArgs arg2)
        {
            Recorder.Stopped -= Recorder_Stopped;
            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(PathToRecordableAudioForCurrentSegment)
            ); // make sure audio directory exists
            try
            {
                var minimum = TimeSpan.FromMilliseconds(300); // this is arbitrary
                AudioRecorder.TrimWavFile(
                    PathToTemporaryWav,
                    PathToRecordableAudioForCurrentSegment,
                    new TimeSpan(),
                    TimeSpan.FromMilliseconds(_collectionAudioTrimEndMilliseconds),
                    minimum
                );
                RobustFile.Delete(PathToTemporaryWav); // Otherwise, these continue to clutter up the temp directory.
            }
            catch (Exception error)
            {
                Logger.WriteEvent(error.Message);
                RobustFile.Copy(PathToTemporaryWav, PathToRecordableAudioForCurrentSegment, true);
            }

            //We could put this off entirely until we make the ePUB.
            //I'm just gating this for now because maybe the thought was that it's better to do it a little at a time?
            //That's fine so long as it doesn't make the UI unresponsive on slow machines.
            string mp3Path = "";
            RetryUtility.Retry(
                () =>
                {
                    mp3Path = _mp3Encoder.Encode(PathToRecordableAudioForCurrentSegment);
                },
                10,
                200,
                retryMp3Exceptions,
                memo: "Encode mp3"
            );
            // Got a good new recording, can safely clean up all backups related to old one.
            foreach (
                var path in Directory.EnumerateFiles(
                    Path.GetDirectoryName(PathToRecordableAudioForCurrentSegment),
                    Path.GetFileNameWithoutExtension(PathToRecordableAudioForCurrentSegment)
                        + "*"
                        + ".bak"
                )
            )
            {
                RobustFile.Delete(path);
            }

            // BL-7617 Don't keep .wav file after .mp3 is created successfully.
            if (!string.IsNullOrEmpty(mp3Path) && RobustFile.Exists(mp3Path))
            {
                RobustFile.Delete(PathToRecordableAudioForCurrentSegment);
            }
            _completingRecording.Set(); // will release HandleAudioFileRequest if it is waiting.
        }

        private bool TestForTooShortAndSendFailIfSo(ApiRequest request)
        {
            if ((DateTime.Now - _startRecording) < TimeSpan.FromSeconds(0.5))
            {
                CleanUpAfterPressTooShort();
                var msg = LocalizationManager.GetString(
                    "EditTab.Toolbox.TalkingBook.PleaseHoldMessage",
                    "Please hold the button down until you have finished recording",
                    "Appears when the speak/record button is pressed very briefly"
                );
                request.Failed(msg);
                return true;
            }
            return false;
        }

        public void HandleStartRecording(ApiRequest request)
        {
            // Precondition: HandleStartRecording shouldn't run until the previous HandleEndRecord() is completely done with PathToRecordableAudioForCurrentSegment
            //   Unfortunately this is not as easy to ensure on the code side due to HandleStartRecord() not being able to be moved off the UI thread, and deadlock potential
            //   I found it too difficult to actually violate this precondition from the user side.
            //   Therefore, I just assume this to be true.

            if (Recording)
            {
                request.Failed("Already recording");
                return;
            }

            string segmentId = request.RequiredParam("id");
            PathToRecordableAudioForCurrentSegment = GetPathToRecordableAudioForSegment(segmentId); // Careful! Overwrites the previous value of the member variable.
            PathToTemporaryWav = Path.GetTempFileName();

            if (Recorder.RecordingState == RecordingState.RequestedStop)
            {
                request.Failed(
                    LocalizationManager.GetString(
                        "EditTab.Toolbox.TalkingBook.BadState",
                        "Bloom recording is in an unusual state, possibly caused by unplugging a microphone. You will need to restart.",
                        "This is very low priority for translation."
                    )
                );
            }

            // If someone unplugged the microphone we were planning to use switch to another.
            // This also triggers selecting the first one initially.
            if (!RecordingDevices.Contains(RecordingDevice))
            {
                RecordingDevice = RecordingDevices.FirstOrDefault();
            }
            if (RecordingDevice == null)
            {
                ReportNoMicrophone();
                request.Failed("No Microphone");
                return;
            }

            if (Recording)
            {
                request.Failed("Already Recording");
                return;
            }

            if (
                !PrepareBackupFile(
                    PathToRecordableAudioForCurrentSegment,
                    ref _backupPathForRecordableAudio,
                    request
                )
            )
                return;

            // There are two possible scenarios when starting to record.
            //  1. We have a recordable file and corresponding publishable file.
            //     In that case, we need to make sure to restore the publishable file if we restore the recordable one so they stay in sync.
            //  2. We have an publishable file with no corresponding recordable file.
            //     In that case, we need to restore it if there is any problem creating a new recordable file.
            if (
                !PrepareBackupFile(
                    GetPathToPublishableAudioForSegment(segmentId),
                    ref _backupPathForPublishableAudio,
                    request
                )
            )
                return;

            _startRecording = DateTime.Now;
            _startRecordingTimer.Start();
            request.ReplyWithText("starting record soon");
        }

        // We want to move the file specified in the first path to a new location to use
        // as a backup while we typically replace it.
        // A previous backup, possibly of the same or another file, is no longer needed (if it exists)
        // and should be deleted, if possible, on a background thread.
        // The path to the backup will be updated to the new backup.
        // Typically the new name matches the original with the extension changed to .bak.
        // If necessary (because the desired backup file already exists), we will add a counter
        // to get the a name that is not in use.
        // A goal is (for performance reasons) not to have to wait while a file is deleted
        // (and definitely not while one is copied).
        private static bool PrepareBackupFile(
            string path,
            ref string backupPath,
            ApiRequest request
        )
        {
            int counter = 0;
            backupPath = path + ".bak";
            var originalExtension = Path.GetExtension(path);
            var pathWithNoExtension = Path.GetFileNameWithoutExtension(path);
            while (RobustFile.Exists(backupPath))
            {
                counter++;
                backupPath = pathWithNoExtension + counter + originalExtension + ".bak";
            }
            // An earlier version copied the file to a temp file. We can't MOVE to a file in the system temp
            // directory, though, because we're not sure it is on the same volume. And sometimes the time
            // required to copy the file was noticeable and resulted in the user starting to speak before
            // the system started recording. So we pay the price of a small chance of backups being left
            // around the book directory to avoid that danger.
            if (RobustFile.Exists(path))
            {
                try
                {
                    RobustFile.Move(path, backupPath);
                }
                catch (Exception err)
                {
                    ErrorReport.NotifyUserOfProblem(
                        err,
                        "The old copy of the recording at "
                            + path
                            + " is locked up, so Bloom can't record over it at the moment. If it remains stuck, you may need to restart your computer."
                    );
                    request.Failed("Audio file locked");
                    return false;
                }
            }

            return true;
        }

        private string GetPathToPublishableAudioForSegment(string segmentId)
        {
            if (_bookSelection?.CurrentSelection?.FolderPath == null)
            {
                return "";
            }
            return Path.Combine(
                _bookSelection.CurrentSelection.FolderPath,
                "audio",
                $"{segmentId}.{kPublishableExtension}"
            );
        }

        private string GetPathToRecordableAudioForSegment(string segmentId)
        {
            if (_bookSelection?.CurrentSelection?.FolderPath == null)
            {
                return "";
            }
            return Path.Combine(
                _bookSelection.CurrentSelection.FolderPath,
                "audio",
                $"{segmentId}.{kRecordableExtension}"
            );
        }

        public bool Recording
        {
            get
            {
                return Recorder.RecordingState == RecordingState.Recording
                    || Recorder.RecordingState == RecordingState.RequestedStop;
            }
        }

        private void OnStartRecordingTimer_Elapsed(object sender, EventArgs e)
        {
            _startRecordingTimer.Stop();
            Debug.WriteLine("Start actual recording");
            try
            {
                Recorder.BeginRecording(PathToTemporaryWav);
            }
            catch (InvalidOperationException ex)
            {
                // Likely a case of BL-7568, which as far as we can figure isn't Bloom's fault.
                // Show a friendly message in the TalkingBook toolbox.
                Logger.WriteError("Could not begin recording", ex);
                var msg = LocalizationManager.GetString(
                    "EditTab.Toolbox.TalkingBook.MicrophoneProblem",
                    "Bloom is having problems connecting to your microphone. Please restart your computer and try again."
                );
                _webSocketServer.SendString(kWebsocketContext, "recordingStartError", msg);
            }
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
                catch (Exception) { }
            }
            // Don't kid the user we have a recording for this.
            // Also, the absence of the file is how the UI knows to switch back to the state where 'speak'
            // is the expected action.
            try
            {
                // Delete doesn't throw if the FILE doesn't exist, but if the Directory doesn't, you're toast.
                // And the very first time a user tries this, the audio directory probably doesn't exist...
                if (Directory.Exists(Path.GetDirectoryName(PathToRecordableAudioForCurrentSegment)))
                {
                    RobustFile.Delete(PathToRecordableAudioForCurrentSegment);
                    // BL-6881: "Play btn sometimes enabled after too short audio", because the .mp3 version was left behind.
                    var mp3Version = Path.ChangeExtension(
                        PathToRecordableAudioForCurrentSegment,
                        kPublishableExtension
                    );
                    RobustFile.Delete(mp3Version);
                }
            }
            catch (Exception error)
            {
                Logger.WriteError(
                    "Audio Recording trying to delete " + PathToRecordableAudioForCurrentSegment,
                    error
                );
                Debug.Fail("can't delete the recording even after we stopped:" + error.Message);
            }

            // If we had a prior recording, restore it...button press may have been a mistake.
            if (RobustFile.Exists(_backupPathForRecordableAudio))
            {
                try
                {
                    RobustFile.Move(
                        _backupPathForRecordableAudio,
                        PathToRecordableAudioForCurrentSegment
                    );
                }
                catch (IOException e)
                {
                    Logger.WriteError(
                        "Audio Recording could not restore backup " + _backupPathForRecordableAudio,
                        e
                    );
                    // if we can't restore it we can't. Review: are there other exception types we should ignore? Should we bother the user?
                }
            }
            if (RobustFile.Exists(_backupPathForPublishableAudio))
            {
                try
                {
                    RobustFile.Move(
                        _backupPathForPublishableAudio,
                        Path.ChangeExtension(
                            PathToRecordableAudioForCurrentSegment,
                            kPublishableExtension
                        )
                    );
                }
                catch (IOException e)
                {
                    Logger.WriteError(
                        "Audio Recording could not restore backup "
                            + _backupPathForPublishableAudio,
                        e
                    );
                }
            }
        }

        public IRecordingDevice RecordingDevice
        {
            get { return Recorder.SelectedDevice; }
            set { Recorder.SelectedDevice = value; }
        }

        private IEnumerable<IRecordingDevice> RecordingDevices
        {
#if __MonoCS__
            get { return SIL.Media.AlsaAudio.RecordingDevice.Devices; }
#else
            get { return SIL.Media.Naudio.RecordingDevice.Devices; }
#endif
        }

        internal void ReportNoMicrophone()
        {
            MessageBox.Show(
                null,
                LocalizationManager.GetString(
                    "EditTab.Toolbox.TalkingBook.NoMic",
                    "This computer appears to have no sound recording device available. You will need one to record audio for a talking book."
                ),
                LocalizationManager.GetString(
                    "EditTab.Toolbox.TalkingBook.NoInput",
                    "No input device"
                )
            );
        }

        public void HandleCurrentRecordingDevice(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Post)
            {
                var name = request.RequiredPostString();
                if (SetRecordingDevice(name))
                    request.PostSucceeded();
                else
                    request.Failed("Could not find the device named " + name);
            }
            else
                request.Failed("Only Post is currently supported");
        }

        private bool SetRecordingDevice(string micName)
        {
            foreach (var d in RecordingDevices)
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
            var segmentId = request.RequiredParam("id");
            var path = GetPathToRecordableAudioForSegment(segmentId);

            WaitForRecordingToComplete(); // Wait until the recording is flushed to disk before testing file existence

            if (RobustFile.Exists(path))
                request.ReplyWithText("exists");
            else
            {
                path = GetPathToPublishableAudioForSegment(segmentId);
                request.ReplyWithText(RobustFile.Exists(path) ? "exists" : "not found");
            }
        }

        /// <summary>
        /// Returns the content of the requested .mp3 file.
        /// </summary>
        /// <param name="request"></param>
        private void HandleAudioFileRequest(ApiRequest request)
        {
            const string Api_Prefix = "bloom/";
            if (request.HttpMethod == HttpMethods.Get)
            {
                // RequiredParam() decodes the url parameters, so we don't need to do any UrlPathString decoding here.
                var idWithPrefix = request.RequiredParam("id");
                var bloomIndex = idWithPrefix.IndexOf(Api_Prefix);
                var id = idWithPrefix.Substring(bloomIndex + Api_Prefix.Length);
                var segmentId = Path.GetFileNameWithoutExtension(id);

                WaitForRecordingToComplete();

                // return the audio file contents
                var mp3File = GetPathToPublishableAudioForSegment(segmentId);
                if (RobustFile.Exists(mp3File))
                {
                    request.ReplyWithAudioFileContents(mp3File);
                    return;
                }

                request.Failed("Somehow we don't have the .mp3 file.");
            }
            else
                request.Failed("Only Get is currently supported");
        }

        /// <summary>
        /// Delete a recording segment, as requested by the Clear button in the talking book tool.
        /// The corresponding mp3 should also be deleted.
        /// </summary>
        private void HandleDeleteSegment(ApiRequest request)
        {
            var segmentId = request.RequiredParam("id");
            var recordablePath = GetPathToRecordableAudioForSegment(segmentId);
            var publishablePath = GetPathToPublishableAudioForSegment(segmentId);
            var success = true;

            WaitForRecordingToComplete(); // Wait for any files to (potentially) flush to disk before trying to deleting them.

            if (RobustFile.Exists(recordablePath))
                success = DeleteFileReportingAnyProblem(recordablePath);

            if (RobustFile.Exists(publishablePath))
                success &= DeleteFileReportingAnyProblem(publishablePath);

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
                var msg = string.Format(
                    LocalizationManager.GetString(
                        "Errors.ProblemDeletingFile",
                        "Bloom had a problem deleting this file: {0}"
                    ),
                    path
                );
                ErrorReport.NotifyUserOfProblem(e, msg + Environment.NewLine + e.Message);
            }
            return false;
        }

        /// <summary>
        ///  Waits (if necessary) for any recordings to complete (regardless of where it is trying to save the recording)
        /// </summary>
        private void WaitForRecordingToComplete()
        {
            _completingRecording.WaitOne(); // This will block if we ran HandleEndRecord, but haven't finished saving.
        }

        private void HandleCopyAudioFile(ApiRequest request)
        {
            var oldId = request.RequiredParam("oldId");
            var newId = request.RequiredParam("newId");
            var oldPath = GetPathToPublishableAudioForSegment(oldId);
            var newPath = GetPathToPublishableAudioForSegment(newId);
            if (RobustFile.Exists(oldPath))
            {
                RobustFile.Copy(oldPath, newPath);
            }
            // If the old file doesn't exist, it's probably because one hasn't been recorded before the user decided
            // to copy and paste some text.  See https://issues.bloomlibrary.org/youtrack/issue/BL-10291.  Setting a
            // new id in the copied element without having actual audio behind it is perfectly okay.
            request.PostSucceeded();
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
                    var formToInvokeOn = Shell.GetShellOrOtherOpenForm();
                    if (formToInvokeOn == null)
                    {
                        NonFatalProblem.Report(
                            ModalIf.All,
                            PassiveIf.All,
                            "Bloom could not find a form on which to start the level monitoring code. Please restart Bloom."
                        );
                        return null;
                    }
                    if (formToInvokeOn.InvokeRequired)
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
            _collectionAudioTrimEndMilliseconds = _bookSelection
                .CurrentSelection
                .CollectionSettings
                .AudioRecordingTrimEndMilliseconds;
            _recorder = new AudioRecorder(1);
            _recorder.PeakLevelChanged += ((s, e) => SetPeakLevel(e));
            BeginMonitoring(); // could get here recursively _recorder isn't set by now!
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
                NonFatalProblem.Report(
                    ModalIf.Alpha,
                    PassiveIf.Alpha,
                    "AudioRecording was not disposed"
                );
            }
        }
    }
}
