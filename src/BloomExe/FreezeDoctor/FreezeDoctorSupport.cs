using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using SIL.Reporting;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Publishes just enough about Bloom's health for the Bloom Freeze Doctor
    /// (https://github.com/BloomBooks/bloom-freeze-doctor) to tell a frozen Bloom from a busy one, and a
    /// crash from an orderly shutdown.
    ///
    /// **Why Bloom needs to help at all.** Watching from outside cannot detect the freeze we most expect.
    /// A UI thread blocked in a managed wait on an STA thread — `WaitHandle.WaitOne`, `Task.Wait`,
    /// anything that ends in `CoWaitForMultipleHandles` — keeps dispatching *sent* messages so that COM
    /// still works. The window therefore answers `SendMessageTimeout`, `IsHungAppWindow` reports it as
    /// healthy, and `Process.Responding` says `true`, while Bloom is completely stuck. This was measured,
    /// not assumed. `WM_TIMER` is *not* dispatched by those restricted pumps, so a timer that stops
    /// ticking is the one signal that gives the freeze away — and since Bloom's UI thread awaits WebView2
    /// constantly, we should expect this shape of freeze to be common rather than exotic.
    ///
    /// **The rules this code lives by**, because it runs on the UI thread and in the shutdown path of an
    /// application whose shutdown has historically been fragile:
    ///
    /// - It must never throw. Every entry point swallows its own failures.
    /// - It must never block. Nothing here waits on anything.
    /// - It must never matter. If the channel cannot be created, Bloom carries on exactly as before, and
    ///   the Doctor falls back to watching from outside as it does for every Bloom already in the field.
    /// </summary>
    public static class FreezeDoctorSupport
    {
        /// <summary>
        /// How often the UI thread reports in. Frequent enough that a freeze is obvious within the
        /// Doctor's shortest threshold, cheap enough to be invisible: this is one `WM_TIMER` and a
        /// handful of writes to a page of memory already resident.
        /// </summary>
        private static readonly TimeSpan UiHeartbeatInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// How often the background thread reports in. Its purpose is comparison: if the UI heartbeat is
        /// stale and this one is fresh, the UI thread is blocked; if both are stale, the whole process is
        /// wedged — a garbage collection that will not finish, or a suspended process.
        /// </summary>
        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

        private static DoctorChannelWriter _channel;

        // Explicitly the WinForms timer, and that is the whole point: it delivers WM_TIMER through the
        // message loop, so it stops ticking exactly when the UI thread stops pumping. A
        // System.Threading.Timer would keep ticking right through the freeze we are trying to detect.
        private static System.Windows.Forms.Timer _uiHeartbeat;
        private static Thread _watchdog;
        private static int _shutdownPhase;
        private static bool _started;
        private static DoctorSession _session;

        /// <summary>
        /// True once we have exited because the Doctor asked us to. Stops the ProcessExit handler recording
        /// that as an orderly shutdown, which it very much was not.
        /// </summary>
        private static bool _endedAtDoctorsRequest;

        /// <summary>
        /// How often the session file is rewritten. It carries facts that barely change, so this is not
        /// about freshness — it is so that a Doctor installed *after* Bloom started, or one that had not
        /// yet been running when Bloom did, still finds a file describing the Bloom in front of it.
        /// </summary>
        private static readonly TimeSpan SessionRefreshInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How long an unexplained session file is kept. Long enough that a Doctor installed the day after
        /// a crash can still find out about it, short enough not to accumulate.
        /// </summary>
        private static readonly TimeSpan SessionRetention = TimeSpan.FromDays(7);

        /// <summary>
        /// Starts publishing. Call once, on the UI thread, immediately before entering the message loop:
        /// the UI heartbeat is a WinForms timer, so it only ticks once messages are being pumped, which is
        /// exactly the property that makes it a useful signal.
        /// </summary>
        public static void Start()
        {
            if (_started)
                return;
            _started = true;

            // The session file goes first, and happens even if the shared-memory channel cannot be
            // created: it is what a Doctor reads about a Bloom that has already died, so it is the more
            // important of the two for the case where nobody was watching at the time.
            WriteSessionFile();

            try
            {
                _channel = new DoctorChannelWriter(Process.GetCurrentProcess().Id);
                if (!_channel.IsOpen)
                    return; // Another process owns the name, or the OS refused. Nothing more to do.

                _channel.SetActivity("starting up");
                // Authoritative, and worth more than the Doctor's own guess: it is why a developer
                // stopping their debugger never produces a report.
                _channel.SetDebuggerAttached(Debugger.IsAttached);

                _uiHeartbeat = new System.Windows.Forms.Timer
                {
                    Interval = (int)UiHeartbeatInterval.TotalMilliseconds,
                };
                _uiHeartbeat.Tick += (sender, args) => SafeRecordUiTick();
                _uiHeartbeat.Start();

                _watchdog = new Thread(WatchdogLoop)
                {
                    Name = "Freeze Doctor watchdog",
                    // Background, so it can never be the reason Bloom fails to exit. The Doctor treats a
                    // vanished heartbeat as the process being gone, which by then it is.
                    IsBackground = true,
                    // Slightly above normal so that a machine thrashing under load does not starve the
                    // very signal we use to distinguish thrashing from a deadlock.
                    Priority = ThreadPriority.AboveNormal,
                };
                _watchdog.Start();

                // ProcessExit is the whole of the clean-exit proof, and the reason it is a hook rather
                // than an edit to each exit path: it runs for a normal return from Main and for
                // Environment.Exit, and NOT for FailFast, TerminateProcess, or an access violation. That
                // is precisely the line the Doctor wants drawn, and no future exit path in Program.cs can
                // forget to honour it.
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => RecordCleanExit();

                // Ask for a dump on the way down. Hooked here rather than only at Program.Run's two catch
                // blocks because those catch just TargetInvocationException and AccessViolationException
                // from the message loop, whereas this fires for an unhandled exception on ANY thread — far
                // more of the crashes we actually want to explain.
                //
                // Note what this deliberately does NOT cover: a direct Environment.FailFast runs no managed
                // handlers at all, by design, so no dump can be requested for one. Those crashes are still
                // recognisable, through Windows Error Reporting and through the absence of a clean-exit
                // proof; we just do not get a dump of our own.
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                    RequestDumpBeforeDying();

                Logger.WriteEvent(
                    "Freeze Doctor channel opened; publishing health to any Doctor watching"
                );
            }
            catch (Exception e)
            {
                // Deliberately swallowed. Diagnostics that can break the application they diagnose are
                // worse than no diagnostics at all.
                TryLog("Freeze Doctor channel could not be opened", e);
            }
        }

        /// <summary>
        /// Says what Bloom is doing, in words fit to appear at the top of a bug report — "Publishing to
        /// BloomPUB", "Saving Foo.htm". Truncated if very long; safe to call from any thread.
        /// </summary>
        public static void SetActivity(string activity)
        {
            try
            {
                _channel?.SetActivity(activity);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Marks work that legitimately blocks the UI for a long time — publishing, uploading, making a
        /// PDF. This buys Bloom patience rather than silence: the Doctor raises its threshold from one
        /// minute to five, and still reports if Bloom is stuck beyond that.
        /// </summary>
        public static void SetLongOperation(bool inProgress)
        {
            try
            {
                _channel?.SetLongOperation(inProgress);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records how far shutdown has got. Called at the points Bloom passes through on its way out, so
        /// that a Bloom which dies mid-shutdown can say *where* it stopped rather than merely that it did
        /// — which is the same evidence the "UI gone but process alive" case needs.
        /// </summary>
        public static void SetShutdownPhase(int phase)
        {
            try
            {
                _shutdownPhase = phase;
                _channel?.SetShutdownPhase(phase);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records that shutdown ran to completion. Its ABSENCE is what the Doctor treats as evidence, so
        /// this must be written only when Bloom really did shut down in an orderly way.
        /// </summary>
        private static void RecordCleanExit()
        {
            try
            {
                _channel?.SetShutdownPhase(_shutdownPhase);
                // Only claim a clean exit when it was one. A Doctor-requested exit reaches here too, via
                // Environment.Exit, and recording it as orderly would tell the next reader the opposite of
                // the truth about a Bloom we had to end.
                if (!_endedAtDoctorsRequest)
                    _channel?.RecordCleanExit();
                // Also on disk, because the shared-memory page vanishes once the last handle closes and a
                // Doctor may not have been watching. The file is what a Doctor started tomorrow will read.
                WriteSessionExit();
            }
            catch (Exception)
            {
                // Losing the proof means the Doctor may report an exit that was in fact clean. Annoying,
                // but far better than delaying or breaking Bloom's shutdown to avoid it.
            }
        }

        /// <summary>
        /// Records that Bloom's own reporting has already told us about a problem this run, so the Doctor
        /// does not file a second report about the same thing. Called when a Sentry event or a tracker card
        /// goes out successfully.
        /// </summary>
        public static void NoteBloomReportedAProblem(string reportedId)
        {
            try
            {
                if (_session == null)
                    return;
                _session = _session with
                {
                    Exit = new DoctorSessionExit
                    {
                        AtUtc = DateTimeOffset.UtcNow,
                        ShutdownPhase = _shutdownPhase,
                        BloomAlreadyReported = true,
                        ReportedId = reportedId,
                    },
                };
                DoctorSessionStore.TryWrite(_session);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records the facts a Doctor needs but cannot reliably work out from outside — above all the log
        /// file we are actually writing to, and the ports.
        /// </summary>
        private static void WriteSessionFile()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var httpPort = Api.BloomServer.portForHttp;
                _session = new DoctorSession
                {
                    ProcessId = process.Id,
                    StartedAtUtc = process.StartTime.ToUniversalTime(),
                    ExePath = SafeExePath(process),
                    Version = Shell.GetShortVersionInfo(),
                    Channel = ApplicationUpdateSupport.ChannelName,
                    CommandLine = Environment.CommandLine,
                    // The point of the whole file: Bloom recreates Log.txt each run and only falls back to
                    // a random name when another Bloom holds it, so from outside the newest log is the
                    // wrong answer exactly when it matters most.
                    LogPath = Logger.LogPath ?? "",
                    HttpPort = httpPort,
                    // Kept in step with WebView2Browser.RemoteDebuggingPort rather than recomputed
                    // independently, so the two cannot drift apart.
                    CdpPort = httpPort > 0 ? Api.BloomServer.RemoteDebuggingPort : 0,
                    CollectionName = SafeCollectionName(),
                };
                DoctorSessionStore.TryWrite(_session);
                // Note what is NOT done here: pruning old session files. This method runs on the UI thread
                // during startup, and enumerating and deleting a directory's worth of files is exactly the
                // sort of synchronous I/O that has no business on Bloom's startup path. The watchdog thread
                // does it instead, on its first beat.
            }
            catch (Exception e)
            {
                TryLog("Freeze Doctor session file could not be written", e);
            }
        }

        /// <summary>
        /// Rewrites the session file with how this run ended. Called from the same place as the clean-exit
        /// flag, so the on-disk record and the shared-memory record agree.
        /// </summary>
        private static void WriteSessionExit()
        {
            try
            {
                if (_session == null)
                    return;
                // Do not overwrite a record that Bloom already reported a problem: that is more
                // informative than "shut down at phase 4", and the Doctor uses it to stay quiet.
                if (_session.Exit?.BloomAlreadyReported == true)
                    return;
                _session = _session with
                {
                    Exit = new DoctorSessionExit
                    {
                        AtUtc = DateTimeOffset.UtcNow,
                        ShutdownPhase = _shutdownPhase,
                    },
                };
                DoctorSessionStore.TryWrite(_session);
            }
            catch (Exception) { }
        }

        private static bool ProcessIsAlive(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SafeExePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string SafeCollectionName()
        {
            try
            {
                var path = Bloom.Properties.Settings.Default.MruProjects?.Latest;
                return string.IsNullOrEmpty(path)
                    ? ""
                    : System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// The background heartbeat. Deliberately does almost nothing beyond proving the process as a whole
        /// is still scheduling threads — anything more would only add ways for it to stop for reasons that
        /// are not the ones we care about. It does also refresh the session file, which is cheap and keeps
        /// that work off the UI thread.
        /// </summary>
        private static void WatchdogLoop()
        {
            var sinceSessionRefresh = TimeSpan.Zero;
            // The Doctor sets this to ask us to exit under our own power when our UI is gone but we are
            // still running. Waiting on it HERE is the point: this thread is still alive long after the UI
            // thread has stopped, which is exactly the situation in which the request gets made.
            var quitRequest = DoctorSignals.TryCreate(
                DoctorSignals.QuitRequestName(Process.GetCurrentProcess().Id)
            );

            // Tidy up previous runs' session files here rather than at startup: this is file enumeration and
            // deletion, and it belongs on a background thread rather than on Bloom's startup path. An
            // unexplained session survives until it ages out, since that is precisely the evidence a Doctor
            // installed after a crash comes looking for.
            try
            {
                DoctorSessionStore.Prune(ProcessIsAlive, SessionRetention);
            }
            catch (Exception) { }

            while (true)
            {
                try
                {
                    _channel?.RecordWatchdogTick();
                    PublishWhatBloomIsDoing();
                    sinceSessionRefresh += WatchdogInterval;
                    if (sinceSessionRefresh >= SessionRefreshInterval)
                    {
                        sinceSessionRefresh = TimeSpan.Zero;
                        RefreshSessionFile();
                    }

                    // Sleep on the quit request rather than sleeping blindly, so one thread does both jobs
                    // and a request is acted on within a second rather than whenever we next wake.
                    if (quitRequest != null)
                    {
                        if (quitRequest.WaitOne(WatchdogInterval))
                        {
                            ExitAtDoctorsRequest();
                            return;
                        }
                        continue;
                    }
                    Thread.Sleep(WatchdogInterval);
                }
                catch (Exception)
                {
                    // Never let this thread die of an exception; the Doctor reads its silence as the
                    // process being wedged, which would be a false alarm.
                    try
                    {
                        Thread.Sleep(WatchdogInterval);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Publishes what Bloom is doing right now, worked out from the in-flight API requests, along with
        /// the server's worker counts.
        ///
        /// This is where the Doctor's most useful sentence comes from. A stack trace says the UI thread is
        /// waiting; this says *which request* has been running for 47 seconds, which is usually the answer.
        /// Computed on this thread rather than in the request path, so the cost falls on a once-a-second
        /// thread instead of on every request.
        /// </summary>
        private static void PublishWhatBloomIsDoing()
        {
            try
            {
                var activity = ApiActivityTracker.DescribeCurrentActivity();
                if (activity != null)
                    _channel?.SetActivity(activity);

                var server = Api.BloomServer._theOneInstance;
                if (server != null)
                    _channel?.SetServerWorkerCounts(
                        server.BusyWorkerCount,
                        server.BlockedWorkerCount
                    );
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Rewrites the session file with anything that may have changed since startup — the ports, which
        /// are assigned after we first wrote it, and the collection, which changes when the user switches.
        /// </summary>
        private static void RefreshSessionFile()
        {
            try
            {
                if (_session == null)
                    return;
                var httpPort = Api.BloomServer.portForHttp;
                var refreshed = _session with
                {
                    LogPath = Logger.LogPath ?? _session.LogPath,
                    HttpPort = httpPort,
                    CdpPort = httpPort > 0 ? Api.BloomServer.RemoteDebuggingPort : 0,
                    CollectionName = SafeCollectionName(),
                };
                // Only touch the disk when something actually changed: this runs every ten seconds for the
                // life of the process, and a pointless write every ten seconds is a pointless write.
                if (refreshed == _session)
                    return;
                _session = refreshed;
                DoctorSessionStore.TryWrite(_session);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Exits because the Doctor asked us to, having decided our UI is gone and we are in the user's way.
        ///
        /// We exit *ourselves* rather than being killed, which is worth the round trip: `Environment.Exit`
        /// runs the `ProcessExit` handlers, so Bloom's single-instance token is released properly and its own
        /// record of the shutdown is written. Being killed from outside would leave both undone. Note we do
        /// NOT try to shut down gracefully — a Bloom in this state has already failed to do that, which is
        /// why anyone is asking.
        /// </summary>
        private static void ExitAtDoctorsRequest()
        {
            try
            {
                Logger.WriteEvent(
                    "The Bloom Freeze Doctor asked this process to exit (its UI is gone and it is holding "
                        + "the single-instance token). Exiting."
                );
            }
            catch (Exception) { }

            // Mark this as what it is BEFORE exiting. Environment.Exit runs the ProcessExit handler, which
            // writes the clean-exit proof — and this was emphatically not a clean exit. Without this flag a
            // zombie we had to end would be recorded as having shut down properly, which is exactly the
            // conclusion the proof exists to prevent anyone drawing.
            _endedAtDoctorsRequest = true;

            try
            {
                // 1 rather than 0: this was not a normal exit, and the exit code should say so.
                Environment.Exit(1);
            }
            catch (Exception)
            {
                // If even that fails, the Doctor's fallback is to kill us, which is what it is there for.
            }
        }

        /// <summary>
        /// Asks a watching Doctor to dump this process, and waits briefly for it. Call from a crash path, as
        /// late as possible: a dump taken from outside is worth more than one a dying process takes of
        /// itself, but only if the process is still there to dump.
        ///
        /// **The zero-timeout check comes first, and it is the important part.** Nearly every user has no
        /// Doctor installed, and an unconditional pause here would make every crash worse for all of them to
        /// benefit the few. So: if nobody is watching, this returns immediately and costs nothing.
        /// </summary>
        public static void RequestDumpBeforeDying()
        {
            try
            {
                var pid = Process.GetCurrentProcess().Id;
                if (!DoctorSignals.Exists(DoctorSignals.WatchingName(pid)))
                    return; // No Doctor. Do not delay this crash by even a millisecond.

                if (!DoctorSignals.TrySignal(DoctorSignals.DumpRequestName(pid)))
                    return;

                // Short on purpose. The user is already looking at a crash; a few seconds to capture what
                // caused it is a fair trade, but only a few.
                var dumped = DoctorSignals.WaitFor(
                    DoctorSignals.DumpCompleteName(pid),
                    TimeSpan.FromSeconds(3)
                );
                Logger.WriteEvent(
                    dumped
                        ? "The Bloom Freeze Doctor captured a dump of this crash"
                        : "Asked the Bloom Freeze Doctor for a crash dump but it did not answer in time"
                );
            }
            catch (Exception)
            {
                // A crash path is the last place to introduce a new way to fail.
            }
        }

        private static void SafeRecordUiTick()
        {
            try
            {
                _channel?.RecordUiTick();
            }
            catch (Exception) { }
        }

        private static void TryLog(string message, Exception e)
        {
            try
            {
                Logger.WriteError(message, e);
            }
            catch (Exception) { }
        }
    }
}
